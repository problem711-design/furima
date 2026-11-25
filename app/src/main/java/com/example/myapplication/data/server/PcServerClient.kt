package com.example.myapplication.data.server

import android.content.Context
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.PhotoStorageManager
import java.io.File

enum class SyncPhase {
    IDLE,
    SYNCING_ITEMS,
    UPLOADING_PHOTOS,
    FETCHING_ITEMS,
    SUCCESS,
    ERROR
}

data class PcSyncProgress(
    val phase: SyncPhase = SyncPhase.IDLE,
    val currentPhotoIndex: Int = 0,
    val totalPhotoCount: Int = 0,
    val bytesSent: Long = 0L,
    val totalBytes: Long = 0L,
    val progressPercent: Int = 0,
    val detailMessage: String = "",
    val errorDetail: String? = null,
    val updatedAt: Long = System.currentTimeMillis()
)

data class PcSyncResult(
    val isSuccess: Boolean,
    val errorMessage: String? = null,
    val syncedItemsCount: Int = 0,
    val syncedPhotosCount: Int = 0,
    val freedMb: Int = 0,
    val missingOriginalPhotosCount: Int = 0,
    val storeId: String? = null
)

class PcServerClient(
    private val context: Context,
    private val repository: FleaMarketRepository
) {
    data class ClaudeJobCreateResult(
        val success: Boolean,
        val jobId: String = "",
        val message: String = "",
        val templateRegistered: Boolean = false,
        val templateRegisterError: String? = null
    )

    private val photoStorageManager = PhotoStorageManager(context)
    private val transport = PcServerTransport(context, repository)
    private val claudeApi = PcClaudeApiClient(transport)
    private val deltaSyncUploader = PcDeltaSyncUploader(transport, repository, photoStorageManager)
    private val serverItemMergeEngine = ServerItemMergeEngine(context, transport, repository, photoStorageManager)

    suspend fun pushSettingsToServer(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = transport.pushSettingsToServer(baseUrl, token)

    fun getClaudeFreeTextHistory(): List<String> = transport.getClaudeFreeTextHistory()
    fun pushClaudeFreeTextHistory(text: String) = transport.pushClaudeFreeTextHistory(text)

    suspend fun createClaudeJob(
        itemId: Int,
        itemUuid: String,
        photos: List<Pair<String, String>>,
        templateId: String,
        queue: String = "patrol",
        promptText: String? = null,
        customPromptName: String? = null,
        registerTemplateName: String? = null,
        referenceMd5: String = "",
        stampNumber: Boolean = true,
        referenceFileName: String = "",
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeJobCreateResult = claudeApi.createClaudeJob(
        itemId = itemId,
        itemUuid = itemUuid,
        photos = photos,
        templateId = templateId,
        queue = queue,
        promptText = promptText,
        customPromptName = customPromptName,
        registerTemplateName = registerTemplateName,
        referenceMd5 = referenceMd5,
        stampNumber = stampNumber,
        referenceFileName = referenceFileName,
        baseUrl = baseUrl,
        token = token
    )

    suspend fun fetchClaudeJobs(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeJobListResponseDto? = claudeApi.fetchClaudeJobs(baseUrl, token)

    suspend fun fetchClaudeReferences(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeReferencesResponseDto? = claudeApi.fetchClaudeReferences(baseUrl, token)

    fun claudeReferenceImageUrl(md5: String, thumb: Boolean): Pair<String, String>? =
        claudeApi.claudeReferenceImageUrl(md5, thumb)

    suspend fun fetchClaudeRevision(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Long? = claudeApi.fetchClaudeRevision(baseUrl, token)

    suspend fun postClaudeApproval(
        jobId: String,
        approved: Boolean,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = claudeApi.postClaudeApproval(jobId, approved, baseUrl, token)

    suspend fun postClaudeJobAck(
        jobId: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = claudeApi.postClaudeJobAck(jobId, baseUrl, token)

    suspend fun postClaudeJobCancel(
        jobId: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = claudeApi.postClaudeJobCancel(jobId, baseUrl, token)

    suspend fun postClaudeLabelSeen(
        itemUuids: List<String>,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeLabelSeenResponseDto? = claudeApi.postClaudeLabelSeen(itemUuids, baseUrl, token)

    suspend fun postClaudeJobMove(
        jobId: String,
        queue: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = claudeApi.postClaudeJobMove(jobId, queue, baseUrl, token)

    suspend fun fetchAndSyncServerSettings(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = transport.fetchAndSyncServerSettings(baseUrl, token)

    suspend fun fetchServerSettingsAfterTemplateRegistration(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = transport.fetchServerSettingsAfterTemplateRegistration(baseUrl, token)

    fun getSavedStoreId(): String = transport.getSavedStoreId()
    fun saveStoreId(storeId: String) = transport.saveStoreId(storeId)
    fun getSavedAuthToken(): String = transport.getSavedAuthToken()
    fun saveAuthToken(token: String) = transport.saveAuthToken(token)
    fun getSavedServerUrl(): String = transport.getSavedServerUrl()
    fun saveServerUrl(url: String) = transport.saveServerUrl(url)

    suspend fun checkServerStatus(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Boolean = transport.checkServerStatus(baseUrl, token)

    suspend fun sendHeartbeatWithRevision(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): HeartbeatInfo = transport.sendHeartbeatWithRevision(baseUrl, token)

    suspend fun sendHeartbeat(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Boolean = transport.sendHeartbeat(baseUrl, token)

    suspend fun fetchPermanentlyDeletedUuids(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Set<String> = transport.fetchPermanentlyDeletedUuids(baseUrl, token)

    suspend fun fetchServerHashes(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Set<String> = transport.fetchServerHashes(baseUrl, token)

    suspend fun syncDeltaToPcServer(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken(),
        onProgress: (PcSyncProgress) -> Unit = {}
    ): PcSyncResult = deltaSyncUploader.syncDeltaToPcServer(baseUrl, token, onProgress)

    internal suspend fun mergeServerItemsFromJson(
        jsonArray: org.json.JSONArray,
        permanentlyDeletedUuids: Set<String> = emptySet(),
        baseUrl: String = "",
        token: String = "",
        repairThumbnails: Boolean = true
    ): Boolean = serverItemMergeEngine.mergeServerItemsFromJson(
        jsonArray, permanentlyDeletedUuids, baseUrl, token, repairThumbnails
    )

    suspend fun fetchServerItems(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Boolean = serverItemMergeEngine.fetchServerItems(baseUrl, token)

    suspend fun downloadPhotoToFileByHash(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken(),
        md5Hash: String,
        targetFile: File,
        verifyMd5: Boolean = true
    ): Boolean = serverItemMergeEngine.downloadPhotoToFileByHash(baseUrl, token, md5Hash, targetFile, verifyMd5)

    suspend fun downloadPhotoByHash(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken(),
        md5Hash: String,
        verifyMd5: Boolean = true
    ): ByteArray? = serverItemMergeEngine.downloadPhotoByHash(baseUrl, token, md5Hash, verifyMd5)

    suspend fun repairMissingOrCorruptThumbnailsForItem(
        itemId: Int,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Int = serverItemMergeEngine.repairMissingOrCorruptThumbnailsForItem(itemId, baseUrl, token)

    suspend fun unmergeOnPcServer(parentId: Int): Boolean = transport.unmergeOnPcServer(parentId)
}
