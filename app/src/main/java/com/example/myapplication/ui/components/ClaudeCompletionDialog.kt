package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.server.ClaudeJobDto

/**
 * 「Museに送る」完了通知ダイアログ (フェーズ 5-5)。
 * MainActivity のアプリレベルオーバーレイから表示される。
 * 「確認」を押すとサーバーへ ack が送られ、PC・スマホ・全端末の未確認表示が同時に消える。
 * 返却された写真そのものは既存のデータPullで自動的に届く。
 */
@Composable
fun ClaudeCompletionDialog(
    job: ClaudeJobDto,
    remainingCount: Int,
    onAcknowledge: () -> Unit
) {
    AlertDialog(
        onDismissRequest = { /* 確認ボタンでのみ閉じる (未確認のまま流れないように) */ },
        title = { Text("画像加工が完了しました") },
        text = {
            Column {
                Text("商品: ${job.itemTitle}", fontWeight = FontWeight.Bold)
                Spacer(modifier = Modifier.height(6.dp))
                Text("成功: ${job.results.size}枚")
                if (job.errors.isNotEmpty()) {
                    Text("失敗: ${job.errors.size}枚")
                }
                Text("テンプレート: ${job.templateName}", fontSize = 12.sp)
                Spacer(modifier = Modifier.height(8.dp))
                Text(
                    "加工済みの写真は元の商品フォルダへ返却され、同期でこの端末にも届きます。",
                    fontSize = 12.sp
                )
                if (remainingCount > 1) {
                    Spacer(modifier = Modifier.height(6.dp))
                    Text("他に未確認の完了が ${remainingCount - 1} 件あります。", fontSize = 12.sp)
                }
            }
        },
        confirmButton = {
            Button(onClick = onAcknowledge) {
                Text("確認")
            }
        }
    )
}
