package com.example.myapplication.data.server

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import com.example.myapplication.MainActivity
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger

class PcSyncForegroundService : Service() {

    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private lateinit var syncCoordinator: SyncCoordinator
    private lateinit var notificationManager: NotificationManager
    private var wakeLock: PowerManager.WakeLock? = null
    private var progressCollectorJob: Job? = null
    private val activeTaskCount = AtomicInteger(0)
    private val activeJobs = ConcurrentHashMap<String, Job>()
    @Volatile private var lastStartId: Int = 0

    // レビュー §83-1: 開始登録・完了・停止判定を単一ロックで直列化し、
    // 先行タスクの終了処理が後続タスクの通知・WakeLock・サービス自体を
    // 奪わないようにする。teardownDone は新しい開始登録でリセットされる。
    private val stateLock = Any()
    private var teardownDone = false

    override fun onCreate() {
        super.onCreate()
        syncCoordinator = SyncCoordinator.getInstance(applicationContext)
        notificationManager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        ensureNotificationChannel()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // §83-1: 開始登録 (startId・件数・停止済みフラグ) をエントリで原子的に行う。
        // 先行タスクの終了処理は、この登録より前には新しい開始を見逃さず、
        // 登録より後では件数が残っているため停止に進まない。
        synchronized(stateLock) {
            lastStartId = startId
            activeTaskCount.incrementAndGet()
            teardownDone = false
        }
        ensureNotificationChannel()
        val initialProgress = syncCoordinator.syncStatusState.value
        val initialNotification = buildSyncNotification(initialProgress)
        val serviceType = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
        } else {
            0
        }
        ServiceCompat.startForeground(this, NOTIFICATION_ID, initialNotification, serviceType)

        val requestId = intent?.getStringExtra(EXTRA_REQUEST_ID) ?: ""
        val url = intent?.getStringExtra(EXTRA_URL) ?: ""
        val token = intent?.getStringExtra(EXTRA_TOKEN) ?: ""
        val initialMessage = intent?.getStringExtra(EXTRA_INITIAL_MESSAGE) ?: "PC差分同期を実行中..."

        if (url.isBlank()) {
            if (requestId.isNotBlank()) {
                pendingRequests.remove(requestId)?.complete(
                    PcSyncResult(isSuccess = false, errorMessage = "PCサーバーURLが設定されていません")
                )
            }
            // この開始コマンド分の登録を即座に完了扱いにする
            // (他に実行中タスクが無ければ finishTask が停止まで進める)
            finishTask()
            return START_NOT_STICKY
        }

        acquireWakeLockIfNeeded()
        startProgressNotificationCollectorIfNeeded()

        val taskKey = requestId.ifBlank { UUID.randomUUID().toString() }
        val job = serviceScope.launch {
            try {
                val result = try {
                    syncCoordinator.executeFullSync(
                        url = url,
                        token = token,
                        initialMessage = initialMessage,
                        updateConnectedOnResult = true
                    )
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    PcSyncResult(
                        isSuccess = false,
                        errorMessage = e.localizedMessage ?: e.message ?: "同期中にエラーが発生しました"
                    )
                }

                if (requestId.isNotBlank()) {
                    pendingRequests.remove(requestId)?.complete(result)
                }
            } catch (e: CancellationException) {
                if (requestId.isNotBlank()) {
                    pendingRequests.remove(requestId)?.complete(
                        PcSyncResult(isSuccess = false, errorMessage = "同期がキャンセルされました")
                    )
                }
                throw e
            } finally {
                activeJobs.remove(taskKey)
                finishTask()
            }
        }
        activeJobs[taskKey] = job
        return START_NOT_STICKY
    }

    override fun onTimeout(startId: Int, fgsType: Int) {
        Log.w("PC_SYNC_FGS", "Foreground service timeout reached (startId=$startId, type=$fgsType)")
        activeJobs.values.forEach { it.cancel() }
        activeJobs.clear()
        completeAllPendingWithError("同期タイムアウトのためフォアグラウンドサービスを停止しました")
        // §83-1: 停止は forceTeardown に一本化する (キャンセルされたタスク側の
        // finishTask と二重に走っても teardownDone で抑止される)
        forceTeardown()
    }

    override fun onDestroy() {
        completeAllPendingWithError("同期サービスが中断されました")
        progressCollectorJob?.cancel()
        releaseWakeLock()
        serviceScope.cancel()
        super.onDestroy()
    }

    private fun completeAllPendingWithError(message: String) {
        val iterator = pendingRequests.entries.iterator()
        while (iterator.hasNext()) {
            val entry = iterator.next()
            iterator.remove()
            entry.value.complete(PcSyncResult(isSuccess = false, errorMessage = message))
        }
    }

    private fun acquireWakeLockIfNeeded() {
        try {
            if (wakeLock == null) {
                val pm = getSystemService(Context.POWER_SERVICE) as? PowerManager
                wakeLock = pm?.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "FurimaApp::PcSyncWakeLock")?.apply {
                    setReferenceCounted(false)
                }
            }
            if (wakeLock?.isHeld == false) {
                wakeLock?.acquire(15 * 60 * 1000L)
            }
        } catch (e: Exception) {
            Log.w("PC_SYNC_FGS", "WakeLock acquire failed: ${e.message}")
        }
    }

    private fun releaseWakeLock() {
        try {
            if (wakeLock?.isHeld == true) {
                wakeLock?.release()
            }
        } catch (_: Exception) {
        }
    }

    private fun startProgressNotificationCollectorIfNeeded() {
        if (progressCollectorJob?.isActive == true) return
        progressCollectorJob = serviceScope.launch {
            var lastNotifyAt = 0L
            var lastPhase: SyncPhase? = null
            var lastPhotoIndex = -1
            syncCoordinator.syncStatusState.collect { progress ->
                val now = System.currentTimeMillis()
                val significantChange = progress.phase != lastPhase ||
                    progress.currentPhotoIndex != lastPhotoIndex ||
                    (now - lastNotifyAt) >= 400L
                if (significantChange) {
                    lastNotifyAt = now
                    lastPhase = progress.phase
                    lastPhotoIndex = progress.currentPhotoIndex
                    try {
                        notificationManager.notify(NOTIFICATION_ID, buildSyncNotification(progress))
                    } catch (_: Exception) {
                    }
                }
            }
        }
    }

    /**
     * §83-1: 1 件のタスク完了を記録する。全タスクが終わった場合だけ、
     * ロック内で再検証してから停止処理を行う。
     */
    private fun finishTask() {
        synchronized(stateLock) {
            if (teardownDone) return
            if (activeTaskCount.decrementAndGet() > 0) return
            teardownDone = true
            teardownLocked()
        }
    }

    /** §83-1: タイムアウト・空リクエスト時の強制停止。タスク側の finishTask とは teardownDone で排他する。 */
    private fun forceTeardown() {
        synchronized(stateLock) {
            if (teardownDone) return
            teardownDone = true
            activeTaskCount.set(0)
            teardownLocked()
        }
    }

    /** stateLock を保持した状態でのみ呼ぶこと。 */
    private fun teardownLocked() {
        // 先に進捗コレクタを止め、最後に通知を明示キャンセルして、
        // notify() と STOP_FOREGROUND_REMOVE の競合による通知残留を防ぐ
        progressCollectorJob?.cancel()
        progressCollectorJob = null
        releaseWakeLock()
        try {
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        } catch (_: Exception) {
        }
        try {
            notificationManager.cancel(NOTIFICATION_ID)
        } catch (_: Exception) {
        }
        // より新しい開始コマンドが来ていれば stopSelfResult が false を返し、サービスは存続する
        // (その開始側が startForeground・WakeLock・コレクタを取り直す)
        if (!stopSelfResult(lastStartId)) {
            Log.i("PC_SYNC_FGS", "Newer start command exists; keeping service alive")
        }
    }

    private fun ensureNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val existing = notificationManager.getNotificationChannel(CHANNEL_ID)
            if (existing == null) {
                val channel = NotificationChannel(
                    CHANNEL_ID,
                    "PC差分同期",
                    NotificationManager.IMPORTANCE_LOW
                ).apply {
                    description = "画面オフ中も写真・商品データのPC同期を継続するための通知です"
                    setShowBadge(false)
                }
                notificationManager.createNotificationChannel(channel)
            }
        }
    }

    private fun buildSyncNotification(progress: PcSyncProgress): Notification {
        val launchIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val contentIntent = PendingIntent.getActivity(
            this,
            0,
            launchIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val title = when (progress.phase) {
            SyncPhase.UPLOADING_PHOTOS -> "PCへ写真を送信中"
            SyncPhase.FETCHING_ITEMS -> "PC最新データを受信中"
            SyncPhase.SUCCESS -> "PC同期完了"
            SyncPhase.ERROR -> "PC同期エラー"
            else -> "PCと差分同期中"
        }
        val text = progress.detailMessage.ifBlank { "同期を実行しています..." }
        val pct = progress.progressPercent.coerceIn(0, 100)
        val indeterminate = progress.phase == SyncPhase.SYNCING_ITEMS || progress.phase == SyncPhase.IDLE

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(title)
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentIntent(contentIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setSilent(true)
            .setProgress(100, pct, indeterminate)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .build()
    }

    companion object {
        private const val CHANNEL_ID = "pc_sync_channel"
        private const val NOTIFICATION_ID = 4101
        private const val EXTRA_REQUEST_ID = "extra_request_id"
        private const val EXTRA_URL = "extra_url"
        private const val EXTRA_TOKEN = "extra_token"
        private const val EXTRA_INITIAL_MESSAGE = "extra_initial_message"

        private val pendingRequests = ConcurrentHashMap<String, CompletableDeferred<PcSyncResult>>()

        suspend fun runSyncViaService(
            context: Context,
            url: String,
            token: String,
            initialMessage: String = "同期を開始中..."
        ): PcSyncResult {
            val appContext = context.applicationContext
            val requestId = UUID.randomUUID().toString()
            val deferred = CompletableDeferred<PcSyncResult>()
            pendingRequests[requestId] = deferred

            val intent = Intent(appContext, PcSyncForegroundService::class.java).apply {
                putExtra(EXTRA_REQUEST_ID, requestId)
                putExtra(EXTRA_URL, url)
                putExtra(EXTRA_TOKEN, token)
                putExtra(EXTRA_INITIAL_MESSAGE, initialMessage)
            }

            try {
                ContextCompat.startForegroundService(appContext, intent)
            } catch (e: Exception) {
                Log.w("PC_SYNC_FGS", "startForegroundService failed, falling back to direct sync: ${e.message}")
                pendingRequests.remove(requestId)
                return SyncCoordinator.getInstance(appContext).executeFullSync(
                    url = url,
                    token = token,
                    initialMessage = initialMessage,
                    updateConnectedOnResult = true
                )
            }

            // §83-1: 呼び出し側がキャンセルされても pendingRequests にエントリを残さない
            try {
                return deferred.await()
            } finally {
                pendingRequests.remove(requestId)
            }
        }

        // §83-6: 自動同期専用の固定スロット。実行中の自動同期がある間、
        // 後続トリガーは新しいサービス開始を積み上げず既存の実行へ相乗りし、
        // 実行中に来たトリガーは完了後に 1 回だけ追走実行する (コアレス)。
        private const val AUTO_SYNC_REQUEST_ID = "auto-sync-slot"
        private val autoSyncStateLock = Any()
        private var autoSyncTriggerGeneration = 0L
        private var autoSyncSlotGeneration = 0L

        private fun isForegroundServiceStartNotAllowed(e: Exception): Boolean {
            // android.app.ForegroundServiceStartNotAllowedException (API 31+)。
            // 旧 API でのクラス解決を避けるため単純名で判定する。
            return e.javaClass.simpleName == "ForegroundServiceStartNotAllowedException"
        }

        suspend fun runAutoSyncViaService(
            context: Context,
            url: String,
            token: String,
            initialMessage: String = "自動同期を開始..."
        ): PcSyncResult {
            val appContext = context.applicationContext
            synchronized(autoSyncStateLock) { autoSyncTriggerGeneration++ }

            while (true) {
                val slot = synchronized(autoSyncStateLock) {
                    val existing = pendingRequests[AUTO_SYNC_REQUEST_ID]
                    if (existing != null && !existing.isCompleted) {
                        Triple(existing, autoSyncSlotGeneration, false)
                    } else {
                        val created = CompletableDeferred<PcSyncResult>()
                        pendingRequests[AUTO_SYNC_REQUEST_ID] = created
                        autoSyncSlotGeneration = autoSyncTriggerGeneration
                        Triple(created, autoSyncSlotGeneration, true)
                    }
                }
                val deferred = slot.first
                val slotGeneration = slot.second
                val shouldStart = slot.third

                if (shouldStart) {
                    val intent = Intent(appContext, PcSyncForegroundService::class.java).apply {
                        putExtra(EXTRA_REQUEST_ID, AUTO_SYNC_REQUEST_ID)
                        putExtra(EXTRA_URL, url)
                        putExtra(EXTRA_TOKEN, token)
                        putExtra(EXTRA_INITIAL_MESSAGE, initialMessage)
                    }
                    try {
                        ContextCompat.startForegroundService(appContext, intent)
                    } catch (e: Exception) {
                        if (isForegroundServiceStartNotAllowed(e)) {
                            // §83-6: バックグラウンド制限時は WakeLock なしの直接同期へ
                            // 落とさず保留し、ハートビート側の再試行に委ねる。
                            // 相乗り中の呼び出し側を待たせ続けないよう、保留結果で完了させる。
                            Log.w("PC_SYNC_FGS", "Auto sync deferred: FGS start not allowed in background")
                            SyncCoordinator.getInstance(appContext).requestPendingAutoSyncRetry()
                            val deferredResult = PcSyncResult(
                                isSuccess = false,
                                errorMessage = "バックグラウンド状態のため自動同期を保留しました"
                            )
                            deferred.complete(deferredResult)
                            pendingRequests.remove(AUTO_SYNC_REQUEST_ID, deferred)
                            return deferredResult
                        }
                        pendingRequests.remove(AUTO_SYNC_REQUEST_ID, deferred)
                        Log.w("PC_SYNC_FGS", "Auto sync startForegroundService failed, falling back to direct sync: ${e.message}")
                        val direct = SyncCoordinator.getInstance(appContext).executeFullSync(
                            url = url,
                            token = token,
                            initialMessage = initialMessage,
                            updateConnectedOnResult = true
                        )
                        deferred.complete(direct)
                        return direct
                    }
                }

                // スロットの所有権はサービス側 (完了時に除去される)。
                // 呼び出し側がキャンセルされてもスロットは残し、サービス側の実行を継続させる。
                val result = deferred.await()
                val settled = synchronized(autoSyncStateLock) {
                    autoSyncTriggerGeneration <= slotGeneration
                }
                if (settled) return result
                // 実行中に新しいトリガーが来ていた場合は追走する
            }
        }
    }
}
