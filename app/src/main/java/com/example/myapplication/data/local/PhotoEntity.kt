package com.example.myapplication.data.local

import androidx.room.Entity
import androidx.room.ForeignKey
import androidx.room.Index
import androidx.room.PrimaryKey

@Entity(
    tableName = "photos",
    foreignKeys = [
        ForeignKey(
            entity = ItemEntity::class,
            parentColumns = ["id"],
            childColumns = ["itemId"],
            onDelete = ForeignKey.CASCADE
        )
    ],
    indices = [Index(value = ["itemId"])]
)
data class PhotoEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Int = 0,
    val itemId: Int,
    val filePath: String,
    val thumbnailPath: String? = null, // 軽量サムネイルのローカルパス
    val isOriginalDeleted: Boolean = false, // PC送信後に元高画質画像が削除されサムネイルのみか
    val isPcSynced: Boolean = false, // PC側に高画質画像が同期・保存済みか
    val md5Hash: String = "", // 写真のMD5ハッシュ値
    val syncStatus: String = "PENDING", // 同期ステータス ("PENDING", "SYNCED", "THUMB_ONLY")
    val displayOrder: Int = 0,
    val createdAt: Long = System.currentTimeMillis(),
    val updatedAt: Long = System.currentTimeMillis() // 更新日時タイムスタンプ
) {
    /**
     * 表示用ファイル（サムネイルが存在すればサムネイル、無ければ元画像を自動優先返却）
     */
    val displayFile: java.io.File?
        get() {
            if (!thumbnailPath.isNullOrBlank()) {
                val thumb = java.io.File(thumbnailPath)
                if (thumb.exists()) return thumb
            }
            val orig = java.io.File(filePath)
            if (orig.exists()) return orig
            return null
        }
}
