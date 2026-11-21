package com.example.myapplication.data.server

import android.content.Context
import com.example.myapplication.data.FleaMarketRepository
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class SyncCoordinator private constructor(context: Context) {
    private val appContext = context.applicationContext
    val repository = FleaMarketRepository(appContext)
    val pcServerClient = PcServerClient(appContext, repository)

    val syncMutex = Mutex()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var syncSuccessResetJob: Job? = null

    private val _isPcServerConnected = MutableStateFlow(false)
    val isPcServerConnected: StateFlow<Boolean> = _isPcServerConnected.asStateFlow()

    private val _syncStatusState = MutableStateFlow(PcSyncProgress())
    val syncStatusState: StateFlow<PcSyncProgress> = _syncStatusState.asStateFlow()

    private val _lastSyncSuccessAt = MutableStateFlow<Long?>(null)
    val lastSyncSuccessAt: StateFlow<Long?> = _lastSyncSuccessAt.asStateFlow()

    @Volatile var lastKnownDataRevision: Long = -1L
    @Volatile var lastKnownSettingsRevision: Long = -1L

    fun setPcServerConnected(connected: Boolean) {
        _isPcServerConnected.value = connected
    }

    fun updateSyncProgress(progress: PcSyncProgress) {
        _syncStatusState.value = progress
    }

    fun markSyncSuccessNow(detailMessage: String) {
        val now = System.currentTimeMillis()
        _lastSyncSuccessAt.value = now
        _syncStatusState.value = PcSyncProgress(
            phase = SyncPhase.SUCCESS,
            progressPercent = 100,
            detailMessage = detailMessage,
            updatedAt = now
        )
        scheduleSyncSuccessResetToIdle(now)
    }

    fun scheduleSyncSuccessResetToIdle(successTimestamp: Long) {
        syncSuccessResetJob?.cancel()
        syncSuccessResetJob = scope.launch {
            delay(3500)
            val cur = _syncStatusState.value
            if (cur.phase == SyncPhase.SUCCESS && cur.updatedAt == successTimestamp) {
                _syncStatusState.value = PcSyncProgress(
                    phase = SyncPhase.IDLE,
                    detailMessage = "同期済",
                    updatedAt = System.currentTimeMillis()
                )
            }
        }
    }

    suspend fun executeFullSync(
        url: String,
        token: String,
        initialMessage: String = "同期を開始中...",
        updateConnectedOnResult: Boolean = true,
        onHeartbeatRefreshed: (suspend (HeartbeatInfo) -> Unit)? = null
    ): PcSyncResult {
        val result = syncMutex.withLock {
            try {
                _syncStatusState.value = PcSyncProgress(
                    phase = SyncPhase.SYNCING_ITEMS,
                    progressPercent = 5,
                    detailMessage = initialMessage
                )
                pcServerClient.fetchAndSyncServerSettings(url, token)
                val pushRes = pcServerClient.syncDeltaToPcServer(url, token) { progress ->
                    _syncStatusState.value = progress
                }
                if (pushRes.isSuccess) {
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.FETCHING_ITEMS,
                        progressPercent = 92,
                        detailMessage = "PC側最新データを取得中..."
                    )
                    val fetched = pcServerClient.fetchServerItems(url, token)
                    val hb = pcServerClient.sendHeartbeatWithRevision(url, token)
                    if (hb.isConnected) {
                        lastKnownDataRevision = hb.dataRevision
                        lastKnownSettingsRevision = hb.settingsRevision
                        onHeartbeatRefreshed?.invoke(hb)
                    }
                    if (fetched) {
                        markSyncSuccessNow("同期完了 (${pushRes.syncedItemsCount}件)")
                        pushRes
                    } else {
                        val errMsg = "GET /api/items の取得に失敗しました"
                        _syncStatusState.value = PcSyncProgress(
                            phase = SyncPhase.ERROR,
                            detailMessage = "PCデータ取得に失敗しました",
                            errorDetail = errMsg
                        )
                        pushRes.copy(isSuccess = false, errorMessage = errMsg)
                    }
                } else {
                    _syncStatusState.value = PcSyncProgress(
                        phase = SyncPhase.ERROR,
                        detailMessage = "同期に失敗しました",
                        errorDetail = pushRes.errorMessage ?: "PCサーバーとの同期に失敗しました"
                    )
                    pushRes
                }
            } catch (e: Exception) {
                val errMsg = e.localizedMessage ?: e.message ?: "同期中に通信エラーが発生しました"
                _syncStatusState.value = PcSyncProgress(
                    phase = SyncPhase.ERROR,
                    detailMessage = "同期エラー",
                    errorDetail = errMsg
                )
                PcSyncResult(
                    isSuccess = false,
                    errorMessage = errMsg
                )
            }
        }
        if (updateConnectedOnResult) {
            _isPcServerConnected.value = result.isSuccess
        }
        return result
    }

    companion object {
        @Volatile
        private var INSTANCE: SyncCoordinator? = null

        fun getInstance(context: Context): SyncCoordinator {
            return INSTANCE ?: synchronized(this) {
                INSTANCE ?: SyncCoordinator(context).also { INSTANCE = it }
            }
        }
    }
}
