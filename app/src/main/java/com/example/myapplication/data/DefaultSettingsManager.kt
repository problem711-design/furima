package com.example.myapplication.data

import android.content.Context
import android.content.SharedPreferences
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.FeePreset
import com.example.myapplication.data.local.TextTemplate
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
        val settingsUpdatedAt = prefs.getLong("settings_updated_at", 0L)

        val presetsJson = prefs.getString("fee_presets_json", null)
        val presets = if (!presetsJson.isNullOrBlank()) {
            try {
                val type = object : TypeToken<List<FeePreset>>() {}.type
                gson.fromJson<List<FeePreset>>(presetsJson, type)?.takeIf { it.isNotEmpty() }
                    ?: DefaultSettings.defaultPresets
            } catch (e: Exception) {
                DefaultSettings.defaultPresets
            }
        } else {
            DefaultSettings.defaultPresets
        }

        val titleTemplatesJson = prefs.getString("title_templates_json", null)
        val titleTemplates = if (!titleTemplatesJson.isNullOrBlank()) {
            try {
                val type = object : TypeToken<List<TextTemplate>>() {}.type
                gson.fromJson<List<TextTemplate>>(titleTemplatesJson, type) ?: emptyList()
            } catch (e: Exception) {
                emptyList()
            }
        } else {
            emptyList()
        }

        val descriptionTemplatesJson = prefs.getString("description_templates_json", null)
        val descriptionTemplates = if (!descriptionTemplatesJson.isNullOrBlank()) {
            try {
                val type = object : TypeToken<List<TextTemplate>>() {}.type
                gson.fromJson<List<TextTemplate>>(descriptionTemplatesJson, type) ?: emptyList()
            } catch (e: Exception) {
                emptyList()
            }
        } else {
            emptyList()
        }

        return DefaultSettings(
            defaultFeeRatePercent = feeRate,
            defaultShippingCost = shipping,
            defaultExpenseCost = expense,
            thumbnailQuality = thumbQuality,
            thumbnailMaxDimension = thumbDim,
            autoCleanupSoldDays = cleanupDays,
            activePresetId = activePresetId,
            presets = presets,
            titleTemplates = titleTemplates,
            descriptionTemplates = descriptionTemplates,
            settingsUpdatedAt = settingsUpdatedAt
        )
    }

    fun updateSettings(newSettings: DefaultSettings, stampNow: Boolean = true) {
        val resolvedPresets = newSettings.presets.ifEmpty { DefaultSettings.defaultPresets }
        val resolvedUpdatedAt = if (stampNow) System.currentTimeMillis() else newSettings.settingsUpdatedAt
        val resolvedSettings = newSettings.copy(
            defaultFeeRatePercent = newSettings.defaultFeeRatePercent.coerceAtLeast(0),
            defaultShippingCost = newSettings.defaultShippingCost.coerceAtLeast(0),
            defaultExpenseCost = newSettings.defaultExpenseCost.coerceAtLeast(0),
            presets = resolvedPresets,
            settingsUpdatedAt = resolvedUpdatedAt
        )
        prefs.edit()
            .putInt("default_fee_rate", resolvedSettings.defaultFeeRatePercent)
            .putInt("default_shipping", resolvedSettings.defaultShippingCost)
            .putInt("default_expense", resolvedSettings.defaultExpenseCost)
            .putInt("thumbnail_quality", resolvedSettings.thumbnailQuality)
            .putInt("thumbnail_dim", resolvedSettings.thumbnailMaxDimension)
            .putInt("cleanup_days", resolvedSettings.autoCleanupSoldDays)
            .putString("active_preset_id", resolvedSettings.activePresetId)
            .putString("fee_presets_json", gson.toJson(resolvedSettings.presets))
            .putString("title_templates_json", gson.toJson(resolvedSettings.titleTemplates))
            .putString("description_templates_json", gson.toJson(resolvedSettings.descriptionTemplates))
            .putLong("settings_updated_at", resolvedSettings.settingsUpdatedAt)
            .apply()
        _settings.value = resolvedSettings
    }
}
