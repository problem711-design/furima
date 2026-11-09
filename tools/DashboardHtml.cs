using System;

public static class DashboardHtml
{
    public static string GetHtml()
    {
        return @"<!DOCTYPE html>
<html lang='ja'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Remix フリマ出品・商品PC管理 (母艦PCサーバー)</title>
    <script src='https://cdn.tailwindcss.com'></script>
    <link href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css' rel='stylesheet'>
    <style>
        .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }
        .custom-scroll::-webkit-scrollbar-track { background: #f1f5f9; }
        .custom-scroll::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }
    </style>
</head>
<body class='bg-slate-100 text-slate-800 min-h-screen'>
    <!-- スマホ接続ステータス通知バナー -->
    <div id='status-banner' class='bg-amber-500 text-white px-4 py-1.5 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40'>
        <div class='flex items-center gap-2'>
            <i id='status-icon' class='fa-solid fa-triangle-exclamation text-base animate-bounce'></i>
            <span id='status-text'>スマホアプリ未接続（操作・編集ロック中）。スマホアプリの「PC連携」画面から接続を開始してください。</span>
        </div>
        <span class='bg-amber-700/60 px-2 py-0.5 rounded text-[11px] font-mono'>v2.1 (Build 15)</span>
    </div>

    <!-- ナビゲーションバー (高密度・インライン統合) -->
    <header class='bg-indigo-700 text-white shadow-md sticky top-7 z-30'>
        <div class='max-w-[1800px] mx-auto px-3 sm:px-4 h-12 flex items-center justify-between gap-3'>
            <!-- 左側: タイトル & 接続ステータス -->
            <div class='flex items-center space-x-2 shrink-0'>
                <i class='fa-solid fa-boxes-stacked text-lg text-indigo-200'></i>
                <h1 class='font-black text-sm tracking-tight whitespace-nowrap'>フリマ商品PC管理</h1>
                <span class='bg-indigo-800/90 text-indigo-100 text-[10px] px-1.5 py-0.5 rounded font-mono font-semibold border border-indigo-500'>母艦サーバー</span>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス -->
            <div class='flex items-center gap-2 flex-1 justify-end overflow-x-auto custom-scroll py-0.5'>
                <div class='flex items-center gap-1 bg-indigo-800/60 p-0.5 rounded-lg shrink-0'>
                    <button onclick='setFilter(""ALL"")' id='filter-all' class='filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition'>すべて (<span id='count-all'>0</span>)</button>
                    <button onclick='setFilter(""LISTED"")' id='filter-listed' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>出品中 (<span id='count-listed'>0</span>)</button>
                    <button onclick='setFilter(""UNSHARED"")' id='filter-unshared' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>未送信 (<span id='count-unshared'>0</span>)</button>
                    <button onclick='setFilter(""SOLD"")' id='filter-sold' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>売却済 (<span id='count-sold'>0</span>)</button>
                </div>

                <div class='relative w-40 lg:w-56 shrink-0'>
                    <i class='fa-solid fa-magnifying-glass absolute left-2.5 top-2 text-indigo-300 text-xs'></i>
                    <input type='text' id='search-input' oninput='renderItems()' placeholder='商品名・説明文で検索...' class='w-full pl-7 pr-2 py-1 text-xs bg-indigo-800/80 text-white placeholder-indigo-300 border border-indigo-500/50 rounded-md focus:outline-none focus:bg-indigo-900 focus:ring-1 focus:ring-white'>
                </div>
            </div>
        </div>
    </header>

    <!-- メインコンテンツ -->
    <main class='max-w-[1800px] mx-auto px-3 sm:px-4 py-4'>
        <div class='flex flex-col lg:flex-row gap-4 items-start'>
            <!-- 左側: メイン商品グリッド表示エリア (高密度レイアウト) -->
            <div class='flex-1 w-full min-w-0'>
                <div id='items-grid' class='grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-3'>
                    <!-- JavaScriptで動的生成 -->
                </div>

                <div id='empty-state' class='hidden text-center py-12 bg-white rounded-lg border border-slate-200'>
                    <i class='fa-solid fa-box-open text-3xl text-slate-300 mb-2'></i>
                    <p class='text-xs text-slate-500 font-medium'>該当する商品が見つかりません</p>
                </div>
            </div>

            <!-- 右側: 固定コントロールパネル (コンパクト化) -->
            <aside class='w-full lg:w-64 shrink-0 sticky top-24 space-y-3'>
                <div class='bg-white rounded-lg p-3 shadow-sm border border-slate-200 space-y-2'>
                    <h2 class='text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1'>
                        <i class='fa-solid fa-chart-pie text-indigo-500'></i> 実績分析サマリー
                    </h2>
                    <div class='grid grid-cols-2 gap-2'>
                        <div class='bg-blue-50/70 p-2 rounded border border-blue-100'>
                            <p class='text-[10px] font-medium text-slate-500'>出品中</p>
                            <p id='stat-listed-count' class='text-base font-black text-blue-900'>0品</p>
                        </div>
                        <div class='bg-emerald-50/70 p-2 rounded border border-emerald-100'>
                            <p class='text-[10px] font-medium text-slate-500'>売却済</p>
                            <p id='stat-sold-count' class='text-base font-black text-emerald-900'>0品</p>
                        </div>
                    </div>
                    <div class='space-y-1.5 pt-1'>
                        <div class='bg-slate-50 p-2 rounded border border-slate-100 flex items-center justify-between'>
                            <span class='text-[11px] text-slate-600 font-medium flex items-center gap-1'>
                                <i class='fa-solid fa-coins text-indigo-500'></i> 累計売上
                            </span>
                            <span id='stat-total-sales' class='text-sm font-black text-indigo-600'>¥0</span>
                        </div>
                        <div class='bg-amber-50/60 p-2 rounded border border-amber-100 flex items-center justify-between'>
                            <span class='text-[11px] text-slate-600 font-medium flex items-center gap-1'>
                                <i class='fa-solid fa-hand-holding-dollar text-amber-500'></i> 累計純利益
                            </span>
                            <span id='stat-total-profit' class='text-sm font-black text-amber-600'>¥0</span>
                        </div>
                    </div>
                </div>

                <div class='bg-white rounded-lg p-3 shadow-sm border border-slate-200 space-y-2'>
                    <h2 class='text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1'>
                        <i class='fa-solid fa-hard-drive text-indigo-500'></i> PC容量 & 写真整理
                    </h2>
                    <div class='bg-slate-50 p-2 rounded border border-slate-100 flex items-center justify-between'>
                        <span class='text-[11px] text-slate-600 font-medium'>保存フォルダ容量</span>
                        <span id='stat-storage-size' class='text-xs font-black text-slate-800'>...</span>
                    </div>
                    <button onclick='openCleanupModal()' class='w-full bg-amber-600 hover:bg-amber-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-broom text-xs'></i> 売却30日経過写真の整理
                    </button>
                </div>

                <div class='bg-white rounded-lg p-3 shadow-sm border border-slate-200 space-y-2'>
                    <h2 class='text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1'>
                        <i class='fa-solid fa-sliders text-indigo-500'></i> コントロール
                    </h2>
                    <button onclick='fetchItems()' class='w-full bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-rotate-right text-xs'></i> データ手動更新
                    </button>
                </div>
            </aside>
        </div>
    </main>

    <!-- 30日経過写真クリーンアップモーダル -->
    <div id='cleanup-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeCleanupModal()'>
        <div class='bg-white rounded-xl max-w-2xl w-full max-h-[85vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <!-- モーダルヘッダー -->
            <div class='bg-slate-900 text-white px-5 py-3.5 flex items-center justify-between border-b border-slate-800'>
                <div class='flex items-center gap-2.5'>
                    <i class='fa-solid fa-broom text-amber-400 text-lg'></i>
                    <div>
                        <h3 class='font-bold text-sm tracking-tight'>売却30日経過写真のクリーンアップ</h3>
                        <p class='text-[11px] text-slate-400'>売却成立から30日以上経過した商品の写真ファイルを安全に一括削除します</p>
                    </div>
                </div>
                <button onclick='closeCleanupModal()' class='text-slate-400 hover:text-white text-lg font-bold p-1 transition'>
                    <i class='fa-solid fa-xmark'></i>
                </button>
            </div>

            <!-- モーダルボディ -->
            <div class='p-5 flex-1 overflow-y-auto custom-scroll space-y-4'>
                <div id='cleanup-summary-card' class='bg-amber-50 border border-amber-200 rounded-lg p-3 flex items-center justify-between'>
                    <div>
                        <p class='text-xs font-bold text-amber-900'>削減見込みデータサイズ</p>
                        <p id='cleanup-reclaim-text' class='text-lg font-black text-amber-700'>計算中...</p>
                    </div>
                    <span id='cleanup-count-badge' class='bg-amber-600 text-white text-xs font-bold px-2.5 py-1 rounded-full'>0件のファイル</span>
                </div>

                <div>
                    <h4 class='text-xs font-bold text-slate-700 mb-2 flex items-center gap-1'>
                        <i class='fa-solid fa-list-check text-indigo-500'></i> 削除対象候補ファイル一覧
                    </h4>
                    <div class='border border-slate-200 rounded-lg overflow-hidden max-h-60 overflow-y-auto custom-scroll'>
                        <table class='w-full text-left text-xs border-collapse'>
                            <thead class='bg-slate-100 text-slate-600 font-bold sticky top-0 border-b border-slate-200'>
                                <tr>
                                    <th class='p-2'>商品タイトル</th>
                                    <th class='p-2'>状態</th>
                                    <th class='p-2'>売却/更新日</th>
                                    <th class='p-2 text-right'>サイズ</th>
                                </tr>
                            </thead>
                            <tbody id='cleanup-candidates-table' class='divide-y divide-slate-100 text-slate-700'>
                                <!-- JSで動的生成 -->
                            </tbody>
                        </table>
                    </div>
                </div>

                <div class='bg-slate-50 border border-slate-200 rounded-lg p-2.5 text-[11px] text-slate-500 space-y-1'>
                    <p class='font-bold text-slate-700 flex items-center gap-1'>
                        <i class='fa-solid fa-shield-halved text-emerald-600'></i> 安全性のご案内
                    </p>
                    <p>・出品中・未出品の写真は削除対象に含まれません。</p>
                    <p>・PC上の写真ファイルのみをクリーンアップし、商品テキストデータはそのまま保持されます。</p>
                </div>
            </div>

            <!-- モーダルフッター -->
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button onclick='closeCleanupModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>
                    キャンセル
                </button>
                <button id='cleanup-execute-btn' onclick='executeCleanup()' class='px-4 py-2 text-xs font-bold bg-amber-600 hover:bg-amber-700 text-white rounded-lg shadow-sm transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5'>
                    <i class='fa-solid fa-trash-can'></i> クリーンアップを実行
                </button>
            </div>
        </div>
    </div>

    <!-- 写真拡大モーダル -->
    <div id='image-modal' class='fixed inset-0 bg-black/85 z-50 hidden flex flex-col items-center justify-center p-4' onclick='closeImageModal()'>
        <div class='relative max-w-5xl max-h-[92vh] flex flex-col items-center justify-center' onclick='event.stopPropagation()'>
            <button onclick='closeImageModal()' class='absolute -top-10 right-0 text-white hover:text-slate-300 text-2xl font-bold p-2 transition'>
                <i class='fa-solid fa-xmark'></i>
            </button>
            <div class='bg-slate-900 rounded-lg p-2 shadow-2xl flex items-center justify-center overflow-hidden max-h-[80vh]'>
                <img id='modal-img' src='' class='max-h-[76vh] max-w-full object-contain rounded'>
            </div>
            <div id='modal-thumbs' class='flex gap-2 mt-3 overflow-x-auto max-w-full p-1 custom-scroll'></div>
        </div>
    </div>

    <script>
        let allItems = [];
        let currentFilter = 'ALL';
        let currentModalPhotos = [];

        document.addEventListener('DOMContentLoaded', () => {
            fetchItems();
            setInterval(fetchItems, 3000);
        });

        async function fetchItems() {
            try {
                const res = await fetch('/api/items');
                if (!res.ok) return;
                allItems = await res.json();
                updateStats();
                renderItems();
            } catch (e) {}

            try {
                const statusRes = await fetch('/api/server/status');
                if (statusRes.ok) {
                    const statusData = await statusRes.json();
                    const banner = document.getElementById('status-banner');
                    const statusText = document.getElementById('status-text');
                    const statusIcon = document.getElementById('status-icon');
                    const missingCount = statusData.missingFileCount || 0;
                    const missingBadge = missingCount > 0 ? ' | 原本なし: ' + missingCount + '件' : '';
                    if (statusData.appConnected) {
                        banner.className = 'bg-emerald-600 text-white px-4 py-1.5 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                        statusIcon.className = 'fa-solid fa-circle-check text-base';
                        statusText.innerText = 'スマホアプリ接続中 (' + statusData.indexedPhotosCount + '枚の写真インデックス完了' + missingBadge + ')';
                    } else {
                        banner.className = 'bg-amber-500 text-white px-4 py-1.5 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                        statusIcon.className = 'fa-solid fa-triangle-exclamation text-base animate-bounce';
                        statusText.innerText = 'スマホアプリ未接続（操作・編集ロック中）。スマホアプリの「PC連携」画面から接続を開始してください。' + missingBadge;
                    }
                }
            } catch (e) {}

            fetchCleanupPreview();
        }

        function updateStats() {
            const listed = allItems.filter(i => i.status === 'LISTED');
            const sold = allItems.filter(i => i.status === 'SOLD');
            const totalSales = sold.reduce((sum, i) => sum + (i.soldPrice > 0 ? i.soldPrice : i.price), 0);
            const totalProfit = sold.reduce((sum, i) => sum + (i.profit || 0), 0);

            document.getElementById('stat-listed-count').innerText = listed.length + '品';
            document.getElementById('stat-sold-count').innerText = sold.length + '品';
            document.getElementById('stat-total-sales').innerText = '¥' + totalSales.toLocaleString();
            document.getElementById('stat-total-profit').innerText = '¥' + totalProfit.toLocaleString();

            document.getElementById('count-all').innerText = allItems.length;
            document.getElementById('count-listed').innerText = listed.length;
            document.getElementById('count-unshared').innerText = allItems.filter(i => !i.isShared).length;
            document.getElementById('count-sold').innerText = sold.length;
        }

        function setFilter(filter) {
            currentFilter = filter;
            document.querySelectorAll('.filter-btn').forEach(btn => {
                btn.className = 'filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition';
            });
            if (filter === 'ALL') document.getElementById('filter-all').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition';
            if (filter === 'LISTED') document.getElementById('filter-listed').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition';
            if (filter === 'UNSHARED') document.getElementById('filter-unshared').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-amber-700 shadow-sm transition';
            if (filter === 'SOLD') document.getElementById('filter-sold').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-emerald-700 shadow-sm transition';
            renderItems();
        }

        function renderItems() {
            const grid = document.getElementById('items-grid');
            const emptyState = document.getElementById('empty-state');
            const query = document.getElementById('search-input').value.trim().toLowerCase();

            let items = allItems.filter(item => {
                if (currentFilter === 'LISTED' && item.status !== 'LISTED') return false;
                if (currentFilter === 'UNSHARED' && item.isShared) return false;
                if (currentFilter === 'SOLD' && item.status !== 'SOLD') return false;

                if (query) {
                    const title = (item.title || '').toLowerCase();
                    const desc = (item.description || '').toLowerCase();
                    return title.includes(query) || desc.includes(query);
                }
                return true;
            });

            if (items.length === 0) {
                grid.innerHTML = '';
                emptyState.classList.remove('hidden');
                return;
            }
            emptyState.classList.add('hidden');

            grid.innerHTML = items.map((item, itemIdx) => {
                const photos = item.photos || [];
                const mainPhoto = photos.length > 0 ? (photos[0].md5Hash ? '/api/photos/' + photos[0].md5Hash : photos[0].photoUrl) : '';
                const displayPrice = item.status === 'SOLD' && item.soldPrice > 0 ? item.soldPrice : (item.price || 0);
                const isSold = item.status === 'SOLD';
                const profitText = item.profit != null ? ' (利益 ¥' + item.profit.toLocaleString() + ')' : '';

                return `<div class='bg-white rounded-lg border border-slate-200 overflow-hidden shadow-sm hover:shadow-md transition flex flex-col'>
                    <div class='relative aspect-square bg-slate-100 overflow-hidden cursor-pointer group' onclick='openImageModal(${itemIdx}, 0)'>
                        ${mainPhoto ? `<img src='${mainPhoto}' class='w-full h-full object-cover group-hover:scale-105 transition duration-200'>` : `<div class='w-full h-full flex items-center justify-center text-slate-300'><i class='fa-solid fa-image text-3xl'></i></div>`}
                        <div class='absolute top-1.5 left-1.5 flex gap-1 flex-wrap'>
                            <span class='px-2 py-0.5 rounded text-xs font-black shadow-sm ${isSold ? 'bg-rose-600 text-white' : 'bg-emerald-600 text-white'}'>
                                ${isSold ? '売却済' : (item.statusLabel || '出品中')}
                            </span>
                            ${!item.isShared ? `<span class='px-1.5 py-0.5 rounded text-[10px] font-bold bg-amber-500 text-white shadow-sm'>未送信</span>` : ''}
                        </div>
                        ${photos.length > 1 ? `<div class='absolute bottom-1.5 right-1.5 bg-black/60 text-white text-[10px] font-bold px-1.5 py-0.5 rounded flex items-center gap-1'><i class='fa-solid fa-camera'></i> ${photos.length}</div>` : ''}
                    </div>
                    <div class='p-2.5 flex-1 flex flex-col justify-between space-y-1.5'>
                        <div>
                            <h3 class='font-bold text-xs text-slate-800 line-clamp-1' title='${item.title || '無題'}'>${item.title || '無題'}</h3>
                            <p class='text-[11px] text-slate-500 line-clamp-2 mt-0.5 leading-tight'>${item.description || ''}</p>
                        </div>
                        <div class='border-t border-slate-100 pt-1.5 flex items-baseline justify-between'>
                            <div>
                                <span class='text-base font-black ${isSold ? 'text-rose-600' : 'text-indigo-600'}'>¥${displayPrice.toLocaleString()}</span>
                                ${profitText ? `<span class='text-[10px] font-bold text-emerald-600 block'>${profitText}</span>` : ''}
                            </div>
                        </div>
                    </div>
                </div>`;
            }).join('');
        }

        function openImageModal(itemIdx, photoIdx) {
            const query = document.getElementById('search-input').value.trim().toLowerCase();
            let filteredItems = allItems.filter(item => {
                if (currentFilter === 'LISTED' && item.status !== 'LISTED') return false;
                if (currentFilter === 'UNSHARED' && item.isShared) return false;
                if (currentFilter === 'SOLD' && item.status !== 'SOLD') return false;
                if (query) {
                    const title = (item.title || '').toLowerCase();
                    const desc = (item.description || '').toLowerCase();
                    return title.includes(query) || desc.includes(query);
                }
                return true;
            });

            const item = filteredItems[itemIdx];
            if (!item || !item.photos || item.photos.length === 0) return;

            currentModalPhotos = item.photos;
            selectModalPhoto(photoIdx);

            const thumbsContainer = document.getElementById('modal-thumbs');
            if (currentModalPhotos.length > 1) {
                thumbsContainer.innerHTML = currentModalPhotos.map((p, idx) => {
                    const url = p.md5Hash ? '/api/photos/' + p.md5Hash : p.photoUrl;
                    return `<img src='${url}' onclick='selectModalPhoto(${idx})' class='w-12 h-12 object-cover rounded cursor-pointer border-2 hover:border-indigo-400 transition ${idx === photoIdx ? 'border-indigo-500 ring-2 ring-indigo-400' : 'border-transparent opacity-70'}'>`;
                }).join('');
            } else {
                thumbsContainer.innerHTML = '';
            }

            document.getElementById('image-modal').classList.remove('hidden');
        }

        function selectModalPhoto(idx) {
            if (!currentModalPhotos || !currentModalPhotos[idx]) return;
            const p = currentModalPhotos[idx];
            const url = p.md5Hash ? '/api/photos/' + p.md5Hash : p.photoUrl;
            document.getElementById('modal-img').src = url;

            const thumbs = document.getElementById('modal-thumbs').querySelectorAll('img');
            thumbs.forEach((t, i) => {
                if (i === idx) {
                    t.className = 'w-12 h-12 object-cover rounded cursor-pointer border-2 border-indigo-500 ring-2 ring-indigo-400 transition';
                } else {
                    t.className = 'w-12 h-12 object-cover rounded cursor-pointer border-2 border-transparent opacity-70 transition';
                }
            });
        }

        function closeImageModal() {
            document.getElementById('image-modal').classList.add('hidden');
        }

        async function fetchCleanupPreview() {
            try {
                const res = await fetch('/api/cleanup/preview');
                if (!res.ok) return null;
                const data = await res.json();
                if (data.status === 'ok') {
                    const storageElem = document.getElementById('stat-storage-size');
                    if (storageElem) storageElem.innerText = data.totalStorageFormatted || '0 B';
                    return data;
                }
            } catch (e) {}
            return null;
        }

        async function openCleanupModal() {
            const data = await fetchCleanupPreview();
            const modal = document.getElementById('cleanup-modal');
            const table = document.getElementById('cleanup-candidates-table');
            const reclaimText = document.getElementById('cleanup-reclaim-text');
            const countBadge = document.getElementById('cleanup-count-badge');
            const executeBtn = document.getElementById('cleanup-execute-btn');

            if (data && data.status === 'ok') {
                reclaimText.innerText = data.reclaimableFormatted + ' (' + data.candidateCount + 'ファイル)';
                countBadge.innerText = data.candidateCount + '件のファイル';

                if (data.candidates && data.candidates.length > 0) {
                    table.innerHTML = data.candidates.map(c => `
                        <tr class='hover:bg-amber-50/50 transition'>
                            <td class='p-2 font-bold line-clamp-1 max-w-[200px]' title='${c.title}'>${c.title}</td>
                            <td class='p-2'><span class='bg-rose-100 text-rose-700 px-1.5 py-0.5 rounded text-[10px] font-bold'>${c.status || '売却済'}</span></td>
                            <td class='p-2 text-slate-500 font-mono text-[11px]'>${c.soldAtText}</td>
                            <td class='p-2 text-right font-mono font-bold text-amber-800'>${c.sizeFormatted}</td>
                        </tr>
                    `).join('');
                    executeBtn.disabled = false;
                } else {
                    table.innerHTML = `<tr><td colspan='4' class='p-4 text-center text-slate-400'>売却から30日以上経過した削除対象の写真はありません。</td></tr>`;
                    executeBtn.disabled = true;
                }
            } else {
                reclaimText.innerText = '取得失敗';
                table.innerHTML = `<tr><td colspan='4' class='p-4 text-center text-rose-500'>候補データの取得に失敗しました。</td></tr>`;
                executeBtn.disabled = true;
            }

            modal.classList.remove('hidden');
        }

        function closeCleanupModal() {
            document.getElementById('cleanup-modal').classList.add('hidden');
        }

        async function executeCleanup() {
            if (!confirm('売却から30日以上経過した写真ファイルをPCから削除します。実行してもよろしいですか？')) return;

            const executeBtn = document.getElementById('cleanup-execute-btn');
            executeBtn.disabled = true;
            executeBtn.innerHTML = '<i class=\'fa-solid fa-spinner animate-spin\'></i> 削除実行中...';

            try {
                const res = await fetch('/api/cleanup/execute', { method: 'POST' });
                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        alert('クリーンアップが完了しました。\n削除ファイル数: ' + result.deletedCount + '件\n削減データ容量: ' + result.freedFormatted);
                        closeCleanupModal();
                        fetchItems();
                    } else {
                        alert('エラーが発生しました: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラーが発生しました (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラーが発生しました: ' + e.message);
            } finally {
                executeBtn.disabled = false;
                executeBtn.innerHTML = '<i class=\'fa-solid fa-trash-can\'></i> クリーンアップを実行';
            }
        }
    </script>
</body>
</html>";
    }
}
