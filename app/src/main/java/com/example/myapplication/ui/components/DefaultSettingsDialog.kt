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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.DefaultSettings

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun DefaultSettingsDialog(
    currentSettings: DefaultSettings,
    onDismiss: () -> Unit,
    onSave: (DefaultSettings) -> Unit
) {
    var feeRateText by remember { mutableStateOf(currentSettings.defaultFeeRatePercent.toString()) }
    var shippingCostText by remember { mutableStateOf(currentSettings.defaultShippingCost.toString()) }
    var expenseCostText by remember { mutableStateOf(currentSettings.defaultExpenseCost.toString()) }

    AlertDialog(
        onDismissRequest = onDismiss,
        icon = {
            Icon(
                imageVector = Icons.Default.Settings,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.primary
            )
        },
        title = {
            Text(
                text = "基本費用設定 (デフォルト)",
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
                    text = "個別設定を行わない出品フォルダに自動適用されます。変更すると対象フォルダの利益も自動再計算されます。",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                Spacer(modifier = Modifier.height(16.dp))

                // 1. 販売手数料率
                Text(
                    text = "① 基本販売手数料率 (%)",
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp
                )
                Spacer(modifier = Modifier.height(6.dp))
                OutlinedTextField(
                    value = feeRateText,
                    onValueChange = { feeRateText = it },
                    label = { Text("販売手数料 (%)") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("default_fee_input")
                )
                Spacer(modifier = Modifier.height(6.dp))
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    FilterChip(
                        selected = (feeRateText == "5"),
                        onClick = { feeRateText = "5" },
                        label = { Text("5% (PayPay等)", fontSize = 12.sp) }
                    )
                    FilterChip(
                        selected = (feeRateText == "10"),
                        onClick = { feeRateText = "10" },
                        label = { Text("10% (メルカリ等)", fontSize = 12.sp) }
                    )
                }

                Spacer(modifier = Modifier.height(14.dp))

                // 2. 標準送料
                Text(
                    text = "② 標準送料 (円)",
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp
                )
                Spacer(modifier = Modifier.height(6.dp))
                OutlinedTextField(
                    value = shippingCostText,
                    onValueChange = { shippingCostText = it },
                    label = { Text("送料 (円)") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("default_shipping_input")
                )
                Spacer(modifier = Modifier.height(6.dp))
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    FilterChip(
                        selected = (shippingCostText == "210"),
                        onClick = { shippingCostText = "210" },
                        label = { Text("210円 (ネコポス/ゆうパケ)", fontSize = 12.sp) }
                    )
                    FilterChip(
                        selected = (shippingCostText == "450"),
                        onClick = { shippingCostText = "450" },
                        label = { Text("450円 (コンパクト等)", fontSize = 12.sp) }
                    )
                    FilterChip(
                        selected = (shippingCostText == "750"),
                        onClick = { shippingCostText = "750" },
                        label = { Text("750円 (60サイズ)", fontSize = 12.sp) }
                    )
                }

                Spacer(modifier = Modifier.height(14.dp))

                // 3. 梱包資材・経費
                Text(
                    text = "③ 標準経費・梱包資材費 (円)",
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp
                )
                Spacer(modifier = Modifier.height(6.dp))
                OutlinedTextField(
                    value = expenseCostText,
                    onValueChange = { expenseCostText = it },
                    label = { Text("経費・梱包材費 (円)") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("default_expense_input")
                )
                Spacer(modifier = Modifier.height(6.dp))
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    modifier = Modifier.fillMaxWidth()
                ) {
                    FilterChip(
                        selected = (expenseCostText == "0"),
                        onClick = { expenseCostText = "0" },
                        label = { Text("0円", fontSize = 12.sp) }
                    )
                    FilterChip(
                        selected = (expenseCostText == "55"),
                        onClick = { expenseCostText = "55" },
                        label = { Text("55円 (クッション封筒等)", fontSize = 12.sp) }
                    )
                    FilterChip(
                        selected = (expenseCostText == "100"),
                        onClick = { expenseCostText = "100" },
                        label = { Text("100円 (専用BOX等)", fontSize = 12.sp) }
                    )
                }

                Spacer(modifier = Modifier.height(16.dp))

                OutlinedButton(
                    onClick = {
                        feeRateText = "5"
                        shippingCostText = "210"
                        expenseCostText = "55"
                    },
                    modifier = Modifier
                        .fillMaxWidth()
                        .testTag("reset_defaults_button"),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Text("初期値 (5% / 210円 / 55円) に戻す", fontSize = 13.sp)
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val fee = feeRateText.toIntOrNull() ?: 5
                    val shipping = shippingCostText.toIntOrNull() ?: 210
                    val expense = expenseCostText.toIntOrNull() ?: 55
                    onSave(
                        DefaultSettings(
                            defaultFeeRatePercent = fee.coerceAtLeast(0),
                            defaultShippingCost = shipping.coerceAtLeast(0),
                            defaultExpenseCost = expense.coerceAtLeast(0)
                        )
                    )
                },
                modifier = Modifier.testTag("save_defaults_button")
            ) {
                Text("保存して適用")
            }
        },
        dismissButton = {
            TextButton(
                onClick = onDismiss,
                modifier = Modifier.testTag("cancel_defaults_button")
            ) {
                Text("キャンセル")
            }
        }
    )
}
