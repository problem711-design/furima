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

    <!-- ナビゲーションバー (Yahoo!フリマ出品支援ヘッダー) -->
    <header class='bg-indigo-700 text-white shadow-md sticky top-0 z-30'>
        <div class='max-w-[1800px] mx-auto px-3 sm:px-4 min-h-10 py-1 flex flex-wrap items-center justify-between gap-x-3 gap-y-1'>
            <!-- 左側: タイトル & Yahoo!フリマショートカット -->
            <div class='flex items-center space-x-2 shrink-0'>
                <i class='fa-solid fa-boxes-stacked text-base text-indigo-200'></i>
                <h1 class='font-black text-sm tracking-tight whitespace-nowrap'>フリマ商品PC管理</h1>
                <button onclick='openYahooSplit()' class='bg-rose-600 hover:bg-rose-700 text-white text-[11px] font-bold px-2 py-0.5 rounded shadow-sm transition flex items-center gap-1 ml-1 border border-rose-400/50' title='Yahoo!フリマ出品画面を通常タブで開きます（タブを切り離して Win + ← / → で左右に並べて利用できます）'>
                    <i class='fa-solid fa-up-right-from-square text-[9px]'></i> Yahoo!フリマ出品画面を開く
                </button>
            </div>

            <!-- 中央・右側: ステータスフィルター & 検索ボックス -->
            <div class='flex items-center gap-2 flex-1 justify-end overflow-x-auto custom-scroll py-0.5'>
                <div class='flex items-center gap-1 bg-indigo-800/60 p-0.5 rounded-lg shrink-0'>
                    <button onclick='setFilter(""ALL"")' id='filter-all' class='filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-indigo-800 shadow-sm transition'>すべて (<span id='count-all'>0</span>)</button>
                    <button onclick='setFilter(""DRAFT"")' id='filter-draft' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>下書き (<span id='count-draft'>0</span>)</button>
                    <button onclick='setFilter(""LISTED"")' id='filter-listed' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>出品中 (<span id='count-listed'>0</span>)</button>
                    <button onclick='setFilter(""SHIPPING"")' id='filter-shipping' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>発送準備中 (<span id='count-shipping'>0</span>)</button>
                    <button onclick='setFilter(""SOLD"")' id='filter-sold' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'>売却済 (<span id='count-sold'>0</span>)</button>
                    <button onclick='setFilter(""TRASH"")' id='filter-trash' class='filter-btn px-2 py-0.5 text-xs font-medium rounded hover:bg-indigo-600/50 text-indigo-100 transition'><i class='fa-solid fa-trash-can mr-0.5'></i> ゴミ箱 (<span id='count-trash'>0</span>)</button>
                </div>

                <div class='relative w-40 lg:w-56 shrink-0'>
                    <i class='fa-solid fa-magnifying-glass absolute left-2.5 top-2 text-indigo-300 text-xs'></i>
                    <input type='text' id='search-input' oninput='renderItems()' placeholder='商品名・説明文で検索...' class='w-full pl-7 pr-2 py-1 text-xs bg-indigo-800/80 text-white placeholder-indigo-300 border border-indigo-500/50 rounded-md focus:outline-none focus:bg-indigo-900 focus:ring-1 focus:ring-white'>
                </div>
                <span id='stat-connection-status' class='font-bold text-[11px] text-amber-700 bg-amber-50 border border-amber-200 rounded-full px-2 py-0.5 flex items-center gap-1 shrink-0 whitespace-nowrap'><i class='fa-solid fa-circle-exclamation'></i> 未接続</span>
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
            <aside class='w-full lg:w-64 shrink-0 lg:sticky lg:top-14 lg:max-h-[calc(100vh-4.5rem)] lg:overflow-y-auto custom-scroll space-y-3'>
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
                        <i class='fa-solid fa-hard-drive text-indigo-500'></i> PC容量 & 写真情報
                    </h2>
                    <div class='bg-slate-50 p-2 rounded border border-slate-100 flex items-center justify-between text-[11px]'>
                        <span class='text-slate-600 font-medium'>取込済み写真</span>
                        <span id='stat-photo-index-count' class='font-bold text-slate-800 font-mono'>0枚</span>
                    </div>
                    <div class='bg-slate-50 p-2 rounded border border-slate-100 flex items-center justify-between text-[11px]'>
                        <span class='text-slate-600 font-medium'>保存フォルダ容量</span>
                        <span id='stat-storage-size' class='text-xs font-black text-slate-800'>...</span>
                    </div>
                    <div class='bg-slate-50 p-2 rounded border border-slate-100 flex items-center justify-between text-[11px]'>
                        <span class='text-slate-600 font-medium'>バージョン</span>
                        <span class='font-mono font-bold text-slate-700'>v1.3 (Build 9)</span>
                    </div>
                    <button onclick='openCleanupModal()' class='w-full bg-amber-600 hover:bg-amber-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-broom text-xs'></i> 売却30日経過写真の整理
                    </button>
                    <button onclick='openPhotoTrashModal()' class='w-full bg-rose-600 hover:bg-rose-700 text-white font-medium py-1.5 px-2.5 rounded-lg text-xs flex items-center justify-center gap-1.5 transition'>
                        <i class='fa-solid fa-trash-can-arrow-up text-xs'></i> 写真ゴミ箱 (<span id='sidebar-photo-trash-count'>0</span>)
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

    <!-- 複数選択フローティング操作バー (通常タブ・ゴミ箱タブ共通) -->
    <div id='merge-floating-bar' class='fixed bottom-5 left-1/2 -translate-x-1/2 z-50 bg-slate-900/95 text-white px-4 py-2.5 rounded-full shadow-2xl flex flex-wrap items-center justify-center gap-2 border border-slate-700 hidden max-w-[95vw]'>
        <span class='text-xs font-bold text-indigo-300 px-1'><span id='selected-count'>0</span>件選択中</span>
        <div id='floating-actions-active' class='flex items-center gap-2'>
            <button onclick='openBatchEditModal()' class='bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1'>
                <i class='fa-solid fa-pen-to-square'></i> 一括編集
            </button>
            <button onclick='batchTrashSelected()' class='bg-rose-600 hover:bg-rose-500 text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1'>
                <i class='fa-solid fa-trash'></i> ゴミ箱へ
            </button>
            <button id='floating-merge-btn' onclick='openMergeModal()' class='bg-purple-600 hover:bg-purple-500 disabled:bg-slate-700 disabled:text-slate-400 disabled:cursor-not-allowed text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1'>
                <i class='fa-solid fa-gift'></i> まとめる
            </button>
        </div>
        <div id='floating-actions-trash' class='hidden flex items-center gap-2'>
            <button onclick='batchRestoreSelected()' class='bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1'>
                <i class='fa-solid fa-rotate-left'></i> 一括復元
            </button>
            <button onclick='batchPermanentDeleteSelected()' class='bg-rose-600 hover:bg-rose-500 text-white text-xs font-bold px-3 py-1.5 rounded-full shadow transition flex items-center gap-1'>
                <i class='fa-solid fa-ban'></i> 一括完全削除
            </button>
        </div>
        <button onclick='clearSelection()' class='text-slate-400 hover:text-white text-xs font-bold px-2 py-1'>選択クリア</button>
    </div>

    <!-- 一括編集モーダル (部分更新＋サンプルプレビュー) -->
    <div id='batch-edit-modal' class='fixed inset-0 bg-black/60 z-50 hidden flex items-center justify-center p-4' onclick='closeBatchEditModal()'>
        <div class='bg-white rounded-xl max-w-lg w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-indigo-700 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-list-check text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>複数商品の一括編集 (<span id='batch-edit-count'>0</span>件)</h3>
                </div>
                <button onclick='closeBatchEditModal()' class='text-indigo-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-3.5 text-xs max-h-[75vh] overflow-y-auto custom-scroll'>
                <p class='text-[11px] text-slate-500'>チェックをONにした項目だけを上書きします（OFFの項目は元の値を保持）。</p>

                <!-- 1. ステータス -->
                <div class='border border-slate-200 rounded-lg p-3 bg-slate-50 space-y-2'>
                    <label class='flex items-center gap-2 font-bold text-slate-800 cursor-pointer'>
                        <input type='checkbox' id='batch-enable-status' onchange='updateBatchEditPreview()' class='w-4 h-4 text-indigo-600 rounded'>
                        <span>ステータスを変更</span>
                    </label>
                    <select id='batch-status-value' onchange='document.getElementById(""batch-enable-status"").checked=true; updateBatchEditPreview();' class='w-full p-2 border border-slate-300 rounded bg-white text-xs font-bold'>
                        <option value='LISTED'>出品中 (LISTED)</option>
                        <option value='DRAFT'>下書き中 (DRAFT)</option>
                        <option value='DRAFT_DONE'>下書き済み (DRAFT_DONE)</option>
                        <option value='SHIPPING'>発送準備中 (SHIPPING)</option>
                        <option value='SOLD'>売却済み (SOLD)</option>
                    </select>
                </div>

                <!-- 2. 費用プリセット -->
                <div class='border border-slate-200 rounded-lg p-3 bg-slate-50 space-y-2'>
                    <label class='flex items-center gap-2 font-bold text-slate-800 cursor-pointer'>
                        <input type='checkbox' id='batch-enable-preset' onchange='updateBatchEditPreview()' class='w-4 h-4 text-indigo-600 rounded'>
                        <span>送料・手数料プリセットを適用</span>
                    </label>
                    <select id='batch-preset-select' onchange='document.getElementById(""batch-enable-preset"").checked=true; updateBatchEditPreview();' class='w-full p-2 border border-slate-300 rounded bg-white text-xs'></select>
                </div>

                <!-- 3. 価格変更 -->
                <div class='border border-slate-200 rounded-lg p-3 bg-slate-50 space-y-2'>
                    <label class='flex items-center gap-2 font-bold text-slate-800 cursor-pointer'>
                        <input type='checkbox' id='batch-enable-price' onchange='updateBatchEditPreview()' class='w-4 h-4 text-indigo-600 rounded'>
                        <span>販売価格を一括変更</span>
                    </label>
                    <div class='grid grid-cols-2 gap-2'>
                        <select id='batch-price-mode' onchange='document.getElementById(""batch-enable-price"").checked=true; updateBatchEditPreview();' class='p-2 border border-slate-300 rounded bg-white text-xs font-bold'>
                            <option value='DELTA_YEN'>±金額で増減 (円)</option>
                            <option value='DELTA_PERCENT'>±割合で増減 (%)</option>
                            <option value='FIXED'>固定額に統一 (円)</option>
                        </select>
                        <input type='number' id='batch-price-value' value='-100' oninput='document.getElementById(""batch-enable-price"").checked=true; updateBatchEditPreview();' class='p-2 border border-slate-300 rounded bg-white text-xs font-mono' placeholder='例: -100'>
                    </div>
                    <p class='text-[10px] text-slate-500'>※ 値下げ時は「-100」または「-10」のようにマイナス入力してください（0円未満は0円にクランプ）。</p>
                </div>

                <!-- サンプルプレビュー -->
                <div id='batch-edit-preview' class='hidden bg-indigo-50 border border-indigo-200 rounded-lg p-2.5 text-[11px] text-indigo-900 space-y-0.5'></div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2'>
                <button type='button' onclick='closeBatchEditModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button type='button' id='batch-edit-submit-btn' onclick='submitBatchEdit()' class='px-5 py-2 text-xs font-bold bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white rounded-lg shadow-sm transition'>適用する</button>
            </div>
        </div>
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
                    <button id='viewer-scan-folder-btn' type='button' class='bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold px-2.5 py-1.5 rounded-lg border border-emerald-400/60 transition flex items-center gap-1.5' title='フォルダに直接追加された新規画像を取り込み'>
                        <i class='fa-solid fa-rotate'></i> フォルダ取込
                    </button>
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

            <!-- ギャラリー操作ツールバー (選択削除・全選択・ゴミ箱移動) -->
            <div id='viewer-action-bar' class='bg-indigo-50/80 px-5 py-2 border-b border-indigo-100 flex flex-wrap items-center justify-between gap-2'>
                <div class='flex items-center gap-2 text-[11px] text-slate-700'>
                    <span id='viewer-photo-count-badge' class='font-mono font-bold text-indigo-700 bg-white px-2 py-0.5 rounded border border-indigo-200'>0枚</span>
                    <button type='button' onclick='toggleSelectAllViewerPhotos()' class='px-2 py-1 rounded bg-white hover:bg-slate-100 border border-slate-300 text-slate-700 font-bold transition text-[11px]'>
                        すべて選択/解除
                    </button>
                    <span class='text-slate-500 hidden sm:inline'>サムネイル右上のチェックボックスで写真のゴミ箱移動・Muse送信ができます</span>
                </div>
                <div class='flex flex-wrap items-center gap-2'>
                    <span id='viewer-claude-bundle-note' class='hidden text-[11px] text-amber-700 font-bold'>まとめ商品は個別の子商品から送信してください</span>
                    <select id='viewer-claude-template' class='text-xs font-bold border border-violet-300 rounded-lg px-2 py-1.5 bg-white text-slate-700 max-w-[180px]' title='画像加工プロンプトテンプレート'></select>
                    <select id='viewer-claude-queue' class='text-xs font-bold border border-violet-300 rounded-lg px-2 py-1.5 bg-white text-slate-700' title='送り先（巡回: 承認後に自動処理 / 手動: チャットで呼んで即実行 / 待機: あとで送る）'>
                        <option value='patrol'>巡回（自動）</option>
                        <option value='manual'>手動（すぐ）</option>
                        <option value='waiting'>待機（あとで）</option>
                    </select>
                    <button type='button' id='viewer-claude-send-btn' onclick='sendViewerPhotosToClaude()' disabled class='bg-violet-600 hover:bg-violet-700 disabled:bg-slate-300 disabled:text-slate-500 disabled:cursor-not-allowed text-white text-xs font-bold px-3 py-1.5 rounded-lg shadow-sm transition flex items-center gap-1.5'>
                        <i class='fa-solid fa-wand-magic-sparkles'></i> Museに送る (<span id='viewer-claude-selected-count'>0</span>枚)
                    </button>
                    <button type='button' onclick='openClaudeJobsModal()' class='bg-white hover:bg-violet-50 border border-violet-200 text-violet-700 text-xs font-bold px-2.5 py-1.5 rounded-lg transition flex items-center gap-1'>
                        <i class='fa-solid fa-list-check'></i> Museジョブ (<span id='viewer-claude-jobs-count'>0</span>)
                    </button>
                    <button type='button' id='viewer-trash-selected-btn' onclick='trashSelectedViewerPhotos()' disabled class='bg-rose-600 hover:bg-rose-700 disabled:bg-slate-300 disabled:text-slate-500 disabled:cursor-not-allowed text-white text-xs font-bold px-3 py-1.5 rounded-lg shadow-sm transition flex items-center gap-1.5'>
                        <i class='fa-solid fa-trash-can'></i> 選択した写真をゴミ箱へ移動 (<span id='viewer-selected-count'>0</span>枚)
                    </button>
                    <button type='button' onclick='openPhotoTrashModal()' class='bg-white hover:bg-rose-50 border border-rose-200 text-rose-700 text-xs font-bold px-2.5 py-1.5 rounded-lg transition flex items-center gap-1'>
                        <i class='fa-solid fa-trash-can-arrow-up'></i> 写真ゴミ箱 (<span id='viewer-photo-trash-count'>0</span>)
                    </button>
                </div>
            </div>

            <div class='p-4 flex-1 overflow-y-auto custom-scroll space-y-4'>
                <!-- 写真ドラッグ＆ドロップ追加ゾーン -->
                <div id='photo-drop-zone' class='border-2 border-dashed border-indigo-200 hover:border-indigo-400 bg-indigo-50/40 rounded-xl p-3 text-center transition cursor-pointer'>
                    <i class='fa-solid fa-cloud-arrow-up text-indigo-500 text-xl mb-1'></i>
                    <p class='text-xs font-bold text-slate-700'>PCから画像ファイルをドラッグ＆ドロップして写真を追加</p>
                    <p class='text-[10px] text-slate-400'>JPG, PNG 等の画像ファイルを直接ドロップして本商品に追加できます</p>
                    <input type='file' id='photo-drop-file-input' accept='image/*' multiple class='hidden' onclick='event.stopPropagation()' onchange='handleFileInputChange(event)'>
                </div>

                <!-- 写真プレビュー表示グリッド -->
                <div id='viewer-photos-container' class='space-y-4'>
                    <!-- JavaScriptで動的生成 -->
                </div>
            </div>

            <div class='bg-slate-50 px-5 py-2.5 border-t border-slate-200 flex items-center justify-between text-[11px] text-slate-500'>
                <span class='flex items-center gap-1.5'><i class='fa-solid fa-circle-info text-indigo-500'></i> サムネイルクリックまたは「原寸表示」で全画面送り戻しビューアーが開きます。</span>
                <button onclick='closePhotoViewer()' class='px-4 py-1.5 text-xs font-bold bg-slate-200 hover:bg-slate-300 text-slate-700 rounded-lg transition'>閉じる</button>
            </div>
        </div>
    </div>

    <!-- 全画面原寸写真ライトボックスモーダル (次の写真 / 前の写真 送り戻し対応) -->
    <div id='photo-lightbox-modal' class='fixed inset-0 bg-black/95 z-[60] hidden flex flex-col justify-between p-3 sm:p-5' onclick='closePhotoLightbox()'>
        <div class='w-full max-w-6xl mx-auto flex flex-wrap items-center justify-between gap-2 bg-slate-900/90 border border-slate-800 rounded-xl px-4 py-2.5 text-white shadow-xl' onclick='event.stopPropagation()'>
            <div class='flex items-center gap-2.5 overflow-hidden'>
                <span id='lightbox-counter' class='text-xs font-mono font-bold bg-indigo-600 px-2.5 py-1 rounded-md shrink-0'>1 / 1</span>
                <span id='lightbox-title' class='text-xs sm:text-sm font-bold text-slate-100 truncate max-w-xs sm:max-w-md'></span>
                <span id='lightbox-filename' class='hidden text-[11px] font-mono bg-slate-800 text-emerald-300 border border-slate-700 px-2 py-0.5 rounded truncate max-w-[200px]'></span>
            </div>
            <div class='flex items-center gap-2 shrink-0'>
                <button type='button' onclick='copyCurrentLightboxPhoto()' class='bg-slate-800 hover:bg-slate-700 border border-slate-600 text-white text-xs font-bold px-3 py-1.5 rounded-lg transition flex items-center gap-1.5'>
                    <i class='fa-regular fa-copy text-indigo-300'></i> 画像コピー
                </button>
                <a id='lightbox-newtab-link' href='#' target='_blank' class='bg-slate-800 hover:bg-slate-700 border border-slate-600 text-slate-200 text-xs font-bold px-3 py-1.5 rounded-lg transition flex items-center gap-1.5'>
                    <i class='fa-solid fa-up-right-from-square'></i> 別タブ
                </a>
                <button type='button' onclick='trashCurrentLightboxPhoto()' class='bg-rose-600 hover:bg-rose-700 text-white text-xs font-bold px-3 py-1.5 rounded-lg transition flex items-center gap-1.5'>
                    <i class='fa-solid fa-trash-can'></i> ゴミ箱へ
                </button>
                <button type='button' onclick='closePhotoLightbox()' class='text-slate-300 hover:text-white text-xl p-1 transition' title='閉じる (Esc)'>
                    <i class='fa-solid fa-xmark'></i>
                </button>
            </div>
        </div>

        <div class='relative flex-1 w-full max-w-6xl mx-auto flex items-center justify-center my-2 min-h-0' onclick='event.stopPropagation()'>
            <button id='lightbox-prev-btn' type='button' onclick='prevLightboxPhoto()' class='absolute left-2 sm:left-4 top-1/2 -translate-y-1/2 bg-black/70 hover:bg-black text-white w-12 h-12 rounded-full flex items-center justify-center text-xl border border-white/25 shadow-lg transition disabled:opacity-30 disabled:cursor-not-allowed' title='前の写真 (←キー)'>
                <i class='fa-solid fa-chevron-left'></i>
            </button>
            <img id='lightbox-main-img' src='' alt='原寸プレビュー' class='max-w-full max-h-[72vh] rounded-lg shadow-2xl object-contain border border-slate-800 select-none'>
            <button id='lightbox-next-btn' type='button' onclick='nextLightboxPhoto()' class='absolute right-2 sm:right-4 top-1/2 -translate-y-1/2 bg-black/70 hover:bg-black text-white w-12 h-12 rounded-full flex items-center justify-center text-xl border border-white/25 shadow-lg transition disabled:opacity-30 disabled:cursor-not-allowed' title='次の写真 (→キー)'>
                <i class='fa-solid fa-chevron-right'></i>
            </button>
        </div>

        <div class='w-full max-w-6xl mx-auto bg-slate-900/90 border border-slate-800 rounded-xl p-2.5 text-white' onclick='event.stopPropagation()'>
            <div class='flex items-center justify-between text-[10px] text-slate-400 mb-1.5 px-1'>
                <span>下のサムネイルまたは左右ボタンで写真を切り替えできます</span>
                <span>キーボード: [←]前へ / [→]次へ / [Esc]閉じる</span>
            </div>
            <div id='lightbox-thumbs-strip' class='flex gap-2 overflow-x-auto custom-scroll py-1 px-1'></div>
        </div>
    </div>

    <!-- 写真単位ゴミ箱モーダル (案A: _photo_trash 復元・完全削除) -->
    <div id='photo-trash-modal' class='fixed inset-0 bg-black/70 z-[55] hidden flex items-center justify-center p-3 sm:p-5' onclick='closePhotoTrashModal()'>
        <div class='bg-white rounded-xl max-w-4xl w-full max-h-[88vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-rose-800 text-white px-5 py-3 flex items-center justify-between border-b border-rose-900'>
                <div class='flex items-center gap-2.5'>
                    <i class='fa-solid fa-trash-can-arrow-up text-rose-200 text-lg'></i>
                    <div>
                        <h3 class='font-bold text-sm tracking-tight'>写真ゴミ箱 (_photo_trash)</h3>
                        <p id='photo-trash-subtitle' class='text-[11px] text-rose-200'>ギャラリーから削除した写真を元の商品へ復元、または完全削除できます</p>
                    </div>
                </div>
                <button type='button' onclick='closePhotoTrashModal()' class='text-rose-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='bg-rose-50 px-5 py-2.5 border-b border-rose-100 flex flex-wrap items-center justify-between gap-2'>
                <div class='flex items-center gap-2 text-xs'>
                    <button type='button' onclick='toggleSelectAllPhotoTrash()' class='px-2.5 py-1 bg-white hover:bg-slate-100 border border-slate-300 rounded font-bold text-slate-700 transition'>すべて選択/解除</button>
                    <span id='photo-trash-summary-text' class='font-mono text-slate-600'>0枚 (0 B)</span>
                </div>
                <div class='flex flex-wrap items-center gap-2'>
                    <button type='button' id='photo-trash-restore-btn' onclick='restoreSelectedPhotoTrash()' disabled class='bg-emerald-600 hover:bg-emerald-700 disabled:bg-slate-300 disabled:text-slate-500 text-white text-xs font-bold px-3 py-1.5 rounded-lg transition flex items-center gap-1'>
                        <i class='fa-solid fa-rotate-left'></i> 選択を復元 (<span id='photo-trash-selected-count'>0</span>)
                    </button>
                    <button type='button' id='photo-trash-perm-btn' onclick='deleteSelectedPhotoTrashPermanent()' disabled class='bg-rose-600 hover:bg-rose-700 disabled:bg-slate-300 disabled:text-slate-500 text-white text-xs font-bold px-3 py-1.5 rounded-lg transition flex items-center gap-1'>
                        <i class='fa-solid fa-fire'></i> 完全削除
                    </button>
                    <button type='button' onclick='emptyPhotoTrash()' class='bg-slate-700 hover:bg-slate-800 text-white text-xs font-bold px-3 py-1.5 rounded-lg transition'>
                        空にする
                    </button>
                </div>
            </div>
            <div id='photo-trash-grid' class='p-4 flex-1 overflow-y-auto custom-scroll grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3'>
                <!-- JSで動的描画 -->
            </div>
            <div class='bg-slate-50 px-5 py-2.5 border-t border-slate-200 flex items-center justify-between text-[11px] text-slate-500'>
                <span>復元すると元の商品（まとめ商品の場合は内訳含む）へ戻ります。</span>
                <button type='button' onclick='closePhotoTrashModal()' class='px-4 py-1.5 text-xs font-bold bg-slate-200 hover:bg-slate-300 text-slate-700 rounded-lg transition'>閉じる</button>
            </div>
        </div>
    </div>

    <!-- Muse 送信ジョブ一覧モーダル -->
    <div id='claude-jobs-modal' class='fixed inset-0 bg-black/70 z-[55] hidden flex items-center justify-center p-3 sm:p-5' onclick='closeClaudeJobsModal()'>
        <div class='bg-white rounded-xl max-w-3xl w-full max-h-[88vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-violet-800 text-white px-5 py-3 flex items-center justify-between border-b border-violet-900'>
                <div class='flex items-center gap-2.5'>
                    <i class='fa-solid fa-wand-magic-sparkles text-violet-200 text-lg'></i>
                    <div>
                        <h3 class='font-bold text-sm tracking-tight'>Muse 送信ジョブ</h3>
                        <p class='text-[11px] text-violet-200'>「Museに送る」で作成した画像加工ジョブの一覧です。巡回・手動は送信時にスマホへ承認リクエストが届き、巡回は承認後に自動で処理されます。手動はチャットで呼び出して実行します。待機はあとから巡回・手動へ移せます</p>
                    </div>
                </div>
                <button type='button' onclick='closeClaudeJobsModal()' class='text-violet-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='px-4 pt-3 flex items-center gap-2'>
                <button type='button' id='claude-jobs-tab-active' onclick='switchClaudeJobsTab("active")' class='px-3 py-1.5 text-xs font-bold rounded-lg transition'>依頼中</button>
                <button type='button' id='claude-jobs-tab-waiting' onclick='switchClaudeJobsTab("waiting")' class='px-3 py-1.5 text-xs font-bold rounded-lg transition'>待機中</button>
            </div>
            <div id='claude-jobs-list' class='p-4 flex-1 overflow-y-auto custom-scroll space-y-2'>
                <!-- JSで動的描画 -->
            </div>
            <div class='bg-slate-50 px-5 py-2.5 border-t border-slate-200 flex items-center justify-between text-[11px] text-slate-500'>
                <span>待機中のジョブは取り消せます。完了したジョブは30日後に自動整理されます。</span>
                <button type='button' onclick='closeClaudeJobsModal()' class='px-4 py-1.5 text-xs font-bold bg-slate-200 hover:bg-slate-300 text-slate-700 rounded-lg transition'>閉じる</button>
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
                    <p>・出品中・下書き・発送準備中の写真は削除対象に含まれません。</p>
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
        <div class='bg-white rounded-xl max-w-2xl w-full shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-emerald-700 text-white px-5 py-3.5 flex items-center justify-between'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-plus text-lg'></i>
                    <h3 class='font-bold text-sm tracking-tight'>新規商品の追加 (PC母艦)</h3>
                </div>
                <button onclick='closeAddModal()' class='text-emerald-200 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 space-y-4 text-xs max-h-[88vh] overflow-y-auto custom-scroll'>
                <!-- ① 商品写真セクション -->
                <div class='space-y-2'>
                    <div class='flex items-center justify-between'>
                        <label class='font-bold text-slate-700 flex items-center gap-1.5'>
                            <i class='fa-solid fa-camera text-emerald-600'></i> 商品写真 (<span id='add-photo-count'>0</span>枚)
                        </label>
                        <span class='text-[10px] text-slate-400'>※ 登録確定時にアップロードされます</span>
                    </div>
                    <div id='add-photo-preview-grid' class='flex flex-wrap gap-2 items-center'></div>
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
                        <label class='font-bold text-slate-700'>商品説明文 (表示・編集領域拡大)</label>
                        <select id='add-desc-tpl-select' onchange='applyTemplateSelect(""add-description"", this, false)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[180px]'>
                            <option value=''>説明定型文を挿入...</option>
                        </select>
                    </div>
                    <textarea id='add-description' rows='12' oninput='updateModalValidation(""add"")' class='w-full p-2.5 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-emerald-500 min-h-[220px]' placeholder='商品の状態や詳細をご入力ください...'></textarea>
                </div>

                <!-- ステータス選択チップ -->
                <div>
                    <label class='block font-bold text-slate-700 mb-1.5'>ステータス</label>
                    <input type='hidden' id='add-status' value='DRAFT'>
                    <div class='flex flex-wrap gap-2'>
                        <button type='button' id='add-status-chip-DRAFT' onclick='setModalStatus(""add"", ""DRAFT"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き中</button>
                        <button type='button' id='add-status-chip-DRAFT_DONE' onclick='setModalStatus(""add"", ""DRAFT_DONE"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き済み</button>
                        <button type='button' id='add-status-chip-LISTED' onclick='setModalStatus(""add"", ""LISTED"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>出品中</button>
                        <button type='button' id='add-status-chip-SHIPPING' onclick='setModalStatus(""add"", ""SHIPPING"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>発送準備中</button>
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
            <div class='p-5 space-y-4 text-xs max-h-[88vh] overflow-y-auto custom-scroll'>
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
                    <div id='edit-photo-preview-grid' class='flex flex-wrap gap-2 items-center'></div>
                </div>

                <!-- ② 商品基本情報 -->
                <div>
                    <div class='flex justify-between items-center mb-1 gap-2'>
                        <label class='font-bold text-slate-700'>商品タイトル (Yahoo!フリマ40文字以内) <span class='text-rose-500'>*</span></label>
                        <div class='flex items-center gap-2'>
                            <button type='button' onclick='copyText(document.getElementById(""edit-title"").value, ""タイトル"", this)' class='px-2 py-0.5 rounded border border-slate-300 bg-white hover:bg-slate-100 text-[10px] font-bold text-slate-700 flex items-center gap-1 transition'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> コピー
                            </button>
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
                        <label class='font-bold text-slate-700'>商品説明文 (表示・編集領域拡大)</label>
                        <div class='flex items-center gap-2'>
                            <button type='button' onclick='copyText(document.getElementById(""edit-description"").value, ""商品説明"", this)' class='px-2 py-0.5 rounded border border-slate-300 bg-white hover:bg-slate-100 text-[10px] font-bold text-slate-700 flex items-center gap-1 transition'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> コピー
                            </button>
                            <select id='edit-desc-tpl-select' onchange='applyTemplateSelect(""edit-description"", this, false)' class='text-[10px] border border-slate-300 rounded px-1.5 py-0.5 bg-white text-slate-700 max-w-[180px]'>
                                <option value=''>説明定型文を挿入...</option>
                            </select>
                        </div>
                    </div>
                    <textarea id='edit-description' rows='12' oninput='updateModalValidation(""edit"")' class='w-full p-2.5 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500 min-h-[220px]'></textarea>
                </div>

                <!-- ステータス選択チップ -->
                <div>
                    <label class='block font-bold text-slate-700 mb-1.5'>ステータス</label>
                    <input type='hidden' id='edit-status' value='LISTED'>
                    <div class='flex flex-wrap gap-2'>
                        <button type='button' id='edit-status-chip-DRAFT' onclick='setModalStatus(""edit"", ""DRAFT"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き中</button>
                        <button type='button' id='edit-status-chip-DRAFT_DONE' onclick='setModalStatus(""edit"", ""DRAFT_DONE"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>下書き済み</button>
                        <button type='button' id='edit-status-chip-LISTED' onclick='setModalStatus(""edit"", ""LISTED"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>出品中</button>
                        <button type='button' id='edit-status-chip-SHIPPING' onclick='setModalStatus(""edit"", ""SHIPPING"")' class='px-3 py-1.5 rounded-lg text-xs font-bold border transition'>発送準備中</button>
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
                    <div class='flex items-center justify-between border-b border-slate-200 pb-1.5'>
                        <h4 class='font-bold text-slate-800 text-sm flex items-center gap-1.5'>
                            <i class='fa-solid fa-heading text-indigo-500'></i> タイトル用テンプレート
                        </h4>
                        <button type='button' onclick='openTemplateEditorModal(""title"", -1)' class='px-2.5 py-1 bg-indigo-600 hover:bg-indigo-700 text-white rounded-md font-bold text-[11px] flex items-center gap-1 transition'>
                            <i class='fa-solid fa-plus'></i> 追加・大型編集
                        </button>
                    </div>
                    <div id='settings-title-tpl-list' class='space-y-1.5'></div>
                </div>

                <!-- 3. 商品説明用テンプレート -->
                <div class='space-y-2.5'>
                    <div class='flex items-center justify-between border-b border-slate-200 pb-1.5'>
                        <h4 class='font-bold text-slate-800 text-sm flex items-center gap-1.5'>
                            <i class='fa-solid fa-align-left text-emerald-600'></i> 商品説明用テンプレート
                        </h4>
                        <button type='button' onclick='openTemplateEditorModal(""desc"", -1)' class='px-2.5 py-1 bg-emerald-600 hover:bg-emerald-700 text-white rounded-md font-bold text-[11px] flex items-center gap-1 transition'>
                            <i class='fa-solid fa-plus'></i> 追加・大型編集
                        </button>
                    </div>
                    <div id='settings-desc-tpl-list' class='space-y-1.5'></div>
                </div>

                <!-- 4. 画像加工プロンプト用テンプレート -->
                <div class='space-y-2.5'>
                    <div class='flex items-center justify-between border-b border-slate-200 pb-1.5'>
                        <h4 class='font-bold text-slate-800 text-sm flex items-center gap-1.5'>
                            <i class='fa-solid fa-wand-magic-sparkles text-violet-500'></i> 画像加工プロンプト用テンプレート
                        </h4>
                        <button type='button' onclick='openTemplateEditorModal(""prompt"", -1)' class='px-2.5 py-1 bg-violet-600 hover:bg-violet-700 text-white rounded-md font-bold text-[11px] flex items-center gap-1 transition'>
                            <i class='fa-solid fa-plus'></i> 追加・大型編集
                        </button>
                    </div>
                    <div id='settings-prompt-tpl-list' class='space-y-1.5'></div>
                </div>
                <!-- Muse 巡回用の許可アドレス (サーバー専用設定・スマホとは同期しない) -->
                <div class='space-y-3'>
                    <div class='border-b border-slate-200 pb-1.5'>
                        <h4 class='font-bold text-slate-800 text-sm flex items-center gap-1.5'>
                            <i class='fa-solid fa-robot text-indigo-500'></i> Muse 巡回用の許可アドレス（サーバー専用）
                        </h4>
                    </div>
                    <p class='text-[11px] text-slate-500 leading-relaxed'>Muse 端末側の Tailscale IP を登録すると、そのアドレスからのジョブ実行系アクセスだけが合鍵なしで通るようになります（スマホでの承認は今までどおり必要です）。<span class='font-bold text-rose-600'>PC 自身のアドレスではありません</span>。空欄にすると無効です。この設定はスマホと同期されません。</p>
                    <div class='flex items-center gap-2'>
                        <input type='text' id='settings-trusted-claude-ip' placeholder='例: 100.x.x.x（空欄で無効）' class='flex-1 border border-slate-300 rounded-md px-2.5 py-1.5 text-xs font-mono' />
                        <button type='button' onclick='saveTrustedClaudeIp()' class='px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-md font-bold text-[11px] transition'>保存</button>
                    </div>
                </div>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end'>
                <button onclick='closeSettingsModal()' class='px-4 py-2 text-xs font-bold bg-slate-700 hover:bg-slate-800 text-white rounded-lg transition'>閉じる</button>
            </div>
        </div>
    </div>

    <!-- テンプレート全画面スタイル編集モーダル -->
    <div id='template-editor-modal' class='fixed inset-0 bg-black/70 z-[60] hidden flex items-center justify-center p-3 sm:p-6' onclick='closeTemplateEditorModal()'>
        <div class='bg-white rounded-xl w-full max-w-4xl h-[88vh] shadow-2xl flex flex-col overflow-hidden border border-slate-200' onclick='event.stopPropagation()'>
            <div class='bg-slate-800 text-white px-5 py-3.5 flex items-center justify-between shrink-0'>
                <div class='flex items-center gap-2'>
                    <i class='fa-solid fa-file-pen text-emerald-300 text-lg'></i>
                    <h3 id='tpl-editor-title' class='font-bold text-sm tracking-tight'>テンプレート編集</h3>
                </div>
                <button onclick='closeTemplateEditorModal()' class='text-slate-300 hover:text-white text-lg font-bold p-1 transition'><i class='fa-solid fa-xmark'></i></button>
            </div>
            <div class='p-5 flex-1 flex flex-col gap-3 overflow-hidden text-xs'>
                <input type='hidden' id='tpl-editor-category' value='desc'>
                <input type='hidden' id='tpl-editor-edit-id' value=''>
                <div>
                    <label class='block font-bold text-slate-700 mb-1'>表示名</label>
                    <input type='text' id='tpl-editor-name' placeholder='表示名 (例: 発送・ペットなし案内)' class='w-full p-2.5 border border-slate-300 rounded-lg bg-white text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500'>
                </div>
                <div class='flex items-center justify-between flex-wrap gap-2'>
                    <div class='flex items-center gap-1.5'>
                        <span class='text-[11px] font-bold text-slate-600'>変数挿入：</span>
                        <button type='button' onclick='insertTextAtCursor(""tpl-editor-content"", ""{title}"")' class='px-2 py-1 rounded border border-indigo-200 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 font-mono text-[11px] font-bold transition'>+ {title} 商品名</button>
                        <button type='button' onclick='insertTextAtCursor(""tpl-editor-content"", ""{price}"")' class='px-2 py-1 rounded border border-indigo-200 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 font-mono text-[11px] font-bold transition'>+ {price} 価格</button>
                    </div>
                    <span id='tpl-editor-char-count' class='text-[11px] font-mono text-slate-500'>0文字</span>
                </div>
                <textarea id='tpl-editor-content' oninput='updateTemplateEditorCharCount()' placeholder='挿入するテキストを入力...' class='w-full flex-1 p-3 border border-slate-300 rounded-lg bg-white text-sm leading-relaxed resize-none focus:outline-none focus:ring-2 focus:ring-indigo-500 custom-scroll'></textarea>
            </div>
            <div class='bg-slate-100 px-5 py-3 border-t border-slate-200 flex items-center justify-end gap-2 shrink-0'>
                <button type='button' onclick='closeTemplateEditorModal()' class='px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200 rounded-lg transition'>キャンセル</button>
                <button type='button' onclick='saveTemplateFromEditorModal()' class='px-5 py-2 text-xs font-bold bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg shadow-sm transition'>保存して同期</button>
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
        let lastKnownClaudeRevision = null;
        let currentViewerItemId = null;
        let currentViewerTab = 'group';
        let selectedViewerPhotoIndices = new Set();
        let lightboxPhotos = [];
        let lightboxIndex = 0;
        let photoTrashEntries = [];
        let selectedPhotoTrashIds = new Set();
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
            promptTemplates: [],
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

        async function copyText(text, label, btnElem) {
            const markCopied = () => {
                showToast(label + 'をコピーしました！');
                if (btnElem) {
                    if (!btnElem.dataset.origHtml) btnElem.dataset.origHtml = btnElem.innerHTML;
                    btnElem.innerHTML = '<i class=""fa-solid fa-check text-emerald-600""></i> コピー済';
                    btnElem.classList.add('border-emerald-400', 'bg-emerald-50', 'text-emerald-700');
                    clearTimeout(btnElem._copyTimer);
                    btnElem._copyTimer = setTimeout(() => {
                        btnElem.innerHTML = btnElem.dataset.origHtml;
                        btnElem.classList.remove('border-emerald-400', 'bg-emerald-50', 'text-emerald-700');
                    }, 1500);
                }
            };
            try {
                await navigator.clipboard.writeText(text);
                markCopied();
            } catch (err) {
                const ta = document.createElement('textarea');
                ta.value = text;
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                document.body.removeChild(ta);
                markCopied();
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
            refreshPhotoTrashCount();
            setInterval(pollServerRevisions, 3000);
            document.addEventListener('visibilitychange', () => {
                if (!document.hidden) {
                    pollServerRevisions();
                }
            });

            // キーボードショートカット (ライトボックス送り戻し / Esc)
            document.addEventListener('keydown', (e) => {
                const lightbox = document.getElementById('photo-lightbox-modal');
                if (lightbox && !lightbox.classList.contains('hidden')) {
                    if (e.key === 'ArrowLeft') {
                        e.preventDefault();
                        prevLightboxPhoto();
                    } else if (e.key === 'ArrowRight') {
                        e.preventDefault();
                        nextLightboxPhoto();
                    } else if (e.key === 'Escape') {
                        e.preventDefault();
                        closePhotoLightbox();
                    }
                    return;
                }
                const photoTrashModal = document.getElementById('photo-trash-modal');
                if (photoTrashModal && !photoTrashModal.classList.contains('hidden')) {
                    if (e.key === 'Escape') {
                        e.preventDefault();
                        closePhotoTrashModal();
                    }
                    return;
                }
                const viewerModal = document.getElementById('photo-viewer-modal');
                if (viewerModal && !viewerModal.classList.contains('hidden')) {
                    if (e.key === 'Escape') {
                        e.preventDefault();
                        closePhotoViewer();
                    }
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
                        promptTemplates: Array.isArray(data.promptTemplates) ? data.promptTemplates : [],
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

        function expandPcTemplatePlaceholders(content, prefix) {
            const rawTitle = (document.getElementById(prefix + '-title')?.value || '').trim();
            const rawPrice = parseNonNegInt(document.getElementById(prefix + '-price')?.value, 0);
            return (content || '')
                .replace(/\{title\}/g, rawTitle)
                .replace(/\{price\}/g, rawPrice.toLocaleString());
        }

        function applyTemplateSelect(elementId, selectElem, isTitle) {
            const idx = selectElem.selectedIndex;
            if (idx <= 0) return;
            const list = isTitle ? (pcSettings.titleTemplates || []) : (pcSettings.descriptionTemplates || []);
            const tpl = list[idx - 1];
            if (tpl && tpl.content) {
                const prefix = elementId.split('-')[0];
                const expanded = expandPcTemplatePlaceholders(tpl.content, prefix);
                insertTextAtCursor(elementId, expanded);
                if (isTitle) {
                    checkTitleLength(prefix);
                } else if (prefix === 'add' || prefix === 'edit') {
                    updateModalValidation(prefix);
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
            renderClaudeTemplateSelect();
        }

        function renderClaudeTemplateSelect() {
            const sel = document.getElementById('viewer-claude-template');
            if (!sel) return;
            const tpls = pcSettings.promptTemplates || [];
            const prev = sel.value;
            if (tpls.length === 0) {
                sel.innerHTML = `<option value=''>プロンプト未登録（設定で追加）</option>`;
                sel.disabled = true;
            } else {
                sel.innerHTML = tpls.map(t => `<option value='${escapeHtml(t.id)}'>${escapeHtml(t.name)}</option>`).join('');
                sel.disabled = false;
                if (prev && tpls.some(t => t.id === prev)) sel.value = prev;
            }
            const item = allItems.find(i => i.id === currentViewerItemId);
            updateViewerSelectionBar(item && item.photos ? item.photos.length : 0);
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
                    promptTemplates: Array.isArray(saved.promptTemplates) ? saved.promptTemplates : [],
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
            loadTrustedClaudeIp();
            document.getElementById('settings-modal').classList.remove('hidden');
        }

        async function loadTrustedClaudeIp() {
            try {
                const res = await fetch('/api/server/trusted-muse-ip', { headers: { 'Authorization': 'Bearer ' + AUTH_TOKEN } });
                if (!res.ok) return;
                const data = await res.json();
                document.getElementById('settings-trusted-claude-ip').value = data.trustedMuseIp || '';
            } catch (e) { /* 読み込みに失敗した場合は空欄のままにする */ }
        }

        async function saveTrustedClaudeIp() {
            const value = document.getElementById('settings-trusted-claude-ip').value.trim();
            try {
                const res = await fetch('/api/server/trusted-muse-ip', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + AUTH_TOKEN },
                    body: JSON.stringify({ trustedMuseIp: value })
                });
                const data = await res.json().catch(() => null);
                if (res.ok && data && data.status === 'ok') {
                    document.getElementById('settings-trusted-claude-ip').value = data.trustedMuseIp || '';
                    showToast(data.trustedMuseIp ? '許可アドレスを保存しました' : '許可アドレスを解除しました');
                } else {
                    showToast((data && data.message) || '許可アドレスの保存に失敗しました');
                }
            } catch (e) {
                showToast('許可アドレスの保存に失敗しました');
            }
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
            renderTemplateListHtml('settings-prompt-tpl-list', pcSettings.promptTemplates || [], 'prompt');
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

        function resetTemplateForm(category) {
            // テンプレートフォーム初期化用プレースホルダー
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
            openTemplateEditorModal(category, idx);
        }

        function openTemplateEditorModal(category, idx) {
            const list = category === 'title' ? (pcSettings.titleTemplates || []) : (category === 'prompt' ? (pcSettings.promptTemplates || []) : (pcSettings.descriptionTemplates || []));
            const tpl = (typeof idx === 'number' && idx >= 0) ? list[idx] : null;
            document.getElementById('tpl-editor-category').value = category;
            document.getElementById('tpl-editor-edit-id').value = tpl ? tpl.id : '';
            document.getElementById('tpl-editor-name').value = tpl ? tpl.name : '';
            document.getElementById('tpl-editor-content').value = tpl ? tpl.content : '';
            document.getElementById('tpl-editor-title').innerText = tpl
                ? (category === 'title' ? 'タイトルテンプレートを編集' : (category === 'prompt' ? '画像加工プロンプトを編集' : '商品説明テンプレートを編集'))
                : (category === 'title' ? 'タイトルテンプレートを追加' : (category === 'prompt' ? '画像加工プロンプトを追加' : '商品説明テンプレートを追加'));
            updateTemplateEditorCharCount();
            document.getElementById('template-editor-modal').classList.remove('hidden');
        }

        function closeTemplateEditorModal() {
            document.getElementById('template-editor-modal').classList.add('hidden');
        }

        function updateTemplateEditorCharCount() {
            const len = (document.getElementById('tpl-editor-content')?.value || '').length;
            const el = document.getElementById('tpl-editor-char-count');
            if (el) el.innerText = len + '文字';
        }

        async function saveTemplateFromEditorModal() {
            const category = document.getElementById('tpl-editor-category').value || 'desc';
            const editId = document.getElementById('tpl-editor-edit-id').value.trim();
            const name = document.getElementById('tpl-editor-name').value.trim();
            const content = document.getElementById('tpl-editor-content').value;
            if (!name || !content) {
                alert('表示名と挿入テキストを入力してください');
                return;
            }
            const currentList = category === 'title' ? (pcSettings.titleTemplates || []) : (category === 'prompt' ? (pcSettings.promptTemplates || []) : (pcSettings.descriptionTemplates || []));
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
                descriptionTemplates: category === 'desc' ? nextList : (pcSettings.descriptionTemplates || []),
                promptTemplates: category === 'prompt' ? nextList : (pcSettings.promptTemplates || [])
            };
            const ok = await savePcSettingsPayload(next, 'テンプレートを保存しました');
            if (ok) closeTemplateEditorModal();
        }

        async function deleteTemplateByIndex(category, idx) {
            const currentList = category === 'title' ? (pcSettings.titleTemplates || []) : (category === 'prompt' ? (pcSettings.promptTemplates || []) : (pcSettings.descriptionTemplates || []));
            const target = currentList[idx];
            if (!target || !confirm('テンプレート「' + target.name + '」を削除しますか？')) return;
            const nextList = currentList.filter((_, i) => i !== idx);
            const next = {
                ...pcSettings,
                titleTemplates: category === 'title' ? nextList : (pcSettings.titleTemplates || []),
                descriptionTemplates: category === 'desc' ? nextList : (pcSettings.descriptionTemplates || []),
                promptTemplates: category === 'prompt' ? nextList : (pcSettings.promptTemplates || [])
            };
            await savePcSettingsPayload(next, 'テンプレートを削除しました');
        }

        function updateStatusBannerFromData(statusData) {
            if (!statusData) return;
            const statusElem = document.getElementById('stat-connection-status');
            const photoCountElem = document.getElementById('stat-photo-index-count');
            const missingCount = statusData.missingFileCount || 0;
            const missingBadge = missingCount > 0 ? ' (欠損: ' + missingCount + ')' : '';

            if (photoCountElem) {
                photoCountElem.innerText = (statusData.indexedPhotosCount || 0) + '枚' + missingBadge;
            }
            if (statusElem) {
                if (statusData.appConnected) {
                    statusElem.className = 'font-bold text-[11px] text-emerald-700 bg-emerald-50 border border-emerald-200 rounded-full px-2 py-0.5 flex items-center gap-1 shrink-0 whitespace-nowrap';
                    statusElem.innerHTML = `<i class='fa-solid fa-circle-check text-emerald-600'></i> 接続中`;
                } else {
                    statusElem.className = 'font-bold text-[11px] text-amber-700 bg-amber-50 border border-amber-200 rounded-full px-2 py-0.5 flex items-center gap-1 shrink-0 whitespace-nowrap';
                    statusElem.innerHTML = `<i class='fa-solid fa-circle-exclamation text-amber-500'></i> 未接続`;
                }
            }
        }

        async function fetchItemsOnly() {
            try {
                const res = await fetch('/api/items', { headers: getHeaders() });
                if (!res.ok) return;
                allItems = await res.json();
                updateStats();
                renderItems();

                const viewerModal = document.getElementById('photo-viewer-modal');
                if (currentViewerItemId && viewerModal && !viewerModal.classList.contains('hidden')) {
                    renderViewerPhotos();
                }

                const lightboxModal = document.getElementById('photo-lightbox-modal');
                if (currentViewerItemId && lightboxModal && !lightboxModal.classList.contains('hidden')) {
                    const currentItem = allItems.find(i => i.id === currentViewerItemId);
                    if (!currentItem || !currentItem.photos || currentItem.photos.length === 0) {
                        closePhotoLightbox();
                    } else {
                        const prevHash = (lightboxPhotos && lightboxPhotos[lightboxIndex]) ? lightboxPhotos[lightboxIndex].md5Hash : '';
                        lightboxPhotos = currentItem.photos;
                        if (prevHash) {
                            const foundIdx = lightboxPhotos.findIndex(p => p.md5Hash === prevHash);
                            if (foundIdx >= 0) lightboxIndex = foundIdx;
                        }
                        if (lightboxIndex >= lightboxPhotos.length) lightboxIndex = lightboxPhotos.length - 1;
                        if (lightboxIndex < 0) lightboxIndex = 0;
                        updateLightboxView();
                    }
                }
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
                const srvLabelSeenRev = typeof statusData.labelSeenRevision === 'number' ? statusData.labelSeenRevision : null;
                if (srvLabelSeenRev !== null && srvLabelSeenRev !== lastKnownLabelSeenRevision) {
                    fetchClaudeJobs();
                }
                const srvClaudeRev = typeof statusData.claudeRevision === 'number' ? statusData.claudeRevision : null;
                if (srvClaudeRev !== null && srvClaudeRev !== lastKnownClaudeRevision) {
                    lastKnownClaudeRevision = srvClaudeRev;
                    refreshClaudeJobsCount();
                    const jobsModal = document.getElementById('claude-jobs-modal');
                    if (jobsModal && !jobsModal.classList.contains('hidden')) {
                        refreshClaudeJobsList();
                    }
                }
                if (srvDataRev === null || srvDataRev !== lastKnownDataRevision) {
                    lastKnownDataRevision = srvDataRev;
                    await fetchItemsOnly();
                    fetchCleanupPreview();
                    refreshPhotoTrashCount();
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
                    if (typeof statusData.claudeRevision === 'number') lastKnownClaudeRevision = statusData.claudeRevision;
                }
            } catch (e) {}

            fetchCleanupPreview();
            refreshPhotoTrashCount();
            refreshClaudeJobsCount();
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

        // フェーズ 6-4: ステータス共通ヘルパー (サーバー側の統一表と一致させる)
        function isSoldFamilyStatus(status) { return status === 'SOLD' || status === 'SHIPPING'; }
        function statusLabelOf(status) {
            switch (status) {
                case 'DRAFT': return '下書き中';
                case 'DRAFT_DONE': return '下書き済み';
                case 'LISTED': return '出品中';
                case 'SHIPPING': return '発送準備中';
                case 'SOLD': return '売却済み';
                default: return status || '';
            }
        }
        function statusBadgeClassOf(status) {
            switch (status) {
                case 'DRAFT': return 'bg-slate-500 text-white';
                case 'DRAFT_DONE': return 'bg-teal-600 text-white';
                case 'LISTED': return 'bg-emerald-600 text-white';
                case 'SHIPPING': return 'bg-amber-600 text-white';
                case 'SOLD': return 'bg-rose-600 text-white';
                case 'MERGED': return 'bg-purple-600 text-white';
                default: return 'bg-slate-700 text-white';
            }
        }
        function isMergeEligibleStatus(status) { return status === 'DRAFT' || status === 'DRAFT_DONE' || status === 'LISTED'; }

        function updateStats() {
            // MERGED（まとめ内包済み）子商品とゴミ箱商品は通常有効件数から除外
            const active = allItems.filter(i => !isItemTrashed(i) && i.status !== 'MERGED' && !(i.mergedParentId && i.mergedParentId > 0));
            const trashed = allItems.filter(i => isItemTrashed(i) && !(i.status === 'MERGED' && i.mergedParentId && i.mergedParentId > 0));
            const listed = active.filter(i => i.status === 'LISTED');
            const draft = active.filter(i => i.status === 'DRAFT' || i.status === 'DRAFT_DONE');
            const shipping = active.filter(i => i.status === 'SHIPPING');
            const sold = active.filter(i => i.status === 'SOLD');
            // レビュー §30.4-5: 売上集計は売却系 (SOLD + SHIPPING)、フィルタ件数は状態ごとに分離する
            const soldFamily = active.filter(i => isSoldFamilyStatus(i.status));

            const totalSales = soldFamily.reduce((sum, i) => sum + (i.soldPrice > 0 ? i.soldPrice : (i.price || 0)), 0);
            const totalProfit = soldFamily.reduce((sum, i) => sum + (i.profit || 0), 0);

            let totalFee = 0;
            let totalShipping = 0;
            let totalExpense = 0;

            soldFamily.forEach(i => {
                const sp = i.soldPrice > 0 ? i.soldPrice : (i.price || 0);
                const feeRate = i.feeRatePercent != null ? i.feeRatePercent : pcSettings.defaultFeeRatePercent;
                const shipping = i.shippingCost != null ? i.shippingCost : pcSettings.defaultShippingCost;
                const expense = i.expenseCost != null ? i.expenseCost : pcSettings.defaultExpenseCost;
                totalFee += Math.floor(sp * feeRate / 100);
                totalShipping += shipping;
                totalExpense += expense;
            });

            const margin = totalSales > 0 ? ((totalProfit / totalSales) * 100).toFixed(1) : '0.0';
            const avgPrice = soldFamily.length > 0 ? Math.round(totalSales / soldFamily.length) : 0;

            document.getElementById('stat-listed-count').innerText = listed.length + '品';
            document.getElementById('stat-sold-count').innerText = soldFamily.length + '品';
            document.getElementById('stat-total-sales').innerText = '¥' + totalSales.toLocaleString();
            document.getElementById('stat-total-profit').innerText = '¥' + totalProfit.toLocaleString();
            document.getElementById('stat-profit-margin').innerText = margin + '%';
            document.getElementById('stat-avg-price').innerText = '¥' + avgPrice.toLocaleString();

            document.getElementById('stat-total-fee').innerText = '¥' + totalFee.toLocaleString();
            document.getElementById('stat-total-shipping').innerText = '¥' + totalShipping.toLocaleString();
            document.getElementById('stat-total-expense').innerText = '¥' + totalExpense.toLocaleString();

            document.getElementById('count-all').innerText = active.length;
            document.getElementById('count-draft').innerText = draft.length;
            document.getElementById('count-listed').innerText = listed.length;
            document.getElementById('count-shipping').innerText = shipping.length;
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
            if (filter === 'DRAFT') document.getElementById('filter-draft').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-amber-700 shadow-sm transition';
            if (filter === 'SHIPPING') document.getElementById('filter-shipping').className = 'filter-btn px-2 py-0.5 text-xs font-bold rounded bg-white text-amber-700 shadow-sm transition';
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
            if (selectedItemIds.size > 0) {
                selectedItemIds.clear();
                updateSelectionBar();
            }
            renderItems();
        }

        function toggleSelect(id) {
            const item = allItems.find(i => i.id === id);
            if (!item) return;
            const trashed = isItemTrashed(item);
            if (currentFilter === 'TRASH') {
                if (!trashed) return;
            } else {
                if (trashed || item.status === 'MERGED') return;
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
            const activeActions = document.getElementById('floating-actions-active');
            const trashActions = document.getElementById('floating-actions-trash');
            const mergeBtn = document.getElementById('floating-merge-btn');

            if (selectedItemIds.size > 0) {
                const selectedItems = allItems.filter(i => selectedItemIds.has(i.id));
                countElem.innerText = selectedItems.length;
                if (currentFilter === 'TRASH') {
                    activeActions.classList.add('hidden');
                    trashActions.classList.remove('hidden');
                } else {
                    trashActions.classList.add('hidden');
                    activeActions.classList.remove('hidden');
                    const canMerge = selectedItems.length >= 2 && selectedItems.every(i => isMergeEligibleStatus(i.status));
                    if (mergeBtn) {
                        mergeBtn.disabled = !canMerge;
                        mergeBtn.title = canMerge ? '選択した商品をまとめる' : '下書き・出品中から2件以上を選択するとまとめられます';
                    }
                }
                bar.classList.remove('hidden');
            } else {
                bar.classList.add('hidden');
            }
        }

        async function batchTrashSelected() {
            const ids = Array.from(selectedItemIds);
            if (ids.length === 0) return;
            if (!confirm('選択中の ' + ids.length + '件をゴミ箱へ移動しますか？（30日間保持され復元可能です）')) return;
            try {
                const res = await fetch('/api/items/batch-trash', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ itemIds: ids })
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                clearSelection();
                await fetchItems();
                showToast(ids.length + '件をゴミ箱へ移動しました');
            } catch (e) {
                alert('一括ゴミ箱移動に失敗しました: ' + e.message);
            }
        }

        async function batchRestoreSelected() {
            const ids = Array.from(selectedItemIds);
            if (ids.length === 0) return;
            try {
                const res = await fetch('/api/items/batch-restore', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ itemIds: ids })
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                clearSelection();
                await fetchItems();
                showToast(ids.length + '件を復元しました');
            } catch (e) {
                alert('一括復元に失敗しました: ' + e.message);
            }
        }

        async function batchPermanentDeleteSelected() {
            const ids = Array.from(selectedItemIds);
            if (ids.length === 0) return;
            if (!confirm('選択中の ' + ids.length + '件を完全に削除しますか？この操作は元に戻せません。')) return;
            try {
                const res = await fetch('/api/items/batch-permanent', {
                    method: 'DELETE',
                    headers: getHeaders(),
                    body: JSON.stringify({ itemIds: ids })
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                clearSelection();
                await fetchItems();
                showToast(ids.length + '件を完全削除しました');
            } catch (e) {
                alert('一括完全削除に失敗しました: ' + e.message);
            }
        }

        function openBatchEditModal() {
            const ids = Array.from(selectedItemIds);
            if (ids.length === 0) return;
            document.getElementById('batch-edit-count').innerText = ids.length;
            document.getElementById('batch-enable-status').checked = false;
            document.getElementById('batch-enable-preset').checked = false;
            document.getElementById('batch-enable-price').checked = false;

            const sel = document.getElementById('batch-preset-select');
            const presets = pcSettings.presets || [];
            sel.innerHTML = `<option value='__DEFAULT__'>基本設定に従う (${pcSettings.defaultFeeRatePercent}% / 送料¥${pcSettings.defaultShippingCost} / 経費¥${pcSettings.defaultExpenseCost})</option>` +
                presets.map((p, idx) => `<option value='${idx}'>${escapeHtml(p.name)} (${p.feeRatePercent}% / 送料¥${p.shippingCost} / 経費¥${p.expenseCost})</option>`).join('');

            updateBatchEditPreview();
            document.getElementById('batch-edit-modal').classList.remove('hidden');
        }

        function closeBatchEditModal() {
            document.getElementById('batch-edit-modal').classList.add('hidden');
        }

        function updateBatchEditPreview() {
            const enableStatus = document.getElementById('batch-enable-status').checked;
            const enablePreset = document.getElementById('batch-enable-preset').checked;
            const enablePrice = document.getElementById('batch-enable-price').checked;
            const btn = document.getElementById('batch-edit-submit-btn');
            const preview = document.getElementById('batch-edit-preview');
            const hasAny = enableStatus || enablePreset || enablePrice;
            if (btn) btn.disabled = !hasAny;

            const selectedItems = allItems.filter(i => selectedItemIds.has(i.id));
            const sample = selectedItems[0];
            if (!hasAny || !sample) {
                preview.classList.add('hidden');
                return;
            }
            const beforeStatus = statusLabelOf(sample.status);
            const targetStatus = enableStatus ? document.getElementById('batch-status-value').value : sample.status;
            const afterStatus = statusLabelOf(targetStatus);

            const oldPrice = sample.price || 0;
            let newPrice = oldPrice;
            if (enablePrice) {
                const mode = document.getElementById('batch-price-mode').value;
                const val = parseInt(document.getElementById('batch-price-value').value || '0', 10) || 0;
                if (mode === 'FIXED') newPrice = Math.max(0, val);
                else if (mode === 'DELTA_YEN') newPrice = Math.max(0, oldPrice + val);
                else if (mode === 'DELTA_PERCENT') newPrice = Math.max(0, oldPrice + Math.floor((oldPrice * val) / 100));
            }
            preview.innerHTML = `<p class='font-bold'>変更サンプル (先頭1件: ${escapeHtml(sample.title || '無題')})</p>` +
                `<p>状態: ${beforeStatus} ➔ ${afterStatus} ／ 価格: ¥${oldPrice.toLocaleString()} ➔ ¥${newPrice.toLocaleString()}</p>`;
            preview.classList.remove('hidden');
        }

        async function submitBatchEdit() {
            const ids = Array.from(selectedItemIds);
            if (ids.length === 0) return;
            const enableStatus = document.getElementById('batch-enable-status').checked;
            const enablePreset = document.getElementById('batch-enable-preset').checked;
            const enablePrice = document.getElementById('batch-enable-price').checked;
            if (!enableStatus && !enablePreset && !enablePrice) return;

            let useCustomSettings = false;
            let feeRatePercent = pcSettings.defaultFeeRatePercent;
            let shippingCost = pcSettings.defaultShippingCost;
            let expenseCost = pcSettings.defaultExpenseCost;
            if (enablePreset) {
                const pVal = document.getElementById('batch-preset-select').value;
                if (pVal !== '__DEFAULT__') {
                    const preset = (pcSettings.presets || [])[parseInt(pVal, 10)];
                    if (preset) {
                        useCustomSettings = true;
                        feeRatePercent = preset.feeRatePercent;
                        shippingCost = preset.shippingCost;
                        expenseCost = preset.expenseCost;
                    }
                }
            }

            const payload = {
                itemIds: ids,
                newStatus: enableStatus ? document.getElementById('batch-status-value').value : null,
                applyCostPreset: enablePreset,
                useCustomSettings,
                feeRatePercent,
                shippingCost,
                expenseCost,
                priceMode: enablePrice ? document.getElementById('batch-price-mode').value : 'NONE',
                priceValue: enablePrice ? (parseInt(document.getElementById('batch-price-value').value || '0', 10) || 0) : 0
            };

            try {
                const res = await fetch('/api/items/batch-update', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify(payload)
                });
                if (!res.ok) throw new Error('HTTP ' + res.status);
                closeBatchEditModal();
                clearSelection();
                await fetchItems();
                showToast(ids.length + '件を一括更新しました');
            } catch (e) {
                alert('一括更新に失敗しました: ' + e.message);
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
                if (isTrashView) {
                    if (!trashed) return false;
                    if (item.status === 'MERGED' && item.mergedParentId && item.mergedParentId > 0) return false;
                } else {
                    if (trashed) return false;
                    // まとめ内包済み(MERGED)子商品は個別カードとしては非表示（親まとめ商品内に集約表示）
                    if (item.status === 'MERGED' || (item.mergedParentId && item.mergedParentId > 0)) return false;
                    if (currentFilter === 'LISTED' && item.status !== 'LISTED') return false;
                    if (currentFilter === 'DRAFT' && item.status !== 'DRAFT' && item.status !== 'DRAFT_DONE') return false;
                    if (currentFilter === 'SHIPPING' && item.status !== 'SHIPPING') return false;
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
                    if (isB && item.status === 'SHIPPING') return 2;
                    if (!isB && item.status === 'LISTED') return 3;
                    if (!isB && item.status === 'SHIPPING') return 4;
                    if (isB && item.status === 'DRAFT_DONE') return 5;
                    if (isB && item.status === 'DRAFT') return 6;
                    if (!isB && item.status === 'DRAFT_DONE') return 7;
                    if (!isB && item.status === 'DRAFT') return 8;
                    return 9;
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
                const displayPrice = isSoldFamilyStatus(item.status) && item.soldPrice > 0 ? item.soldPrice : (item.price || 0);
                const isSold = item.status === 'SOLD';
                const isSoldFam = isSoldFamilyStatus(item.status);
                const isDraft = item.status === 'DRAFT';
                const isBundle = checkIsBundle(item);
                const isMergedChild = item.status === 'MERGED';
                const trashed = isItemTrashed(item);
                const remainingDays = trashed ? getTrashRemainingDays(item) : 30;
                const profitText = item.profit != null ? ' (利益 ¥' + item.profit.toLocaleString() + ')' : '';
                const titleLen = (item.title || '').length;
                const isChecked = selectedItemIds.has(item.id);

                const itemWarnings = (!trashed && !isSoldFam && !isMergedChild)
                    ? evaluateListingWarningsObj({
                        title: item.title || '',
                        description: item.description || '',
                        photoCount: photos.length,
                        price: item.price || 0,
                        profit: (item.profit != null) ? item.profit : ((item.price || 0) - Math.floor((item.price || 0) * ((item.useCustomSettings && item.feeRatePercent != null) ? item.feeRatePercent : pcSettings.defaultFeeRatePercent) / 100) - ((item.useCustomSettings && item.shippingCost != null) ? item.shippingCost : pcSettings.defaultShippingCost) - ((item.useCustomSettings && item.expenseCost != null) ? item.expenseCost : pcSettings.defaultExpenseCost)),
                        status: item.status || 'LISTED'
                    })
                    : [];
                const itemWarningsCritical = itemWarnings.filter(w => w.critical);
                const itemWarningsCriticalCount = itemWarningsCritical.length;
                const itemWarningsTitle = itemWarningsCritical.map(w => w.text).join(' / ');

                return `<div data-item-card-id='${item.id}' class='bg-white rounded-lg border ${trashed ? 'border-rose-200 bg-rose-50/20' : (isChecked ? 'border-indigo-500 ring-2 ring-indigo-300' : 'border-slate-200')} overflow-hidden shadow-sm hover:shadow-md transition flex flex-col'>
                    <!-- サムネイル外の選択チェックボックスバー -->
                    <div class='px-2.5 py-1 bg-slate-50 border-b border-slate-100 flex items-center justify-between text-[11px]'>
                        <label class='flex items-center gap-1.5 cursor-pointer font-bold text-slate-700 hover:text-indigo-600' onclick='event.stopPropagation()'>
                            <input type='checkbox' class='w-3.5 h-3.5 text-indigo-600 rounded border-slate-300 focus:ring-indigo-500 ${isMergedChild ? 'opacity-40 cursor-not-allowed' : ''}' onchange='toggleSelect(${item.id})' ${isChecked ? 'checked' : ''} ${isMergedChild ? 'disabled' : ''}>
                            <span class='font-mono text-slate-500'>#${item.id}</span>
                        </label>
                        <div class='flex items-center gap-1'>
                            ${trashed ? `<span class='bg-rose-100 text-rose-700 text-[10px] px-1.5 py-0.2 rounded-full font-bold'>完全削除まであと ${remainingDays}日</span>` : ''}
                            ${!trashed && !item.isShared ? `<span class='bg-slate-200 text-slate-700 text-[9px] px-1.5 py-0.2 rounded-full font-bold'>未同期</span>` : ''}
                            ${isBundle ? `<span class='bg-purple-100 text-purple-800 text-[10px] px-1.5 py-0.2 rounded-full font-bold flex items-center gap-1'><i class='fa-solid fa-gift'></i> まとめ (${item.mergedChildCount || 2}点)</span>` : ''}
                            ${isMergedChild ? `<span class='bg-purple-50 text-purple-600 text-[10px] px-1.5 py-0.2 rounded-full font-bold'>まとめ内包済</span>` : ''}
                            ${(!trashed && !isSoldFam && !isMergedChild && itemWarningsCriticalCount > 0) ? `<span class='bg-orange-100 text-orange-800 text-[9px] px-1.5 py-0.2 rounded-full font-bold flex items-center gap-0.5' title='${escapeHtml(itemWarningsTitle)}'><i class='fa-solid fa-triangle-exclamation'></i> 要確認(${itemWarningsCriticalCount})</span>` : ''}
                        </div>
                    </div>

                    <!-- サムネイル画像 -->
                    <div class='relative aspect-square bg-slate-100 overflow-hidden cursor-pointer group' onclick='openPhotoViewer(${item.id})'>
                        ${mainPhoto ? `<img src='${mainPhoto}' draggable='true' ondragstart='handlePhotoDragStart(event, ""${mainPhoto}"")' class='w-full h-full object-cover group-hover:scale-105 transition duration-200 ${trashed ? 'opacity-80' : ''}'>` : `<div class='w-full h-full flex items-center justify-center text-slate-300'><i class='fa-solid fa-image text-3xl'></i></div>`}
                        <div class='absolute top-1.5 left-1.5 flex gap-1 flex-wrap'>
                            ${trashed
                                ? `<span class='px-2 py-0.5 rounded text-xs font-black shadow-sm bg-rose-700 text-white'>ゴミ箱</span>`
                                : `<span class='px-2 py-0.5 rounded text-xs font-black shadow-sm ${statusBadgeClassOf(item.status)}'>
                                    ${isSold ? '売却済' : (isMergedChild ? 'まとめ' : statusLabelOf(item.status))}
                                   </span>`
                            }
                            ${!trashed ? claudeItemLabelChip(item) : ''}
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
                            <button onclick='copyText(${JSON.stringify(item.title || '')}, ""タイトル"", this)' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='タイトルをコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 題名
                            </button>
                            <button onclick='copyText(${JSON.stringify(item.description || '')}, ""商品説明"", this)' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='商品説明をコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 説明
                            </button>
                            <button onclick='copyText(${JSON.stringify(displayPrice.toString())}, ""価格"", this)' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-0.5 rounded font-bold transition flex items-center justify-center gap-1' title='価格をコピー'>
                                <i class='fa-regular fa-copy text-indigo-600'></i> 価格
                            </button>
                            <button onclick='openPhotoViewer(${item.id})' class='bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 text-indigo-700 px-1.5 py-0.5 rounded font-bold transition flex items-center justify-center' title='写真ギャラリー / 出品画像ドラッグ'>
                                <i class='fa-solid fa-images'></i>
                            </button>
                        </div>` : ''}

                        <div class='border-t border-slate-100 pt-1.5 flex items-center justify-between gap-1'>
                            <div>
                                <span class='text-base font-black ${isSoldFam ? 'text-rose-600' : 'text-indigo-600'}'>¥${displayPrice.toLocaleString()}</span>
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
            // 開いた時点の既読状態を保持し、表示中は加工済みを確認できるようにする (32-2)。
            // 既読の記録は直後に行い、背後のカードには最新状態が反映される。
            viewerSessionSeenMap = Object.assign({}, labelSeenAtByItemUuid);
            markClaudeLabelSeen(item);

            currentViewerItemId = itemId;
            currentViewerTab = 'group';
            selectedViewerPhotoIndices.clear();

            document.getElementById('viewer-title').innerText = '写真ギャラリー: ' + (item.title || '無題');
            const isBundle = checkIsBundle(item);
            const tabsBar = document.getElementById('viewer-tabs-bar');

            if (isBundle) {
                tabsBar.classList.remove('hidden');
                document.getElementById('viewer-subtitle').innerText = 'まとめ商品 (' + (item.mergedChildCount || '複数') + '点同梱) - プレビュー・送り戻し・1枚削除（写真ゴミ箱）が可能です';
            } else {
                tabsBar.classList.add('hidden');
                document.getElementById('viewer-subtitle').innerText = '商品写真のプレビュー・原寸送り戻し・1枚削除（写真ゴミ箱）が可能です';
            }

            const scanBtn = document.getElementById('viewer-scan-folder-btn');
            if (scanBtn) {
                scanBtn.onclick = () => scanItemFolderPhotos(itemId);
            }
            const folderBtn = document.getElementById('viewer-open-folder-btn');
            folderBtn.onclick = () => openFolder(itemId);

            renderViewerPhotos();
            refreshPhotoTrashCount();
            document.getElementById('photo-viewer-modal').classList.remove('hidden');
        }

        function closePhotoViewer() {
            closePhotoLightbox();
            document.getElementById('photo-viewer-modal').classList.add('hidden');
            viewerSessionSeenMap = null;
            currentViewerItemId = null;
            selectedViewerPhotoIndices.clear();
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

        function updateViewerSelectionBar(totalCount) {
            const countBadge = document.getElementById('viewer-photo-count-badge');
            const selCountSpan = document.getElementById('viewer-selected-count');
            const trashBtn = document.getElementById('viewer-trash-selected-btn');
            if (countBadge) countBadge.innerText = totalCount + '枚';
            const selSize = selectedViewerPhotoIndices.size;
            if (selCountSpan) selCountSpan.innerText = selSize;
            if (trashBtn) trashBtn.disabled = (selSize === 0);

            const claudeCountSpan = document.getElementById('viewer-claude-selected-count');
            const claudeBtn = document.getElementById('viewer-claude-send-btn');
            const claudeSel = document.getElementById('viewer-claude-template');
            const claudeQueueSel = document.getElementById('viewer-claude-queue');
            const bundleNote = document.getElementById('viewer-claude-bundle-note');
            if (claudeCountSpan) claudeCountSpan.innerText = selSize;
            const viewerItem = allItems.find(i => i.id === currentViewerItemId);
            const isBundle = checkIsBundle(viewerItem);
            const hasTpl = (pcSettings.promptTemplates || []).length > 0;
            if (claudeBtn) claudeBtn.disabled = (selSize === 0 || isBundle || !hasTpl);
            if (claudeSel) claudeSel.disabled = (!hasTpl || isBundle);
            if (claudeQueueSel) claudeQueueSel.disabled = (!hasTpl || isBundle);
            if (bundleNote) bundleNote.classList.toggle('hidden', !isBundle);
        }

        function toggleViewerPhotoSelect(index, event) {
            if (event) event.stopPropagation();
            if (selectedViewerPhotoIndices.has(index)) {
                selectedViewerPhotoIndices.delete(index);
            } else {
                selectedViewerPhotoIndices.add(index);
            }
            const item = allItems.find(i => i.id === currentViewerItemId);
            updateViewerSelectionBar(item && item.photos ? item.photos.length : 0);
        }

        function toggleSelectAllViewerPhotos() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            const photos = (item && item.photos) ? item.photos : [];
            if (photos.length === 0) return;
            if (selectedViewerPhotoIndices.size >= photos.length) {
                selectedViewerPhotoIndices.clear();
            } else {
                photos.forEach((_, idx) => selectedViewerPhotoIndices.add(idx));
            }
            renderViewerPhotos();
        }

        let claudeJobsCache = [];

        // フェーズ 6-5 (是正 Build 8): 加工ラベル (導出は §27.1.1、既読は §27.1.2、実装条件は §27.4)
        let labelSeenAtByItemUuid = {};
        let lastKnownLabelSeenRevision = 0;
        let claudeSeenSeq = 0;
        let viewerSessionSeenMap = null;
        let viewerUuidSet = [];
        const CLAUDE_LABEL_DONE_WINDOW_MS = 3 * 24 * 60 * 60 * 1000;
        const CLAUDE_LABEL_META = {
            error: { text: 'エラー', cls: 'bg-rose-600 text-white' },
            processing: { text: '加工中', cls: 'bg-blue-600 text-white' },
            approval: { text: '承認待ち', cls: 'bg-amber-400 text-amber-900' },
            waiting: { text: '待機中', cls: 'bg-slate-500 text-white' },
            done: { text: '加工済み', cls: 'bg-emerald-600 text-white' }
        };

        // まとめ配下の UUID 集合 (自商品 + 子孫すべて)。導出・スナップショット・既読化で共用する (34-1)
        function collectBundleUuidSet(item) {
            const result = new Set();
            const visitedIds = new Set();
            const queue = [item];
            while (queue.length) {
                const cur = queue.shift();
                if (!cur || visitedIds.has(cur.id)) continue;
                visitedIds.add(cur.id);
                if (cur.syncUuid) result.add(String(cur.syncUuid).toLowerCase());
                (cur.photos || []).forEach(p => {
                    if (p.childItemId) {
                        const child = allItems.find(i => i.id === p.childItemId);
                        if (child) queue.push(child);
                    }
                });
                allItems.forEach(i => { if (i.mergedParentId === cur.id) queue.push(i); });
            }
            return Array.from(result);
        }

        // 写真単位の導出。seenMap は加工済み判定に使う既読マップ (通常は最新、ビューア表示中はスナップショット)
        function claudePhotoLabel(uuidSet, md5, seenMap) {
            if (!uuidSet || !uuidSet.length || !md5) return null;
            const target = String(md5).toLowerCase();
            const set = new Set(uuidSet);
            const jobs = claudeJobsCache.filter(j => set.has(String(j.itemUuid || '').toLowerCase()));
            // エラー (supersede されていないものが1件でもあればエラー)
            for (const errJob of jobs) {
                if (errJob.status !== 'done_with_errors' || errJob.errorDismissed) continue;
                if (!(errJob.errors || []).some(e => String(e.sourceMd5 || '').toLowerCase() === target)) continue;
                const errTime = errJob.completedAt > 0 ? errJob.completedAt : errJob.createdAt;
                const superseded = jobs.some(ok =>
                    (ok.status === 'done' || ok.status === 'done_with_errors') && ok.completedAt > errTime &&
                    (ok.results || []).some(r => String(r.sourceMd5 || '').toLowerCase() === target));
                if (!superseded) return { key: 'error', text: CLAUDE_LABEL_META.error.text, cls: CLAUDE_LABEL_META.error.cls };
            }
            let hasProcessing = false, hasApproval = false, hasWaiting = false, hasDone = false;
            const now = Date.now();
            for (const job of jobs) {
                if ((job.photos || []).some(p => String(p.md5 || '').toLowerCase() === target)) {
                    if (job.status === 'approved' || job.status === 'processing') hasProcessing = true;
                    else if (job.status === 'approval_pending') hasApproval = true;
                    else if (job.status === 'queued') hasWaiting = true;
                }
                // 加工済みは生成物 (resultMd5) 基準で、所属チェックの外で判定する (32-1)
                if ((job.status === 'done' || job.status === 'done_with_errors') &&
                    (job.results || []).some(r => String(r.resultMd5 || '').toLowerCase() === target)) {
                    const seenAt = seenMap[String(job.itemUuid || '').toLowerCase()] || 0;
                    if (job.completedAt > seenAt && (now - job.completedAt) <= CLAUDE_LABEL_DONE_WINDOW_MS) hasDone = true;
                }
            }
            if (hasProcessing) return { key: 'processing', text: CLAUDE_LABEL_META.processing.text, cls: CLAUDE_LABEL_META.processing.cls };
            if (hasApproval) return { key: 'approval', text: CLAUDE_LABEL_META.approval.text, cls: CLAUDE_LABEL_META.approval.cls };
            if (hasWaiting) return { key: 'waiting', text: CLAUDE_LABEL_META.waiting.text, cls: CLAUDE_LABEL_META.waiting.cls };
            if (hasDone) return { key: 'done', text: CLAUDE_LABEL_META.done.text, cls: CLAUDE_LABEL_META.done.cls };
            return null;
        }

        function claudeItemLabel(item) {
            const uuidSet = collectBundleUuidSet(item);
            if (!uuidSet.length) return null;
            const order = { error: 0, processing: 1, approval: 2, waiting: 3, done: 4 };
            const counts = {};
            (item.photos || []).forEach(p => {
                const l = claudePhotoLabel(uuidSet, p.md5Hash, labelSeenAtByItemUuid);
                if (l) counts[l.key] = (counts[l.key] || 0) + 1;
            });
            const keys = Object.keys(counts).sort((a, b) => order[a] - order[b]);
            if (!keys.length) return null;
            const key = keys[0];
            const showCount = (key === 'error' || key === 'done') && counts[key] > 1;
            return { key: key, text: CLAUDE_LABEL_META[key].text + (showCount ? ' ×' + counts[key] : ''), cls: CLAUDE_LABEL_META[key].cls };
        }

        function claudeItemLabelChip(item) {
            const l = claudeItemLabel(item);
            if (!l) return '';
            return `<span class='px-2 py-0.5 rounded text-xs font-black shadow-sm ${l.cls}' title='Muse 加工状態'>${l.text}</span>`;
        }

        // 既読化は UUID 集合で一括送信し、サーバー確定の seenAt でローカルを確定させる (32-5 / 34-1)
        async function markClaudeLabelSeen(item) {
            const uuids = collectBundleUuidSet(item);
            if (!uuids.length) return;
            claudeSeenSeq++;
            // 応答が得られない場合のフォールバックは、対象商品の最大完了時刻以上へ丸める (§33.5 / 37-2)
            let serverSeenAt = Date.now();
            claudeJobsCache.forEach(j => {
                if (uuids.indexOf(String(j.itemUuid || '').toLowerCase()) >= 0 && j.completedAt > serverSeenAt) serverSeenAt = j.completedAt;
            });
            try {
                const res = await fetch('/api/claude/labels/seen', { method: 'POST', headers: getHeaders(), body: JSON.stringify({ itemUuids: uuids }) });
                if (res.ok) {
                    const data = await res.json();
                    if (typeof data.seenAt === 'number' && data.seenAt > 0) serverSeenAt = data.seenAt;
                    if (typeof data.labelSeenRevision === 'number') lastKnownLabelSeenRevision = Math.max(lastKnownLabelSeenRevision, data.labelSeenRevision);
                }
            } catch (e) {}
            uuids.forEach(u => { labelSeenAtByItemUuid[u] = serverSeenAt; });
            renderItems();
            if (currentViewerItemId) renderViewerPhotos();
        }

        function claudeJobStatusInfo(status, queue) {
            switch (status) {
                case 'queued': return queue === 'waiting'
                    ? { label: '待機中', cls: 'bg-slate-200 text-slate-700' }
                    : { label: '未依頼', cls: 'bg-slate-200 text-slate-700' };
                case 'approval_pending': return { label: '承認待ち', cls: 'bg-amber-200 text-amber-800' };
                case 'approved': return { label: '承認済み', cls: 'bg-sky-200 text-sky-800' };
                case 'processing': return { label: '処理中', cls: 'bg-indigo-200 text-indigo-800' };
                case 'done': return { label: '完了', cls: 'bg-emerald-200 text-emerald-800' };
                case 'done_with_errors': return { label: '一部エラー', cls: 'bg-amber-200 text-amber-800' };
                case 'denied': return { label: '拒否', cls: 'bg-rose-200 text-rose-800' };
                case 'approval_expired': return { label: '承認期限切れ', cls: 'bg-rose-200 text-rose-800' };
                case 'cancelled': return { label: '取消済み', cls: 'bg-slate-200 text-slate-500' };
                default: return { label: status || '', cls: 'bg-slate-200 text-slate-700' };
            }
        }

        async function fetchClaudeJobs() {
            const seqAtStart = claudeSeenSeq;
            try {
                const res = await fetch('/api/claude/jobs', { headers: getHeaders() });
                if (!res.ok) return;
                const data = await res.json();
                claudeJobsCache = Array.isArray(data.jobs) ? data.jobs : [];
                if (typeof data.claudeRevision === 'number') lastKnownClaudeRevision = data.claudeRevision;
                // 取得中に自端末の既読化が走っていない場合のみ、サーバーの既読状態を基準として採用する。
                // サーバー側カウンタが下がった場合でも取り逃がさない (37-1 と同種の対策 / 34-4-1)
                if (data.labelSeenAtByItemUuid && claudeSeenSeq === seqAtStart) {
                    labelSeenAtByItemUuid = data.labelSeenAtByItemUuid;
                    if (typeof data.labelSeenRevision === 'number') lastKnownLabelSeenRevision = data.labelSeenRevision;
                }
                renderItems();
                if (currentViewerItemId) renderViewerPhotos();
            } catch (e) {}
        }

        function countActiveClaudeJobs() {
            return claudeJobsCache.filter(j => ['queued', 'approval_pending', 'approved', 'processing'].includes(j.status) && !(j.status === 'queued' && j.queue === 'waiting')).length;
        }

        function refreshClaudeJobsCount() {
            fetchClaudeJobs().then(() => {
                const el = document.getElementById('viewer-claude-jobs-count');
                if (el) el.innerText = countActiveClaudeJobs();
            });
        }

        async function openClaudeJobsModal() {
            document.getElementById('claude-jobs-modal').classList.remove('hidden');
            await refreshClaudeJobsList();
        }

        function closeClaudeJobsModal() {
            document.getElementById('claude-jobs-modal').classList.add('hidden');
        }

        async function refreshClaudeJobsList() {
            await fetchClaudeJobs();
            renderClaudeJobsList();
            const el = document.getElementById('viewer-claude-jobs-count');
            if (el) el.innerText = countActiveClaudeJobs();
        }

        // フェーズ 6-6: ジョブ一覧のタブ分類 (待機中 = 待機キュー + エラー未解消の完了ジョブ。§27.2)
        let claudeJobsTab = 'active';

        function isClaudeWaitingJob(job) {
            return (job.queue === 'waiting' && job.status === 'queued') ||
                (job.status === 'done_with_errors' && !job.errorDismissed);
        }

        function isClaudeErrorJob(job) {
            return job.status === 'done_with_errors' && !job.errorDismissed;
        }

        function switchClaudeJobsTab(tab) {
            claudeJobsTab = tab;
            renderClaudeJobsList();
        }

        // ジョブ行から対象商品のカードへ移動する (まとめ子は親カードへ)
        function jumpToClaudeJobItem(itemId) {
            if (!itemId) return;
            let item = allItems.find(i => i.id === itemId);
            if (!item) { showToast('商品が見つかりません'); return; }
            if ((item.status === 'MERGED' || (item.mergedParentId && item.mergedParentId > 0)) && item.mergedParentId) {
                const parent = allItems.find(i => i.id === item.mergedParentId);
                if (parent) item = parent;
            }
            closeClaudeJobsModal();
            let el = document.querySelector('[data-item-card-id="' + item.id + '"]');
            if (!el) {
                const searchInput = document.getElementById('search-input');
                if (searchInput) searchInput.value = '';
                setFilter('ALL');
                el = document.querySelector('[data-item-card-id="' + item.id + '"]');
            }
            if (!el) { showToast('商品カードを表示できませんでした'); return; }
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            el.classList.add('ring-4', 'ring-violet-400');
            setTimeout(() => el.classList.remove('ring-4', 'ring-violet-400'), 2200);
        }

        async function dismissClaudeJobError(jobId) {
            try {
                const res = await fetch('/api/claude/jobs/' + jobId + '/dismiss-error', { method: 'POST', headers: getHeaders() });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '解消に失敗しました');
                showToast('エラーを解消済みにしました');
                await refreshClaudeJobsList();
                refreshClaudeJobsCount();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        function renderClaudeJobsList() {
            const container = document.getElementById('claude-jobs-list');
            if (!container) return;
            const waitingJobs = (claudeJobsCache || []).filter(isClaudeWaitingJob);
            const activeJobs = (claudeJobsCache || []).filter(j => !isClaudeWaitingJob(j));
            const tabA = document.getElementById('claude-jobs-tab-active');
            const tabW = document.getElementById('claude-jobs-tab-waiting');
            if (tabA) {
                tabA.innerText = '依頼中 (' + activeJobs.length + ')';
                tabA.className = 'px-3 py-1.5 text-xs font-bold rounded-lg transition ' + (claudeJobsTab === 'active' ? 'bg-violet-700 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200');
            }
            if (tabW) {
                tabW.innerText = '待機中 (' + waitingJobs.length + ')';
                tabW.className = 'px-3 py-1.5 text-xs font-bold rounded-lg transition ' + (claudeJobsTab === 'waiting' ? 'bg-violet-700 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200');
            }
            const jobs = (claudeJobsTab === 'waiting' ? waitingJobs : activeJobs).slice();
            if (claudeJobsTab === 'waiting') {
                // エラージョブを先頭に固定表示する
                jobs.sort((a, b) => (isClaudeErrorJob(b) ? 1 : 0) - (isClaudeErrorJob(a) ? 1 : 0));
            }
            if (jobs.length === 0) {
                container.innerHTML = claudeJobsTab === 'waiting'
                    ? `<p class='text-xs text-slate-400 text-center py-6'>待機中のジョブはありません。</p>`
                    : `<p class='text-xs text-slate-400 text-center py-6'>表示できるジョブがありません。ギャラリーで写真を選択し「Museに送る」を押してください。</p>`;
                return;
            }
            container.innerHTML = jobs.map(job => {
                const info = claudeJobStatusInfo(job.status, job.queue);
                const created = job.createdAt ? new Date(job.createdAt).toLocaleString('ja-JP') : '';
                const queueBadge = job.queue === 'patrol'
                    ? `<span class='text-[10px] font-bold px-1.5 py-0.5 rounded bg-teal-100 text-teal-800'>巡回</span>`
                    : (job.queue === 'manual'
                        ? `<span class='text-[10px] font-bold px-1.5 py-0.5 rounded bg-orange-100 text-orange-800'>手動</span>`
                        : '');
                const moveBtns = (job.status === 'queued' && job.queue === 'waiting')
                    ? `<button type='button' onclick='moveClaudeJob(""${job.jobId}"", ""patrol"")' class='px-2 py-1 bg-teal-600 hover:bg-teal-500 text-white rounded text-[11px] font-bold transition shrink-0'>巡回へ</button><button type='button' onclick='moveClaudeJob(""${job.jobId}"", ""manual"")' class='px-2 py-1 bg-orange-500 hover:bg-orange-400 text-white rounded text-[11px] font-bold transition shrink-0'>手動へ</button>`
                    : (((job.status === 'queued' && job.queue !== 'waiting') || job.status === 'approval_pending')
                        ? `<button type='button' onclick='moveClaudeJob(""${job.jobId}"", ""waiting"")' class='px-2 py-1 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded text-[11px] font-bold transition shrink-0'>待機へ</button>`
                        : '');
                const canCancel = job.status === 'queued' || job.status === 'approval_pending';
                const isDone = job.status === 'done' || job.status === 'done_with_errors';
                const unacked = isDone && !job.acknowledged;
                const ackMark = isDone
                    ? (job.acknowledged ? `<span class='text-[10px] text-emerald-600 font-bold'>確認済み</span>` : `<span class='text-[10px] text-amber-600 font-bold'>未確認</span>`)
                    : '';
                const countsText = isDone
                    ? ` / 成功 ${job.results ? job.results.length : 0}枚・失敗 ${job.errors ? job.errors.length : 0}枚`
                    : '';
                const errorJob = isClaudeErrorJob(job);
                const rowCls = errorJob
                    ? 'border-rose-300 bg-rose-50'
                    : (unacked ? 'border-amber-300 bg-amber-50' : 'border-slate-200 bg-white');
                const errorBlock = errorJob
                    ? `<div class='mt-1.5 space-y-0.5'>${(job.errors || []).map(e => `<p class='text-[11px] text-rose-700 font-bold'>⚠ ${escapeHtml(e.message || 'エラーが発生しました')}</p>`).join('')}</div>`
                    : '';
                return `<div class='flex items-center justify-between gap-2 p-2.5 rounded-lg border ${rowCls}'>
                    <div class='min-w-0 flex-1'>
                        <div class='flex items-center gap-2 flex-wrap'>
                            <span class='text-[10px] font-bold px-1.5 py-0.5 rounded ${info.cls}'>${info.label}</span>
                            ${queueBadge}
                            <span class='font-bold text-slate-800 truncate cursor-pointer hover:underline' title='商品カードへ移動' onclick='jumpToClaudeJobItem(${job.itemId || 0})'>${escapeHtml(job.itemTitle || '(無題)')}</span>
                            ${ackMark}
                        </div>
                        <p class='text-[11px] text-slate-500 mt-0.5'>写真 ${job.photos ? job.photos.length : 0}枚 / テンプレート「${escapeHtml(job.templateName || '')}」 / ${escapeHtml(job.origin === 'phone' ? 'スマホから' : 'PCから')} / ${escapeHtml(created)}${countsText}</p>
                        ${errorBlock}
                    </div>
                    ${moveBtns}
                    ${canCancel ? `<button type='button' onclick='cancelClaudeJob(""${job.jobId}"")' class='px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-600 rounded text-[11px] font-bold transition shrink-0'>取消</button>` : ''}
                    ${unacked ? `<button type='button' onclick='ackClaudeJob(""${job.jobId}"")' class='px-2 py-1 bg-violet-600 hover:bg-violet-500 text-white rounded text-[11px] font-bold transition shrink-0'>確認</button>` : ''}
                    ${errorJob ? `<button type='button' onclick='dismissClaudeJobError(""${job.jobId}"")' class='px-2 py-1 bg-rose-600 hover:bg-rose-500 text-white rounded text-[11px] font-bold transition shrink-0'>エラーを解消</button>` : ''}
                </div>`;
            }).join('');
        }

        async function moveClaudeJob(jobId, queue) {
            try {
                const res = await fetch('/api/claude/jobs/' + jobId + '/move', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ queue: queue })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '移動に失敗しました');
                showToast(queue === 'waiting' ? '待機リストへ移しました' : '送信しました（スマホに承認リクエストが届きます）');
                await refreshClaudeJobsList();
                refreshClaudeJobsCount();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        async function ackClaudeJob(jobId) {
            try {
                const res = await fetch('/api/claude/jobs/' + jobId + '/ack', { method: 'POST', headers: getHeaders() });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '確認に失敗しました');
                showToast('完了を確認しました（全端末で共有されます）');
                await refreshClaudeJobsList();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        async function cancelClaudeJob(jobId) {
            if (!confirm('このジョブを取り消しますか？')) return;
            try {
                const res = await fetch('/api/claude/jobs/' + jobId + '/cancel', { method: 'POST', headers: getHeaders() });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '取消に失敗しました');
                showToast('ジョブを取り消しました');
                await refreshClaudeJobsList();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        async function sendViewerPhotosToClaude() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            const photos = (item && item.photos) ? item.photos : [];
            if (!item || selectedViewerPhotoIndices.size === 0) return;
            if (checkIsBundle(item)) {
                showToast('まとめ商品は個別の子商品から送信してください');
                return;
            }
            const sel = document.getElementById('viewer-claude-template');
            const templateId = sel ? sel.value : '';
            if (!templateId) {
                showToast('プロンプトテンプレートが未登録です（設定画面で登録してください）');
                return;
            }
            // 送信直前の照合 (Q6): 選択中インデックスが現在の写真一覧の実在写真に対応するか確認する
            const targets = [];
            let mismatch = false;
            selectedViewerPhotoIndices.forEach(idx => {
                const ph = photos[idx];
                if (!ph || !ph.md5Hash) { mismatch = true; return; }
                targets.push({ md5: ph.md5Hash, fileName: ph.fileName || '' });
            });
            if (mismatch || targets.length === 0) {
                showToast('写真の選択状態が変わっています。選択し直してください');
                return;
            }
            const tpl = (pcSettings.promptTemplates || []).find(t => t.id === templateId);
            const tplName = tpl ? tpl.name : '';
            const queueSel = document.getElementById('viewer-claude-queue');
            const queue = queueSel ? queueSel.value : 'patrol';
            const queueLabel = queue === 'manual' ? '手動' : (queue === 'waiting' ? '待機' : '巡回');
            const listText = targets.map((t, i) => (i + 1) + '. ' + (t.fileName || t.md5)).join('\n');
            if (!confirm('「' + (item.title || '') + '」の写真 ' + targets.length + '枚を、テンプレート「' + tplName + '」でMuseに送ります。\n送り先: ' + queueLabel + (queue === 'waiting' ? '（承認リクエストは送られません）' : '（スマホに承認リクエストが届きます）') + '\n\n対象ファイル:\n' + listText)) return;

            try {
                const res = await fetch('/api/claude/jobs', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ itemId: item.id, itemUuid: item.syncUuid || '', photos: targets, templateId: templateId, origin: 'pc', queue: queue })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') {
                    throw new Error(data.message || '送信に失敗しました');
                }
                selectedViewerPhotoIndices.clear();
                updateViewerSelectionBar(photos.length);
                renderViewerPhotos();
                showToast(queue === 'waiting'
                    ? '待機リストへ送りました（' + targets.length + '枚）'
                    : 'Museジョブを作成しました（' + targets.length + '枚・' + queueLabel + '）スマホの承認をお待ちください');
                openClaudeJobsModal();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        async function trashSelectedViewerPhotos() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            const photos = (item && item.photos) ? item.photos : [];
            if (!item || selectedViewerPhotoIndices.size === 0) return;

            const targets = [];
            selectedViewerPhotoIndices.forEach(idx => {
                const p = photos[idx];
                if (p && p.md5Hash) {
                    targets.push({
                        itemId: item.id,
                        md5Hash: p.md5Hash,
                        childItemId: p.childItemId || 0
                    });
                }
            });
            if (targets.length === 0) {
                showToast('MD5ハッシュ未確定の写真は移動できません（先に同期してください）');
                return;
            }
            if (!confirm('選択した写真 ' + targets.length + '枚を写真ゴミ箱へ移動しますか？（写真ゴミ箱から復元可能です）')) return;

            try {
                const res = await fetch('/api/photos/trash-batch', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ targets })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') {
                    throw new Error(data.message || '削除に失敗しました');
                }
                selectedViewerPhotoIndices.clear();
                showToast((data.movedToTrashCount || targets.length) + '枚の写真を写真ゴミ箱へ移動しました');
                await fetchItems();
                refreshPhotoTrashCount();
                if (currentViewerItemId === item.id) {
                    renderViewerPhotos();
                }
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        function renderViewerPhotos() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            if (!item) return;
            viewerUuidSet = collectBundleUuidSet(item);

            const container = document.getElementById('viewer-photos-container');
            const photos = item.photos || [];
            document.getElementById('viewer-photo-count').innerText = photos.length + '枚の写真';

            // 存在しないインデックスを掃除
            Array.from(selectedViewerPhotoIndices).forEach(idx => {
                if (idx < 0 || idx >= photos.length) selectedViewerPhotoIndices.delete(idx);
            });
            updateViewerSelectionBar(photos.length);

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

        async function scanItemFolderPhotos(itemId) {
            const btn = document.getElementById('viewer-scan-folder-btn');
            const origHtml = btn ? btn.innerHTML : '';
            if (btn) {
                btn.disabled = true;
                btn.innerHTML = `<i class='fa-solid fa-spinner animate-spin'></i> 取込中...`;
            }
            try {
                const res = await fetch('/api/items/' + itemId + '/photos/scan', {
                    method: 'POST',
                    headers: getHeaders()
                });
                if (!res.ok) {
                    showToast('フォルダ取込に失敗しました (Status: ' + res.status + ')');
                    return;
                }
                const data = await res.json();
                await fetchItems();
                if (data && data.importedCount > 0) {
                    showToast(data.importedCount + '枚の新規写真を取り込みました');
                } else {
                    showToast('新規の未登録画像はありませんでした');
                }
            } catch (e) {
                showToast('フォルダ取込エラー: ' + e.message);
            } finally {
                if (btn) {
                    btn.disabled = false;
                    btn.innerHTML = origHtml;
                }
            }
        }

        function renderSinglePhotoCard(photo, index) {
            const url = photo.md5Hash ? '/api/photos/' + photo.md5Hash : photo.photoUrl;
            const photoLabel = claudePhotoLabel(viewerUuidSet, photo.md5Hash, viewerSessionSeenMap || labelSeenAtByItemUuid);
            const fileName = (photo.fileName || '').trim();
            const isChecked = selectedViewerPhotoIndices.has(index);
            return `<div class='bg-white border ${isChecked ? 'border-rose-500 ring-2 ring-rose-300' : 'border-slate-200'} rounded-lg overflow-hidden shadow-sm flex flex-col justify-between transition'>
                <div class='relative aspect-square bg-slate-100 overflow-hidden cursor-pointer group' onclick='openPhotoLightbox(${index})'>
                    <img src='${url}' draggable='true' ondragstart='handlePhotoDragStart(event, ""${url}"")' class='w-full h-full object-cover group-hover:scale-105 transition duration-200'>
                    <span class='absolute top-1 left-1 bg-black/65 text-white text-[9px] font-mono px-1.5 py-0.5 rounded pointer-events-none'>#${index + 1}</span>
                    <!-- ギャラリー開時限定チェックボックス (右上配置: サムネイルの左上以外) -->
                    <label onclick='event.stopPropagation()' class='absolute top-1 right-1 z-10 bg-black/75 hover:bg-black/95 p-1 rounded shadow cursor-pointer flex items-center justify-center border border-white/30' title='写真を選択（ゴミ箱移動・Muse送信）'>
                        <input type='checkbox' ${isChecked ? 'checked' : ''} onchange='toggleViewerPhotoSelect(${index}, event)' class='w-4 h-4 accent-rose-500 cursor-pointer'>
                    </label>
                    ${photoLabel ? `<span class='absolute bottom-1 left-1 ${photoLabel.cls} text-[9px] font-bold px-1.5 py-0.5 rounded pointer-events-none shadow'>${photoLabel.text}</span>` : ''}
                </div>
                ${fileName ? `<div class='px-1.5 py-0.5 bg-slate-100/90 border-t border-slate-100 text-[9px] font-mono text-slate-600 truncate' title='${escapeHtml(fileName)}'>${escapeHtml(fileName)}</div>` : ''}
                <div class='p-1.5 bg-slate-50 border-t border-slate-100 flex items-center gap-1 text-[10px]'>
                    <button type='button' onclick='copyPhotoBlob(""${url}"")' class='flex-1 bg-white hover:bg-slate-200 border border-slate-200 text-slate-700 py-1 rounded font-bold transition flex items-center justify-center gap-1' title='クリップボードに画像をコピー'>
                        <i class='fa-regular fa-copy text-indigo-600'></i> 画像コピー
                    </button>
                    <button type='button' onclick='openPhotoLightbox(${index})' class='bg-indigo-50 hover:bg-indigo-100 border border-indigo-200 text-indigo-700 px-2 py-1 rounded font-bold transition flex items-center justify-center' title='原寸表示 (送り・戻し)'>
                        <i class='fa-solid fa-expand'></i>
                    </button>
                </div>
            </div>`;
        }

        function openPhotoLightbox(startIndex) {
            const item = allItems.find(i => i.id === currentViewerItemId);
            if (!item || !item.photos || item.photos.length === 0) return;
            lightboxPhotos = item.photos;
            lightboxIndex = Math.max(0, Math.min(startIndex || 0, lightboxPhotos.length - 1));
            updateLightboxView();
            document.getElementById('photo-lightbox-modal').classList.remove('hidden');
        }

        function closePhotoLightbox() {
            const modal = document.getElementById('photo-lightbox-modal');
            if (modal) modal.classList.add('hidden');
        }

        function updateLightboxView() {
            if (!lightboxPhotos || lightboxPhotos.length === 0) {
                closePhotoLightbox();
                return;
            }
            if (lightboxIndex < 0) lightboxIndex = 0;
            if (lightboxIndex >= lightboxPhotos.length) lightboxIndex = lightboxPhotos.length - 1;

            const photo = lightboxPhotos[lightboxIndex];
            const url = photo.md5Hash ? '/api/photos/' + photo.md5Hash : photo.photoUrl;
            const item = allItems.find(i => i.id === currentViewerItemId);
            const titlePrefix = item ? (item.title || '無題') : '';
            const childSuffix = photo.childItemTitle ? (' / ' + photo.childItemTitle) : '';

            document.getElementById('lightbox-counter').innerText = (lightboxIndex + 1) + ' / ' + lightboxPhotos.length;
            document.getElementById('lightbox-title').innerText = titlePrefix + childSuffix;
            const fnEl = document.getElementById('lightbox-filename');
            if (fnEl) {
                const fn = (photo.fileName || '').trim();
                if (fn) {
                    fnEl.innerText = fn;
                    fnEl.title = fn;
                    fnEl.classList.remove('hidden');
                } else {
                    fnEl.innerText = '';
                    fnEl.classList.add('hidden');
                }
            }
            document.getElementById('lightbox-main-img').src = url;
            document.getElementById('lightbox-newtab-link').href = url;
            document.getElementById('lightbox-prev-btn').disabled = (lightboxIndex <= 0);
            document.getElementById('lightbox-next-btn').disabled = (lightboxIndex >= lightboxPhotos.length - 1);

            const strip = document.getElementById('lightbox-thumbs-strip');
            strip.innerHTML = lightboxPhotos.map((p, idx) => {
                const pUrl = p.md5Hash ? '/api/photos/' + p.md5Hash : p.photoUrl;
                const activeCls = (idx === lightboxIndex)
                    ? 'border-indigo-400 ring-2 ring-indigo-400/60 opacity-100 scale-105'
                    : 'border-slate-700 opacity-60 hover:opacity-100';
                return `<button type='button' onclick='selectLightboxPhoto(${idx})' class='relative w-12 h-12 rounded-md overflow-hidden border-2 shrink-0 transition ${activeCls}'>
                    <img src='${pUrl}' class='w-full h-full object-cover'>
                    <span class='absolute bottom-0 right-0 bg-black/70 text-white text-[8px] font-mono px-1'>${idx + 1}</span>
                </button>`;
            }).join('');
        }

        function selectLightboxPhoto(idx) {
            lightboxIndex = idx;
            updateLightboxView();
        }

        function prevLightboxPhoto() {
            if (lightboxIndex > 0) {
                lightboxIndex--;
                updateLightboxView();
            }
        }

        function nextLightboxPhoto() {
            if (lightboxIndex < lightboxPhotos.length - 1) {
                lightboxIndex++;
                updateLightboxView();
            }
        }

        function copyCurrentLightboxPhoto() {
            if (!lightboxPhotos || !lightboxPhotos[lightboxIndex]) return;
            const photo = lightboxPhotos[lightboxIndex];
            const url = photo.md5Hash ? '/api/photos/' + photo.md5Hash : photo.photoUrl;
            copyPhotoBlob(url);
        }

        async function trashCurrentLightboxPhoto() {
            const item = allItems.find(i => i.id === currentViewerItemId);
            if (!item || !lightboxPhotos || !lightboxPhotos[lightboxIndex]) return;
            const photo = lightboxPhotos[lightboxIndex];
            if (!photo.md5Hash) {
                showToast('MD5ハッシュ未確定の写真は移動できません');
                return;
            }
            if (!confirm('表示中のこの写真を写真ゴミ箱へ移動しますか？（写真ゴミ箱から復元可能です）')) return;

            try {
                const res = await fetch('/api/photos/trash-batch', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({
                        targets: [{
                            itemId: item.id,
                            md5Hash: photo.md5Hash,
                            childItemId: photo.childItemId || 0
                        }]
                    })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') {
                    throw new Error(data.message || '移動に失敗しました');
                }
                showToast('写真を写真ゴミ箱へ移動しました');
                selectedViewerPhotoIndices.clear();
                await fetchItems();
                refreshPhotoTrashCount();
                const updatedItem = allItems.find(i => i.id === currentViewerItemId);
                if (updatedItem && updatedItem.photos && updatedItem.photos.length > 0) {
                    lightboxPhotos = updatedItem.photos;
                    if (lightboxIndex >= lightboxPhotos.length) lightboxIndex = lightboxPhotos.length - 1;
                    updateLightboxView();
                    renderViewerPhotos();
                } else {
                    closePhotoLightbox();
                    if (updatedItem) renderViewerPhotos();
                }
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        function formatBytesSimple(bytes) {
            if (!bytes || bytes <= 0) return '0 B';
            const units = ['B', 'KB', 'MB', 'GB'];
            let val = bytes;
            let idx = 0;
            while (val >= 1024 && idx < units.length - 1) {
                val /= 1024;
                idx++;
            }
            return (idx === 0 ? val : val.toFixed(1)) + ' ' + units[idx];
        }

        async function refreshPhotoTrashCount() {
            try {
                const res = await fetch('/api/photo-trash', { headers: getHeaders() });
                if (!res.ok) return;
                const data = await res.json();
                const cnt = data.count || 0;
                const sEl = document.getElementById('sidebar-photo-trash-count');
                const vEl = document.getElementById('viewer-photo-trash-count');
                if (sEl) sEl.innerText = cnt;
                if (vEl) vEl.innerText = cnt;
            } catch (e) {}
        }

        async function openPhotoTrashModal() {
            selectedPhotoTrashIds.clear();
            document.getElementById('photo-trash-modal').classList.remove('hidden');
            await loadPhotoTrashEntries();
        }

        function closePhotoTrashModal() {
            document.getElementById('photo-trash-modal').classList.add('hidden');
            selectedPhotoTrashIds.clear();
        }

        async function loadPhotoTrashEntries() {
            const grid = document.getElementById('photo-trash-grid');
            grid.innerHTML = '<div class=\'col-span-full text-center py-8 text-slate-400 text-xs\'>読み込み中...</div>';
            try {
                const res = await fetch('/api/photo-trash', { headers: getHeaders() });
                const data = await res.json();
                photoTrashEntries = data.entries || [];
                const totalBytes = data.totalSizeBytes || 0;
                document.getElementById('photo-trash-summary-text').innerText = photoTrashEntries.length + '枚 (' + formatBytesSimple(totalBytes) + ')';
                const sEl = document.getElementById('sidebar-photo-trash-count');
                const vEl = document.getElementById('viewer-photo-trash-count');
                if (sEl) sEl.innerText = photoTrashEntries.length;
                if (vEl) vEl.innerText = photoTrashEntries.length;
                renderPhotoTrashGrid();
            } catch (e) {
                grid.innerHTML = '<div class=\'col-span-full text-center py-8 text-rose-500 text-xs\'>読み込みに失敗しました</div>';
            }
        }

        function updatePhotoTrashButtons() {
            const cnt = selectedPhotoTrashIds.size;
            document.getElementById('photo-trash-selected-count').innerText = cnt;
            document.getElementById('photo-trash-restore-btn').disabled = (cnt === 0);
            document.getElementById('photo-trash-perm-btn').disabled = (cnt === 0);
        }

        function togglePhotoTrashSelect(trashId, event) {
            if (event) event.stopPropagation();
            if (selectedPhotoTrashIds.has(trashId)) {
                selectedPhotoTrashIds.delete(trashId);
            } else {
                selectedPhotoTrashIds.add(trashId);
            }
            updatePhotoTrashButtons();
        }

        function toggleSelectAllPhotoTrash() {
            if (photoTrashEntries.length === 0) return;
            if (selectedPhotoTrashIds.size >= photoTrashEntries.length) {
                selectedPhotoTrashIds.clear();
            } else {
                photoTrashEntries.forEach(e => selectedPhotoTrashIds.add(e.trashId));
            }
            renderPhotoTrashGrid();
        }

        function renderPhotoTrashGrid() {
            const grid = document.getElementById('photo-trash-grid');
            updatePhotoTrashButtons();
            if (photoTrashEntries.length === 0) {
                grid.innerHTML = '<div class=\'col-span-full text-center py-12 text-slate-400 text-xs\'>写真ゴミ箱は空です</div>';
                return;
            }
            grid.innerHTML = photoTrashEntries.map(e => {
                const isChecked = selectedPhotoTrashIds.has(e.trashId);
                const dt = e.trashedAt ? new Date(e.trashedAt).toLocaleString('ja-JP') : '';
                const safeTitle = escapeHtml(e.originalItemTitle || '無題');
                const safeChild = e.originalChildItemTitle ? (' / ' + escapeHtml(e.originalChildItemTitle)) : '';
                return `<div class='bg-white border ${isChecked ? 'border-rose-500 ring-2 ring-rose-300' : 'border-slate-200'} rounded-lg overflow-hidden shadow-sm flex flex-col justify-between'>
                    <div class='relative aspect-square bg-slate-100 overflow-hidden'>
                        <img src='${e.imageUrl}' class='w-full h-full object-cover'>
                        <label onclick='event.stopPropagation()' class='absolute top-1 right-1 z-10 bg-black/75 hover:bg-black p-1 rounded cursor-pointer flex items-center justify-center border border-white/30'>
                            <input type='checkbox' ${isChecked ? 'checked' : ''} onchange='togglePhotoTrashSelect(""${escapeHtml(e.trashId)}"", event)' class='w-4 h-4 accent-rose-500 cursor-pointer'>
                        </label>
                    </div>
                    <div class='p-2 space-y-1 text-[10px]'>
                        <p class='font-bold text-slate-800 line-clamp-1' title='${safeTitle.replace(/'/g, '&#39;')}'>${safeTitle}${safeChild}</p>
                        <p class='text-slate-400 font-mono'>${escapeHtml(dt)} ・ ${formatBytesSimple(e.fileSizeBytes)}</p>
                        <div class='flex gap-1 pt-1'>
                            <button type='button' onclick='restoreSinglePhotoTrash(""${escapeHtml(e.trashId)}"")' class='flex-1 bg-emerald-50 hover:bg-emerald-100 border border-emerald-200 text-emerald-700 py-1 rounded font-bold transition'>復元</button>
                            <button type='button' onclick='deleteSinglePhotoTrashPermanent(""${escapeHtml(e.trashId)}"")' class='bg-rose-50 hover:bg-rose-100 border border-rose-200 text-rose-700 px-2 py-1 rounded font-bold transition'>削除</button>
                        </div>
                    </div>
                </div>`;
            }).join('');
        }

        async function restorePhotoTrashIds(ids) {
            if (!ids || ids.length === 0) return;
            try {
                const res = await fetch('/api/photo-trash/restore', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ trashIds: ids })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') {
                    throw new Error(data.message || '復元に失敗しました');
                }
                selectedPhotoTrashIds.clear();
                showToast(data.message || (data.restoredCount + '枚を復元しました'));
                await fetchItems();
                await loadPhotoTrashEntries();
                if (currentViewerItemId) renderViewerPhotos();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        function restoreSelectedPhotoTrash() {
            restorePhotoTrashIds(Array.from(selectedPhotoTrashIds));
        }

        function restoreSinglePhotoTrash(trashId) {
            restorePhotoTrashIds([trashId]);
        }

        async function deletePhotoTrashIdsPermanent(ids) {
            if (!ids || ids.length === 0) return;
            if (!confirm('選択した写真 ' + ids.length + '枚を PC の _photo_trash から完全に削除しますか？（元に戻せません）')) return;
            try {
                const res = await fetch('/api/photo-trash/delete', {
                    method: 'POST',
                    headers: getHeaders(),
                    body: JSON.stringify({ trashIds: ids })
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '削除に失敗しました');
                selectedPhotoTrashIds.clear();
                showToast((data.deletedCount || ids.length) + '枚の写真を完全削除しました');
                await loadPhotoTrashEntries();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
        }

        function deleteSelectedPhotoTrashPermanent() {
            deletePhotoTrashIdsPermanent(Array.from(selectedPhotoTrashIds));
        }

        function deleteSinglePhotoTrashPermanent(trashId) {
            deletePhotoTrashIdsPermanent([trashId]);
        }

        async function emptyPhotoTrash() {
            if (photoTrashEntries.length === 0) {
                showToast('写真ゴミ箱はすでに空です');
                return;
            }
            if (!confirm('写真ゴミ箱内の ' + photoTrashEntries.length + '枚をすべて完全削除しますか？（元に戻せません）')) return;
            try {
                const res = await fetch('/api/photo-trash/empty', {
                    method: 'POST',
                    headers: getHeaders()
                });
                const data = await res.json();
                if (!res.ok || data.status === 'error') throw new Error(data.message || '削除に失敗しました');
                selectedPhotoTrashIds.clear();
                showToast('写真ゴミ箱を空にしました (' + (data.deletedCount || 0) + '枚削除)');
                await loadPhotoTrashEntries();
            } catch (e) {
                showToast('エラー: ' + e.message);
            }
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
            const selectedCheck = allItems.filter(i => selectedItemIds.has(i.id));
            if (selectedCheck.some(i => !isMergeEligibleStatus(i.status) || isItemTrashed(i))) {
                alert('売却済み・発送準備中またはゴミ箱内の商品はまとめ買いに含められません。');
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
            ['DRAFT', 'DRAFT_DONE', 'LISTED', 'SHIPPING', 'SOLD'].forEach(st => {
                const btn = document.getElementById(prefix + '-status-chip-' + st);
                if (!btn) return;
                if (st === status) {
                    const activeColor = st === 'SOLD'
                        ? 'bg-rose-600 text-white border-rose-600 shadow-sm'
                        : (st === 'SHIPPING'
                            ? 'bg-amber-600 text-white border-amber-600 shadow-sm'
                            : (st === 'DRAFT_DONE'
                                ? 'bg-teal-600 text-white border-teal-600 shadow-sm'
                                : (st === 'DRAFT'
                                    ? 'bg-slate-500 text-white border-slate-500 shadow-sm'
                                    : 'bg-emerald-600 text-white border-emerald-600 shadow-sm')));
                    btn.className = 'px-3 py-1.5 rounded-lg text-xs font-bold border transition ' + activeColor;
                } else {
                    btn.className = 'px-3 py-1.5 rounded-lg text-xs font-bold border border-slate-300 bg-white text-slate-600 hover:bg-slate-100 transition';
                }
            });
            const soldGroup = document.getElementById(prefix + '-sold-price-group');
            if (soldGroup) {
                soldGroup.className = isSoldFamilyStatus(status) ? 'block' : 'hidden';
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
            const soldPrice = (isSoldFamilyStatus(status) && soldInputVal !== '') ? parseNonNegInt(soldInputVal, listPrice) : 0;
            const basePrice = (isSoldFamilyStatus(status) && soldPrice > 0) ? soldPrice : listPrice;

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
            if (isSoldFamilyStatus(status) || status === 'MERGED') return [];
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
            markClaudeLabelSeen(item);

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

            const dropZoneHtml = `<div id='edit-photo-drop-zone' onclick='document.getElementById(""edit-photo-input"").click()' class='w-20 h-20 border-2 border-dashed border-indigo-300 hover:border-indigo-500 bg-indigo-50/50 rounded-lg flex flex-col items-center justify-center text-center transition cursor-pointer shrink-0' title='クリックまたはD&Dで写真追加'>
                <i class='fa-solid fa-plus text-indigo-600 text-sm mb-0.5'></i>
                <span class='text-[9px] font-bold text-indigo-800'>写真追加</span>
                <input type='file' id='edit-photo-input' accept='image/*' multiple class='hidden' onclick='event.stopPropagation()' onchange='handleEditPhotoInputChange(event)'>
            </div>`;

            const thumbnailsHtml = photos.map((p, idx) => {
                const url = p.md5Hash ? '/api/photos/' + p.md5Hash : (p.photoUrl || '');
                const hash = p.md5Hash || '';
                return `<div class='relative w-20 h-20 rounded-lg overflow-hidden border border-slate-200 bg-slate-100 shrink-0 group'>
                    <img src='${url}' onclick='window.open(""${url}"", ""_blank"")' class='w-full h-full object-cover cursor-pointer' title='クリックで原寸表示'>
                    <span class='absolute top-1 left-1 bg-black/60 text-white text-[9px] font-mono px-1 rounded'>#${idx + 1}</span>
                    ${hash ? `<button type='button' onclick='deleteEditPhotoByIdx(${item.id}, ${idx})' class='absolute top-1 right-1 w-5 h-5 rounded-full bg-rose-600 hover:bg-rose-700 text-white text-[10px] flex items-center justify-center shadow transition' title='写真を削除'><i class='fa-solid fa-xmark'></i></button>` : ''}
                </div>`;
            }).join('');

            grid.innerHTML = thumbnailsHtml + dropZoneHtml;
            setupModalPhotoDropZone('edit-photo-drop-zone', (files) => uploadEditPhotoFiles(files));
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
            const soldPrice = isSoldFamilyStatus(status) ? parseNonNegInt(soldRaw, price) : 0;
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

            const dropZoneHtml = `<div id='add-photo-drop-zone' onclick='document.getElementById(""add-photo-input"").click()' class='w-20 h-20 border-2 border-dashed border-emerald-300 hover:border-emerald-500 bg-emerald-50/50 rounded-lg flex flex-col items-center justify-center text-center transition cursor-pointer shrink-0' title='クリックまたはD&Dで写真追加'>
                <i class='fa-solid fa-plus text-emerald-600 text-sm mb-0.5'></i>
                <span class='text-[9px] font-bold text-emerald-800'>写真追加</span>
                <input type='file' id='add-photo-input' accept='image/*' multiple class='hidden' onclick='event.stopPropagation()' onchange='handleAddPhotoInputChange(event)'>
            </div>`;

            const thumbnailsHtml = pendingAddPhotoFiles.map((entry, idx) => `
                <div class='relative w-20 h-20 rounded-lg overflow-hidden border border-slate-200 bg-slate-100 shrink-0'>
                    <img src='${entry.previewUrl}' class='w-full h-full object-cover'>
                    <span class='absolute top-1 left-1 bg-black/60 text-white text-[9px] font-mono px-1 rounded'>#${idx + 1}</span>
                    <button type='button' onclick='removePendingAddPhoto(${idx})' class='absolute top-1 right-1 w-5 h-5 rounded-full bg-rose-600 hover:bg-rose-700 text-white text-[10px] flex items-center justify-center shadow transition' title='選択から除外'><i class='fa-solid fa-xmark'></i></button>
                </div>
            `).join('');

            grid.innerHTML = thumbnailsHtml + dropZoneHtml;
            setupModalPhotoDropZone('add-photo-drop-zone', addPendingPhotos);
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
            const soldPrice = isSoldFamilyStatus(status) ? parseNonNegInt(soldRaw, price) : 0;
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
                        const stLabel = statusLabelOf(status);
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
                    table.innerHTML = data.candidates.map(c => {
                        const safeTitle = escapeHtml(c.title || '無題');
                        const safeStatus = escapeHtml(c.status || '売却済');
                        const safeSoldAt = escapeHtml(c.soldAtText || '');
                        const safeSize = escapeHtml(c.sizeFormatted || '');
                        return `
                        <tr class='hover:bg-amber-50/50 transition'>
                            <td class='p-2 font-bold line-clamp-1 max-w-[200px]' title='${safeTitle.replace(/'/g, '&#39;')}'>${safeTitle}</td>
                            <td class='p-2'><span class='bg-rose-100 text-rose-700 px-1.5 py-0.5 rounded text-[10px] font-bold'>${safeStatus}</span></td>
                            <td class='p-2 text-slate-500 font-mono text-[11px]'>${safeSoldAt}</td>
                            <td class='p-2 text-right font-mono font-bold text-amber-800'>${safeSize}</td>
                        </tr>
                    `;
                    }).join('');
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
                const statusText = item.status === 'MERGED'
                    ? ('まとめ内包済' + (item.mergedParentId ? ' (親#' + item.mergedParentId + ')' : ''))
                    : statusLabelOf(item.status);

                const price = item.price || 0;
                const soldPrice = item.soldPrice || 0;
                const feePercent = item.feeRatePercent != null ? item.feeRatePercent : 5;
                const shipping = item.shippingCost != null ? item.shippingCost : 210;
                const expense = item.expenseCost != null ? item.expenseCost : 55;
                const basePrice = (isSoldFamilyStatus(item.status) && soldPrice > 0) ? soldPrice : price;
                const fee = Math.floor((basePrice * feePercent) / 100);
                const profit = item.profit != null ? item.profit : Math.max(0, basePrice - fee - shipping - expense);
                const syncStatusText = item.isShared ? 'PC同期済' : '未送信';

                const createdStr = formatCsvDate(item.createdAt);
                const updatedStr = formatCsvDate(item.updatedAt);
                const soldStr = isSoldFamilyStatus(item.status) ? formatCsvDate(item.soldAt) : '';
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
