package com.example.myapplication.ui.screens

import android.widget.Toast
import androidx.compose.ui.draw.alpha
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.MergeType
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Sort
import androidx.compose.material.icons.filled.CloudDone
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.CloudSync
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import coil.request.ImageRequest
import coil.size.Precision
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.data.local.isSoldFamilyStatus
import com.example.myapplication.data.local.statusLabelOf
import com.example.myapplication.data.server.PcSyncProgress
import com.example.myapplication.data.server.SyncPhase
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.ui.SortOption
import com.example.myapplication.ui.TimeRange
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.OutlinedButton
import com.example.myapplication.ui.components.BatchEditDialog
import com.example.myapplication.ui.components.ItemQuickActionDialog
import com.example.myapplication.ui.components.MergeItemsDialog
import java.io.File
import java.text.NumberFormat
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ItemListScreen(
    viewModel: FleaMarketViewModel,
    onNavigateToDetail: (Int) -> Unit,
    onNavigateToAdd: () -> Unit,
    onNavigateToEdit: (Int) -> Unit = {},
    onNavigateToTrash: () -> Unit = {}
) {
    val context = LocalContext.current
    val haptic = LocalHapticFeedback.current
    val allActiveItems by viewModel.itemsWithPhotos.collectAsState()
    val filteredItems by viewModel.filteredItems.collectAsState()
    val periodSummary by viewModel.periodSalesSummary.collectAsState()
    val defaultSettings by viewModel.defaultSettings.collectAsState()
    val timeRange by viewModel.timeRange.collectAsState()
    val searchQuery by viewModel.searchQuery.collectAsState()
    val statusFilter by viewModel.statusFilter.collectAsState()
    val sortOption by viewModel.sortOption.collectAsState()
    val isSelectionMode by viewModel.isSelectionMode.collectAsState()
    val selectedItemIds by viewModel.selectedItemIds.collectAsState()
    val trashCount by viewModel.trashCount.collectAsState()
    val syncStatusState by viewModel.syncStatusState.collectAsState()
    val isPcServerConnected by viewModel.isPcServerConnected.collectAsState()
    val unsyncedItemsCount by viewModel.unsyncedItemsCount.collectAsState()
    val pcServerUrl by viewModel.pcServerUrl.collectAsState()
    val lastSyncSuccessAt by viewModel.lastSyncSuccessAt.collectAsState()
    val claudeItemLabels by viewModel.claudeItemLabelSummaries.collectAsState()

    var showSortMenu by remember { mutableStateOf(false) }
    var showMergeDialog by remember { mutableStateOf(false) }
    var showBatchEditDialog by remember { mutableStateOf(false) }
    var showBatchTrashConfirmDialog by remember { mutableStateOf(false) }
    var showSyncStatusDialog by remember { mutableStateOf(false) }
    var quickActionItem by remember { mutableStateOf<ItemWithPhotos?>(null) }

    // 表示件数リミット（デフォルト20件、10件ずつ追加可能。詳細/編集から戻った再コンポーズでは維持する）
    var displayLimit by rememberSaveable { mutableIntStateOf(20) }
    var lastFilterKey by rememberSaveable { mutableStateOf("$searchQuery|$statusFilter|${sortOption.name}") }

    // 検索語やフィルターが前回値から実際に変化した時のみ表示件数をリセット
    LaunchedEffect(searchQuery, statusFilter, sortOption) {
        val currentKey = "$searchQuery|$statusFilter|${sortOption.name}"
        if (currentKey != lastFilterKey) {
            lastFilterKey = currentKey
            displayLimit = 20
        }
    }

    // フェーズ 6-9 (Build 14): 一覧への復帰時に Muse ラベル集約を現在時刻で再導出する (3 日窓の自然消滅への追随)
    val lifecycleOwner = androidx.lifecycle.compose.LocalLifecycleOwner.current
    DisposableEffect(lifecycleOwner) {
        val observer = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_RESUME) viewModel.refreshClaudeItemLabels()
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    val visibleItems = remember(filteredItems, displayLimit) {
        filteredItems.take(displayLimit)
    }

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)

    // 長押しクイックアクションダイアログ
    quickActionItem?.let { itemWithPhotos ->
        ItemQuickActionDialog(
            itemWithPhotos = itemWithPhotos,
            onDismiss = { quickActionItem = null },
            onToggleStatus = {
                val newStatus = when (itemWithPhotos.item.status) {
                    "SOLD", "DRAFT", "DRAFT_DONE" -> "LISTED"
                    else -> "SOLD"
                }
                viewModel.updateItemStatus(itemWithPhotos.item, newStatus)
                Toast.makeText(context, "「${statusLabelOf(newStatus)}」に変更しました", Toast.LENGTH_SHORT).show()
            },
            onSelectStatus = { newStatus ->
                viewModel.updateItemStatus(itemWithPhotos.item, newStatus)
                Toast.makeText(context, "「${statusLabelOf(newStatus)}」に変更しました", Toast.LENGTH_SHORT).show()
            },
            onNavigateToEdit = {
                // サムネ長押し→編集選択時にダイレクトに商品編集画面へ遷移
                onNavigateToEdit(itemWithPhotos.item.id)
            },
            onCopyText = {
                viewModel.copyListingTemplate(context, itemWithPhotos)
            },
            onUnmerge = {
                quickActionItem = null
                viewModel.unmergeItem(itemWithPhotos.item.id) {
                    Toast.makeText(context, "まとめ買い出品を解除し、元の個別出品を復元しました", Toast.LENGTH_SHORT).show()
                }
            },
            onDelete = {
                quickActionItem = null
                viewModel.deleteItem(itemWithPhotos) {
                    Toast.makeText(context, "ゴミ箱へ移動しました", Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    // 複数選択時の一括編集ダイアログ
    if (showBatchEditDialog && selectedItemIds.isNotEmpty()) {
        val selectedItems = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
        if (selectedItems.isNotEmpty()) {
            BatchEditDialog(
                selectedItems = selectedItems,
                defaultSettings = defaultSettings,
                onDismiss = { showBatchEditDialog = false },
                onConfirm = { spec ->
                    showBatchEditDialog = false
                    viewModel.batchUpdateItems(selectedItems.map { it.item.id }, spec) { updatedCount ->
                        Toast.makeText(context, "${updatedCount}件を一括更新しました", Toast.LENGTH_SHORT).show()
                    }
                }
            )
        }
    }

    // 複数選択時の一括ゴミ箱移動確認ダイアログ
    if (showBatchTrashConfirmDialog && selectedItemIds.isNotEmpty()) {
        val selectedIdsList = selectedItemIds.toList()
        AlertDialog(
            onDismissRequest = { showBatchTrashConfirmDialog = false },
            icon = { Icon(imageVector = Icons.Default.DeleteOutline, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
            title = { Text("選択した商品をゴミ箱へ移動") },
            text = { Text("選択中の ${selectedIdsList.size}件の商品をゴミ箱へ移動しますか？（30日間保持され、いつでも元に戻せます）") },
            confirmButton = {
                Button(
                    onClick = {
                        showBatchTrashConfirmDialog = false
                        viewModel.batchMoveToTrash(selectedIdsList) { movedCount ->
                            Toast.makeText(context, "${movedCount}件をゴミ箱へ移動しました", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.testTag("confirm_batch_trash_button")
                ) {
                    Text("ゴミ箱へ移動")
                }
            },
            dismissButton = {
                TextButton(onClick = { showBatchTrashConfirmDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    // 複数選択時のまとめ買いダイアログ
    if (showMergeDialog && selectedItemIds.size >= 2) {
        val selectedItems = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
        MergeItemsDialog(
            selectedItems = selectedItems,
            defaultSettings = defaultSettings,
            onDismiss = { showMergeDialog = false },
            onConfirm = { title, desc, price, shipping, feeRate, expense, itemIds ->
                viewModel.mergeSelectedItems(
                    mergedTitle = title,
                    mergedDescription = desc,
                    totalSoldPrice = price,
                    newShippingCost = shipping,
                    newFeeRatePercent = feeRate,
                    newExpenseCost = expense,
                    childItemIds = itemIds,
                    asStatus = "LISTED"
                ) { _ ->
                    showMergeDialog = false
                    Toast.makeText(context, "まとめ出品を作成しました！", Toast.LENGTH_SHORT).show()
                }
            }
        )
    }

    if (showSyncStatusDialog) {
        SyncStatusDetailDialog(
            syncProgress = syncStatusState,
            isConnected = isPcServerConnected,
            serverUrl = pcServerUrl,
            unsyncedItemsCount = unsyncedItemsCount,
            lastSyncSuccessAt = lastSyncSuccessAt,
            onRetrySync = {
                viewModel.syncDeltaToPcServer { res ->
                    Toast.makeText(
                        context,
                        if (res.isSuccess) "同期完了 (${res.syncedItemsCount}件)" else "同期失敗: ${res.errorMessage ?: "エラー"}",
                        Toast.LENGTH_SHORT
                    ).show()
                }
            },
            onDismiss = { showSyncStatusDialog = false }
        )
    }

    Scaffold(
        contentWindowInsets = WindowInsets(0),
        topBar = {
            // スリム化したTopAppBar + 同期ステータスバッジ
            Surface(
                color = MaterialTheme.colorScheme.primary,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 12.dp, vertical = 2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween
                ) {
                    Text(
                        text = if (isSelectionMode) "選択中 (${selectedItemIds.size}件)" else "フリマ出品・売上管理",
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        color = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier.weight(1f, fill = false)
                    )

                    if (isSelectionMode) {
                        TextButton(
                            onClick = { viewModel.clearSelection() },
                            colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.onPrimary)
                        ) {
                            Text("完了", fontWeight = FontWeight.Bold)
                        }
                    } else {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(2.dp)
                        ) {
                            HeaderSyncStatusBadge(
                                syncProgress = syncStatusState,
                                isConnected = isPcServerConnected,
                                unsyncedItemsCount = unsyncedItemsCount,
                                onClick = { showSyncStatusDialog = true }
                            )

                            IconButton(
                                onClick = onNavigateToTrash,
                                modifier = Modifier
                                    .minimumInteractiveComponentSize()
                                    .testTag("open_trash_button")
                            ) {
                                BadgedBox(
                                    badge = {
                                        if (trashCount > 0) {
                                            Badge(
                                                containerColor = MaterialTheme.colorScheme.error,
                                                contentColor = MaterialTheme.colorScheme.onError
                                            ) {
                                                Text(if (trashCount > 99) "99+" else trashCount.toString())
                                            }
                                        }
                                    }
                                ) {
                                    Icon(
                                        imageVector = Icons.Default.DeleteOutline,
                                        contentDescription = "ゴミ箱を開く",
                                        tint = MaterialTheme.colorScheme.onPrimary,
                                        modifier = Modifier.size(20.dp)
                                    )
                                }
                            }
                        }
                    }
                }
            }
        },
        floatingActionButton = {
            if (!isSelectionMode) {
                FloatingActionButton(
                    onClick = onNavigateToAdd,
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.testTag("add_item_fab")
                ) {
                    Icon(imageVector = Icons.Default.Add, contentDescription = "新規追加")
                }
            }
        }
    ) { paddingValues ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(paddingValues)
        ) {
            // ① スリム化された上部サマリーバー
            Surface(
                color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.4f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 12.dp, vertical = 4.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // 期間トグル [ 今週 | 今月 | 全期間 ]
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        TimeRange.values().forEach { range ->
                            val isSelected = timeRange == range
                            Box(
                                modifier = Modifier
                                    .clip(RoundedCornerShape(10.dp))
                                    .background(if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent)
                                    .clickable {
                                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                        viewModel.setTimeRange(range)
                                    }
                                    .padding(horizontal = 7.dp, vertical = 2.dp)
                            ) {
                                Text(
                                    text = range.label,
                                    fontSize = 11.sp,
                                    fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                    color = if (isSelected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }

                    // 1行集計（売上と純利益）
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = "売上: ${formatter.format(periodSummary.totalSales)}",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            text = " | 利益: ",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        Text(
                            text = formatter.format(periodSummary.totalProfit),
                            fontSize = 12.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                }
            }

            // ② 検索バー & ソートメニュー & 表示切り替え
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 10.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { viewModel.setSearchQuery(it) },
                    placeholder = { Text("検索 (スペースでAND, OR, \"完全一致\")", fontSize = 11.sp) },
                    textStyle = androidx.compose.ui.text.TextStyle(fontSize = 12.sp),
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, modifier = Modifier.size(16.dp)) },
                    trailingIcon = {
                        if (searchQuery.isNotBlank()) {
                            IconButton(onClick = { viewModel.setSearchQuery("") }) {
                                Icon(Icons.Default.Clear, contentDescription = "クリア", modifier = Modifier.size(14.dp))
                            }
                        }
                    },
                    singleLine = true,
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedContainerColor = MaterialTheme.colorScheme.surface,
                        unfocusedContainerColor = MaterialTheme.colorScheme.surface
                    ),
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp)
                        .testTag("search_input")
                )

                Spacer(modifier = Modifier.width(4.dp))

                // まとめ買い選択ボタントグル
                IconButton(
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        viewModel.toggleSelectionMode()
                    },
                    modifier = Modifier
                        .size(38.dp)
                        .background(
                            if (isSelectionMode) MaterialTheme.colorScheme.primaryContainer else Color.Transparent,
                            shape = CircleShape
                        )
                        .testTag("search_merge_toggle")
                ) {
                    Icon(
                        imageVector = Icons.Default.MergeType,
                        contentDescription = "まとめ買い選択切替",
                        tint = if (isSelectionMode) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.size(18.dp)
                    )
                }

                // ソートボタンドロップダウン
                Box {
                    IconButton(
                        onClick = { showSortMenu = true },
                        modifier = Modifier.size(38.dp)
                    ) {
                        Icon(
                            imageVector = Icons.Default.Sort,
                            contentDescription = "ソート",
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.size(18.dp)
                        )
                    }

                    DropdownMenu(
                        expanded = showSortMenu,
                        onDismissRequest = { showSortMenu = false }
                    ) {
                        SortOption.values().forEach { option ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        text = option.label,
                                        fontWeight = if (sortOption == option) FontWeight.Bold else FontWeight.Normal,
                                        color = if (sortOption == option) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                                        fontSize = 13.sp
                                    )
                                },
                                onClick = {
                                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                    viewModel.setSortOption(option)
                                    showSortMenu = false
                                }
                            )
                        }
                    }
                }
            }

            // ③ ステータスフィルターチップ (レビュー §30.4-6: 狭幅でも潰れないようチップ側を横スクロール化)
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 10.dp, vertical = 2.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    modifier = Modifier
                        .weight(1f)
                        .horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    FilterChip(
                        selected = (statusFilter == "ALL"),
                        onClick = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.setStatusFilter("ALL")
                        },
                        label = { Text("すべて (${filteredItems.size})", fontSize = 11.sp) },
                        modifier = Modifier.height(28.dp)
                    )
                    FilterChip(
                        selected = (statusFilter == "DRAFT"),
                        onClick = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.setStatusFilter("DRAFT")
                        },
                        label = { Text("下書き", fontSize = 11.sp) },
                        modifier = Modifier.height(28.dp)
                    )
                    FilterChip(
                        selected = (statusFilter == "LISTED"),
                        onClick = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.setStatusFilter("LISTED")
                        },
                        label = { Text("出品中", fontSize = 11.sp) },
                        modifier = Modifier.height(28.dp)
                    )
                    FilterChip(
                        selected = (statusFilter == "SHIPPING"),
                        onClick = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.setStatusFilter("SHIPPING")
                        },
                        label = { Text("発送準備中", fontSize = 11.sp) },
                        modifier = Modifier.height(28.dp)
                    )
                    FilterChip(
                        selected = (statusFilter == "SOLD"),
                        onClick = {
                            haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            viewModel.setStatusFilter("SOLD")
                        },
                        label = { Text("売却済", fontSize = 11.sp) },
                        modifier = Modifier.height(28.dp)
                    )
                }
                Text(
                    text = "${visibleItems.size} / ${filteredItems.size}件",
                    fontSize = 11.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                )
            }

            Spacer(modifier = Modifier.height(4.dp))

            // ④ 一覧表示（コンパクトな2列グリッド ＆ 折りたたみボタン）
            if (filteredItems.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text("該当する商品がありません", color = Color.Gray)
                }
            } else {
                Box(modifier = Modifier.weight(1f)) {
                    LazyVerticalGrid(
                        columns = GridCells.Fixed(2),
                        contentPadding = PaddingValues(8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        modifier = Modifier.fillMaxSize()
                    ) {
                        items(visibleItems, key = { it.item.id }) { itemWithPhotos ->
                            val isSelected = selectedItemIds.contains(itemWithPhotos.item.id)
                            CompactItemCard(
                                itemWithPhotos = itemWithPhotos,
                                claudeLabel = claudeItemLabels[itemWithPhotos.item.id],
                                defaultFeePercent = defaultSettings.defaultFeeRatePercent,
                                defaultShipping = defaultSettings.defaultShippingCost,
                                defaultExpense = defaultSettings.defaultExpenseCost,
                                isSelectionMode = isSelectionMode,
                                isSelected = isSelected,
                                onSelectToggle = {
                                    if (itemWithPhotos.item.status != "MERGED" && itemWithPhotos.item.deletedAt == null) {
                                        haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                        viewModel.toggleItemSelection(itemWithPhotos.item.id)
                                    }
                                },
                                onClick = {
                                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                                    if (isSelectionMode) {
                                        if (itemWithPhotos.item.status != "MERGED" && itemWithPhotos.item.deletedAt == null) {
                                            viewModel.toggleItemSelection(itemWithPhotos.item.id)
                                        }
                                    } else {
                                        onNavigateToDetail(itemWithPhotos.item.id)
                                    }
                                },
                                onLongClick = {
                                    if (!isSelectionMode) {
                                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                                        quickActionItem = itemWithPhotos
                                    }
                                }
                            )
                        }

                        // 表示件数が全件未満の場合の「さらに10件表示」ボタン
                        if (filteredItems.size > displayLimit) {
                            item(span = { GridItemSpan(2) }) {
                                Column(
                                    modifier = Modifier
                                        .fillMaxWidth()
                                        .padding(vertical = 12.dp),
                                    horizontalAlignment = Alignment.CenterHorizontally
                                ) {
                                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        Button(
                                            onClick = { displayLimit += 10 },
                                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondaryContainer, contentColor = MaterialTheme.colorScheme.onSecondaryContainer),
                                            shape = RoundedCornerShape(20.dp)
                                        ) {
                                            Icon(Icons.Default.ExpandMore, contentDescription = null, modifier = Modifier.size(16.dp))
                                            Spacer(modifier = Modifier.width(4.dp))
                                            Text("さらに10件表示 (${displayLimit}/${filteredItems.size}件)", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                                        }

                                        OutlinedButton(
                                            onClick = { displayLimit = filteredItems.size },
                                            shape = RoundedCornerShape(20.dp)
                                        ) {
                                            Text("全件表示", fontSize = 12.sp)
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 複数選択時の下部一括操作アクションバー (1件以上で一括編集・ゴミ箱／2件以上かつ未売却でまとめ)
                    if (isSelectionMode && selectedItemIds.isNotEmpty()) {
                        val selectedItemsList = allActiveItems.filter { selectedItemIds.contains(it.item.id) }
                        val selectedSubItemCount = selectedItemsList.sumOf { if (it.item.mergedChildCount > 0) it.item.mergedChildCount else 1 }
                        val canMerge = selectedItemsList.size >= 2 && selectedItemsList.all {
                            it.item.status == "LISTED" || it.item.status == "DRAFT" || it.item.status == "DRAFT_DONE"
                        }
                        Surface(
                            shadowElevation = 8.dp,
                            color = MaterialTheme.colorScheme.primaryContainer,
                            modifier = Modifier
                                .fillMaxWidth()
                                .align(Alignment.BottomCenter)
                        ) {
                            Column(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 12.dp, vertical = 10.dp),
                                verticalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text(
                                        text = "${selectedItemsList.size}件 (${selectedSubItemCount}点) 選択中",
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 13.sp,
                                        color = MaterialTheme.colorScheme.onPrimaryContainer
                                    )
                                    if (selectedItemsList.size >= 2 && !canMerge) {
                                        Text(
                                            text = "※売却済・発送準備中を含むためまとめ不可",
                                            fontSize = 10.sp,
                                            color = MaterialTheme.colorScheme.error
                                        )
                                    }
                                }
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(6.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Button(
                                        onClick = { showBatchEditDialog = true },
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("batch_edit_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.Edit, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("一括編集", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                    Button(
                                        onClick = { showBatchTrashConfirmDialog = true },
                                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("batch_trash_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.DeleteOutline, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("ゴミ箱へ", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                    Button(
                                        onClick = { showMergeDialog = true },
                                        enabled = canMerge,
                                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.tertiary),
                                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 6.dp),
                                        modifier = Modifier
                                            .weight(1f)
                                            .testTag("merge_action_button")
                                    ) {
                                        Icon(imageVector = Icons.Default.MergeType, contentDescription = null, modifier = Modifier.size(15.dp))
                                        Spacer(modifier = Modifier.width(4.dp))
                                        Text("まとめる", fontWeight = FontWeight.Bold, fontSize = 12.sp, maxLines = 1)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
