package com.example.myapplication.data.server

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject

internal class PcClaudeApiClient(
    private val transport: PcServerTransport
) {
    private val client get() = transport.client
    private val createClaudeJobRequestAdapter get() = transport.createClaudeJobRequestAdapter
    private val claudeJobResponseAdapter get() = transport.claudeJobResponseAdapter
    private val claudeLabelSeenBatchRequestAdapter get() = transport.claudeLabelSeenBatchRequestAdapter
    private val claudeLabelSeenResponseAdapter get() = transport.claudeLabelSeenResponseAdapter
    private val claudeJobListResponseAdapter get() = transport.claudeJobListResponseAdapter
    private val claudeReferencesResponseAdapter get() = transport.claudeReferencesResponseAdapter
    private val claudeApprovalRequestAdapter get() = transport.claudeApprovalRequestAdapter

    private fun getSavedServerUrl(): String = transport.getSavedServerUrl()
    private fun getSavedAuthToken(): String = transport.getSavedAuthToken()
    private fun pushClaudeFreeTextHistory(text: String) = transport.pushClaudeFreeTextHistory(text)

    // フェーズ 6-8: promptText を渡すと手打ちジョブになる (templateId より優先される)。
    suspend fun createClaudeJob(
        itemId: Int,
        itemUuid: String,
        photos: List<Pair<String, String>>,
        templateId: String,
        queue: String = "patrol",
        promptText: String? = null,
        customPromptName: String? = null,
        registerTemplateName: String? = null,
        referenceMd5: String = "",
        stampNumber: Boolean = true,
        referenceFileName: String = "",
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): PcServerClient.ClaudeJobCreateResult = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) {
                return@withContext PcServerClient.ClaudeJobCreateResult(false, message = "PCサーバーのURLが未設定です")
            }
            if (itemUuid.isBlank()) {
                return@withContext PcServerClient.ClaudeJobCreateResult(false, message = "商品の同期IDが未確定です（先にPCと同期してください）")
            }
            if (photos.isEmpty()) {
                return@withContext PcServerClient.ClaudeJobCreateResult(false, message = "送信する写真がありません")
            }
            val reqDto = CreateClaudeJobRequestDto(
                itemId = itemId,
                itemUuid = itemUuid,
                photos = photos.map { ClaudeJobPhotoDto(md5 = it.first, fileName = it.second) },
                templateId = templateId,
                origin = "phone",
                queue = queue,
                promptText = promptText,
                customPromptName = customPromptName,
                registerTemplateName = registerTemplateName,
                referenceMd5 = referenceMd5,
                stampNumber = stampNumber,
                referenceFileName = referenceFileName
            )
            val body = createClaudeJobRequestAdapter.toJson(reqDto)
                .toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                val respStr = response.body?.string()
                val parsed = if (!respStr.isNullOrBlank()) {
                    try { claudeJobResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
                if (response.isSuccessful && parsed?.status == "ok" && parsed.job != null) {
                    PcServerClient.ClaudeJobCreateResult(
                        true,
                        jobId = parsed.job.jobId,
                        templateRegistered = parsed.templateRegistered,
                        templateRegisterError = parsed.templateRegisterError
                    )
                } else {
                    PcServerClient.ClaudeJobCreateResult(false, message = parsed?.message ?: "サーバーエラー (${response.code})")
                }
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "createClaudeJob 例外: ${e.message}", e)
            PcServerClient.ClaudeJobCreateResult(false, message = "PCサーバーへ接続できませんでした")
        }
    }

    // Claude ジョブ一覧を取得する (フェーズ 5-4)。失敗時は null を返し、呼び出し側で状態を維持する。
    // レビュー 24-4: ジョブ一覧と変化カウンタをセットで返す (反映側でカウンタも進めるため)
    suspend fun fetchClaudeJobs(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeJobListResponseDto? = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext null
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val respStr = response.body?.string()
                if (respStr.isNullOrBlank()) return@withContext null
                val parsed = try { claudeJobListResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                if (parsed?.status == "ok") parsed else null
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "fetchClaudeJobs 例外: ${e.message}", e)
            null
        }
    }

    // フェーズ 6-13 (§47.10): 参考画像コレクションの一覧を取得する (スマホは取得・選択のみ)。
    suspend fun fetchClaudeReferences(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeReferencesResponseDto? = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext null
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/references").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val respStr = response.body?.string()
                if (respStr.isNullOrBlank()) return@withContext null
                val parsed = try { claudeReferencesResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                if (parsed?.status == "ok") parsed else null
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "fetchClaudeReferences 例外: ${e.message}", e)
            null
        }
    }

    // フェーズ 6-13 (§47.11-3): 参考画像の表示用 URL と認証トークンを返す。
    // PC サーバーの全ルートは Bearer 必須のため、Coil のリクエストに Authorization ヘッダーを付ける用途。
    fun claudeReferenceImageUrl(md5: String, thumb: Boolean): Pair<String, String>? {
        val cleanUrl = getSavedServerUrl().trimEnd('/')
        if (cleanUrl.isBlank() || !cleanUrl.startsWith("http") || md5.isBlank()) return null
        val path = if (thumb) "/api/claude/references/$md5/thumb" else "/api/claude/references/$md5"
        return (cleanUrl + path) to getSavedAuthToken()
    }

    // 変化カウンタだけを取得する軽量窓口 (フェーズ 6-3 / レビュー 24-4)。
    // 認証不要・サーバー側の最終接続時刻も更新しないため、独立監視の定期確認に使う。
    suspend fun fetchClaudeRevision(
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Long? = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext null
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/revision").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val respStr = response.body?.string()
                if (respStr.isNullOrBlank()) return@withContext null
                val v = org.json.JSONObject(respStr).optLong("claudeRevision", -1L)
                if (v >= 0L) v else null
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            null
        }
    }

    // Claude ジョブの承認/拒否を送信する (フェーズ 5-4)。
    suspend fun postClaudeApproval(
        jobId: String,
        approved: Boolean,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val body = claudeApprovalRequestAdapter.toJson(ClaudeApprovalRequestDto(approved))
                .toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs/$jobId/approval").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                val parsed = if (!respStr.isNullOrBlank()) {
                    try { claudeJobResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
                parsed?.status == "ok"
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "postClaudeApproval 例外: ${e.message}", e)
            false
        }
    }

    // Claude ジョブの完了確認 (ack) を送信する (フェーズ 5-5)。確認状態は全端末で共有される。
    suspend fun postClaudeJobAck(
        jobId: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val body = "{}".toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs/$jobId/ack").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                val parsed = if (!respStr.isNullOrBlank()) {
                    try { claudeJobResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
                parsed?.status == "ok"
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "postClaudeJobAck 例外: ${e.message}", e)
            false
        }
    }

    // フェーズ 6-10 (§37.4): ジョブの取消 (queued / approval_pending / approved / processing が対象)
    suspend fun postClaudeJobCancel(
        jobId: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val body = "{}".toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs/$jobId/cancel").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                val parsed = if (!respStr.isNullOrBlank()) {
                    try { claudeJobResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
                parsed?.status == "ok"
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "postClaudeJobCancel 例外: ${e.message}", e)
            false
        }
    }

    // フェーズ 6-5 (是正 Build 8): 加工済みラベルの一括既読化 (商品を開いたときに呼ぶ)。
    // 応答にはサーバー確定の seenAt と labelSeenRevision が含まれ、呼び出し側で楽観値を確定させる。
    // 失敗しても本処理は止めない (null を返す)。
    suspend fun postClaudeLabelSeen(
        itemUuids: List<String>,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): ClaudeLabelSeenResponseDto? = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            val targets = itemUuids.map { it.trim() }.filter { it.isNotEmpty() }
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http") || targets.isEmpty()) return@withContext null
            val bodyJson = claudeLabelSeenBatchRequestAdapter.toJson(ClaudeLabelSeenBatchRequestDto(targets))
            val body = bodyJson.toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/labels/seen").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext null
                val respStr = response.body?.string()
                if (!respStr.isNullOrBlank()) {
                    try { claudeLabelSeenResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            null
        }
    }

    // Claude ジョブの所属リスト移動 (フェーズ 6-1a の move API / 6-2 の一括承認で未チェック分を待機へ戻すために使用)。
    suspend fun postClaudeJobMove(
        jobId: String,
        queue: String,
        baseUrl: String = getSavedServerUrl(),
        token: String = getSavedAuthToken()
    ): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            // queue は内部定数 (patrol / manual / waiting) のみを渡す前提で JSON を直接組み立てる
            val body = "{\"queue\":\"$queue\"}".toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/claude/jobs/$jobId/move").post(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                val parsed = if (!respStr.isNullOrBlank()) {
                    try { claudeJobResponseAdapter.fromJson(respStr) } catch (e: Exception) { null }
                } else null
                parsed?.status == "ok"
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "postClaudeJobMove 例外: ${e.message}", e)
            false
        }
    }

}
