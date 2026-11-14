package com.example.myapplication.ui.components

import android.widget.Toast
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
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
import androidx.compose.material.icons.filled.Hd
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
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
    onDismiss: () -> Unit
) {
    if (photos.isEmpty()) return

    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val safeInitial = initialIndex.coerceIn(0, photos.lastIndex)
    val pagerState = rememberPagerState(initialPage = safeInitial, pageCount = { photos.size })

    val cachedOriginals = remember { mutableStateMapOf<String, File>() }
    var isFetchingHash by remember { mutableStateOf<String?>(null) }

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
                val photo = photos[pageIndex]
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

            // 上部ヘッダーエリア (ページ番号 + 原本/サムネ表示バッジ + 閉じるボタン + 原本取得ボタン)
            val activePhoto = photos[pagerState.currentPage]
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
                                text = "${pagerState.currentPage + 1} / ${photos.size}",
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

            // 下部操作ガイドバー (システムナビゲーションバーと重ならないシンプル案内)
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .align(Alignment.BottomCenter)
                    .background(Color.Black.copy(alpha = 0.45f))
                    .navigationBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 10.dp),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "ピンチで拡大縮小・ダブルタップでズーム切替・左右スワイプで移動",
                    color = Color.White.copy(alpha = 0.75f),
                    fontSize = 11.sp
                )
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
                detectTransformGestures { _, pan, zoom, _ ->
                    val nextScale = (scale * zoom).coerceIn(1f, 4f)
                    if (nextScale <= 1.01f) {
                        scale = 1f
                        offsetX = 0f
                        offsetY = 0f
                    } else {
                        scale = nextScale
                        val maxOffsetX = (containerSize.width * (scale - 1f)) / 2f
                        val maxOffsetY = (containerSize.height * (scale - 1f)) / 2f
                        offsetX = (offsetX + pan.x).coerceIn(-maxOffsetX, maxOffsetX)
                        offsetY = (offsetY + pan.y).coerceIn(-maxOffsetY, maxOffsetY)
                    }
                    onScaleChanged(scale)
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
