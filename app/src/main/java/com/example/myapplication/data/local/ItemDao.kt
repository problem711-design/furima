package com.example.myapplication.data.local

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import androidx.room.Update
import kotlinx.coroutines.flow.Flow

@Dao
interface ItemDao {
    @Transaction
    @Query("SELECT * FROM items WHERE mergedParentId IS NULL AND deletedAt IS NULL ORDER BY createdAt DESC")
    fun getAllActiveItemsWithPhotos(): Flow<List<ItemWithPhotos>>

    @Transaction
    @Query("SELECT * FROM items WHERE mergedParentId IS NULL AND deletedAt IS NULL ORDER BY createdAt DESC")
    suspend fun getActiveItemsWithPhotosDirect(): List<ItemWithPhotos>

    @Transaction
    @Query("SELECT * FROM items WHERE deletedAt IS NULL ORDER BY createdAt DESC")
    fun getAllItemsWithPhotos(): Flow<List<ItemWithPhotos>>

    @Transaction
    @Query("SELECT * FROM items WHERE deletedAt IS NULL ORDER BY createdAt DESC")
    suspend fun getAllItemsWithPhotosDirect(): List<ItemWithPhotos>

    @Transaction
    @Query("SELECT * FROM items ORDER BY createdAt DESC")
    suspend fun getAllItemsWithPhotosForSync(): List<ItemWithPhotos>

    @Transaction
    @Query("SELECT * FROM items WHERE deletedAt IS NOT NULL AND mergedParentId IS NULL ORDER BY deletedAt DESC")
    fun getTrashedItemsWithPhotos(): Flow<List<ItemWithPhotos>>

    @Transaction
    @Query("SELECT * FROM items WHERE deletedAt IS NOT NULL AND mergedParentId IS NULL ORDER BY deletedAt DESC")
    suspend fun getTrashedItemsWithPhotosDirect(): List<ItemWithPhotos>

    @Query("SELECT COUNT(*) FROM items WHERE deletedAt IS NOT NULL AND mergedParentId IS NULL")
    fun getTrashedCount(): Flow<Int>

    @Transaction
    @Query("SELECT * FROM items WHERE deletedAt IS NOT NULL AND deletedAt <= :cutoffMs")
    suspend fun getExpiredTrashedItemsDirect(cutoffMs: Long): List<ItemWithPhotos>

    @Transaction
    @Query("SELECT * FROM items WHERE id = :id")
    suspend fun getItemWithPhotosById(id: Int): ItemWithPhotos?

    @Transaction
    @Query("SELECT * FROM items WHERE syncUuid = :syncUuid LIMIT 1")
    suspend fun getItemWithPhotosBySyncUuid(syncUuid: String): ItemWithPhotos?

    @Transaction
    @Query("SELECT * FROM items WHERE id IN (:ids)")
    suspend fun getItemsWithPhotosByIds(ids: List<Int>): List<ItemWithPhotos>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertItem(item: ItemEntity): Long

    @Update
    suspend fun updateItem(item: ItemEntity)

    @Delete
    suspend fun deleteItem(item: ItemEntity)

    @Query("UPDATE items SET deletedAt = :deletedAt, updatedAt = :updatedAt, isDirty = 1 WHERE id = :id")
    suspend fun softDeleteItem(id: Int, deletedAt: Long, updatedAt: Long)

    @Query("UPDATE items SET deletedAt = :deletedAt, updatedAt = :updatedAt, isDirty = 1 WHERE mergedParentId = :parentId")
    suspend fun softDeleteChildrenByParent(parentId: Int, deletedAt: Long, updatedAt: Long)

    @Query("UPDATE items SET deletedAt = NULL, updatedAt = :updatedAt, isDirty = 1 WHERE id = :id")
    suspend fun restoreFromTrash(id: Int, updatedAt: Long)

    @Query("UPDATE items SET deletedAt = NULL, updatedAt = :updatedAt, isDirty = 1 WHERE mergedParentId = :parentId")
    suspend fun restoreChildrenByParent(parentId: Int, updatedAt: Long)

    @Query("SELECT COUNT(*) FROM items WHERE isDirty = 1")
    suspend fun getDirtyItemsCount(): Int

    @Query("UPDATE items SET isDirty = 0, isShared = 1 WHERE id = :id AND updatedAt = :expectedUpdatedAt")
    suspend fun markItemSyncedIfUnchanged(id: Int, expectedUpdatedAt: Long)

    @Query("UPDATE items SET isDirty = 0, isShared = 1 WHERE id IN (:ids)")
    suspend fun markPendingPushCleared(ids: List<Int>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertPhotos(photos: List<PhotoEntity>)

    @Update
    suspend fun updatePhotos(photos: List<PhotoEntity>)

    @Query("DELETE FROM photos WHERE itemId = :itemId")
    suspend fun deletePhotosByItemId(itemId: Int)

    @Query("SELECT * FROM photos WHERE itemId = :itemId ORDER BY displayOrder ASC")
    suspend fun getPhotosByItemId(itemId: Int): List<PhotoEntity>

    @Query("SELECT * FROM photos WHERE itemId != :excludedItemId")
    suspend fun getPhotosExcludingItem(excludedItemId: Int): List<PhotoEntity>

    @Query("SELECT * FROM photos WHERE id = :id")
    suspend fun getPhotoById(id: Int): PhotoEntity?

    @Query("SELECT * FROM items WHERE mergedParentId IS NULL AND deletedAt IS NULL")
    fun getAllActiveItems(): Flow<List<ItemEntity>>

    @Query("UPDATE items SET status = 'MERGED', mergedParentId = :parentId, updatedAt = :updatedAt, isDirty = 1 WHERE id IN (:childIds)")
    suspend fun markItemsMerged(childIds: List<Int>, parentId: Int, updatedAt: Long = System.currentTimeMillis())

    @Query("SELECT * FROM items WHERE mergedParentId = :parentId")
    suspend fun getMergedChildItems(parentId: Int): List<ItemEntity>

    @Transaction
    @Query("SELECT * FROM items WHERE mergedParentId = :parentId")
    suspend fun getMergedChildItemsWithPhotos(parentId: Int): List<ItemWithPhotos>

    @Query("UPDATE items SET status = 'LISTED', mergedParentId = NULL, updatedAt = :updatedAt, isDirty = 1 WHERE mergedParentId = :parentId")
    suspend fun restoreMergedChildren(parentId: Int, updatedAt: Long = System.currentTimeMillis())

    @Query("UPDATE items SET status = 'LISTED', mergedParentId = NULL, updatedAt = :updatedAt, isDirty = 1 WHERE id IN (:childIds)")
    suspend fun unmergeSpecificChildren(childIds: List<Int>, updatedAt: Long = System.currentTimeMillis())

    @Query("UPDATE photos SET isPcSynced = :isSynced WHERE id = :photoId")
    suspend fun updatePhotoPcSynced(photoId: Int, isSynced: Boolean)

    @Query("UPDATE photos SET isOriginalDeleted = :isDeleted, filePath = :newPath WHERE id = :photoId")
    suspend fun updatePhotoOriginalDeleted(photoId: Int, isDeleted: Boolean, newPath: String)

    @Query("UPDATE photos SET isOriginalDeleted = 1, thumbnailPath = COALESCE(NULLIF(thumbnailPath, ''), :thumbPath), filePath = :thumbPath WHERE filePath = :oldFilePath")
    suspend fun markOriginalDeletedByFilePath(oldFilePath: String, thumbPath: String)

    @Query("UPDATE photos SET md5Hash = :md5 WHERE id = :photoId")
    suspend fun updatePhotoMd5Hash(photoId: Int, md5: String)

    @Query("UPDATE photos SET thumbnailPath = :thumbPath, isOriginalDeleted = :isOriginalDeleted, filePath = :newFilePath WHERE id = :photoId")
    suspend fun updatePhotoRepairedThumbnail(photoId: Int, thumbPath: String, isOriginalDeleted: Boolean, newFilePath: String)
}
