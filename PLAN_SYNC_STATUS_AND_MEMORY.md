# 統合実装計画書（フェーズ 4）：重大バグ修復 ➔ PC写真ギャラリー改良＆写真ゴミ箱（案A） ➔ 同期ステータス％表示＆メモリ安定化

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ ＋ PC母艦サーバー ＋ スマホ緊急Web管理画面）
現行バージョン：**`v1.0.0 (Build 6)`**（フェーズ 4-1・4-2・4-3 全完了済み）
改定日：**2026-11-15**（優先順位再編成 ＆ 案A採用による統合版）

> **進行・引き継ぎルール（必須）**
> - サブフェーズ（`4-1` / `4-2a` / `4-2b` / `4-3a` / `4-3b`）または重要ファイルの修正完了単位で本計画書（チェックボックス `[x]` と引き継ぎメモ）および `PROGRESS.md` を更新し、ローカルだけでなく **GitHub `main` へ即座にコミット・プッシュ** する。
> - 大幅な依存関係の変更や改修が無い限り、毎回の単体ビルドテストは省略可（各マイルストーン終端・必要時のみ `compile_applet` / テスト実施）。
> - バージョン更新時は `app/build.gradle.kts`・`tools/DashboardHtml.cs`・`app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` の3箇所を必ず同時に統一更新する。

---

## 0. 全体フェーズ構成と現在地

- [x] **フェーズ 4-1 (`v1.0.0 Build 4`) — 重大なバグ・エラーの緊急修復（最優先）** 【完了】
  - コミット `48a42ba` 混入バグの修復（JS構文エラー・ID再採番回避・型変換安全化・表記整合）
  - PC ⇄ スマホ間の写真0枚同期漏れ修復（`PcServerClient.kt`）
  - PC/スマホWeb管理画面のドロップゾーン二重クリック防止・XSS修正・フォルダ同期修復・C#未使用変数警告解消
- [x] **フェーズ 4-2 (`v1.0.0 Build 5`) — PCブラウザ管理画面の写真ギャラリー改良・原寸送り戻し・写真単位ゴミ箱（案A）** 【完了】
  - `4-2a` PC母艦バックエンド（`SyncStore.cs` / `FurimaServerNative.cs`）：`_photo_trash` 退避＋`photo_trash.json` 管理・一括ゴミ箱移動/復元/完全削除/空にするAPI・まとめ親/子商品対応・`syncedToPhone = false` + `BumpDataRevision()`
  - `4-2b` PCブラウザUI（`tools/DashboardHtml.cs`）：ギャラリー開時限定チェックボックス（右上配置）・全画面原寸ライトボックス（`<` / `>` ボタン、キーボード `←` / `→` / `Esc`、`1 / N` 表示、下部ミニサムネ帯、コピー/別タブ/ゴミ箱移動）・写真ゴミ箱モーダル（閲覧・復元・完全削除・空にする）
- [x] **フェーズ 4-3 (`v1.0.0 Build 6`) — 同期ステータス可視化 ＆ メモリ消費・アプリ安定性の抜本改善** 【完了】
  - `4-3a` 一覧画面ヘッダースリム化 ＆ リアルタイム同期ステータス・進捗％バッジ＆詳細ダイアログ
  - `4-3b` メモリ安定化 ＆ OOM・ANR防止（5本柱：保存IO完全移行＆ロック、16KBストリーミング送信、Coil上限設定、CameraX連写負荷軽減＆`2048px/フル`切替記憶、サムネ`RGB_565`＋一時キャッシュ掃除＋`largeHeap`）

---

## 1. 【フェーズ 4-1】重大なバグ・エラーの修復（最優先 / Target: `v1.0.0 Build 4`）【完了】

### 背景（コミット `48a42ba` 精査結果）
`48a42ba` はコミットメッセージ上「PC管理画面の写真ギャラリー機能拡張」となっているが、実際にはスマホ側緊急Webサーバーのみを変更し、JS構文エラーや写真ID再採番等の重大不具合を含んでいる。履歴削除ではなく、`48a42ba` で追加されたスマホ側ギャラリー送り戻しUIを活かした上で以下の通り修復し、既知の重大バグを一掃する。

### 実装タスクチェックリスト
- [x] **4-1-1: スマホWeb管理画面 JS 構文エラー解消 (`WebDashboardHtml.kt`)**
  - 1280〜1281行目付近の `closeImageViewer()` 直後に重複残留している `currentViewingPhoto = null; }` を削除する。
  - `<script>` ブロックを抽出し `node --check` で構文エラーゼロを確認する。
- [x] **4-1-2: スマホ側写真削除ロジックの安全化 (`ItemDao.kt`, `FleaMarketRepository.kt`, `LocalWebServer.kt`, `WebDashboardHtml.kt`)**
  - `FleaMarketRepository.kt` の `deletePhotoById(photoId)` / `deletePhotosByIds(photoIds)` が `saveItem(...)` を経由して残存写真の `PhotoEntity.id` を毎回再採番してしまう問題を修正する（残存写真の `id`・`md5Hash`・`isOriginalDeleted` 等を破壊せず、対象写真のみ削除＋未参照ファイル削除＋`displayOrder` 再採番＋`item.updatedAt` & `isDirty = true` 更新を行う安全な単体/一括削除処理へ統一）。
  - `LocalWebServer.kt`（598行目付近）の `POST /api/photos/delete-batch` における `body["photoIds"] as? List<Number>` を `as? List<*>` ＋ `.mapNotNull { (it as? Number)?.toInt() }` に変更し、不正ペイロードでの `ClassCastException` (500) を防止する。
  - スマホ緊急Web管理画面・ログ上の表記が物理削除の実態と矛盾しないよう文言を整合させる（「削除（端末・DBから削除）」等、復元できないことを明示）。
- [x] **4-1-3: PC ⇄ スマホ間の写真0枚同期バグ修復 (`PcServerClient.kt`)**
  - `mergeServerItemsFromJson`（907行目付近）の `if (newPhotoEntities.isNotEmpty())` 条件により、PC側で写真を最後の1枚まで削除してもスマホ側に旧写真が残ってしまうバグを修正する。
  - サーバー応答のアイテムに `photos` 配列が含まれている場合、`newPhotoEntities` が空（0枚）であっても `dao.deletePhotosByItemId(targetItemId)` を実行して0枚状態を正しく同期反映する（ローカル未同期写真 `!isPcSynced` がある既存スキップガードは維持）。
- [x] **4-1-4: PC/スマホWeb管理画面の操作不具合・XSS・フォルダ同期・警告修復**
  - **ドロップゾーン二重クリック防止** (`tools/DashboardHtml.cs` の `#photo-drop-file-input`, `#edit-photo-input`, `#add-photo-input`、および `WebDashboardHtml.kt` の `#form-photos`, `#restore-file-input`, `#folder-sync-input`)：`<input type="file">` に `onclick="event.stopPropagation()"` を付与し、親要素クリック時のファイル選択ダイアログ二重起動を防止する。
  - **HTMLエスケープ漏れ (XSS/表示崩れ) 修復** (`tools/DashboardHtml.cs` 2939行目付近 `openCleanupModal()`)：`${c.title}` および `${c.status}` / `${c.soldAtText}` / `${c.sizeFormatted}` に `escapeHtml(...)` を適用する。
  - **スマホWeb側フォルダ差分同期修復** (`LocalWebServer.kt` `WebPhotoDto` / `WebDashboardHtml.kt` `handleFolderSync`)：`WebPhotoDto` に `filePath: String`（または照合用ファイル名フィールド）を追加し、既存の `handleFolderSync` による重複判定が機能するようにする。
  - **C# コンパイル警告整理** (`tools/FurimaServerNative.cs` 1058行目付近)：未使用変数 `bool settingsChanged = false;` および代入箇所を削除し `warning CS0219` を解消する。
- [x] **4-1-5: バージョン統一更新 (`Build 4`) ＆ 検証**
  - `app/build.gradle.kts` (`versionCode = 4`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を **`v1.0.0 (Build 4)`** へ統一更新。
  - `node --check`（PC側・スマホ側双方の `<script>`）および `compile_applet` 実行・GitHub `main` コミット/プッシュ。

---

## 2. 【フェーズ 4-2】PCブラウザ管理画面の写真ギャラリー改良 ＆ 写真単位ゴミ箱（案A）（第2優先 / Target: `v1.0.0 Build 5`）

### 採用仕様（案A：本格的な写真単位ゴミ箱 `_photo_trash`）
PCブラウザ管理画面の写真ギャラリーを開いたとき限定で、サムネイルに削除用チェックボックス（左上以外＝右上配置）を表示し、選択写真をアイテムから外して PC側専用退避ディレクトリ（`_photo_trash`）＋管理JSON（`photo_trash.json`）へ移動できる。PCブラウザ上の「写真ゴミ箱」モーダルから元の商品への復元・完全削除・一括整理が可能。

### サブフェーズ 4-2a：PC母艦側 バックエンド実装 (`tools/SyncStore.cs`, `tools/FurimaServerNative.cs`) 【完了】
- [x] **4-2a-1: 写真ゴミ箱永続化ストア (`tools/SyncStore.cs`)**
  - `_syncDir` 配下に `_photo_trash/` ディレクトリおよび `photo_trash.json` を定義・読み書きするヘルパーを実装（ロック保護・破損時フォールバック付き）。
  - `PhotoTrashEntry` 構造（`trashId`, `originalItemId`, `originalItemUuid`, `originalItemTitle`, `originalChildItemId`, `originalChildItemTitle`, `md5Hash`, `fileName`, `originalFilePath`, `trashFilePath`, `displayOrder`, `trashedAt`, `fileSizeBytes`）を定義。
- [x] **4-2a-2: 写真単位ゴミ箱移動・復元・完全削除 API (`tools/FurimaServerNative.cs`)**
  - `ScanAndIndexLocalPhotos()` にて `_photo_trash` ディレクトリをインデックス対象外へ除外。
  - `POST /api/items/{id}/photos/delete` および `POST /api/photos/trash-batch` を実装し、商品（およびまとめ子商品）の `photos` リストから除外＋未参照ファイルのみ `_photo_trash` へ移動＋`syncedToPhone = false` + `BumpDataRevision()` を実行。
  - `GET /api/photo-trash`, `GET /api/photo-trash/{trashId}/image`, `POST /api/photo-trash/restore`, `POST /api/photo-trash/delete`, `POST /api/photo-trash/empty` を実装。

### サブフェーズ 4-2b：PCブラウザUI実装 (`tools/DashboardHtml.cs`) 【完了】
- [x] **4-2b-1: ギャラリー開時限定チェックボックス（右上配置）＆ 一括ゴミ箱移動 UI**
  - 一覧カードや編集モーダルではなく、`openPhotoViewer(itemId)` で開く `#photo-viewer-modal` 内のサムネイル（`renderSinglePhotoCard`）**のみ**にチェックボックスを表示する。
  - 左上（`top-1 left-1`）の `#1` 番号バッジと重ならないよう、**右上（`top-1 right-1`）** に大きめで視認性の高いチェックボックスを配置し、`onclick="event.stopPropagation(); toggleViewerPhotoSelect(...)"` でライトボックス起動やドラッグ操作への干渉を防ぐ。
  - ギャラリーヘッダーまたはサブツールバーに「すべて選択/解除」「選択した写真をゴミ箱へ移動 (N枚)」ボタンを追加。
  - まとめ売り親商品ギャラリーでは、各写真が持つ本来の所有 `itemId`（`photo.childItemId || currentViewerItemId` 等、DB構造に合わせて厳密特定）を保持してバックエンドへ送信する。
- [x] **4-2b-2: 全画面原寸ライトボックス（前の写真 / 次の写真 送り戻し機能）**
  - サムネイル画像クリックまたは各カード「原寸表示」ボタンから、従来の別タブ（`target='_blank'`）ではなく **PC管理画面内全画面ライトボックスモーダル** を開く（モーダル内に「別タブで開く」補助リンクも維持）。
  - スマホアプリ同等の操作性を実装：
    - 左右の大型 `<`（前の写真）／ `>`（次の写真）ボタン（先頭・末尾で適切に無効化/ループ制御）
    - キーボードショートカット：`ArrowLeft`（前へ）、`ArrowRight`（次へ）、`Escape`（ライトボックスのみ閉じる、開いていなければギャラリーを閉じる）
    - 上部バー：`1 / N` カウンター、ファイル名/内訳タイトル、「画像コピー」「別タブ」「この写真をゴミ箱へ」ボタン、閉じるボタン
    - 下部バー：ミニサムネイル一覧（現在表示中をハイライト・ワンクリックジャンプ可能）
- [x] **4-2b-3: 写真ゴミ箱モーダル UI（閲覧・復元・完全削除）**
  - PC管理画面上部またはサイドバーに「写真ゴミ箱（N）」ボタン（`openPhotoTrashModal()`）を追加。
  - 退避された写真サムネイル一覧、元商品名、削除日時、ファイルサイズを表示し、チェック選択または単体で「元の商品へ復元」「完全削除」「ゴミ箱を空にする」を実行可能にする。
- [x] **4-2b-4: バージョン統一更新 (`Build 5`) ＆ 検証**
  - `app/build.gradle.kts` (`versionCode = 5`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を **`v1.0.0 (Build 5)`** へ統一更新。
  - `node --check` による `DashboardHtml.cs` 内 JS 構文検証・C# コード整合確認・GitHub `main` コミット/プッシュ。

---

## 3. 【フェーズ 4-3】同期ステータス可視化 ＆ メモリ消費・アプリ安定性の抜本改善（第3優先 / Target: `v1.0.0 Build 6`）

従来の `PLAN_SYNC_STATUS_AND_MEMORY.md`（コミット `266f3d5`）の設計を、フェーズ 4-1 / 4-2 適用後の最新コードベースへ統合して実施する。

### サブフェーズ 4-3a：上部ヘッダースリム化 ＆ リアルタイム同期ステータス・進捗％表示 【完了】
- [x] **4-3a-1: 同期進捗・エラー状態モデルの導入 (`PcServerClient.kt`, `FleaMarketViewModel.kt`)**
  - `SyncPhase` 列挙型（`IDLE`, `SYNCING_ITEMS`, `UPLOADING_PHOTOS`, `FETCHING_ITEMS`, `SUCCESS`, `ERROR`）および `PcSyncProgress` データクラス（`phase`, `currentPhotoIndex`, `totalPhotoCount`, `bytesSent`, `totalBytes`, `progressPercent`, `detailMessage`, `errorDetail`, `updatedAt`）を定義。
  - `syncDeltaToPcServer` に `onProgress: (PcSyncProgress) -> Unit = {}` コールバックを追加し、メタデータ送信・写真1枚ずつのストリーミング進捗（バイト単位）・結果判定を逐次通知。
  - `FleaMarketViewModel` に `syncStatusState: StateFlow<PcSyncProgress>` を公開し、`triggerAutoSync()`、再接続時ハンドシェイク、`syncDeltaToPcServer()` の全経路でリアルタイム更新。
  - `triggerAutoSync()` 内の `catch (_: Exception) {}` サイレント握りつぶしを廃止し、`SyncPhase.ERROR` として詳細メッセージ付きで画面へ保持（再同期成功まで維持）。成功時は数秒後に `IDLE`（接続中・同期済）へ戻る。
- [x] **4-3a-2: 一覧画面ヘッダースリム化 ＆ ステータスバッジ UI (`ItemListScreen.kt`)**
  - 内側 `Scaffold` に `contentWindowInsets = WindowInsets(0)` を明示し、外側 `MainActivity.kt` との二重インセット余白を排除。
  - 上部 `Surface` 内 `Row` の縦パディングを `vertical = 2.dp` へ削減、タイトル `16.sp`、ゴミ箱アイコンボタン視覚サイズをコンパクト化（タッチ領域 `48.dp` は `minimumInteractiveComponentSize()` で維持）。
  - タイトルとゴミ箱アイコン間に同期ステータスバッジを配置（「未接続」「同期済」「送信 XX% (n/m)」「同期失敗 ⚠」等）＋ `testTag("header_sync_status_badge")`。
  - タップで詳細ダイアログ（接続先 PC 状態・最終同期時刻・進行バイト/枚数・直近エラー全文・「今すぐ再同期」ボタン）を開く。

### サブフェーズ 4-3b：メモリ安定化 ＆ クラッシュ防止フル実装（5本柱） 【完了】
- [x] **4-3b-1: 商品保存時のサムネイル圧縮＆MD5計算の完全バックグラウンド化 ＋ 連打防止 (`FleaMarketRepository.kt`, `FleaMarketViewModel.kt`, `ItemEditScreen.kt`)**
  - `FleaMarketRepository.saveItem(...)` 全体を `withContext(Dispatchers.IO)` で囲み、写真コピー・MD5計算・`BitmapFactory` デコード・EXIF補正・JPEG書き出しを完全バックグラウンド化。
  - `ItemEditScreen.kt` に `isSaving` フラグを導入し、保存中は「保存する」ボタン無効化＋スピナー＋「写真処理・保存中...」表示、および `BackHandler`/戻るボタンをロックして二重保存を防止。
- [x] **4-3b-2: PC 写真送信の `file.readBytes()` 廃止 ＆ ストリーミングアップロード化 (`PcServerClient.kt`)**
  - `syncDeltaToPcServer` 内の `file.readBytes()` を廃止し、16KB チャンク読み出しのカスタム `StreamingFileRequestBody(file, mediaType, onProgressBytes)` を導入（送信中メモリ約16KB固定＋1バイト単位％進捗、PC側 `POST /api/sync/photo` は無変更で完全互換）。
  - `downloadPhotoByHash(...)` 等の巨大配列滞留もストリーム/一時ファイル化で抑止。
- [x] **4-3b-3: Coil 専用 `ImageLoader` 設定 ＆ 表示デコード解像度の上限指定 (`MainActivity.kt`, `PhotoPreviewDialog.kt`, `CameraXCaptureDialog.kt`, `ItemEditScreen.kt`, `ItemDetailScreen.kt`)**
  - `MainActivity.kt` で `Coil.setImageLoader` によりアプリ専用 `ImageLoader`（`MemoryCache` ヒープ15%上限、`DiskCache` 64MB上限、`allowRgb565(true)`）を登録。
  - `AsyncImage` 呼び出しへ `ImageRequest.Builder(context)` でデコード上限を明示（`CameraXCaptureDialog` 左下サムネ / `ItemEditScreen` サムネ列：`size(360)` + `Precision.INEXACT`、`ItemListScreen` / `ItemDetailScreen` サムネ：`size(480)` + `Precision.INEXACT`、`PhotoPreviewDialog` 拡大プレビュー：`size(2048)` + `Precision.INEXACT`）。元画像ファイルは一切変更しない。
- [x] **4-3b-4: CameraX 撮影負荷軽減 ＆ 撮影解像度モード切替・永続化 (`CameraXCaptureDialog.kt`, `PhotoStorageManager.kt`)**
  - `ImageCapture` キャプチャモードを `CAPTURE_MODE_MINIMIZE_LATENCY` ベースへ調整。
  - 上部コントロールバーに保存解像度切替ボタン（`標準 2048px` ／ `フル解像度`、デフォルト `標準 2048px`）を追加し、`CameraXCapturePreferences` へ永続化。
  - `標準 2048px` 時は撮影直後に `Dispatchers.IO` で長辺最大 `2048px`（EXIF回転反映済 JPEG）へ安全リサイズ（`PhotoStorageManager.downscaleCapturedPhotoIfNeeded`）。
- [x] **4-3b-5: サムネイル生成 `RGB_565` 化 ＋ 一時キャッシュ掃除 ＋ `largeHeap` (`PhotoStorageManager.kt`, `FleaMarketViewModel.kt`, `AndroidManifest.xml`)**
  - `PhotoStorageManager.createThumbnail` / `createThumbnailFromBytes` へ 2 のべき乗サンプリング共通ヘルパー（`calculatePowerOfTwoSampleSize`）と `Bitmap.Config.RGB_565` を導入し、例外経路含め中間 `Bitmap.recycle()` を徹底。
  - `PhotoStorageManager.cleanupTemporaryCaches` で `cacheDir/shared_zips` および `cacheDir/preview_originals` の24時間超過・合計50MB超過ファイルを起動時に自動掃除。
  - `AndroidManifest.xml` の `<application>` に `android:largeHeap="true"` を追加（`targetSdk = 35` 維持）。
- [x] **4-3b-6: バージョン統一更新 (`Build 6`) ＆ 最終ビルド・テスト・APK更新**
  - `app/build.gradle.kts` (`versionCode = 6`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を **`v1.0.0 (Build 6)`** へ統一更新。
  - `compile_applet` ＋ JVM ユニットテスト通過後、`flea_market_app.apk` / `public/flea_market_app.apk` を更新し `PROGRESS.md` 更新の上で GitHub `main` へプッシュ。
