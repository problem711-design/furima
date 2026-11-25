package com.example.myapplication.data.server

import android.content.Context
import com.example.myapplication.data.FleaMarketRepository
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.example.myapplication.data.local.TextTemplate
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.util.concurrent.TimeUnit

internal class PcServerTransport(
    val context: Context,
    val repository: FleaMarketRepository
) {
    val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build()

    val prefs = context.getSharedPreferences("pc_sync_prefs", Context.MODE_PRIVATE)

    val moshi = Moshi.Builder()
        .addLast(KotlinJsonAdapterFactory())
        .build()

    val syncItemsRequestAdapter = moshi.adapter(SyncItemsRequest::class.java)
    val syncItemsResponseAdapter = moshi.adapter(SyncItemsResponse::class.java)
    val syncPhotoResponseAdapter = moshi.adapter(SyncPhotoResponse::class.java)
    val syncSettingsAdapter = moshi.adapter(SyncSettingsDto::class.java)
    val createClaudeJobRequestAdapter = moshi.adapter(CreateClaudeJobRequestDto::class.java)
    val claudeJobResponseAdapter = moshi.adapter(ClaudeJobResponseDto::class.java)
    val claudeLabelSeenBatchRequestAdapter = moshi.adapter(ClaudeLabelSeenBatchRequestDto::class.java)
    val claudeLabelSeenResponseAdapter = moshi.adapter(ClaudeLabelSeenResponseDto::class.java)
    val claudeJobListResponseAdapter = moshi.adapter(ClaudeJobListResponseDto::class.java)
    val claudeReferencesResponseAdapter = moshi.adapter(ClaudeReferencesResponseDto::class.java)
    val claudeApprovalRequestAdapter = moshi.adapter(ClaudeApprovalRequestDto::class.java)

    internal fun toSyncSettingsDto(settings: DefaultSettings): SyncSettingsDto {
        return SyncSettingsDto(
            defaultFeeRatePercent = settings.defaultFeeRatePercent,
            defaultShippingCost = settings.defaultShippingCost,
            defaultExpenseCost = settings.defaultExpenseCost,
            activePresetId = settings.activePresetId,
            presets = settings.presets.map { p ->
                SyncFeePresetDto(
                    id = p.id,
                    name = p.name,
                    feeRatePercent = p.feeRatePercent,
                    shippingCost = p.shippingCost,
                    expenseCost = p.expenseCost
                )
            },
            titleTemplates = settings.titleTemplates.map { t ->
                SyncTextTemplateDto(id = t.id, name = t.name, content = t.content)
            },
            descriptionTemplates = settings.descriptionTemplates.map { t ->
                SyncTextTemplateDto(id = t.id, name = t.name, content = t.content)
            },
            promptTemplates = settings.promptTemplates.map { t ->
                SyncTextTemplateDto(id = t.id, name = t.name, content = t.content)
            },
            settingsUpdatedAt = settings.settingsUpdatedAt
        )
    }

    internal fun applyServerSettingsDto(dto: SyncSettingsDto, base: DefaultSettings): DefaultSettings {
        val mergedPresets = dto.presets.map { p ->
            FeePreset(
                id = p.id,
                name = p.name,
                feeRatePercent = p.feeRatePercent.coerceAtLeast(0),
                shippingCost = p.shippingCost.coerceAtLeast(0),
                expenseCost = p.expenseCost.coerceAtLeast(0)
            )
        }.ifEmpty { base.presets }
        val resolvedActiveId = if (mergedPresets.any { it.id == dto.activePresetId }) {
            dto.activePresetId
        } else {
            mergedPresets.firstOrNull()?.id ?: base.activePresetId
        }
        return base.copy(
            defaultFeeRatePercent = dto.defaultFeeRatePercent.coerceAtLeast(0),
            defaultShippingCost = dto.defaultShippingCost.coerceAtLeast(0),
            defaultExpenseCost = dto.defaultExpenseCost.coerceAtLeast(0),
            activePresetId = resolvedActiveId,
            presets = mergedPresets,
            titleTemplates = dto.titleTemplates.map { t ->
                TextTemplate(id = t.id, name = t.name, content = t.content)
            },
            descriptionTemplates = dto.descriptionTemplates.map { t ->
                TextTemplate(id = t.id, name = t.name, content = t.content)
            },
            promptTemplates = dto.promptTemplates.map { t ->
                TextTemplate(id = t.id, name = t.name, content = t.content)
            },
            settingsUpdatedAt = dto.settingsUpdatedAt
        )
    }

    suspend fun pushSettingsToServer(baseUrl: String = getSavedServerUrl(), token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val currentSettings = repository.defaultSettings.value
            val jsonStr = syncSettingsAdapter.toJson(toSyncSettingsDto(currentSettings))
            val body = jsonStr.toRequestBody("application/json; charset=utf-8".toMediaType())
            val reqBuilder = Request.Builder().url("$cleanUrl/api/settings").put(body)
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string()
                if (!respStr.isNullOrBlank()) {
                    val serverDto = syncSettingsAdapter.fromJson(respStr)
                    if (serverDto != null && serverDto.settingsUpdatedAt > 0L) {
                        repository.updateDefaultSettings(
                            applyServerSettingsDto(serverDto, repository.defaultSettings.value),
                            stampNow = false
                        )
                    }
                }
                true
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "pushSettingsToServer 例外: ${e.message}", e)
            false
        }
    }

    data class ClaudeJobCreateResult(
        val success: Boolean,
        val jobId: String = "",
        val message: String = "",
        // フェーズ 6-8: テンプレート登録の結果 (ジョブ作成の成否とは切り離して伝える)
        val templateRegistered: Boolean = false,
        val templateRegisterError: String? = null
    )

    // フェーズ 6-8 (§55.6-3 / §41.4): 手打ちプロンプトの履歴は端末ローカルにのみ直近 3 件を
    // 保持する (同期される設定には絶対に入れない)
    fun getClaudeFreeTextHistory(): List<String> {
        val raw = prefs.getString("claude_free_text_history", null) ?: return emptyList()
        return try {
            val arr = org.json.JSONArray(raw)
            (0 until arr.length()).map { arr.optString(it) }.filter { it.isNotBlank() }
        } catch (e: Exception) {
            emptyList()
        }
    }

    fun pushClaudeFreeTextHistory(text: String) {
        val trimmed = text.trim()
        if (trimmed.isBlank()) return
        val next = (listOf(trimmed) + getClaudeFreeTextHistory().filter { it != trimmed }).take(3)
        val arr = org.json.JSONArray()
        next.forEach { arr.put(it) }
        prefs.edit().putString("claude_free_text_history", arr.toString()).apply()
    }

    // 「Museに送る」ジョブを作成する (フェーズ 5-3)。商品特定は itemUuid (syncUuid) を必須とする (Q1)。
    // フェーズ 6-8: promptText を渡すと手打ちジョブになる (templateId より優先される)。

    suspend fun fetchAndSyncServerSettings(baseUrl: String = getSavedServerUrl(), token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val reqBuilder = Request.Builder().url("$cleanUrl/api/settings").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string() ?: return@withContext false
                val serverDto = syncSettingsAdapter.fromJson(respStr) ?: return@withContext false
                val localSettings = repository.defaultSettings.value
                when {
                    serverDto.settingsUpdatedAt > localSettings.settingsUpdatedAt -> {
                        repository.updateDefaultSettings(
                            applyServerSettingsDto(serverDto, localSettings),
                            stampNow = false
                        )
                    }
                    localSettings.settingsUpdatedAt > serverDto.settingsUpdatedAt -> {
                        pushSettingsToServer(cleanUrl, token)
                    }
                }
                true
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "fetchAndSyncServerSettings 例外: ${e.message}", e)
            false
        }
    }

    // フェーズ 6-8 是正 (レビュー 67-2): テンプレート登録直後の設定取り直し専用。
    // 通常の fetchAndSyncServerSettings はローカルが新しければ pushSettingsToServer で
    // ローカル設定を押し戻すため、時計の進んだ端末では登録したばかりのテンプレートを
    // 自分の古い promptTemplates で消してしまう。ここでは push を一切行わず、
    // サーバーが新しければ丸ごと取り込み、ローカルが新しければ promptTemplates と
    // settingsUpdatedAt だけをサーバーへ合わせて登録結果を必ず手元へ反映する
    // (他の設定項目はローカルの未同期分を保持する)。
    suspend fun fetchServerSettingsAfterTemplateRegistration(baseUrl: String = getSavedServerUrl(), token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val reqBuilder = Request.Builder().url("$cleanUrl/api/settings").get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext false
                val respStr = response.body?.string() ?: return@withContext false
                val serverDto = syncSettingsAdapter.fromJson(respStr) ?: return@withContext false
                val localSettings = repository.defaultSettings.value
                if (serverDto.settingsUpdatedAt >= localSettings.settingsUpdatedAt) {
                    repository.updateDefaultSettings(
                        applyServerSettingsDto(serverDto, localSettings),
                        stampNow = false
                    )
                } else {
                    val serverApplied = applyServerSettingsDto(serverDto, localSettings)
                    repository.updateDefaultSettings(
                        localSettings.copy(
                            promptTemplates = serverApplied.promptTemplates,
                            settingsUpdatedAt = serverDto.settingsUpdatedAt
                        ),
                        stampNow = false
                    )
                }
                true
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "fetchServerSettingsAfterTemplateRegistration 例外: ${e.message}", e)
            false
        }
    }

    fun getSavedStoreId(): String {
        return prefs.getString("pc_store_id", "") ?: ""
    }

    fun saveStoreId(storeId: String) {
        prefs.edit().putString("pc_store_id", storeId).apply()
    }

    fun getSavedAuthToken(): String {
        return prefs.getString("pc_auth_token", "") ?: ""
    }

    fun saveAuthToken(token: String) {
        prefs.edit().putString("pc_auth_token", token).apply()
    }

    fun getSavedServerUrl(): String {
        return prefs.getString("pc_server_url", "") ?: ""
    }

    fun saveServerUrl(url: String) {
        prefs.edit().putString("pc_server_url", url).apply()
    }

    suspend fun checkServerStatus(baseUrl: String, token: String = getSavedAuthToken()): Boolean = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext false
            val url = "$cleanUrl/api/server/status"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            false
        }
    }

    suspend fun sendHeartbeatWithRevision(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): HeartbeatInfo = withContext(Dispatchers.IO) {
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext HeartbeatInfo(false)
            val url = "$cleanUrl/api/server/heartbeat"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (!response.isSuccessful) return@withContext HeartbeatInfo(false)
                val bodyStr = response.body?.string() ?: ""
                if (bodyStr.isNotBlank()) {
                    try {
                        val json = JSONObject(bodyStr)
                        return@withContext HeartbeatInfo(
                            isConnected = true,
                            dataRevision = json.optLong("dataRevision", 0L),
                            settingsRevision = json.optLong("settingsRevision", 0L),
                            claudeRevision = json.optLong("claudeRevision", 0L),
                            labelSeenRevision = json.optLong("labelSeenRevision", 0L)
                        )
                    } catch (_: Exception) {
                    }
                }
                HeartbeatInfo(isConnected = true)
            }
        } catch (_: Exception) {
            HeartbeatInfo(false)
        }
    }

    suspend fun sendHeartbeat(baseUrl: String, token: String = getSavedAuthToken()): Boolean {
        return sendHeartbeatWithRevision(baseUrl, token).isConnected
    }

    suspend fun fetchPermanentlyDeletedUuids(
        baseUrl: String,
        token: String = getSavedAuthToken()
    ): Set<String> = withContext(Dispatchers.IO) {
        val result = mutableSetOf<String>()
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext result
            val url = "$cleanUrl/api/server/status"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    if (bodyStr.isNotBlank()) {
                        val json = JSONObject(bodyStr)
                        val arr = json.optJSONArray("permanentlyDeletedUuids")
                        if (arr != null) {
                            for (i in 0 until arr.length()) {
                                val u = arr.optString(i, "").trim()
                                if (u.isNotBlank()) result.add(u)
                            }
                        }
                    }
                }
            }
        } catch (_: Exception) {
        }
        result
    }

    suspend fun fetchServerHashes(baseUrl: String, token: String = getSavedAuthToken()): Set<String> = withContext(Dispatchers.IO) {
        val hashSet = mutableSetOf<String>()
        try {
            val cleanUrl = baseUrl.trimEnd('/')
            if (cleanUrl.isBlank() || !cleanUrl.startsWith("http")) return@withContext hashSet
            val url = "$cleanUrl/api/sync/hashes"
            val reqBuilder = Request.Builder().url(url).get()
            if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
            client.newCall(reqBuilder.build()).execute().use { response ->
                if (response.isSuccessful) {
                    val bodyStr = response.body?.string() ?: ""
                    val json = JSONObject(bodyStr)
                    val array = json.optJSONArray("hashes")
                    if (array != null) {
                        for (i in 0 until array.length()) {
                            hashSet.add(array.getString(i))
                        }
                    }
                }
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            e.printStackTrace()
        }
        hashSet
    }

    /**
     * PC サーバーへまとめ買い解除（POST /api/items/{parentId}/unmerge）を通知
     */
    suspend fun unmergeOnPcServer(parentId: Int): Boolean = withContext(Dispatchers.IO) {
        val cleanUrl = getSavedServerUrl().trim().removeSuffix("/")
        if (cleanUrl.isBlank()) return@withContext false
        val token = getSavedAuthToken()
        val url = "$cleanUrl/api/items/$parentId/unmerge"
        val reqBuilder = Request.Builder().url(url).post(ByteArray(0).toRequestBody(null))
        if (token.isNotBlank()) reqBuilder.addHeader("Authorization", "Bearer $token")
        try {
            client.newCall(reqBuilder.build()).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            android.util.Log.e("PC_SYNC", "unmergeOnPcServer 例外: ${e.message}", e)
            false
        }
    }
}

