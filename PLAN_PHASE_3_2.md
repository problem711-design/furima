# フェーズ 3.2 実装計画書：UI改善・テンプレート拡張・一括削除＆一括編集機能

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン：**`v0.9β (Build 8)`**  
目的：カメラUI重なり解消、サムネイル全写真スワイプ表示、スマホ＆PCのテンプレート管理フルウィンドウ化、複数選択一括削除および一括ステータス/プリセット/価格変更機能の実装。

---

## 1. 概要と改善対象一覧

1. **CameraX シャッターボタンUI改善 (カメラ被り緊急修正)**:
   - `CameraXCaptureDialog.kt` の下部コントロールレイアウトに `.navigationBarsPadding()` を適用し、システムナビゲーションバー（3ボタン/ジェスチャーバー）との被りを解消。
2. **サムネイル一画面プレビュー時のスワイプ切替**:
   - `ItemEditScreen.kt` および `ItemDetailScreen.kt` でサムネイルタップ時、全登録写真リストとタップされたインデックスを渡して `PhotoPreviewDialog`（`HorizontalPager`）を起動。左右スワイプで全写真を鑑賞・原本取得できるように修復。
3. **テンプレート管理・選択UIの全面改修 (スマホ ＆ PC)**:
   - **出品編集画面 (スマホ `ItemEditScreen.kt` & PC `DashboardHtml.cs`)**: テンプレート選択をドロップダウンメニュー（プルダウン選択）に置き換え。
   - **設定画面 (スマホ `SettingsStorageScreen.kt`)**: タイトル用・商品説明用テンプレートの「追加・編集（削除含む）」ボタンを配置。全画面フルウィンドウダイアログ（`Dialog(properties = DialogProperties(usePlatformDefaultWidth = false))`）を開き、大型テキストエリアで快適に編集・作成・削除できる環境を提供。
   - **PC管理画面 (`DashboardHtml.cs`)**: テンプレート編集モーダルを全画面スタイルへ拡張。
4. **複数選択一括削除 (スマホ ＆ PC)**:
   - **スマホ (`ItemListScreen.kt`)**: 下部選択バーに「まとめ買い作成」と並べて「一括ゴミ箱移動」ボタンを追加し、確認ダイアログ表示後に一括処理。
   - **PC (`DashboardHtml.cs`)**: 選択フローティングバーに「一括ゴミ箱移動」ボタンを追加し、確認ダイアログ表示後に一括処理。
5. **複数選択一括編集機能 (一括ステータス・プリセット・価格変更)**:
   - スマホ・PC双方の選択バーに「一括編集」ボタンを追加。
   - 一括でステータス変更（下書き/出品中/売却済）、送料・手数料プリセットの一括適用、価格の一括調整（定額設定、一括値引き/値上げ）を行うダイアログおよび API (`POST /api/items/batch-update`) を新設。

---

## 2. 編集・新規作成対象ファイル一覧

### 2.1 カメラUI ＆ プレビュー調整
- **[MODIFY] [CameraXCaptureDialog.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt)**: 下部撮影コントロールバーへ `.navigationBarsPadding()` を付与。
- **[MODIFY] [ItemEditScreen.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt)**: サムネイルタップ時に全写真リストとインデックスを保持して `PhotoPreviewDialog` を呼び出し。
- **[MODIFY] [ItemDetailScreen.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt)**: サムネイルタップ時に全写真を渡して `PhotoPreviewDialog` を呼び出し。

### 2.2 テンプレート管理・選択のフルウィンドウ化
- **[MODIFY] [ItemEditScreen.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt)**: テンプレート選択をドロップダウンメニューへ変更。
- **[MODIFY] [SettingsStorageScreen.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt)**: フルウィンドウダイアログによるテンプレート一覧・作成・大型テキストエリア編集UIを追加。
- **[MODIFY] [DashboardHtml.cs](file:///C:/Users/user/AndroidStudioProjects/furima/tools/DashboardHtml.cs)**: PC管理画面のテンプレート選択をプルダウン化し、編集モーダルを全画面スタイルへ拡張。

### 2.3 複数選択一括削除 ＆ 一括編集機能
- **[NEW] [BatchEditDialog.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/components/BatchEditDialog.kt)**: スマホ用の一括ステータス/プリセット/価格変更ダイアログ。
- **[MODIFY] [ItemListScreen.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt)**: 選択モード時の下部バーに「一括削除」および「一括編集」ボタンを追加。
- **[MODIFY] [FleaMarketViewModel.kt](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt)**: `batchDeleteItems` および `batchUpdateItems` 関数を追加。
- **[MODIFY] [DashboardHtml.cs](file:///C:/Users/user/AndroidStudioProjects/furima/tools/DashboardHtml.cs)**: フローティングバーへ「一括削除」および「一括編集」モーダル機能を追加。
- **[MODIFY] [FurimaServerNative.cs](file:///C:/Users/user/AndroidStudioProjects/furima/tools/FurimaServerNative.cs)**: 一括更新・一括削除用 API (`POST /api/items/batch-update`, `POST /api/items/batch-delete`) を追加。
