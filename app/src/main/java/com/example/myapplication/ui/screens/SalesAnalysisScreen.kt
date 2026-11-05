package com.example.myapplication.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ElectricBolt
import androidx.compose.material.icons.filled.FileDownload
import androidx.compose.material.icons.filled.LocalFireDepartment
import androidx.compose.material.icons.filled.MonetizationOn
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.ui.TimeRange
import java.text.NumberFormat
import java.util.Calendar
import java.util.Locale

data class PriceTierStats(
    val label: String,
    val minPrice: Int,
    val maxPrice: Int,
    var count: Int = 0,
    var totalSales: Int = 0,
    var totalProfit: Int = 0,
    val leadTimeDaysList: MutableList<Double> = mutableListOf()
) {
    val avgPrice: Int get() = if (count > 0) totalSales / count else 0
    val profitMargin: Int get() = if (totalSales > 0) ((totalProfit.toDouble() / totalSales) * 100).toInt() else 0
    val avgLeadTimeDays: Double get() = if (leadTimeDaysList.isNotEmpty()) leadTimeDaysList.average() else 0.0
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SalesAnalysisScreen(
    viewModel: FleaMarketViewModel
) {
    val context = LocalContext.current
    val itemsWithPhotos by viewModel.itemsWithPhotos.collectAsState()
    val defaultSettings by viewModel.defaultSettings.collectAsState()
    val timeRange by viewModel.timeRange.collectAsState()

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)

    // 期間フィルタリング
    val startOfPeriod = Calendar.getInstance().apply {
        when (timeRange) {
            TimeRange.THIS_WEEK -> {
                set(Calendar.DAY_OF_WEEK, firstDayOfWeek)
                set(Calendar.HOUR_OF_DAY, 0)
                set(Calendar.MINUTE, 0)
                set(Calendar.SECOND, 0)
                set(Calendar.MILLISECOND, 0)
            }
            TimeRange.THIS_MONTH -> {
                set(Calendar.DAY_OF_MONTH, 1)
                set(Calendar.HOUR_OF_DAY, 0)
                set(Calendar.MINUTE, 0)
                set(Calendar.SECOND, 0)
                set(Calendar.MILLISECOND, 0)
            }
            TimeRange.ALL_TIME -> {
                timeInMillis = 0L
            }
        }
    }.timeInMillis

    val soldItems = itemsWithPhotos.filter {
        it.item.status == "SOLD" && ((it.item.soldAt ?: it.item.createdAt) >= startOfPeriod)
    }

    // 1. 基本数値集計
    var totalSales = 0
    var totalProfit = 0
    var totalFees = 0
    var totalShipping = 0
    var totalExpenses = 0

    // まとめ買い分析データ
    var bundleItemCount = 0
    var totalBundleDiscount = 0
    var totalEstimatedShippingSaved = 0

    val allLeadTimeDays = mutableListOf<Double>()
    var speedUnder1Day = 0
    var speed1to3Days = 0
    var speed4to7Days = 0
    var speedOver8Days = 0

    // 価格帯バケットの定義
    val tiers = listOf(
        PriceTierStats("〜¥999 (低価格)", 0, 999),
        PriceTierStats("¥1,000〜¥2,999 (ボリューム)", 1000, 2999),
        PriceTierStats("¥3,000〜¥4,999 (ミドル)", 3000, 4999),
        PriceTierStats("¥5,000〜¥9,999 (高価格)", 5000, 9999),
        PriceTierStats("¥10,000〜 (プレミアム)", 10000, Int.MAX_VALUE)
    )

    for (itemWithPhotos in soldItems) {
        val item = itemWithPhotos.item
        val price = if (item.soldPrice > 0) item.soldPrice else item.price
        val feeRate = item.getEffectiveFeeRatePercent(defaultSettings)
        val fee = (price * feeRate) / 100
        val shipping = item.getEffectiveShippingCost(defaultSettings)
        val expense = item.getEffectiveExpenseCost(defaultSettings)
        val profit = item.getCalculatedProfit(defaultSettings)

        totalSales += price
        totalProfit += profit
        totalFees += fee
        totalShipping += shipping
        totalExpenses += expense

        // まとめ買い情報の集計
        if (item.mergedChildCount > 1 || item.bundleLevel > 0) {
            bundleItemCount++
            totalBundleDiscount += item.discountAmount
            // 個別に発送した場合の推定送料 (単品送料 * 子商品数) - まとめ発送の実際の送料
            val estOriginalShipping = defaultSettings.defaultShippingCost * item.mergedChildCount
            val savedShipping = (estOriginalShipping - shipping).coerceAtLeast(0)
            totalEstimatedShippingSaved += savedShipping
        }

        // 売却リードタイム計算 (日数)
        val soldTime = item.soldAt ?: item.createdAt
        val leadTimeMillis = (soldTime - item.createdAt).coerceAtLeast(0)
        val leadTimeDays = leadTimeMillis.toDouble() / (1000.0 * 60 * 60 * 24)
        allLeadTimeDays.add(leadTimeDays)

        if (leadTimeDays < 1.0) {
            speedUnder1Day++
        } else if (leadTimeDays <= 3.0) {
            speed1to3Days++
        } else if (leadTimeDays <= 7.0) {
            speed4to7Days++
        } else {
            speedOver8Days++
        }

        // 価格帯への割り当て
        val matchedTier = tiers.find { price in it.minPrice..it.maxPrice }
        if (matchedTier != null) {
            matchedTier.count++
            matchedTier.totalSales += price
            matchedTier.totalProfit += profit
            matchedTier.leadTimeDaysList.add(leadTimeDays)
        }
    }

    val overallAvgLeadDays = if (allLeadTimeDays.isNotEmpty()) allLeadTimeDays.average() else 0.0
    val overallProfitMargin = if (totalSales > 0) ((totalProfit.toDouble() / totalSales) * 100).toInt() else 0

    // スマート・インサイト分析
    val mostPopularTier = tiers.filter { it.count > 0 }.maxByOrNull { it.count }
    val highestRevenueTier = tiers.filter { it.totalSales > 0 }.maxByOrNull { it.totalSales }
    val highestMarginTier = tiers.filter { it.count > 0 }.maxByOrNull { it.profitMargin }

    Scaffold(
        topBar = {
            Surface(
                color = MaterialTheme.colorScheme.primary,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 14.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "売上 & 価格帯・日数分析",
                        fontWeight = FontWeight.Bold,
                        fontSize = 17.sp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                }
            }
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
                .padding(14.dp)
                .verticalScroll(rememberScrollState())
        ) {
            // 期間選択チップ
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                TimeRange.values().forEach { range ->
                    FilterChip(
                        selected = (timeRange == range),
                        onClick = { viewModel.setTimeRange(range) },
                        label = { Text(range.label, fontWeight = FontWeight.Bold, fontSize = 12.sp) },
                        modifier = Modifier.weight(1f)
                    )
                }
            }

            Spacer(modifier = Modifier.height(14.dp))

            // ① 主要サマリーカード
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(14.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "📊 ${timeRange.label}の実績サマリー",
                            fontWeight = FontWeight.Bold,
                            fontSize = 15.sp,
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(12.dp))
                                .background(MaterialTheme.colorScheme.primary)
                                .padding(horizontal = 8.dp, vertical = 3.dp)
                        ) {
                            Text(
                                text = "利益率: $overallProfitMargin%",
                                color = MaterialTheme.colorScheme.onPrimary,
                                fontWeight = FontWeight.Bold,
                                fontSize = 11.sp
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(12.dp))

                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Column {
                            Text("総売上金額", fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f))
                            Text(formatter.format(totalSales), fontSize = 20.sp, fontWeight = FontWeight.Bold)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text("総純利益額", fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.7f))
                            Text(
                                formatter.format(totalProfit),
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                    }

                    Spacer(modifier = Modifier.height(8.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.2f))
                    Spacer(modifier = Modifier.height(8.dp))

                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceAround) {
                        Text("売却点数: ${soldItems.size}点", fontWeight = FontWeight.SemiBold, fontSize = 12.sp)
                        Text(
                            "平均単価: ${if (soldItems.isNotEmpty()) formatter.format(totalSales / soldItems.size) else "¥0"}",
                            fontWeight = FontWeight.SemiBold,
                            fontSize = 12.sp
                        )
                    }

                    Spacer(modifier = Modifier.height(8.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.2f))
                    Spacer(modifier = Modifier.height(8.dp))

                    // 経費・手数料内訳の詳細
                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text("手数料合計: ${formatter.format(totalFees)}", fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f))
                        Text("送料合計: ${formatter.format(totalShipping)}", fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f))
                        Text("資材費合計: ${formatter.format(totalExpenses)}", fontSize = 11.sp, color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f))
                    }
                }
            }

            Spacer(modifier = Modifier.height(16.dp))

            // ② スマート・インサイト（人気ライン・売上No.1・平均日数・まとめ買い節約）
            Text("💡 分析ハイライト", fontWeight = FontWeight.Bold, fontSize = 15.sp)
            Spacer(modifier = Modifier.height(8.dp))

            if (soldItems.isEmpty()) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f))
                ) {
                    Text(
                        text = "${timeRange.label}の売却データがまだありません。売却済み商品が登録されると自動分析が表示されます。",
                        fontSize = 12.sp,
                        color = Color.Gray,
                        modifier = Modifier.padding(14.dp)
                    )
                }
            } else {
                // まとめ買い効果カード (存在する場合)
                if (bundleItemCount > 0) {
                    InsightItemCard(
                        icon = Icons.Default.Star,
                        iconTint = Color(0xFF7C3AED),
                        title = "まとめ買い効果分析",
                        headline = "まとめ取引 ${bundleItemCount}件 (送料節約: 推定${formatter.format(totalEstimatedShippingSaved)})",
                        description = "まとめ値引き総額: ${formatter.format(totalBundleDiscount)} / 配送をおまとめしたことで推定${formatter.format(totalEstimatedShippingSaved)}の送料が節約されました"
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }

                // ハイライト項目1: 一番人気ライン
                if (mostPopularTier != null) {
                    val countShare = if (soldItems.isNotEmpty()) (mostPopularTier.count * 100) / soldItems.size else 0
                    InsightItemCard(
                        icon = Icons.Default.LocalFireDepartment,
                        iconTint = Color(0xFFE65100),
                        title = "一番人気（販売点数 No.1）",
                        headline = "【${mostPopularTier.label}】",
                        description = "全体の ${countShare}%（${mostPopularTier.count}点）がこの価格帯で売れています"
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }

                // ハイライト項目2: 総売上が最も高いライン
                if (highestRevenueTier != null) {
                    val salesShare = if (totalSales > 0) ((highestRevenueTier.totalSales.toDouble() / totalSales) * 100).toInt() else 0
                    InsightItemCard(
                        icon = Icons.Default.MonetizationOn,
                        iconTint = Color(0xFF2E7D32),
                        title = "売上貢献度 No.1（総売上トップ）",
                        headline = "【${highestRevenueTier.label}】",
                        description = "売上 ${formatter.format(highestRevenueTier.totalSales)}（全体の ${salesShare}%）を稼ぎ出しています"
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                }

                // ハイライト項目3: 平均売却日数
                InsightItemCard(
                    icon = Icons.Default.Schedule,
                    iconTint = Color(0xFF1565C0),
                    title = "売却リードタイム（出品から成約まで）",
                    headline = String.format(Locale.JAPAN, "平均 %.1f 日", overallAvgLeadDays),
                    description = "24時間以内の即売れ: ${speedUnder1Day}件 (${if (soldItems.isNotEmpty()) (speedUnder1Day * 100) / soldItems.size else 0}%) / 3日以内: ${speed1to3Days}件"
                )
            }

            Spacer(modifier = Modifier.height(18.dp))

            // ③ 価格帯別の詳細分析テーブル ＆ 構成比バー
            Text("🎯 価格帯別の売上・点数内訳", fontWeight = FontWeight.Bold, fontSize = 15.sp)
            Spacer(modifier = Modifier.height(8.dp))

            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(14.dp)) {
                    tiers.forEachIndexed { index, tier ->
                        val salesShare = if (totalSales > 0) tier.totalSales.toFloat() / totalSales.toFloat() else 0f
                        val countShare = if (soldItems.isNotEmpty()) (tier.count * 100) / soldItems.size else 0

                        Column(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(tier.label, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                                Text(
                                    "${tier.count}点 (${countShare}%)",
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.primary
                                )
                            }

                            Spacer(modifier = Modifier.height(3.dp))

                            // 構成比プログレスバー
                            LinearProgressIndicator(
                                progress = { salesShare },
                                modifier = Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)),
                                color = MaterialTheme.colorScheme.primary,
                                trackColor = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.2f)
                            )

                            Spacer(modifier = Modifier.height(4.dp))

                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween
                            ) {
                                Text("売上: ${formatter.format(tier.totalSales)}", fontSize = 11.sp, color = Color.Gray)
                                Text(
                                    "利益: ${formatter.format(tier.totalProfit)} (利益率 ${tier.profitMargin}%)",
                                    fontSize = 11.sp,
                                    color = Color.DarkGray
                                )
                                Text(
                                    "平均: ${if (tier.count > 0) String.format(Locale.JAPAN, "%.1f日", tier.avgLeadTimeDays) else "-"}",
                                    fontSize = 11.sp,
                                    color = Color.Gray
                                )
                            }
                        }

                        if (index < tiers.size - 1) {
                            HorizontalDivider(modifier = Modifier.padding(vertical = 6.dp), color = Color.LightGray.copy(alpha = 0.5f))
                        }
                    }
                }
            }

            Spacer(modifier = Modifier.height(18.dp))

            // ④ 売却スピード（日数）の内訳
            Text("⚡ 売却スピードの内訳", fontWeight = FontWeight.Bold, fontSize = 15.sp)
            Spacer(modifier = Modifier.height(8.dp))

            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(14.dp)
            ) {
                Column(modifier = Modifier.padding(14.dp)) {
                    SpeedRow(label = "🚀 即日 (24時間以内)", count = speedUnder1Day, total = soldItems.size)
                    Spacer(modifier = Modifier.height(6.dp))
                    SpeedRow(label = "⚡ 1〜3日以内", count = speed1to3Days, total = soldItems.size)
                    Spacer(modifier = Modifier.height(6.dp))
                    SpeedRow(label = "📦 4〜7日以内 (1週間)", count = speed4to7Days, total = soldItems.size)
                    Spacer(modifier = Modifier.height(6.dp))
                    SpeedRow(label = "⏳ 8日以上 (長期)", count = speedOver8Days, total = soldItems.size)
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            // CSVエクスポートボタン
            Button(
                onClick = { viewModel.exportAllToCsv(context) },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(50.dp)
                    .testTag("export_csv_full_button"),
                shape = RoundedCornerShape(12.dp)
            ) {
                Icon(imageVector = Icons.Default.FileDownload, contentDescription = null)
                Spacer(modifier = Modifier.width(8.dp))
                Text("全売上データをCSVで出力 (Excel/共有)", fontSize = 15.sp, fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
fun InsightItemCard(
    icon: ImageVector,
    iconTint: Color,
    title: String,
    headline: String,
    description: String
) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        shape = RoundedCornerShape(12.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Row(
            modifier = Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Box(
                modifier = Modifier
                    .size(38.dp)
                    .clip(CircleShape)
                    .background(iconTint.copy(alpha = 0.15f)),
                contentAlignment = Alignment.Center
            ) {
                Icon(imageVector = icon, contentDescription = null, tint = iconTint, modifier = Modifier.size(22.dp))
            }

            Spacer(modifier = Modifier.width(12.dp))

            Column {
                Text(text = title, fontSize = 11.sp, color = Color.Gray, fontWeight = FontWeight.Medium)
                Text(text = headline, fontSize = 14.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
                Text(text = description, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}

@Composable
fun SpeedRow(label: String, count: Int, total: Int) {
    val percent = if (total > 0) (count * 100) / total else 0
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = label, fontSize = 12.sp, fontWeight = FontWeight.Medium)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(text = "${count}件", fontSize = 12.sp, fontWeight = FontWeight.Bold)
            Spacer(modifier = Modifier.width(6.dp))
            Text(text = "(${percent}%)", fontSize = 11.sp, color = Color.Gray)
        }
    }
}
