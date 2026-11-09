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
<body class='bg-slate-50 text-slate-800 min-h-screen'>
    <!-- スマホ接続ステータス通知バナー -->
    <div id='status-banner' class='bg-amber-500 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40'>
        <div class='flex items-center gap-2'>
            <i id='status-icon' class='fa-solid fa-triangle-exclamation text-base animate-bounce'></i>
            <span id='status-text'>スマホアプリ未接続（操作・編集ロック中）。スマホアプリの「PC連携」画面から接続を開始してください。</span>
        </div>
        <span class='bg-amber-700/60 px-2 py-0.5 rounded text-[11px] font-mono'>v2.1 (Build 13)</span>
    </div>

    <!-- ナビゲーションバー (スリム＆インライン統合) -->
    <header class='bg-indigo-600 text-white shadow-md sticky top-8 z-30'>
        <div class='max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-14 flex items-center justify-between gap-4'>
            <!-- 左側: タイトル & 接続ステータス -->
            <div class='flex items-center space-x-2.5 shrink-0'>
                <i class='fa-solid fa-boxes-stacked text-xl text-indigo-200'></i>
                <h1 class='font-bold text-base tracking-tight whitespace-nowrap'>フリマ出品・商品PC管理</h1>
                <span class='bg-indigo-700/80 text-indigo-100 text-[11px] px-2 py-0.5 rounded font-mono font-semibold border border-indigo-500'>母艦サーバー</span>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス -->
            <div class='flex items-center gap-2.5 flex-1 justify-end overflow-x-auto custom-scroll py-1'>
                <div class='flex items-center gap-1 bg-indigo-700/60 p-1 rounded-lg shrink-0'>
                    <button onclick='setFilter(""ALL"")' id='filter-all' class='filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition'>すべて (<span id='count-all'>0</span>)</button>
                    <button onclick='setFilter(""LISTED"")' id='filter-listed' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>出品中 (<span id='count-listed'>0</span>)</button>
                    <button onclick='setFilter(""UNSHARED"")' id='filter-unshared' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>未送信 (<span id='count-unshared'>0</span>)</button>
                    <button onclick='setFilter(""SOLD"")' id='filter-sold' class='filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition'>売却済 (<span id='count-sold'>0</span>)</button>
                </div>

                <div class='relative w-44 lg:w-60 shrink-0'>
                    <i class='fa-solid fa-magnifying-glass absolute left-2.5 top-2 text-indigo-300 text-xs'></i>
                    <input type='text' id='search-input' oninput='renderItems()' placeholder='商品名・説明文で検索...' class='w-full pl-8 pr-2 py-1 text-xs bg-indigo-700/80 text-white placeholder-indigo-300 border border-indigo-400/50 rounded-lg focus:outline-none focus:bg-indigo-800 focus:ring-1 focus:ring-white'>
                </div>
            </div>
        </div>
    </header>

    <!-- メインコンテンツ -->
    <main class='max-w-[1600px] mx-auto px-4 sm:px-6 lg:px-8 py-6'>
        <div class='flex flex-col lg:flex-row gap-6 items-start'>
            <!-- 左側: メイン商品グリッド表示エリア -->
            <div class='flex-1 w-full min-w-0'>
                <div id='items-grid' class='grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4'>
                    <!-- JavaScriptで動的生成 -->
                </div>

                <div id='empty-state' class='hidden text-center py-16 bg-white rounded-xl border border-slate-200'>
                    <i class='fa-solid fa-box-open text-4xl text-slate-300 mb-3'></i>
                    <p class='text-slate-500 font-medium'>該当する商品が見つかりません</p>
                </div>
            </div>

            <!-- 右側: 固定コントロールパネル -->
            <aside class='w-full lg:w-72 xl:w-80 shrink-0 sticky top-28 space-y-4'>
                <div class='bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3'>
                    <h2 class='text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5'>
                        <i class='fa-solid fa-chart-pie text-indigo-500'></i> 実績分析サマリー
                    </h2>
                    <div class='grid grid-cols-2 gap-2.5'>
                        <div class='bg-blue-50/70 p-2.5 rounded-lg border border-blue-100'>
                            <p class='text-[11px] font-medium text-slate-500'>出品中の商品</p>
                            <p id='stat-listed-count' class='text-lg font-bold text-blue-900'>0品</p>
                        </div>
                        <div class='bg-emerald-50/70 p-2.5 rounded-lg border border-emerald-100'>
                            <p class='text-[11px] font-medium text-slate-500'>売却済みの商品</p>
                            <p id='stat-sold-count' class='text-lg font-bold text-emerald-900'>0品</p>
                        </div>
                    </div>
                    <div class='space-y-2 pt-1'>
                        <div class='bg-slate-50 p-3 rounded-lg border border-slate-100 flex items-center justify-between'>
                            <span class='text-xs text-slate-600 font-medium flex items-center gap-1.5'>
                                <i class='fa-solid fa-coins text-indigo-500'></i> 累計売上高
                            </span>
                            <span id='stat-total-sales' class='text-base font-bold text-indigo-600'>¥0</span>
                        </div>
                        <div class='bg-amber-50/60 p-3 rounded-lg border border-amber-100 flex items-center justify-between'>
                            <span class='text-xs text-slate-600 font-medium flex items-center gap-1.5'>
                                <i class='fa-solid fa-hand-holding-dollar text-amber-500'></i> 累計純利益
                            </span>
                            <span id='stat-total-profit' class='text-base font-bold text-amber-600'>¥0</span>
                        </div>
                    </div>
                </div>

                <div class='bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3'>
                    <h2 class='text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5'>
                        <i class='fa-solid fa-sliders text-indigo-500'></i> コントロールパネル
                    </h2>
                    <div class='space-y-2'>
                        <button onclick='fetchItems()' class='w-full bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-3 rounded-xl text-xs flex items-center justify-center gap-2 transition'>
                            <i class='fa-solid fa-rotate-right text-sm'></i> データ更新
                        </button>
                    </div>
                </div>
            </aside>
        </div>
    </main>

    <script>
        let allItems = [];
        let currentFilter = 'ALL';

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
                        banner.className = 'bg-emerald-600 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                        statusIcon.className = 'fa-solid fa-circle-check text-base';
                        statusText.innerText = 'スマホアプリ接続中 (' + statusData.indexedPhotosCount + '枚の写真インデックス完了' + missingBadge + ')';
                    } else {
                        banner.className = 'bg-amber-500 text-white px-4 py-2 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40';
                        statusIcon.className = 'fa-solid fa-triangle-exclamation text-base animate-bounce';
                        statusText.innerText = 'スマホアプリ未接続（操作・編集ロック中）。スマホアプリの「PC連携」画面から接続を開始してください。' + missingBadge;
                    }
                }
            } catch (e) {}
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
                btn.className = 'filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition';
            });
            if (filter === 'ALL') document.getElementById('filter-all').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition';
            if (filter === 'LISTED') document.getElementById('filter-listed').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition';
            if (filter === 'UNSHARED') document.getElementById('filter-unshared').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-amber-700 shadow-sm transition';
            if (filter === 'SOLD') document.getElementById('filter-sold').className = 'filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-emerald-700 shadow-sm transition';
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

            grid.innerHTML = items.map(item => {
                const photos = item.photos || [];
                const firstPhoto = photos.length > 0 ? photos[0].photoUrl : '';

                return `<div class='bg-white rounded-xl border border-slate-200 overflow-hidden shadow-sm hover:shadow-md transition flex flex-col'>
                    <div class='relative aspect-square bg-slate-100 overflow-hidden'>
                        ${firstPhoto ? `<img src='${firstPhoto}' class='w-full h-full object-cover'>` : `<div class='w-full h-full flex items-center justify-center text-slate-300'><i class='fa-solid fa-image text-4xl'></i></div>`}
                        <div class='absolute top-2 left-2'>
                            <span class='px-2 py-0.5 rounded text-[11px] font-bold ${item.status === 'SOLD' ? 'bg-rose-500 text-white' : 'bg-emerald-500 text-white'}'>
                                ${item.statusLabel || item.status}
                            </span>
                        </div>
                    </div>
                    <div class='p-3 flex-1 flex flex-col justify-between space-y-2'>
                        <div>
                            <h3 class='font-bold text-sm text-slate-800 line-clamp-1'>${item.title || '無題'}</h3>
                            <p class='text-xs text-slate-500 line-clamp-2 mt-1'>${item.description || ''}</p>
                        </div>
                        <div class='flex items-center justify-between border-t border-slate-100 pt-2'>
                            <span class='text-sm font-bold text-indigo-600'>¥${(item.price || 0).toLocaleString()}</span>
                            <span class='text-xs text-slate-400'>写真: ${photos.length}枚</span>
                        </div>
                    </div>
                </div>`;
            }).join('');
        }
    </script>
</body>
</html>";
    }
}
