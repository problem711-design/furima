package com.example.myapplication.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Database(entities = [ItemEntity::class, PhotoEntity::class], version = 6, exportSchema = false)
abstract class AppDatabase : RoomDatabase() {
    abstract fun itemDao(): ItemDao

    companion object {
        @Volatile
        private var INSTANCE: AppDatabase? = null

        val MIGRATION_4_5 = object : Migration(4, 5) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE items ADD COLUMN updatedAt INTEGER NOT NULL DEFAULT 0")
                db.execSQL("UPDATE items SET updatedAt = createdAt WHERE updatedAt = 0")

                db.execSQL("ALTER TABLE photos ADD COLUMN md5Hash TEXT NOT NULL DEFAULT ''")
                db.execSQL("ALTER TABLE photos ADD COLUMN syncStatus TEXT NOT NULL DEFAULT 'PENDING'")
                db.execSQL("ALTER TABLE photos ADD COLUMN updatedAt INTEGER NOT NULL DEFAULT 0")
                db.execSQL("UPDATE photos SET updatedAt = createdAt WHERE updatedAt = 0")
            }
        }

        val MIGRATION_5_6 = object : Migration(5, 6) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE items ADD COLUMN syncUuid TEXT NOT NULL DEFAULT ''")
                db.execSQL("ALTER TABLE items ADD COLUMN deletedAt INTEGER DEFAULT NULL")
                db.execSQL("ALTER TABLE items ADD COLUMN isDirty INTEGER NOT NULL DEFAULT 1")

                val cursor = db.query("SELECT id, syncUuid FROM items")
                cursor.use { c ->
                    while (c.moveToNext()) {
                        val rowId = c.getInt(0)
                        val existingUuid = if (c.isNull(1)) "" else c.getString(1)
                        if (existingUuid.isBlank()) {
                            val newUuid = java.util.UUID.randomUUID().toString()
                            db.execSQL("UPDATE items SET syncUuid = ? WHERE id = ?", arrayOf<Any>(newUuid, rowId))
                        }
                    }
                }
            }
        }

        fun getDatabase(context: Context): AppDatabase {
            return INSTANCE ?: synchronized(this) {
                val instance = Room.databaseBuilder(
                    context.applicationContext,
                    AppDatabase::class.java,
                    "flea_market_database"
                )
                    .addMigrations(MIGRATION_4_5, MIGRATION_5_6)
                    .build()
                INSTANCE = instance
                instance
            }
        }
    }
}
