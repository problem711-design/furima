package com.example.myapplication.ui.screens

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import coil.request.ImageRequest
import coil.size.Precision
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.statusLabelOf
import com.example.myapplication.ui.FleaMarketViewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun CompactItemCard(
    itemWithPhotos: ItemWithPhotos,
    claudeLabel: FleaMarketViewModel.ClaudeItemLabelSummary? = null,
    defaultFeePercent: Int,
    defaultShipping: Int,
    defaultExpense: Int,
    isSelectionMode: Boolean,
    isSelected: Boolean,
    onSelectToggle: () -> Unit,
    onClick: () -> Unit,
    onLongClick: () -> Unit = {}
) {
    val item = itemWithPhotos.item
    val photos = itemWithPhotos.photos
    val firstPhoto = photos.firstOrNull()

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val basePrice = if (isSoldFamilyStatus(item.status) && item.soldPrice > 0) item.soldPrice else item.price

    val effectiveFee = if (item.useCustomSettings) item.feeRatePercent else defaultFeePercent
    val effectiveShipping = if (item.useCustomSettings) item.shippingCost else defaultShipping
    val effectiveExpense = if (item.useCustomSettings) item.expenseCost else defaultExpense
    val fee = (basePrice * effectiveFee) / 100
    val profit = (basePrice - fee - effectiveShipping - effectiveExpense).coerceAtLeast(0)

    val isBundle = item.mergedChildCount > 0 || item.bundleLevel > 0
    val isSelectable = item.status != "MERGED" && item.deletedAt == null

    val displayImageFile = firstPhoto?.displayFile

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .then(if (isSelectionMode && !isSelectable) Modifier.alpha(0.5f) else Modifier)
            .combinedClickable(
                onClick = onClick,
                onLongClick = onLongClick
            )
            .border(
                width = if (isSelected) 2.dp else 0.dp,
                color = if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent,
                shape = RoundedCornerShape(12.dp)
            )
            .testTag("item_card_${item.id}"),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.3f) else MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Column {
            // 写真エリア
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(1.2f)
                    .background(Color.LightGray)
            ) {
                val displayImageExists by produceState(initialValue = false, displayImageFile) {
                    value = withContext(Dispatchers.IO) { displayImageFile != null && displayImageFile.exists() }
                }
                if (displayImageFile != null && displayImageExists) {
                    val cardContext = LocalContext.current
                    AsyncImage(
                        model = remember(displayImageFile) {
                            ImageRequest.Builder(cardContext)
                                .data(displayImageFile)
                                .size(480)
                                .precision(Precision.INEXACT)
                                .build()
                        },
                        contentDescription = item.title,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                } else {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text("No Photo", color = Color.DarkGray, fontSize = 11.sp)
                    }
                }

                // ステータスバッジ ＆ まとめバッジ
                val hasUnsynced by produceState(initialValue = false, photos) {
                    value = withContext(Dispatchers.IO) {
                        photos.any { !it.isPcSynced && java.io.File(it.filePath).exists() }
                    }
                }
                val isAllSynced = photos.isNotEmpty() && photos.all { it.isPcSynced }

                Row(
                    modifier = Modifier
                        .align(Alignment.TopStart)
                        .padding(4.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(4.dp))
                            .background(
                                when (item.status) {
                                    "SOLD" -> Color(0xFFC62828)
                                    "MERGED" -> Color(0xFF6A1B9A)
                                    "DRAFT" -> Color(0xFF64748B)
                                    "DRAFT_DONE" -> Color(0xFF0D9488)
                                    "SHIPPING" -> Color(0xFFD97706)
                                    "LISTED" -> Color(0xFF059669)
                                    else -> Color(0xFF334155)
                                }
                            )
                            .padding(horizontal = 6.dp, vertical = 2.dp)
                    ) {
                        Text(
                            text = when (item.status) {
                                "SOLD" -> "売却済"
                                "MERGED" -> "まとめ済"
                                else -> statusLabelOf(item.status)
                            },
                            color = Color.White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }

                    if (hasUnsynced) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFF4F46E5))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(text = "未同期", color = Color.White, fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    } else if (isAllSynced) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFF0284C7))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(text = "送信済み", color = Color.White, fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    }

                    if (isBundle) {
                        Box(
                            modifier = Modifier
                                .clip(RoundedCornerShape(4.dp))
                                .background(Color(0xFFE65100))
                                .padding(horizontal = 5.dp, vertical = 2.dp)
                        ) {
                            Text(
                                text = "まとめ (${item.mergedChildCount}点)",
                                color = Color.White,
                                fontSize = 10.sp,
                                fontWeight = FontWeight.Bold
                            )
                        }
                    }
                }

                // フェーズ 6-9 (Build 14): Muse ラベルの商品単位集約チップ (優先順位と件数表記は PC と同一)
                if (claudeLabel != null) {
                    Box(
                        modifier = Modifier
                            .align(Alignment.BottomStart)
                            .padding(4.dp)
                            .clip(RoundedCornerShape(4.dp))
                            .background(claudeLabelColor(claudeLabel.key))
                            .padding(horizontal = 5.dp, vertical = 2.dp)
                    ) {
                        Text(
                            text = claudeLabelText(claudeLabel.key) + if ((claudeLabel.key == "error" || claudeLabel.key == "done") && claudeLabel.count > 1) " ×${claudeLabel.count}" else "",
                            color = if (claudeLabel.key == "approval") Color.Black else Color.White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }

                // 写真枚数インジケーター
                if (photos.size > 1) {
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .clip(RoundedCornerShape(4.dp))
                            .background(Color.Black.copy(alpha = 0.6f))
                            .padding(horizontal = 5.dp, vertical = 2.dp)
                            .align(Alignment.TopEnd)
                    ) {
                        Text(
                            text = "${photos.size}枚",
                            color = Color.White,
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Medium
                        )
                    }
                }
            }

            // 情報エリア
            Column(modifier = Modifier.padding(8.dp)) {
                Text(
                    text = item.title.ifBlank { "無題の商品" },
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )

                Spacer(modifier = Modifier.height(2.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = formatter.format(basePrice),
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary
                    )
                    Text(
                        text = "利益: ${formatter.format(profit)}",
                        fontSize = 11.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color(0xFF2E7D32)
                    )
                }

                if (item.useCustomSettings) {
                    Text(
                        text = "個別設定: 手数料${effectiveFee}% / 送料${effectiveShipping}円",
                        fontSize = 9.sp,
                        color = Color.Gray,
                        maxLines = 1
                    )
                }
            }
        }
    }
}
