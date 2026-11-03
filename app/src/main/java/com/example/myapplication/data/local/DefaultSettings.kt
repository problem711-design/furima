package com.example.myapplication.data.local

data class DefaultSettings(
    val defaultFeeRatePercent: Int = 5,
    val defaultShippingCost: Int = 210,
    val defaultExpenseCost: Int = 55,
    val thumbnailQuality: Int = 75, // サムネイルJPEG圧縮品質 (50=軽量, 75=標準, 90=高画質)
    val thumbnailMaxDimension: Int = 480, // サムネイル最大解像度 (320, 480, 640px)
    val autoCleanupSoldDays: Int = 30 // 売却済みから写真削除までの日数
)
