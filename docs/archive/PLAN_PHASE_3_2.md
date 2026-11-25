# フェーズ 3.2 実装計画書：UI改善・テンプレート拡張・一括削除＆一括編集・出品支援強化

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン：**`v0.9β (Build 8)`** ➔ 完了目標：**`v0.9β (Build 9)`**

---

## 1. 確定仕様とサブフェーズ構成

### フェーズ 3.2-A：カメラUI被り根本解消・プレビュー横スワイプ修復・出品前チェック強化
1. **CameraX 下部コントロールバー被りの根本解消 (`CameraXCaptureDialog.kt` / `ItemEditScreen.kt`)**
   - Build 8 時点で `CameraXCaptureDialog.kt` には既に `.navigationBarsPadding()` が付与されていたが、`Dialog` ウィンドウは Xiaomi HyperOS / Android 15 (`targetSdk = 35`) 環境下でシステムバー Insets を正しく受け取れない場合がある。
   - **採用案**：`CameraXCaptureDialog` を `Dialog` ではなく **`ItemEditScreen` 内のルート全画面オーバーレイ（`Box` 最前面レイヤー）** として描画するよう変更し、`MainActivity` の `enableEdgeToEdge()` による正確な `WindowInsets.navigationBars` / `statusBars` を継承させる（併せて安全下限余白も確保）。
2. **写真拡大プレビューの左右スワイプ修復 (`PhotoPreviewDialog.kt`)**
   - `ItemEditScreen` / `ItemDetailScreen` からは既に全写真リストと `initialIndex` が渡されているが、`PhotoPreviewDialog.kt` の `ZoomablePhotoPage` が `detectTransformGestures` を使用しているため、等倍（`scale == 1f`）の1本指ドラッグもイベント消費してしまい `HorizontalPager` の横スワイプを阻害していた。
   - **採用案**：`awaitEachGesture` ベースのカスタムジェスチャーに置き換え、**2本指以上のピンチ操作**または**拡大表示中（`scale > 1.05f`）**のみイベントを消費し、等倍時の1本指横スワイプは `HorizontalPager` へ透過させる。
3. **出品前の文字数・枚数・必須項目チェックアラート（スマホ ＆ PC）**
   - Yahoo!フリマ出品基準を想定し、**タイトル未入力／65文字超過・写真0枚・写真10枚超過・価格0円・利益マイナス** などを編集画面 (`ItemEditScreen.kt`)・詳細画面 (`ItemDetailScreen.kt`)・PC管理画面 (`DashboardHtml.cs`) で視認性の高い警告バッジ／アラートとして事前表示する。

### フェーズ 3.2-B：テンプレート管理・選択UI刷新 ＋ 変数展開 ＋ PCワンクリックコピー
1. **出品編集・詳細画面のテンプレート選択をドロップダウン化 (`ItemEditScreen.kt`)**
   - `FlowRow` + `AssistChip` を `ExposedDropdownMenuBox` + `DropdownMenu` に置き換え、長文名・多件数でも画面を縦に圧迫しないようにする。
   - 選択・挿入後は選択表示を即座にリセット（「定型文を選択して挿入...」）し、同じ定型文の連続挿入を可能にする。
2. **テンプレートの簡易変数（プレースホルダー）自動展開（スマホ ＆ PC）**
   - テンプレート本文中の `{title}`（現在の商品名）および `{price}`（現在の価格・円表記/数値）を挿入時に自動置換して展開する（未入力時は空文字または `0` として安全処理）。
3. **テンプレート管理のフルウィンドウ／大型エディタ化 (`SettingsStorageScreen.kt` & `DashboardHtml.cs`)**
   - **スマホ (`SettingsStorageScreen.kt`)**：従来の小さな `AlertDialog` を廃止し、`Dialog(properties = DialogProperties(usePlatformDefaultWidth = false))` によるフルウィンドウ編集ダイアログを新設（`Scaffold` + 上部保存/戻るバー + `statusBarsPadding()` / `navigationBarsPadding()` / `imePadding()` + 残余高さをすべて使う大型テキストエリア）。タイトル用・説明文用の追加/編集/削除を快適化。
   - **PC (`DashboardHtml.cs`)**：テンプレート作成・編集モーダルを全画面に近い大型オーバーレイモーダルへ拡張し、プレビュー／変数ヒント（`{title}`, `{price}`）も表示。
4. **PC管理画面 ワンクリックコピー強化 (`DashboardHtml.cs`)**
   - 商品一覧カード／閲覧・編集モーダルにおいて、「タイトルコピー」「説明文コピー」ボタンを1クリック操作可能にし、コピー成功時にチェックマーク＋フィードバック表示を行う。

### フェーズ 3.2-C：選択モード拡張・ゴミ箱一括操作・一括ゴミ箱移動＆部分一括編集 ＋ Build 9 統一
1. **通常一覧の選択モード拡張（スマホ `ItemListScreen.kt` / `FleaMarketViewModel.kt` ＆ PC `DashboardHtml.cs`）**
   - 従来の「まとめ買い専用（`SOLD` 選択不可）」制限を解除し、**論理削除されていない親商品（`LISTED` / `DRAFT` / `SOLD`、ただし `MERGED` 子商品を除く）はすべて選択可能**にする。
   - 下部選択バー（スマホ）およびフローティングバー（PC）は **1件以上の選択で表示**。
   - 「まとめ買い作成」ボタンのみ「2件以上 かつ 選択商品がすべて `LISTED` または `DRAFT`」のときに有効化（`SOLD` を含む場合は無効化＋理由ヒント表示）して安全性を確保する。
2. **一括ゴミ箱移動（スマホ ＆ PC）**
   - 選択バーに「ゴミ箱へ移動」ボタンを追加し、確認ダイアログ後に一括論理削除（まとめ親商品の場合は子商品も連動削除）。
   - PCサーバー API は論理削除であることが明確な **`POST /api/items/batch-trash`** として実装し、バッチ全体で `BumpDataRevision()` と `SaveItemsDbToDisk()` を1回のみ実行する。
3. **部分更新型・一括編集機能（`BatchEditDialog.kt` ＆ `DashboardHtml.cs` ＆ `POST /api/items/batch-update`）**
   - 「ステータス変更（下書き / 出品中 / 売却済）」「費用プリセット適用（`基本設定に従う` を含む）」「価格調整（固定額設定 / ±円 / ±%）」の3項目を、**それぞれONにした項目だけ上書きする部分更新（パーシャルアップデート）** として実装。
   - **価格下限クランプ**：相対値下げ等でマイナスになる場合は **`0` 円未満にならないよう 0 円クランプ** を行う（端数丸めは行わない）。
   - **ステータス整合性**：`SOLD` へ一括変更時は `soldAt` を付与し `soldPrice <= 0` なら `price` を反映。`LISTED`/`DRAFT` へ戻す場合は `soldAt = null` / `soldPrice = 0` にリセット。まとめ親商品の場合は子商品の `statusLabel` も連動更新し、`profit` を再計算する。
   - 確定前に「対象件数と価格変更サンプル（変更前 ➔ 変更後）」のプレビューを表示して誤操作を防止する。
4. **ゴミ箱画面の複数選択一括操作 (`TrashScreen.kt` ＆ `DashboardHtml.cs` TRASHタブ)**
   - ゴミ箱内でも複数選択チェックを有効化し、「選択した商品を元に戻す (`POST /api/items/batch-restore`)」「選択した商品を完全削除 (`DELETE /api/items/batch-permanent`)」を実行できるようにする。
5. **バージョン統一 (`Build 9`) ＆ 引き継ぎ更新**
   - `app/build.gradle.kts` (`versionCode = 9`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v0.9β (Build 9)`** へ3箇所同時統一し、`PROGRESS.md` を更新する。

---

## 2. 編集・新規作成対象ファイル一覧

- `app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt`
- `app/src/main/java/com/example/myapplication/ui/components/PhotoPreviewDialog.kt`
- `app/src/main/java/com/example/myapplication/ui/components/BatchEditDialog.kt` (**新規**)
- `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/screens/TrashScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
- `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt`
- `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
- `app/build.gradle.kts`
- `tools/DashboardHtml.cs`
- `tools/FurimaServerNative.cs`
- `PROGRESS.md`
