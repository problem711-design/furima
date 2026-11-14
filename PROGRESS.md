# 開発進捗・引き継ぎログ (Progress & Handover Log) - 第2世代

このドキュメントは、第2世代アーキテクチャ（母艦PCマスターサーバー型 & MD5ハッシュ差分同期構成）における各実装フェーズの進捗状況、現状のギャップ調査結果、および修復記録を保持するためのものです。

> **過去の開発進捗ログ・詳細計画書 アーカイブ & 現行計画書**:
> - 第1世代〜初期ログ: [PROGRESS_ARCHIVE_PHASE1.md](PROGRESS_ARCHIVE_PHASE1.md)
> - フェーズ2.0〜2.11全詳細ログ・計画書: [PROGRESS_ARCHIVE_PHASE2.md](PROGRESS_ARCHIVE_PHASE2.md)
> - フェーズ 2.12〜2.14 計画書: [PLAN_PHASE_2_12_TO_2_14.md](PLAN_PHASE_2_12_TO_2_14.md)
> - フェーズ 3.1 計画書: [PLAN_CAMERA_AND_PREVIEW_FIX.md](PLAN_CAMERA_AND_PREVIEW_FIX.md)
> - **現行実装計画書 (フェーズ 3.2 / UI改善・テンプレート拡張・一括削除＆一括編集)**: [PLAN_PHASE_3_2.md](PLAN_PHASE_3_2.md)

---

## 開発・引き継ぎ基本原則 (Important Rules)

1. **小規模逐次実装**: 変更規模の小さなフェーズから優先的に1つずつ実装する。
2. **最小限のファイル編集**: 各フェーズごとに必要最小限のファイルのみを編集対象とし、不要なファイルを触らない。
3. **完全な進捗記録**: 各フェーズの完了時、または大きめの変更（データ保存・通信方式の変更、機能の追加・削除など）が発生するたびに、本ファイル (`PROGRESS.md`) に「フェーズ〇〇完了」と明確に記録を残す。
4. **既存UI・レイアウトの維持**: 特別な指示がない限り、ユーザーが気に入っているアプリの見た目・レイアウト・配色を変更しない。
5. **ビルド検証の最適化 (トークン消費節約)**: 純粋なWeb UI/HTML/CSS/JSの調整や、Kotlinロジックに変更のない軽微なデザイン調整時は、毎回 `gradle_build` を実行せずビルドを省略・スキップしてトークン消費と処理時間を抑える。
6. **厳格なバージョン表記・同時統一更新規則 (Strict Versioning Rule)**:
   - 各完了フェーズ・更新履歴ごとに「進捗状況」と「バージョン・Build番号」を完全対応させて明記する。
   - 表記フォーマット: `yyyy/mm/dd フェーズ〇〇完了 バージョン 0.x (Build x)`
   - バグ修正・レイアウト調整・内部処理順変更等の小規模変更: Build番号を加算（例: `Build 1` ➔ `Build 2`）。
   - **バージョン表記の更新義務**: バージョン更新時は、**スマホアプリ側 (`app/build.gradle.kts`)**、**PC管理画面 (`DashboardHtml.cs`)**、**スマホ緊急Web管理画面 (`WebDashboardHtml.kt`)** の3箇所を**必ず同時に統一して更新**する。
7. **仕様・実装計画書の併せての確認ルール (Plan File Verification Rule)**:
   - 作業開始時、およびビルドナンバー（Build番号）更新時には、本ファイル (`PROGRESS.md`) に加えて、最新の仕様・計画書を**必ず同時に確認・参照・更新**する。
8. **フェーズ完了ごとの Git コミット & GitHub 同期ルール**:
   - 各フェーズ（またはサブフェーズ）の完了ごとに、対象ファイルを選択的に `git add` して `git commit` を作成し、**`git push origin main` で GitHub リモートへ同期**する。
9. **実機直接インストール保証・`testOnly` 防止ルール (Physical Device Installation Rule)**:
   - スマホ実機への直接インストール（ファイルマネージャー等からのタップ）を常に保証するため、`gradle.properties` に `android.injected.testOnly=false` を常時固定し、`targetSdk = 35`（安定版）を維持する。
   - APK 再ビルド時は、生成された `app-debug.apk` をプロジェクトルートの `flea_market_app.apk` および `public/flea_market_app.apk` へ上書き更新して実機配布用ファイルとして提示する。

---

## 全体ロードマップ (第2世代アーキテクチャ)

現在のバージョン：**`v0.9β (Build 9)`** （Phase 3.2 完了：UI改善・テンプレートフルウィンドウ化＆変数展開・一括操作・出品前チェック・Build 9 反映）

### これまでに完了した主要開発マイルストーン (詳細は `PROGRESS_ARCHIVE_PHASE2.md` を参照)
- [x] **フェーズ 2.0 〜 2.7 (基幹同期・まとめ買い・ゾンビ親商品根絶・選択ガード)** 【完了】
  - 差分同期の安定化、まとめ買い作成・解体復元、論理削除伝播によるゾンビ復活防止、売却済み選択ガード、まとめ点数合算表示の適正化。
- [x] **フェーズ 2.8 (同期競合防止・QR完全修復・接続時ブラウザ起動・Yahoo画面改善)** 【完了】
  - PULL先行型自動同期、ISO/IEC 18004 準拠 QRコード描画、緑ステータス確定時ブラウザダイアログ表示、Yahoo!フリマ画面の通常タブ化。
- [x] **フェーズ 2.9 (日時双方向同期・PC表示優先度ソート・CSVダウンロード強化)** 【完了】
  - `createdAt`/`updatedAt`/`soldAt` の完全同期、PC管理画面での「出品中まとめ→未出品まとめ→出品中単品→下書き単品→売却済み」優先度ソート、PC側 CSV ダウンロードエクスポート。
- [x] **フェーズ 2.10 (バックボタン安全化・サムネ容量可視化・ギャラリー保存刷新)** 【完了】
  - 未保存変更破棄ダイアログ・タブ復元・終了ダイアログ等のバックキーガード、圧縮率別削減容量リアルタイム表示、詳細画面「PCに送信」分離および原本取得付きギャラリー自己修復。
- [x] **フェーズ 2.11 (基本設定/プリセット同期・0円許容&写真削除API・PC編集モーダル同等化)** 【完了】
  - 基本費用/プリセットのスマホ→PC同期、送料・経費0円および手数料0%の許容、写真個別削除 API、PC新規/編集モーダルの完全スマホ同等化。
- [x] **実機判定による初期テストデータシード自動オフ機能** 【完了】
  - `FleaMarketRepository.kt` にて実機/エミュレーター判定を実施し、実機環境では初期テストデータ10件を自動挿入せず完全クリーン状態で起動するよう制御。
- [x] **実運用到達に伴うバージョン表記リセット (`v0.9β Build 1`)** 【完了】
  - アプリ・PC管理画面・Web画面すべてのバージョン表記を `v0.9β (Build 1)` に統一。

### これからの実装予定マイルストーン (詳細仕様は `PLAN_PHASE_2_12_TO_2_14.md` を参照)
- [x] **フェーズ 2.12 (`v0.9β Build 2`) — スマホUI不具合修正 ＋ 原本取得付き写真拡大プレビュー** 【完了】
  - `2.12-A` 長押しメニュー (`ItemQuickActionDialog`) のステータス表示不一致修正（4ステータス統一・下書き切替対応）
  - `2.12-B` 長押しメニューからの削除／まとめ解除実行後にメニューが閉じない不具合修正（二重送信防止ガード＋即時クローズ）
  - `2.12-C` 詳細画面＆編集画面でのサムネイルタップ全画面拡大プレビュー（左右スワイプ・ピンチズーム・ダブルタップズーム ＋ PCサーバー接続中の高画質原本取得ボタン付き）
- [x] **フェーズ 2.13 (`v0.9β Build 3`) — 設定双方向同期 ＋ デフォルトプリセット変更強化 ＋ テンプレート機能** 【完了】
  - `2.13-A` `DefaultSettings` / `store.json` 拡張による設定双方向同期基盤（`PUT /api/settings` ＋ `settingsUpdatedAt` 新旧判定、Room DB 無変更）
  - `2.13-B` デフォルトプリセットの切り替え・編集・即時同期（スマホ設定画面・編集画面チップ・PC管理画面モーダル）
  - `2.13-C` タイトル／商品説明テンプレートの登録・編集・カーソル位置選択挿入（スマホ＆PC管理画面共通）
- [x] **フェーズ 2.14 (`v0.9β Build 4`) — 本命「ゴミ箱」機能 ＋ 一人利用最適・競合防止同期への刷新** 【完了】
  - `2.14-A` Room DB 拡張 (`syncUuid`, `deletedAt`, `isDirty`, `MIGRATION_5_6`) ＆ 論理削除/復元/30日超過完全削除リポジトリ
  - `2.14-B` イベント駆動Push ＋ 軽量リビジョン検知 (`dataRevision`/`settingsRevision`) Pull ＋ `isDirty`/`updatedAt` 保護 ＋ UUID衝突防止同期エンジン
  - `2.14-C` スマホ＆PC共通「ゴミ箱」UI実装（復元・完全削除・一括削除・30日保持カウント表示）
- [x] **フェーズ 2.15 (`v0.9β Build 5`) — 重大度「致命的」バグ修復（型不一致・原本/MD5破壊・ビルド復旧）** 【完了】
  - [x] `2.15-A` PCサーバー `items_data.json` 再読込後の型不一致 (`JArray`/`JObject` vs `Dictionary`) 根絶（写真追加時消去・フォルダ振り分け・欠損数・30日掃除の復旧）
  - [x] `2.15-B` スマホ側 `cleanupSharedOriginalPhotos` / `saveItem` / `syncDeltaToPcServer` / `mergeServerItemsFromJson` での `isOriginalDeleted` & `md5Hash` 破壊修復
  - [x] `2.15-C` `tools/build-exe.bat` への `TunnelQrForm.cs` 追加 ＋ DLL配置整理 ＋ `Build 5` 統一
- [x] **フェーズ 2.16 (`v0.9β Build 6`) — 重大度「警告」修復（PC単独編集・同期整合・画像処理・UI修正）** 【完了】
  - [x] `2.16-A` デバッグ残骸 `#18` 撤去 ＋ まとめ買い子ステータス適正化＆30日クリーンアップ安全化
  - [x] `2.16-B` PC単独編集向け案内文言刷新（ロック表記撤廃）・`lastHeartbeatSec` / `HandleRequest` / `JValue(null)` / UTF-8安全性・フォルダ分断解消・`soldPrice` / 復元パス修復
  - [x] `2.16-C` 画像形式マジックバイト判定・Canvas経由クリップボードコピー・EXIF回転補正・旧モーダル整理 ＋ `Build 6` 統一
- [x] **フェーズ 2.17 (`v0.9β Build 7`) — 重大度「提案」改善（ポーリング最適化・孤立ファイル掃除・資産整理）** 【完了】
  - [x] `2.17-A` `DashboardHtml.cs` リビジョン検知ポーリング一本化・`SaveItemsDbToDisk` 条件付き保存・ロック保護＆未使用バッファ削除
  - [x] `2.17-B` `savePhotoFromUri` 失敗時null保証・`saveItem` 孤立ファイル掃除・デッドコード/旧スクリプト/未使用依存整理 ＋ `Build 7` 統一
- [x] **フェーズ 3.1 (`v0.9β Build 8`) — ハイブリッドカメラ（CameraX ＋ 標準カメラ）＆ 拡大プレビュー原本取得ボタン上部移設** 【完了】
  - [x] `ItemEditScreen.kt` 撮影ボタン横並び 2 分割（`52.dp` 維持：左側「アプリ内カメラ」＋右側「標準カメラ」＋ `CAMERA` 権限リクエスト／拒否時案内）
  - [x] `CameraXCaptureDialog.kt` 新設（連続撮影・明るさEVスライダー・ズーム・タップフォーカス・フラッシュ/トーチ・3×3/1対1出品枠ガイド・`Camera2Interop` 対応範囲簡易マニュアル調整）
  - [x] `PhotoPreviewDialog.kt` 「PCから高画質原本を取得」ボタンの上部ヘッダーエリア移設 ＋ `Build 8` 統一
- [x] **フェーズ 3.2 (`v0.9β Build 9`) — UI改善・テンプレート拡張・一括操作・出品前チェック** 【完了】
  - [x] `3.2-A` CameraX全画面オーバーレイ化によるナビバー被り根絶・プレビュー1本指スワイプ透過修復・出品前チェックアラート
  - [x] `3.2-B` テンプレート選択ドロップダウン化＆フルウィンドウ編集・`{title}`/`{price}`変数展開・PCワンクリックコピー強化
  - [x] `3.2-C` 選択モード拡張・ゴミ箱一括復元/完全削除・一括ゴミ箱移動(`batch-trash`)・部分一括編集(`batch-update`, 0円クランプ) ＋ `Build 9` 統一

---

## 更新履歴 (Changelog)

### 2026/11/14 フェーズ 3.2 完了 バージョン 0.9β (Build 9)
- **3.2-C (選択モード拡張・ゴミ箱一括操作・一括ゴミ箱移動＆部分一括編集・Build 9統一)**：
  - `ItemListScreen.kt` / `FleaMarketViewModel.kt` / `DashboardHtml.cs`：選択モードの `SOLD` 選択禁止を解除（非削除・非内包商品は全ステータス選択可）し、1件選択から一括操作バーを表示。「まとめ買い」ボタンのみ `2件以上かつ未売却のみ` で有効化する安全ガードを追加。
  - `BatchEditDialog.kt` / `FleaMarketRepository.kt` / `DashboardHtml.cs`：ステータス変更（`soldAt`/`soldPrice`・まとめ子 `statusLabel` 連動）、費用プリセット適用（`基本設定に従う` 含む）、価格変更（固定額/±円/±%、**0円未満クランプ**）を個別ON/OFFできる部分一括編集 ＋ 変更サンプルプレビューを実装。
  - `TrashScreen.kt` / `FurimaServerNative.cs` / `DashboardHtml.cs`：ゴミ箱画面での複数選択一括復元・一括完全削除を追加し、PCサーバーに `POST /api/items/batch-trash`、`POST /api/items/batch-restore`、`DELETE /api/items/batch-permanent`、`POST /api/items/batch-update` を新設（バッチ毎に1回のみ `BumpDataRevision()` / `SaveItemsDbToDisk()` を実行）。
  - **コンパイル検証 ＆ バージョン統一更新**：`FleaMarketRepository.kt`（`getItemWithPhotosById` 参照修正）・`ItemEditScreen.kt`（`FlowRow` インポート/OptIn復元）・`ItemListScreen.kt`（`AlertDialog` インポート追加）・`SettingsStorageScreen.kt`（`ExperimentalMaterial3Api` OptIn追加・重複インポート整理）の4箇所を修復し、`compile_applet` で Android ビルド正常通過を確認。`app/build.gradle.kts` (`versionCode = 9`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v0.9β (Build 9)`** へ3箇所同時統一（※Windows向け `.exe` および実機配布用 `.apk` のバイナリ再ビルドは各自環境で実行可能）。

### 2026/11/14 フェーズ 3.2-B 完了 (進行中 ➔ 目標 v0.9β Build 9)
- **3.2-B (テンプレートUI刷新・フルウィンドウ編集・変数展開・PCワンクリックコピー)**：
  - `ItemEditScreen.kt`：タイトル/商品説明テンプレート選択を `ExposedDropdownMenuBox` ドロップダウンへ移行し、選択挿入後即座に選択表示をリセット（連続挿入可能）。挿入時に `{title}` / `{price}` 変数を自動展開。
  - `SettingsStorageScreen.kt`：従来の小サイズ `AlertDialog` を廃止し、`Dialog(usePlatformDefaultWidth = false)` + `Scaffold` + `imePadding()` / `navigationBarsPadding()` + `{title}`/`{price}` 変数チップ付きフルウィンドウ大型テキストエリアエディタを実装。
  - `DashboardHtml.cs`：テンプレート編集モーダルを全画面スタイル (`#template-editor-modal`, `max-w-4xl h-[88vh]`) へ拡張、プルダウン挿入時の `{title}`/`{price}` 変数展開に対応。`copyText` にボタン上 `✓ コピー済` フィードバックを追加し、編集モーダル内にもタイトル/説明文ワンクリックコピーボタンを配置。

### 2026/11/14 フェーズ 3.2-A 完了 (進行中 ➔ 目標 v0.9β Build 9)
- **3.2-A (CameraXオーバーレイ化・プレビュー1本指スワイプ透過・出品前チェック)**：
  - `CameraXCaptureDialog.kt` / `ItemEditScreen.kt`：`Dialog` ウィンドウを廃止して `ItemEditScreen` ルート `Box` 最前面オーバーレイ (`zIndex(100f)`) へ移行し、`MainActivity` の `enableEdgeToEdge()` に基づく `navigationBarsPadding()` を確実に適用（Xiaomi HyperOS / Android 15 での3ボタンバー被りを根本解消）。
  - `PhotoPreviewDialog.kt`：`ZoomablePhotoPage` の `detectTransformGestures` を `awaitEachGesture` ベースへ刷新し、2本指ピンチまたは拡大中 (`scale > 1.05f`) のみイベント消費することで等倍時の1本指左右スワイプが `HorizontalPager` へ透過するよう修復。
  - `ListingValidationBanner.kt` / `ItemEditScreen.kt` / `ItemDetailScreen.kt` / `DashboardHtml.cs`：タイトル未入力/40文字超過・写真0枚/10枚超過・価格0円/300円未満・純利益0円以下・説明文未入力を事前検知する出品前チェックアラートをスマホ＆PC双方に追加。

### 2026/11/14 フェーズ 3.1 完了 バージョン 0.9β (Build 8)
- **ハイブリッドカメラ UI ＆ `rememberSaveable` 状態保護 (`ItemEditScreen.kt`, `AndroidManifest.xml`, `PhotoStorageManager.kt`)**：縦方向のレイアウト高さを増やさず従来の `52.dp` 高さのままで撮影ボタンを横並び 2 分割（「アプリ内カメラ」＋「標準カメラ」）へ改修。`AndroidManifest.xml` に `CAMERA` 権限と `STILL_IMAGE_CAMERA` 用 `<queries>` を追加し、左ボタンは `ActivityResultContracts.RequestPermission()` 経由で `CameraXCaptureDialog` を起動、右ボタンは `MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA` により POCO X6 Pro（HyperOS）標準カメラのフル機能（プロモード・マクロ・彩度調整等）を起動（非対応環境時は `TakePicture` へフォールバック）＋ギャラリー取込案内トーストを表示するよう実装。また一時撮影パス等の状態を `rememberSaveable` で保護。
- **アプリ内 CameraX 撮影画面 (`CameraXCaptureDialog.kt`)**：`PreviewView` (`ImplementationMode.COMPATIBLE`) による安定プレビュー、画面タップAF/AE・フォーカスリング、ピンチ＆プリセットズーム、露出補正（明るさ EV）スライダー、フラッシュ/トーチ切替、利用可能レンズ切替、3×3 グリッド＆1対1出品枠ガイド、1枚取消対応の連続撮影バッチ追加、および `Camera2Interop` による対応端末向け簡易マニュアル調整（ISO・シャッター速度・WB・Auto戻し、非対応項目自動非表示）を実装。
- **写真拡大プレビュー原本取得ボタンの上部移設 (`PhotoPreviewDialog.kt`) ＋ Build 8 統一**：「PCから高画質原本を取得」ボタンを画面最下部から上部ヘッダーエリア（`statusBarsPadding()` 適用）へ移設してシステムナビゲーションバーとの重なりを解消。`app/build.gradle.kts` (`versionCode = 8`, `targetSdk = 35` 維持)、`WebDashboardHtml.kt`、`tools/DashboardHtml.cs` を **`v0.9β (Build 8)`** へ統一し、`compile_applet` でコンパイル正常完了を確認。

### 2026/11/14 フェーズ 2.17 完了 バージョン 0.9β (Build 7)
- **2.17-A (ポーリング最適化・ディスクI/O削減・ロック安全化)**：`DashboardHtml.cs` の3秒ポーリングを軽量な `/api/server/status` のみへ一本化（`document.hidden` 時は自動停止・復帰時即時確認）し、`dataRevision` / `settingsRevision` 変化時のみ `/api/items`・`/api/cleanup/preview`・`/api/settings` を取得するよう最適化。`FurimaServerNative.cs` では `HandleSyncItems` の `SaveItemsDbToDisk()` を `dataChanged` 時のみ実行へ限定し、`HandleSyncPhoto` の未使用 `byte[] buffer` 確保を削除し、`hashToFilePathMap.Count` / `itemsDb.Count` 参照をすべて `lock (lockObj)` 内に保護。
- **2.17-B (ストレージ安全性・孤立ファイル掃除・不要資産/依存整理・Build 7 統一)**：`PhotoStorageManager.savePhotoFromUri` で `openInputStream` が `null` または書き出しサイズ `<= 0` 時に `null` を返却するよう保護。`FleaMarketRepository.saveItem` で編集時に外された写真について他商品未参照の端末ファイルを削除する孤立ファイル掃除を追加し、未実装スタブ `cleanupSharedPhotos()` と旧補助スクリプト (`tools/furima-sync.bat`, `tools/furima-sync.ps1`) を削除。`app/build.gradle.kts` から未使用依存（`retrofit`, `converter-moshi`, `logging-interceptor`, 未使用 `firebase-*`）をコメントアウト整理し、`v0.9β (Build 7)` (`versionCode = 7`) へ3箇所同時統一・APK更新・全ユニットテスト通過を確認。

### 2026/11/14 フェーズ 2.16 完了 バージョン 0.9β (Build 6)
- **2.16-A (デバッグ残骸撤去・まとめ買い子ステータス・クリーンアップ安全化)**：`DashboardHtml.cs` (`item.id === 18`) と `FurimaServerNative.cs` (`parentId == 18`) のテスト用ハードコードを撤去。`HandleMergeItems` の子 `statusLabel` を `asStatus` に応じて `"まとめ売却済"` / `"まとめ出品中"` へ適正化し、`SyncStore.GetCleanupCandidates` を `status == "SOLD"` のみに限定＋アクティブ他商品（`LISTED` / `DRAFT` / 未売却親に属する `MERGED` 子）が参照中のハッシュ・ファイル除外ガードを追加。
- **2.16-B (PC単独編集整合・サーバー堅牢化・フォルダ/復元修復)**：`DashboardHtml.cs` の旧「操作・編集ロック中」バナーを「PC単独で編集可／スマホ再接続時に自動同期」へ刷新。`FurimaServerNative.cs` では `lastHeartbeatSec` の `-1` 保護、`HandleRequest` 終端 `catch` 保護、`ParseBoolSafe` 導入による `JValue(null)` 回避、`Encoding.UTF8` 統一、`ResolveOrCreateItemFolder` によるタイトル変更時フォルダ分断解消、`displayOrder`/`childItemId` 保持を実施。Android 側も `ItemEditScreen.kt` の非売却時 `soldPrice = 0` 化および `LocalWebServer.kt` の `/api/photos/{photoId}/restore` サムネイル上書き防止を実装。
- **2.16-C (画像形式/EXIF/クリップボード対応・旧モーダル整理・Build 6 統一)**：`FurimaServerNative.cs` でマジックバイト判定 (`image/jpeg`, `image/png`, `image/webp`, `image/gif`) と `.jpg`/`.jpeg`/`.png`/`.webp` スキャンを実装。`DashboardHtml.cs` の `copyPhotoBlob` を Canvas 経由 `image/png` 変換へ改善し、旧 `openImageModal` / `#image-modal` を整理。`PhotoStorageManager.kt` に `ExifInterface` 回転補正を組み込み、`v0.9β (Build 6)` (`versionCode = 6`) へ3箇所同時統一・APK更新・全テスト通過を確認。

### 2026/11/14 フェーズ 2.15 完了 バージョン 0.9β (Build 5)
- **2.15-A (PCサーバー `JArray`/`JObject` 型不一致根絶)**：`SyncStore.cs` に `NormalizePhotosList(object)` を新設し、`CountMissingFiles` / `GetCleanupCandidates` および `FurimaServerNative.cs` の `HandleSyncPhoto` / `HandleUnmergeItem` / `HandleAddPhoto` の全5箇所を正規化経由へ統一。再起動後の `items_data.json` 読込状態でも写真追加時の既存写真消失や欠損数・クリーンアップ・フォルダ配置の破損が発生しないよう修復。
- **2.15-B (スマホ側 原本削除・再保存・共有写真の `isOriginalDeleted` / `md5Hash` 破壊修復)**：`ItemDao.kt` に同一 `filePath` 一括更新クエリ `markOriginalDeletedByFilePath` を追加し、`FleaMarketViewModel.cleanupSharedOriginalPhotos` および `PcServerClient.syncDeltaToPcServer` での原本削除時に親子双方の `PhotoEntity` を更新するよう修正。`FleaMarketRepository.saveItem` では `thumbnailPath` でも既存写真を照合して元の `md5Hash` と `isOriginalDeleted` を保持し、`PcServerClient.mergeServerItemsFromJson` では実原本の存在有無で `isOriginalDeleted` を正しく判定するよう修正。`Phase215PhotoIntegrityTest.kt`（3件）を追加し全テスト通過を確認。
- **2.15-C (`tools/build-exe.bat` 復旧・DLL整理・Build 5 統一)**：`build-exe.bat` に `TunnelQrForm.cs` を追加し、重複していた `tools/lib/Newtonsoft.Json.dll` を整理して `tools/Newtonsoft.Json.dll` へ参照を一本化（`.NET Framework 4.6+` 要件を明記）。`app/build.gradle.kts` (`versionCode = 5`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を `v0.9β (Build 5)` へ統一し、`flea_market_app.apk` / `public/flea_market_app.apk` を更新（※Windows向け `.exe` 再生成は Windows 環境で `tools/build-exe.bat` 実行が必要）。

### [2026-11-14] コードレビュー総点検に基づく修復・改善計画書 (`PLAN_PHASE_2_15_TO_2_17.md`) 策定
- 最新コミット (`3f87ea8`) の徹底コードレビュー結果に基づき、重大度「致命的」➔「警告」➔「提案」の優先順で順次修復するための詳細実装計画書を `PLAN_PHASE_2_15_TO_2_17.md` に策定。
- 仕様変更点の反映：初期の「スマホ常時接続時のみPC操作許可」仕様から現在の「PC単独編集 ＋ ゴミ箱復元対応 ＋ 安全な双方向同期」方針への移行を明確化し、ロック表記の撤廃と逐次ハンドオーバー時の整合性修復を組み込んだ。

### 2026/11/14 フェーズ 2.14 完了 バージョン 0.9β (Build 4)
- **2.14-A (Room DB拡張 ＆ 論理削除/復元リポジトリ)**：`ItemEntity` に `syncUuid`・`deletedAt`・`isDirty` を追加し、`AppDatabase` を Version 6 (`MIGRATION_5_6`) へ移行。通常一覧/集計クエリへ `deletedAt IS NULL` を適用し、`FleaMarketRepository` に `moveToTrash`・`restoreFromTrash`・`permanentlyDeleteItem`・`emptyTrash`・`purgeExpiredTrash(30)`・`unmergeItem` 時トームストーン記録を実装。
- **2.14-B (イベント駆動Push ＋ リビジョン検知Pull ＋ 競合防止同期)**：`SyncDtos.kt` / `PcServerClient.kt` / `FleaMarketViewModel.kt` と PC側 `SyncStore.cs` / `FurimaServerNative.cs` を同期プロトコルv4対応へ刷新。`dataRevision` / `settingsRevision` による軽量ハートビート検知、変更発生時500msデバウンスPush、再接続時Push先行ハンドシェイク、`syncUuid` 基準の和集合マージ＆数値ID衝突回避、`isDirty`・`updatedAt` 保護、90日保持トームストーン (`deleted_uuids.json`) によるゾンビ復活防止および `HandleSyncItems` からの無条件全削除廃止を実装。
- **2.14-C (スマホ＆PC共通ゴミ箱UI ＋ Build 4 統一)**：`TrashScreen.kt`・`MainActivity.kt`・`ItemListScreen.kt`（件数バッジ付きアイコン導線）・`ItemDetailScreen.kt`・`ItemQuickActionDialog.kt` へゴミ箱移動/復元/完全削除/空にするUIおよび30日保持カウント表示を実装。PC側 `DashboardHtml.cs` にも `TRASH` フィルタータブ・バナー・復元/完全削除/空にする操作を追加し、`Phase214TrashAndSyncTest.kt`（Robolectric/JUnit）を追加。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 4`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 4)` へ統一。

### 2026/11/13 フェーズ 2.13 完了 バージョン 0.9β (Build 3)
- **2.13-A (設定双方向同期基盤)**：`DefaultSettings` / `DefaultSettingsManager` / `SyncDtos` / `PcServerClient` / `FleaMarketViewModel` および PC側 `SyncStore.cs` / `FurimaServerNative.cs` を拡張し、`titleTemplates`・`descriptionTemplates`・`settingsUpdatedAt` を含む設定モデルを追加。`PUT /api/settings` の実装および `settingsUpdatedAt` 比較による古い設定の先祖返りガードを追加（Room DB 無変更）。
- **2.13-B (デフォルトプリセット変更・編集強化)**：`SettingsStorageScreen.kt` にて初期・追加を含む全プリセットの編集・削除（最低1件ガード）・ワンタップ「標準にする」即時同期を実装。`ItemEditScreen.kt` の個別費用設定内にプリセット適用チップを追加し、PC管理画面 `DashboardHtml.cs` にも設定モーダルを追加。
- **2.13-C (タイトル／商品説明テンプレート機能 ＋ Build 3 統一)**：スマホ設定画面＆PC設定モーダルにタイトル用・商品説明用テンプレートの追加・編集・削除UIを実装。`ItemEditScreen.kt`・`MergeItemsDialog.kt`（`TextFieldValue` カーソル位置挿入）および `DashboardHtml.cs`（`insertTextAtCursor` による新規・編集・まとめモーダル挿入）へ統合。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 3`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 3)` へ統一し、`compile_applet` 成功を確認。

### 2026/11/13 フェーズ 2.12 完了 バージョン 0.9β (Build 2)
- **2.12-A (長押しメニューのステータス表示不一致修正)**：`ItemQuickActionDialog.kt` のステータスバッジを一覧カードと同じ4種類（売却済・まとめ済・下書き・出品中）に統一し、下書き時の切替アクション（「出品中にする」）および `ItemListScreen.kt` の遷移処理を修正。
- **2.12-B (長押しメニューの削除/解除後残留修正)**：`ItemQuickActionDialog.kt` の削除・まとめ解除確認ボタンに二重クリック防止ガードと即時 `onDismiss()` を追加し、`ItemListScreen.kt` 側でも即座に `quickActionItem = null` を適用。
- **2.12-C (写真拡大プレビュー ＋ Build 2 統一)**：`PhotoPreviewDialog.kt` を新設（`HorizontalPager`、ピンチ拡大縮小、ダブルタップズーム、原本キャッシュ確認・PCサーバーからの「PCから高画質原本を取得」ボタン）し、`ItemDetailScreen.kt` および `ItemEditScreen.kt` のサムネイルタップへ連携。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 2`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 2)` へ統一し、`compile_applet` 成功を確認。

### [2026-11-13] フェーズ 2.12〜2.14 詳細実装計画書 (`PLAN_PHASE_2_12_TO_2_14.md`) の策定・追記
- スマホUIバグ2件（長押しステータス不一致・削除時メニュー残留）、原本取得ボタン付き拡大プレビュー、テンプレート＆デフォルトプリセット変更、および本命「ゴミ箱」＋一人利用最適のイベント駆動・競合防止同期設計の詳細仕様を `PLAN_PHASE_2_12_TO_2_14.md` に策定完了。
- 本計画書および進捗ログをローカル Git リポジトリへ先行コミット（GitHub リモート同期はトークン再共有時に実施）。

### [2026-11-13] 実運用開始に伴うバージョンリセット (`v0.9β Build 1`) & 履歴ドキュメント整理完了
- 全機能の安定動作および実運用到達を確認し、バージョン表記を `v0.9β (Build 1)` へ更新統一。
- **改修対象ファイル**:
  1. `app/build.gradle.kts` (`versionCode = 1`, `versionName = "0.9β"`)
  2. `tools/DashboardHtml.cs` (`v0.9β (Build 1)`)
  3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v0.9β (Build 1)`)
- 過去の全フェーズ・計画書ログ（Phase 1.0 〜 Phase 2.11）を `PROGRESS_ARCHIVE_PHASE2.md` に集約・隔離完了。
- 実機判定 (`isEmulator()`) によるクリーン起動機能の追加完了。
- 単体テスト (`testDebugUnitTest`) およびコンパイル検証の全件通過を確認。
