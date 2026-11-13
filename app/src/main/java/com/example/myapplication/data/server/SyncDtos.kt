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
    @param:Json(name = "title") val title: String,
    @param:Json(name = "description") val description: String,
    @param:Json(name = "status") val status: String,
    @param:Json(name = "price") val price: Int,
    @param:Json(name = "soldPrice") val soldPrice: Int,
    @param:Json(name = "profit") val profit: Int,
    @param:Json(name = "statusLabel") val statusLabel: String,
    @param:Json(name = "photos") val photos: List<SyncPhotoDto>,
    @param:Json(name = "mergedParentId") val mergedParentId: Int? = null,
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
data class SyncSettingsDto(
    @param:Json(name = "defaultFeeRatePercent") val defaultFeeRatePercent: Int,
    @param:Json(name = "defaultShippingCost") val defaultShippingCost: Int,
    @param:Json(name = "defaultExpenseCost") val defaultExpenseCost: Int,
    @param:Json(name = "activePresetId") val activePresetId: String,
    @param:Json(name = "presets") val presets: List<SyncFeePresetDto>
)

@JsonClass(generateAdapter = true)
data class SyncItemsRequest(
    @param:Json(name = "protocolVersion") val protocolVersion: Int = 3,
    @param:Json(name = "storeId") val storeId: String,
    @param:Json(name = "items") val items: List<SyncItemDto>,
    @param:Json(name = "activeFolderNames") val activeFolderNames: List<String>,
    @param:Json(name = "defaultSettings") val defaultSettings: SyncSettingsDto? = null
)

@JsonClass(generateAdapter = true)
data class SyncItemsResponse(
    @param:Json(name = "status") val status: String,
    @param:Json(name = "protocolVersion") val protocolVersion: Int = 3,
    @param:Json(name = "storeId") val storeId: String? = null,
    @param:Json(name = "syncedHashes") val syncedHashes: List<String> = emptyList(),
    @param:Json(name = "neededHashes") val neededHashes: List<String> = emptyList(),
    @param:Json(name = "message") val message: String? = null
)

@JsonClass(generateAdapter = true)
data class SyncPhotoResponse(
    @param:Json(name = "status") val status: String,
    @param:Json(name = "stored") val stored: Boolean = false,
    @param:Json(name = "hash") val hash: String? = null,
    @param:Json(name = "message") val message: String? = null
)
