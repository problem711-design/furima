package com.example.myapplication.data

import android.content.Context
import android.util.Log
import com.example.myapplication.data.local.ItemDao
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

internal class SyncTombstoneStore(
    context: Context,
    private val dao: ItemDao,
    private val photoStorageManager: PhotoStorageManager,
    private val runInDbTransaction: suspend (suspend () -> Int) -> Int,
    private val isItemEditingActive: (Int) -> Boolean
) {
    private val syncTombstonePrefs =
        context.getSharedPreferences("flea_market_sync_tombstones", Context.MODE_PRIVATE)

    // レビュー §83-4: 直近のゴースト浄化の内訳 (同期進捗メッセージでの UI 通知用)。
    // PcServerClient.syncDeltaToPcServer が消費して表示し、表示後にクリアする。
    @Volatile var lastGhostCleanupGhostCount: Int = 0
        private set
    @Volatile var lastGhostCleanupUnrecoverableCount: Int = 0
        private set

    /** 浄化内訳を 1 回だけ取り出す (取り出すと 0 に戻る)。 */
    fun consumeGhostCleanupSummary(): Pair<Int, Int> {
        val ghost = lastGhostCleanupGhostCount
        val unrecoverable = lastGhostCleanupUnrecoverableCount
        lastGhostCleanupGhostCount = 0
        lastGhostCleanupUnrecoverableCount = 0
        return ghost to unrecoverable
    }

    /**
     * ローカル DB に残存する空 MD5 の壊れ写真行を安全に除去する。
     * - 完全ゴースト: 実ファイルもサムネイルも存在しない行。
     * - 復旧不能行 (§83-4): 原本ファイルが消失し MD5 も未確定のまま、サムネイルだけが残った行。
     *   原本が無い以上ハッシュを確定できず、未同期カウントに残り続けると自動同期が
     *   無限に再起動するため、隔離せず浄化し、件数は UI 通知用に記録する。
     * 実体ファイル (原本) が存在する写真には絶対に触れない。編集中アイテムも保護する。
     */
    suspend fun cleanupGhostBlankMd5Photos(): Int = withContext(Dispatchers.IO) {
        val orphanThumbPaths = mutableListOf<String>()
        val removedTotal = runInDbTransaction {
            val candidates = dao.getBlankMd5Photos()
            if (candidates.isEmpty()) return@runInDbTransaction 0

            val ghostIds = mutableListOf<Int>()
            val unrecoverableIds = mutableListOf<Int>()
            val affectedItemIds = mutableSetOf<Int>()
            for (photo in candidates) {
                if (photo.md5Hash.isNotBlank()) continue
                if (isItemEditingActive(photo.itemId)) continue
                val origExists = photo.filePath.isNotBlank() &&
                    File(photo.filePath).let { it.exists() && it.isFile && it.length() > 0L }
                val thumbExists = !photo.thumbnailPath.isNullOrBlank() &&
                    File(photo.thumbnailPath).let { it.exists() && it.isFile && it.length() > 0L }
                if (!origExists && !thumbExists) {
                    ghostIds.add(photo.id)
                    affectedItemIds.add(photo.itemId)
                } else if (!origExists && thumbExists) {
                    // §83-4: 原本消失・MD5 未確定・サムネイルのみ生存 = 復旧不能行として浄化する
                    unrecoverableIds.add(photo.id)
                    affectedItemIds.add(photo.itemId)
                    val thumbPath = photo.thumbnailPath
                    if (!thumbPath.isNullOrBlank() &&
                        dao.getPhotosReferencingPath(thumbPath).none { it.id != photo.id }
                    ) {
                        orphanThumbPaths.add(thumbPath)
                    }
                }
            }
            val allIds = ghostIds + unrecoverableIds
            if (allIds.isEmpty()) return@runInDbTransaction 0

            dao.deletePhotosByIds(allIds)
            val now = System.currentTimeMillis()
            for (affectedItemId in affectedItemIds) {
                val remaining = dao.getPhotosByItemId(affectedItemId)
                if (remaining.isNotEmpty()) {
                    val reindexed = remaining.mapIndexed { idx, p ->
                        p.copy(displayOrder = idx, updatedAt = now)
                    }
                    dao.updatePhotos(reindexed)
                }
            }
            if (ghostIds.isNotEmpty()) {
                Log.w("PHOTO_INTEGRITY", "Cleaned up ${ghostIds.size} orphan blank-MD5 ghost photo(s) across ${affectedItemIds.size} item(s)")
            }
            if (unrecoverableIds.isNotEmpty()) {
                Log.w("PHOTO_INTEGRITY", "Cleaned up ${unrecoverableIds.size} unrecoverable blank-MD5 photo row(s) (original missing, thumbnail only)")
                lastGhostCleanupUnrecoverableCount += unrecoverableIds.size
            }
            if (ghostIds.isNotEmpty()) {
                lastGhostCleanupGhostCount += ghostIds.size
            }
            allIds.size
        }
        // 他の写真行から参照されていないサムネイル実ファイルは、行の除去に合わせて破棄する
        for (thumbPath in orphanThumbPaths) {
            try {
                File(thumbPath).delete()
            } catch (_: Exception) {
            }
        }
        removedTotal
    }

    /**
     * §83-4: 未同期写真数の判定前にゴースト浄化を行い、壊れ行で常時 > 0 にならないようにする。
     * 自動同期・再接続ハンドシェイクの判定はこちらを使う。
     */
    suspend fun getUnsyncedPhotosCountAfterGhostCleanup(): Int {
        cleanupGhostBlankMd5Photos()
        return dao.getUnsyncedPhotosCount()
    }

    fun addPendingDeletedUuid(uuid: String) {
        val clean = uuid.trim()
        if (clean.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val current = (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toMutableSet()
            current.add(clean)
            syncTombstonePrefs.edit().putStringSet("pending_deleted_uuids", current).apply()
        }
    }

    fun getPendingDeletedUuids(): Set<String> {
        synchronized(syncTombstonePrefs) {
            return (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toSet()
        }
    }

    fun removePendingDeletedUuids(uuids: Collection<String>) {
        if (uuids.isEmpty()) return
        synchronized(syncTombstonePrefs) {
            val current = (syncTombstonePrefs.getStringSet("pending_deleted_uuids", emptySet()) ?: emptySet()).toMutableSet()
            current.removeAll(uuids.map { it.trim() }.toSet())
            syncTombstonePrefs.edit().putStringSet("pending_deleted_uuids", current).apply()
        }
    }

    // レビュー 24-7: 未Pushの写真削除の記録。商品キーはローカル数値IDではなく syncUuid を使う。
    // isDirty 中の加算マージ (PcServerClient) で、削除したはずの写真がサーバーから
    // 復活しないよう、送信が成功するまで (syncUuid, md5Hash) を保持して除外に使う。
    fun recordPendingPhotoDeletion(syncUuid: String, md5Hash: String) {
        val uuid = syncUuid.trim()
        val md5 = md5Hash.trim().lowercase()
        if (uuid.isBlank() || md5.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val key = "pending_deleted_photo_hashes_$uuid"
            val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
            current.add(md5)
            syncTombstonePrefs.edit().putStringSet(key, current).apply()
        }
    }

    fun getPendingPhotoDeletions(syncUuid: String): Set<String> {
        val uuid = syncUuid.trim()
        if (uuid.isBlank()) return emptySet()
        synchronized(syncTombstonePrefs) {
            return (syncTombstonePrefs.getStringSet("pending_deleted_photo_hashes_$uuid", emptySet()) ?: emptySet()).toSet()
        }
    }

    fun getAllPendingPhotoDeletions(): Map<String, Set<String>> {
        synchronized(syncTombstonePrefs) {
            val result = mutableMapOf<String, Set<String>>()
            for ((key, value) in syncTombstonePrefs.all) {
                if (key.startsWith("pending_deleted_photo_hashes_") && value is Set<*>) {
                    @Suppress("UNCHECKED_CAST")
                    result[key.removePrefix("pending_deleted_photo_hashes_")] = (value as Set<String>).toSet()
                }
            }
            return result
        }
    }

    // 送信に含めた記録だけを消去する (送信中に新たに記録された分は残す)
    fun removePendingPhotoDeletions(recorded: Map<String, Set<String>>) {
        if (recorded.isEmpty()) return
        synchronized(syncTombstonePrefs) {
            val editor = syncTombstonePrefs.edit()
            for ((uuid, hashes) in recorded) {
                val key = "pending_deleted_photo_hashes_$uuid"
                val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
                current.removeAll(hashes)
                if (current.isEmpty()) editor.remove(key) else editor.putStringSet(key, current)
            }
            editor.apply()
        }
    }

    // 削除した写真と同一 MD5 の写真をその場で再追加した場合は、その記録を解除する
    fun releasePendingPhotoDeletion(syncUuid: String, md5Hash: String) {
        val uuid = syncUuid.trim()
        val md5 = md5Hash.trim().lowercase()
        if (uuid.isBlank() || md5.isBlank()) return
        synchronized(syncTombstonePrefs) {
            val key = "pending_deleted_photo_hashes_$uuid"
            val current = (syncTombstonePrefs.getStringSet(key, emptySet()) ?: emptySet()).toMutableSet()
            if (current.remove(md5)) {
                if (current.isEmpty()) {
                    syncTombstonePrefs.edit().remove(key).apply()
                } else {
                    syncTombstonePrefs.edit().putStringSet(key, current).apply()
                }
            }
        }
    }

}
