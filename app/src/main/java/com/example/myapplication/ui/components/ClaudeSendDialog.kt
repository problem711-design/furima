package com.example.myapplication.ui.components

import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import coil.compose.AsyncImage
import coil.request.ImageRequest
import com.example.myapplication.data.local.TextTemplate
import com.example.myapplication.data.server.ClaudeReferenceDto

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
    onFetchReferences: (suspend () -> List<ClaudeReferenceDto>)? = null,
    referenceImageUrl: ((md5: String, thumb: Boolean) -> Pair<String, String>?)? = null,
    onSendTemplate: (TextTemplate, String, ClaudeReferenceDto?) -> Unit,
    onSendCustom: (promptText: String, queue: String, registerTemplateName: String?, ClaudeReferenceDto?) -> Unit
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
    // フェーズ 6-13 (§47.10): 参考画像の添付 (チェック ON でピッカーを開く。管理は PC のみ)
    var attachReference by remember { mutableStateOf(false) }
    var selectedReference by remember { mutableStateOf<ClaudeReferenceDto?>(null) }
    var showReferencePicker by remember { mutableStateOf(false) }
    var pickerReferences by remember { mutableStateOf<List<ClaudeReferenceDto>>(emptyList()) }
    var pickerLoading by remember { mutableStateOf(false) }
    var pickerPreview by remember { mutableStateOf<ClaudeReferenceDto?>(null) }
    val dialogContext = LocalContext.current
    fun referenceImageModel(md5: String, thumb: Boolean): ImageRequest? {
        val pair = referenceImageUrl?.invoke(md5, thumb) ?: return null
        val builder = ImageRequest.Builder(dialogContext).data(pair.first)
        if (pair.second.isNotBlank()) builder.addHeader("Authorization", "Bearer ${pair.second}")
        return builder.build()
    }

    LaunchedEffect(showReferencePicker) {
        if (showReferencePicker) {
            pickerLoading = true
            pickerReferences = try {
                onFetchReferences?.invoke() ?: emptyList()
            } catch (e: Exception) {
                emptyList()
            }
            pickerLoading = false
        }
    }
    val queueOptions = listOf(
        Triple("patrol", "巡回", "承認後に自動で処理されます"),
        Triple("manual", "手動", "チャットで呼び出してすぐ実行します"),
        Triple("waiting", "待機", "あとで巡回・手動へ送ります（承認はまだ届きません）")
    )
    val customTrimmed = customText.trim()
    val customValid = customTrimmed.isNotEmpty() && customTrimmed.length <= 4000
    val sendEnabled = isConnected && (!attachReference || selectedReference != null) &&
        if (useCustom) customValid else selected != null

    if (showRegisterConfirm) {
        AlertDialog(
            onDismissRequest = { showRegisterConfirm = false },
            title = { Text("テンプレートに登録しますか？") },
            text = { Text("このプロンプトをテンプレートに登録すると、次回から一覧で選べるようになります。登録しなくても送信はできます。", fontSize = 13.sp) },
            confirmButton = {
                TextButton(onClick = {
                    // レビュー 67-3: 仮入れタイトルは改行を空白へ置換してから先頭 20 文字を取る
                    registerName = customTrimmed.replace("\n", " ").replace("\r", " ").take(20)
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
                        onSendCustom(customTrimmed, queue, null, selectedReference)
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
                    // レビュー 67-3: 確定時も改行を空白へ正規化する (貼り付け対策)
                    val name = registerName.replace("\n", " ").replace("\r", " ").trim()
                    when {
                        name.isEmpty() -> registerError = "タイトルを入力してください"
                        templates.any { it.name.trim().equals(name, ignoreCase = true) } ->
                            registerError = "同名のテンプレートが既にあります: $name"
                        else -> {
                            showRegisterName = false
                            onSendCustom(customTrimmed, queue, name, selectedReference)
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

    // フェーズ 6-13 (§47.9-2 必須要件): 参考画像ピッカー。
    // サムネイルのグリッド表示、タップで原寸プレビュー、プレビューから選択確定ができること。
    if (showReferencePicker) {
        AlertDialog(
            onDismissRequest = {
                showReferencePicker = false
                if (selectedReference == null) attachReference = false
            },
            title = { Text("参考画像を選ぶ") },
            text = {
                when {
                    pickerLoading -> {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(160.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            CircularProgressIndicator()
                        }
                    }
                    pickerReferences.isEmpty() -> {
                        Text(
                            "参考画像がありません。PCの管理画面（Muse連携の「参考画像」）から登録してください。",
                            fontSize = 13.sp
                        )
                    }
                    else -> {
                        LazyVerticalGrid(
                            columns = GridCells.Fixed(3),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(360.dp)
                        ) {
                            items(pickerReferences, key = { it.md5 }) { ref ->
                                Column(
                                    modifier = Modifier.clickable { pickerPreview = ref }
                                ) {
                                    AsyncImage(
                                        model = referenceImageModel(ref.md5, true),
                                        contentDescription = ref.displayName,
                                        contentScale = ContentScale.Crop,
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .height(84.dp)
                                    )
                                    Text(
                                        ref.displayName.ifBlank { ref.fileName },
                                        fontSize = 10.sp,
                                        maxLines = 1
                                    )
                                }
                            }
                        }
                    }
                }
            },
            confirmButton = {},
            dismissButton = {
                TextButton(onClick = {
                    showReferencePicker = false
                    if (selectedReference == null) attachReference = false
                }) {
                    Text("閉じる")
                }
            }
        )
    }

    // 参考画像の原寸プレビュー (高解像度をその場で取得して表示する)
    val previewRef = pickerPreview
    if (previewRef != null) {
        Dialog(onDismissRequest = { pickerPreview = null }) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(16.dp)
            ) {
                AsyncImage(
                    model = referenceImageModel(previewRef.md5, false),
                    contentDescription = previewRef.displayName,
                    contentScale = ContentScale.Fit,
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f)
                )
                Text(
                    previewRef.displayName.ifBlank { previewRef.fileName },
                    fontSize = 13.sp,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Row {
                    TextButton(onClick = { pickerPreview = null }) {
                        Text("戻る")
                    }
                    Spacer(modifier = Modifier.weight(1f))
                    TextButton(onClick = {
                        selectedReference = previewRef
                        attachReference = true
                        pickerPreview = null
                        showReferencePicker = false
                    }) {
                        Text("この画像にする")
                    }
                }
            }
        }
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
                        .clickable { useCustom = true }
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
                // フェーズ 6-13 (§47.10): 参考画像の添付 (チェック ON でピッカーを開く)
                if (onFetchReferences != null) {
                    Spacer(modifier = Modifier.height(12.dp))
                    Text("参考画像（任意）", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                attachReference = !attachReference
                                if (attachReference) {
                                    showReferencePicker = true
                                } else {
                                    selectedReference = null
                                }
                            }
                            .padding(vertical = 2.dp)
                    ) {
                        Checkbox(
                            checked = attachReference,
                            onCheckedChange = { checked ->
                                attachReference = checked
                                if (checked) {
                                    showReferencePicker = true
                                } else {
                                    selectedReference = null
                                }
                            }
                        )
                        Text("参考画像を添付する", fontSize = 13.sp, fontWeight = FontWeight.Bold)
                    }
                    if (attachReference) {
                        val ref = selectedReference
                        if (ref == null) {
                            Text(
                                "未選択です。選ぶまで送信できません。",
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.error
                            )
                            TextButton(onClick = { showReferencePicker = true }) {
                                Text("参考画像を選ぶ")
                            }
                        } else {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                AsyncImage(
                                    model = referenceImageModel(ref.md5, true),
                                    contentDescription = ref.displayName,
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier.size(48.dp)
                                )
                                Text(
                                    ref.displayName.ifBlank { ref.fileName },
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold,
                                    modifier = Modifier
                                        .weight(1f)
                                        .padding(start = 8.dp)
                                )
                                TextButton(onClick = { showReferencePicker = true }) {
                                    Text("変更")
                                }
                                TextButton(onClick = {
                                    selectedReference = null
                                    attachReference = false
                                }) {
                                    Text("外す")
                                }
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
                onClick = {
                    if (useCustom) {
                        showRegisterConfirm = true
                    } else {
                        selected?.let { onSendTemplate(it, queue, selectedReference) }
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
