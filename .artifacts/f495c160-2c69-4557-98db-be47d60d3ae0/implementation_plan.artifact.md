# PC連携・Web管理画面の不具合調査および修復計画

ユーザー様からご報告いただいた以下の不具合・懸念点を調査・解決するための計画です。

## ユーザー様からのご報告内容
1. **旧データの未同期**: 同期ツール作成前の既存データが同期されない（※Room DBバージョンアップ時のスキーマ変更・破棄によるもの）。
2. **新規データのフォルダ名のみ生成問題**: 新規出品データのフォルダは生成されるが、写真・説明文・属性・ステータスが同期されていない。
3. **PCブラウザ管理画面での表示・操作不良**:
   - サムネイルや出品一覧が一切表示されない。
   - 「PCフォルダ自動同期」ボタン以外のボタン（新規出品、検索、フィルター、ステータス変更、表示形式変更、サムネクリック時のメニュー等）が反応していないように見える。
4. **中間同期ツールのリクエスト疎通の懸念**: 定期的な同期リクエストは送られているが、ブラウザからの全件同期・照合リクエストが正しく渡っていない可能性がある。

---

## 調査・修復のステップ

### 1. サーバー側（Android `LocalWebServer.kt`）のログ・リクエスト詳細化
- **概要**: `/api/items`, `/api/sync/full-data`, `/api/photos/{id}` などの各エンドポイントへのリクエスト受信時に、パラメータや該当件数、レスポンスのステータスをサーバーログ (`addLog`) に詳細に出力するよう強化します。
- **対象ファイル**: [`LocalWebServer.kt`](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/data/server/LocalWebServer.kt)

### 2. ブラウザ管理画面（`WebDashboardHtml.kt`）の JavaScript エラー検知・デバッグ強化
- **概要**: `fetchItems()` や各ボタンのイベントハンドラー（`openCreateModal`, `renderItems`, 各種モーダル操作）に `try-catch` と `console.error` / トースト通知を追加し、ブラウザ上でどの処理がエラーになっているかを視覚的に把握できるようにします。
- **対象ファイル**: [`WebDashboardHtml.kt`](file:///C:/Users/user/AndroidStudioProjects/furima/app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt)

### 3. APIレスポンスおよびデータモデル（DTO）の整合性確認
- **概要**: `/api/sync/full-data` や `/api/items` が返す JSON データが、PC側同期ツールやブラウザ側 JavaScript が期待する構造と完全に一致しているか確認します。

---

## 検証計画

### 動作確認
- Android実機またはエミュレーターでアプリを起動し、Webサーバーを有効化。
- PCブラウザでアクセスし、コンソールエラーの有無、商品一覧・サムネイルの表示、新規出品ボタン等の応答性を確認。
- サーバーログ (`addLog`) でリクエストの到達状況を確認。
