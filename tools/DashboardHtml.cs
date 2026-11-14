using System;

public static class DashboardHtml
{
    public static string GetHtml(string authToken = "")
    {
        return @"<!DOCTYPE html>
<html lang='ja'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>フリマ商品PC管理 (Yahoo!フリマ対応母艦サーバー)</title>
    <script src='https://cdn.tailwindcss.com'></script>
    <link href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css' rel='stylesheet'>
    <style>
        .custom-scroll::-webkit-scrollbar { width: 6px; height: 6px; }
        .custom-scroll::-webkit-scrollbar-track { background: #f1f5f9; }
        .custom-scroll::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 3px; }
    </style>
</head>
<body class='bg-slate-100 text-slate-800 min-h-screen'>
    <!-- トースト通知 -->
    <div id='toast' class='fixed bottom-5 right-5 z-50 bg-slate-900 text-white px-4 py-2.5 rounded-lg shadow-xl text-xs font-bold flex items-center gap-2 transform transition-all duration-300 translate-y-20 opacity-0 pointer-events-none'>
        <i class='fa-solid fa-circle-check text-emerald-400'></i>
        <span id='toast-msg'>コピーしました</span>
    </div>

    <!-- スマホ接続ステータス通知バナー -->
    <div id='status-banner' class='bg-amber-500 text-white px-4 py-1.5 text-xs font-bold flex items-center justify-between shadow-sm transition-all duration-300 sticky top-0 z-40'>
        <div class='flex items-center gap-2'>
            <i id='status-icon' class='fa-solid fa-triangle-exclamation text-base animate-bounce'></i>
            <span id='status-text'>スマホアプリ未接続（PC単独で編集可／スマホ再接続時に自動同期）。スマホアプリの「PC連携」画面から接続できます。</span>
        </div>
        <span class='bg-amber-700/60 px-2 py-0.5 rounded text-[11px] font-mono'>v0.9β (Build 8)</span>
    </div>

    <!-- ナビゲーションバー (Yahoo!フリマ出品支援ヘッダー) -->
    <header class='bg-indigo-700 text-white shadow-md sticky top-7 z-30'>
        <div class='max-w-[1800px] mx-auto px-3 sm:px-4 h-12 flex items-center justify-between gap-3'>
            <!-- 左側: タイトル & Yahoo!フリマショートカット -->
            <div class='flex items-center space-x-2 shrink-0'>
                <i class='fa-solid fa-boxes-stacked text-lg text-indigo-200'></i>
                <h1 class='font-black text-sm tracking-tight whitespace-nowrap'>フリマ商品PC管理</h1>
                <span class='bg-indigo-800/90 text-indigo-100 text-[10px] px-1.5 py-0.5 rounded font-mono font-semibold border border-indigo-500'>母艦サーバー</span>
                <button onclick='openYahooSplit()' class='bg-rose-600 hover:bg-rose-700 text-white text-[11px] font-bold px-2.5 py-1 rounded-md shadow-sm transition flex items-center gap-1.5 ml-2 border border-rose-400/50' title='Yahoo!フリマ出品画面を通常タブで開きます（タブを切り離して Win + ← / → で左右に並べて利用できます）'>
                    <i class='fa-solid fa-up-right-from-square text-[10px]'></i> Yahoo!フリマ出品画面を開く
                </button>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス -->
            <div class='flex items-center gap-2 flex-1 justify-end overflow-x-auto custom-scroll py-0.5'>
                <div class='flex items-center gap-1 bg-indigo-800/60 p-0.5 rounded-lg shrink-0'>
                    <button onclick='setFilter(""ALL"")' id='filter-all' class='filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition'>すべて (<span id='count-all'>0</span>)</button>
                    <button onclick='setFilter(""UNSHARED"")' id='filter-unshared' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>未出品 (<span id='count-unshared'>0</span>)</button>
                    <button onclick='setFilter(""LISTED"")' id='filter-listed' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>出品中 (<span id='count-listed'>0</span>)</button>
                    <button onclick='setFilter(""SOLD"")' id='filter-sold' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>売却済 (<span id='count-sold'>0</span>)</button>
                    <button onclick='setFilter(""TRASH"")' id='filter-trash' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'><i class='fa-solid fa-trash-can mr-0.5'></i> ゴミ箱 (<span id='count-trash'>0</span>)</button>
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
            <!-- 左側: メイン商品グリッド表示エリア -->
            <div class='flex-1 w-full min-w-0'>
                <div id='trash-banner' class='hidden mb-3 bg-rose-50 border border-rose-200 rounded-lg px-3.5 py-2.5 flex flex-wrap items-center justify-between gap-2'>
                    <div class='flex items-center gap-2 text-xs text-rose-800'>
                        <i class='fa-solid fa-trash-can text-rose-600 text-sm'></i>
                        <span class='font-bold'>ゴミ箱</span>
                        <span class='text-rose-700'>削除した商品は30日間保管され、いつでも復元できます。30日経過後に写真を含め自動完全削除されます。</span>
                    </div>
                    <button onclick='emptyTrash()' id='empty-trash-btn' class='px-3 py-1 bg-rose-600 hover:bg-rose-700 text-white rounded text-xs font-bold shadow-sm transition flex items-center gap-1'>
                        <i class='fa-solid fa-ban'></i> ゴミ箱を空にする
                    </button>
                </div>

                <div id='items-grid' class='grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6 gap-3'>
                    <!-- JavaScriptで動的生成 -->
                </div>

                <div id='empty-state' class='hidden text-center py-12 bg-white rounded-lg border border-slate-200'>
                    <i class='fa-solid fa-box-open text-3xl text-slate-300 mb-2'></i>
                    <p class='text-xs text-slate-500 font-medium'>該当する商品が見つかりません</p>
                </div>
            </div>

            <!-- 右側: 固定コントロールパネル (売上分析拡充) -->
            <aside class='w-full lg:w-64 shrink-0 sticky top-24 space-y-3'>
                <div class='bg-white rounded-lg p-3 shadow-sm border border-slate-200 space-y-2'>
                    <h2 class='text-[11px] font-bold text-slate-400 uppercase tracking-wider flex items-center gap-1'>
                        <i class='fa-solid fa-chart-pie text-indigo-500'></i> 実績・売上分析
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
                        <div class='grid grid-cols-2 gap-2 pt-0.5'>
                            <div class='bg-indigo-50/60 p-1.5 rounded border border-indigo-100'>
                                <p class='text-[9px] text-slate-500 font-medium'>利益率</p>
                                <p id='stat-profit-margin' class='text-xs font-black text-indigo-700'>0.0%</p>
                            </div>
                            <div class='bg-purple-50/60 p-1.5 rounded border border-purple-100'>
                                <p class='text-[9px] text-slate-500 font-medium'>平均売価</p>
                                <p id='stat-avg-price' class='text-xs font-black text-purple-700'>¥0</p>
                            </div>
                        </div>

                        <!-- 経費・手数料内訳サマリー -->
                        <div class='border-t border-slate-100 pt-1.5 text-[10px] text-slate-500 space-y-1'>
                            <div class='flex justify-between'>
                                <span>販売手数料合計:</span>
                                <span id='stat-total-fee' class='font-mono font-bold text-slate-700'>¥0</span>
                            </div>
                            <div class='flex justify-between'>
                                <span>送料合計:</span>
                                <span id='stat-total-shipping' class='font-mono font-bold text-slate-700'>¥0</span>
                            </div>
                            <div class='flex justify-between'>
                                <span>梱包・経費合計:</span>
                                <span id='stat-total-expense' class='font-mono font-bold text-slate-700'>¥0</span>
                            </div>
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
                    <button onclick='openAddModal()' class='w-full bg-emerald-600 hover:bg-emerald-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-plus text-xs'></i> 新規出品追加 (PC)
                    </button>
                    <button onclick='openSettingsModal()' class='w-full bg-slate-700 hover:bg-slate-800 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-gear text-xs'></i> 設定・プリセット・テンプレート管理
                    </button>
                    <button onclick='downloadPcCsv()' class='w-full bg-teal-600 hover:bg-teal-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-file-csv text-xs'></i> 売上・全商品CSVダウンロード
                    </button>
                    <button onclick='fetchItems()' class='w-full bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-rotate-right text-xs'></i> データ手動更新
                    </button>
                </div>
            </aside>
        </div>
    </main>

    <!-- まとめ買い作成フローティング操作バー (チェックボックス選択時に表示) -->
    <div id='merge-floating-bar' class='fixed bottom-5 left-1/2 -translate-x-1/2 z-50 bg-slate-900/95 text-white px-5 py-2.5 rounded-full shadow-2xl flex items-center gap-3 border border-slate-700 hidden'>
        <span class='text-xs font-bold text-indigo-300'><span id='selected-count'>0</span>点選択中</span>
        <button onclick='openMergeModal()' class='bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1.5'>
            <i class='fa-solid fa-gift'></i> まとめ出品を作成
        </button>
        <button onclick='clearSelection()' class='text-slate-400 hover:text-white text-xs font-bold px-2 py-1'>選択クリア</button>
    </div>

    <!-- まとめ買い作成モーダル -->
    <div id='merge-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeMergeModal()'>
        <div class='bg-white rounded-xl max-w-lg w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-purple-700 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-gift text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>まとめ買い出品の作成 (PC母艦)</h3>
                </div>
                <button onclick='closeMergeModal()' class='text-purple-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-3.5 text-xs max-h-[75vh] overflow-y-auto custom-scroll'>
                <div class='bg-purple-50 border border-purple-200 rounded-lg p-2.5 text-purple-900 leading-tight'>
                    <p class='font-bold flex items-center gap-1'><i class='fa-solid fa-shield-halved text-purple-600'></i> 子商品データの保全</p>
                    <p class='text-[11px] mt-0.5 text-purple-700'>選択された個別商品のID・写真データ・出品日時はそのまま安全に保持され、親商品のみが新設されます。</p>
                </div>

                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>まとめ商品タイトル (Yahoo!フリマ40文字以内)</label>
                        <div class='flex items-center gap-2'>
                            <select id='merge-title-tpl-select' onchange='applyTemplateSelect(""merge-title"", this, true)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[160px]'>
                                <option value=''>定型文を挿入...</option>
                            </select>
                            <span id='merge-title-count' class='text-[10px] font-mono text-slate-400'>0/40文字</span>
                        </div>
                    </div>
                    <input type='text' id='merge-title' oninput='checkTitleLength(""merge"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-purple-500'>
                </div>

                <div class='grid grid-cols-2 gap-3'>
                    <div>
                        <label class='block font-bold text-slate-700 mb-1'>まとめ販売価格 (円)</label>
                        <input type='number' id='merge-price' oninput='updateMergeCalculations()' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-purple-500'>
                    </div>
                    <div>
                        <label class='block font-bold text-slate-700 mb-1'>元商品合計額 / 値引</label>
                        <div id='merge-discount-text' class='p-2 bg-slate-100 rounded-lg text-slate-600 font-mono font-bold text-center'>¥0</div>
                    </div>
                </div>

                <div class='grid grid-cols-3 gap-2'>
                    <div>
                        <label class='block font-medium text-slate-600 mb-1 text-[11px]'>送料 (円)</label>
                        <input type='number' id='merge-shipping' value='210' oninput='updateMergeCalculations()' class='w-full p-1.5 border border-slate-300 rounded focus:outline-none'>
                    </div>
                    <div>
                        <label class='block font-medium text-slate-600 mb-1 text-[11px]'>手数料率 (%)</label>
                        <input type='number' id='merge-fee-rate' value='5' oninput='updateMergeCalculations()' class='w-full p-1.5 border border-slate-300 rounded focus:outline-none'>
                    </div>
                    <div>
                        <label class='block font-medium text-slate-600 mb-1 text-[11px]'>梱包経費 (円)</label>
                        <input type='number' id='merge-expense' value='55' oninput='updateMergeCalculations()' class='w-full p-1.5 border border-slate-300 rounded focus:outline-none'>
                    </div>
                </div>

                <div class='bg-slate-50 p-2 rounded border border-slate-200 flex justify-between items-center font-bold'>
                    <span class='text-slate-600'>見込み純利益:</span>
                    <span id='merge-profit-display' class='text-sm text-emerald-600 font-black'>¥0</span>
                </div>

                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品説明文</label>
                        <select id='merge-desc-tpl-select' onchange='applyTemplateSelect(""merge-description"", this, false)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[180px]'>
                            <option value=''>説明定型文を挿入...</option>
                        </select>
                    </div>
                    <textarea id='merge-description' rows='6' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-purple-500 font-mono text-[11px]'></textarea>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button onclick='closeMergeModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button onclick='submitMerge()' class='px-4 py-2 text-xs font-bold bg-purple-600 hover:bg-purple-700 text-white rounded-lg shadow-sm transition'>まとめ出品を作成</button>
            </div>
        </div>
    </div>

    <!-- 写真ギャラリービューアーモーダル (写真D&D / クリップボードコピー / グループ分け対応) -->
    <div id='photo-viewer-modal' class='fixed inset-0 bg-black/80 z-50 hidden flex items-center justify-center p-3 sm:p-5' onclick='closePhotoViewer()'>
        <div class='bg-white rounded-xl max-w-4xl w-full max-h-[90vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-indigo-800 text-white px-5 py-3 flex items-center justify-between border-b border-indigo-900'>
                <div class='flex items-center gap-2.5 overflow-hidden'>
                    <i class='fa-solid fa-images text-indigo-300 text-lg shrink-0'></i>
                    <div class='overflow-hidden'>
                        <h3 id='viewer-title' class='font-bold text-sm tracking-tight truncate'>商品写真ギャラリー</h3>
                        <p id='viewer-subtitle' class='text-[11px] text-indigo-200 truncate'>出品画面へ写真をドラッグ＆ドロップまたはコピーできます</p>
                    </div>
                </div>
                <div class='flex items-center gap-2 shrink-0'>
                    <button id='viewer-open-folder-btn' class='bg-indigo-700 hover:bg-indigo-600 text-white text-xs font-bold px-2.5 py-1.5 rounded-lg border border-indigo-500/60 transition flex items-center gap-1.5'>
                        <i class='fa-regular fa-folder-open text-amber-300'></i> PCフォルダで開く
                    </button>
                    <button onclick='closePhotoViewer()' class='text-indigo-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
                </div>
            </div>

            <!-- タブ切り替えバー (まとめ買い商品の場合のみ表示、デフォルトはグループ分け) -->
            <div id='viewer-tabs-bar' class='bg-slate-100 px-5 py-2 border-b border-slate-200 flex items-center justify-between hidden'>
                <div class='flex gap-1'>
                    <button id='viewer-tab-group' onclick='switchViewerTab(""group"")' class='px-3 py-1 rounded text-xs font-bold bg-white text-indigo-700 shadow-sm border border-slate-200 transition flex items-center gap-1'>
                        <i class='fa-solid fa-layer-group'></i> 商品別グループ分け (デフォルト)
                    </button>
                    <button id='viewer-tab-all' onclick='switchViewerTab(""all"")' class='px-3 py-1 rounded text-xs font-medium text-slate-600 hover:text-slate-800 transition flex items-center gap-1'>
                        <i class='fa-solid fa-grip'></i> 全写真一覧
                    </button>
                </div>
                <span id='viewer-photo-count' class='text-[11px] font-mono text-slate-500'>0枚の写真</span>
            </div>

            <div class='p-4 flex-1 overflow-y-auto custom-scroll space-y-4'>
                <!-- 写真ドラッグ＆ドロップ追加ゾーン -->
                <div id='photo-drop-zone' class='border-2 border-dashed border-indigo-200 hover:border-indigo-400 bg-indigo-50/40 rounded-xl p-3 text-center transition cursor-pointer'>
                    <i class='fa-solid fa-cloud-arrow-up text-indigo-500 text-xl mb-1'></i>
                    <p class='text-xs font-bold text-slate-700'>PCから画像ファイルをドラッグ＆ドロップして写真を追加</p>
                    <p class='text-[10px] text-slate-400'>JPG, PNG 等の画像ファイルを直接ドロップして本商品に追加できます</p>
                    <input type='file' id='photo-drop-file-input' accept='image/*' multiple class='hidden' onchange='handleFileInputChange(event)'>
                </div>

                <!-- 写真プレビュー表示グリッド -->
                <div id='viewer-photos-container' class='space-y-4'>
                    <!-- JavaScriptで動的生成 -->
                </div>
            </div>

            <div class='bg-slate-50 px-5 py-2.5 border-t border-slate-200 flex items-center justify-between text-[11px] text-slate-500'>
                <span class='flex items-center gap-1.5'><i class='fa-solid fa-circle-info text-indigo-500'></i> 画像を長押しドラッグするか、「画像コピー」ボタンで出品画面に貼り付けられます。</span>
                <button onclick='closePhotoViewer()' class='px-4 py-1.5 text-xs font-bold bg-slate-200 hover:bg-slate-300 text-slate-700 rounded-lg transition'>閉じる</button>
            </div>
        </div>
    </div>

    <!-- 30日経過写真クリーンアップモーダル -->
    <div id='cleanup-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeCleanupModal()'>
        <div class='bg-white rounded-xl max-w-2xl w-full max-h-[85vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-slate-900 text-white px-5 py-3.5 flex items-center justify-between border-b border-slate-800'>
                <div class='flex items-center gap-2.5'>
                    <i class='fa-solid fa-broom text-amber-400 text-lg'></i>
                    <div>
                        <h3 class='font-bold text-sm tracking-tight'>売却30日経過写真のクリーンアップ</h3>
                        <p class='text-[11px] text-slate-400'>売却成立から30日以上経過した商品の写真ファイルを安全に一括削除します</p>
                    </div>
                </div>
                <button onclick='closeCleanupModal()' class='text-slate-400 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
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
                            <tbody id='cleanup-candidates-table' class='divide-y divide-slate-100 text-slate-700'></tbody>
                        </table>
                    </div>
                </div>
                <div class='bg-slate-50 border border-slate-200 rounded-lg p-2.5 text-[11px] text-slate-500 space-y-1'>
                    <p class='font-bold text-slate-700 flex items-center gap-1'><i class='fa-solid fa-shield-halved text-emerald-600'></i> 安全性のご案内</p>
                    <p>・出品中・未出品の写真は削除対象に含まれません。</p>
                    <p>・PC上の写真ファイルのみをクリーンアップし、商品テキストデータはそのまま保持されます。</p>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button onclick='closeCleanupModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button id='cleanup-execute-btn' onclick='executeCleanup()' class='px-4 py-2 text-xs font-bold bg-amber-600 hover:bg-amber-700 text-white rounded-lg shadow-sm transition disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-1.5'>
                    <i class='fa-solid fa-trash-can'></i> クリーンアップを実行
                </button>
            </div>
        </div>
    </div>

    <!-- 新規商品追加モーダル (スマホ同等UI) -->
    <div id='add-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeAddModal()'>
        <div class='bg-white rounded-xl max-w-xl w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-emerald-700 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-plus text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>新規商品の追加 (PC母艦)</h3>
                </div>
                <button onclick='closeAddModal()' class='text-emerald-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-4 text-xs max-h-[78vh] overflow-y-auto custom-scroll'>
                <!-- ① 商品写真セクション -->
                <div class='space-y-2'>
                    <div class='flex items-center justify-between'>
                        <label class='font-bold text-slate-700 flex items-center gap-1.5'>
                            <i class='fa-solid fa-camera text-emerald-600'></i> 商品写真 (<span id='add-photo-count'>0</span>枚)
                        </label>
                        <span class='text-[10px] text-slate-400'>※ 登録確定時にアップロードされます</span>
                    </div>
                    <div id='add-photo-drop-zone' class='border-2 border-dashed border-emerald-200 hover:border-emerald-400 bg-emerald-50/30 rounded-xl p-3 text-center transition cursor-pointer'>
                        <i class='fa-solid fa-cloud-arrow-up text-emerald-500 text-lg mb-0.5'></i>
                        <p class='text-xs font-bold text-slate-700'>クリックして画像を選択、またはここにドラッグ＆ドロップ</p>
                        <p class='text-[10px] text-slate-400'>JPG, PNG 等の複数選択に対応しています</p>
                        <input type='file' id='add-photo-input' accept='image/*' multiple class='hidden' onchange='handleAddPhotoInputChange(event)'>
                    </div>
                    <div id='add-photo-preview-grid' class='flex flex-wrap gap-2'></div>
                </div>

                <!-- ② 商品基本情報 -->
                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品タイトル (Yahoo!フリマ40文字以内) <span class='text-rose-500'>*</span></label>
                        <div class='flex items-center gap-2'>
                            <select id='add-title-tpl-select' onchange='applyTemplateSelect(""add-title"", this, true)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[160px]'>
                                <option value=''>定型文を挿入...</option>
                            </select>
                            <span id='add-title-count' class='text-[10px] font-mono text-slate-400'>0/40文字</span>
                        </div>
                    </div>
                    <input type='text' id='add-title' oninput='checkTitleLength(""add"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-emerald-500' placeholder='例: ヴィンテージ デニムジャケット Mサイズ'>
                </div>

                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品説明文</label>
                        <select id='add-desc-tpl-select' onchange='applyTemplateSelect(""add-description"", this, false)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[180px]'>
                            <option value=''>説明定型文を挿入...</option>
                        </select>
                    </div>
                    <textarea id='add-description' rows='3' oninput='updateModalValidation(""add"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-emerald-500' placeholder='商品の状態や詳細をご入力ください...'></textarea>
                </div>

                <!-- ステータス選択チップ -->
                <div>
                    <label class='block font-bold text-slate-700 mb-1.5'>ステータス</label>
                    <input type='hidden' id='add-status' value='DRAFT'>
                    <div class='flex flex-wrap gap-2'>
                        <button type='button' id='add-status-chip-DRAFT' onclick='setModalStatus(""add"", ""DRAFT"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き (未出品)</button>
                        <button type='button' id='add-status-chip-LISTED' onclick='setModalStatus(""add"", ""LISTED"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>出品中</button>
                        <button type='button' id='add-status-chip-SOLD' onclick='setModalStatus(""add"", ""SOLD"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>売却済み</button>
                    </div>
                </div>

                <div class='grid grid-cols-1 sm:grid-cols-2 gap-3'>
                    <div>
                        <label class='block font-bold text-slate-700 mb-1'>出品価格 (円)</label>
                        <input type='number' min='0' id='add-price' oninput='calcModalProfit(""add"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-emerald-500' placeholder='5000'>
                    </div>
                    <div id='add-sold-price-group' class='hidden'>
                        <label class='block font-bold text-slate-700 mb-1'>実際の売却価格 (円)</label>
                        <input type='number' min='0' id='add-sold-price' oninput='calcModalProfit(""add"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-emerald-500' placeholder='空欄時は出品価格'>
                    </div>
                </div>

                <!-- ③ 費用設定・プリセット & 利益シミュレーション -->
                <div class='bg-slate-50 p-3.5 rounded-xl border border-slate-200 space-y-3'>
                    <div class='flex items-center justify-between gap-2'>
                        <div>
                            <p class='font-bold text-slate-800'>個別費用設定 (カスタマイズ)</p>
                            <p id='add-default-summary' class='text-[10px] text-slate-500 mt-0.5'>デフォルト設定適用中</p>
                        </div>
                        <label class='inline-flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 shrink-0'>
                            <input type='checkbox' id='add-use-custom' onchange='toggleCustomSettings(""add"")' class='w-4 h-4 text-emerald-600 rounded border-slate-300 focus:ring-emerald-500'>
                            <span class='text-[11px]'>有効にする</span>
                        </label>
                    </div>

                    <!-- モール別プリセットチップ -->
                    <div>
                        <p class='text-[10px] font-bold text-slate-500 mb-1'>出品先プリセット (クリックで費用を一括反映)</p>
                        <div id='add-preset-chips' class='flex flex-wrap gap-1.5'></div>
                    </div>

                    <div id='add-custom-settings-fields' class='space-y-3 hidden pt-1 border-t border-slate-200'>
                        <!-- 手数料率 + クイックチップ -->
                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>販売手数料率 (%)</label>
                            <input type='number' min='0' id='add-fee-rate' value='5' oninput='onCostInput(""add"")' class='w-full p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                            <div class='flex flex-wrap gap-1.5 mt-1.5'>
                                <button type='button' id='add-fee-chip-5' onclick='setQuickFee(""add"", 5)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>5% (PayPay等)</button>
                                <button type='button' id='add-fee-chip-10' onclick='setQuickFee(""add"", 10)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>10% (メルカリ等)</button>
                            </div>
                        </div>

                        <!-- 送料 + クイックチップ -->
                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>送料 (円)</label>
                            <input type='number' min='0' id='add-shipping' value='210' oninput='onCostInput(""add"")' class='w-full p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                            <div class='flex flex-wrap gap-1.5 mt-1.5'>
                                <button type='button' id='add-ship-chip-0' onclick='setQuickShipping(""add"", 0)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>0円 (無料)</button>
                                <button type='button' id='add-ship-chip-210' onclick='setQuickShipping(""add"", 210)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>210円 (ネコポス等)</button>
                                <button type='button' id='add-ship-chip-450' onclick='setQuickShipping(""add"", 450)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>450円 (コンパクト)</button>
                                <button type='button' id='add-ship-chip-750' onclick='setQuickShipping(""add"", 750)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>750円 (60サイズ)</button>
                            </div>
                        </div>

                        <!-- 経費 -->
                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>経費・梱包資材費 (円)</label>
                            <div class='flex items-center gap-2'>
                                <input type='number' min='0' id='add-expense' value='55' oninput='onCostInput(""add"")' class='flex-1 p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                                <button type='button' onclick='setQuickExpense(""add"", 0)' class='px-2.5 py-1.5 rounded-md text-[10px] font-bold border border-slate-300 bg-white hover:bg-slate-100 text-slate-700 transition'>0円</button>
                            </div>
                        </div>
                    </div>

                    <!-- リアルタイム利益シミュレーションカード -->
                    <div class='bg-indigo-50 border border-indigo-200 rounded-lg p-2.5 space-y-1'>
                        <p class='text-[11px] font-bold text-indigo-900'>🧮 利益シミュレーション</p>
                        <p id='add-profit-formula' class='text-[10px] text-indigo-800 font-mono'>売価 ¥0 - 手数料(5%) ¥0 - 送料 ¥210 - 経費 ¥55</p>
                        <p id='add-profit-value' class='text-sm font-black text-indigo-700'>純利益: ¥0 (0%)</p>
                    </div>
                    <div id='add-validation-banner' class='hidden rounded-lg p-2.5 text-[11px] border space-y-1'></div>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button onclick='closeAddModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button id='add-submit-btn' onclick='createItem()' class='px-4 py-2 text-xs font-bold bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg shadow-sm transition'>出品を追加</button>
            </div>
        </div>
    </div>

    <!-- 商品編集モーダル (Yahoo!フリマ・スマホ同等UI) -->
    <div id='edit-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeEditModal()'>
        <div class='bg-white rounded-xl max-w-xl w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-indigo-700 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-pen-to-square text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>商品情報の編集 (Yahoo!フリマ)</h3>
                </div>
                <button onclick='closeEditModal()' class='text-indigo-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-4 text-xs max-h-[78vh] overflow-y-auto custom-scroll'>
                <input type='hidden' id='edit-id'>

                <!-- ① 商品写真管理セクション -->
                <div class='space-y-2'>
                    <div class='flex items-center justify-between'>
                        <label class='font-bold text-slate-700 flex items-center gap-1.5'>
                            <i class='fa-solid fa-images text-indigo-600'></i> 商品写真 (<span id='edit-photo-count'>0</span>枚)
                        </label>
                        <button type='button' id='edit-open-folder-btn' class='text-[11px] font-bold text-indigo-600 hover:text-indigo-800 flex items-center gap-1 transition'>
                            <i class='fa-regular fa-folder-open'></i> PCフォルダを開く
                        </button>
                    </div>
                    <div id='edit-photo-drop-zone' class='border-2 border-dashed border-indigo-200 hover:border-indigo-400 bg-indigo-50/30 rounded-xl p-3 text-center transition cursor-pointer'>
                        <i class='fa-solid fa-cloud-arrow-up text-indigo-500 text-lg mb-0.5'></i>
                        <p class='text-xs font-bold text-slate-700'>クリックして写真を追加、またはここにドラッグ＆ドロップ</p>
                        <p class='text-[10px] text-slate-400'>追加・削除は即座にPCフォルダとスマホ同期へ反映されます</p>
                        <input type='file' id='edit-photo-input' accept='image/*' multiple class='hidden' onchange='handleEditPhotoInputChange(event)'>
                    </div>
                    <div id='edit-photo-preview-grid' class='flex flex-wrap gap-2'></div>
                </div>

                <!-- ② 商品基本情報 -->
                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品タイトル (Yahoo!フリマ40文字以内) <span class='text-rose-500'>*</span></label>
                        <div class='flex items-center gap-2'>
                            <select id='edit-title-tpl-select' onchange='applyTemplateSelect(""edit-title"", this, true)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[160px]'>
                                <option value=''>定型文を挿入...</option>
                            </select>
                            <span id='edit-title-count' class='text-[10px] font-mono text-slate-400'>0/40文字</span>
                        </div>
                    </div>
                    <input type='text' id='edit-title' oninput='checkTitleLength(""edit"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500'>
                </div>

                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品説明文</label>
                        <select id='edit-desc-tpl-select' onchange='applyTemplateSelect(""edit-description"", this, false)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[180px]'>
                            <option value=''>説明定型文を挿入...</option>
                        </select>
                    </div>
                    <textarea id='edit-description' rows='4' oninput='updateModalValidation(""edit"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500'></textarea>
                </div>

                <!-- ステータス選択チップ -->
                <div>
                    <label class='block font-bold text-slate-700 mb-1.5'>ステータス</label>
                    <input type='hidden' id='edit-status' value='LISTED'>
                    <div class='flex flex-wrap gap-2'>
                        <button type='button' id='edit-status-chip-DRAFT' onclick='setModalStatus(""edit"", ""DRAFT"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き (未出品)</button>
                        <button type='button' id='edit-status-chip-LISTED' onclick='setModalStatus(""edit"", ""LISTED"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>出品中</button>
                        <button type='button' id='edit-status-chip-SOLD' onclick='setModalStatus(""edit"", ""SOLD"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>売却済み</button>
                    </div>
                </div>

                <div class='grid grid-cols-1 sm:grid-cols-2 gap-3'>
                    <div>
                        <label class='block font-bold text-slate-700 mb-1'>出品価格 (円)</label>
                        <input type='number' min='0' id='edit-price' oninput='calcModalProfit(""edit"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500'>
                    </div>
                    <div id='edit-sold-price-group' class='hidden'>
                        <label class='block font-bold text-slate-700 mb-1'>実際の売却価格 (円)</label>
                        <input type='number' min='0' id='edit-sold-price' oninput='calcModalProfit(""edit"")' class='w-full p-2 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500' placeholder='空欄時は出品価格'>
                    </div>
                </div>

                <!-- ③ 費用設定・プリセット & 利益シミュレーション -->
                <div class='bg-slate-50 p-3.5 rounded-xl border border-slate-200 space-y-3'>
                    <div class='flex items-center justify-between gap-2'>
                        <div>
                            <p class='font-bold text-slate-800'>個別費用設定 (カスタマイズ)</p>
                            <p id='edit-default-summary' class='text-[10px] text-slate-500 mt-0.5'>デフォルト設定適用中</p>
                        </div>
                        <label class='inline-flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 shrink-0'>
                            <input type='checkbox' id='edit-use-custom' onchange='toggleCustomSettings(""edit"")' class='w-4 h-4 text-indigo-600 rounded border-slate-300 focus:ring-indigo-500'>
                            <span class='text-[11px]'>有効にする</span>
                        </label>
                    </div>

                    <!-- モール別プリセットチップ -->
                    <div>
                        <p class='text-[10px] font-bold text-slate-500 mb-1'>出品先プリセット (クリックで費用を一括反映)</p>
                        <div id='edit-preset-chips' class='flex flex-wrap gap-1.5'></div>
                    </div>

                    <div id='edit-custom-settings-fields' class='space-y-3 hidden pt-1 border-t border-slate-200'>
                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>販売手数料率 (%)</label>
                            <input type='number' min='0' id='edit-fee-rate' value='5' oninput='onCostInput(""edit"")' class='w-full p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                            <div class='flex flex-wrap gap-1.5 mt-1.5'>
                                <button type='button' id='edit-fee-chip-5' onclick='setQuickFee(""edit"", 5)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>5% (PayPay等)</button>
                                <button type='button' id='edit-fee-chip-10' onclick='setQuickFee(""edit"", 10)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>10% (メルカリ等)</button>
                            </div>
                        </div>

                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>送料 (円)</label>
                            <input type='number' min='0' id='edit-shipping' value='210' oninput='onCostInput(""edit"")' class='w-full p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                            <div class='flex flex-wrap gap-1.5 mt-1.5'>
                                <button type='button' id='edit-ship-chip-0' onclick='setQuickShipping(""edit"", 0)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>0円 (無料)</button>
                                <button type='button' id='edit-ship-chip-210' onclick='setQuickShipping(""edit"", 210)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>210円 (ネコポス等)</button>
                                <button type='button' id='edit-ship-chip-450' onclick='setQuickShipping(""edit"", 450)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>450円 (コンパクト)</button>
                                <button type='button' id='edit-ship-chip-750' onclick='setQuickShipping(""edit"", 750)' class='px-2 py-0.5 rounded-md text-[10px] font-bold border transition'>750円 (60サイズ)</button>
                            </div>
                        </div>

                        <div>
                            <label class='block text-[11px] font-bold text-slate-700 mb-1'>経費・梱包資材費 (円)</label>
                            <div class='flex items-center gap-2'>
                                <input type='number' min='0' id='edit-expense' value='55' oninput='onCostInput(""edit"")' class='flex-1 p-1.5 border border-slate-300 rounded-lg text-xs bg-white'>
                                <button type='button' onclick='setQuickExpense(""edit"", 0)' class='px-2.5 py-1.5 rounded-md text-[10px] font-bold border border-slate-300 bg-white hover:bg-slate-100 text-slate-700 transition'>0円</button>
                            </div>
                        </div>
                    </div>

                    <!-- リアルタイム利益シミュレーションカード -->
                    <div class='bg-indigo-50 border border-indigo-200 rounded-lg p-2.5 space-y-1'>
                        <p class='text-[11px] font-bold text-indigo-900'>🧮 利益シミュレーション</p>
                        <p id='edit-profit-formula' class='text-[10px] text-indigo-800 font-mono'>売価 ¥0 - 手数料(5%) ¥0 - 送料 ¥210 - 経費 ¥55</p>
                        <p id='edit-profit-value' class='text-sm font-black text-indigo-700'>純利益: ¥0 (0%)</p>
                    </div>
                    <div id='edit-validation-banner' class='hidden rounded-lg p-2.5 text-[11px] border space-y-1'></div>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button onclick='closeEditModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button id='edit-submit-btn' onclick='updateItem()' class='px-4 py-2 text-xs font-bold bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg shadow-sm transition'>変更を保存</button>
            </div>
        </div>
    </div>

    <!-- 設定・プリセット・テンプレート管理モーダル -->
    <div id='settings-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeSettingsModal()'>
        <div class='bg-white rounded-xl max-w-2xl w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-slate-800 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-gear text-indigo-300 text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>設定・プリセット・テンプレート管理 (双方向同期)</h3>
                </div>
                <button onclick='closeSettingsModal()' class='text-slate-300 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-5 text-xs max-h-[78vh] overflow-y-auto custom-scroll'>
                <!-- 1. デフォルトプリセット管理 -->
                <div class='space-y-3'>
                    <div class='flex items-center justify-between border-b border-slate-200 pb-1.5'>
                        <h4 class='font-bold text-slate-800 text-sm flex items-center gap-1.5'>
                            <i class='fa-solid fa-coins text-amber-500'></i> 基本費用・デフォルトプリセット管理
                        </h4>
                        <span class='text-[10px] text-slate-500'>変更は即時保存・スマホと同期されます</span>
                    </div>
                    <div id='settings-presets-list' class='space-y-2'></div>
                    <div class='bg-slate-50 border border-slate-200 rounded-lg p-3 space-y-2'>
                        <p id='setting-preset-form-title' class='font-bold text-slate-700 text-[11px]'>＋ 新しいプリセットを追加</p>
                        <input type='hidden' id='setting-preset-edit-id' value=''>
                        <div class='grid grid-cols-1 sm:grid-cols-4 gap-2'>
                            <input type='text' id='setting-preset-name' placeholder='プリセット名 (例: メルカリ大型)' class='sm:col-span-4 p-1.5 border border-slate-300 rounded bg-white'>
                            <div>
                                <label class='block text-[10px] text-slate-500 mb-0.5'>手数料 (%)</label>
                                <input type='number' min='0' id='setting-preset-fee' value='10' class='w-full p-1.5 border border-slate-300 rounded bg-white'>
                            </div>
                            <div>
                                <label class='block text-[10px] text-slate-500 mb-0.5'>送料 (円)</label>
                                <input type='number' min='0' id='setting-preset-ship' value='210' class='w-full p-1.5 border border-slate-300 rounded bg-white'>
                            </div>
                            <div>
                                <label class='block text-[10px] text-slate-500 mb-0.5'>経費 (円)</label>
                                <input type='number' min='0' id='setting-preset-exp' value='55' class='w-full p-1.5 border border-slate-300 rounded bg-white'>
                            </div>
                            <div class='flex items-end gap-1'>
                                <button type='button' onclick='savePresetFromForm()' class='flex-1 bg-indigo-600 hover:bg-indigo-700 text-white font-bold py-1.5 px-2 rounded transition'>保存</button>
                                <button type='button' onclick='resetPresetForm()' class='bg-slate-200 hover:bg-slate-300 text-slate-700 font-bold py-1.5 px-2 rounded transition'>クリア</button>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 2. タイトル用テンプレート -->
                <div class='space-y-2.5'>
                    <h4 class='font-bold text-slate-800 text-sm border-b border-slate-200 pb-1.5 flex items-center gap-1.5'>
                        <i class='fa-solid fa-heading text-indigo-500'></i> タイトル用テンプレート
                    </h4>
                    <div id='settings-title-tpl-list' class='space-y-1.5'></div>
                    <div class='bg-slate-50 border border-slate-200 rounded-lg p-3 space-y-2'>
                        <p id='setting-title-tpl-form-title' class='font-bold text-slate-700 text-[11px]'>＋ タイトル定型文を追加</p>
                        <input type='hidden' id='setting-title-tpl-edit-id' value=''>
                        <div class='grid grid-cols-1 sm:grid-cols-2 gap-2'>
                            <input type='text' id='setting-title-tpl-name' placeholder='表示名 (例: 美品タグ)' class='p-1.5 border border-slate-300 rounded bg-white'>
                            <input type='text' id='setting-title-tpl-content' placeholder='挿入文字列 (例: 【美品】)' class='p-1.5 border border-slate-300 rounded bg-white'>
                        </div>
                        <div class='flex gap-2 justify-end'>
                            <button type='button' onclick='resetTemplateForm(""title"")' class='bg-slate-200 hover:bg-slate-300 text-slate-700 font-bold py-1 px-3 rounded transition'>クリア</button>
                            <button type='button' onclick='saveTemplateFromForm(""title"")' class='bg-indigo-600 hover:bg-indigo-700 text-white font-bold py-1 px-3 rounded transition'>保存</button>
                        </div>
                    </div>
                </div>

                <!-- 3. 商品説明用テンプレート -->
                <div class='space-y-2.5'>
                    <h4 class='font-bold text-slate-800 text-sm border-b border-slate-200 pb-1.5 flex items-center gap-1.5'>
                        <i class='fa-solid fa-align-left text-emerald-600'></i> 商品説明用テンプレート
                    </h4>
                    <div id='settings-desc-tpl-list' class='space-y-1.5'></div>
                    <div class='bg-slate-50 border border-slate-200 rounded-lg p-3 space-y-2'>
                        <p id='setting-desc-tpl-form-title' class='font-bold text-slate-700 text-[11px]'>＋ 説明文定型文を追加</p>
                        <input type='hidden' id='setting-desc-tpl-edit-id' value=''>
                        <input type='text' id='setting-desc-tpl-name' placeholder='表示名 (例: 発送・ペットなし案内)' class='w-full p-1.5 border border-slate-300 rounded bg-white'>
                        <textarea id='setting-desc-tpl-content' rows='3' placeholder='挿入する説明文本文...' class='w-full p-1.5 border border-slate-300 rounded bg-white'></textarea>
                        <div class='flex gap-2 justify-end'>
                            <button type='button' onclick='resetTemplateForm(""desc"")' class='bg-slate-200 hover:bg-slate-300 text-slate-700 font-bold py-1 px-3 rounded transition'>クリア</button>
                            <button type='button' onclick='saveTemplateFromForm(""desc"")' class='bg-emerald-600 hover:bg-emerald-700 text-white font-bold py-1 px-3 rounded transition'>保存</button>
                        </div>
                    </div>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end'>
                <button onclick='closeSettingsModal()' class='px-4 py-2 text-xs font-bold bg-slate-700 hover:bg-slate-800 text-white rounded-lg transition'>閉じる</button>
            </div>
        </div>
    </div>

    <!-- メインスクリプト -->
    <script>
        const AUTH_TOKEN = '"" + authToken + @""';
        let allItems = [];
        let currentFilter = 'ALL';
        let selectedItemIds = new Set();
        let lastKnownDataRevision = null;
        let lastKnownSettingsRevision = null;
        let currentViewerItemId = null;
        let currentViewerTab = 'group';
        let pendingAddPhotoFiles = [];
        let pcSettings = {
            defaultFeeRatePercent: 5,
            defaultShippingCost: 210,
            defaultExpenseCost: 55,
            activePresetId: 'yahoo_furima',
            presets: [
                { id: 'yahoo_furima', name: 'Yahoo!フリマ (基本)', feeRatePercent: 5, shippingCost: 210, expenseCost: 55 },
                { id: 'mercari', name: 'メルカリ (ネコポス/ゆうパケット)', feeRatePercent: 10, shippingCost: 210, expenseCost: 55 },
                { id: 'rakuma', name: '楽天ラクマ (かんたんラクマパック)', feeRatePercent: 10, shippingCost: 180, expenseCost: 55 },
                { id: 'yupaket_mini', name: 'ゆうパケットポストmini', feeRatePercent: 5, shippingCost: 160, expenseCost: 20 }
            ],
            titleTemplates: [],
            descriptionTemplates: [],
            settingsUpdatedAt: 0
        };

        function parseNonNegInt(val, fallback) {
            if (val === '' || val === null || val === undefined) return fallback;
            const n = parseInt(val, 10);
            return (!isNaN(n) && n >= 0) ? n : fallback;
        }

        function getHeaders() {
            const h = { 'Content-Type': 'application/json; charset=utf-8' };
            if (AUTH_TOKEN) {
                h['Authorization'] = 'Bearer ' + AUTH_TOKEN;
            }
            return h;
        }

        function showToast(msg) {
            const toast = document.getElementById('toast');
            document.getElementById('toast-msg').innerText = msg;
            toast.classList.remove('translate-y-20', 'opacity-0');
            toast.classList.add('translate-y-0', 'opacity-100');
            setTimeout(() => {
                toast.classList.remove('translate-y-0', 'opacity-100');
                toast.classList.add('translate-y-20', 'opacity-0');
            }, 2500);
        }

        async function copyText(text, label) {
            try {
                await navigator.clipboard.writeText(text);
                showToast(label + 'をコピーしました！');
            } catch (err) {
                const ta = document.createElement('textarea');
                ta.value = text;
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                document.body.removeChild(ta);
                showToast(label + 'をコピーしました！');
            }
        }

        async function copyPhotoBlob(url) {
            try {
                const res = await fetch(url);
                const blob = await res.blob();
                if (navigator.clipboard && navigator.clipboard.write) {
                    let pngBlob = blob;
                    if (blob.type !== 'image/png') {
                        pngBlob = await new Promise((resolve, reject) => {
                            const img = new Image();
                            const objUrl = URL.createObjectURL(blob);
                            img.onload = () => {
                                try {
                                    const canvas = document.createElement('canvas');
                                    canvas.width = img.naturalWidth || img.width;
                                    canvas.height = img.naturalHeight || img.height;
                                    const ctx = canvas.getContext('2d');
                                    ctx.drawImage(img, 0, 0);
                                    canvas.toBlob((outBlob) => {
                                        URL.revokeObjectURL(objUrl);
                                        if (outBlob) resolve(outBlob);
                                        else reject(new Error('toBlob failed'));
                                    }, 'image/png');
                                } catch (err) {
                                    URL.revokeObjectURL(objUrl);
                                    reject(err);
                                }
                            };
                            img.onerror = (err) => {
                                URL.revokeObjectURL(objUrl);
                                reject(err);
                            };
                            img.src = objUrl;
                        });
                    }
                    await navigator.clipboard.write([
                        new ClipboardItem({ 'image/png': pngBlob })
                    ]);
                    showToast('画像をクリップボードにコピーしました！');
                    return;
                }
            } catch (e) {
                console.log('ClipboardItem copy error:', e);
            }

            // フォールバック: 別タブで開く
            window.open(url, '_blank');
            showToast('画像を別タブで開きました (右クリックでコピー可能)');
        }

        function checkTitleLength(prefix) {
            const input = document.getElementById(prefix + '-title');
            const counter = document.getElementById(prefix + '-title-count');
            const len = (input.value || '').length;
            counter.innerText = len + '/40文字';
            if (len > 40) {
                counter.className = 'text-[10px] font-mono text-rose-600 font-bold';
            } else {
                counter.className = 'text-[10px] font-mono text-slate-400';
            }
            if (prefix === 'add' || prefix === 'edit') updateModalValidation(prefix);
        }

        function openYahooSplit() {
            window.open('https://paypayfleamarket.yahoo.co.jp/sell', '_blank');
            showToast('Yahoo!フリマを通常タブで開きました（左右分割する場合はタブを別ウィンドウに出して Win + → で配置できます）');
        }

        async function openFolder(id) {
            try {
                const res = await fetch('/api/items/' + id + '/open-folder', {
                    method: 'POST',
                    headers: getHeaders()
                });
                if (res.ok) {
                    showToast('PC写真フォルダを開きました');
                } else {
                    showToast('フォルダ起動に失敗しました (Status: ' + res.status + ')');
                }
            } catch (e) {
                showToast('通信エラー: ' + e.message);
            }
        }

        document.addEventListener('DOMContentLoaded', () => {
            fetchSettings();
            fetchItems();
            setInterval(pollServerRevisions, 3000);
            document.addEventListener('visibilitychange', () => {
                if (!document.hidden) {
                    pollServerRevisions();
                }
            });

            // ドラッグ＆ドロップ写真アップロードの初期化 (ギャラリー・新規追加・編集モーダル)
            setupDropZone();
            setupModalPhotoDropZone('add-photo-drop-zone', addPendingPhotos);
            setupModalPhotoDropZone('edit-photo-drop-zone', uploadEditPhotoFiles);
        });

        async function fetchSettings() {
            try {
                const res = await fetch('/api/settings', { headers: getHeaders() });
                if (!res.ok) return;
                const data = await res.json();
                if (data) {
                    pcSettings = {
                        defaultFeeRatePercent: parseNonNegInt(data.defaultFeeRatePercent, 5),
                        defaultShippingCost: parseNonNegInt(data.defaultShippingCost, 210),
                        defaultExpenseCost: parseNonNegInt(data.defaultExpenseCost, 55),
                        activePresetId: data.activePresetId || 'yahoo_furima',
                        presets: (Array.isArray(data.presets) && data.presets.length > 0) ? data.presets : pcSettings.presets,
                        titleTemplates: Array.isArray(data.titleTemplates) ? data.titleTemplates : [],
                        descriptionTemplates: Array.isArray(data.descriptionTemplates) ? data.descriptionTemplates : [],
                        settingsUpdatedAt: data.settingsUpdatedAt || 0
                    };
                    renderTemplateSelects();
                }
            } catch (e) {}
        }

        function insertTextAtCursor(elementId, text) {
            const el = document.getElementById(elementId);
            if (!el || !text) return;
            const val = el.value || '';
            const start = (typeof el.selectionStart === 'number') ? el.selectionStart : val.length;
            const end = (typeof el.selectionEnd === 'number') ? el.selectionEnd : val.length;
            el.value = val.slice(0, start) + text + val.slice(end);
            const newPos = start + text.length;
            el.focus();
            try { el.setSelectionRange(newPos, newPos); } catch (e) {}
        }

        function applyTemplateSelect(elementId, selectElem, isTitle) {
            const idx = selectElem.selectedIndex;
            if (idx <= 0) return;
            const list = isTitle ? (pcSettings.titleTemplates || []) : (pcSettings.descriptionTemplates || []);
            const tpl = list[idx - 1];
            if (tpl && tpl.content) {
                insertTextAtCursor(elementId, tpl.content);
                if (isTitle) {
                    const prefix = elementId.split('-')[0];
                    checkTitleLength(prefix);
                }
            }
            selectElem.selectedIndex = 0;
        }

        function renderTemplateSelects() {
            const titleTpls = pcSettings.titleTemplates || [];
            const descTpls = pcSettings.descriptionTemplates || [];
            ['add-title-tpl-select', 'edit-title-tpl-select', 'merge-title-tpl-select'].forEach(id => {
                const sel = document.getElementById(id);
                if (!sel) return;
                sel.innerHTML = `<option value=''>タイトル定型文 (${titleTpls.length}件)...</option>` +
                    titleTpls.map((t, i) => `<option value='${i}'>${escapeHtml(t.name)}</option>`).join('');
            });
            ['add-desc-tpl-select', 'edit-desc-tpl-select', 'merge-desc-tpl-select'].forEach(id => {
                const sel = document.getElementById(id);
                if (!sel) return;
                sel.innerHTML = `<option value=''>説明定型文 (${descTpls.length}件)...</option>` +
                    descTpls.map((t, i) => `<option value='${i}'>${escapeHtml(t.name)}</option>`).join('');
            });
        }

        async function savePcSettingsPayload(nextSettings, toastMsg) {
            try {
                const res = await fetch('/api/settings', {
                    method: 'PUT',
                    headers: getHeaders(),
                    body: JSON.stringify(nextSettings)
                });
                if (!res.ok) {
                    alert('設定の保存に失敗しました (Status: ' + res.status + ')');
                    return false;
                }
                const saved = await res.json();
                pcSettings = {
                    defaultFeeRatePercent: parseNonNegInt(saved.defaultFeeRatePercent, nextSettings.defaultFeeRatePercent),
                    defaultShippingCost: parseNonNegInt(saved.defaultShippingCost, nextSettings.defaultShippingCost),
                    defaultExpenseCost: parseNonNegInt(saved.defaultExpenseCost, nextSettings.defaultExpenseCost),
                    activePresetId: saved.activePresetId || nextSettings.activePresetId,
                    presets: (Array.isArray(saved.presets) && saved.presets.length > 0) ? saved.presets : nextSettings.presets,
                    titleTemplates: Array.isArray(saved.titleTemplates) ? saved.titleTemplates : [],
                    descriptionTemplates: Array.isArray(saved.descriptionTemplates) ? saved.descriptionTemplates : [],
                    settingsUpdatedAt: saved.settingsUpdatedAt || Date.now()
                };
                renderSettingsModalLists();
                renderTemplateSelects();
                updateStats();
                renderItems();
                if (toastMsg) showToast(toastMsg);
                return true;
            } catch (e) {
                alert('設定保存エラー: ' + e.message);
                return false;
            }
        }

        async function openSettingsModal() {
            await fetchSettings();
            resetPresetForm();
            resetTemplateForm('title');
            resetTemplateForm('desc');
            renderSettingsModalLists();
            document.getElementById('settings-modal').classList.remove('hidden');
        }

        function closeSettingsModal() {
            document.getElementById('settings-modal').classList.add('hidden');
        }

        function renderSettingsModalLists() {
            const pContainer = document.getElementById('settings-presets-list');
            if (pContainer) {
                const presets = pcSettings.presets || [];
                pContainer.innerHTML = presets.map((p, idx) => {
                    const isDef = p.id === pcSettings.activePresetId;
                    return `<div class='flex items-center justify-between gap-2 p-2.5 rounded-lg border ${isDef ? 'bg-indigo-50 border-indigo-400' : 'bg-white border-slate-200'}'>
                        <div class='min-w-0 flex-1'>
                            <div class='flex items-center gap-1.5'>
                                ${isDef ? `<span class='bg-indigo-600 text-white text-[10px] font-bold px-1.5 py-0.5 rounded'>★ デフォルト</span>` : ''}
                                <span class='font-bold text-slate-800 truncate'>${escapeHtml(p.name)}</span>
                            </div>
                            <p class='text-[11px] text-slate-500 mt-0.5'>手数料 ${p.feeRatePercent}% / 送料 ¥${p.shippingCost} / 経費 ¥${p.expenseCost}</p>
                        </div>
                        <div class='flex items-center gap-1 shrink-0'>
                            ${!isDef ? `<button type='button' onclick='setDefaultPresetByIndex(${idx})' class='px-2 py-1 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 border border-indigo-200 rounded text-[11px] font-bold transition'>標準にする</button>` : ''}
                            <button type='button' onclick='editPresetByIndex(${idx})' class='px-2 py-1 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded text-[11px] font-bold transition' title='編集'><i class='fa-solid fa-pen'></i></button>
                            ${presets.length > 1 ? `<button type='button' onclick='deletePresetByIndex(${idx})' class='px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-600 rounded text-[11px] font-bold transition' title='削除'><i class='fa-solid fa-trash'></i></button>` : ''}
                        </div>
                    </div>`;
                }).join('');
            }

            renderTemplateListHtml('settings-title-tpl-list', pcSettings.titleTemplates || [], 'title');
            renderTemplateListHtml('settings-desc-tpl-list', pcSettings.descriptionTemplates || [], 'desc');
        }

        function renderTemplateListHtml(containerId, list, category) {
            const el = document.getElementById(containerId);
            if (!el) return;
            if (!list || list.length === 0) {
                el.innerHTML = `<p class='text-[11px] text-slate-400'>※ テンプレートはまだ登録されていません</p>`;
                return;
            }
            el.innerHTML = list.map((t, idx) => `
                <div class='flex items-center justify-between gap-2 p-2 rounded-lg border border-slate-200 bg-white'>
                    <div class='min-w-0 flex-1'>
                        <p class='font-bold text-slate-800 truncate'>${escapeHtml(t.name)}</p>
                        <p class='text-[11px] text-slate-500 truncate'>${escapeHtml((t.content || '').replace(/\n/g, ' '))}</p>
                    </div>
                    <div class='flex items-center gap-1 shrink-0'>
                        <button type='button' onclick='editTemplateByIndex(""${category}"", ${idx})' class='px-2 py-1 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded text-[11px] font-bold transition'><i class='fa-solid fa-pen'></i></button>
                        <button type='button' onclick='deleteTemplateByIndex(""${category}"", ${idx})' class='px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-600 rounded text-[11px] font-bold transition'><i class='fa-solid fa-trash'></i></button>
                    </div>
                </div>
            `).join('');
        }

        async function setDefaultPresetByIndex(idx) {
            const p = (pcSettings.presets || [])[idx];
            if (!p) return;
            const next = {
                ...pcSettings,
                activePresetId: p.id,
                defaultFeeRatePercent: p.feeRatePercent,
                defaultShippingCost: p.shippingCost,
                defaultExpenseCost: p.expenseCost
            };
            await savePcSettingsPayload(next, '「' + p.name + '」をデフォルトに設定しました');
        }

        function editPresetByIndex(idx) {
            const p = (pcSettings.presets || [])[idx];
            if (!p) return;
            document.getElementById('setting-preset-edit-id').value = p.id;
            document.getElementById('setting-preset-name').value = p.name;
            document.getElementById('setting-preset-fee').value = p.feeRatePercent;
            document.getElementById('setting-preset-ship').value = p.shippingCost;
            document.getElementById('setting-preset-exp').value = p.expenseCost;
            document.getElementById('setting-preset-form-title').innerText = '✎ プリセットを編集中';
        }

        function resetPresetForm() {
            document.getElementById('setting-preset-edit-id').value = '';
            document.getElementById('setting-preset-name').value = '';
            document.getElementById('setting-preset-fee').value = '10';
            document.getElementById('setting-preset-ship').value = '210';
            document.getElementById('setting-preset-exp').value = '55';
            document.getElementById('setting-preset-form-title').innerText = '＋ 新しいプリセットを追加';
        }

        async function savePresetFromForm() {
            const editId = document.getElementById('setting-preset-edit-id').value.trim();
            const name = document.getElementById('setting-preset-name').value.trim();
            const fee = parseNonNegInt(document.getElementById('setting-preset-fee').value, 5);
            const ship = parseNonNegInt(document.getElementById('setting-preset-ship').value, 210);
            const exp = parseNonNegInt(document.getElementById('setting-preset-exp').value, 55);
            if (!name) {
                alert('プリセット名を入力してください');
                return;
            }
            const nextPresets = (pcSettings.presets || []).map(p => ({ ...p }));
            let nextActiveId = pcSettings.activePresetId;
            let defFee = pcSettings.defaultFeeRatePercent;
            let defShip = pcSettings.defaultShippingCost;
            let defExp = pcSettings.defaultExpenseCost;

            if (editId) {
                const idx = nextPresets.findIndex(p => p.id === editId);
                if (idx >= 0) {
                    nextPresets[idx] = { id: editId, name, feeRatePercent: fee, shippingCost: ship, expenseCost: exp };
                }
                if (nextActiveId === editId) {
                    defFee = fee;
                    defShip = ship;
                    defExp = exp;
                }
            } else {
                const newId = 'custom_' + Date.now();
                nextPresets.push({ id: newId, name, feeRatePercent: fee, shippingCost: ship, expenseCost: exp });
                nextActiveId = newId;
                defFee = fee;
                defShip = ship;
                defExp = exp;
            }
            const ok = await savePcSettingsPayload({
                ...pcSettings,
                activePresetId: nextActiveId,
                defaultFeeRatePercent: defFee,
                defaultShippingCost: defShip,
                defaultExpenseCost: defExp,
                presets: nextPresets
            }, editId ? 'プリセットを更新しました' : 'プリセットを追加しデフォルトに設定しました');
            if (ok) resetPresetForm();
        }

        async function deletePresetByIndex(idx) {
            const presets = pcSettings.presets || [];
            if (presets.length <= 1) {
                alert('最低1件のプリセットを残す必要があります');
                return;
            }
            const target = presets[idx];
            if (!target || !confirm('プリセット「' + target.name + '」を削除しますか？')) return;
            const nextPresets = presets.filter((_, i) => i !== idx);
            let nextActiveId = pcSettings.activePresetId;
            let defFee = pcSettings.defaultFeeRatePercent;
            let defShip = pcSettings.defaultShippingCost;
            let defExp = pcSettings.defaultExpenseCost;
            if (target.id === nextActiveId) {
                nextActiveId = nextPresets[0].id;
                defFee = nextPresets[0].feeRatePercent;
                defShip = nextPresets[0].shippingCost;
                defExp = nextPresets[0].expenseCost;
            }
            await savePcSettingsPayload({
                ...pcSettings,
                activePresetId: nextActiveId,
                defaultFeeRatePercent: defFee,
                defaultShippingCost: defShip,
                defaultExpenseCost: defExp,
                presets: nextPresets
            }, 'プリセットを削除しました');
        }

        function editTemplateByIndex(category, idx) {
            const list = category === 'title' ? (pcSettings.titleTemplates || []) : (pcSettings.descriptionTemplates || []);
            const tpl = list[idx];
            if (!tpl) return;
            document.getElementById('setting-' + category + '-tpl-edit-id').value = tpl.id;
            document.getElementById('setting-' + category + '-tpl-name').value = tpl.name;
            document.getElementById('setting-' + category + '-tpl-content').value = tpl.content;
            document.getElementById('setting-' + category + '-tpl-form-title').innerText = '✎ 定型文を編集中';
        }

        function resetTemplateForm(category) {
            document.getElementById('setting-' + category + '-tpl-edit-id').value = '';
            document.getElementById('setting-' + category + '-tpl-name').value = '';
            document.getElementById('setting-' + category + '-tpl-content').value = '';
            document.getElementById('setting-' + category + '-tpl-form-title').innerText = category === 'title' ? '＋ タイトル定型文を追加' : '＋ 説明文定型文を追加';
        }

        async function saveTemplateFromForm(category) {
            const editId = document.getElementById('setting-' + category + '-tpl-edit-id').value.trim();
            const name = document.getElementById('setting-' + category + '-tpl-name').value.trim();
            const content = document.getElementById('setting-' + category + '-tpl-content').value;
            if (!name || !content) {
                alert('表示名と挿入テキストを入力してください');
                return;
            }
            const currentList = category === 'title' ? (pcSettings.titleTemplates || []) : (pcSettings.descriptionTemplates || []);
            const nextList = currentList.map(t => ({ ...t }));
            if (editId) {
                const idx = nextList.findIndex(t => t.id === editId);
                if (idx >= 0) nextList[idx] = { id: editId, name, content };
            } else {
                nextList.push({ id: 'tpl_' + Date.now(), name, content });
            }
            const next = {
                ...pcSettings,
                titleTemplates: category === 'title' ? nextList : (pcSettings.titleTemplates || []),
                descriptionTemplates: category === 'desc' ? nextList : (pcSettings.descriptionTemplates || [])
            };
            const ok = await savePcSettingsPayload(next, 'テンプレートを保存しました');
            if (ok) resetTemplateForm(category);
        }

        async function deleteTemplateByIndex(category, idx) {
            const currentList = category === 'title' ? (pcSettings.titleTemplates || []) : (pcSettings.descriptionTemplates || []);
            const target = currentList[idx];
            if (!target || !confirm('テンプレート「' + target.name + '」を削除しますか？')) return;
            const nextList = currentList.filter((_, i) => i !== idx);
            const next = {
                ...pcSettings,
                titleTemplates: category === 'title' ? nextList : (pcSettings.titleTemplates || []),
                descriptionTemplates: category === 'desc' ? nextList : (pcSettings.descriptionTemplates || [])
            };
            await savePcSettingsPayload(next, 'テンプレートを削除しました');
        }

        function updateStatusBannerFromData(statusData) {
            if (!statusData) return;
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
                statusText.innerText = 'スマホアプリ未接続（PC単独で編集可／スマホ再接続時に自動同期）' + missingBadge;
            }
        }

        async function fetchItemsOnly() {
            try {
                const res = await fetch('/api/items', { headers: getHeaders() });
                if (!res.ok) return;
                allItems = await res.json();
                updateStats();
                renderItems();
            } catch (e) {}
        }

        async function pollServerRevisions() {
            if (document.hidden) return;
            try {
                const statusRes = await fetch('/api/server/status', { headers: getHeaders() });
                if (!statusRes.ok) return;
                const statusData = await statusRes.json();
                updateStatusBannerFromData(statusData);

                const srvDataRev = typeof statusData.dataRevision === 'number' ? statusData.dataRevision : null;
                const srvSettingsRev = typeof statusData.settingsRevision === 'number' ? statusData.settingsRevision : null;

                if (srvSettingsRev !== null && srvSettingsRev !== lastKnownSettingsRevision) {
                    lastKnownSettingsRevision = srvSettingsRev;
                    fetchSettings();
                }
                if (srvDataRev === null || srvDataRev !== lastKnownDataRevision) {
                    lastKnownDataRevision = srvDataRev;
                    await fetchItemsOnly();
                    fetchCleanupPreview();
                }
            } catch (e) {}
        }

        async function fetchItems() {
            fetchSettings();
            await fetchItemsOnly();

            try {
                const statusRes = await fetch('/api/server/status', { headers: getHeaders() });
                if (statusRes.ok) {
                    const statusData = await statusRes.json();
                    updateStatusBannerFromData(statusData);
                    if (typeof statusData.dataRevision === 'number') lastKnownDataRevision = statusData.dataRevision;
                    if (typeof statusData.settingsRevision === 'number') lastKnownSettingsRevision = statusData.settingsRevision;
                }
            } catch (e) {}

            fetchCleanupPreview();
        }

        function isItemTrashed(item) {
            return !!(item && (item.isDeleted || (item.deletedAt != null && Number(item.deletedAt) > 0)));
        }

        function getTrashRemainingDays(item) {
            const deletedAt = Number(item && item.deletedAt ? item.deletedAt : 0);
            if (deletedAt <= 0) return 30;
            const dayMs = 24 * 60 * 60 * 1000;
            const remainingMs = Math.max(0, (deletedAt + 30 * dayMs) - Date.now());
            return Math.max(0, Math.min(30, Math.ceil(remainingMs / dayMs)));
        }

        function updateStats() {
            // MERGED（まとめ内包済み）子商品とゴミ箱商品は通常有効件数から除外
            const active = allItems.filter(i => !isItemTrashed(i) && i.status !== 'MERGED' && !(i.mergedParentId && i.mergedParentId > 0));
            const trashed = allItems.filter(i => isItemTrashed(i) && !(i.status === 'MERGED' && i.mergedParentId && i.mergedParentId > 0));
            const listed = active.filter(i => i.status === 'LISTED');
            const unshared = active.filter(i => i.status === 'DRAFT');
            const sold = active.filter(i => i.status === 'SOLD');

            const totalSales = sold.reduce((sum, i) => sum + (i.soldPrice > 0 ? i.soldPrice : (i.price || 0)), 0);
            const totalProfit = sold.reduce((sum, i) => sum + (i.profit || 0), 0);

            let totalFee = 0;
            let totalShipping = 0;
            let totalExpense = 0;

            sold.forEach(i => {
                const sp = i.soldPrice > 0 ? i.soldPrice : (i.price || 0);
                const feeRate = i.feeRatePercent != null ? i.feeRatePercent : pcSettings.defaultFeeRatePercent;
                const shipping = i.shippingCost != null ? i.shippingCost : pcSettings.defaultShippingCost;
                const expense = i.expenseCost != null ? i.expenseCost : pcSettings.defaultExpenseCost;
                totalFee += Math.floor(sp * feeRate / 100);
                totalShipping += shipping;
                totalExpense += expense;
            });

            const margin = totalSales > 0 ? ((totalProfit / totalSales) * 100).toFixed(1) : '0.0';
            const avgPrice = sold.length > 0 ? Math.round(totalSales / sold.length) : 0;

            document.getElementById('stat-listed-count').innerText = listed.length + '品';
            document.getElementById('stat-sold-count').innerText = sold.length + '品';
            document.getElementById('stat-total-sales').innerText = '¥' + totalSales.toLocaleString();
            document.getElementById('stat-total-profit').innerText = '¥' + totalProfit.toLocaleString();
            document.getElementById('stat-profit-margin').innerText = margin + '%';
            document.getElementById('stat-avg-price').innerText = '¥' + avgPrice.toLocaleString();

            document.getElementById('stat-total-fee').innerText = '¥' + totalFee.toLocaleString();
            document.getElementById('stat-total-shipping').innerText = '¥' + totalShipping.toLocaleString();
            document.getElementById('stat-total-expense').innerText = '¥' + totalExpense.toLocaleString();

            document.getElementById('count-all').innerText = active.length;
            document.getElementById('count-unshared').innerText = unshared.length;
            document.getElementById('count-listed').innerText = listed.length;
            document.getElementById('count-sold').innerText = sold.length;
            const trashCountElem = document.getElementById('count-trash');
            if (trashCountElem) trashCountElem.innerText = trashed.length;
            const emptyTrashBtn = document.getElementById('empty-trash-btn');
            if (emptyTrashBtn) emptyTrashBtn.disabled = trashed.length === 0;
        }

        function setFilter(filter) {
            currentFilter = filter;
            document.querySelectorAll('.filter-btn').forEach(btn => {
                btn.className = 'filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition';
            });
            if (filter === 'ALL') document.getElementById('filter-all').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition';
            if (filter === 'UNSHARED') document.getElementById('filter-unshared').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-amber-700 shadow-sm transition';
            if (filter === 'LISTED') document.getElementById('filter-listed').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition';
            if (filter === 'SOLD') document.getElementById('filter-sold').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-emerald-700 shadow-sm transition';
            if (filter === 'TRASH') document.getElementById('filter-trash').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-rose-700 shadow-sm transition';

            const trashBanner = document.getElementById('trash-banner');
            if (trashBanner) {
                if (filter === 'TRASH') {
                    trashBanner.classList.remove('hidden');
                } else {
                    trashBanner.classList.add('hidden');
                }
            }
            if (filter === 'TRASH' && selectedItemIds.size > 0) {
                selectedItemIds.clear();
                updateSelectionBar();
            }
            renderItems();
        }

        function toggleSelect(id) {
            const item = allItems.find(i => i.id === id);
            if (item && (item.status === 'SOLD' || item.status === 'MERGED' || isItemTrashed(item))) {
                return;
            }
            if (selectedItemIds.has(id)) {
                selectedItemIds.delete(id);
            } else {
                selectedItemIds.add(id);
            }
            updateSelectionBar();
            renderItems();
        }

        function clearSelection() {
            selectedItemIds.clear();
            updateSelectionBar();
            renderItems();
        }

        function updateSelectionBar() {
            const bar = document.getElementById('merge-floating-bar');
            const countElem = document.getElementById('selected-count');
            if (selectedItemIds.size > 0) {
                const selectedItems = allItems.filter(i => selectedItemIds.has(i.id));
                const totalSubItems = selectedItems.reduce((s, i) => s + ((i.mergedChildCount && i.mergedChildCount > 0) ? i.mergedChildCount : 1), 0);
                countElem.innerText = totalSubItems;
                bar.classList.remove('hidden');
            } else {
                bar.classList.add('hidden');
            }
        }

        function checkIsBundle(item) {
            if (!item) return false;
            if (item.mergedChildCount && item.mergedChildCount > 0) return true;
            if (item.bundleLevel && item.bundleLevel > 0) return true;
            if (item.mergedChildTitles && item.mergedChildTitles.trim().length > 0) return true;
            const title = item.title || '';
            if (title.startsWith('【まとめ買い】') || title.startsWith('まとめ買いセット') || title.includes('まとめ買い') || title.includes('まとめセット')) return true;
            if (item.photos && item.photos.some(p => p.childItemId && p.childItemId > 0)) return true;
            return false;
        }

        function renderItems() {
            const grid = document.getElementById('items-grid');
            const emptyState = document.getElementById('empty-state');
            const query = document.getElementById('search-input').value.trim().toLowerCase();

            const isTrashView = currentFilter === 'TRASH';
            let items = allItems.filter(item => {
                const trashed = isItemTrashed(item);
                const itemWarnings = (!trashed && item.status !== 'SOLD' && item.status !== 'MERGED')
                    ? evaluateListingWarningsObj({
                        title: item.title || '',
                        description: item.description || '',
                        photoCount: Array.isArray(item.photos) ? item.photos.length : 0,
                        price: item.price || 0,
                        profit: (item.profit != null) ? item.profit : ((item.price || 0) - Math.floor((item.price || 0) * ((item.useCustomSettings && item.feeRatePercent != null) ? item.feeRatePercent : pcSettings.defaultFeeRatePercent) / 100) - ((item.useCustomSettings && item.shippingCost != null) ? item.shippingCost : pcSettings.defaultShippingCost) - ((item.useCustomSettings && item.expenseCost != null) ? item.expenseCost : pcSettings.defaultExpenseCost)),
                        status: item.status || 'LISTED'
                    })
                    : [];
                const itemWarningsCritical = itemWarnings.filter(w => w.critical);
                const itemWarningsCriticalCount = itemWarningsCritical.length;
                const itemWarningsTitle = itemWarningsCritical.map(w => w.text).join(' / ');
                if (isTrashView) {
                    if (!trashed) return false;
                    if (item.status === 'MERGED' && item.mergedParentId && item.mergedParentId > 0) return false;
                } else {
                    if (trashed) return false;
                    // まとめ内包済み(MERGED)子商品は個別カードとしては非表示（親まとめ商品内に集約表示）
                    if (item.status === 'MERGED' || (item.mergedParentId && item.mergedParentId > 0)) return false;
                    if (currentFilter === 'LISTED' && item.status !== 'LISTED') return false;
                    if (currentFilter === 'UNSHARED' && item.status !== 'DRAFT') return false;
                    if (currentFilter === 'SOLD' && item.status !== 'SOLD') return false;
                }

                if (query) {
                    const title = (item.title || '').toLowerCase();
                    const desc = (item.description || '').toLowerCase();
                    return title.includes(query) || desc.includes(query);
                }
                return true;
            });

            if (isTrashView) {
                items.sort((a, b) => {
                    const da = Number(a.deletedAt || a.updatedAt || 0);
                    const db = Number(b.deletedAt || b.updatedAt || 0);
                    if (db !== da) return db - da;
                    return (b.id || 0) - (a.id || 0);
                });
            } else {
                // 5段階優先度＋登録/変更の新しい順ソート
                const getItemSortPriority = (item) => {
                    const isB = checkIsBundle(item);
                    if (isB && item.status === 'LISTED') return 1;
                    if (isB && item.status === 'DRAFT') return 2;
                    if (!isB && item.status === 'LISTED') return 3;
                    if (!isB && item.status === 'DRAFT') return 4;
                    return 5;
                };
                items.sort((a, b) => {
                    const pa = getItemSortPriority(a);
                    const pb = getItemSortPriority(b);
                    if (pa !== pb) return pa - pb;
                    const ta = Math.max(a.updatedAt || 0, a.createdAt || 0);
                    const tb = Math.max(b.updatedAt || 0, b.createdAt || 0);
                    if (tb !== ta) return tb - ta;
                    return (b.id || 0) - (a.id || 0);
                });
            }

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
                const isDraft = item.status === 'DRAFT';
                const isBundle = checkIsBundle(item);
                const isMergedChild = item.status === 'MERGED';
                const trashed = isItemTrashed(item);
                const remainingDays = trashed ? getTrashRemainingDays(item) : 30;
                const profitText = item.profit != null ? ' (利益 ¥' + item.profit.toLocaleString() + ')' : '';
                const titleLen = (item.title || '').length;
                const isChecked = !trashed && selectedItemIds.has(item.id);

                return `<div class='bg-white rounded-lg border ${trashed ? 'border-rose-200 bg-rose-50/20' : (isChecked ? 'border-indigo-500 ring-2 ring-indigo-300' : 'border-slate-200')} overflow-hidden shadow-sm hover:shadow-md transition flex flex-col'>
                    <!-- サムネイル外の選択チェックボックスバー -->
                    <div class='px-2.5 py-1 bg-slate-50 border-b border-slate-100 flex items-center justify-between text-[11px]'>
                        <label class='flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 hover:text-indigo-600' onclick='event.stopPropagation()'>
                            <input type='checkbox' class='w-3.5 h-3.5 text-indigo-600 rounded border-slate-300 focus:ring-indigo-500 ${trashed || isSold || isMergedChild ? 'opacity-40 cursor-not-allowed' : ''}' onchange='toggleSelect(${item.id})' ${isChecked ? 'checked' : ''} ${trashed || isSold || isMergedChild ? 'disabled' : ''}>
                            <span class='font-mono text-slate-500'>#${item.id}</span>
                        </label>
                        <div class='flex items-center gap-1'>
                            ${trashed ? `<span class='bg-rose-100 text-rose-700 text-[10px] px-1.5 py-0.2 rounded-full font-bold'>完全削除まであと ${remainingDays}日</span>` : ''}
                            ${!trashed && !item.isShared ? `<span class='bg-slate-200 text-slate-700 text-[9px] px-1.5 py-0.2 rounded-full font-bold'>未同期</span>` : ''}
                            ${isBundle ? `<span class='bg-purple-100 text-purple-800 text-[10px] px-1.5 py-0.2 rounded-full font-bold flex items-center gap-1'><i class='fa-solid fa-gift'></i> まとめ (${item.mergedChildCount || 2}点)</span>` : ''}
                            ${isMergedChild ? `<span class='bg-purple-50 text-purple-600 text-[10px] px-1.5 py-0.2 rounded-full font-bold'>まとめ内包済</span>` : ''}
                            ${(!trashed && !isSold && !isMergedChild && itemWarningsCriticalCount > 0) ? `<span class='bg-orange-100 text-orange-800 text-[9px] px-1.5 py-0.2 rounded-full font-bold flex items-center gap-0.5' title='${escapeHtml(itemWarningsTitle)}'><i class='fa-solid fa-triangle-exclamation'></i> 要確認(${itemWarningsCriticalCount})</span>` : ''}
                        </div>
                    </div>

                    <!-- サムネイル画像 -->
                    <div class='relative aspect-square bg-slate-100 overflow-hidden cursor-pointer group' onclick='openPhotoViewer(${item.id})'>
                        ${mainPhoto ? `<img src='${mainPhoto}' draggable='true' ondragstart='handlePhotoDragStart(event, ""${mainPhoto}"")' class='w-full h-full object-cover group-hover:scale-105 transition duration-200 ${trashed ? 'opacity-80' : ''}'>` : `<div class='w-full h-full flex items-center justify-center text-slate-300'><i class='fa-solid fa-image text-3xl'></i></div>`}
                        <div class='absolute top-1.5 left-1.5 flex gap-1 flex-wrap'>
                            ${trashed
                                ? `<span class='px-2 py-0.5 rounded text-xs font-black shadow-sm bg-rose-700 text-white'>ゴミ箱</span>`
                                : `<span class='px-2 py-0.5 rounded text-xs font-black shadow-sm ${isSold ? 'bg-rose-600 text-white' : (isDraft ? 'bg-amber-500 text-white' : (isMergedChild ? 'bg-purple-600 text-white' : 'bg-emerald-600 text-white'))}'>
                                    ${isSold ? '売却済' : (isDraft ? '未出品' : (isMergedChild ? 'まとめ済' : '出品中'))}
                                   </span>`
                            }
                        </div>
                        ${photos.length > 0 ? `<div class='absolute bottom-1.5 right-1.5 bg-black/60 text-white text-[10px] font-bold px-1.5 py-0.5 rounded flex items-center gap-1'><i class='fa-solid fa-camera'></i> ${photos.length}</div>` : ''}
                    </div>

                    <div class='p-2.5 flex-1 flex flex-col justify-between space-y-2'>
                        <div>
                            <div class='flex items-center justify-between gap-1'>
                                <h3 class='font-bold text-xs text-slate-800 line-clamp-1 flex-1' title='${escapeHtml(item.title || '無題')}'>${escapeHtml(item.title || '無題')}</h3>
                                <span class='text-[9px] font-mono shrink-0 ${titleLen > 40 ? 'text-rose-600 font-bold' : 'text-slate-400'}'>${titleLen}/40字</span>
                            </div>
                            <p class='text-[11px] text-slate-500 line-clamp-2 mt-0.5 leading-tight'>${escapeHtml(item.description || '')}</p>
                        </div>

                        ${!trashed ? `
                        <!-- Yahoo!フリマ出品コピペ & 写真ビューアー支援バー -->
                        <div class='bg-slate-50 border border-slate-100 rounded p-1 flex items-center justify-between gap-1 text-[10px]'>
                            <button onclick='copyText(${JSON.stringify(item.title || '')}, ""タイトル"")' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='タイトルをコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 題名
                            </button>
                            <button onclick='copyText(${JSON.stringify(item.description || '')}, ""商品説明"")' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='商品説明をコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 説明
                            </button>
                            <button onclick='copyText(${JSON.stringify(displayPrice.toString())}, ""価格"")' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='価格をコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 価格
                            </button>
                            <button onclick='openPhotoViewer(${item.id})' class='bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 text-indigo-700 px-1.5 py-0.5 rounded font-bold transition flex items-center justify-center' title='写真ギャラリー / 出品画像ドラッグ'>
                                <i class='fa-solid fa-images'></i>
                            </button>
                        </div>` : ''}

                        <div class='border-t border-slate-100 pt-1.5 flex items-center justify-between gap-1'>
                            <div>
                                <span class='text-base font-black ${isSold ? 'text-rose-600' : 'text-indigo-600'}'>¥${displayPrice.toLocaleString()}</span>
                                ${profitText ? `<span class='text-[10px] font-bold text-emerald-600 block'>${profitText}</span>` : ''}
                            </div>
                            <div class='flex gap-1'>
                                ${trashed ? `
                                    <button onclick='restoreItem(${item.id})' class='px-2 py-1 bg-emerald-50 hover:bg-emerald-100 text-emerald-700 border border-emerald-200 rounded text-[10px] font-bold transition flex items-center gap-1' title='ゴミ箱から復元'><i class='fa-solid fa-rotate-left'></i> 復元</button>
                                    <button onclick='permanentDeleteItem(${item.id})' class='px-2 py-1 bg-rose-600 hover:bg-rose-700 text-white rounded text-[10px] font-bold transition flex items-center gap-1' title='完全削除'><i class='fa-solid fa-xmark'></i> 完全削除</button>
                                ` : `
                                    ${isBundle ? `<button onclick='unmergeItem(${item.id})' class='px-1.5 py-1 bg-purple-50 hover:bg-purple-100 text-purple-700 rounded text-[10px] font-bold transition flex items-center gap-1' title='まとめ解除 (個別商品に戻す)'><i class='fa-solid fa-box-open'></i> 解除</button>` : ''}
                                    <button onclick='openEditModal(${item.id})' class='px-2 py-1 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded text-[10px] font-bold transition' title='編集'><i class='fa-solid fa-pen'></i></button>
                                    <button onclick='deleteItem(${item.id})' class='px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-600 rounded text-[10px] font-bold transition' title='ゴミ箱へ移動'><i class='fa-solid fa-trash'></i></button>
                                `}
                            </div>
                        </div>
                    </div>
                </div>`;
            }).join('');
        }

        function escapeHtml(str) {
            return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/""/g, '&quot;');
        }

        function handlePhotoDragStart(e, url) {
            e.dataTransfer.setData('text/uri-list', url);
            e.dataTransfer.setData('text/plain', url);
        }

        function openPhotoViewer(itemId) {
            const item = allItems.find(i => i.id === itemId);
            if (!item) return;

            currentViewerItemId = itemId;
            currentViewerTab = 'group';

            document.getElementById('viewer-title').innerText = '写真ギャラリー: ' + (item.title || '無題');
            const isBundle = checkIsBundle(item);
            const tabsBar = document.getElementById('viewer-tabs-bar');

            if (isBundle) {
                tabsBar.classList.remove('hidden');
                document.getElementById('viewer-subtitle').innerText = 'まとめ商品 (' + (item.mergedChildCount || '複数') + '点同梱) - 写真の直接プレビュー・D&D・コピーが可能です';
            } else {
                tabsBar.classList.add('hidden');
                document.getElementById('viewer-subtitle').innerText = '商品写真のプレビュー・出品画面へのドラッグ＆ドロップ・コピーが可能です';
            }

            const folderBtn = document.getElementById('viewer-open-folder-btn');
            folderBtn.onclick = () => openFolder(itemId);

            renderViewerPhotos();
            document.getElementById('photo-viewer-modal').classList.remove('hidden');
        }

        function closePhotoViewer() {
            document.getElementById('photo-viewer-modal').classList.add('hidden');
            currentViewerItemId = null;
        }

        function switchViewerTab(tab) {
            currentViewerTab = tab;
            const btnGroup = document.getElementById('viewer-tab-group');
            const btnAll = document.getElementById('viewer-tab-all');
            if (tab === 'group') {
                btnGroup.className = 'px-3 py-1 rounded text-xs font-bold bg-white text-indigo-700 shadow-sm border border-slate-200 transition flex items-center gap-1';
                btnAll.className = 'px-3 py-1 rounded text-xs font-medium text-slate-600 hover:text-slate-800 transition flex items-center gap-1';
            } else {
                btnAll.className = 'px-3 py-1 rounded text-xs font-bold bg-white text-indigo-700 shadow-sm border border-slate-200 transition flex items-center gap-1';
                btnGroup.className = 'px-3 py-1 rounded text-xs font-medium text-slate-600 hover:text-slate-800 transition flex items-center gap-1';
            }
            renderViewerPhotos();
        }

        function renderViewerPhotos() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            if (!item) return;

            const container = document.getElementById('viewer-photos-container');
            const photos = item.photos || [];
            document.getElementById('viewer-photo-count').innerText = photos.length + '枚の写真';

            if (photos.length === 0) {
                container.innerHTML = '<div class=\'text-center py-8 text-slate-400 text-xs\'>登録されている写真がありません。上の枠から画像ファイルをドラッグ＆ドロップして追加してください。</div>';
                return;
            }

            const isBundle = checkIsBundle(item);

            // デフォルト: グループ分け表示 (まとめ商品の場合)
            if (isBundle && currentViewerTab === 'group') {
                const groups = {};
                photos.forEach((p, idx) => {
                    const childTitle = p.childItemTitle || ('商品 #' + (p.childItemId || 'まとめ'));
                    if (!groups[childTitle]) groups[childTitle] = [];
                    groups[childTitle].push({ photo: p, index: idx });
                });

                container.innerHTML = Object.keys(groups).map((groupTitle, gIdx) => {
                    const groupPhotos = groups[groupTitle];
                    return `<div class='bg-slate-50 border border-slate-200 rounded-xl p-3 space-y-2'>
                        <div class='flex items-center justify-between border-b border-slate-200 pb-1.5'>
                            <h4 class='text-xs font-bold text-slate-700 flex items-center gap-1.5'>
                                <span class='bg-purple-100 text-purple-800 text-[10px] px-1.5 py-0.5 rounded font-bold'>内訳 ${gIdx + 1}</span>
                                <span>${escapeHtml(groupTitle)}</span>
                            </h4>
                            <span class='text-[10px] text-slate-400 font-mono'>${groupPhotos.length}枚</span>
                        </div>
                        <div class='grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3'>
                            ${groupPhotos.map(itemObj => renderSinglePhotoCard(itemObj.photo, itemObj.index)).join('')}
                        </div>
                    </div>`;
                }).join('');
            } else {
                // 全写真一覧
                container.innerHTML = `<div class='grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3'>
                    ${photos.map((p, idx) => renderSinglePhotoCard(p, idx)).join('')}
                </div>`;
            }
        }

        function renderSinglePhotoCard(photo, index) {
            const url = photo.md5Hash ? '/api/photos/' + photo.md5Hash : photo.photoUrl;
            return `<div class='bg-white border border-slate-200 rounded-lg overflow-hidden shadow-sm flex flex-col justify-between'>
                <div class='relative aspect-square bg-slate-100 overflow-hidden cursor-grab group'>
                    <img src='${url}' draggable='true' ondragstart='handlePhotoDragStart(event, ""${url}"")' class='w-full h-full object-cover group-hover:scale-105 transition duration-200'>
                    <span class='absolute top-1 left-1 bg-black/60 text-white text-[9px] font-mono px-1 rounded'>#${index + 1}</span>
                </div>
                <div class='p-1.5 bg-slate-50 border-t border-slate-100 flex items-center gap-1 text-[10px]'>
                    <button onclick='copyPhotoBlob(""${url}"")' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-1 rounded font-bold transition flex items-center justify-center gap-1' title='クリップボードに画像をコピー'>
                        <i class='fa-regular fa-copy text-indigo-600'></i> 画像コピー
                    </button>
                    <a href='${url}' target='_blank' class='bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 text-indigo-700 px-2 py-1 rounded font-bold transition flex items-center justify-center' title='原寸表示'>
                        <i class='fa-solid fa-up-right-from-square'></i>
                    </a>
                </div>
            </div>`;
        }

        function setupDropZone() {
            const zone = document.getElementById('photo-drop-zone');
            const fileInput = document.getElementById('photo-drop-file-input');

            zone.onclick = () => fileInput.click();

            zone.addEventListener('dragover', (e) => {
                e.preventDefault();
                zone.classList.add('border-indigo-500', 'bg-indigo-100/60');
            });

            zone.addEventListener('dragleave', (e) => {
                e.preventDefault();
                zone.classList.remove('border-indigo-500', 'bg-indigo-100/60');
            });

            zone.addEventListener('drop', async (e) => {
                e.preventDefault();
                zone.classList.remove('border-indigo-500', 'bg-indigo-100/60');
                if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                    await uploadPhotoFiles(e.dataTransfer.files);
                }
            });
        }

        async function handleFileInputChange(e) {
            if (e.target.files && e.target.files.length > 0) {
                await uploadPhotoFiles(e.target.files);
                e.target.value = '';
            }
        }

        async function uploadPhotoFiles(fileList) {
            if (!currentViewerItemId) return;
            const files = Array.from(fileList).filter(f => /\.(jpg|jpeg|png|webp|gif|bmp)$/i.test(f.name));
            if (files.length === 0) {
                showToast('有効な画像ファイルを選択してください');
                return;
            }

            showToast(files.length + '枚の写真をアップロード中...');
            let uploaded = 0;

            for (const f of files) {
                try {
                    const buf = await f.arrayBuffer();
                    const res = await fetch('/api/items/' + currentViewerItemId + '/photos/add', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/octet-stream',
                            ...(AUTH_TOKEN ? { 'Authorization': 'Bearer ' + AUTH_TOKEN } : {})
                        },
                        body: buf
                    });
                    if (res.ok) uploaded++;
                } catch (e) {
                    console.log('Upload error:', e);
                }
            }

            showToast(uploaded + '枚の写真を追加しました！');
            await fetchItems();
            renderViewerPhotos();
        }

        async function openMergeModal() {
            await fetchSettings();
            if (selectedItemIds.size < 2) {
                alert('まとめ出品を作成するには、2点以上の商品を選択してください。');
                return;
            }

            const selectedItems = allItems.filter(i => selectedItemIds.has(i.id));
            const totalSubItems = selectedItems.reduce((s, i) => s + ((i.mergedChildCount && i.mergedChildCount > 0) ? i.mergedChildCount : 1), 0);
            const originalTotal = selectedItems.reduce((s, i) => s + (i.price || 0), 0);

            document.getElementById('merge-title').value = 'まとめ買いセット (' + totalSubItems + '点)';
            checkTitleLength('merge');

            document.getElementById('merge-price').value = originalTotal;
            document.getElementById('merge-shipping').value = pcSettings.defaultShippingCost;
            document.getElementById('merge-fee-rate').value = pcSettings.defaultFeeRatePercent;
            document.getElementById('merge-expense').value = pcSettings.defaultExpenseCost;

            const breakdown = selectedItems.map(i => '- ' + i.title + ' (¥' + (i.price || 0).toLocaleString() + ')').join('\n');
            document.getElementById('merge-description').value = '【まとめ買い対象商品 (' + totalSubItems + '点)】\n' + breakdown + '\n\n--- 商品説明 ---\nそれぞれの商品の状態は個別写真をご確認ください。個別でのご購入よりお安く設定しております。';

            renderTemplateSelects();
            updateMergeCalculations();
            document.getElementById('merge-modal').classList.remove('hidden');
        }

        function closeMergeModal() {
            document.getElementById('merge-modal').classList.add('hidden');
        }

        function updateMergeCalculations() {
            const selectedItems = allItems.filter(i => selectedItemIds.has(i.id));
            const originalTotal = selectedItems.reduce((s, i) => s + (i.price || 0), 0);
            const price = parseNonNegInt(document.getElementById('merge-price').value, 0);
            const shipping = parseNonNegInt(document.getElementById('merge-shipping').value, pcSettings.defaultShippingCost);
            const feeRate = parseNonNegInt(document.getElementById('merge-fee-rate').value, pcSettings.defaultFeeRatePercent);
            const expense = parseNonNegInt(document.getElementById('merge-expense').value, pcSettings.defaultExpenseCost);

            const discount = Math.max(0, originalTotal - price);
            document.getElementById('merge-discount-text').innerText = '元計:¥' + originalTotal.toLocaleString() + ' / 値引:¥' + discount.toLocaleString();

            const fee = Math.floor(price * feeRate / 100);
            const profit = Math.max(0, price - fee - shipping - expense);
            document.getElementById('merge-profit-display').innerText = '¥' + profit.toLocaleString();
        }

        async function submitMerge() {
            const title = document.getElementById('merge-title').value.trim();
            const price = parseNonNegInt(document.getElementById('merge-price').value, 0);
            const shipping = parseNonNegInt(document.getElementById('merge-shipping').value, pcSettings.defaultShippingCost);
            const feeRate = parseNonNegInt(document.getElementById('merge-fee-rate').value, pcSettings.defaultFeeRatePercent);
            const expense = parseNonNegInt(document.getElementById('merge-expense').value, pcSettings.defaultExpenseCost);
            const description = document.getElementById('merge-description').value.trim();

            if (!title) {
                alert('商品タイトルを入力してください。');
                return;
            }

            try {
                const res = await fetch('/api/items/merge', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({
                        childItemIds: Array.from(selectedItemIds),
                        mergedTitle: title,
                        mergedDescription: description,
                        totalSoldPrice: price,
                        newShippingCost: shipping,
                        newFeeRatePercent: feeRate,
                        newExpenseCost: expense,
                        asStatus: 'LISTED'
                    })
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        closeMergeModal();
                        clearSelection();
                        showToast('まとめ買い商品を作成しました！');
                        fetchItems();
                    } else {
                        alert('作成失敗: ' + (result.message || 'エラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        async function unmergeItem(id) {
            if (!confirm('このまとめ買い出品を解除（バラす）し、内包されている元の個別商品をすべて出品中に戻しますか？\n（※ 元の子商品のIDや写真情報はそのまま復元されます）')) return;

            try {
                const res = await fetch('/api/items/' + id + '/unmerge', {
                    method: 'POST',
                    headers: getHeaders()
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        showToast('まとめ買いを解除し、個別商品を復元しました！');
                        fetchItems();
                    } else {
                        alert('解除失敗: ' + (result.message || 'エラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        function setupModalPhotoDropZone(zoneId, onFilesDropped) {
            const zone = document.getElementById(zoneId);
            if (!zone) return;
            const inputId = zoneId === 'add-photo-drop-zone' ? 'add-photo-input' : 'edit-photo-input';
            zone.onclick = () => document.getElementById(inputId).click();
            zone.addEventListener('dragover', (e) => {
                e.preventDefault();
                zone.classList.add('ring-2', 'ring-indigo-400');
            });
            zone.addEventListener('dragleave', (e) => {
                e.preventDefault();
                zone.classList.remove('ring-2', 'ring-indigo-400');
            });
            zone.addEventListener('drop', async (e) => {
                e.preventDefault();
                zone.classList.remove('ring-2', 'ring-indigo-400');
                if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                    await onFilesDropped(e.dataTransfer.files);
                }
            });
        }

        function setModalStatus(prefix, status) {
            document.getElementById(prefix + '-status').value = status;
            ['DRAFT', 'LISTED', 'SOLD'].forEach(st => {
                const btn = document.getElementById(prefix + '-status-chip-' + st);
                if (!btn) return;
                if (st === status) {
                    const activeColor = st === 'SOLD'
                        ? 'bg-rose-600 text-white border-rose-600 shadow-sm'
                        : (st === 'DRAFT' ? 'bg-amber-500 text-white border-amber-500 shadow-sm' : 'bg-emerald-600 text-white border-emerald-600 shadow-sm');
                    btn.className = 'px-3 py-1.5 rounded-lg text-xs font-bold border transition ' + activeColor;
                } else {
                    btn.className = 'px-3 py-1.5 rounded-lg text-xs font-bold border border-slate-300 bg-white text-slate-600 hover:bg-slate-100 transition';
                }
            });
            const soldGroup = document.getElementById(prefix + '-sold-price-group');
            if (soldGroup) {
                soldGroup.className = (status === 'SOLD') ? 'block' : 'hidden';
            }
            calcModalProfit(prefix);
        }

        function toggleCustomSettings(prefix) {
            const useCustom = document.getElementById(prefix + '-use-custom').checked;
            const fields = document.getElementById(prefix + '-custom-settings-fields');
            if (fields) {
                if (useCustom) fields.classList.remove('hidden');
                else fields.classList.add('hidden');
            }
            updateCostQuickChipStates(prefix);
            renderPresetChips(prefix);
            calcModalProfit(prefix);
        }

        function renderPresetChips(prefix) {
            const container = document.getElementById(prefix + '-preset-chips');
            if (!container) return;
            const useCustom = document.getElementById(prefix + '-use-custom').checked;
            const curFee = parseNonNegInt(document.getElementById(prefix + '-fee-rate').value, pcSettings.defaultFeeRatePercent);
            const curShip = parseNonNegInt(document.getElementById(prefix + '-shipping').value, pcSettings.defaultShippingCost);
            const curExp = parseNonNegInt(document.getElementById(prefix + '-expense').value, pcSettings.defaultExpenseCost);

            const summaryElem = document.getElementById(prefix + '-default-summary');
            if (summaryElem) {
                summaryElem.innerText = useCustom
                    ? '個別設定を適用中'
                    : ('デフォルト設定 (手数料' + pcSettings.defaultFeeRatePercent + '% / 送料' + pcSettings.defaultShippingCost + '円 / 経費' + pcSettings.defaultExpenseCost + '円)');
            }

            container.innerHTML = (pcSettings.presets || []).map((p, idx) => {
                const isSelected = useCustom
                    ? (curFee === p.feeRatePercent && curShip === p.shippingCost && curExp === p.expenseCost)
                    : (p.id === pcSettings.activePresetId);
                const cls = isSelected
                    ? 'bg-indigo-600 text-white border-indigo-600 shadow-sm'
                    : 'bg-white text-slate-700 border-slate-300 hover:bg-slate-100';
                return `<button type='button' onclick='applyPresetByIndex(""${prefix}"", ${idx})' class='px-2.5 py-1 rounded-md text-[10px] font-bold border transition ${cls}'>${escapeHtml(p.name)} (${p.feeRatePercent}% / ¥${p.shippingCost})</button>`;
            }).join('');
        }

        function applyPresetByIndex(prefix, idx) {
            const preset = (pcSettings.presets || [])[idx];
            if (!preset) return;
            document.getElementById(prefix + '-use-custom').checked = true;
            document.getElementById(prefix + '-fee-rate').value = preset.feeRatePercent;
            document.getElementById(prefix + '-shipping').value = preset.shippingCost;
            document.getElementById(prefix + '-expense').value = preset.expenseCost;
            toggleCustomSettings(prefix);
        }

        function setQuickFee(prefix, fee) {
            document.getElementById(prefix + '-use-custom').checked = true;
            document.getElementById(prefix + '-fee-rate').value = fee;
            toggleCustomSettings(prefix);
        }

        function setQuickShipping(prefix, ship) {
            document.getElementById(prefix + '-use-custom').checked = true;
            document.getElementById(prefix + '-shipping').value = ship;
            toggleCustomSettings(prefix);
        }

        function setQuickExpense(prefix, exp) {
            document.getElementById(prefix + '-use-custom').checked = true;
            document.getElementById(prefix + '-expense').value = exp;
            toggleCustomSettings(prefix);
        }

        function onCostInput(prefix) {
            updateCostQuickChipStates(prefix);
            renderPresetChips(prefix);
            calcModalProfit(prefix);
        }

        function updateCostQuickChipStates(prefix) {
            const feeVal = parseNonNegInt(document.getElementById(prefix + '-fee-rate').value, -1);
            [5, 10].forEach(v => {
                const b = document.getElementById(prefix + '-fee-chip-' + v);
                if (!b) return;
                b.className = (feeVal === v)
                    ? 'px-2 py-0.5 rounded-md text-[10px] font-bold border border-indigo-600 bg-indigo-600 text-white transition'
                    : 'px-2 py-0.5 rounded-md text-[10px] font-bold border border-slate-300 bg-white hover:bg-slate-100 text-slate-700 transition';
            });

            const shipVal = parseNonNegInt(document.getElementById(prefix + '-shipping').value, -1);
            [0, 210, 450, 750].forEach(v => {
                const b = document.getElementById(prefix + '-ship-chip-' + v);
                if (!b) return;
                b.className = (shipVal === v)
                    ? 'px-2 py-0.5 rounded-md text-[10px] font-bold border border-indigo-600 bg-indigo-600 text-white transition'
                    : 'px-2 py-0.5 rounded-md text-[10px] font-bold border border-slate-300 bg-white hover:bg-slate-100 text-slate-700 transition';
            });
        }

        function calcModalProfit(prefix) {
            const status = document.getElementById(prefix + '-status').value;
            const listPrice = parseNonNegInt(document.getElementById(prefix + '-price').value, 0);
            const soldInputVal = document.getElementById(prefix + '-sold-price') ? document.getElementById(prefix + '-sold-price').value : '';
            const soldPrice = (status === 'SOLD' && soldInputVal !== '') ? parseNonNegInt(soldInputVal, listPrice) : 0;
            const basePrice = (status === 'SOLD' && soldPrice > 0) ? soldPrice : listPrice;

            const useCustom = document.getElementById(prefix + '-use-custom').checked;
            const shipping = useCustom
                ? parseNonNegInt(document.getElementById(prefix + '-shipping').value, pcSettings.defaultShippingCost)
                : pcSettings.defaultShippingCost;
            const feeRate = useCustom
                ? parseNonNegInt(document.getElementById(prefix + '-fee-rate').value, pcSettings.defaultFeeRatePercent)
                : pcSettings.defaultFeeRatePercent;
            const expense = useCustom
                ? parseNonNegInt(document.getElementById(prefix + '-expense').value, pcSettings.defaultExpenseCost)
                : pcSettings.defaultExpenseCost;

            const fee = Math.floor(basePrice * feeRate / 100);
            const profit = Math.max(0, basePrice - fee - shipping - expense);
            const margin = basePrice > 0 ? Math.round((profit / basePrice) * 100) : 0;

            const formulaElem = document.getElementById(prefix + '-profit-formula');
            if (formulaElem) {
                formulaElem.innerText = '売価 ¥' + basePrice.toLocaleString() + ' - 手数料(' + feeRate + '%) ¥' + fee.toLocaleString() + ' - 送料 ¥' + shipping.toLocaleString() + ' - 経費 ¥' + expense.toLocaleString();
            }
            const valueElem = document.getElementById(prefix + '-profit-value');
            if (valueElem) {
                valueElem.innerText = '純利益: ¥' + profit.toLocaleString() + ' (' + margin + '%)';
            }
            if (prefix === 'add' || prefix === 'edit') {
                updateModalValidation(prefix, basePrice - fee - shipping - expense);
            }
        }

        function evaluateListingWarningsObj({ title, description, photoCount, price, profit, status }) {
            if (status === 'SOLD') return [];
            const warnings = [];
            const t = (title || '').trim();
            if (!t) warnings.push({ critical: true, text: '商品名が未入力です' });
            else if (t.length > 40) warnings.push({ critical: true, text: '商品名が ' + t.length + '文字です（Yahoo!フリマ目安 40文字超過）' });
            if (photoCount === 0) warnings.push({ critical: true, text: '商品が未撮影です（写真 0枚）' });
            else if (photoCount > 10) warnings.push({ critical: true, text: '写真が ' + photoCount + '枚あります（上限 10枚超過）' });
            if (price <= 0) warnings.push({ critical: true, text: '販売価格が未設定（¥0）です' });
            else if (price < 300) warnings.push({ critical: false, text: '販売価格 ¥' + price + ' は最低出品額（¥300）未満です' });
            if (price > 0 && profit <= 0) warnings.push({ critical: true, text: '見込純利益が ¥' + profit + '（利益なし／赤字）です' });
            if (!(description || '').trim()) warnings.push({ critical: false, text: '商品説明文が未入力です' });
            return warnings;
        }

        function updateModalValidation(prefix, knownProfit) {
            const banner = document.getElementById(prefix + '-validation-banner');
            if (!banner) return;
            const title = (document.getElementById(prefix + '-title')?.value || '');
            const description = (document.getElementById(prefix + '-description')?.value || '');
            const status = (document.getElementById(prefix + '-status')?.value || 'LISTED');
            const price = parseNonNegInt(document.getElementById(prefix + '-price')?.value, 0);
            let photoCount = 0;
            if (prefix === 'add') {
                photoCount = (typeof pendingAddFiles !== 'undefined' && Array.isArray(pendingAddFiles)) ? pendingAddFiles.length : 0;
            } else {
                const editId = parseInt(document.getElementById('edit-id')?.value || '0', 10);
                const item = allItems.find(i => i.id === editId);
                photoCount = (item && Array.isArray(item.photos)) ? item.photos.length : 0;
            }
            const useCustom = document.getElementById(prefix + '-use-custom')?.checked;
            const shipping = useCustom ? parseNonNegInt(document.getElementById(prefix + '-shipping')?.value, pcSettings.defaultShippingCost) : pcSettings.defaultShippingCost;
            const feeRate = useCustom ? parseNonNegInt(document.getElementById(prefix + '-fee-rate')?.value, pcSettings.defaultFeeRatePercent) : pcSettings.defaultFeeRatePercent;
            const expense = useCustom ? parseNonNegInt(document.getElementById(prefix + '-expense')?.value, pcSettings.defaultExpenseCost) : pcSettings.defaultExpenseCost;
            const profit = (typeof knownProfit === 'number') ? knownProfit : (price - Math.floor(price * feeRate / 100) - shipping - expense);

            const warnings = evaluateListingWarningsObj({ title, description, photoCount, price, profit, status });
            if (warnings.length === 0) {
                banner.classList.add('hidden');
                banner.innerHTML = '';
                return;
            }
            const hasCritical = warnings.some(w => w.critical);
            banner.className = hasCritical
                ? 'rounded-lg p-2.5 text-[11px] border space-y-1 bg-orange-50 border-orange-200 text-orange-900'
                : 'rounded-lg p-2.5 text-[11px] border space-y-1 bg-amber-50 border-amber-200 text-amber-900';
            banner.innerHTML = `<p class='font-bold flex items-center gap-1'><i class='fa-solid fa-triangle-exclamation'></i> ${hasCritical ? '出品前チェック（要確認 ' + warnings.length + '件）' : '出品前メモ（' + warnings.length + '件）'}</p>` +
                warnings.map(w => `<p class='${w.critical ? 'text-orange-800 font-medium' : 'text-amber-800'}'>・${escapeHtml(w.text)}</p>`).join('');
            banner.classList.remove('hidden');
        }

        async function openEditModal(id) {
            await fetchSettings();
            const item = allItems.find(i => i.id === id);
            if (!item) return;

            document.getElementById('edit-id').value = item.id;
            document.getElementById('edit-title').value = item.title || '';
            document.getElementById('edit-price').value = (item.price != null) ? item.price : 0;
            document.getElementById('edit-sold-price').value = (item.soldPrice && item.soldPrice > 0) ? item.soldPrice : '';
            document.getElementById('edit-description').value = item.description || '';

            const useCustom = !!item.useCustomSettings;
            document.getElementById('edit-use-custom').checked = useCustom;
            document.getElementById('edit-shipping').value = (item.shippingCost != null) ? item.shippingCost : pcSettings.defaultShippingCost;
            document.getElementById('edit-fee-rate').value = (item.feeRatePercent != null) ? item.feeRatePercent : pcSettings.defaultFeeRatePercent;
            document.getElementById('edit-expense').value = (item.expenseCost != null) ? item.expenseCost : pcSettings.defaultExpenseCost;

            checkTitleLength('edit');
            setModalStatus('edit', item.status || 'LISTED');
            toggleCustomSettings('edit');
            renderEditModalPhotos(item.id);

            const folderBtn = document.getElementById('edit-open-folder-btn');
            folderBtn.onclick = () => openFolder(item.id);

            document.getElementById('edit-modal').classList.remove('hidden');
        }

        function renderEditModalPhotos(itemId) {
            const item = allItems.find(i => i.id == itemId);
            const grid = document.getElementById('edit-photo-preview-grid');
            const countElem = document.getElementById('edit-photo-count');
            const photos = (item && Array.isArray(item.photos)) ? item.photos : [];
            if (countElem) countElem.innerText = photos.length;
            if (!grid) return;

            if (photos.length === 0) {
                grid.innerHTML = `<p class='text-[11px] text-slate-400'>※ 登録済みの写真はありません</p>`;
                return;
            }

            grid.innerHTML = photos.map((p, idx) => {
                const url = p.md5Hash ? '/api/photos/' + p.md5Hash : (p.photoUrl || '');
                const hash = p.md5Hash || '';
                return `<div class='relative w-20 h-20 rounded-lg overflow-hidden border border-slate-200 bg-slate-100 shrink-0 group'>
                    <img src='${url}' onclick='window.open(""${url}"", ""_blank"")' class='w-full h-full object-cover cursor-pointer' title='クリックで原寸表示'>
                    <span class='absolute top-1 left-1 bg-black/60 text-white text-[9px] font-mono px-1 rounded'>#${idx + 1}</span>
                    ${hash ? `<button type='button' onclick='deleteEditPhotoByIdx(${item.id}, ${idx})' class='absolute top-1 right-1 w-5 h-5 rounded-full bg-rose-600 hover:bg-rose-700 text-white text-[10px] flex items-center justify-center shadow transition' title='写真を削除'><i class='fa-solid fa-xmark'></i></button>` : ''}
                </div>`;
            }).join('');
        }

        async function handleEditPhotoInputChange(e) {
            if (e.target.files && e.target.files.length > 0) {
                await uploadEditPhotoFiles(e.target.files);
                e.target.value = '';
            }
        }

        async function uploadEditPhotoFiles(fileList) {
            const itemId = document.getElementById('edit-id').value;
            if (!itemId) return;
            const files = Array.from(fileList).filter(f => /\.(jpg|jpeg|png|webp|gif|bmp)$/i.test(f.name) || (f.type && f.type.startsWith('image/')));
            if (files.length === 0) {
                showToast('有効な画像ファイルを選択してください');
                return;
            }

            showToast(files.length + '枚の写真を追加中...');
            let uploaded = 0;
            for (const f of files) {
                try {
                    const buf = await f.arrayBuffer();
                    const res = await fetch('/api/items/' + itemId + '/photos/add', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/octet-stream',
                            ...(AUTH_TOKEN ? { 'Authorization': 'Bearer ' + AUTH_TOKEN } : {})
                        },
                        body: buf
                    });
                    if (res.ok) uploaded++;
                } catch (err) {
                    console.log('Edit upload error:', err);
                }
            }
            await fetchItems();
            renderEditModalPhotos(itemId);
            showToast(uploaded + '枚の写真を追加しました！');
        }

        async function deleteEditPhotoByIdx(itemId, photoIdx) {
            const item = allItems.find(i => i.id == itemId);
            const photo = (item && item.photos) ? item.photos[photoIdx] : null;
            const md5Hash = photo ? photo.md5Hash : '';
            if (!md5Hash) return;
            if (!confirm('この写真を商品から削除しますか？')) return;
            try {
                const res = await fetch('/api/items/' + itemId + '/photos/delete', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ md5Hash })
                });
                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        await fetchItems();
                        renderEditModalPhotos(itemId);
                        showToast('写真を削除しました');
                    } else {
                        alert('写真削除に失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        function closeEditModal() {
            document.getElementById('edit-modal').classList.add('hidden');
        }

        async function updateItem() {
            const id = document.getElementById('edit-id').value;
            const title = document.getElementById('edit-title').value.trim();
            const price = parseNonNegInt(document.getElementById('edit-price').value, 0);
            const status = document.getElementById('edit-status').value;
            const soldRaw = document.getElementById('edit-sold-price').value;
            const soldPrice = status === 'SOLD' ? parseNonNegInt(soldRaw, price) : 0;
            const description = document.getElementById('edit-description').value.trim();
            const useCustomSettings = document.getElementById('edit-use-custom').checked;
            const shippingCost = useCustomSettings
                ? parseNonNegInt(document.getElementById('edit-shipping').value, pcSettings.defaultShippingCost)
                : pcSettings.defaultShippingCost;
            const feeRatePercent = useCustomSettings
                ? parseNonNegInt(document.getElementById('edit-fee-rate').value, pcSettings.defaultFeeRatePercent)
                : pcSettings.defaultFeeRatePercent;
            const expenseCost = useCustomSettings
                ? parseNonNegInt(document.getElementById('edit-expense').value, pcSettings.defaultExpenseCost)
                : pcSettings.defaultExpenseCost;

            if (!title) {
                alert('商品タイトルを入力してください。');
                return;
            }

            const btn = document.getElementById('edit-submit-btn');
            if (btn) btn.disabled = true;

            try {
                const res = await fetch('/api/items/' + id, {
                    method: 'PUT',
                    headers: getHeaders(),
                    body: JSON.stringify({
                        title, price, soldPrice, status, description,
                        useCustomSettings, shippingCost, feeRatePercent, expenseCost
                    })
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        closeEditModal();
                        showToast('商品情報を更新しました！');
                        fetchItems();
                    } else {
                        alert('更新失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            } finally {
                if (btn) btn.disabled = false;
            }
        }

        async function deleteItem(id) {
            if (!confirm('商品ID #' + id + ' をゴミ箱へ移動しますか？（30日間保管され、ゴミ箱タブから復元できます）')) return;

            try {
                const res = await fetch('/api/items/' + id, {
                    method: 'DELETE',
                    headers: getHeaders()
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        showToast('商品をゴミ箱へ移動しました');
                        fetchItems();
                    } else {
                        alert('削除失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        async function restoreItem(id) {
            try {
                const res = await fetch('/api/items/' + id + '/restore', {
                    method: 'POST',
                    headers: getHeaders()
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        showToast('商品をゴミ箱から復元しました');
                        fetchItems();
                    } else {
                        alert('復元失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        async function permanentDeleteItem(id) {
            if (!confirm('商品ID #' + id + ' を完全に削除しますか？\n写真ファイル・同期データを含め元に戻せなくなります。')) return;

            try {
                const res = await fetch('/api/items/' + id + '/permanent', {
                    method: 'DELETE',
                    headers: getHeaders()
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        showToast('商品を完全に削除しました');
                        fetchItems();
                    } else {
                        alert('完全削除失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        async function emptyTrash() {
            const trashedCount = allItems.filter(i => isItemTrashed(i)).length;
            if (trashedCount === 0) {
                showToast('ゴミ箱は空です');
                return;
            }
            if (!confirm('ゴミ箱内のすべての商品を完全に削除しますか？\nこの操作は取り消せません。')) return;

            try {
                const res = await fetch('/api/trash/empty', {
                    method: 'POST',
                    headers: getHeaders()
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        showToast('ゴミ箱を空にしました (' + (result.deletedCount || 0) + '件完全削除)');
                        fetchItems();
                    } else {
                        alert('ゴミ箱の削除に失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            }
        }

        function clearPendingAddPhotos() {
            pendingAddPhotoFiles.forEach(entry => {
                try { URL.revokeObjectURL(entry.previewUrl); } catch (e) {}
            });
            pendingAddPhotoFiles = [];
            renderAddModalPhotos();
        }

        function handleAddPhotoInputChange(e) {
            if (e.target.files && e.target.files.length > 0) {
                addPendingPhotos(e.target.files);
                e.target.value = '';
            }
        }

        function addPendingPhotos(fileList) {
            const files = Array.from(fileList).filter(f => /\.(jpg|jpeg|png|webp|gif|bmp)$/i.test(f.name) || (f.type && f.type.startsWith('image/')));
            if (files.length === 0) {
                showToast('有効な画像ファイルを選択してください');
                return;
            }
            files.forEach(file => {
                pendingAddPhotoFiles.push({
                    file,
                    previewUrl: URL.createObjectURL(file)
                });
            });
            renderAddModalPhotos();
        }

        function removePendingAddPhoto(idx) {
            const entry = pendingAddPhotoFiles[idx];
            if (entry) {
                try { URL.revokeObjectURL(entry.previewUrl); } catch (e) {}
            }
            pendingAddPhotoFiles.splice(idx, 1);
            renderAddModalPhotos();
        }

        function renderAddModalPhotos() {
            const grid = document.getElementById('add-photo-preview-grid');
            const countElem = document.getElementById('add-photo-count');
            if (countElem) countElem.innerText = pendingAddPhotoFiles.length;
            if (!grid) return;

            if (pendingAddPhotoFiles.length === 0) {
                grid.innerHTML = `<p class='text-[11px] text-slate-400'>※ 追加する写真を選択するとプレビューが表示されます</p>`;
                return;
            }

            grid.innerHTML = pendingAddPhotoFiles.map((entry, idx) => `
                <div class='relative w-20 h-20 rounded-lg overflow-hidden border border-slate-200 bg-slate-100 shrink-0'>
                    <img src='${entry.previewUrl}' class='w-full h-full object-cover'>
                    <span class='absolute top-1 left-1 bg-black/60 text-white text-[9px] font-mono px-1 rounded'>#${idx + 1}</span>
                    <button type='button' onclick='removePendingAddPhoto(${idx})' class='absolute top-1 right-1 w-5 h-5 rounded-full bg-rose-600 hover:bg-rose-700 text-white text-[10px] flex items-center justify-center shadow transition' title='選択から除外'><i class='fa-solid fa-xmark'></i></button>
                </div>
            `).join('');
        }

        async function openAddModal() {
            await fetchSettings();
            document.getElementById('add-title').value = '';
            document.getElementById('add-price').value = '';
            document.getElementById('add-sold-price').value = '';
            document.getElementById('add-description').value = '';
            document.getElementById('add-use-custom').checked = false;
            document.getElementById('add-shipping').value = pcSettings.defaultShippingCost;
            document.getElementById('add-fee-rate').value = pcSettings.defaultFeeRatePercent;
            document.getElementById('add-expense').value = pcSettings.defaultExpenseCost;
            clearPendingAddPhotos();
            setModalStatus('add', 'DRAFT');
            toggleCustomSettings('add');
            checkTitleLength('add');
            document.getElementById('add-modal').classList.remove('hidden');
        }

        function closeAddModal() {
            clearPendingAddPhotos();
            document.getElementById('add-modal').classList.add('hidden');
        }

        async function createItem() {
            const title = document.getElementById('add-title').value.trim();
            const price = parseNonNegInt(document.getElementById('add-price').value, 0);
            const status = document.getElementById('add-status').value;
            const soldRaw = document.getElementById('add-sold-price').value;
            const soldPrice = status === 'SOLD' ? parseNonNegInt(soldRaw, price) : 0;
            const description = document.getElementById('add-description').value.trim();
            const useCustomSettings = document.getElementById('add-use-custom').checked;
            const shippingCost = useCustomSettings
                ? parseNonNegInt(document.getElementById('add-shipping').value, pcSettings.defaultShippingCost)
                : pcSettings.defaultShippingCost;
            const feeRatePercent = useCustomSettings
                ? parseNonNegInt(document.getElementById('add-fee-rate').value, pcSettings.defaultFeeRatePercent)
                : pcSettings.defaultFeeRatePercent;
            const expenseCost = useCustomSettings
                ? parseNonNegInt(document.getElementById('add-expense').value, pcSettings.defaultExpenseCost)
                : pcSettings.defaultExpenseCost;

            if (!title) {
                alert('商品タイトルを入力してください。');
                return;
            }

            const btn = document.getElementById('add-submit-btn');
            if (btn) btn.disabled = true;

            try {
                const res = await fetch('/api/items', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({
                        title, price, soldPrice, status, description,
                        useCustomSettings, shippingCost, feeRatePercent, expenseCost
                    })
                });

                if (res.ok) {
                    const result = await res.json();
                    if (result.status === 'ok') {
                        let uploaded = 0;
                        if (pendingAddPhotoFiles.length > 0 && result.id) {
                            showToast('商品登録完了。写真 (' + pendingAddPhotoFiles.length + '枚) をアップロード中...');
                            for (const entry of pendingAddPhotoFiles) {
                                try {
                                    const buf = await entry.file.arrayBuffer();
                                    const upRes = await fetch('/api/items/' + result.id + '/photos/add', {
                                        method: 'POST',
                                        headers: {
                                            'Content-Type': 'application/octet-stream',
                                            ...(AUTH_TOKEN ? { 'Authorization': 'Bearer ' + AUTH_TOKEN } : {})
                                        },
                                        body: buf
                                    });
                                    if (upRes.ok) uploaded++;
                                } catch (upErr) {
                                    console.log('Add photo upload error:', upErr);
                                }
                            }
                        }
                        const stLabel = status === 'DRAFT' ? '下書き' : (status === 'SOLD' ? '売却済' : '出品中');
                        const photoMsg = uploaded > 0 ? ' (写真' + uploaded + '枚)' : '';
                        closeAddModal();
                        showToast('新規商品（' + stLabel + '）を追加しました' + photoMsg + '！');
                        fetchItems();
                    } else {
                        alert('追加失敗: ' + (result.message || '不明なエラー'));
                    }
                } else {
                    alert('サーバー通信エラー (Status: ' + res.status + ')');
                }
            } catch (e) {
                alert('通信エラー: ' + e.message);
            } finally {
                if (btn) btn.disabled = false;
            }
        }

        async function fetchCleanupPreview() {
            try {
                const res = await fetch('/api/cleanup/preview', { headers: getHeaders() });
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
                const res = await fetch('/api/cleanup/execute', { method: 'POST', headers: getHeaders() });
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

        function csvEscape(val) {
            const s = (val === null || val === undefined) ? '' : String(val);
            return '""' + s.replace(/""/g, '""""') + '""';
        }

        function formatCsvDate(ms) {
            if (!ms || ms <= 0) return '';
            const d = new Date(ms);
            const pad = (n) => String(n).padStart(2, '0');
            return d.getFullYear() + '/' + pad(d.getMonth() + 1) + '/' + pad(d.getDate()) + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
        }

        function downloadPcCsv() {
            const exportItems = allItems.filter(i => !isItemTrashed(i)).slice().sort((a, b) => (a.id || 0) - (b.id || 0));
            if (exportItems.length === 0) {
                showToast('エクスポート対象の商品がありません');
                return;
            }

            const lines = [];
            lines.push('ID,商品名,商品説明,ステータス,出品価格(円),実売却価格(円),手数料率(%),販売手数料(円),送料(円),梱包経費(円),想定/確定利益(円),写真枚数,同期ステータス,出品日,最終更新日,売却日,写真ハッシュ一覧,まとめ同梱内訳');

            exportItems.forEach(item => {
                const photos = item.photos || [];
                const statusText = item.status === 'SOLD'
                    ? '売却済'
                    : (item.status === 'MERGED'
                        ? ('まとめ内包済' + (item.mergedParentId ? ' (親#' + item.mergedParentId + ')' : ''))
                        : (item.status === 'DRAFT' ? '下書き' : '出品中'));

                const price = item.price || 0;
                const soldPrice = item.soldPrice || 0;
                const feePercent = item.feeRatePercent != null ? item.feeRatePercent : 5;
                const shipping = item.shippingCost != null ? item.shippingCost : 210;
                const expense = item.expenseCost != null ? item.expenseCost : 55;
                const basePrice = (item.status === 'SOLD' && soldPrice > 0) ? soldPrice : price;
                const fee = Math.floor((basePrice * feePercent) / 100);
                const profit = item.profit != null ? item.profit : Math.max(0, basePrice - fee - shipping - expense);
                const syncStatusText = item.isShared ? 'PC同期済' : '未送信';

                const createdStr = formatCsvDate(item.createdAt);
                const updatedStr = formatCsvDate(item.updatedAt);
                const soldStr = item.status === 'SOLD' ? formatCsvDate(item.soldAt) : '';
                const hashesStr = photos.map(p => p.md5Hash || '').filter(h => h).join(';');
                const mergedStr = item.mergedChildTitles || '';

                lines.push([
                    item.id,
                    csvEscape(item.title || ''),
                    csvEscape(item.description || ''),
                    statusText,
                    price,
                    soldPrice,
                    feePercent,
                    fee,
                    shipping,
                    expense,
                    profit,
                    photos.length,
                    syncStatusText,
                    createdStr,
                    updatedStr,
                    soldStr,
                    csvEscape(hashesStr),
                    csvEscape(mergedStr)
                ].join(','));
            });

            const csvContent = '\uFEFF' + lines.join('\r\n') + '\r\n';
            const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
            const url = URL.createObjectURL(blob);
            const now = new Date();
            const pad = (n) => String(n).padStart(2, '0');
            const fname = 'flea_market_pc_' + now.getFullYear() + pad(now.getMonth() + 1) + pad(now.getDate()) + '_' + pad(now.getHours()) + pad(now.getMinutes()) + '.csv';

            const a = document.createElement('a');
            a.href = url;
            a.download = fname;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            showToast('CSVをダウンロードしました (' + exportItems.length + '件)');
        }
    </script>
</body>
</html>";
    }
}
