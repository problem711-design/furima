package com.example.myapplication.data

import android.content.Context
import android.content.SharedPreferences
import com.example.myapplication.data.local.DefaultSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class DefaultSettingsManager(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences("flea_market_settings", Context.MODE_PRIVATE)

    private val _settings = MutableStateFlow(loadSettings())
    val settings: StateFlow<DefaultSettings> = _settings.asStateFlow()

    private fun loadSettings(): DefaultSettings {
        val feeRate = prefs.getInt("default_fee_rate", 5)
        val shipping = prefs.getInt("default_shipping", 210)
        val expense = prefs.getInt("default_expense", 55)
        val thumbQuality = prefs.getInt("thumbnail_quality", 75)
        val thumbDim = prefs.getInt("thumbnail_dim", 480)
        val cleanupDays = prefs.getInt("cleanup_days", 30)
        return DefaultSettings(
            defaultFeeRatePercent = feeRate,
            defaultShippingCost = shipping,
            defaultExpenseCost = expense,
            thumbnailQuality = thumbQuality,
            thumbnailMaxDimension = thumbDim,
            autoCleanupSoldDays = cleanupDays
        )
    }

    fun updateSettings(newSettings: DefaultSettings) {
        prefs.edit()
            .putInt("default_fee_rate", newSettings.defaultFeeRatePercent)
            .putInt("default_shipping", newSettings.defaultShippingCost)
            .putInt("default_expense", newSettings.defaultExpenseCost)
            .putInt("thumbnail_quality", newSettings.thumbnailQuality)
            .putInt("thumbnail_dim", newSettings.thumbnailMaxDimension)
            .putInt("cleanup_days", newSettings.autoCleanupSoldDays)
            .apply()
        _settings.value = newSettings
    }
}
