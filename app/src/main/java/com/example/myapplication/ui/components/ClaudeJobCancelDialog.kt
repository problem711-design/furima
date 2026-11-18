package com.example.myapplication.ui.components

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.myapplication.data.server.ClaudeJobDto
import com.example.myapplication.ui.screens.claudeJobStatusText

// フェーズ 6-10 (§37.4): ラベルチップ / プレビューから開くジョブ取消ダイアログ。
// 取消はジョブ全体に効くこと、返却済みの結果は残ることを明示し、確定ボタンで確認を経る。
@Composable
fun ClaudeJobCancelDialog(
    job: ClaudeJobDto,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("ジョブの取り消し", fontWeight = FontWeight.Bold) },
        text = {
            Column {
                Text("テンプレート「${job.templateName.ifBlank { "(名称なし)" }}」", fontSize = 14.sp)
                Text("写真 ${job.photos.size} 枚 / 現在の状態: ${claudeJobStatusText(job.status)}", fontSize = 14.sp)
                Spacer(Modifier.height(8.dp))
                Text(
                    "取り消しはこのジョブ全体に効きます。まだ処理されていない写真は加工されません。すでに返却済みの加工結果はそのまま残ります。",
                    fontSize = 12.sp,
                    color = Color.Gray
                )
            }
        },
        confirmButton = {
            Button(
                onClick = onConfirm,
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFFE11D48))
            ) {
                Text("ジョブを取り消す")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text("戻る") }
        }
    )
}
