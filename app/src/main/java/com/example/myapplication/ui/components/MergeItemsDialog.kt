package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.MergeType
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemWithPhotos
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MergeItemsDialog(
    selectedItems: List<ItemWithPhotos>,
    defaultSettings: DefaultSettings,
    onDismiss: () -> Unit,
    onConfirm: (title: String, desc: String, price: Int, shipping: Int, feeRate: Int, expense: Int, itemIds: List<Int>) -> Unit
) {
    // ユーザーがダイアログ内で同梱品目を調整（削除）できるように管理
    val itemsList = remember { mutableStateListOf<ItemWithPhotos>().apply { addAll(selectedItems) } }

    val totalOriginalPrice = itemsList.sumOf { it.item.price }
    val totalChildCount = itemsList.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }
    val defaultTitle = "【まとめ買い】" + itemsList.joinToString("＋") { it.item.title }
    val defaultDesc = buildString {
        appendLine("まとめ買いご依頼品 (${totalChildCount}点) です。")
        appendLine("-------------------------")
        for (item in itemsList) {
            appendLine("・${item.item.title} (通常価格: ¥${item.item.price})")
        }
        appendLine("-------------------------")
        appendLine("よろしくお願いいたします。")
    }

    var title by remember { mutableStateOf(defaultTitle) }
    var description by remember { mutableStateOf(defaultDesc) }
    var priceText by remember { mutableStateOf(totalOriginalPrice.toString()) }
    var shippingText by remember { mutableStateOf("750") } // まとめて箱で送る想定
    var feeRateText by remember { mutableStateOf(defaultSettings.defaultFeeRatePercent.toString()) }
    var expenseText by remember { mutableStateOf((defaultSettings.defaultExpenseCost * itemsList.size).coerceAtLeast(100).toString()) }

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val inputPrice = priceText.toIntOrNull() ?: totalOriginalPrice
    val feeRate = feeRateText.toIntOrNull() ?: defaultSettings.defaultFeeRatePercent
    val shipping = shippingText.toIntOrNull() ?: 0
    val expense = expenseText.toIntOrNull() ?: 0

    val fee = (inputPrice * feeRate) / 100
    val finalProfit = (inputPrice - fee - shipping - expense).coerceAtLeast(0)

    AlertDialog(
        onDismissRequest = onDismiss,
        icon = {
            Icon(imageVector = Icons.Default.MergeType, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
        },
        title = {
            Text(
                text = "選択した出品をまとめる (${totalChildCount}点)",
                fontWeight = FontWeight.Bold,
                fontSize = 18.sp
            )
        },
        text = {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .verticalScroll(rememberScrollState())
            ) {
                Text(
                    text = "個別出品を1つのまとめ出品に統合します。作成されたまとめ商品は『出品中』として一覧に表示され、元の個別出品は履歴として保持され必要に応じて後からバラす（解除）ことも可能です。",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                Spacer(modifier = Modifier.height(10.dp))

                // 同梱対象の商品リストカード
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Column(modifier = Modifier.padding(10.dp)) {
                        Text("📦 まとめ対象の商品一覧", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                        Spacer(modifier = Modifier.height(4.dp))
                        itemsList.forEach { itemWithPhotos ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 2.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = "・${itemWithPhotos.item.title}",
                                    fontSize = 12.sp,
                                    modifier = Modifier.weight(1f)
                                )
                                Text(
                                    text = formatter.format(itemWithPhotos.item.price),
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.SemiBold
                                )
                                if (itemsList.size > 2) {
                                    IconButton(
                                        onClick = { itemsList.remove(itemWithPhotos) },
                                        modifier = Modifier.size(24.dp)
                                    ) {
                                        Icon(Icons.Default.Close, contentDescription = "削除", modifier = Modifier.size(16.dp), tint = Color.Gray)
                                    }
                                }
                            }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(12.dp))

                // まとめ商品名
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text("まとめ出品タイトル (必須)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().testTag("merge_title_input")
                )

                Spacer(modifier = Modifier.height(10.dp))

                // まとめ商品説明文（自動生成テンプレート・編集可能）
                OutlinedTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = { Text("まとめ商品説明文 (任意・自動生成テンプレート)") },
                    minLines = 4,
                    maxLines = 8,
                    modifier = Modifier.fillMaxWidth().testTag("merge_description_input")
                )

                Spacer(modifier = Modifier.height(10.dp))

                val discount = (totalOriginalPrice - inputPrice).coerceAtLeast(0)
                // まとめ出品価格（手動自由入力）
                OutlinedTextField(
                    value = priceText,
                    onValueChange = { priceText = it },
                    label = { Text("まとめ出品価格 (円)") },
                    supportingText = {
                        Text(
                            text = "元商品の合計: ${formatter.format(totalOriginalPrice)}" +
                                    if (discount > 0) " (値引き額: -${formatter.format(discount)})" else ""
                        )
                    },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().testTag("merge_price_input")
                )

                Spacer(modifier = Modifier.height(10.dp))

                // 送料の手動入力 ＆ ショートカットチップ
                OutlinedTextField(
                    value = shippingText,
                    onValueChange = { shippingText = it },
                    label = { Text("まとめ後の送料 (円 - 手動入力可)") },
                    supportingText = { Text("任意の送料を直接入力できます（チップでクイック入力）") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().testTag("merge_shipping_input")
                )
                Spacer(modifier = Modifier.height(4.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    FilterChip(selected = (shippingText == "210"), onClick = { shippingText = "210" }, label = { Text("210円 (ネコポス/ゆうパケ)", fontSize = 11.sp) })
                    FilterChip(selected = (shippingText == "450"), onClick = { shippingText = "450" }, label = { Text("450円 (コンパクト)", fontSize = 11.sp) })
                    FilterChip(selected = (shippingText == "750"), onClick = { shippingText = "750" }, label = { Text("750円 (60サイズ箱)", fontSize = 11.sp) })
                    FilterChip(selected = (shippingText == "850"), onClick = { shippingText = "850" }, label = { Text("850円 (80サイズ箱)", fontSize = 11.sp) })
                }

                Spacer(modifier = Modifier.height(10.dp))

                // 経費・梱包資材費の手動入力 ＆ ショートカットチップ
                OutlinedTextField(
                    value = expenseText,
                    onValueChange = { expenseText = it },
                    label = { Text("梱包資材費・経費 (円 - 手動入力可)") },
                    supportingText = { Text("ダンボール代や緩衝材等の経費を直接入力できます") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().testTag("merge_expense_input")
                )
                Spacer(modifier = Modifier.height(4.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    FilterChip(selected = (expenseText == "0"), onClick = { expenseText = "0" }, label = { Text("0円", fontSize = 11.sp) })
                    FilterChip(selected = (expenseText == "55"), onClick = { expenseText = "55" }, label = { Text("55円", fontSize = 11.sp) })
                    FilterChip(selected = (expenseText == "100"), onClick = { expenseText = "100" }, label = { Text("100円 (箱代)", fontSize = 11.sp) })
                    FilterChip(selected = (expenseText == "150"), onClick = { expenseText = "150" }, label = { Text("150円 (箱+緩衝材)", fontSize = 11.sp) })
                }

                Spacer(modifier = Modifier.height(10.dp))

                // 販売手数料率の手動入力 ＆ ショートカットチップ
                OutlinedTextField(
                    value = feeRateText,
                    onValueChange = { feeRateText = it },
                    label = { Text("販売手数料率 (%) - 手動入力可") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(modifier = Modifier.height(4.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    FilterChip(selected = (feeRateText == "5"), onClick = { feeRateText = "5" }, label = { Text("5% (PayPayフリマ等)", fontSize = 11.sp) })
                    FilterChip(selected = (feeRateText == "10"), onClick = { feeRateText = "10" }, label = { Text("10% (メルカリ等)", fontSize = 11.sp) })
                }

                Spacer(modifier = Modifier.height(14.dp))

                // 統合後利益プレビューカード
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                    shape = RoundedCornerShape(10.dp)
                ) {
                    Column(modifier = Modifier.padding(12.dp)) {
                        Text(
                            text = "💰 まとめ売却の確定利益シミュレーション",
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "売価 ${formatter.format(inputPrice)} - 手数料(${feeRate}%) ${formatter.format(fee)} - 送料 ${formatter.format(shipping)} - 経費 ${formatter.format(expense)}",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "確定純利益: ${formatter.format(finalProfit)}",
                            fontSize = 18.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (title.isNotBlank() && inputPrice > 0 && itemsList.isNotEmpty()) {
                        onConfirm(
                            title,
                            description,
                            inputPrice,
                            shipping,
                            feeRate,
                            expense,
                            itemsList.map { it.item.id }
                        )
                    }
                },
                enabled = title.isNotBlank() && inputPrice > 0 && itemsList.isNotEmpty(),
                modifier = Modifier.testTag("confirm_merge_button")
            ) {
                Text("統合して1ページにまとめる")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("キャンセル")
            }
        }
    )
}
