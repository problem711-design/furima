package com.example.myapplication

import com.example.myapplication.data.server.ClaudeJobDto
import com.example.myapplication.data.server.ClaudeJobErrorDto
import com.example.myapplication.data.server.ClaudeJobPhotoDto
import com.example.myapplication.data.server.ClaudeJobResultDto
import com.example.myapplication.data.server.ClaudeLabelDerivation
import com.squareup.moshi.Moshi
import com.squareup.moshi.Types
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

// フェーズ 6-10 (§41.5): 共通ゴールデン仕様表の Android 側テスト。
// JS 側 (tools/tests/claude-label.test.mjs) と同じフィクスチャ
// (src/test/resources/claude_label_fixtures.json) を読み、導出関数の写真ラベルを検証する。
// ミューズカラムのタイルは PC 専用のため、ここでは expectLabel のみを対象とする。
class ClaudeLabelDerivationTest {

    @Suppress("UNCHECKED_CAST")
    private fun loadFixtures(): List<Map<String, Any?>> {
        val stream = javaClass.getResourceAsStream("/claude_label_fixtures.json")
        assertNotNull("claude_label_fixtures.json が test resources に無い", stream)
        val text = stream!!.bufferedReader(Charsets.UTF_8).readText()
        val type = Types.newParameterizedType(Map::class.java, String::class.java, Any::class.java)
        val parsed = Moshi.Builder().build().adapter<Map<String, Any?>>(type).fromJson(text)
        return parsed?.get("cases") as? List<Map<String, Any?>>
            ?: error("フィクスチャの cases が読めない")
    }

    @Suppress("UNCHECKED_CAST")
    @Test
    fun goldenFixtures_photoLabels() {
        val dayMs = 24L * 60 * 60 * 1000
        val now = System.currentTimeMillis()
        val cases = loadFixtures()
        assertEquals("フィクスチャは 13 ケース", 13, cases.size)
        for (case in cases) {
            val id = (case["id"] as Number).toInt()
            val jobMaps = case["jobs"] as List<Map<String, Any?>>
            val jobs = jobMaps.mapIndexed { idx, jm ->
                ClaudeJobDto(
                    jobId = "case$id-$idx",
                    itemUuid = jm["itemUuid"] as String,
                    queue = jm["queue"] as String,
                    status = jm["status"] as String,
                    createdAt = now - (((jm["createdDaysAgo"] as? Number)?.toDouble() ?: 0.0) * dayMs).toLong(),
                    completedAt = ((jm["completedDaysAgo"] as? Number)?.toDouble())?.let { now - (it * dayMs).toLong() } ?: 0L,
                    errorDismissed = jm["errorDismissed"] as? Boolean ?: false,
                    photos = ((jm["photos"] as? List<String>) ?: emptyList()).map { ClaudeJobPhotoDto(md5 = it) },
                    results = ((jm["results"] as? List<Map<String, Any?>>) ?: emptyList()).map {
                        ClaudeJobResultDto(sourceMd5 = it["sourceMd5"] as String, resultMd5 = it["resultMd5"] as String)
                    },
                    errors = ((jm["errors"] as? List<String>) ?: emptyList()).map { ClaudeJobErrorDto(sourceMd5 = it) }
                )
            }
            val seenMap = ((case["seenAtByItemUuid"] as? Map<String, Any?>) ?: emptyMap()).mapValues { (_, v) ->
                now - (((v as Number).toDouble()) * dayMs).toLong()
            }
            val bundleUuids = (case["bundleUuids"] as List<String>)
            val actual = ClaudeLabelDerivation.photoLabelKey(
                bundleUuids,
                case["targetMd5"] as String,
                jobs,
                seenMap,
                emptyMap(),
                now
            )
            assertEquals("case $id の写真ラベル", case["expectLabel"], actual)
        }
    }
}
