package com.example.myapplication.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.local.TextTemplate

/**
 * 「Museに送る」プロンプト選択ダイアログ。
 * フェーズ 6-8 (§35.2.1): プルダウンをやめ、リスト方式に変更した。先頭行は「プロンプトを手打ち」。
 * チェックは排他で、選択中以外の行はグレーアウトする。タイトルをタップすると本文を展開する。
 * 手打ち送信時は、送信直前にテンプレート登録を確認するダイアログを挟む (§35.2.2)。
 */
@Composable
fun ClaudeSendDialog(
    templates: List<TextTemplate>,
    photoCount: Int,
    excludedCount: Int,
    isConnected: Boolean,
    freeTextHistory: List<String>,
    onDismiss: () -> Unit,
    onSendTemplate: (TextTemplate, String) -> Unit,
    onSendCustom: (promptText: String, queue: String, registerTemplateName: String?) -> Unit
) {
    var useCustom by remember { mutableStateOf(templates.isEmpty()) }
    var selectedId by remember { mutableStateOf(templates.firstOrNull()?.id) }
    val selected = templates.find { it.id == selectedId }
    var expandedBodyId by remember { mutableStateOf<String?>(null) }
    var customText by remember { mutableStateOf("") }
    var queue by remember { mutableStateOf("patrol") }
    var showRegisterConfirm by remember { mutableStateOf(false) }
    var showRegisterName by remember { mutableStateOf(false) }
    var registerName by remember { mutableStateOf("") }
    var registerError by remember { mutableStateOf<String?>(null) }
    val queueOptions = listOf(
        Triple("patrol", "巡回", "承認後に自動で処理されます"),
        Triple("manual", "手動", "チャットで呼び出してすぐ実行します"),
        Triple("waiting", "待機", "あとで巡回・手動へ送ります（承認はまだ届きません）")
    )
    val customTrimmed = customText.trim()
    val customValid = customTrimmed.isNotEmpty() && customTrimmed.length <= 4000
    val sendEnabled = isConnected && if (useCustom) customValid else selected != null

    if (showRegisterConfirm) {
        AlertDialog(
            onDismissRequest = { showRegisterConfirm = false },
            title = { Text("テンプレートに登録しますか？") },
            text = { Text("このプロンプトをテンプレートに登録すると、次回から一覧で選べるようになります。登録しなくても送信はできます。", fontSize = 13.sp) },
            confirmButton = {
                TextButton(onClick = {
                    registerName = customTrimmed.take(20)
                    registerError = null
                    showRegisterConfirm = false
                    showRegisterName = true
                }) {
                    Text("登録する")
                }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = { showRegisterConfirm = false }) {
                        Text("キャンセル")
                    }
                    TextButton(onClick = {
                        showRegisterConfirm = false
                        onSendCustom(customTrimmed, queue, null)
                    }) {
                        Text("登録しないで送信")
                    }
                }
            }
        )
    }

    if (showRegisterName) {
        AlertDialog(
            onDismissRequest = { showRegisterName = false },
            title = { Text("テンプレートのタイトル") },
            text = {
                Column {
                    OutlinedTextField(
                        value = registerName,
                        onValueChange = { registerName = it; registerError = null },
                        singleLine = true,
                        label = { Text("タイトル") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    val err = registerError
                    if (err != null) {
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(err, fontSize = 12.sp, color = MaterialTheme.colorScheme.error)
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val name = registerName.trim()
                    when {
                        name.isEmpty() -> registerError = "タイトルを入力してください"
                        templates.any { it.name.trim().equals(name, ignoreCase = true) } ->
                            registerError = "同名のテンプレートが既にあります: $name"
                        else -> {
                            showRegisterName = false
                            onSendCustom(customTrimmed, queue, name)
                        }
                    }
                }) {
                    Text("登録して送信")
                }
            },
            dismissButton = {
                TextButton(onClick = {
                    showRegisterName = false
                    showRegisterConfirm = true
                }) {
                    Text("戻る")
                }
            }
        )
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Museに送る") },
        text = {
            Column(modifier = Modifier.verticalScroll(rememberScrollState())) {
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
                Text("プロンプト（タイトルを押すと本文を表示）", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                // 先頭行: 手打ち (§35.2.1)
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier
                        .fillMaxWidth()
                        .alpha(if (useCustom) 1f else 0.55f)
                        .padding(vertical = 2.dp)
                ) {
                    Checkbox(
                        checked = useCustom,
                        onCheckedChange = { useCustom = true }
                    )
                    Text("プロンプトを手打ち", fontSize = 13.sp, fontWeight = FontWeight.Bold)
                }
                if (useCustom) {
                    OutlinedTextField(
                        value = customText,
                        onValueChange = { customText = it },
                        label = { Text("プロンプト") },
                        minLines = 4,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Text(
                        "${customTrimmed.length} / 4,000 文字",
                        fontSize = 11.sp,
                        color = if (customTrimmed.length > 4000) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    if (freeTextHistory.isNotEmpty()) {
                        Spacer(modifier = Modifier.height(4.dp))
                        Text("前回の手打ちを再利用", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        freeTextHistory.forEach { h ->
                            TextButton(onClick = { customText = h }) {
                                Text(
                                    h.replace("\n", " ").take(24),
                                    fontSize = 12.sp,
                                    maxLines = 1
                                )
                            }
                        }
                    }
                }
                // テンプレート行 (排他チェック + タイトルタップで本文展開)
                templates.forEach { tpl ->
                    val checked = !useCustom && selectedId == tpl.id
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .alpha(if (checked) 1f else 0.55f)
                            .padding(vertical = 2.dp)
                    ) {
                        Checkbox(
                            checked = checked,
                            onCheckedChange = {
                                selectedId = tpl.id
                                useCustom = false
                            }
                        )
                        Text(
                            tpl.name,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Bold,
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    expandedBodyId = if (expandedBodyId == tpl.id) null else tpl.id
                                }
                                .padding(vertical = 8.dp)
                        )
                    }
                    if (expandedBodyId == tpl.id) {
                        Text(
                            tpl.content,
                            fontSize = 12.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(start = 48.dp, end = 8.dp, bottom = 6.dp)
                        )
                    }
                }
                if (templates.isEmpty()) {
                    Text(
                        "プロンプトテンプレートは未登録です（設定画面で登録できます）。手打ちはそのまま使えます。",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                Spacer(modifier = Modifier.height(12.dp))
                Text("送り先", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                queueOptions.forEach { (value, label, desc) ->
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { queue = value }
                            .padding(vertical = 2.dp)
                    ) {
                        RadioButton(
                            selected = queue == value,
                            onClick = { queue = value }
                        )
                        Column {
                            Text(label, fontSize = 13.sp, fontWeight = FontWeight.Bold)
                            Text(desc, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
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
                onClick = {
                    if (useCustom) {
                        showRegisterConfirm = true
                    } else {
                        selected?.let { onSendTemplate(it, queue) }
                    }
                },
                enabled = sendEnabled
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
