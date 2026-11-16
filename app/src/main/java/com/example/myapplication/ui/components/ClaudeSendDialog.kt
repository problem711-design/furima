package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.TextTemplate

/**
 * 「Museに送る」テンプレート選択ダイアログ (フェーズ 5-3)。
 * プロンプトテンプレートをプルダウンで選び、画像加工ジョブの作成を確定する。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ClaudeSendDialog(
    templates: List<TextTemplate>,
    photoCount: Int,
    excludedCount: Int,
    isConnected: Boolean,
    onDismiss: () -> Unit,
    onSend: (TextTemplate) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    var selectedId by remember { mutableStateOf(templates.firstOrNull()?.id) }
    val selected = templates.find { it.id == selectedId }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Museに送る") },
        text = {
            Column {
                Text(
                    "選択した写真 ${photoCount}枚を、画像加工ジョブとしてPCへ送ります。",
                    fontSize = 13.sp
                )
                if (excludedCount > 0) {
                    Spacer(modifier = Modifier.height(4.dp))
                    Text(
                        "※ PC未同期の写真 ${excludedCount}枚は対象外です（先に同期してください）",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.error
                    )
                }
                Spacer(modifier = Modifier.height(12.dp))
                if (templates.isEmpty()) {
                    Text(
                        "プロンプトテンプレートが未登録です。設定画面で「画像加工プロンプト用テンプレート」を登録してください。",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.error
                    )
                } else {
                    ExposedDropdownMenuBox(
                        expanded = expanded,
                        onExpandedChange = { expanded = !expanded },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        OutlinedTextField(
                            value = selected?.name ?: "",
                            onValueChange = {},
                            readOnly = true,
                            singleLine = true,
                            label = { Text("プロンプトテンプレート") },
                            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
                            modifier = Modifier
                                .menuAnchor()
                                .fillMaxWidth()
                        )
                        ExposedDropdownMenu(
                            expanded = expanded,
                            onDismissRequest = { expanded = false }
                        ) {
                            templates.forEach { tpl ->
                                DropdownMenuItem(
                                    text = {
                                        Column {
                                            Text(tpl.name, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                                            Text(
                                                text = tpl.content.lines().joinToString(" "),
                                                fontSize = 11.sp,
                                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis
                                            )
                                        }
                                    },
                                    onClick = {
                                        selectedId = tpl.id
                                        expanded = false
                                    }
                                )
                            }
                        }
                    }
                }
                if (!isConnected) {
                    Spacer(modifier = Modifier.height(8.dp))
                    Text(
                        "PCサーバー未接続のため送信できません。",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.error
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { selected?.let { onSend(it) } },
                enabled = isConnected && selected != null
            ) {
                Text("送信する")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("キャンセル")
            }
        }
    )
}
