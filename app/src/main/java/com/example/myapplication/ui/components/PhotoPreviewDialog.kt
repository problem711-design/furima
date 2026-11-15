package com.example.myapplication.ui.components

import android.widget.Toast
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.calculatePan
import androidx.compose.foundation.gestures.calculateZoom
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.CloudDownload
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Hd
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import coil.compose.AsyncImage
import com.example.myapplication.data.local.PhotoEntity
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.io.File

data class PreviewPhotoItem(
    val model: Any?,
    val md5Hash: String = "",
    val hasLocalOriginal: Boolean = true
)

fun PhotoEntity.toPreviewPhotoItem(): PreviewPhotoItem {
    val origFile = filePath.takeIf { it.isNotBlank() }?.let { File(it) }
    val origExists = !isOriginalDeleted &&
        origFile != null &&
        origFile.exists() &&
        !origFile.absolutePath.equals(thumbnailPath, ignoreCase = true)
    return PreviewPhotoItem(
        model = if (origExists) origFile else displayFile,
        md5Hash = md5Hash,
        hasLocalOriginal = origExists
    )
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun PhotoPreviewDialog(
    photos: List<PreviewPhotoItem>,
    initialIndex: Int = 0,
    isPcServerConnected: Boolean = false,
    onFindCachedOriginal: (String) -> File? = { null },
    onFetchOriginal: suspend (String) -> File? = { null },
    confirmBeforeDelete: Boolean = false,
    onDeletePhoto: ((Int) -> Unit)? = null,
    onUndoDelete: (() -> Unit)? = null,
    onDismiss: () -> Unit
) {
    if (photos.isEmpty()) {
        LaunchedEffect(Unit) {
            onDismiss()
        }
        return
    }

    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val safeInitial = initialIndex.coerceIn(0, photos.lastIndex)
    val pagerState = rememberPagerState(initialPage = safeInitial, pageCount = { photos.size })

    val cachedOriginals = remember { mutableStateMapOf<String, File>() }
    var isFetchingHash by remember { mutableStateOf<String?>(null) }
    var showDeleteConfirmDialog by remember { mutableStateOf(false) }
    var showUndoBanner by remember { mutableStateOf(false) }
    var undoResetNonce by remember { mutableIntStateOf(0) }
    var lastDeletedPageIndex by remember { mutableIntStateOf(-1) }
    var pendingRestoreScrollIndex by remember { mutableStateOf<Int?>(null) }

    LaunchedEffect(photos.size) {
        if (photos.isNotEmpty() && pagerState.currentPage >= photos.size) {
            pagerState.scrollToPage((photos.size - 1).coerceAtLeast(0))
        }
        val restoreTarget = pendingRestoreScrollIndex
        if (restoreTarget != null && photos.isNotEmpty()) {
            pendingRestoreScrollIndex = null
            pagerState.animateScrollToPage(restoreTarget.coerceIn(0, photos.lastIndex))
        }
    }

    LaunchedEffect(undoResetNonce) {
        if (showUndoBanner) {
            delay(3500L)
            showUndoBanner = false
        }
    }

    val performDeleteAtCurrentPage: () -> Unit = {
        val deleteIdx = pagerState.currentPage.coerceIn(0, photos.lastIndex)
        lastDeletedPageIndex = deleteIdx
        if (onUndoDelete != null && photos.size > 1) {
            showUndoBanner = true
            undoResetNonce++
        }
        onDeletePhoto?.invoke(deleteIdx)
    }

    // すでにキャッシュ取得済みの原本があれば自動セット
    LaunchedEffect(pagerState.currentPage, photos) {
        val current = photos.getOrNull(pagerState.currentPage) ?: return@LaunchedEffect
        val hash = current.md5Hash.trim().lowercase()
        if (!current.hasLocalOriginal && hash.isNotBlank() && !cachedOriginals.containsKey(hash)) {
            onFindCachedOriginal(hash)?.let { cached ->
                cachedOriginals[hash] = cached
            }
        }
    }

    var currentScale by remember { mutableFloatStateOf(1f) }
    LaunchedEffect(pagerState.currentPage) {
        currentScale = 1f
    }

    if (showDeleteConfirmDialog) {
        AlertDialog(
            onDismissRequest = { showDeleteConfirmDialog = false },
            icon = { Icon(Icons.Default.Delete, contentDescription = null, tint = Color.Red) },
            title = { Text("写真を削除") },
            text = { Text("この写真を一覧から削除しますか？") },
            confirmButton = {
                Button(
                    onClick = {
                        showDeleteConfirmDialog = false
                        performDeleteAtCurrentPage()
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                    modifier = Modifier.testTag("confirm_delete_photo_preview_button")
                ) {
                    Text("削除する")
                }
            },
            dismissButton = {
                TextButton(onClick = { showDeleteConfirmDialog = false }) {
                    Text("キャンセル")
                }
            }
        )
    }

    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(
            usePlatformDefaultWidth = false,
            decorFitsSystemWindows = false
        )
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
                .testTag("photo_preview_dialog")
        ) {
            HorizontalPager(
                state = pagerState,
                userScrollEnabled = currentScale <= 1.05f,
                modifier = Modifier.fillMaxSize()
            ) { pageIndex ->
                val photo = photos.getOrNull(pageIndex) ?: return@HorizontalPager
                val hash = photo.md5Hash.trim().lowercase()
                val displayModel = cachedOriginals[hash] ?: photo.model

                ZoomablePhotoPage(
                    model = displayModel,
                    contentDescription = "写真プレビュー ${pageIndex + 1} / ${photos.size}",
                    isActivePage = (pageIndex == pagerState.currentPage),
                    onScaleChanged = { newScale ->
                        if (pageIndex == pagerState.currentPage) {
                            currentScale = newScale
                        }
                    }
                )
            }

            // 上部ヘッダーエリア (ページ番号 + 原本/サムネ表示バッジ + 削除ボタン + 閉じるボタン + 原本取得ボタン)
            val safeCurrentPage = pagerState.currentPage.coerceIn(0, photos.lastIndex)
            val activePhoto = photos[safeCurrentPage]
            val activeHash = activePhoto.md5Hash.trim().lowercase()
            val hasLoadedOriginal = activePhoto.hasLocalOriginal || cachedOriginals.containsKey(activeHash)

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.TopCenter)
                    .background(Color.Black.copy(alpha = 0.55f))
                    .statusBarsPadding()
                    .padding(horizontal = 12.dp, vertical = 8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Surface(
                            color = Color.White.copy(alpha = 0.18f),
                            shape = RoundedCornerShape(16.dp)
                        ) {
                            Text(
                                text = "${safeCurrentPage + 1} / ${photos.size}",
                                color = Color.White,
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier
                                    .padding(horizontal = 10.dp, vertical = 4.dp)
                                    .testTag("photo_preview_counter")
                            )
                        }

                        Surface(
                            color = if (hasLoadedOriginal) Color(0xFF2E7D32).copy(alpha = 0.85f) else Color(0xFF475569).copy(alpha = 0.85f),
                            shape = RoundedCornerShape(16.dp)
                        ) {
                            Row(
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                if (hasLoadedOriginal) {
                                    Icon(
                                        imageVector = Icons.Default.Hd,
                                        contentDescription = null,
                                        tint = Color.White,
                                        modifier = Modifier.size(14.dp)
                                    )
                                    Spacer(modifier = Modifier.width(3.dp))
                                }
                                Text(
                                    text = if (hasLoadedOriginal) "高画質原本" else "サムネイル表示",
                                    color = Color.White,
                                    fontSize = 11.sp,
                                    fontWeight = FontWeight.SemiBold,
                                    modifier = Modifier.testTag("photo_preview_quality_badge")
                                )
                            }
                        }
                    }

                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        if (onDeletePhoto != null) {
                            IconButton(
                                onClick = {
                                    if (confirmBeforeDelete) {
                                        showDeleteConfirmDialog = true
                                    } else {
                                        performDeleteAtCurrentPage()
                                    }
                                },
                                modifier = Modifier
                                    .size(48.dp)
                                    .clip(CircleShape)
                                    .background(Color.Red.copy(alpha = 0.25f))
                                    .testTag("photo_preview_delete_button")
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Delete,
                                    contentDescription = "写真を削除",
                                    tint = Color.Red
                                )
                            }
                        }

                        IconButton(
                            onClick = onDismiss,
                            modifier = Modifier
                                .size(48.dp)
                                .clip(CircleShape)
                                .background(Color.White.copy(alpha = 0.16f))
                                .testTag("photo_preview_close_button")
                        ) {
                            Icon(
                                imageVector = Icons.Default.Close,
                                contentDescription = "閉じる",
                                tint = Color.White
                            )
                        }
                    }
                }

                // 端末に原本がなくPCサーバー接続中のときのみ上部へ「PCから高画質原本を取得」を表示
                if (!hasLoadedOriginal && activeHash.isNotBlank() && isPcServerConnected) {
                    val fetchingThis = (isFetchingHash == activeHash)
                    Button(
                        enabled = !fetchingThis,
                        onClick = {
                            if (isFetchingHash == null) {
                                isFetchingHash = activeHash
                                scope.launch {
                                    val fetched = onFetchOriginal(activeHash)
                                    isFetchingHash = null
                                    if (fetched != null && fetched.exists()) {
                                        cachedOriginals[activeHash] = fetched
                                        Toast.makeText(context, "PCから高画質原本を取得しました", Toast.LENGTH_SHORT).show()
                                    } else {
                                        Toast.makeText(context, "PCサーバーから原本を取得できませんでした", Toast.LENGTH_SHORT).show()
                                    }
                                }
                            }
                        },
                        colors = ButtonDefaults.buttonColors(
                            containerColor = MaterialTheme.colorScheme.primary,
                            contentColor = Color.White
                        ),
                        shape = RoundedCornerShape(24.dp),
                        modifier = Modifier
                            .align(Alignment.CenterHorizontally)
                            .height(48.dp)
                            .testTag("fetch_original_preview_button")
                    ) {
                        if (fetchingThis) {
                            CircularProgressIndicator(
                                color = Color.White,
                                strokeWidth = 2.dp,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("原本を取得中...", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        } else {
                            Icon(
                                imageVector = Icons.Default.CloudDownload,
                                contentDescription = null,
                                modifier = Modifier.size(18.dp)
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("PCから高画質原本を取得", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        }
                    }
                }
            }

            // 下部Undoバナー + 操作ガイドバー (システムナビゲーションバーと重ならない配置)
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.BottomCenter),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                AnimatedVisibility(
                    visible = showUndoBanner && onUndoDelete != null,
                    enter = fadeIn() + slideInVertically { it / 2 },
                    exit = fadeOut() + slideOutVertically { it / 2 }
                ) {
                    Surface(
                        color = Color(0xFF1E293B).copy(alpha = 0.94f),
                        shape = RoundedCornerShape(24.dp),
                        shadowElevation = 6.dp,
                        modifier = Modifier
                            .padding(horizontal = 16.dp, vertical = 8.dp)
                            .testTag("photo_preview_undo_banner")
                    ) {
                        Row(
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            modifier = Modifier.padding(start = 16.dp, end = 6.dp, top = 2.dp, bottom = 2.dp)
                        ) {
                            Text(
                                text = "1枚削除しました",
                                color = Color.White,
                                fontSize = 13.sp,
                                fontWeight = FontWeight.Medium
                            )
                            TextButton(
                                onClick = {
                                    showUndoBanner = false
                                    if (lastDeletedPageIndex >= 0) {
                                        pendingRestoreScrollIndex = lastDeletedPageIndex
                                    }
                                    onUndoDelete?.invoke()
                                },
                                modifier = Modifier
                                    .height(48.dp)
                                    .testTag("photo_preview_undo_button")
                            ) {
                                Text(
                                    text = "元に戻す",
                                    color = Color(0xFF38BDF8),
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 13.sp
                                )
                            }
                        }
                    }
                }

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(Color.Black.copy(alpha = 0.45f))
                        .navigationBarsPadding()
                        .padding(horizontal = 16.dp, vertical = 10.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Text(
                        text = when {
                            onDeletePhoto != null && !confirmBeforeDelete ->
                                "ピンチで拡大・右上の削除で即除外・左右スワイプで選別"
                            onDeletePhoto != null ->
                                "ピンチで拡大縮小・ダブルタップでズーム・右上の削除ボタンで削除"
                            else ->
                                "ピンチで拡大縮小・ダブルタップでズーム切替・左右スワイプで移動"
                        },
                        color = Color.White.copy(alpha = 0.75f),
                        fontSize = 11.sp
                    )
                }
            }
        }
    }
}

@Composable
private fun ZoomablePhotoPage(
    model: Any?,
    contentDescription: String,
    isActivePage: Boolean,
    onScaleChanged: (Float) -> Unit
) {
    var scale by remember(model) { mutableFloatStateOf(1f) }
    var offsetX by remember(model) { mutableFloatStateOf(0f) }
    var offsetY by remember(model) { mutableFloatStateOf(0f) }
    var containerSize by remember { mutableStateOf(IntSize.Zero) }

    LaunchedEffect(isActivePage) {
        if (!isActivePage && scale != 1f) {
            scale = 1f
            offsetX = 0f
            offsetY = 0f
            onScaleChanged(1f)
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .onSizeChanged { containerSize = it }
            .pointerInput(model) {
                detectTapGestures(
                    onDoubleTap = {
                        if (scale > 1.05f) {
                            scale = 1f
                            offsetX = 0f
                            offsetY = 0f
                        } else {
                            scale = 2.5f
                        }
                        onScaleChanged(scale)
                    }
                )
            }
            .pointerInput(model) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false)
                    do {
                        val event = awaitPointerEvent()
                        val pressedCount = event.changes.count { it.pressed }
                        val zoomChange = event.calculateZoom()
                        val panChange = event.calculatePan()
                        val isMultiTouch = pressedCount >= 2 || kotlin.math.abs(zoomChange - 1f) > 0.01f
                        if (isMultiTouch || scale > 1.05f) {
                            val nextScale = (scale * zoomChange).coerceIn(1f, 4f)
                            if (nextScale <= 1.02f) {
                                scale = 1f
                                offsetX = 0f
                                offsetY = 0f
                            } else {
                                scale = nextScale
                                val maxOffsetX = (containerSize.width * (scale - 1f)) / 2f
                                val maxOffsetY = (containerSize.height * (scale - 1f)) / 2f
                                offsetX = (offsetX + panChange.x).coerceIn(-maxOffsetX, maxOffsetX)
                                offsetY = (offsetY + panChange.y).coerceIn(-maxOffsetY, maxOffsetY)
                            }
                            onScaleChanged(scale)
                            event.changes.forEach { it.consume() }
                        }
                    } while (event.changes.any { it.pressed })
                }
            },
        contentAlignment = Alignment.Center
    ) {
        if (model != null) {
            AsyncImage(
                model = model,
                contentDescription = contentDescription,
                contentScale = ContentScale.Fit,
                modifier = Modifier
                    .fillMaxSize()
                    .graphicsLayer {
                        scaleX = scale
                        scaleY = scale
                        translationX = offsetX
                        translationY = offsetY
                    }
            )
        } else {
            Text("画像を読み込めませんでした", color = Color.LightGray, fontSize = 14.sp)
        }
    }
}
