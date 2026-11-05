package com.example.myapplication.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "items")
data class ItemEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Int = 0,
    val title: String,
    val description: String = "", // 商品説明文（任意）
    val status: String = "LISTED", // "LISTED" (出品中), "SOLD" (売却済み), "DRAFT" (未出品), "MERGED" (まとめ売却済み)
    val price: Int = 0, // 出品価格
    val soldPrice: Int = 0, // 実際の売却価格
    val shippingCost: Int = 210, // 個別送料 (円)
    val feeRatePercent: Int = 5, // 個別販売手数料 %
    val expenseCost: Int = 55, // 個別経費・梱包資材費 (円)
    val useCustomSettings: Boolean = false, // 個別費用設定を使用するかどうか
    val isShared: Boolean = false, // PC送信・共有済みフラグ
    val mergedParentId: Int? = null, // まとめ買いの親ID（nullなら通常表示、値があればまとめられた子商品）
    val mergedChildCount: Int = 0, // まとめ買い親商品の場合の同梱数
    val mergedChildTitles: String = "", // まとめ買い時の元の個別商品名リスト
    val originalTotalBundlePrice: Int = 0, // まとめ買い前の元商品合計額
    val discountAmount: Int = 0, // まとめ買い値引き額
    val bundleLevel: Int = 0, // まとめ階層（0: 単品, 1: まとめ商品, 2: まとめのまとめ）
    val createdAt: Long = System.currentTimeMillis(),
    val soldAt: Long? = null // 売却成立日時
) {
    fun getEffectiveFeeRatePercent(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) feeRatePercent else defaultSettings.defaultFeeRatePercent

    fun getEffectiveShippingCost(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) shippingCost else defaultSettings.defaultShippingCost

    fun getEffectiveExpenseCost(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) expenseCost else defaultSettings.defaultExpenseCost

    fun getCalculatedProfit(defaultSettings: DefaultSettings): Int {
        val basePrice = if (status == "SOLD" && soldPrice > 0) soldPrice else price
        val feeRate = getEffectiveFeeRatePercent(defaultSettings)
        val shipping = getEffectiveShippingCost(defaultSettings)
        val expense = getEffectiveExpenseCost(defaultSettings)
        val fee = (basePrice * feeRate) / 100
        return (basePrice - fee - shipping - expense).coerceAtLeast(0)
    }

    val calculatedProfit: Int
        get() = getCalculatedProfit(DefaultSettings())
}
