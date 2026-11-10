package com.example.myapplication.data.server

object WebDashboardHtml {
    fun getHtml(): String {
        return """
<!DOCTYPE html>
<html lang="ja">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Remix フリマ出品・商品PC管理</title>
    <script src="https://cdn.tailwindcss.com"></script>
    <link href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css" rel="stylesheet">
    <style>
        .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }
        .custom-scroll::-webkit-scrollbar-track { background: #f1f5f9; }
        .custom-scroll::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }
    </style>
</head>
<body class="bg-slate-50 text-slate-800 min-h-screen">
    <!-- ナビゲーションバー (スリム＆インライン統合) -->
    <header class="bg-indigo-600 text-white shadow-md sticky top-0 z-30">
        <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-14 flex items-center justify-between gap-4">
            <!-- 左側: タイトル & 接続ステータス (バージョン表示) -->
            <div class="flex items-center space-x-2.5 shrink-0">
                <i class="fa-solid fa-boxes-stacked text-xl text-indigo-200"></i>
                <h1 class="font-bold text-base tracking-tight whitespace-nowrap">フリマ出品・商品PC管理</h1>
                <span class="bg-indigo-700/80 text-indigo-100 text-[11px] px-2 py-0.5 rounded font-mono font-semibold border border-indigo-500">v2.3 (Build 18)</span>
                <span class="bg-indigo-500 text-[11px] px-2 py-0.5 rounded-full font-medium text-indigo-100 flex items-center gap-1 hidden md:inline-flex">
                    <span class="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span> スマホ接続中
                </span>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス (インライン統合) -->
            <div class="flex items-center gap-2.5 flex-1 justify-end overflow-x-auto custom-scroll py-1">
                <div class="flex items-center gap-1 bg-indigo-700/60 p-1 rounded-lg shrink-0">
                    <button onclick="setFilter('ALL')" id="filter-all" class="filter-btn px-2.5 py-1 text-xs font-bold rounded-md bg-white text-indigo-700 shadow-sm transition">すべて (<span id="count-all">0</span>)</button>
                    <button onclick="setFilter('LISTED')" id="filter-listed" class="filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition">出品中 (<span id="count-listed">0</span>)</button>
                    <button onclick="setFilter('UNSHARED')" id="filter-unshared" class="filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition">未送信 (<span id="count-unshared">0</span>)</button>
                    <button onclick="setFilter('SOLD')" id="filter-sold" class="filter-btn px-2.5 py-1 text-xs font-medium rounded-md hover:bg-indigo-500/50 text-indigo-100 transition">売却済 (<span id="count-sold">0</span>)</button>
                </div>

                <div class="relative w-44 lg:w-60 shrink-0">
                    <i class="fa-solid fa-magnifying-glass absolute left-2.5 top-2 text-indigo-300 text-xs"></i>
                    <input type="text" id="search-input" oninput="renderItems()" placeholder="商品名・説明文で検索..." class="w-full pl-8 pr-2 py-1 text-xs bg-indigo-700/80 text-white placeholder-indigo-300 border border-indigo-400/50 rounded-lg focus:outline-none focus:bg-indigo-800 focus:ring-1 focus:ring-white">
                </div>

                <!-- ヘッダー右側アクションボタン -->
                <div class="flex items-center gap-1.5 shrink-0">
                    <input type="file" id="folder-sync-input" webkitdirectory class="hidden" onchange="handleFolderSync(event)">
                    <button onclick="openCreateModal()" class="bg-emerald-500 hover:bg-emerald-600 text-white px-3 py-1 rounded-lg text-xs font-bold shadow flex items-center gap-1 transition">
                        <i class="fa-solid fa-plus"></i> 出品
                    </button>
                </div>
            </div>
        </div>
    </header>

    <!-- メインコンテンツ (左側: 商品グリッド, 右側: 固定サイドバー) -->
    <main class="max-w-[1600px] mx-auto px-4 sm:px-6 lg:px-8 py-6">
        <div class="flex flex-col lg:flex-row gap-6 items-start">

            <!-- 左側: メイン商品グリッド表示エリア -->
            <div class="flex-1 w-full min-w-0">
                <!-- まとめ買い作成モード案内バナー -->
                <div id="merge-mode-banner" class="hidden mb-4 bg-purple-50 border border-purple-200 text-purple-900 p-4 rounded-xl flex items-center justify-between shadow-sm">
                    <div class="flex items-center gap-3">
                        <div class="w-8 h-8 rounded-full bg-purple-600 text-white flex items-center justify-center font-bold shrink-0">
                            <i class="fa-solid fa-object-group"></i>
                        </div>
                        <div>
                            <h4 class="font-bold text-sm">まとめ買い作成モード中</h4>
                            <p class="text-xs text-purple-700">まとめたい商品カードをクリックして選択してください (選択中: <span id="merge-mode-count" class="font-bold">0</span>件)。2件以上選択後に右下の「まとめ買いを作成する」ボタンで実行できます。</p>
                        </div>
                    </div>
                </div>

                <!-- 商品一覧グリッド -->
                <div id="items-grid" class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
                    <!-- JavaScriptで動的生成 -->
                </div>

                <div id="empty-state" class="hidden text-center py-16 bg-white rounded-xl border border-slate-200">
                    <i class="fa-solid fa-box-open text-4xl text-slate-300 mb-3"></i>
                    <p class="text-slate-500 font-medium">該当する商品が見つかりません</p>
                </div>
            </div>

            <!-- 右側: 固定コントロールパネル (サイドバー) -->
            <aside class="w-full lg:w-72 xl:w-80 shrink-0 sticky top-20 space-y-4">

                <!-- 統計サマリー (縦並びコンパクトカード) -->
                <div class="bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3">
                    <h2 class="text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
                        <i class="fa-solid fa-chart-pie text-indigo-500"></i> 実績分析サマリー
                    </h2>

                    <div class="grid grid-cols-2 gap-2.5">
                        <div class="bg-blue-50/70 p-2.5 rounded-lg border border-blue-100">
                            <p class="text-[11px] font-medium text-slate-500">出品中の商品</p>
                            <p id="stat-listed-count" class="text-lg font-bold text-blue-900">-</p>
                        </div>
                        <div class="bg-emerald-50/70 p-2.5 rounded-lg border border-emerald-100">
                            <p class="text-[11px] font-medium text-slate-500">売却済みの商品</p>
                            <p id="stat-sold-count" class="text-lg font-bold text-emerald-900">-</p>
                        </div>
                    </div>

                    <div class="space-y-2 pt-1">
                        <div class="bg-slate-50 p-3 rounded-lg border border-slate-100 flex items-center justify-between">
                            <span class="text-xs text-slate-600 font-medium flex items-center gap-1.5">
                                <i class="fa-solid fa-coins text-indigo-500"></i> 累計売上高
                            </span>
                            <span id="stat-total-sales" class="text-base font-bold text-indigo-600">¥0</span>
                        </div>
                        <div class="bg-amber-50/60 p-3 rounded-lg border border-amber-100 flex items-center justify-between">
                            <span class="text-xs text-slate-600 font-medium flex items-center gap-1.5">
                                <i class="fa-solid fa-hand-holding-dollar text-amber-500"></i> 累計純利益
                            </span>
                            <span id="stat-total-profit" class="text-base font-bold text-amber-600">¥0</span>
                        </div>
                    </div>
                </div>

                <!-- アクションコントロールパネル (縦一列ボタン群) -->
                <div class="bg-white rounded-xl p-4 shadow-sm border border-slate-200 space-y-3">
                    <h2 class="text-xs font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
                        <i class="fa-solid fa-sliders text-indigo-500"></i> コントロールパネル
                    </h2>

                    <div class="space-y-2">
                        <!-- 新規商品出品 -->
                        <button onclick="openCreateModal()" class="w-full bg-emerald-500 hover:bg-emerald-600 text-white font-bold py-2.5 px-4 rounded-xl text-sm shadow flex items-center justify-center gap-2 transition">
                            <i class="fa-solid fa-plus text-base"></i> 新規商品を出品
                        </button>

                        <!-- まとめ買い作成 (1ボタン操作完結) -->
                        <button id="merge-sidebar-btn" onclick="handleMergeButtonClick()" class="w-full bg-purple-600 hover:bg-purple-700 text-white font-bold py-2.5 px-4 rounded-xl text-sm shadow flex items-center justify-center gap-2 transition cursor-pointer">
                            <i class="fa-solid fa-object-group text-base"></i> まとめ買い作成
                        </button>

                        <!-- PCフォルダ自動同期 -->
                        <button onclick="document.getElementById('folder-sync-input').click()" class="w-full bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-3 rounded-xl text-xs flex items-center justify-center gap-2 transition" title="PCローカルフォルダの写真と自動差分同期">
                            <i class="fa-solid fa-folder-sync text-sm"></i> PCフォルダ自動同期
                        </button>

                        <!-- 手動更新 -->
                        <button onclick="fetchItems()" class="w-full bg-slate-100 hover:bg-slate-200 text-slate-700 font-medium py-2 px-3 rounded-xl text-xs flex items-center justify-center gap-2 transition">
                            <i class="fa-solid fa-rotate-right text-sm"></i> データ手動更新
                        </button>

                        <!-- スマホ全件同期 -->
                        <button onclick="fetchItems(); showToast('全件データを同期しました')" class="w-full bg-slate-100 hover:bg-slate-200 text-slate-700 font-medium py-2 px-3 rounded-xl text-xs flex items-center justify-center gap-2 transition">
                            <i class="fa-solid fa-bolt text-amber-500 text-sm"></i> スマホ全件同期・照合
                        </button>
                    </div>
                </div>

            </aside>
        </div>
    </main>

    <!-- 新規作成・編集モーダル -->
    <div id="item-modal" class="hidden fixed inset-0 bg-black/60 z-50 flex items-center justify-center p-4">
        <div class="bg-white rounded-2xl shadow-xl w-full max-w-2xl max-h-[90vh] flex flex-col overflow-hidden">
            <div class="px-6 py-4 border-b border-slate-200 flex items-center justify-between bg-slate-50">
                <h3 id="modal-title" class="font-bold text-lg text-slate-800">新規商品出品</h3>
                <button onclick="closeModal()" class="text-slate-400 hover:text-slate-600 text-lg">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>
            <form id="item-form" onsubmit="handleFormSubmit(event)" class="overflow-y-auto p-6 space-y-4 custom-scroll">
                <input type="hidden" id="form-item-id">
                
                <div>
                    <label class="block text-xs font-bold text-slate-700 mb-1">商品名 <span class="text-rose-500">*</span></label>
                    <input type="text" id="form-title" required placeholder="例: UNIQLO ウルトラライトダウン Mサイズ 黒" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500 focus:outline-none">
                </div>

                <div>
                    <label class="block text-xs font-bold text-slate-700 mb-1">商品説明文</label>
                    <textarea id="form-description" rows="4" placeholder="商品の状態、サイズ、購入時期、注意事項などを記入（メルカリ出品時にワンクリックコピーできます）" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500 focus:outline-none custom-scroll"></textarea>
                </div>

                <div class="grid grid-cols-2 gap-4">
                    <div>
                        <label class="block text-xs font-bold text-slate-700 mb-1">出品価格 (円) <span class="text-rose-500">*</span></label>
                        <div class="relative">
                            <span class="absolute left-3 top-2 text-slate-400 text-sm">¥</span>
                            <input type="number" id="form-price" required min="0" placeholder="2500" class="w-full pl-7 pr-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500 focus:outline-none">
                        </div>
                    </div>
                    <div>
                        <label class="block text-xs font-bold text-slate-700 mb-1">ステータス</label>
                        <select id="form-status" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500 focus:outline-none bg-white">
                            <option value="LISTED">出品中</option>
                            <option value="SOLD">売却済み</option>
                            <option value="DRAFT">未出品・下書き</option>
                        </select>
                    </div>
                </div>

                <div class="grid grid-cols-3 gap-3 bg-slate-50 p-3 rounded-xl border border-slate-200">
                    <div>
                        <label class="block text-[11px] font-medium text-slate-600 mb-1">送料 (円)</label>
                        <input type="number" id="form-shipping" placeholder="210" class="w-full px-2 py-1.5 border border-slate-300 rounded text-xs bg-white">
                    </div>
                    <div>
                        <label class="block text-[11px] font-medium text-slate-600 mb-1">手数料率 (%)</label>
                        <input type="number" id="form-fee-rate" placeholder="5" class="w-full px-2 py-1.5 border border-slate-300 rounded text-xs bg-white">
                    </div>
                    <div>
                        <label class="block text-[11px] font-medium text-slate-600 mb-1">梱包資材費 (円)</label>
                        <input type="number" id="form-expense" placeholder="55" class="w-full px-2 py-1.5 border border-slate-300 rounded text-xs bg-white">
                    </div>
                </div>

                <div id="photos-input-container">
                    <label class="block text-xs font-bold text-slate-700 mb-1">商品写真アップロード (複数可)</label>
                    <div class="border-2 border-dashed border-slate-300 rounded-xl p-4 text-center hover:bg-slate-50 transition cursor-pointer relative" onclick="document.getElementById('form-photos').click()">
                        <i class="fa-solid fa-cloud-arrow-up text-3xl text-indigo-400 mb-2"></i>
                        <p class="text-xs text-slate-600 font-medium">クリックしてPCから写真を選択、またはドラッグ＆ドロップ</p>
                        <p class="text-[10px] text-slate-400 mt-1">JPEG, PNG形式対応 (スマホ端末のギャラリーにも自動保存されます)</p>
                        <input type="file" id="form-photos" multiple accept="image/*" class="hidden" onchange="previewUploadPhotos(event)">
                    </div>
                    <div id="photo-preview-grid" class="flex flex-wrap gap-2 mt-3"></div>
                </div>

                <div class="pt-4 border-t border-slate-200 flex justify-end gap-3">
                    <button type="button" onclick="closeModal()" class="px-4 py-2 border border-slate-300 rounded-lg text-sm text-slate-700 hover:bg-slate-100 transition">キャンセル</button>
                    <button type="submit" id="submit-btn" class="px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-semibold shadow transition">保存する</button>
                </div>
            </form>
        </div>
    </div>

    <!-- まとめ買い作成モーダル -->
    <div id="merge-modal" class="hidden fixed inset-0 bg-black/60 z-50 flex items-center justify-center p-4">
        <div class="bg-white rounded-2xl shadow-xl w-full max-w-lg flex flex-col overflow-hidden">
            <div class="px-6 py-4 border-b border-slate-200 flex items-center justify-between bg-purple-50">
                <h3 class="font-bold text-lg text-purple-900 flex items-center gap-2">
                    <i class="fa-solid fa-object-group text-purple-600"></i> まとめ買い出品の作成
                </h3>
                <button onclick="closeMergeModal()" class="text-slate-400 hover:text-slate-600 text-lg">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>
            <form onsubmit="handleMergeSubmit(event)" class="p-6 space-y-4">
                <div>
                    <label class="block text-xs font-bold text-slate-700 mb-1">まとめ商品タイトル <span class="text-rose-500">*</span></label>
                    <input type="text" id="merge-title" required class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-purple-500">
                </div>
                <div>
                    <label class="block text-xs font-bold text-slate-700 mb-1">商品説明文</label>
                    <textarea id="merge-description" rows="3" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-purple-500 custom-scroll"></textarea>
                </div>
                <div class="grid grid-cols-2 gap-4">
                    <div>
                        <label class="block text-xs font-bold text-slate-700 mb-1">まとめ販売価格 (円) <span class="text-rose-500">*</span></label>
                        <input type="number" id="merge-price" required min="0" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-purple-500">
                    </div>
                    <div>
                        <label class="block text-xs font-bold text-slate-700 mb-1">送料 (まとめ後) (円)</label>
                        <input type="number" id="merge-shipping" value="210" class="w-full px-3 py-2 border border-slate-300 rounded-lg text-sm focus:ring-2 focus:ring-purple-500">
                    </div>
                </div>

                <div class="grid grid-cols-2 gap-4 bg-purple-50/50 p-3 rounded-xl border border-purple-100">
                    <div>
                        <label class="block text-[11px] font-bold text-slate-700 mb-1">販売手数料率 (%)</label>
                        <input type="number" id="merge-fee-rate" value="5" placeholder="5" class="w-full px-3 py-1.5 border border-slate-300 rounded-lg text-xs bg-white focus:ring-2 focus:ring-purple-500">
                    </div>
                    <div>
                        <label class="block text-[11px] font-bold text-slate-700 mb-1">梱包資材費・経費 (円)</label>
                        <input type="number" id="merge-expense" value="55" placeholder="55" class="w-full px-3 py-1.5 border border-slate-300 rounded-lg text-xs bg-white focus:ring-2 focus:ring-purple-500">
                    </div>
                </div>
                <div class="pt-4 border-t border-slate-200 flex justify-end gap-3">
                    <button type="button" onclick="closeMergeModal()" class="px-4 py-2 border border-slate-300 rounded-lg text-sm text-slate-700 hover:bg-slate-100">キャンセル</button>
                    <button type="submit" id="merge-submit-btn" class="px-5 py-2 bg-purple-600 hover:bg-purple-700 text-white rounded-lg text-sm font-semibold shadow">作成する</button>
                </div>
            </form>
        </div>
    </div>

    <!-- アプリ風クイック操作モーダル -->
    <div id="quick-action-modal" class="hidden fixed inset-0 bg-black/60 z-50 flex items-center justify-center p-4" onclick="closeQuickActionModal()">
        <div class="bg-white rounded-2xl shadow-2xl w-full max-w-md overflow-hidden animate-in fade-in zoom-in duration-150" onclick="event.stopPropagation()">
            <!-- ヘッダー (商品サムネイル・タイトル・価格) -->
            <div id="quick-action-header" class="p-4 bg-slate-50 border-b border-slate-200 flex items-center gap-3 relative">
                <img id="quick-action-thumb" src="" class="w-16 h-16 object-cover rounded-xl border border-slate-200 shadow-sm shrink-0">
                <div class="flex-1 min-w-0 pr-6">
                    <div id="quick-action-badge" class="mb-1"></div>
                    <h4 id="quick-action-title" class="font-bold text-slate-900 text-sm truncate"></h4>
                    <p id="quick-action-price" class="text-xs font-bold text-indigo-600 mt-0.5"></p>
                </div>
                <button onclick="closeQuickActionModal()" class="absolute top-3 right-3 text-slate-400 hover:text-slate-600 text-lg">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>

            <!-- 操作メニューリスト -->
            <div id="quick-action-body" class="p-3 space-y-1.5 max-h-[70vh] overflow-y-auto custom-scroll">
                <!-- JSで動的生成 -->
            </div>
        </div>
    </div>

    <!-- 画像拡大＆写真復元モーダル -->
    <div id="image-viewer-modal" class="hidden fixed inset-0 bg-black/80 z-50 flex items-center justify-center p-4" onclick="closeImageViewer()">
        <div class="max-w-4xl max-h-[90vh] relative flex flex-col items-center gap-3" onclick="event.stopPropagation()">
            <button onclick="closeImageViewer()" class="absolute -top-10 right-0 text-white hover:text-slate-300 text-2xl">
                <i class="fa-solid fa-xmark"></i>
            </button>
            <img id="image-viewer-src" src="" alt="商品写真" class="max-w-full max-h-[75vh] rounded-lg shadow-2xl object-contain">
            <div id="restore-banner" class="hidden bg-slate-900/90 text-white px-4 py-2 rounded-xl text-xs flex items-center gap-3 border border-slate-700">
                <span class="text-amber-400 font-bold"><i class="fa-solid fa-triangle-exclamation"></i> スマホ側の元高画質写真が削除されています (サムネイルを表示中)</span>
                <button onclick="document.getElementById('restore-file-input').click()" class="bg-indigo-600 hover:bg-indigo-500 text-white px-3 py-1 rounded font-bold transition">
                    <i class="fa-solid fa-upload"></i> PCから高画質元画像を復元
                </button>
                <input type="file" id="restore-file-input" accept="image/*" class="hidden" onchange="uploadRestoredPhoto(event)">
            </div>
        </div>
    </div>

    <!-- トースト通知 -->
    <div id="toast" class="fixed bottom-6 right-6 bg-slate-900 text-white px-4 py-2.5 rounded-xl shadow-xl text-sm font-medium transition-all transform translate-y-16 opacity-0 z-50 flex items-center gap-2">
        <i class="fa-solid fa-circle-check text-emerald-400"></i>
        <span id="toast-message"></span>
    </div>

    <script>
        let allItems = [];
        let currentFilter = 'ALL';
        let selectedItemIds = new Set();
        let currentViewingPhoto = null;
        let isMergeMode = false;

        function handleMergeButtonClick() {
            if (!isMergeMode) {
                startMergeMode();
            } else {
                if (selectedItemIds.size >= 2) {
                    openMergeModal();
                } else {
                    cancelMergeMode();
                }
            }
        }

        function startMergeMode() {
            isMergeMode = true;
            selectedItemIds.clear();
            document.getElementById('merge-mode-banner').classList.remove('hidden');
            updateMergeSidebarBtn();
            renderItems();
            showToast('まとめ買いモードを開始しました。商品カードをクリックして選択してください。');
        }

        function cancelMergeMode() {
            isMergeMode = false;
            selectedItemIds.clear();
            document.getElementById('merge-mode-banner').classList.add('hidden');
            updateMergeSidebarBtn();
            renderItems();
            showToast('まとめ買いモードを終了しました');
        }

        function updateMergeSidebarBtn() {
            const sbBtn = document.getElementById('merge-sidebar-btn');
            if (!sbBtn) return;
            sbBtn.disabled = false; // 常に活性化

            const count = selectedItemIds.size;
            const countSpan = document.getElementById('merge-mode-count');
            if (countSpan) countSpan.innerText = count;

            if (!isMergeMode) {
                sbBtn.className = 'w-full bg-purple-600 hover:bg-purple-700 text-white font-bold py-2.5 px-4 rounded-xl text-sm shadow flex items-center justify-center gap-2 transition cursor-pointer';
                sbBtn.innerHTML = '<i class="fa-solid fa-object-group text-base"></i> まとめ買い作成';
            } else {
                if (count >= 2) {
                    sbBtn.className = 'w-full bg-purple-600 hover:bg-purple-500 text-white font-bold py-2.5 px-4 rounded-xl text-sm shadow-lg flex items-center justify-center gap-2 transition cursor-pointer animate-pulse ring-4 ring-purple-300';
                    sbBtn.innerHTML = '<i class="fa-solid fa-check text-base"></i> まとめ買いを作成する (' + count + '件)';
                } else {
                    const text = count === 1 ? 'モード解除 (1件選択中)' : 'モード解除 (未選択)';
                    sbBtn.className = 'w-full bg-purple-700 hover:bg-purple-800 text-white font-bold py-2.5 px-4 rounded-xl text-sm shadow flex items-center justify-center gap-2 transition cursor-pointer ring-2 ring-purple-300';
                    sbBtn.innerHTML = '<i class="fa-solid fa-xmark text-base"></i> ' + text;
                }
            }
        }

        let autoSyncTimer = null;

        document.addEventListener('DOMContentLoaded', () => {
            fetchItems();
            startAutoSync();
        });

        // 2.5秒周期スマートバックグラウンドポーリング (タブ非表示時は自動停止)
        function startAutoSync() {
            stopAutoSync();
            autoSyncTimer = setInterval(() => {
                fetchItemsSilent();
            }, 2500);
        }

        function stopAutoSync() {
            if (autoSyncTimer) {
                clearInterval(autoSyncTimer);
                autoSyncTimer = null;
            }
        }

        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                stopAutoSync();
            } else {
                fetchItemsSilent();
                startAutoSync();
            }
        });

        // サイレントフェッチ (トースト通知なし・エラー無視)
        async function fetchItemsSilent() {
            try {
                const res = await fetch('/api/items');
                if (!res.ok) return;
                const newItems = await res.json();
                // データに変更があった場合のみ再描画
                const newHash = JSON.stringify(newItems.map(i => i.id + ':' + i.status + ':' + i.isShared + ':' + i.price + ':' + (i.photos ? i.photos.length : 0)));
                const oldHash = JSON.stringify(allItems.map(i => i.id + ':' + i.status + ':' + i.isShared + ':' + i.price + ':' + (i.photos ? i.photos.length : 0)));
                if (newHash !== oldHash) {
                    allItems = newItems;
                    updateStats();
                    renderItems();
                }
            } catch (e) {
                // サイレント: エラーは無視
            }
        }

        async function fetchItems() {
            try {
                const res = await fetch('/api/items');
                if (!res.ok) throw new Error('通信エラー');
                allItems = await res.json();
                updateStats();
                renderItems();
            } catch (err) {
                showToast('データの取得に失敗しました: ' + err.message);
            }
        }

        function updateStats() {
            const listed = allItems.filter(i => i.status === 'LISTED');
            const unshared = allItems.filter(i => !i.isShared);
            const sold = allItems.filter(i => i.status === 'SOLD');
            const totalSales = sold.reduce((sum, i) => sum + (i.soldPrice > 0 ? i.soldPrice : i.price), 0);
            const totalProfit = sold.reduce((sum, i) => sum + i.profit, 0);

            document.getElementById('stat-listed-count').innerText = listed.length + '品';
            document.getElementById('stat-sold-count').innerText = sold.length + '品';
            document.getElementById('stat-total-sales').innerText = '¥' + totalSales.toLocaleString();
            document.getElementById('stat-total-profit').innerText = '¥' + totalProfit.toLocaleString();

            document.getElementById('count-all').innerText = allItems.length;
            document.getElementById('count-listed').innerText = listed.length;
            document.getElementById('count-unshared').innerText = unshared.length;
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

        function toggleSelect(id) {
            if (selectedItemIds.has(id)) {
                selectedItemIds.delete(id);
            } else {
                selectedItemIds.add(id);
            }
            if (isMergeMode) updateMergeSidebarBtn();
            renderItems();
        }

        function renderItems() {
            try {
                const grid = document.getElementById('items-grid');
                const emptyState = document.getElementById('empty-state');
                const search = document.getElementById('search-input').value.toLowerCase().trim();

                const filtered = allItems.filter(item => {
                    let matchFilter = true;
                    if (currentFilter === 'LISTED') matchFilter = (item.status === 'LISTED');
                    if (currentFilter === 'UNSHARED') matchFilter = (!item.isShared);
                    if (currentFilter === 'SOLD') matchFilter = (item.status === 'SOLD');

                    const matchSearch = !search || (item.title && item.title.toLowerCase().includes(search)) || (item.description && item.description.toLowerCase().includes(search));
                    return matchFilter && matchSearch;
                });

                if (filtered.length === 0) {
                    grid.innerHTML = '';
                    emptyState.classList.remove('hidden');
                    return;
                }

                emptyState.classList.add('hidden');
                grid.innerHTML = filtered.map(item => {
                    const isSold = item.status === 'SOLD';
                    const isMerged = item.status === 'MERGED';
                    const isSelected = selectedItemIds.has(item.id);

                    let statusBadge = '<span class="bg-blue-100 text-blue-800 text-[11px] px-2 py-0.5 rounded-full font-bold">出品中</span>';
                    if (isSold) statusBadge = '<span class="bg-emerald-100 text-emerald-800 text-[11px] px-2 py-0.5 rounded-full font-bold">売却済</span>';
                    if (isMerged) statusBadge = '<span class="bg-purple-100 text-purple-800 text-[11px] px-2 py-0.5 rounded-full font-bold">まとめ売却</span>';

                    const sharedBadge = item.isShared
                        ? '<span class="bg-indigo-100 text-indigo-700 text-[10px] px-1.5 py-0.5 rounded font-bold" title="PC送信済み"><i class="fa-solid fa-check"></i> PC済</span>'
                        : '<button onclick="event.stopPropagation(); markShared(' + item.id + ')" class="bg-amber-100 hover:bg-amber-200 text-amber-800 text-[10px] px-1.5 py-0.5 rounded font-bold transition">送信済にする</button>';

                    const mainPhotoObj = item.photos && item.photos.length > 0 ? item.photos[0] : null;
                    const mainPhoto = mainPhotoObj
                        ? '<div class="relative group/photo"><img src="' + (mainPhotoObj.thumbUrl || mainPhotoObj.url) + '" alt="" class="w-full h-36 object-cover rounded-t-xl cursor-pointer hover:opacity-95 transition" onclick="openQuickActionModal(' + item.id + ')"><button onclick="event.stopPropagation(); openImageViewer(' + mainPhotoObj.id + ', \'' + mainPhotoObj.url + '\', ' + mainPhotoObj.isOriginalDeleted + ')" class="absolute bottom-2 right-2 bg-black/60 hover:bg-black/80 text-white px-2 py-1 rounded-md text-[11px] opacity-0 group-hover/photo:opacity-100 transition flex items-center gap-1 shadow" title="写真を拡大表示"><i class="fa-solid fa-expand"></i> 拡大</button></div>'
                        : '<div class="w-full h-36 bg-slate-100 flex items-center justify-center text-slate-300 text-2xl rounded-t-xl cursor-pointer" onclick="openQuickActionModal(' + item.id + ')"><i class="fa-solid fa-image"></i></div>';

                    const photoThumbnails = item.photos && item.photos.length > 1
                        ? '<div class="flex gap-1 px-3 pt-1.5 overflow-x-auto custom-scroll">' + item.photos.map(p => '<img src="' + (p.thumbUrl || p.url) + '" class="w-8 h-8 object-cover rounded cursor-pointer border ' + (p.isOriginalDeleted ? 'border-amber-400' : 'border-slate-200') + ' hover:border-indigo-500" onclick="event.stopPropagation(); openImageViewer(' + p.id + ', \'' + p.url + '\', ' + p.isOriginalDeleted + ')">').join('') + '</div>'
                        : '';

                    const unmergeBtn = isMerged || ((item.title && item.title.includes('まとめ')) || (item.description && item.description.includes('まとめ')))
                        ? '<button onclick="event.stopPropagation(); unmergeItem(' + item.id + ')" class="w-full bg-purple-50 hover:bg-purple-100 text-purple-700 text-[11px] py-1 px-2 rounded-lg font-bold transition flex items-center justify-center gap-1"><i class="fa-solid fa-object-ungroup"></i> まとめを解除</button>'
                        : '';

                    const cardClickAction = isMergeMode ? 'toggleSelect(' + item.id + ')' : 'openQuickActionModal(' + item.id + ')';

                    return `
                    <div class="bg-white rounded-xl shadow-sm border ` + (isSelected ? 'border-purple-500 ring-2 ring-purple-200' : 'border-slate-200') + ` overflow-hidden flex flex-col hover:shadow-md transition relative group">
                        <div class="absolute top-2.5 left-2.5 z-10 flex items-center gap-1.5">
                            ` + statusBadge + `
                        </div>
                        <div class="absolute top-2.5 right-2.5 z-10 flex items-center gap-1.5">
                            ` + sharedBadge + `
                            <div class="bg-black/70 text-white text-[11px] px-2 py-0.5 rounded-md font-bold">
                                ¥` + (item.price ? item.price.toLocaleString() : '0') + `
                            </div>
                        </div>
                        <div class="relative">
                            ` + mainPhoto + `
                        </div>
                        ` + photoThumbnails + `
                        <div class="p-3 flex-1 flex flex-col justify-between cursor-pointer" onclick="` + cardClickAction + `">
                            <div>
                                <h3 class="font-bold text-slate-900 text-sm mb-0.5 line-clamp-1 group-hover:text-indigo-600 transition">` + escapeHtml(item.title) + `</h3>
                                <p class="text-[11px] text-slate-500 mb-2 line-clamp-2">` + escapeHtml(item.description || '説明なし') + `</p>
                                
                                <div class="grid grid-cols-2 gap-1.5 bg-slate-50 p-2 rounded-lg text-[11px] mb-2">
                                    <div>
                                        <span class="text-slate-400 text-[10px]">純利益:</span>
                                        <span class="font-bold text-amber-600 block text-xs">¥` + (item.profit ? item.profit.toLocaleString() : '0') + `</span>
                                    </div>
                                    <div class="text-right">
                                        <span class="text-slate-400 text-[10px]">送料/経費:</span>
                                        <span class="text-slate-700 block text-[11px]">¥` + (item.shippingCost || 0) + ` / ¥` + (item.expenseCost || 0) + `</span>
                                    </div>
                                </div>
                            </div>

                            <div class="pt-2 border-t border-slate-100 space-y-1.5">
                                ` + unmergeBtn + `
                                <div class="flex items-center gap-1.5">
                                    <button onclick="event.stopPropagation(); copyDescription('` + escapeJs(item.title) + `', '` + escapeJs(item.description || '') + `')" class="flex-1 bg-slate-100 hover:bg-slate-200 text-slate-700 text-[11px] py-1 px-2 rounded-lg font-medium flex items-center justify-center gap-1 transition">
                                        <i class="fa-solid fa-copy"></i> 説明文コピー
                                    </button>
                                    <button onclick="event.stopPropagation(); openQuickActionModal(` + item.id + `)" class="bg-indigo-50 hover:bg-indigo-100 text-indigo-700 text-[11px] py-1 px-2.5 rounded-lg font-bold transition flex items-center gap-1" title="クイック操作">
                                        <i class="fa-solid fa-ellipsis-vertical"></i> 操作
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>`;
                }).join('');
            } catch (err) {
                console.error('renderItems error:', err);
                showToast('描画エラー: ' + err.message);
            }
        }

        async function markShared(id) {
            try {
                const res = await fetch('/api/items/' + id + '/mark-shared', { method: 'POST' });
                if (!res.ok) throw new Error('送信済みフラグの更新に失敗');
                showToast('PC送信済みに更新しました');
                fetchItems();
            } catch (err) {
                showToast(err.message);
            }
        }

        async function toggleStatus(id, currentStatus) {
            const newStatus = currentStatus === 'LISTED' ? 'SOLD' : 'LISTED';
            try {
                const res = await fetch('/api/items/' + id + '/status', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ status: newStatus })
                });
                if (!res.ok) throw new Error('ステータス更新に失敗しました');
                showToast(newStatus === 'SOLD' ? '売却済みに変更しました' : '出品中に戻しました');
                fetchItems();
            } catch (err) {
                showToast(err.message);
            }
        }

        async function deleteItem(id, title) {
            if (!confirm('「' + title + '」を削除しますか？\n（写真も端末から削除されます）')) return;
            try {
                const res = await fetch('/api/items/' + id, { method: 'DELETE' });
                if (!res.ok) throw new Error('削除に失敗しました');
                showToast('商品を削除しました');
                fetchItems();
            } catch (err) {
                showToast(err.message);
            }
        }

        function openMergeModal() {
            const ids = Array.from(selectedItemIds);
            if (ids.length < 2) {
                showToast('まとめ買いには商品を2件以上選択してください');
                return;
            }
            const itemsToMerge = allItems.filter(i => ids.includes(i.id));
            const mergedTitle = itemsToMerge.map(i => i.title).join(' + ');
            const totalPrice = itemsToMerge.reduce((sum, i) => sum + i.price, 0);

            document.getElementById('merge-title').value = mergedTitle;
            document.getElementById('merge-description').value = '【まとめ買い内容】\n' + itemsToMerge.map(i => '・' + i.title + ' (¥' + i.price + ')').join('\n');
            document.getElementById('merge-price').value = totalPrice;
            document.getElementById('merge-shipping').value = '210';
            document.getElementById('merge-fee-rate').value = '5';
            document.getElementById('merge-expense').value = '55';
            document.getElementById('merge-modal').classList.remove('hidden');
        }

        function closeMergeModal() {
            document.getElementById('merge-modal').classList.add('hidden');
        }

        async function handleMergeSubmit(e) {
            e.preventDefault();
            const childItemIds = Array.from(selectedItemIds);
            const mergedTitle = document.getElementById('merge-title').value;
            const mergedDescription = document.getElementById('merge-description').value;
            const totalSoldPrice = parseInt(document.getElementById('merge-price').value) || 0;
            const newShippingCost = parseInt(document.getElementById('merge-shipping').value) || 210;
            const newFeeRatePercent = parseInt(document.getElementById('merge-fee-rate').value) || 5;
            const newExpenseCost = parseInt(document.getElementById('merge-expense').value) || 55;

            const btn = document.getElementById('merge-submit-btn');
            btn.disabled = true;

            try {
                const res = await fetch('/api/items/merge', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        childItemIds,
                        mergedTitle,
                        mergedDescription,
                        totalSoldPrice,
                        newShippingCost,
                        newFeeRatePercent,
                        newExpenseCost,
                        asStatus: 'LISTED'
                    })
                });
                if (!res.ok) throw new Error('まとめ買い作成に失敗しました');
                showToast('まとめ買い商品を作成しました！');
                selectedItemIds.clear();
                closeMergeModal();
                cancelMergeMode();
                fetchItems();
            } catch (err) {
                showToast(err.message);
            } finally {
                btn.disabled = false;
            }
        }

        async function unmergeItem(id) {
            if (!confirm('このまとめ買い出品を解除（バラす）し、元の個別商品を出品中に戻しますか？')) return;
            try {
                const res = await fetch('/api/items/' + id + '/unmerge', { method: 'POST' });
                if (!res.ok) throw new Error('まとめ解除に失敗しました');
                showToast('まとめ買いを解除し、元の個別商品を復元しました');
                fetchItems();
            } catch (err) {
                showToast(err.message);
            }
        }

        // <input webkitdirectory> によるPCローカルフォルダ差分同期 (HTTP環境でも動作)
        async function handleFolderSync(event) {
            const files = event.target.files;
            if (!files || files.length === 0) return;

            // 画像ファイルのみ抽出
            const imageFiles = Array.from(files).filter(f => /\.(jpg|jpeg|png|webp|gif|bmp)$/i.test(f.name));
            if (imageFiles.length === 0) {
                showToast('選択フォルダに画像ファイルが見つかりませんでした');
                event.target.value = '';
                return;
            }

            showToast('PCフォルダ走査中… ' + imageFiles.length + '枚の画像を検出');

            // 既存商品の写真ファイル名一覧を取得 (重複アップロード防止)
            const existingPhotoNames = new Set();
            allItems.forEach(item => {
                if (item.photos) {
                    item.photos.forEach(p => {
                        const extractName = pathStr => {
                            if (!pathStr) return '';
                            const parts = pathStr.replace(/\\/g, '/').split('/');
                            return parts[parts.length - 1].toLowerCase();
                        };
                        const fn1 = extractName(p.filePath);
                        const fn2 = extractName(p.url);
                        if (fn1) existingPhotoNames.add(fn1);
                        if (fn2) existingPhotoNames.add(fn2);
                    });
                }
            });

            const normalizeStr = s => s ? s.toLowerCase().replace(/[\s\-_・]/g, '') : '';

            let syncedCount = 0;
            let skippedCount = 0;

            for (const file of imageFiles) {
                // 重複チェック: 同名ファイルが既に存在するならスキップ
                if (existingPhotoNames.has(file.name.toLowerCase())) {
                    skippedCount++;
                    continue;
                }

                // webkitRelativePath (例: "フォルダ名/サブフォルダ/ファイル.jpg") からマッチング
                const relativePath = file.webkitRelativePath || '';
                const pathParts = relativePath.split('/');
                // 親フォルダ名 (直近のディレクトリ名) を商品タイトルとマッチング
                const parentDir = pathParts.length >= 2 ? pathParts[pathParts.length - 2] : '';
                const normParent = normalizeStr(parentDir);

                // マッチング優先順: 1) 親フォルダ名が商品タイトルに含まれる/含む, 2) ファイル名が商品タイトルに含まれる/含む
                let matchItem = null;
                if (normParent.length >= 2) {
                    matchItem = allItems.find(i => {
                        const normTitle = normalizeStr(i.title);
                        return normTitle && (normParent.includes(normTitle) || normTitle.includes(normParent));
                    });
                }

                if (!matchItem) {
                    const nameWithoutExt = file.name.replace(/\.[^.]+$/, '');
                    const normFileName = normalizeStr(nameWithoutExt);
                    if (normFileName.length >= 2) {
                        matchItem = allItems.find(i => {
                            const normTitle = normalizeStr(i.title);
                            return normTitle && (normFileName.includes(normTitle) || normTitle.includes(normFileName));
                        });
                    }
                }

                if (matchItem) {
                    try {
                        const formData = new FormData();
                        formData.append('photos', file);
                        const res = await fetch('/api/items/' + matchItem.id + '/photos', {
                            method: 'POST',
                            body: formData
                        });
                        if (res.ok) {
                            syncedCount++;
                            existingPhotoNames.add(file.name.toLowerCase());
                        }
                    } catch (e) {
                        // 個別エラーは無視して続行
                    }
                }
            }

            // 入力をリセット (同じフォルダを再選択可能にする)
            event.target.value = '';

            let resultMsg = 'PCフォルダ差分同期完了: ' + syncedCount + '枚の写真を追加同期';
            if (skippedCount > 0) resultMsg += ' (' + skippedCount + '枚は既存のためスキップ)';
            showToast(resultMsg);
            fetchItems();
        }

        function copyDescription(title, desc) {
            const text = title + '\n\n' + desc;
            navigator.clipboard.writeText(text).then(() => {
                showToast('タイトルと商品説明文をコピーしました！');
            }).catch(() => {
                showToast('コピーに失敗しました');
            });
        }

        function openCreateModal() {
            document.getElementById('modal-title').innerText = '新規商品出品';
            document.getElementById('form-item-id').value = '';
            document.getElementById('form-title').value = '';
            document.getElementById('form-description').value = '';
            document.getElementById('form-price').value = '';
            document.getElementById('form-status').value = 'LISTED';
            document.getElementById('form-shipping').value = '210';
            document.getElementById('form-fee-rate').value = '5';
            document.getElementById('form-expense').value = '55';
            document.getElementById('form-photos').value = '';
            document.getElementById('photo-preview-grid').innerHTML = '';
            document.getElementById('photos-input-container').classList.remove('hidden');
            document.getElementById('item-modal').classList.remove('hidden');
        }

        function openEditModal(id) {
            const item = allItems.find(i => i.id === id);
            if (!item) return;
            document.getElementById('modal-title').innerText = '商品情報の編集';
            document.getElementById('form-item-id').value = item.id;
            document.getElementById('form-title').value = item.title;
            document.getElementById('form-description').value = item.description || '';
            document.getElementById('form-price').value = item.price;
            document.getElementById('form-status').value = item.status;
            document.getElementById('form-shipping').value = item.shippingCost;
            document.getElementById('form-fee-rate').value = item.feeRatePercent;
            document.getElementById('form-expense').value = item.expenseCost;
            document.getElementById('photos-input-container').classList.add('hidden');
            document.getElementById('item-modal').classList.remove('hidden');
        }

        function closeModal() {
            document.getElementById('item-modal').classList.add('hidden');
        }

        function previewUploadPhotos(e) {
            const grid = document.getElementById('photo-preview-grid');
            grid.innerHTML = '';
            const files = e.target.files;
            for (let f of files) {
                const reader = new FileReader();
                reader.onload = ev => {
                    const img = document.createElement('img');
                    img.src = ev.target.result;
                    img.className = 'w-14 h-14 object-cover rounded-lg border border-slate-300';
                    grid.appendChild(img);
                };
                reader.readAsDataURL(f);
            }
        }

        async function handleFormSubmit(e) {
            e.preventDefault();
            const id = document.getElementById('form-item-id').value;
            const title = document.getElementById('form-title').value;
            const description = document.getElementById('form-description').value;
            const price = parseInt(document.getElementById('form-price').value) || 0;
            const status = document.getElementById('form-status').value;
            const shippingCost = parseInt(document.getElementById('form-shipping').value) || 210;
            const feeRatePercent = parseInt(document.getElementById('form-fee-rate').value) || 5;
            const expenseCost = parseInt(document.getElementById('form-expense').value) || 55;

            const submitBtn = document.getElementById('submit-btn');
            submitBtn.disabled = true;
            submitBtn.innerText = '送信中...';

            try {
                if (id) {
                    const res = await fetch('/api/items/' + id, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            title, description, price, status,
                            shippingCost, feeRatePercent, expenseCost,
                            useCustomSettings: true
                        })
                    });
                    if (!res.ok) throw new Error('更新に失敗しました');
                    showToast('商品情報を更新しました');
                } else {
                    const formData = new FormData();
                    formData.append('title', title);
                    formData.append('description', description);
                    formData.append('price', price);
                    formData.append('status', status);
                    formData.append('shippingCost', shippingCost);
                    formData.append('feeRatePercent', feeRatePercent);
                    formData.append('expenseCost', expenseCost);
                    formData.append('useCustomSettings', 'true');

                    const photoInput = document.getElementById('form-photos');
                    if (photoInput.files) {
                        for (let i = 0; i < photoInput.files.length; i++) {
                            formData.append('photos', photoInput.files[i]);
                        }
                    }

                    const res = await fetch('/api/items', {
                        method: 'POST',
                        body: formData
                    });
                    if (!res.ok) throw new Error('商品作成に失敗しました');
                    showToast('新規商品を出品登録しました');
                }
                closeModal();
                fetchItems();
            } catch (err) {
                showToast(err.message);
            } finally {
                submitBtn.disabled = false;
                submitBtn.innerText = '保存する';
            }
        }

        let currentQuickActionItem = null;

        function openQuickActionModal(itemId) {
            const item = allItems.find(i => i.id === itemId);
            if (!item) return;
            currentQuickActionItem = item;

            const mainPhotoObj = item.photos && item.photos.length > 0 ? item.photos[0] : null;
            const thumbUrl = mainPhotoObj ? (mainPhotoObj.thumbUrl || mainPhotoObj.url) : '';

            document.getElementById('quick-action-thumb').src = thumbUrl || 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="%23f1f5f9"/></svg>';
            document.getElementById('quick-action-title').innerText = item.title;
            document.getElementById('quick-action-price').innerText = '¥' + item.price.toLocaleString() + ' (利益: ¥' + item.profit.toLocaleString() + ')';

            let statusBadge = '<span class="bg-blue-100 text-blue-800 text-[10px] px-2 py-0.5 rounded-full font-bold">出品中</span>';
            if (item.status === 'SOLD') statusBadge = '<span class="bg-emerald-100 text-emerald-800 text-[10px] px-2 py-0.5 rounded-full font-bold">売却済</span>';
            if (item.status === 'MERGED') statusBadge = '<span class="bg-purple-100 text-purple-800 text-[10px] px-2 py-0.5 rounded-full font-bold">まとめ売却済み</span>';
            document.getElementById('quick-action-badge').innerHTML = statusBadge;

            const isSold = item.status === 'SOLD';
            const hasDeletedPhoto = item.photos && item.photos.some(p => p.isOriginalDeleted);

            let html = `
                <button onclick="closeQuickActionModal(); if(currentQuickActionItem && currentQuickActionItem.photos && currentQuickActionItem.photos.length>0) openImageViewer(currentQuickActionItem.photos[0].id, currentQuickActionItem.photos[0].url, currentQuickActionItem.photos[0].isOriginalDeleted);" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-slate-100 font-medium text-xs text-slate-700 flex items-center gap-3 transition">
                    <div class="w-8 h-8 rounded-lg bg-indigo-50 text-indigo-600 flex items-center justify-center text-sm"><i class="fa-solid fa-expand"></i></div>
                    <div><div class="font-bold text-slate-900">写真を全画面プレビュー</div><div class="text-[10px] text-slate-400">高画質写真を拡大確認</div></div>
                </button>

                <button onclick="closeQuickActionModal(); openEditModal(` + item.id + `);" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-slate-100 font-medium text-xs text-slate-700 flex items-center gap-3 transition">
                    <div class="w-8 h-8 rounded-lg bg-blue-50 text-blue-600 flex items-center justify-center text-sm"><i class="fa-solid fa-pen-to-square"></i></div>
                    <div><div class="font-bold text-slate-900">商品情報を編集する</div><div class="text-[10px] text-slate-400">タイトル、価格、説明文、送料等の変更</div></div>
                </button>

                <button onclick="closeQuickActionModal(); toggleStatus(` + item.id + `, '` + item.status + `');" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-slate-100 font-medium text-xs text-slate-700 flex items-center gap-3 transition">
                    <div class="w-8 h-8 rounded-lg ` + (isSold ? 'bg-blue-50 text-blue-600' : 'bg-emerald-50 text-emerald-600') + ` flex items-center justify-center text-sm"><i class="fa-solid fa-arrows-rotate"></i></div>
                    <div><div class="font-bold text-slate-900">` + (isSold ? '出品中に戻す' : '売却済みに変更する') + `</div><div class="text-[10px] text-slate-400">ステータスの切り替え</div></div>
                </button>

                <button onclick="closeQuickActionModal(); copyDescription('` + escapeJs(item.title) + `', '` + escapeJs(item.description || '') + `');" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-slate-100 font-medium text-xs text-slate-700 flex items-center gap-3 transition">
                    <div class="w-8 h-8 rounded-lg bg-amber-50 text-amber-600 flex items-center justify-center text-sm"><i class="fa-solid fa-copy"></i></div>
                    <div><div class="font-bold text-slate-900">説明文をコピー</div><div class="text-[10px] text-slate-400">メルカリ出品画面への貼り付け用</div></div>
                </button>

                <button onclick="closeQuickActionModal(); markShared(` + item.id + `);" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-slate-100 font-medium text-xs text-slate-700 flex items-center gap-3 transition">
                    <div class="w-8 h-8 rounded-lg bg-indigo-50 text-indigo-600 flex items-center justify-center text-sm"><i class="fa-solid fa-check"></i></div>
                    <div><div class="font-bold text-slate-900">PC送信済みに更新</div><div class="text-[10px] text-slate-400">PC側での保存・管理完了フラグ</div></div>
                </button>
            `;

            if (hasDeletedPhoto) {
                html += `
                <button onclick="closeQuickActionModal(); document.getElementById('restore-file-input').click();" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-amber-50 font-medium text-xs text-amber-800 flex items-center gap-3 transition border border-amber-200">
                    <div class="w-8 h-8 rounded-lg bg-amber-100 text-amber-600 flex items-center justify-center text-sm"><i class="fa-solid fa-cloud-arrow-up"></i></div>
                    <div><div class="font-bold">PCから高画質元画像を復元</div><div class="text-[10px] text-amber-600">削除されたスマホ用元画像をPC写真から復元</div></div>
                </button>
                `;
            }

            html += `
                <div class="pt-1 border-t border-slate-100">
                    <button onclick="closeQuickActionModal(); deleteItem(` + item.id + `, '` + escapeJs(item.title) + `');" class="w-full text-left px-3.5 py-2.5 rounded-xl hover:bg-rose-50 font-medium text-xs text-rose-600 flex items-center gap-3 transition">
                        <div class="w-8 h-8 rounded-lg bg-rose-50 text-rose-600 flex items-center justify-center text-sm"><i class="fa-solid fa-trash-can"></i></div>
                        <div><div class="font-bold">商品を削除する</div><div class="text-[10px] text-rose-400">データベースおよび写真の完全削除</div></div>
                    </button>
                </div>
            `;

            document.getElementById('quick-action-body').innerHTML = html;
            document.getElementById('quick-action-modal').classList.remove('hidden');
        }

        function closeQuickActionModal() {
            document.getElementById('quick-action-modal').classList.add('hidden');
            currentQuickActionItem = null;
        }

        function openImageViewer(photoId, url, isOriginalDeleted) {
            currentViewingPhoto = photoId;
            document.getElementById('image-viewer-src').src = url;
            const banner = document.getElementById('restore-banner');
            if (isOriginalDeleted) {
                banner.classList.remove('hidden');
            } else {
                banner.classList.add('hidden');
            }
            document.getElementById('image-viewer-modal').classList.remove('hidden');
        }

        function closeImageViewer() {
            document.getElementById('image-viewer-modal').classList.add('hidden');
            currentViewingPhoto = null;
        }

        async function uploadRestoredPhoto(e) {
            if (!currentViewingPhoto || !e.target.files || e.target.files.length === 0) return;
            const file = e.target.files[0];
            const formData = new FormData();
            formData.append('photo', file);

            try {
                const res = await fetch('/api/photos/' + currentViewingPhoto + '/restore', {
                    method: 'POST',
                    body: formData
                });
                if (!res.ok) throw new Error('復元アップロードに失敗しました');
                showToast('元高画質写真をスマホへ復元しました！');
                closeImageViewer();
                fetchItems();
            } catch (err) {
                showToast(err.message);
            }
        }

        function showToast(msg) {
            const toast = document.getElementById('toast');
            document.getElementById('toast-message').innerText = msg;
            toast.classList.remove('translate-y-16', 'opacity-0');
            setTimeout(() => {
                toast.classList.add('translate-y-16', 'opacity-0');
            }, 3000);
        }

        function escapeHtml(str) {
            if (str === null || str === undefined) return '';
            return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        }

        function escapeJs(str) {
            if (str === null || str === undefined) return '';
            return String(str).replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\r/g, '');
        }
    </script>
</body>
</html>
        """.trimIndent()
    }
}
