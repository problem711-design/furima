package com.example.myapplication.ui.screens

import android.net.Uri
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.sp
import java.text.NumberFormat
import java.util.Locale

internal sealed class DeletedEditPhoto(open val index: Int) {
    data class Existing(override val index: Int, val path: String) : DeletedEditPhoto(index)
    data class NewUri(override val index: Int, val uri: Uri) : DeletedEditPhoto(index)
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TemplateInsertDropdown(
    placeholder: String,
    templates: List<com.example.myapplication.data.local.TextTemplate>,
    dropdownTag: String,
    itemTagPrefix: String,
    onTemplateSelected: (com.example.myapplication.data.local.TextTemplate) -> Unit
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(
        expanded = expanded,
        onExpandedChange = { expanded = !expanded },
        modifier = Modifier.fillMaxWidth()
    ) {
        OutlinedTextField(
            value = placeholder,
            onValueChange = {},
            readOnly = true,
            singleLine = true,
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
            textStyle = MaterialTheme.typography.bodySmall,
            modifier = Modifier
                .menuAnchor()
                .fillMaxWidth()
                .testTag(dropdownTag)
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
                                text = tpl.content.replace("\n", " "),
                                fontSize = 11.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis
                            )
                        }
                    },
                    onClick = {
                        expanded = false
                        onTemplateSelected(tpl)
                    },
                    modifier = Modifier.testTag("${itemTagPrefix}${tpl.id}")
                )
            }
        }
    }
}

internal fun expandTemplatePlaceholders(templateText: String, title: String, priceText: String): String {
    val parsedPrice = priceText.toIntOrNull() ?: 0
    val formattedPrice = NumberFormat.getNumberInstance(Locale.JAPAN).format(parsedPrice)
    return templateText
        .replace("{title}", title.trim())
        .replace("{price}", formattedPrice)
}

internal fun insertTemplateAtCursor(current: TextFieldValue, insertText: String): TextFieldValue {
    val start = current.selection.min.coerceIn(0, current.text.length)
    val end = current.selection.max.coerceIn(0, current.text.length)
    val updatedText = current.text.substring(0, start) + insertText + current.text.substring(end)
    val newCursor = start + insertText.length
    return TextFieldValue(text = updatedText, selection = TextRange(newCursor))
}

// フェーズ 6-5: 加工ラベルの表示名と色 (PC 管理画面のパレットに合わせる)
// フェーズ 6-10 (§37.1): 開始待ち (startwait) を追加
fun claudeLabelText(key: String): String = when (key) {
    "error" -> "エラー"
    "processing" -> "加工中"
    "approval" -> "承認待ち"
    "startwait" -> "開始待ち"
    "waiting" -> "待機中"
    "done" -> "加工済み"
    else -> ""
}

fun claudeLabelColor(key: String): Color = when (key) {
    "error" -> Color(0xFFE11D48)
    "processing" -> Color(0xFF2563EB)
    "approval" -> Color(0xFFFBBF24)
    "startwait" -> Color(0xFF0284C7)
    "waiting" -> Color(0xFF64748B)
    "done" -> Color(0xFF059669)
    else -> Color.Gray
}

// フェーズ 6-10 (§37.4): 取消ダイアログ等で使うジョブ状態の表示名 (ラベル導出と同じ語に揃える)
fun claudeJobStatusText(status: String): String = when (status) {
    "queued" -> "待機中"
    "approval_pending" -> "承認待ち"
    "approved" -> "開始待ち"
    "processing" -> "加工中"
    else -> status
}
