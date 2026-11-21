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

    override fun onCreate() {
        super.onCreate()
        syncCoordinator = SyncCoordinator.getInstance(applicationContext)
        notificationManager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        ensureNotificationChannel()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        lastStartId = startId
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
            if (activeTaskCount.get() == 0) {
                stopForegroundAndSelf(lastStartId)
            }
            return START_NOT_STICKY
        }

        acquireWakeLockIfNeeded()
        startProgressNotificationCollectorIfNeeded()

        activeTaskCount.incrementAndGet()
        val taskKey = requestId.ifBlank { UUID.randomUUID().toString() }
        val job = serviceScope.launch {
            val result = try {
                syncCoordinator.executeFullSync(
                    url = url,
                    token = token,
                    initialMessage = initialMessage,
                    updateConnectedOnResult = true
                )
            } catch (e: Exception) {
                PcSyncResult(
                    isSuccess = false,
                    errorMessage = e.localizedMessage ?: e.message ?: "同期中にエラーが発生しました"
                )
            } finally {
                activeJobs.remove(taskKey)
            }

            if (requestId.isNotBlank()) {
                pendingRequests.remove(requestId)?.complete(result)
            }

            if (activeTaskCount.decrementAndGet() <= 0) {
                stopForegroundAndSelf(lastStartId)
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
        stopForegroundAndSelf(lastStartId)
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

    private fun stopForegroundAndSelf(targetStartId: Int) {
        progressCollectorJob?.cancel()
        progressCollectorJob = null
        releaseWakeLock()
        try {
            ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        } catch (_: Exception) {
        }
        if (targetStartId > 0) {
            stopSelf(targetStartId)
        } else {
            stopSelf()
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

            return deferred.await()
        }
    }
}
