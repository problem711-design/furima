package com.example.myapplication

import android.app.Activity
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Analytics
import androidx.compose.material.icons.filled.Computer
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.example.myapplication.ui.FleaMarketViewModel
import com.example.myapplication.ui.screens.ItemDetailScreen
import com.example.myapplication.ui.screens.ItemEditScreen
import com.example.myapplication.ui.screens.ItemListScreen
import com.example.myapplication.ui.screens.PcSyncScreen
import com.example.myapplication.ui.screens.SalesAnalysisScreen
import com.example.myapplication.ui.screens.SettingsStorageScreen
import com.example.myapplication.ui.screens.TrashScreen
import com.example.myapplication.ui.theme.MyApplicationTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            MyApplicationTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = MaterialTheme.colorScheme.background
                ) {
                    FleaMarketApp()
                }
            }
        }
    }
}

@Composable
fun FleaMarketApp() {
    val navController = rememberNavController()
    val viewModel: FleaMarketViewModel = viewModel()

    NavHost(
        navController = navController,
        startDestination = "main_tabs"
    ) {
        composable("main_tabs") {
            MainTabsContainer(
                viewModel = viewModel,
                onNavigateToDetail = { itemId ->
                    navController.navigate("item_detail/$itemId")
                },
                onNavigateToAdd = {
                    navController.navigate("item_edit/0")
                },
                onNavigateToEdit = { itemId ->
                    navController.navigate("item_edit/$itemId")
                },
                onNavigateToTrash = {
                    navController.navigate("trash")
                }
            )
        }

        composable("trash") {
            TrashScreen(
                viewModel = viewModel,
                onNavigateBack = {
                    navController.popBackStack()
                }
            )
        }

        composable(
            route = "item_detail/{itemId}",
            arguments = listOf(navArgument("itemId") { type = NavType.IntType })
        ) { backStackEntry ->
            val itemId = backStackEntry.arguments?.getInt("itemId") ?: 0
            ItemDetailScreen(
                itemId = itemId,
                viewModel = viewModel,
                onNavigateToEdit = { editItemId ->
                    navController.navigate("item_edit/$editItemId")
                },
                onNavigateBack = {
                    navController.popBackStack()
                }
            )
        }

        composable(
            route = "item_edit/{itemId}",
            arguments = listOf(navArgument("itemId") { type = NavType.IntType })
        ) { backStackEntry ->
            val itemId = backStackEntry.arguments?.getInt("itemId") ?: 0
            ItemEditScreen(
                itemId = itemId,
                viewModel = viewModel,
                onNavigateBack = {
                    navController.popBackStack()
                }
            )
        }
    }
}

@Composable
fun MainTabsContainer(
    viewModel: FleaMarketViewModel,
    onNavigateToDetail: (Int) -> Unit,
    onNavigateToAdd: () -> Unit,
    onNavigateToEdit: (Int) -> Unit,
    onNavigateToTrash: () -> Unit
) {
    val context = LocalContext.current
    val isSelectionMode by viewModel.isSelectionMode.collectAsState()
    var selectedTab by remember { mutableIntStateOf(0) }
    var showExitConfirmDialog by remember { mutableStateOf(false) }

    BackHandler(enabled = true) {
        when {
            selectedTab != 0 -> selectedTab = 0
            isSelectionMode -> viewModel.clearSelection()
            else -> showExitConfirmDialog = true
        }
    }

    if (showExitConfirmDialog) {
        AlertDialog(
            onDismissRequest = { showExitConfirmDialog = false },
            title = { Text("アプリを終了しますか？") },
            text = { Text("フリマ出品・売上管理を終了しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        showExitConfirmDialog = false
                        (context as? Activity)?.finish()
                    },
                    modifier = Modifier.testTag("confirm_exit_app_button")
                ) {
                    Text("終了する")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = { showExitConfirmDialog = false },
                    modifier = Modifier.testTag("cancel_exit_app_button")
                ) {
                    Text("キャンセル")
                }
            }
        )
    }

    Scaffold(
        bottomBar = {
            NavigationBar {
                NavigationBarItem(
                    selected = (selectedTab == 0),
                    onClick = { selectedTab = 0 },
                    icon = { Icon(Icons.Default.Inventory2, contentDescription = "出品一覧") },
                    label = { Text("出品一覧") }
                )
                NavigationBarItem(
                    selected = (selectedTab == 1),
                    onClick = { selectedTab = 1 },
                    icon = { Icon(Icons.Default.Computer, contentDescription = "PC連携") },
                    label = { Text("PC連携") }
                )
                NavigationBarItem(
                    selected = (selectedTab == 2),
                    onClick = { selectedTab = 2 },
                    icon = { Icon(Icons.Default.Analytics, contentDescription = "売上分析") },
                    label = { Text("売上分析") }
                )
                NavigationBarItem(
                    selected = (selectedTab == 3),
                    onClick = { selectedTab = 3 },
                    icon = { Icon(Icons.Default.Settings, contentDescription = "設定・容量") },
                    label = { Text("設定・容量") }
                )
            }
        }
    ) { paddingValues ->
        Surface(modifier = Modifier.padding(paddingValues)) {
            when (selectedTab) {
                0 -> ItemListScreen(
                    viewModel = viewModel,
                    onNavigateToDetail = onNavigateToDetail,
                    onNavigateToAdd = onNavigateToAdd,
                    onNavigateToEdit = onNavigateToEdit
                )
                1 -> PcSyncScreen(
                    viewModel = viewModel
                )
                2 -> SalesAnalysisScreen(
                    viewModel = viewModel
                )
                3 -> SettingsStorageScreen(
                    viewModel = viewModel
                )
            }
        }
    }
}
