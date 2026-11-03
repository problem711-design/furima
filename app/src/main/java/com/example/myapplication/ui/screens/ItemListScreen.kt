package com.example.myapplication.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Clear
import androidx.compose.material.icons.filled.MergeType
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Sort
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import com.example.myapplication.data.local.ItemWithPhotos
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.ui.SortOption
import com.example.myapplication.ui.TimeRange
import com.example.myapplication.ui.components.MergeItemsDialog
import java.io.File
import java.text.NumberFormat
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ItemListScreen(
    viewModel: FleaMarketViewModel,
    onNavigateToDetail: (Int) -> Unit,
    onNavigateToAdd: () -> Unit
) {
    val context = LocalContext.current
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

    var showSortMenu by remember { mutableStateOf(false) }
    var showMergeDialog by remember { mutableStateOf(false) }

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)

    // 検索をまたいで選択されたすべてのアイテムを確実に参照
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
                    childItemIds = itemIds
                ) { newParentId ->
                    showMergeDialog = false
                    onNavigateToDetail(newParentId.toInt())
                }
            }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        if (isSelectionMode) "まとめ選択 (${selectedItemIds.size}件)" else "フリマ出品・売上管理",
                        fontWeight = FontWeight.Bold,
                        fontSize = 18.sp
                    )
                },
                actions = {
                    if (isSelectionMode) {
                        TextButton(
                            onClick = { viewModel.clearSelection() },
                            colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.onPrimary)
                        ) {
                            Text("完了")
                        }
                    } else {
                        IconButton(
                            onClick = { viewModel.toggleSelectionMode() },
                            modifier = Modifier.testTag("select_mode_button")
                        ) {
                            Icon(
                                imageVector = Icons.Default.MergeType,
                                contentDescription = "まとめ買い選択",
                                tint = MaterialTheme.colorScheme.onPrimary
                            )
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.primary,
                    titleContentColor = MaterialTheme.colorScheme.onPrimary
                )
            )
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
            // ① スマートな上部サマリーバー（高さ圧縮＆トグル切り替え）
            Surface(
                color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.5f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        // 期間トグル [ 今週 | 今月 | 全期間 ]
                        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                            TimeRange.values().forEach { range ->
                                val isSelected = timeRange == range
                                Box(
                                    modifier = Modifier
                                        .clip(RoundedCornerShape(12.dp))
                                        .background(if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent)
                                        .clickable { viewModel.setTimeRange(range) }
                                        .padding(horizontal = 8.dp, vertical = 3.dp)
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
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                    }
                }
            }

            // ② 検索バー & まとめ買い選択ボタン & ソートメニュー
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                OutlinedTextField(
                    value = searchQuery,
                    onValueChange = { viewModel.setSearchQuery(it) },
                    placeholder = { Text("商品名・説明文で検索", fontSize = 12.sp) },
                    leadingIcon = { Icon(Icons.Default.Search, contentDescription = null, modifier = Modifier.size(18.dp)) },
                    trailingIcon = {
                        if (searchQuery.isNotBlank()) {
                            IconButton(onClick = { viewModel.setSearchQuery("") }) {
                                Icon(Icons.Default.Clear, contentDescription = "クリア", modifier = Modifier.size(16.dp))
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

                Spacer(modifier = Modifier.width(6.dp))

                // 検索画面からワンタップでまとめ買い選択モードを切り替えるボタン
                IconButton(
                    onClick = { viewModel.toggleSelectionMode() },
                    modifier = Modifier
                        .size(44.dp)
                        .background(
                            if (isSelectionMode) MaterialTheme.colorScheme.primaryContainer else Color.Transparent,
                            shape = CircleShape
                        )
                        .testTag("search_merge_toggle")
                ) {
                    Icon(
                        imageVector = Icons.Default.MergeType,
                        contentDescription = "まとめ買い選択切替",
                        tint = if (isSelectionMode) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface
                    )
                }

                Box {
                    IconButton(
                        onClick = { showSortMenu = true },
                        modifier = Modifier.size(44.dp).testTag("sort_button")
                    ) {
                        Icon(imageVector = Icons.Default.Sort, contentDescription = "ソート")
                    }

                    DropdownMenu(
                        expanded = showSortMenu,
                        onDismissRequest = { showSortMenu = false }
                    ) {
                        SortOption.values().forEach { option ->
                            DropdownMenuItem(
                                text = { Text(option.label, fontSize = 13.sp, fontWeight = if (sortOption == option) FontWeight.Bold else FontWeight.Normal) },
                                onClick = {
                                    viewModel.setSortOption(option)
                                    showSortMenu = false
                                }
                            )
                        }
                    }
                }
            }

            // 検索中またはまとめ選択時のヒントバー
            if (isSelectionMode) {
                Surface(
                    color = MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.7f),
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 2.dp),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Row(
                        modifier = Modifier.padding(horizontal = 10.dp, vertical = 4.dp),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "💡 チェックを入れると複数選択できます（検索語を変えても選択は保持されます）",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSecondaryContainer,
                            modifier = Modifier.weight(1f)
                        )
                        if (selectedItemIds.isNotEmpty()) {
                            Text(
                                text = "${selectedItemIds.size}件",
                                fontSize = 12.sp,
                                fontWeight = FontWeight.Bold,
                                color = MaterialTheme.colorScheme.primary
                            )
                        }
                    }
                }
            } else if (searchQuery.isNotBlank()) {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 2.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = "検索結果: ${filteredItems.size}件",
                        fontSize = 11.sp,
                        color = Color.Gray
                    )
                    OutlinedButton(
                        onClick = { viewModel.toggleSelectionMode() },
                        modifier = Modifier.height(28.dp),
                        contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp)
                    ) {
                        Icon(Icons.Default.MergeType, contentDescription = null, modifier = Modifier.size(14.dp))
                        Spacer(modifier = Modifier.width(4.dp))
                        Text("この結果からまとめ買い", fontSize = 10.sp)
                    }
                }
            }

            // ③ ステータスフィルターチップ
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 12.dp, vertical = 2.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp)
            ) {
                FilterChip(
                    selected = (statusFilter == "ALL"),
                    onClick = { viewModel.setStatusFilter("ALL") },
                    label = { Text("すべて (${filteredItems.size})", fontSize = 11.sp) },
                    modifier = Modifier.height(32.dp)
                )
                FilterChip(
                    selected = (statusFilter == "LISTED"),
                    onClick = { viewModel.setStatusFilter("LISTED") },
                    label = { Text("出品中", fontSize = 11.sp) },
                    modifier = Modifier.height(32.dp)
                )
                FilterChip(
                    selected = (statusFilter == "SOLD"),
                    onClick = { viewModel.setStatusFilter("SOLD") },
                    label = { Text("売却済", fontSize = 11.sp) },
                    modifier = Modifier.height(32.dp)
                )
            }

            Spacer(modifier = Modifier.height(4.dp))

            // ④ 一覧表示（コンパクトな2列グリッド）
            if (filteredItems.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text("該当する商品がありません", color = Color.Gray)
                }
            } else {
                Box(modifier = Modifier.weight(1f)) {
                    LazyVerticalGrid(
                        columns = GridCells.Fixed(2),
                        contentPadding = PaddingValues(10.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                        modifier = Modifier.fillMaxSize()
                    ) {
                        items(filteredItems, key = { it.item.id }) { itemWithPhotos ->
                            val isSelected = selectedItemIds.contains(itemWithPhotos.item.id)
                            CompactItemCard(
                                itemWithPhotos = itemWithPhotos,
                                defaultFeePercent = defaultSettings.defaultFeeRatePercent,
                                defaultShipping = defaultSettings.defaultShippingCost,
                                defaultExpense = defaultSettings.defaultExpenseCost,
                                isSelectionMode = isSelectionMode,
                                isSelected = isSelected,
                                onSelectToggle = { viewModel.toggleItemSelection(itemWithPhotos.item.id) },
                                onClick = {
                                    if (isSelectionMode) {
                                        viewModel.toggleItemSelection(itemWithPhotos.item.id)
                                    } else {
                                        onNavigateToDetail(itemWithPhotos.item.id)
                                    }
                                }
                            )
                        }
                    }

                    // 複数選択時の下部「まとめ買い」アクションバー
                    if (isSelectionMode && selectedItemIds.size >= 2) {
                        Surface(
                            shadowElevation = 8.dp,
                            color = MaterialTheme.colorScheme.primaryContainer,
                            modifier = Modifier
                                .fillMaxWidth()
                                .align(Alignment.BottomCenter)
                        ) {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(12.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Column {
                                    Text(
                                        text = "${selectedItemIds.size}点の商品を選択中",
                                        fontWeight = FontWeight.Bold,
                                        fontSize = 13.sp,
                                        color = MaterialTheme.colorScheme.onPrimaryContainer
                                    )
                                    Text(
                                        text = "新売価や送料を設定して1出品に統合",
                                        fontSize = 11.sp,
                                        color = MaterialTheme.colorScheme.onPrimaryContainer.copy(alpha = 0.8f)
                                    )
                                }

                                Button(
                                    onClick = { showMergeDialog = true },
                                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
                                    modifier = Modifier.testTag("merge_action_button")
                                ) {
                                    Icon(imageVector = Icons.Default.MergeType, contentDescription = null, modifier = Modifier.size(16.dp))
                                    Spacer(modifier = Modifier.width(6.dp))
                                    Text("出品をまとめる", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun CompactItemCard(
    itemWithPhotos: ItemWithPhotos,
    defaultFeePercent: Int,
    defaultShipping: Int,
    defaultExpense: Int,
    isSelectionMode: Boolean,
    isSelected: Boolean,
    onSelectToggle: () -> Unit,
    onClick: () -> Unit
) {
    val item = itemWithPhotos.item
    val photos = itemWithPhotos.photos
    val firstPhoto = photos.firstOrNull()

    val formatter = NumberFormat.getCurrencyInstance(Locale.JAPAN)
    val basePrice = if (item.status == "SOLD" && item.soldPrice > 0) item.soldPrice else item.price

    val effectiveFee = if (item.useCustomSettings) item.feeRatePercent else defaultFeePercent
    val effectiveShipping = if (item.useCustomSettings) item.shippingCost else defaultShipping
    val effectiveExpense = if (item.useCustomSettings) item.expenseCost else defaultExpense
    val fee = (basePrice * effectiveFee) / 100
    val profit = (basePrice - fee - effectiveShipping - effectiveExpense).coerceAtLeast(0)

    val displayImageFile = firstPhoto?.let { p ->
        p.thumbnailPath?.let { File(it) }?.takeIf { it.exists() } ?: File(p.filePath)
    }

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .border(
                width = if (isSelected) 2.dp else 0.dp,
                color = if (isSelected) MaterialTheme.colorScheme.primary else Color.Transparent,
                shape = RoundedCornerShape(12.dp)
            )
            .testTag("item_card_${item.id}"),
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.3f) else MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Column {
            // 写真エリア
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(1.2f)
                    .background(Color.LightGray)
            ) {
                if (displayImageFile != null && displayImageFile.exists()) {
                    AsyncImage(
                        model = displayImageFile,
                        contentDescription = item.title,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                } else {
                    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text("No Photo", color = Color.DarkGray, fontSize = 11.sp)
                    }
                }

                // ステータスバッジ
                Box(
                    modifier = Modifier
                        .padding(4.dp)
                        .clip(RoundedCornerShape(4.dp))
                        .background(
                            when (item.status) {
                                "SOLD" -> Color(0xFFC62828)
                                "MERGED" -> Color(0xFF6A1B9A)
                                "DRAFT" -> Color.Gray
                                else -> MaterialTheme.colorScheme.primary
                            }
                        )
                        .padding(horizontal = 6.dp, vertical = 2.dp)
                        .align(Alignment.TopStart)
                ) {
                    Text(
                        text = when (item.status) {
                            "SOLD" -> "売却済"
                            "MERGED" -> "まとめ済"
                            "DRAFT" -> "下書き"
                            else -> "出品中"
                        },
                        color = Color.White,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold
                    )
                }

                // PC送信済みバッジ
                if (item.isShared) {
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .clip(CircleShape)
                            .background(Color(0xFF2E7D32))
                            .padding(2.dp)
                            .align(Alignment.TopEnd)
                    ) {
                        Icon(
                            imageVector = Icons.Default.Check,
                            contentDescription = "PC送信済",
                            tint = Color.White,
                            modifier = Modifier.size(12.dp)
                        )
                    }
                }

                // 複数選択チェックボックス
                if (isSelectionMode) {
                    Box(
                        modifier = Modifier
                            .padding(4.dp)
                            .align(Alignment.BottomEnd)
                    ) {
                        Checkbox(
                            checked = isSelected,
                            onCheckedChange = { onSelectToggle() },
                            modifier = Modifier.size(24.dp)
                        )
                    }
                }
            }

            // テキスト情報エリア（無駄な余白を削ったスリム設計）
            Column(modifier = Modifier.padding(8.dp)) {
                Text(
                    text = item.title,
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 13.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )

                Spacer(modifier = Modifier.height(2.dp))

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = formatter.format(basePrice),
                        fontWeight = FontWeight.Bold,
                        fontSize = 13.sp
                    )

                    Text(
                        text = "利 ${formatter.format(profit)}",
                        fontWeight = FontWeight.Bold,
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.primary
                    )
                }
            }
        }
    }
}
