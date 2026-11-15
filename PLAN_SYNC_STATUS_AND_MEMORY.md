# 実装計画書：上部ヘッダースリム化・同期ステータス％表示 ＆ メモリ安定化フルパッケージ

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ中心・PCサーバーAPI互換維持）  
現行バージョン：**`v1.0.0 (Build 3)`** ➔ 完了目標：**`v1.0.0 (Build 4)`**

---

## 1. 背景と目的

1. **無線同期の進捗・エラー可視化とヘッダースリム化**
   - 高画質写真を何十枚も連続撮影した後、Wi-Fi 経由の自動／手動 PC 同期には相応の転送時間がかかるが、現状のスマホ画面には「転送中（何％・何枚目まで進行したか）」も「通信エラーや参照不整合で失敗しているか」も表示されない。
   - 一覧画面の上部ヘッダーは上下パディングとアイコン余白により縦幅が広く、タイトル右側に空白があるため、**ヘッダーを文字サイズに合わせてスリム化しつつ、同じヘッダー行内にリアルタイム同期ステータス／進捗％バッジを配置**する。
2. **大量高画質撮影時の OOM（メモリ不足）・ANR（画面フリーズ）根本対策**
   - 撮影直後の編集保存（サムネイル一括圧縮・MD5計算）、PC 同期時の画像読み込み、Coil によるプレビュー／サムネ表示、CameraX の連写バッファのそれぞれに潜むメモリスパイク要因を 5 本柱でまとめて解消する。

---

## 2. サブフェーズ構成と詳細仕様

### フェーズ 4-A：上部ヘッダースリム化 ＆ リアルタイム同期ステータス・進捗％表示

#### 1. 同期進捗・エラー状態モデルの導入 (`PcServerClient.kt`, `FleaMarketViewModel.kt`)
- **進捗モデル定義 (`PcServerClient.kt`)**
  - `SyncPhase` 列挙型：`IDLE`（待機中）、`SYNCING_ITEMS`（商品・削除情報送信中）、`UPLOADING_PHOTOS`（写真アップロード中）、`FETCHING_ITEMS`（PC差分取得中）、`SUCCESS`（完了）、`ERROR`（失敗・警告）
  - `PcSyncProgress` データクラス：
    - `phase: SyncPhase`
    - `currentPhotoIndex: Int`（何枚目を送信中か）
    - `totalPhotoCount: Int`（今回送信対象の写真総数）
    - `bytesSent: Long` / `totalBytes: Long`（全送信写真の累計バイト進捗）
    - `progressPercent: Int`（0〜100%）
    - `detailMessage: String`（短い状態説明）
    - `errorDetail: String?`（HTTPステータス、タイムアウト、Store ID不一致、ハッシュ参照エラー等の詳細）
    - `updatedAt: Long`
- **進捗通知の組み込み (`PcServerClient.kt`, `FleaMarketViewModel.kt`)**
  - `syncDeltaToPcServer` に `onProgress: (PcSyncProgress) -> Unit = {}` コールバックを追加し、メタデータ送信・写真1枚ずつのストリーミング進捗（バイト単位）・結果判定を逐次通知する。
  - `FleaMarketViewModel` に `syncStatusState: StateFlow<PcSyncProgress>` を公開し、`triggerAutoSync()`、再接続時ハンドシェイク、`syncDeltaToPcServer()` のすべての同期経路でリアルタイムに更新する。
  - 従来 `triggerAutoSync()` の `catch (_: Exception) {}` で握りつぶされていた失敗も `SyncPhase.ERROR` として詳細メッセージ付きで画面へ保持する。
  - 同期成功時は `SUCCESS` を数秒表示したのち `IDLE`（接続中・同期済バッジ）へ自然に戻る設計とし、エラー時はユーザーが再同期成功するまでエラー表示を保持して見落としを防ぐ。

#### 2. 一覧画面ヘッダースリム化 ＆ ステータスバッジ UI (`ItemListScreen.kt`)
- **上部ヘッダーのスリム化**
  - 外側 `MainActivity.kt` の `Scaffold` が既にシステムバー余白 (`paddingValues`) を消費しているため、`ItemListScreen.kt` 内の内側 `Scaffold` には `contentWindowInsets = WindowInsets(0)` を明示し、不要な二重インセット余白を排除する。
  - 上部 `Surface` 内 `Row` の `vertical = 10.dp` パディングを `vertical = 2.dp` へ削減し、タイトル文字サイズを `16.sp`、ゴミ箱アイコンボタンの視覚サイズをコンパクト化（タッチ領域 `48.dp` は `minimumInteractiveComponentSize()` で維持）することで、文字・アイコンに合った無駄のないスリムヘッダーにする。
- **ヘッダー内同期ステータスバッジ（タイトルとゴミ箱アイコンの間）**
  - **待機／接続状態**：未接続時は薄グレー小バッジ「未接続」、接続済・同期完了時は薄緑小バッジ「同期済」を表示。
  - **同期・写真送信中**：小型 `CircularProgressIndicator`（`12.dp`）＋「送信 65%」または「送信 65% (13/20)」をコンパクトに表示（複数枚・大容量でも今進んでいることが一目で判別可能）。
  - **エラー／参照警告発生時**：赤／アンバー系小バッジ「同期失敗 ⚠」（または「未取得あり ⚠」）を表示。
  - **タップで詳細ダイアログ表示**：ヘッダー上のステータスバッジに `clickable` + `testTag("header_sync_status_badge")` を付与し、タップすると以下の詳細ダイアログを開く。
    - 現在の接続先 PC 状態・最終同期成功時刻
    - 進行状況（枚数・送信バイト数）または直近のエラー原因全文（例：通信タイムアウト／HTTP 401・500／Store ID不一致／写真ハッシュ欠損）
    - ダイアログ内の「今すぐ再同期」ボタンによるワンタップリトライ

---

### フェーズ 4-B：メモリ安定化 ＆ クラッシュ防止フル実装（全 5 項目）

#### 1. 商品保存時のサムネイル圧縮＆MD5計算の完全バックグラウンド化 ＋ 連打防止 (`FleaMarketRepository.kt`, `FleaMarketViewModel.kt`, `ItemEditScreen.kt`)
- `FleaMarketRepository.saveItem(...)` 全体を `withContext(Dispatchers.IO)` で囲み、写真コピー、MD5 ハッシュ計算、`BitmapFactory` デコード、EXIF 回転補正、JPEG サムネイル書き出しをすべてバックグラウンドスレッドへ移行する（UI フリーズ／ANR 根絶）。
- `ItemEditScreen.kt` に `isSaving` 状態フラグを導入し、保存処理中は「保存する」ボタンを無効化してスピナー＋「写真処理・保存中...」を表示する。併せて `BackHandler` と戻るボタンも保存中はロックし、連打による多重メモリ確保や不完全保存を防止する。

#### 2. PC 写真送信の `file.readBytes()` 廃止 ＆ ストリーミングアップロード化 (`PcServerClient.kt`)
- `syncDeltaToPcServer` 内の `file.readBytes()`（ファイル全体をヒープ `ByteArray` に展開）を完全に廃止し、カスタム `StreamingFileRequestBody(file, mediaType, onProgressBytes)`（OkHttp `RequestBody` 継承クラス）を導入する。
- `contentLength()` は `file.length()` を返し、`writeTo(sink: BufferedSink)` 内で `FileInputStream` から 16KB バッファでチャンク読み出し・書き出しを行うことで、**送信中メモリ消費を約 16KB 固定に抑えつつ、1バイト単位の正確な％計算も同時に実現**する。
- `tools/FurimaServerNative.cs` 側の `POST /api/sync/photo` は `Content-Length` 付きの `application/octet-stream` ボディをそのまま受け取れるため、PC サーバー側のコード変更・exe再ビルドは不要（完全後方互換）。
- あわせて `downloadPhotoByHash(...)` においても一時ファイル／ストリーム処理への改善余地を整理し、不要な巨大配列の滞留を抑える。

#### 3. Coil 専用 `ImageLoader` 設定 ＆ 表示デコード解像度の上限指定 (`MainActivity.kt`, `PhotoStorageManager.kt`, `PhotoPreviewDialog.kt`, `CameraXCaptureDialog.kt`, `ItemEditScreen.kt`, `ItemDetailScreen.kt`)
- `MainActivity.kt`（アプリ起動初期）で `Coil.setImageLoader` によりアプリ専用 `ImageLoader` を1度だけ登録する：
  - `MemoryCache` の最大割合をヒープの **15%**（`maxSizePercent(0.15)`）に抑え、大量スクロールやプレビュー連打時のキャッシュ圧迫を防止。
  - `DiskCache` 上限を **64MB** 程度に制御。
  - `allowRgb565(true)` を有効化し、不透明 JPEG の表示メモリ消費を半減。
- 各画面の `AsyncImage` 呼び出しを `ImageRequest.Builder(context)` 化し、用途に応じたデコード上限サイズを明示する：
  - `CameraXCaptureDialog.kt` 左下サムネイル（`48.dp`）／`ItemEditScreen.kt` サムネイル列（`90.dp`）：`size(360)` ＋ `precision(Precision.INEXACT)`（撮影直後の生 JPEG をフル解像度デコードしない）。
  - `PhotoPreviewDialog.kt` 拡大プレビュー：`size(2048)` ＋ `precision(Precision.INEXACT)`（4倍ピンチズーム時も十分な解像度を保ちつつ 64MP 級のフル展開による OOM を防止）。
  - ※ DB・PC転送・ギャラリー保存される元画像ファイル自体は一切変更されない。

#### 4. CameraX 撮影負荷軽減 ＆ 撮影解像度モード切替・永続化 (`CameraXCaptureDialog.kt`)
- 連続撮影時のネイティブカメラメモリ負荷を抑えるため、撮影モードと解像度上限を最適化：
  - `ImageCapture` のキャプチャモードを、連写キューのメモリ消費が小さく高速な `CAPTURE_MODE_MINIMIZE_LATENCY` ベースへ調整。
  - カメラ上部コントロールバーに **保存解像度切替ボタン（`標準 2048px` ／ `フル解像度`）** を追加（デフォルトは連写安定性とフリマ出品実用画質を両立する `標準 2048px`、細部マクロ撮影時はワンタップで `フル解像度` へ切替可能）。
  - `標準 2048px` 選択時は撮影保存直後にバックグラウンド（`Dispatchers.IO`）で長辺最大 `2048px` へ安全リサイズ・EXIF回転反映済み JPEG として保存し（`PhotoStorageManager.downscaleCapturedPhotoIfNeeded`）、1枚あたり容量（約 8〜15MB ➔ 約 0.6〜1.2MB）・ネイティブ／ヒープメモリ・Wi-Fi 同期時間を劇的に削減する。
  - `CameraXCapturePreferences` にもこの解像度モードを保存し、「完全記憶型」として次回起動時も自動復元する。

#### 5. サムネイル生成 `RGB_565` 化 ＋ 一時キャッシュ掃除 ＋ `largeHeap` (`PhotoStorageManager.kt`, `FleaMarketViewModel.kt`, `AndroidManifest.xml`)
- `PhotoStorageManager.createThumbnail` / `createThumbnailFromBytes`：
  - 2 のべき乗で安全にサンプリング倍率を決める共通ヘルパー（`calculatePowerOfTwoSampleSize`）を導入し、`BitmapFactory.Options.inPreferredConfig = Bitmap.Config.RGB_565` を指定してデコードピークメモリを約 50% 削減。
  - 回転・縮小後の中間 `Bitmap` の `recycle()` を例外経路含め確実に実行。
- 一時キャッシュ自動掃除（`PhotoStorageManager.cleanupTemporaryCaches`）：
  - 共有用 ZIP キャッシュ（`cacheDir/shared_zips`）やプレビュー用原本一時キャッシュ（`cacheDir/preview_originals`）について、起動時に 24 時間以上経過したファイルおよび上限（合計 50MB）を超えた古いファイルから自動削除する。
- `AndroidManifest.xml`：
  - `<application>` に `android:largeHeap="true"` を設定し、連続高画質処理時の ART ヒープ上限に十分な安全マージンを持たせる（`targetSdk = 35` 等既存ビルド設定は変更しない）。

---

## 3. 編集・新規作成対象ファイル一覧

- `PLAN_SYNC_STATUS_AND_MEMORY.md` (**本計画書・新規**)
- `app/src/main/AndroidManifest.xml` (`android:largeHeap="true"` 追加)
- `app/src/main/java/com/example/myapplication/MainActivity.kt` (Coil 専用 `ImageLoader` 初期設定)
- `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt` (`RGB_565` 最適化・2のべき乗サンプル計算・撮影後任意2048pxリサイズ・一時キャッシュ自動掃除)
- `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt` (`saveItem` の `Dispatchers.IO` 完全移行)
- `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt` (`SyncPhase` / `PcSyncProgress`・`StreamingFileRequestBody` 導入・`readBytes` 廃止)
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt` (`syncStatusState` 公開・自動/手動同期進捗＆エラー伝播・起動時キャッシュ掃除)
- `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt` (ヘッダースリム化・`contentWindowInsets` 調整・ヘッダー内同期ステータスバッジ＆詳細/再試行ダイアログ)
- `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt` (`isSaving` 保存中ロック/進捗UI・サムネイル `ImageRequest` 上限設定)
- `app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt` (撮影負荷軽減・`標準2048px / フル解像度` 切替＆記憶・サムネイル `ImageRequest` 上限設定)
- `app/src/main/java/com/example/myapplication/ui/components/PhotoPreviewDialog.kt` (プレビュー `ImageRequest` 長辺 `2048px` 上限設定)
- `app/build.gradle.kts` (`versionCode = 4`)
- `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v1.0.0 (Build 4)`)
- `tools/DashboardHtml.cs` (`v1.0.0 (Build 4)`)
- `PROGRESS.md` (計画書リンク追記 ＆ Build 4 完了記録)

---

## 4. 検証計画

1. **ビルド検証**：`compile_applet` により Android Gradle ビルドおよびリソース・型整合性を確認する。
2. **単体テスト検証**：必要に応じてストリーミング計算・`calculatePowerOfTwoSampleSize`・キャッシュ掃除・`SyncPhase` 遷移の JVM 単体テストを実行し、既存の `Phase214TrashAndSyncTest` / `Phase215PhotoIntegrityTest` / `PhotoStorageManagerTest` と合わせて全件通過を確認する。
3. **実機配布 APK 更新**：`app-debug.apk` を `flea_market_app.apk` および `public/flea_market_app.apk` へ反映し、`PROGRESS.md` のバージョン統一ルール（Build 4）とともに GitHub `main` へ同期する。
