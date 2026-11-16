package com.example.myapplication.data.local

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "items")
data class ItemEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Int = 0,
    val title: String,
    val description: String = "", // 商品説明文（任意）
    val status: String = "DRAFT", // "DRAFT" (下書き中), "DRAFT_DONE" (下書き済み), "LISTED" (出品中), "SHIPPING" (発送準備中), "SOLD" (売却済み), "MERGED" (まとめ子)
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
    val updatedAt: Long = System.currentTimeMillis(), // 最終更新日時タイムスタンプ
    val soldAt: Long? = null, // 売却成立日時
    val syncUuid: String = java.util.UUID.randomUUID().toString(), // 端末・PC共通の一意同期UUID
    val deletedAt: Long? = null, // 非nullならゴミ箱入り日時 (論理削除)
    val isDirty: Boolean = true // 未同期ローカル変更ありフラグ
) {
    fun getEffectiveFeeRatePercent(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) feeRatePercent else defaultSettings.defaultFeeRatePercent

    fun getEffectiveShippingCost(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) shippingCost else defaultSettings.defaultShippingCost

    fun getEffectiveExpenseCost(defaultSettings: DefaultSettings): Int =
        if (useCustomSettings) expenseCost else defaultSettings.defaultExpenseCost

    fun getCalculatedProfit(defaultSettings: DefaultSettings): Int {
        val basePrice = if (isSoldFamilyStatus(status) && soldPrice > 0) soldPrice else price
        val feeRate = getEffectiveFeeRatePercent(defaultSettings)
        val shipping = getEffectiveShippingCost(defaultSettings)
        val expense = getEffectiveExpenseCost(defaultSettings)
        val fee = (basePrice * feeRate) / 100
        return (basePrice - fee - shipping - expense).coerceAtLeast(0)
    }

    val calculatedProfit: Int
        get() = getCalculatedProfit(DefaultSettings())
}

// フェーズ 6-4: 売却系ステータス {SOLD, SHIPPING} の共通判定。
// 売却価格・売却日時の保持、利益計算、売上集計はすべてこの所属判定に揃える。
fun isSoldFamilyStatus(status: String): Boolean = status == "SOLD" || status == "SHIPPING"

// フェーズ 6-4: ステータス表示名の統一表。不明なコードは出品中へ倒さずコードそのものを返す。
// MERGED は親ステータスから決まるため、ここでは扱わない (mergedChildStatusLabelOf を使う)。
fun statusLabelOf(status: String): String = when (status) {
    "DRAFT" -> "下書き中"
    "DRAFT_DONE" -> "下書き済み"
    "LISTED" -> "出品中"
    "SHIPPING" -> "発送準備中"
    "SOLD" -> "売却済み"
    else -> status
}

// フェーズ 6-4: まとめ子商品の表示名は親のステータスから決める。
fun mergedChildStatusLabelOf(parentStatus: String): String = when (parentStatus) {
    "DRAFT" -> "まとめ下書き中"
    "DRAFT_DONE" -> "まとめ下書き済み"
    "SHIPPING" -> "まとめ発送準備中"
    "SOLD" -> "まとめ売却済"
    else -> "まとめ出品中"
}
