package com.example.myapplication.data.local

data class FeePreset(
    val id: String,
    val name: String,
    val feeRatePercent: Int,
    val shippingCost: Int,
    val expenseCost: Int
)

data class TextTemplate(
    val id: String,
    val name: String,
    val content: String
)

data class DefaultSettings(
    val defaultFeeRatePercent: Int = 5,
    val defaultShippingCost: Int = 210,
    val defaultExpenseCost: Int = 55,
    val thumbnailQuality: Int = 75, // サムネイルJPEG圧縮品質 (50=軽量, 75=標準, 90=高画質)
    val thumbnailMaxDimension: Int = 480, // サムネイル最大解像度 (320, 480, 640px)
    val autoCleanupSoldDays: Int = 30, // 売却済みから写真削除までの日数
    val activePresetId: String = "yahoo_furima",
    val presets: List<FeePreset> = defaultPresets,
    val titleTemplates: List<TextTemplate> = emptyList(),
    val descriptionTemplates: List<TextTemplate> = emptyList(),
    val settingsUpdatedAt: Long = 0L
) {
    companion object {
        val defaultPresets = listOf(
            FeePreset(
                id = "yahoo_furima",
                name = "Yahoo!フリマ (基本)",
                feeRatePercent = 5,
                shippingCost = 210,
                expenseCost = 55
            ),
            FeePreset(
                id = "mercari",
                name = "メルカリ (ネコポス/ゆうパケット)",
                feeRatePercent = 10,
                shippingCost = 210,
                expenseCost = 55
            ),
            FeePreset(
                id = "rakuma",
                name = "楽天ラクマ (かんたんラクマパック)",
                feeRatePercent = 10,
                shippingCost = 180,
                expenseCost = 55
            ),
            FeePreset(
                id = "yupaket_mini",
                name = "ゆうパケットポストmini",
                feeRatePercent = 5,
                shippingCost = 160,
                expenseCost = 20
            )
        )
    }

    val activePreset: FeePreset
        get() = presets.find { it.id == activePresetId } ?: presets.firstOrNull() ?: FeePreset(
            id = "custom",
            name = "カスタム",
            feeRatePercent = defaultFeeRatePercent,
            shippingCost = defaultShippingCost,
            expenseCost = defaultExpenseCost
        )
}
