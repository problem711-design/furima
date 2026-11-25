package com.example

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import com.example.myapplication.data.local.DefaultSettings
import com.example.myapplication.data.local.ItemEntity
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.components.MergeItemsDialog
import com.example.myapplication.ui.screens.CompactItemCard
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [36])
class ItemListAndMergeUiTest {

    @get:Rule
    val composeTestRule = createComposeRule()

    private fun makeItem(id: Int, title: String, status: String = "LISTED", price: Int = 1000): ItemWithPhotos {
        return ItemWithPhotos(
            item = ItemEntity(id = id, title = title, status = status, price = price),
            photos = emptyList()
        )
    }

    @Test
    fun `merged item renders badge and respects non-selectable state while listed item toggles`() {
        val mergedItem = makeItem(id = 10, title = "まとめ親元の商品", status = "MERGED", price = 1500)
        val listedItem = makeItem(id = 11, title = "通常商品", status = "LISTED", price = 2000)
        val toggledIds = mutableListOf<Int>()
        val selectedState = mutableStateOf(false)

        composeTestRule.setContent {
            MaterialTheme {
                CompactItemCard(
                    itemWithPhotos = mergedItem,
                    defaultFeePercent = 5,
                    defaultShipping = 210,
                    defaultExpense = 55,
                    isSelectionMode = true,
                    isSelected = false,
                    onSelectToggle = { toggledIds.add(mergedItem.item.id) },
                    onClick = {
                        if (mergedItem.item.status != "MERGED" && mergedItem.item.deletedAt == null) {
                            toggledIds.add(mergedItem.item.id)
                        }
                    }
                )
                CompactItemCard(
                    itemWithPhotos = listedItem,
                    defaultFeePercent = 5,
                    defaultShipping = 210,
                    defaultExpense = 55,
                    isSelectionMode = true,
                    isSelected = selectedState.value,
                    onSelectToggle = {
                        selectedState.value = !selectedState.value
                        toggledIds.add(listedItem.item.id)
                    },
                    onClick = {
                        selectedState.value = !selectedState.value
                        toggledIds.add(listedItem.item.id)
                    }
                )
            }
        }

        composeTestRule.onNodeWithText("まとめ済").assertIsDisplayed()
        composeTestRule.onNodeWithTag("item_card_10").performClick()
        assertTrue(toggledIds.isEmpty())

        composeTestRule.onNodeWithTag("item_card_11").performClick()
        assertEquals(listOf(11), toggledIds)
        assertTrue(selectedState.value)
    }

    @Test
    fun `merge dialog recalculates title and price on child removal and shows regenerate after manual edit`() {
        val candidates = listOf(
            makeItem(1, "ジャケット", price = 1200),
            makeItem(2, "シャツ", price = 800),
            makeItem(3, "ネクタイ", price = 500)
        )

        composeTestRule.setContent {
            MaterialTheme {
                MergeItemsDialog(
                    selectedItems = candidates,
                    defaultSettings = DefaultSettings(defaultExpenseCost = 55),
                    onDismiss = {},
                    onConfirm = { _, _, _, _, _, _, _ -> }
                )
            }
        }

        // 1. 初期状態では自動生成された 3 点合計 ¥2500
        composeTestRule.onNodeWithTag("merge_price_input").assertTextContains("2500")

        // 2. タイトルを手動変更した後で候補を 1 件削除すると再生成ボタンが出現する
        composeTestRule.onNodeWithTag("merge_title_input").performTextReplacement("手動編集タイトル")
        composeTestRule.onAllNodesWithContentDescription("削除")[0].performClick()

        // 価格は未手動変更なので自動再計算 (800 + 500 = 1300) され、手動タイトルは保持される
        composeTestRule.onNodeWithTag("merge_price_input").assertTextContains("1300")
        composeTestRule.onNodeWithTag("merge_title_input").assertTextContains("手動編集タイトル")
        composeTestRule.onNodeWithTag("merge_regenerate_button").assertIsDisplayed()

        // 3. 再生成ボタンをタップすると 2 点の自動生成タイトルへ復帰する
        composeTestRule.onNodeWithTag("merge_regenerate_button").performClick()
        composeTestRule.onNodeWithTag("merge_title_input").assertTextContains("【まとめ買い】シャツ＋ネクタイ")
    }
}
