package com.example.myapplication.data

import android.content.Context
import android.content.SharedPreferences
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class DefaultSettingsManager(context: Context) {
    private val prefs: SharedPreferences =
        context.getSharedPreferences("flea_market_settings", Context.MODE_PRIVATE)
    private val gson = Gson()

    private val _settings = MutableStateFlow(loadSettings())
    val settings: StateFlow<DefaultSettings> = _settings.asStateFlow()

    private fun loadSettings(): DefaultSettings {
        val feeRate = prefs.getInt("default_fee_rate", 5)
        val shipping = prefs.getInt("default_shipping", 210)
        val expense = prefs.getInt("default_expense", 55)
        val thumbQuality = prefs.getInt("thumbnail_quality", 75)
        val thumbDim = prefs.getInt("thumbnail_dim", 480)
        val cleanupDays = prefs.getInt("cleanup_days", 30)
        val activePresetId = prefs.getString("active_preset_id", "yahoo_furima") ?: "yahoo_furima"

        val presetsJson = prefs.getString("fee_presets_json", null)
        val presets = if (!presetsJson.isNullOrBlank()) {
            try {
                val type = object : TypeToken<List<FeePreset>>() {}.type
                gson.fromJson<List<FeePreset>>(presetsJson, type) ?: DefaultSettings.defaultPresets
            } catch (e: Exception) {
                DefaultSettings.defaultPresets
            }
        } else {
            DefaultSettings.defaultPresets
        }

        return DefaultSettings(
            defaultFeeRatePercent = feeRate,
            defaultShippingCost = shipping,
            defaultExpenseCost = expense,
            thumbnailQuality = thumbQuality,
            thumbnailMaxDimension = thumbDim,
            autoCleanupSoldDays = cleanupDays,
            activePresetId = activePresetId,
            presets = presets
        )
    }

    fun updateSettings(newSettings: DefaultSettings) {
        val activePreset = newSettings.activePreset
        prefs.edit()
            .putInt("default_fee_rate", activePreset.feeRatePercent)
            .putInt("default_shipping", activePreset.shippingCost)
            .putInt("default_expense", activePreset.expenseCost)
            .putInt("thumbnail_quality", newSettings.thumbnailQuality)
            .putInt("thumbnail_dim", newSettings.thumbnailMaxDimension)
            .putInt("cleanup_days", newSettings.autoCleanupSoldDays)
            .putString("active_preset_id", newSettings.activePresetId)
            .putString("fee_presets_json", gson.toJson(newSettings.presets))
            .apply()
        _settings.value = newSettings
    }
}
