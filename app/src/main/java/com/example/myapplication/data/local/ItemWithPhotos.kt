package com.example.myapplication.data.local

import androidx.room.Embedded
import androidx.room.Relation

data class ItemWithPhotos(
    @Embedded val item: ItemEntity,
    @Relation(
        parentColumn = "id",
        entityColumn = "itemId"
    )
    val photos: List<PhotoEntity>
)
