package com.example.myapplication.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.example.myapplication.data.server.ClaudeJobDto
import java.io.File

/**
 * 「Museに送る」一括承認ダイアログ (フェーズ 6-2)。
 * MainActivity のアプリレベルオーバーレイから表示される。承認待ちを古い順に最大10件まで
 * チェックボックス付きで並べ、チェック分をまとめて承認する。チェックを外したジョブは
 * 待機リストへ戻る (サーバー側で移動 API が呼ばれる)。拒否ボタンは置かない。
 * チェック状態は「外したジョブID集合」で保持し、表示中に新しい承認待ちが届いても
 * 外した状態が維持される (新着はチェック済みで現れる)。
 * 外側タップや戻る操作では閉じない。
 */
@Composable
fun ClaudeApprovalDialog(
    jobs: List<ClaudeJobDto>,
    totalPendingCount: Int,
    thumbnails: Map<String, String>,
    isProcessing: Boolean = false,
    onRespond: (checkedIds: Set<String>, displayedIds: List<String>) -> Unit,
    onCloseForNow: () -> Unit = {}
) {
    val displayedIds = jobs.map { it.jobId }
    var uncheckedIds by remember { mutableStateOf<Set<String>>(emptySet()) }
    LaunchedEffect(displayedIds) {
        // 消滅したジョブの ID は集合から掃除する
        uncheckedIds = uncheckedIds.intersect(displayedIds.toSet())
    }
    val checkedIds = displayedIds.filter { !uncheckedIds.contains(it) }.toSet()

    AlertDialog(
        onDismissRequest = { /* 承認/待機戻しの明示操作が必要なため、自動では閉じない */ },
        title = { Text("Muse実行の承認リクエスト") },
        text = {
            Column {
                Text(
                    "「Museに送る」ジョブの実行承認が求められています。承認待ち ${totalPendingCount} 件のうち、古い順に ${jobs.size} 件を表示しています。",
                    fontSize = 13.sp
                )
                if (totalPendingCount > jobs.size) {
                    Text(
                        "残り ${totalPendingCount - jobs.size} 件は今回の対象に含まれません（承認待ちのまま残ります）。",
                        fontSize = 12.sp
                    )
                }
                Spacer(modifier = Modifier.height(8.dp))
                Column(
                    modifier = Modifier
                        .heightIn(max = 400.dp)
                        .verticalScroll(rememberScrollState())
                ) {
                    jobs.forEach { job ->
                        val elapsedText = if (job.approvalRequestedAt > 0L) {
                            val minutes = ((System.currentTimeMillis() - job.approvalRequestedAt) / 60000L).coerceAtLeast(0L)
                            if (minutes <= 0L) "たった今" else "約${minutes}分前"
                        } else {
                            ""
                        }
                        val queueLabel = when (job.queue) {
                            "patrol" -> "巡回"
                            "manual" -> "手動"
                            else -> job.queue
                        }
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(vertical = 4.dp)
                        ) {
                            Checkbox(
                                checked = !uncheckedIds.contains(job.jobId),
                                onCheckedChange = { checked ->
                                    uncheckedIds = if (checked) {
                                        uncheckedIds - job.jobId
                                    } else {
                                        uncheckedIds + job.jobId
                                    }
                                },
                                enabled = !isProcessing
                            )
                            val thumbPath = thumbnails[job.jobId]
                            if (thumbPath != null) {
                                AsyncImage(
                                    model = File(thumbPath),
                                    contentDescription = null,
                                    contentScale = ContentScale.Crop,
                                    modifier = Modifier
                                        .size(48.dp)
                                        .clip(RoundedCornerShape(6.dp))
                                )
                            } else {
                                Box(
                                    modifier = Modifier
                                        .size(48.dp)
                                        .clip(RoundedCornerShape(6.dp))
                                        .background(MaterialTheme.colorScheme.surfaceVariant),
                                    contentAlignment = Alignment.Center
                                ) {
                                    Text(
                                        "写真",
                                        fontSize = 10.sp,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant
                                    )
                                }
                            }
                            Spacer(modifier = Modifier.width(8.dp))
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    job.itemTitle,
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 13.sp,
                                    maxLines = 2
                                )
                                Text(
                                    "写真${job.photos.size}枚・${job.templateName}・${queueLabel}",
                                    fontSize = 11.sp
                                )
                                Text(
                                    "送信元: ${if (job.origin == "phone") "スマホ" else "PC"}" +
                                        if (elapsedText.isNotBlank()) "・${elapsedText}" else "",
                                    fontSize = 11.sp
                                )
                            }
                        }
                    }
                }
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    "チェックを外したジョブは待機リストへ戻ります（あとからPCの一覧で送れます）。取りやめたいジョブはチェックを外して待機へ戻し、PCのジョブ一覧から取り消してください。",
                    fontSize = 12.sp
                )
                Text(
                    "「いったん閉じる」を選んでもサーバー側の状態は変わりません（承認待ちは10分で自動的に待機へ戻ります）。",
                    fontSize = 12.sp
                )
            }
        },
        confirmButton = {
            Button(
                onClick = { onRespond(checkedIds, displayedIds) },
                enabled = !isProcessing
            ) {
                Text(
                    if (isProcessing) {
                        "送信中…"
                    } else if (checkedIds.isEmpty()) {
                        "全て待機へ戻す"
                    } else {
                        "承認する（${checkedIds.size}件）"
                    }
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onCloseForNow, enabled = !isProcessing) {
                Text("いったん閉じる")
            }
        }
    )
}
