package com.example.myapplication.ui

import android.app.Application
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.server.PcServerClient
import com.example.myapplication.data.server.PcSyncForegroundService
import com.example.myapplication.data.server.PcSyncProgress
import com.example.myapplication.data.server.PcSyncResult
import com.example.myapplication.data.server.SyncCoordinator
import com.example.myapplication.data.server.SyncPhase
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

internal class PcSyncController(
    private val application: Application,
    private val scope: CoroutineScope,
    private val syncCoordinator: SyncCoordinator,
    private val repository: FleaMarketRepository,
    private val claudeController: ClaudeWorkflowController
) {
    val pcServerClient: PcServerClient = syncCoordinator.pcServerClient
    private val syncMutex get() = syncCoordinator.syncMutex

    private val _pcServerUrl = MutableStateFlow(pcServerClient.getSavedServerUrl())
    val pcServerUrl: StateFlow<String> = _pcServerUrl.asStateFlow()

    private val _pcAuthToken = MutableStateFlow(pcServerClient.getSavedAuthToken())
    val pcAuthToken: StateFlow<String> = _pcAuthToken.asStateFlow()

    val isPcServerConnected: StateFlow<Boolean> = syncCoordinator.isPcServerConnected
    val syncStatusState: StateFlow<PcSyncProgress> = syncCoordinator.syncStatusState
    val lastSyncSuccessAt: StateFlow<Long?> = syncCoordinator.lastSyncSuccessAt

    private var heartbeatJob: Job? = null
    private var autoSyncJob: Job? = null

    @Volatile
    var lastHeartbeatResponseAtMs: Long = 0L
        private set

    private var lastKnownDataRevision: Long
        get() = syncCoordinator.lastKnownDataRevision
        set(value) { syncCoordinator.lastKnownDataRevision = value }

    private var lastKnownSettingsRevision: Long
        get() = syncCoordinator.lastKnownSettingsRevision
        set(value) { syncCoordinator.lastKnownSettingsRevision = value }

    suspend fun refreshKnownRevisions(url: String, token: String) {
        val hb = pcServerClient.sendHeartbeatWithRevision(url, token)
        if (hb.isConnected) {
            lastKnownDataRevision = hb.dataRevision
            lastKnownSettingsRevision = hb.settingsRevision
            if (hb.labelSeenRevision > 0L && hb.labelSeenRevision != claudeController.lastKnownLabelSeenRevision) {
                claudeController.refreshClaudeJobStates(url, token)
            }
        }
    }

    fun startHeartbeatLoop() {
        heartbeatJob?.cancel()
        heartbeatJob = scope.launch(Dispatchers.IO) {
            var wasConnected = false
            while (isActive) {
                val url = _pcServerUrl.value
                val token = _pcAuthToken.value
                if (url.isNotBlank()) {
                    val hb = pcServerClient.sendHeartbeatWithRevision(url, token)
                    if (hb.isConnected) {
                        lastHeartbeatResponseAtMs = System.currentTimeMillis()
                    }
                    val connected = hb.isConnected
                    syncCoordinator.setPcServerConnected(connected)

                    if (!connected) {
                        wasConnected = false
                    } else if (!wasConnected) {
                        wasConnected = true
                        val hasLocalDirty = repository.getDao().getDirtyItemsCount() > 0 ||
                            repository.getPendingDeletedUuids().isNotEmpty()
                        val hasUnsyncedPhotos = repository.getUnsyncedPhotosCountAfterGhostCleanup() > 0
                        if (hasLocalDirty || hasUnsyncedPhotos) {
                            val syncRes = PcSyncForegroundService.runSyncViaService(
                                context = application,
                                url = url,
                                token = token,
                                initialMessage = "再接続・差分送信中..."
                            )
                            if (syncRes.isSuccess) {
                                refreshKnownRevisions(url, token)
                            }
                        } else {
                            syncMutex.withLock {
                                try {
                                    pcServerClient.fetchAndSyncServerSettings(url, token)
                                    syncCoordinator.updateSyncProgress(
                                        PcSyncProgress(
                                            phase = SyncPhase.FETCHING_ITEMS,
                                            progressPercent = 92,
                                            detailMessage = "PC側最新データを取得中..."
                                        )
                                    )
                                    val fetched = pcServerClient.fetchServerItems(url, token)
                                    refreshKnownRevisions(url, token)
                                    if (fetched) {
                                        syncCoordinator.markSyncSuccessNow("再接続同期完了")
                                    } else {
                                        syncCoordinator.updateSyncProgress(
                                            PcSyncProgress(
                                                phase = SyncPhase.ERROR,
                                                detailMessage = "PCデータ取得に失敗しました",
                                                errorDetail = "GET /api/items の取得に失敗しました"
                                            )
                                        )
                                    }
                                } catch (e: Exception) {
                                    if (e is CancellationException) throw e
                                    syncCoordinator.updateSyncProgress(
                                        PcSyncProgress(
                                            phase = SyncPhase.ERROR,
                                            detailMessage = "再接続同期エラー",
                                            errorDetail = e.localizedMessage ?: e.message ?: "再接続時の同期に失敗しました"
                                        )
                                    )
                                }
                            }
                        }
                    } else {
                        val settingsChanged = hb.settingsRevision > 0L && hb.settingsRevision != lastKnownSettingsRevision
                        val dataChanged = hb.dataRevision > 0L && hb.dataRevision != lastKnownDataRevision
                        if (settingsChanged || dataChanged) {
                            if (syncMutex.tryLock()) {
                                try {
                                    syncCoordinator.updateSyncProgress(
                                        PcSyncProgress(
                                            phase = SyncPhase.FETCHING_ITEMS,
                                            progressPercent = 80,
                                            detailMessage = "PC側の更新を反映中..."
                                        )
                                    )
                                    if (settingsChanged) {
                                        pcServerClient.fetchAndSyncServerSettings(url, token)
                                        lastKnownSettingsRevision = hb.settingsRevision
                                    }
                                    var fetchOk = true
                                    if (dataChanged) {
                                        fetchOk = pcServerClient.fetchServerItems(url, token)
                                        if (fetchOk) lastKnownDataRevision = hb.dataRevision
                                    }
                                    if (fetchOk) {
                                        syncCoordinator.markSyncSuccessNow("PC更新を反映しました")
                                    } else {
                                        syncCoordinator.updateSyncProgress(
                                            PcSyncProgress(
                                                phase = SyncPhase.ERROR,
                                                detailMessage = "PC更新の取得に失敗",
                                                errorDetail = "PCサーバーからの差分取得に失敗しました"
                                            )
                                        )
                                    }
                                } catch (e: Exception) {
                                    if (e is CancellationException) throw e
                                    syncCoordinator.updateSyncProgress(
                                        PcSyncProgress(
                                            phase = SyncPhase.ERROR,
                                            detailMessage = "PC更新反映エラー",
                                            errorDetail = e.localizedMessage ?: e.message ?: "差分取得エラー"
                                        )
                                    )
                                } finally {
                                    syncMutex.unlock()
                                }
                            }
                        }
                    }
                    if (connected && syncCoordinator.consumePendingAutoSyncRetry()) {
                        PcSyncForegroundService.runAutoSyncViaService(
                            context = application,
                            url = url,
                            token = token,
                            initialMessage = "自動同期を再開中..."
                        )
                    }
                    if (connected && !claudeController.claudeActionInProgress.value &&
                        ((hb.claudeRevision > 0L && hb.claudeRevision != claudeController.lastKnownClaudeRevision) ||
                            (hb.labelSeenRevision > 0L && hb.labelSeenRevision != claudeController.lastKnownLabelSeenRevision))
                    ) {
                        claudeController.refreshClaudeJobStates(url, token, fromWatch = true)
                    }
                } else {
                    syncCoordinator.setPcServerConnected(false)
                    wasConnected = false
                }
                delay(4000)
            }
        }
    }

    fun triggerAutoSync() {
        val url = _pcServerUrl.value
        val token = _pcAuthToken.value
        if (url.isBlank() || !isPcServerConnected.value) return

        autoSyncJob?.cancel()
        autoSyncJob = scope.launch(Dispatchers.IO) {
            delay(500)
            val hasUnsyncedPhotos = repository.getUnsyncedPhotosCountAfterGhostCleanup() > 0
            val result = if (hasUnsyncedPhotos) {
                PcSyncForegroundService.runAutoSyncViaService(
                    context = application,
                    url = url,
                    token = token,
                    initialMessage = "自動同期を開始..."
                )
            } else {
                syncCoordinator.executeFullSync(
                    url = url,
                    token = token,
                    initialMessage = "自動同期を開始...",
                    updateConnectedOnResult = false
                )
            }
            if (result.isSuccess) {
                refreshKnownRevisions(url, token)
            }
        }
    }

    fun updatePcServerUrl(url: String) {
        val changed = _pcServerUrl.value != url
        _pcServerUrl.value = url
        pcServerClient.saveServerUrl(url)
        if (changed) {
            lastKnownDataRevision = -1L
            lastKnownSettingsRevision = -1L
            claudeController.resetForServerChange()
        }
    }

    fun updatePcAuthToken(token: String) {
        _pcAuthToken.value = token
        pcServerClient.saveAuthToken(token)
    }

    fun parseQrCodeText(qrText: String): Boolean {
        val text = qrText.trim()
        if (!text.startsWith("furima:")) return false

        val content = text.removePrefix("furima:")
        val parts = content.split("|")
        if (parts.size >= 2) {
            val url = parts[0].trim()
            val token = parts[1].trim()
            val storeId = if (parts.size >= 3) parts[2].trim() else ""

            if (url.isNotBlank()) {
                updatePcServerUrl(url)
                updatePcAuthToken(token)
                if (storeId.isNotBlank()) {
                    pcServerClient.saveStoreId(storeId)
                }
                return true
            }
        }
        return false
    }

    fun testPcServerConnection(onResult: (Boolean) -> Unit) {
        scope.launch {
            val url = _pcServerUrl.value
            val token = _pcAuthToken.value
            val connected = pcServerClient.checkServerStatus(url, token)
            syncCoordinator.setPcServerConnected(connected)
            if (connected) {
                refreshKnownRevisions(url, token)
            }
            onResult(connected)
        }
    }

    fun syncDeltaToPcServer(onResult: (PcSyncResult) -> Unit) {
        scope.launch {
            val url = _pcServerUrl.value
            val token = _pcAuthToken.value
            val result = withContext(Dispatchers.IO) {
                PcSyncForegroundService.runSyncViaService(
                    context = application,
                    url = url,
                    token = token,
                    initialMessage = "同期を開始中..."
                )
            }
            if (result.isSuccess) {
                withContext(Dispatchers.IO) {
                    refreshKnownRevisions(url, token)
                }
            }
            onResult(result)
        }
    }

    fun onCleared() {
        heartbeatJob?.cancel()
        autoSyncJob?.cancel()
    }
}
