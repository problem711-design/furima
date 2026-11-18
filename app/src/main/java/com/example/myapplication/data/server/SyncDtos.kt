package com.example.myapplication.data.server

import com.squareup.moshi.Json
import com.squareup.moshi.JsonClass

@JsonClass(generateAdapter = true)
data class SyncPhotoDto(
    @param:Json(name = "id") val id: Int,
    @param:Json(name = "md5Hash") val md5Hash: String
)

@JsonClass(generateAdapter = true)
data class SyncItemDto(
    @param:Json(name = "id") val id: Int,
    @param:Json(name = "syncUuid") val syncUuid: String = "",
    @param:Json(name = "deletedAt") val deletedAt: Long? = null,
    @param:Json(name = "title") val title: String,
    @param:Json(name = "description") val description: String,
    @param:Json(name = "status") val status: String,
    @param:Json(name = "price") val price: Int,
    @param:Json(name = "soldPrice") val soldPrice: Int,
    @param:Json(name = "profit") val profit: Int,
    @param:Json(name = "statusLabel") val statusLabel: String,
    @param:Json(name = "photos") val photos: List<SyncPhotoDto>,
    @param:Json(name = "mergedParentId") val mergedParentId: Int? = null,
    @param:Json(name = "mergedParentSyncUuid") val mergedParentSyncUuid: String? = null,
    @param:Json(name = "mergedChildCount") val mergedChildCount: Int? = null,
    @param:Json(name = "bundleLevel") val bundleLevel: Int? = null,
    @param:Json(name = "mergedChildTitles") val mergedChildTitles: String? = null,
    @param:Json(name = "originalTotalBundlePrice") val originalTotalBundlePrice: Int? = null,
    @param:Json(name = "discountAmount") val discountAmount: Int? = null,
    @param:Json(name = "shippingCost") val shippingCost: Int = 210,
    @param:Json(name = "feeRatePercent") val feeRatePercent: Int = 5,
    @param:Json(name = "expenseCost") val expenseCost: Int = 55,
    @param:Json(name = "useCustomSettings") val useCustomSettings: Boolean = false,
    @param:Json(name = "createdAt") val createdAt: Long? = null,
    @param:Json(name = "updatedAt") val updatedAt: Long? = null,
    @param:Json(name = "soldAt") val soldAt: Long? = null
)

@JsonClass(generateAdapter = true)
data class SyncFeePresetDto(
    @param:Json(name = "id") val id: String,
    @param:Json(name = "name") val name: String,
    @param:Json(name = "feeRatePercent") val feeRatePercent: Int,
    @param:Json(name = "shippingCost") val shippingCost: Int,
    @param:Json(name = "expenseCost") val expenseCost: Int
)

@JsonClass(generateAdapter = true)
data class SyncTextTemplateDto(
    @param:Json(name = "id") val id: String,
    @param:Json(name = "name") val name: String,
    @param:Json(name = "content") val content: String
)

@JsonClass(generateAdapter = true)
data class SyncSettingsDto(
    @param:Json(name = "defaultFeeRatePercent") val defaultFeeRatePercent: Int,
    @param:Json(name = "defaultShippingCost") val defaultShippingCost: Int,
    @param:Json(name = "defaultExpenseCost") val defaultExpenseCost: Int,
    @param:Json(name = "activePresetId") val activePresetId: String,
    @param:Json(name = "presets") val presets: List<SyncFeePresetDto>,
    @param:Json(name = "titleTemplates") val titleTemplates: List<SyncTextTemplateDto> = emptyList(),
    @param:Json(name = "descriptionTemplates") val descriptionTemplates: List<SyncTextTemplateDto> = emptyList(),
    @param:Json(name = "promptTemplates") val promptTemplates: List<SyncTextTemplateDto> = emptyList(),
    @param:Json(name = "settingsUpdatedAt") val settingsUpdatedAt: Long = 0L
)

@JsonClass(generateAdapter = true)
data class SyncItemsRequest(
    @param:Json(name = "protocolVersion") val protocolVersion: Int = 4,
    @param:Json(name = "storeId") val storeId: String,
    @param:Json(name = "items") val items: List<SyncItemDto>,
    @param:Json(name = "activeFolderNames") val activeFolderNames: List<String>,
    @param:Json(name = "defaultSettings") val defaultSettings: SyncSettingsDto? = null,
    @param:Json(name = "permanentlyDeletedUuids") val permanentlyDeletedUuids: List<String> = emptyList()
)

@JsonClass(generateAdapter = true)
data class SyncItemsResponse(
    @param:Json(name = "status") val status: String,
    @param:Json(name = "protocolVersion") val protocolVersion: Int = 4,
    @param:Json(name = "storeId") val storeId: String? = null,
    @param:Json(name = "syncedHashes") val syncedHashes: List<String> = emptyList(),
    @param:Json(name = "neededHashes") val neededHashes: List<String> = emptyList(),
    @param:Json(name = "permanentlyDeletedUuids") val permanentlyDeletedUuids: List<String> = emptyList(),
    @param:Json(name = "dataRevision") val dataRevision: Long = 0L,
    @param:Json(name = "settingsRevision") val settingsRevision: Long = 0L,
    @param:Json(name = "message") val message: String? = null
)

@JsonClass(generateAdapter = true)
data class SyncPhotoResponse(
    @param:Json(name = "status") val status: String,
    @param:Json(name = "stored") val stored: Boolean = false,
    @param:Json(name = "hash") val hash: String? = null,
    @param:Json(name = "message") val message: String? = null
)

data class HeartbeatInfo(
    val isConnected: Boolean,
    val dataRevision: Long = 0L,
    val settingsRevision: Long = 0L,
    // フェーズ 5-4: 旧サーバー応答にフィールドが無くても 0 で安全
    val claudeRevision: Long = 0L,
    // フェーズ 6-5 是正 (32-3): 既読専用カウンタ。旧サーバー応答では 0
    val labelSeenRevision: Long = 0L
)

@JsonClass(generateAdapter = true)
data class ClaudeJobPhotoDto(
    @param:Json(name = "md5") val md5: String,
    @param:Json(name = "fileName") val fileName: String = ""
)

@JsonClass(generateAdapter = true)
data class CreateClaudeJobRequestDto(
    @param:Json(name = "itemId") val itemId: Int,
    @param:Json(name = "itemUuid") val itemUuid: String,
    @param:Json(name = "photos") val photos: List<ClaudeJobPhotoDto>,
    @param:Json(name = "templateId") val templateId: String,
    @param:Json(name = "origin") val origin: String = "phone",
    @param:Json(name = "queue") val queue: String = "patrol",
    // フェーズ 6-8 (§35.3): 手打ちプロンプト用の任意フィールド (null のときは送信しない)
    @param:Json(name = "promptText") val promptText: String? = null,
    @param:Json(name = "customPromptName") val customPromptName: String? = null,
    @param:Json(name = "registerTemplateName") val registerTemplateName: String? = null
)

@JsonClass(generateAdapter = true)
data class ClaudeJobDto(
    @param:Json(name = "jobId") val jobId: String = "",
    @param:Json(name = "itemId") val itemId: Int = 0,
    @param:Json(name = "itemUuid") val itemUuid: String = "",
    @param:Json(name = "itemTitle") val itemTitle: String = "",
    @param:Json(name = "folderName") val folderName: String = "",
    @param:Json(name = "photos") val photos: List<ClaudeJobPhotoDto> = emptyList(),
    @param:Json(name = "templateName") val templateName: String = "",
    @param:Json(name = "origin") val origin: String = "",
    @param:Json(name = "queue") val queue: String = "manual",
    @param:Json(name = "status") val status: String = "",
    @param:Json(name = "createdAt") val createdAt: Long = 0L,
    @param:Json(name = "approvalRequestedAt") val approvalRequestedAt: Long = 0L,
    @param:Json(name = "completedAt") val completedAt: Long = 0L,
    @param:Json(name = "acknowledged") val acknowledged: Boolean = false,
    @param:Json(name = "errorDismissed") val errorDismissed: Boolean = false,
    @param:Json(name = "results") val results: List<ClaudeJobResultDto> = emptyList(),
    @param:Json(name = "errors") val errors: List<ClaudeJobErrorDto> = emptyList()
)

@JsonClass(generateAdapter = true)
data class ClaudeJobResultDto(
    @param:Json(name = "sourceMd5") val sourceMd5: String = "",
    @param:Json(name = "resultFileName") val resultFileName: String = "",
    @param:Json(name = "resultMd5") val resultMd5: String = ""
)

@JsonClass(generateAdapter = true)
data class ClaudeJobErrorDto(
    @param:Json(name = "sourceMd5") val sourceMd5: String = "",
    @param:Json(name = "message") val message: String = ""
)

@JsonClass(generateAdapter = true)
data class ClaudeJobListResponseDto(
    @param:Json(name = "status") val status: String = "",
    @param:Json(name = "jobs") val jobs: List<ClaudeJobDto> = emptyList(),
    @param:Json(name = "claudeRevision") val claudeRevision: Long = 0L,
    // フェーズ 6-5: 加工済みラベルの既読時刻 (itemUuid -> ms)。旧サーバー応答では空。
    @param:Json(name = "labelSeenAtByItemUuid") val labelSeenAtByItemUuid: Map<String, Long> = emptyMap(),
    @param:Json(name = "labelSeenRevision") val labelSeenRevision: Long = 0L
)

@JsonClass(generateAdapter = true)
data class ClaudeLabelSeenBatchRequestDto(
    @param:Json(name = "itemUuids") val itemUuids: List<String>
)

@JsonClass(generateAdapter = true)
data class ClaudeLabelSeenResponseDto(
    @param:Json(name = "status") val status: String = "",
    @param:Json(name = "seenAt") val seenAt: Long = 0L,
    @param:Json(name = "labelSeenRevision") val labelSeenRevision: Long = 0L
)

@JsonClass(generateAdapter = true)
data class ClaudeApprovalRequestDto(
    @param:Json(name = "approved") val approved: Boolean
)

@JsonClass(generateAdapter = true)
data class ClaudeJobResponseDto(
    @param:Json(name = "status") val status: String = "",
    @param:Json(name = "message") val message: String? = null,
    @param:Json(name = "job") val job: ClaudeJobDto? = null,
    @param:Json(name = "claudeRevision") val claudeRevision: Long = 0,
    // フェーズ 6-8 (§35.3): テンプレート登録の結果 (旧サーバー応答では既定値で安全)
    @param:Json(name = "templateRegistered") val templateRegistered: Boolean = false,
    @param:Json(name = "templateRegisterError") val templateRegisterError: String? = null
)
