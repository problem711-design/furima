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
    @Query("SELECT * FROM items WHERE mergedParentId IS NULL ORDER BY createdAt DESC")
    fun getAllActiveItemsWithPhotos(): Flow<List<ItemWithPhotos>>

    @Transaction
    @Query("SELECT * FROM items ORDER BY createdAt DESC")
    fun getAllItemsWithPhotos(): Flow<List<ItemWithPhotos>>

    @Transaction
    @Query("SELECT * FROM items WHERE id = :id")
    suspend fun getItemWithPhotosById(id: Int): ItemWithPhotos?

    @Transaction
    @Query("SELECT * FROM items WHERE id IN (:ids)")
    suspend fun getItemsWithPhotosByIds(ids: List<Int>): List<ItemWithPhotos>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertItem(item: ItemEntity): Long

    @Update
    suspend fun updateItem(item: ItemEntity)

    @Delete
    suspend fun deleteItem(item: ItemEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertPhotos(photos: List<PhotoEntity>)

    @Update
    suspend fun updatePhotos(photos: List<PhotoEntity>)

    @Query("DELETE FROM photos WHERE itemId = :itemId")
    suspend fun deletePhotosByItemId(itemId: Int)

    @Query("SELECT * FROM photos WHERE itemId = :itemId ORDER BY displayOrder ASC")
    suspend fun getPhotosByItemId(itemId: Int): List<PhotoEntity>

    @Query("SELECT * FROM items WHERE mergedParentId IS NULL")
    fun getAllActiveItems(): Flow<List<ItemEntity>>

    @Query("UPDATE items SET status = 'MERGED', mergedParentId = :parentId WHERE id IN (:childIds)")
    suspend fun markItemsMerged(childIds: List<Int>, parentId: Int)

    @Query("SELECT * FROM items WHERE mergedParentId = :parentId")
    suspend fun getMergedChildItems(parentId: Int): List<ItemEntity>
}
