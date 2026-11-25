# 写真同期整合性バグ修正 ＆ 画面オフ耐性強化 実装計画書（最優先バッチ）

> **策定日**: 2026-11-20
> **対象バージョン**: **`v1.4` (Build 17)** / `versionCode = 1417`（現行 `v1.4 Build 16` の緊急バグ修正を Step 5 追加機能 `6-14` / `6-13` より優先して先行実施する）
> **関連計画の繰り下げ**:
> - 追加フェーズ 6-14（`PLAN_CLAUDE_SEND_PHASE6.md` §48）：`v1.5` Build 17 → **`v1.5 (Build 18)` / `versionCode = 1518`** へ繰り下げ
> - 追加フェーズ 6-13（`PLAN_CLAUDE_SEND_PHASE6.md` §47）：`v1.5` Build 18 → **`v1.5 (Build 19)` / `versionCode = 1519`** へ繰り下げ

---

## 1. 発生事象と調査で確定した根本原因（`v1.4 Build 16`）

### 1.1 観測された症状
- オフラインで約 120 枚を連続撮影（新規出品＋既存商品への写真追加）したあと PC サーバーと同期し、同期中の画面オフによる切断・手動再同期・同期中の追加撮影を経たところ、特定の商品（`サンキャッチャー6`）でリスト表示 `28枚`・実ファイル数 `16枚`（12 枚が表示不能な白枠）となった。
- 白枠の 12 枚はスマホ・PC 双方に残り、PC 側ギャラリーから削除しようとすると「MD5ハッシュ未確定の写真は移動できません（先に同期してください）」と拒否され、再同期しても消えずエラーも出ない。

### 1.2 コード精査で特定した根本原因（4 つの構造的欠陥）
1. **空 MD5（`md5Hash == ""`）・実ファイル欠損レコードの生成 (`FleaMarketRepository.saveItem`)**
   - `imageUris` 経路は `savePhotoFromUri` が 0 バイト／失敗時に `null` を返して弾くが、カメラ撮影や既存写真が通る `existingFilePaths` 経路（L254〜278）には「ファイル実在チェック」と「MD5 非空チェック」がない。
   - `existing == null` かつ `!File(path).exists()` の状態で保存が走ると、`calculateMd5(File(path))` が `""`、`createThumbnail(...)` が `null` を返してもそのまま `PhotoEntity(md5Hash = "", thumbnailPath = null)` が Room に挿入される。
2. **「同期中の編集・追加撮影」と「Step 3 原本削除」の競合 (`ItemEditScreen` × `PcServerClient` × `saveItem`)**
   - `ItemEditScreen` の再読込 `LaunchedEffect(lastSyncSuccessAt)`（L276〜289）は `existingFilePaths` を更新しない仕様である一方、裏で走る `syncDeltaToPcServer` の Step 3（L1010〜1025）が原本ファイルを `file.delete()` して Room の `filePath` を `thumbnailPath` へ書き換える（`markOriginalDeletedByFilePath`）。
   - その状態で「保存する」を押すと、画面側に残っていた削除済み原本パスで `existingByFilePath` が引けず `existing == null` となり、空 MD5・サムネ `null` の亡霊レコードが生成されたうえ、Room 側の正常なサムネイルまで `removedPhotos` 扱いで削除される。
   - また `saveItem()` が `syncMutex` 排他も Room `@Transaction` も持たないため、同期中の `mergeServerItemsFromJson` や `markItemSyncedIfUnchanged` と交錯し得る。
3. **空 MD5 を素通しして循環固定化する同期パイプライン**
   - **スマホ送信 (`PcServerClient.syncDeltaToPcServer` L744〜758)** が `md5 == ""` かつ `!file.exists()` でも `SyncPhotoDto(id, md5Hash = "")` を PC へ送る。
   - **PC 受信 (`FurimaServerNative.HandleSyncItems` L2227〜2255)** が `string.IsNullOrEmpty(md5Hash)` を弾かずに `itemsDb`（`items_data.json`）へ保存し、`neededHashes` にも含めず `200 OK` を返す。
   - **スマホ受信・自己修復 (`mergeServerItemsFromJson` L1400 / `repairMissingOrCorruptThumbnailsForItem` L1634)** も空 MD5 をスキップせず Room へ戻し、自己修復でも無言スキップするため、何度同期してもエラーにならず削除もできないループに入る。
4. **画面オフ切断と `isDirty` 先行解除の非原子性**
   - `AndroidManifest.xml` にフォアグラウンドサービス（`dataSync`）がなく、長時間の大量写真同期中に画面をオフにすると通信が中断されやすい。
   - `syncDeltaToPcServer` L885〜887 が Step 1（`POST /api/sync/items`）の直後、まだ Step 2（`POST /api/sync/photo`）が終わる前に `markItemSyncedIfUnchanged` で `isDirty = false` に戻してしまう。

---

## 2. 修正計画（5 本柱）

### 柱 1：空 MD5・実ファイル欠損写真の完全遮断と既存ゴーストデータの自動浄化
- **1-1. スマホ保存時の実在検証 (`FleaMarketRepository.kt`)**
  - `saveItem()` の `existingFilePaths` ループにおいて、`existing == null` のパスは `File(path).let { it.exists() && it.isFile && it.length() > 0L }` を満たさない場合スキップし、さらに `calculateMd5(File(path))` が空文字なら `PhotoEntity` を生成しない（除外件数をログへ記録し、UI へも異常を把握可能にする）。
- **1-2. スマホ送信時の空 MD5 除外とローカル孤立ゴースト掃除 (`PcServerClient.kt` / `ItemDao.kt`)**
  - `syncDeltaToPcServer()` の DTO 構築時、`md5` 補完後も `md5.isBlank()` なエントリ（実体ファイルもハッシュも無い壊れレコード）は `photoDtoList` へ絶対に含めない。
  - ローカル DB に `md5Hash.isBlank()` かつ `filePath`・`thumbnailPath` ともに実ファイルが存在しない完全ゴースト `PhotoEntity` が残っている場合は、同期前またはクリーンアップ処理で安全に除去して警告ログを残す（ファイルが実在する未計算写真には絶対に触れない）。
- **1-3. PC サーバー受信時の空 MD5 拒否と既存 `items_data.json` 浄化 (`FurimaServerNative.cs`)**
  - `HandleSyncItems()` の `photosArr` ループで `string.IsNullOrEmpty(md5Hash)` のエントリを `savedPhotosList` に追加せずスキップし、警告ログを出力する。
  - `LoadItemsDbFromDisk()`（サーバー起動時のロード直後）で `itemsDb` 全商品の `photos` を走査し、`md5Hash` が空文字かつ実ファイル参照不能な壊れエントリが検出された場合は、初回保護バックアップ（`items_data.json.bak_empty_md5`）を作成したうえで自動除去・再保存し、`BumpDataRevision()` する。これにより手作業での JSON 編集なしに既存の 12 枚（`サンキャッチャー6` 等）も起動だけで解消される。
- **1-4. スマホ受信・修復側の空 MD5 除外統一 (`PcServerClient.kt`)**
  - `mergeServerItemsFromJson()` でも `mergeServerPhotosAdditively()` と同じ `if (md5.isBlank()) continue` を適用し、万一サーバー応答に空 MD5 が混ざってもローカル Room へ取り込まない。

### 柱 2：「同期中の編集・追加撮影」と「Step 3 原本削除／マージ」の競合排除
- **2-1. `ItemEditScreen` のパス追従 (`ItemEditScreen.kt`)**
  - `LaunchedEffect(lastSyncSuccessAt)` で DB 再読込した際、既存の `existingFilePaths` 内の `oldPath` について対応する原本が Step 3 でサムネイルパス `newThumbPath` へ切り替わっていた場合（`loadedPhotosByPath[oldPath]?.id` 一致または `THUMB_${File(oldPath).name}` / `md5Hash` 照合）、`existingFilePaths`・`initialExistingFilePaths`・`selectedPhotoKeys`・`loadedPhotosByPath` のキーを `oldPath` → `newThumbPath` へ同期更新する。
  - `initialExistingFilePaths` も同時に差し替えることで、`hasUnsavedChanges` が誤って `true` にならないようにする。
- **2-2. `FleaMarketRepository.saveItem()` のトランザクション化と処理順見直し (`FleaMarketRepository.kt`)**
  - 画像コピー・リサイズ・サムネイル生成・MD5 計算などの重たいファイル I/O は、DB の `updateItem` より**前**に完了させる。
  - その後の DB 読み書き（既存レコード確認 → `insertItem`/`updateItem` → `deletePhotosByItemId` → `insertPhotos`）を `database.withTransaction` 内で原子的かつ短時間で完結させる（I/O 中に `items.updatedAt` だけ先に更新されて古い `photos` とズレる窓を無くす）。
  - さらに `saveItem()` 側でも `existing` 照合時に `THUMB_${File(path).name}` フォールバックを持たせ、万一パス切替と保存が交錯しても元の `md5Hash`・`thumbnailPath` を見失わないようにする。
- **2-3. 編集中アイテムの Step 3 原本削除ガード (`FleaMarketViewModel.kt` / `PcServerClient.kt`)**
  - `ItemEditScreen` を開いている間（`itemId != 0`）、編集対象の `itemId` を `ViewModel`（またはリポジトリ）のアクティブ編集中セットへ登録し、`DisposableEffect` で画面離脱時に解除する。
  - `syncDeltaToPcServer()` の Step 3（原本削除）は、対象商品の `isDirty == false` を DB で再確認し、かつアクティブ編集中セットに含まれている商品の原本は削除をスキップして次回の安全な同期へ回す。
  - DB 書き込みクリティカルセクション（`saveItem` のトランザクション確定・`removePhotoFromItem`・`mergeServerItemsFromJson`・`Step 3` 原本削除後更新）を軽量なリポジトリ側 `dbMutationMutex`（ネットワーク通信や画像エンコードでは保持しない）で直列化する。

### 柱 3：`isDirty` 解除タイミングの是正（Step 2 完了確認後に変更）
- **3-1. 写真アップロード完了ベースの `isDirty` 解除 (`PcServerClient.kt`)**
  - `syncDeltaToPcServer()` において、Step 1（`POST /api/sync/items`）直後の無条件 `markItemSyncedIfUnchanged` を廃止する。
  - Step 2（`POST /api/sync/photo`）完了後に、各アイテムについて「ローカルが持つ有効な全写真の `md5Hash` が `allConfirmedHashes`（`syncedHashesSet + newlyStoredHashes`）に含まれていること」を確認できたアイテムだけ `dao.markItemSyncedIfUnchanged(itemId, pushedUpdatedAt)` を呼ぶ。
  - 途中で画面オフ等により通信が切れて未送信写真が残ったアイテムは `isDirty = true` が維持され、次回再接続ハンドシェイクや手動同期で確実に再送される。

### 柱 4：画面オフ時の同期切断対策（`dataSync` Foreground Service 化）
- **4-1. マニフェストとサービス新設 (`AndroidManifest.xml`, `PcSyncForegroundService.kt`)**
  - `AndroidManifest.xml` に `FOREGROUND_SERVICE`・`FOREGROUND_SERVICE_DATA_SYNC`・`WAKE_LOCK`・`POST_NOTIFICATIONS` を追加し、`<service android:name=".data.server.PcSyncForegroundService" android:exported="false" android:foregroundServiceType="dataSync" />` を登録する（`targetSdk = 35` 準拠）。
  - `PcSyncForegroundService` を新設し、`ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC` 付き `startForeground` とタイムアウト付き `PARTIAL_WAKE_LOCK` を保持した状態で同期を実行し、通知バーへ進捗（`送信中 X/Y枚` 等）を反映し、終了時は `finally` で確実に `stopForeground` / `WakeLock.release()` / `stopSelf()` する。
- **4-2. ViewModel との状態統合 (`FleaMarketViewModel.kt`, `PcSyncScreen.kt`)**
  - 同期進捗（`StateFlow<PcSyncProgress>`）と `syncMutex` をアプリ単位の共有コーディネータ（または `Application` 保持コンポーネント）へ寄せ、手動同期および写真アップロードを伴う再接続本同期は `ContextCompat.startForegroundService` 経由で `PcSyncForegroundService` 上で実行する。
  - 軽量な 4 秒ハートビートや小規模自動差分確認は従来どおりバックグラウンドで過剰な通知を出さず動かし、実際に同期処理（特に複数枚の写真送信）が走る際に Foreground Service で保護する。
  - Android 13+ で `POST_NOTIFICATIONS` が未許可でも Foreground Service 自体の実行は止めず、同期画面から任意で通知許可を案内できるようにする（Play Policy 準拠）。

### 柱 5：PC ダッシュボードの救済削除経路（多層防御）
- **5-1. 壊れ参照の強制破棄フォールバック (`DashboardHtml.cs`, `FurimaServerNative.cs`)**
  - 万一 `md5Hash` が空の不正エントリが画面上に現れた場合でも手詰まりにならないよう、`DashboardHtml.cs`（`trashSelectedViewerPhotos` / `trashCurrentLightboxPhoto`）で空 MD5 の写真が選ばれた場合は「実体がない壊れた写真参照をリストから除去しますか？」の専用確認のうえ、`itemId`＋表示位置インデックス（または `photoId`）でリストから除外できる救済 API 経路を用意する（写真ゴミ箱へのファイル退避は行わない）。

---

## 3. 作業手順とチェックリスト

1. **Android データ・同期・競合修正**
   - `FleaMarketRepository.kt`：`saveItem()` のファイル I/O 先行化・`withTransaction` 化・`existingFilePaths` 実在＆MD5 非空チェック・サムネパス照合フォールバック・`dbMutationMutex` 導入・アクティブ編集中アイテム管理
   - `ItemDao.kt`：ローカル孤立ゴースト写真クリーンアップクエリ（必要に応じて追加）
   - `PcServerClient.kt`：空 MD5 送信除外・ローカル孤立ゴースト掃除・`mergeServerItemsFromJson` 空 MD5 スキップ・`isDirty` 解除条件の Step 2 完了後判定・Step 3 原本削除の編集中/`isDirty` ガード
   - `ItemEditScreen.kt`：アクティブ編集中 `itemId` 登録＋`LaunchedEffect(lastSyncSuccessAt)` での `existingFilePaths` / `initialExistingFilePaths` / `selectedPhotoKeys` 追従
2. **Android Foreground Service 化**
   - `AndroidManifest.xml`：権限（`FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `WAKE_LOCK`, `POST_NOTIFICATIONS`）とサービス宣言追加
   - `PcSyncForegroundService.kt` ＆ 同期状態共有：進捗通知・`PARTIAL_WAKE_LOCK`・`dataSync` 種別指定・安全な終了処理
   - `FleaMarketViewModel.kt` / `PcSyncScreen.kt`：Foreground Service 起動連携と通知許可導線（任意）
3. **PC サーバー・ダッシュボード修正**
   - `tools/FurimaServerNative.cs`：`HandleSyncItems` での空 MD5 拒否、`LoadItemsDbFromDisk` での初回バックアップ付き空 MD5 掃除、壊れ写真参照の救済除去 API 対応
   - `tools/DashboardHtml.cs`：`md5Hash` 空エントリの救済除去 UI（確認付き）
4. **バージョン統一更新 (`v1.4 Build 17`) ＆ 検証**
   - `app/build.gradle.kts`：`versionCode = 1417`, `versionName = "1.4"`
   - `tools/DashboardHtml.cs`・`WebDashboardHtml.kt`：`v1.4 (Build 17)`
   - テスト追加・実行：Android（Robolectric / JUnit で `saveItem` 存在チェック・Step 3 競合・`isDirty` 維持・空 MD5 遮断テスト）、C# ビルド (`csc` / `tools/build-exe.bat` 相当)、JS 構文・ロジックテスト (`node --check` / `tools/tests/`)、Android ビルド (`compile_applet` / `gradle testDebugUnitTest`)

---

## 4. 対象外（本修正では触らない範囲）
- Step 5 の追加機能（`6-14` 送信順統一・PC ライトボックスチェックボックス、`6-13` 参考画像フォルダ・ピッカー）の実装（本修正 `v1.4 Build 17` 完了後にそれぞれ `v1.5 Build 18`・`Build 19` として着工）
- `PROGRESS.md` の将来改善バックログ（3 秒ポーリング軽量化・`ServePhotoByHash` ロック外 I/O 化・ファイル分割等）
