package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.WarningAmber
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

data class ListingWarning(
    val message: String,
    val isCritical: Boolean = true
)

fun evaluateListingWarnings(
    title: String,
    description: String,
    photoCount: Int,
    price: Int,
    estimatedProfit: Int,
    status: String
): List<ListingWarning> {
    if (status == "SOLD") return emptyList()
    val warnings = mutableListOf<ListingWarning>()
    val trimmedTitle = title.trim()
    if (trimmedTitle.isEmpty()) {
        warnings.add(ListingWarning("商品名が未入力です", isCritical = true))
    } else if (trimmedTitle.length > 40) {
        warnings.add(ListingWarning("商品名が ${trimmedTitle.length}文字です（Yahoo!フリマ上限 40文字超過）", isCritical = true))
    }
    if (photoCount == 0) {
        warnings.add(ListingWarning("商品が未撮影です（写真 0枚）", isCritical = true))
    } else if (photoCount > 10) {
        warnings.add(ListingWarning("写真が ${photoCount}枚あります（Yahoo!フリマ上限 10枚超過）", isCritical = true))
    }
    if (price <= 0) {
        warnings.add(ListingWarning("販売価格が未設定（¥0）です", isCritical = true))
    } else if (price < 300) {
        warnings.add(ListingWarning("販売価格 ¥${price} はフリマ最低出品額（¥300）未満です", isCritical = false))
    }
    if (price > 0 && estimatedProfit <= 0) {
        warnings.add(ListingWarning("見込純利益が ¥${estimatedProfit}（利益なし／赤字）です", isCritical = true))
    }
    if (description.isBlank()) {
        warnings.add(ListingWarning("商品説明文が未入力です", isCritical = false))
    }
    return warnings
}

@Composable
fun ListingValidationBanner(
    warnings: List<ListingWarning>,
    modifier: Modifier = Modifier
) {
    if (warnings.isEmpty()) return
    val hasCritical = warnings.any { it.isCritical }
    val containerColor = if (hasCritical) {
        Color(0xFFFFF7ED)
    } else {
        MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.55f)
    }
    val accentColor = if (hasCritical) Color(0xFFC2410C) else MaterialTheme.colorScheme.secondary

    Card(
        modifier = modifier
            .fillMaxWidth()
            .testTag("listing_validation_banner"),
        colors = CardDefaults.cardColors(containerColor = containerColor),
        shape = RoundedCornerShape(10.dp)
    ) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    imageVector = if (hasCritical) Icons.Default.WarningAmber else Icons.Default.Info,
                    contentDescription = null,
                    tint = accentColor,
                    modifier = Modifier.size(16.dp)
                )
                Spacer(modifier = Modifier.width(6.dp))
                Text(
                    text = if (hasCritical) "出品前チェック（要確認 ${warnings.size}件）" else "出品前メモ（${warnings.size}件）",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = accentColor
                )
            }
            Spacer(modifier = Modifier.height(2.dp))
            warnings.forEach { w ->
                Text(
                    text = "・${w.message}",
                    fontSize = 11.sp,
                    color = if (w.isCritical) Color(0xFF9A3412) else MaterialTheme.colorScheme.onSecondaryContainer
                )
            }
        }
    }
}
