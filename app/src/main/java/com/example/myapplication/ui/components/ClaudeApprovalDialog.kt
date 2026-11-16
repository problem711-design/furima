package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.server.ClaudeJobDto

/**
 * 「Museに送る」実行承認ダイアログ (フェーズ 5-4)。
 * MainActivity のアプリレベルオーバーレイから表示され、どの画面にいても承認できる。
 * 承認はフリマ管理アプリがフォアグラウンドの間にだけ届く (ハートビート経由)。
 * 外側タップや戻る操作では閉じず、承認か拒否の明示操作を求める。
 */
@Composable
fun ClaudeApprovalDialog(
    job: ClaudeJobDto,
    onApprove: () -> Unit,
    onDeny: () -> Unit
) {
    val elapsedText = remember(job.approvalRequestedAt) {
        if (job.approvalRequestedAt > 0L) {
            val minutes = ((System.currentTimeMillis() - job.approvalRequestedAt) / 60000L).coerceAtLeast(0L)
            if (minutes <= 0L) "たった今" else "約${minutes}分前"
        } else {
            ""
        }
    }

    AlertDialog(
        onDismissRequest = { /* 承認/拒否の明示操作が必要なため、自動では閉じない */ },
        title = { Text("Claude実行の承認リクエスト") },
        text = {
            Column {
                Text(
                    "「Museに送る」ジョブの実行承認が求められています。",
                    fontSize = 13.sp
                )
                Spacer(modifier = Modifier.height(10.dp))
                Text("商品: ${job.itemTitle}", fontWeight = FontWeight.Bold)
                Text("写真: ${job.photos.size}枚")
                Text("テンプレート: ${job.templateName}")
                if (job.folderName.isNotBlank()) {
                    Text("返却先フォルダ: ${job.folderName}")
                }
                Text("送信元: ${if (job.origin == "phone") "スマホ" else "PC"}")
                if (elapsedText.isNotBlank()) {
                    Text("リクエスト: $elapsedText")
                }
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    "承認すると、Claudeがこのジョブの写真を取得して加工を開始します。",
                    fontSize = 12.sp
                )
            }
        },
        confirmButton = {
            Button(onClick = onApprove) {
                Text("実行を承認")
            }
        },
        dismissButton = {
            TextButton(onClick = onDeny) {
                Text("拒否")
            }
        }
    )
}
