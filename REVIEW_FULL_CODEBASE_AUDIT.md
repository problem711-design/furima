# フリマ出品管理アプリ 全体コードレビュー報告書 (Full Codebase Audit)

- **監査対象リポジトリ**: `https://github.com/problem711-design/furima.git` (`main`)
- **検査ベースライン**: `5922ae5` (`feat(pc): §51 PC 管理画面 UI/UX 改善・レイアウト不具合是正を実装`)
- **監査担当**: Code Review Officer
- **監査範囲**: Batch 1 〜 Batch 8（PC サーバー C# / WinForms / Web UI / JS テスト / Android Kotlin & Compose 全層 / Gradle / Manifest / リポジトリ衛生）
- **ステータス**: **全バッチ検査完了 (100%)**

---

## 1. 検査ベースライン & メトリクス

### 1.1 コードベース規模 (`5922ae5` 時点)
| 領域 | ファイル数 | 総行数 | 主な巨大ファイル |
| --- | ---: | ---: | --- |
| PC サーバー / C# (`tools/*.cs`) | 6 | 16,606 | `FurimaServerNative.cs` (7,768), `DashboardHtml.cs` (5,522), `TunnelQrForm.cs` (1,194), `SyncStore.cs` (1,030), `ClaudeJobStore.cs` (893) |
| PC JS テスト (`tools/tests/*.mjs`) | 3 | 276 | `claude-label.test.mjs` (118), `photo-sync-integrity-build17.test.mjs` (98), `dashboard-ui-phase6-16.test.mjs` (60) |
| Android 本番コード (`app/src/main/java/**/*.kt`) | 43 | 21,210 | `PcServerClient.kt` (1,847), `FleaMarketViewModel.kt` (1,730), `ItemEditScreen.kt` (1,696), `CameraXCaptureDialog.kt` (1,479), `WebDashboardHtml.kt` (1,332), `ItemListScreen.kt` (1,261), `FleaMarketRepository.kt` (1,203), `SettingsStorageScreen.kt` (1,182) |
| Android テスト (`app/src/test/**/*.kt`) | 8 | 1,393 | `PhotoSyncIntegrityBuild17Test.kt` (492), `Phase214TrashAndSyncTest.kt` (345), `Phase215PhotoIntegrityTest.kt` (267) |
| **合計 (ソース + テスト)** | **60** | **39,485** | 1,000 行超のファイルが **10 本**（全体の約 **60.8%** を占有） |

### 1.2 テスト実行結果・静的検証状況
- **JS 単体テスト (`node --test tools/tests/*.mjs`)**: **18/18 PASS** (`294ms`) — 実行確認済み。
- **Android 単体 / Robolectric テスト (`app/src/test/`)**: 全 8 ファイル (1,393 行) を**静的精読済み**（Android SDK / Gradle ビルドはこのレビュー環境では未実行）。
- **C# (.NET 4.x `csc.exe`)**: ソース精読済み（Windows 環境専用のためローカルビルド未実行）。

---

## 2. バッチ別・ファイル別 レビュー進捗表

| Batch | 対象領域 | 主な検査対象ファイル（行数） | 状態 |
| --- | --- | --- | --- |
| **Batch 1** | PC サーバー基盤・設定ストア | `tools/FurimaSyncNative.cs` (199), `tools/SyncStore.cs` (1,030), `tools/FurimaServerNative.cs` (起動/ルーティング/認証/永続化) | **[完了]** |
| **Batch 2** | PC サーバー同期・CRUD・写真配信 | `tools/FurimaServerNative.cs` (同期/CRUD/まとめ/ゴミ箱/写真配信) | **[完了]** |
| **Batch 3** | Claude ジョブ・Muse/Ref・WinForms UI | `tools/ClaudeJobStore.cs` (893), `tools/FurimaServerNative.cs` (Muse/Reference), `tools/TunnelQrForm.cs` (1,194) | **[完了]** |
| **Batch 4** | Android DB・モデル・ストレージ・内蔵Web | `ItemEntity.kt` (77), `PhotoEntity.kt` (47), `AppDatabase.kt` (64), `ItemDao.kt` (158), `DefaultSettings.kt` (72), `ItemWithPhotos.kt` (13), `SalesSummary.kt` (8), `DefaultSettingsManager.kt` (123), `CsvExporter.kt` (145), `SyncDtos.kt` (226), `NetworkUtils.kt` (45), `ClaudeLabelDerivation.kt` (92), `PhotoStorageManager.kt` (801), `LocalWebServer.kt` (740) | **[完了]** |
| **Batch 5A** | Android Repository & PC クライアント | `FleaMarketRepository.kt` (1,203), `PcServerClient.kt` (1,847) | **[完了]** |
| **Batch 5B** | Android 同期サービス・Activity・VM | `SyncCoordinator.kt` (176), `PcSyncForegroundService.kt` (443), `MainActivity.kt` (327), `FleaMarketViewModel.kt` (1,730) | **[完了]** |
| **Batch 6** | Web ダッシュボード & JS テスト | `WebDashboardHtml.kt` (1,332), `tools/DashboardHtml.cs` (5,522 ※全関数索引・主要ハンドラ精読＋二次モーダルスポット精査), `tools/tests/*.mjs` (276) | **[完了]** |
| **Batch 7A** | Android UI (撮影・編集) | `CameraXCaptureDialog.kt` (1,479), `ItemEditScreen.kt` (1,696) | **[完了]** |
| **Batch 7B** | Android UI (一覧・詳細・ゴミ箱・分析) | `ItemListScreen.kt` (1,261), `ItemDetailScreen.kt` (784), `TrashScreen.kt` (591), `SalesAnalysisScreen.kt` (554) | **[完了]** |
| **Batch 7C** | Android UI (設定・同期・ダイアログ・テーマ) | `SettingsStorageScreen.kt` (1,182), `PcSyncScreen.kt` (742), `PhotoPreviewDialog.kt` (690), `ClaudeSendDialog.kt` (580), `ItemQuickActionDialog.kt` (413), `MergeItemsDialog.kt` (355), `BatchEditDialog.kt` (329), `DefaultSettingsDialog.kt` (242), `ClaudeApprovalDialog.kt` (197), `ListingValidationBanner.kt` (118), `SummaryHeaderCard.kt` (105), `ClaudeCompletionDialog.kt` (65), `ClaudeJobCancelDialog.kt` (55), `ui/theme/{Color,Theme,Type}.kt` (100) | **[完了]** |
| **Batch 8** | ビルド設定・テスト網羅性・リポ衛生 | `AndroidManifest.xml` (66), `app/build.gradle.kts` (132), `gradle/libs.versions.toml` (111), `tools/build-exe.bat`, `strings.xml`, `.gitignore`, Android テスト 8 ファイル (1,393), ルート構成 | **[完了]** |

---

## 3. 検出課題一覧（重要度別）

凡例：`[バグ]` 機能不具合 / `[安定化]` クラッシュ・データ保全・並行性 / `[軽量化]` CPU・I/O・ロック・電力 / `[セキュリティ]` 認証・境界検証 / `[保守性]` デッドコード・ビルド・衛生

---

### High（最優先：クラッシュ・データ破損・セキュリティ直結）

#### `AUDIT-001` [安定化 / バグ] `FurimaServerNative.HandleRequest` の `try` ブロック外での未捕捉例外によるサーバー墜落リスク
- **対象**: `tools/FurimaServerNative.cs` 1460 行付近、`1678–1707` 行
- **事象**: `HandleRequest` 内で `res.Headers.Add(...)`、`AuthenticateRequest(ctx)`（1460 行で `req.RemoteEndPoint.Address` を null チェックなしで参照、1554 行と非対称）、`req.Url.AbsolutePath`（1700 行）が `try {`（1707 行）の**手前**で実行されている。`HttpListenerRequest` の切断時や `RemoteEndPoint == null` の場合に .NET ThreadPool 上で未捕捉例外となり、プロセス全体が異常終了する危険がある。
- **推奨修正**: `HandleRequest` の先頭（`ctx` 受領直後）から全体を外側 `try-catch` で囲み、`RemoteEndPoint?.Address` の null 安全化と、切断時 `HttpListenerException` / `ObjectDisposedException` の握りつぶし（ログのみ）を実装する。

#### `AUDIT-002` [安定化 / データ保全] PC 側全 JSON ストアの非アトミック書き込みと破損時の破壊的フォールバック
- **対象**:
  - `tools/FurimaServerNative.cs` `1221–1242` (`LoadItemsDbFromDisk`), `1396–1397` (`SaveItemsDbToDisk`)
  - `tools/SyncStore.cs` `179, 423, 467, 488–560, 925`
  - `tools/ClaudeJobStore.cs` `202, 726`
- **事象**:
  1. すべての JSON 保存が `File.WriteAllText(path, json)` の直接上書きであり、書き込み中の電源断・クラッシュ・強制終了でファイルが 0 バイトまたは中途半端な JSON に破損する。
  2. さらに `SyncStore.Initialize`（488–560 行）はデシリアライズ失敗時にエラーを握りつぶして新規 `storeId` と新規 `authToken` を生成し `store.json` を上書きするため、**スマホ側とのペアリング情報が消滅し、定型文・プリセット・信頼済み IP も消失**する。`LoadItemsDbFromDisk`（1221–1242 行）も空 DB にフォールバックするため、次回保存で全商品レコードが消失するリスクがある。
- **推奨修正**:
  1. 共通ヘルパー `AtomicFileWriter.WriteTextAtomic(string path, string content)` を導入し、`path + ".tmp"` へ `FileStream` で書き込み + `Flush(flushToDisk: true)` → 旧ファイルが存在すれば `path + ".bak"` へ退避しつつ `File.Replace`（または `Move`）する。
  2. 起動時の読み込み失敗時は `.bak` からの自動復旧を試み、それでも復旧不能な場合は破損ファイルを `.corrupt.<timestamp>` に退避し、安易に上書きせず起動警告またはエラー通知を行う。

#### `AUDIT-003` [セキュリティ / バグ] `LocalWebServer.kt` の無認証 HTTP 公開・無制限 CORS・マルチパートパストラバーサル
- **対象**: `app/src/main/java/com/example/myapplication/data/server/LocalWebServer.kt` `101–111`, `120–668`, `180–186`, `220`, `300–303`, `62–63`
- **事象**:
  1. `0.0.0.0:8080` で待ち受けつつ `install(CORS) { anyHost() }` を有効化しており、`DELETE /api/items/{id}`、`deletePhotosByIds`、`merge`、`unmerge`、`restore` など破壊的エンドポイントに **Bearer トークン / PIN 認証が一切ない**。同一 Wi-Fi 上の任意端末・ブラウザ上の悪意あるページ（CSRF）から商品削除が可能。
  2. `POST /api/items`（300–303 行）で `part.originalFileName` をサニタイズせず `File(tempDir, "${System.currentTimeMillis()}_$fileName")` に連結しており、`../` を含むファイル名で `web_uploads` 外への任意ファイル書き込みが可能。
  3. `PUT /api/items/{id}`（180–186 行）と `POST /api/items/{id}/status`（220 行）が `status` 文字列のホワイトリスト検証や価格の下限（`>= 0`）検証をしていない。
  4. `addLog`（62–63 行）の `_logs.value = _logs.value + ...` が非アトミックで、並行リクエスト時にログが欠落する。
- **推奨修正**:
  1. `LocalWebServer` でも起動時にワンタイムまたは永続の PIN / Bearer トークン検証を破壊的エンドポイント（POST/PUT/DELETE）に導入し、CORS を自ホスト・ローカルオリジン限定またはトークンヘッダ必須にする。
  2. `originalFileName` は拡張子ホワイトリスト（`.jpg/.jpeg/.png/.webp`）+ UUID リネームし、`canonicalFile.startsWith(tempDir.canonicalFile)` を検証する。
  3. `status` と価格フィールドを検証し、`addLog` を `_logs.update { ... }` へ変更する。

---

### Medium-High（並行性競合・顕著なパフォーマンス劣化・主要データ整合性）

#### `AUDIT-004` [並行性 / 安定化] `FurimaServerNative.cs` における `lockObj` 外での `itemsDb` / `hashToFilePathMap` 参照
- **対象**: `tools/FurimaServerNative.cs` `4626`, `5061–5064`, `429–430` 付近
- **事象**: `TrashPhotosFromTargets`（4626 行）や `HandleRestorePhotoTrash`（5061–5064 行）が `lock (lockObj)` を解放した後で `qrForm.UpdateStatusInfo(hashToFilePathMap.Count, SyncStore.CountMissingFiles(itemsDb, hashToFilePathMap))` を呼び出している。同時並行で同期ハンドラが `itemsDb` や `hashToFilePathMap` を書き換えると、`InvalidOperationException`（列挙中のコレクション変更）または不整合値読み取りが発生する。
- **推奨修正**: カウント計算を `lock (lockObj)` 内でスナップショット値（`int photoCount, missingCount`）として算出してから `BeginInvoke` で UI スレッドへ渡す。

#### `AUDIT-005` [軽量化 / ロック競合] PC サーバー側の `lock (lockObj)` 内における過大な同期 I/O・再帰走査・全量クローン
- **対象**:
  - `tools/FurimaServerNative.cs` `1805–1824` (`/api/server/status`), `2453` (`HandleSyncItems`), `7728–7746` (`ServePhotoByHash`), `3390–3406` (`BatchUpdateItems`)
  - `tools/SyncStore.cs` `285–286` (`GetDefaultSettings`)
- **事象**:
  1. `/api/server/status` が PC 管理画面から 3 秒ごとにポーリングされ、毎回 `ClaudeJobStore.GetJobs(null)` で全ジョブをディープクローンし、さらに `lock (lockObj)` 内で `CountMissingFiles` により全アクティブ商品の写真パスへ `File.Exists` を同期実行する。
  2. `HandleSyncItems`（2453 行）がスマホからの商品 Push のたびにローカル写真フォルダ全体の `ScanAndIndexLocalPhotos()` を同期実行する。
  3. `ServePhotoByHash`（7728–7746 行）が `lock (lockObj)` を保持したまま `File.ReadAllBytes` と HTTP ストリーム送信を行い、かつレスポンスに `Cache-Control: public, max-age=31536000, immutable` がないため、管理画面側の再描画ごとに画像再読込が発生する。
  4. `BatchUpdateItems`（3390–3406 行）が `lock (lockObj)` 内で各商品の `商品情報_説明文.txt` へ同期書き込みしている（3554 行と改行コードも `\r\n` と `\n` で不統一）。
  5. `SyncStore.GetDefaultSettings` が呼び出しごとに JSON シリアライズ＋デシリアライズでディープクローンしている。
- **推奨修正**:
  1. `ServePhotoByHash` はロック内で「ファイルパス解決のみ」を行い、ファイル読込とレスポンス書込はロック外で実施する。さらに MD5 ハッシュ指定リクエストには `Cache-Control: public, max-age=31536000, immutable`（および `ETag`）を付与する。
  2. `/api/server/status` の `missingFiles` 集計はインメモリキャッシュまたは変更時刻ベースのダーティフラグ化し、3 秒ごとの全ファイル `File.Exists` を廃止する。`GetJobs` もステータス集計専用の軽量メソッドを分離する。
  3. `HandleSyncItems` の全フォルダ再スキャンは前回スキャンからの経過時間デバウンス（例：30 秒）またはオンデマンド実行にする。
  4. `商品情報_説明文.txt` の書き込みはロック外（スナップショット生成後）に移動し、改行コードを `\r\n` へ統一する。
  5. `DefaultSettings` は不変コピーコンストラクタ（または C# の手動 `Clone()`）に置換する。

#### `AUDIT-006` [軽量化 / ロック競合] Android `mergeServerItemsFromJson` が DB ロック＋トランザクション内で画像デコード・MD5 計算を実行
- **対象**: `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt` `1385–1653`, `1760–1778`
- **事象**: `withDbMutationTransaction`（`dbMutationMutex` と SQLite 書込トランザクションの双方を保持）の内部で、`isPhotoThumbnailDamagedOrMissing` を呼び出し、`BitmapFactory.decodeFile` や原本の `calculateMd5` を同期実行している。写真枚数が多い環境では同期中に SQLite が長時間ロックされ、UI 操作（保存・削除・ステータス変更）がハングする原因となる。
- **推奨修正**: 画像修復候補の検査・サムネイル再生成・MD5 算出はトランザクション**外**で事前／事後フェーズとして実行し、トランザクション内は純粋な DB 読み書きのみに短縮する。

#### `AUDIT-007` [軽量化 / バグ] `FleaMarketViewModel` の二重 4 秒ポーリング・Main スレッドでの `File.exists()`・ソート未追従
- **対象**: `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt` `161–169`, `194–239`, `1441–1599`
- **事象**:
  1. `startHeartbeatLoop`（4 秒間隔）の `/api/server/status` レスポンスに既に `claudeRevision` が含まれているにもかかわらず、別コルーチンの `startClaudeWatchLoop`（同じく 4 秒間隔）が無条件に `/api/claude/revision` をポーリングしており、PC サーバーへの常時リクエスト数と起床頻度が 2 倍になっている。
  2. `unsyncedItemsCount`（161–169 行）の `combine` 内で全写真の `File(it.filePath).exists()` を実行しているが `.flowOn(Dispatchers.IO)` がなく、Collector 側（Main）や頻繁な再評価でディスク I/O が発生する。
  3. `filteredItems`（194–239 行）の `SortOption.PROFIT_DESC` が `defaultSettings.value` を参照しているが `combine` の入力に `defaultSettings` が含まれていないため、デフォルト手数料・送料を変更しても利益順ソートが再計算されない。
- **推奨修正**:
  1. `startHeartbeatLoop` が稼働中は `startClaudeWatchLoop` を停止（またはハートビート内の `claudeRevision` 変化検知に一本化）し、ハートビート未接続時または明示監視時のみ `/api/claude/revision` を使う。
  2. `unsyncedItemsCount` に `.flowOn(Dispatchers.IO)` を付与する（可能なら DB/メモリキャッシュ値へ）。
  3. `filteredItems` の `combine` に `defaultSettings` Flow を追加する。

#### `AUDIT-008` [データ整合性 / セキュリティ] `MergeItems` バリデーション不足と `PhotoStorageManager` の原本削除順序・日本語ファイル名衝突
- **対象**:
  - `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt` `750–806` (`mergeItems`)
  - `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt` `240–257`, `361–367`, `554–555`
- **事象**:
  1. `mergeItems` が `childItemIds.distinct().size >= 2` や、`trashed` / `MERGED` / `SOLD` / `SHIPPING` 状態の除外チェックを持たない（`LocalWebServer` `/api/items/merge` 経由で不正マージが可能）。
  2. `downscaleCapturedPhotoIfNeeded`（361–367 行）で、`renameTo` が失敗した場合に元ファイル `file.delete()` を**コピー成功確認前**に実行するパスがあり、容量不足時に写真が消失し得る。
  3. `createThumbnailFromBytes`（554–555 行）が `[^a-zA-Z0-9_.-]` を `_` に置換するため、日本語ファイル名ベースでサムネイルを作ると `THUMB_____.jpg` 等で衝突する危険がある。
  4. `savePhotoFromUri`（240–257 行）は `.jpg` 固定かつ無縮小で保存＋`MediaStore` にも複製するためストレージ消費が大きい。
- **推奨修正**: `mergeItems` に商品数（重複除去後 2 件以上）およびステータス・ゴミ箱除外の検証を追加する。`downscaleCapturedPhotoIfNeeded` は新ファイル書き込み・検証成功後のみ元ファイルを削除する。`createThumbnailFromBytes` には必ず一意 ID / ハッシュ先頭を付与する。

#### `AUDIT-009` [バグ / データ喪失] `ItemEditScreen.kt` の `remember` 使用による Activity 再生成時の入力消失・一時写真リーク・`LazyRow` key 欠如
- **対象**: `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt` `140–173`, `230–272`, `449–463`, `969`, `1104`, `1123`, `1187`
- **事象**:
  1. `titleFieldValue`、`descriptionFieldValue`、`priceText`、`soldPriceText`、`status`、`existingFilePaths`、`newImageUris`、`createdTempCameraPaths` がすべて `remember`（`rememberSaveable` ではない）で保持されており、標準カメラアプリ起動時（プロセスキル／Activity 再生成）や画面回転時に `LaunchedEffect(itemId)`（230–272 行）が走り、編集中だった文字列・追加写真が DB の旧値で上書き消去される。
  2. `handleBackRequest` / `BackHandler`（449–463 行）は `hasUnsavedChanges == false` ならクリーンアップダイアログを出さずに戻るため、「撮影 → 編集画面内で撮影写真を削除 → 戻る」とした場合、`createdTempCameraPaths` に残ったファイルが削除されず孤立ファイル化する。
  3. `LazyRow`（969 行 `existingFilePaths`, 1123 行 `newImageUris`）に `key` が未指定であり、先頭写真を削除すると後続の写真アイテムの状態（サムネイル・削除状態）がズレる。また削除ボタン `Box`（1104 行, 1187 行）が `26.dp` でアクセシビリティ最小タッチターゲット `48.dp` を下回っている。
- **推奨修正**: 入力状態（または編集ドラフト状態）を `ViewModel` または `rememberSaveable` へ退避し、`LaunchedEffect(itemId)` の初期ロードを「未初期化時のみ」に制限する。また戻る時は `hasUnsavedChanges` に関わらず未保存の `createdTempCameraPaths` を確実にクリーンアップし、`LazyRow` に一意な `key` を指定、削除ボタンのタッチ領域を `48.dp`（視覚サイズは維持しパディング拡大）にする。

#### `AUDIT-010` [バグ / セキュリティ] PC・Android 両 Web ダッシュボードの DOM 未更新・写真削除不能・XSS/トークン欠落
- **対象**:
  - `tools/DashboardHtml.cs` `1363`, `3958`, `3969`, `4994–4998`
  - `tools/FurimaServerNative.cs` `2516–2562`, `4140–4182`, `6301–6304`
  - `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` `475`, `503`, `803`
- **事象**:
  1. `DashboardHtml.cs` 4994–4998 行 (`deleteEditPhotoByIdx`) が `if (!md5Hash) return;` で早期リターンするため、MD5 が空の破損写真エントリを編集モーダルから削除できない。
  2. `DashboardHtml.cs` 3958, 3969 行 (`renderSinglePhotoCard`) で `md5Hash` 空時に `photo.photoUrl` をインライン JS (`ondragstart`/`onclick`) へ未エスケープ展開している。また 1363 行 (`copyPhotoBlob`) の `fetch` に `getHeaders()`（Bearer トークン）が付いていない。
  3. `FurimaServerNative.cs` の `HandleSyncPhoto`（2516–2558 行）と `HandleAddPhoto`（4140–4180 行）がサイズ上限チェックなしで `MemoryStream` に読込、かつ `IsValidImageHeader` 検証なしで `.jpg` フォールバック保存しており、保存後に `photoStatCache` も更新していない。さらに `ServeReferenceThumb`（6301–6304 行）は並列アクセス時に同一の `thumbPath + ".tmp"` を奪い合い競合する。
  4. `WebDashboardHtml.kt` 475 行 (`fetchItemsSilent`) の変更検知ハッシュに `title`/`description`/`soldPrice`/`updatedAt` が含まれないため、詳細や価格を変えても一覧が自動再描画されない。503 行 (`updateStats`) は `SHIPPING` を売却数に含めず `DRAFT`/`DRAFT_DONE` も抜けている。803 行 (`handleFolderSync`) はファイル名のみで重複判定するため別商品の同名ファイル（`IMG_001.jpg` 等）を取りこぼす。
- **推奨修正**:
  - `deleteEditPhotoByIdx` は `idx` ベース削除フォールバックを許可し、`renderSinglePhotoCard` は `data-*` 属性＋イベント委譲または厳格な JS エスケープへ変更、`copyPhotoBlob` に `getHeaders()` を付与する。
  - `FurimaServerNative.cs` の画像アップロード 2 箇所に最大サイズ制限（例：50MB）・`IsValidImageHeader` 検証・`photoStatCache` 更新を追加し、`ServeReferenceThumb` の一時ファイル名に `Guid.NewGuid().ToString("N")` を含める。
  - `WebDashboardHtml.kt` の `fetchItemsSilent` ハッシュ・`updateStats` 集計区分・`handleFolderSync`（MD5 ベース重複排除）を修正する。

---

### Medium（UI 不具合・メインスレッド I/O・例外処理・UX・リポジトリ衛生）

#### `AUDIT-011` [バグ] `ItemListScreen.kt` で選択不可な `MERGED` 商品を選択モード中にタップ選択できてしまう
- **対象**: `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt` `155–157`, `652–657`, `817`
- **事象**:
  1. 817 行で `MERGED` 商品は `isSelectable = false`（半透明・チェックボックス無効）として描画されるが、652–653 行のカード `onClick` は `if (isSelectionMode) { viewModel.toggleItemSelection(...) }` のみで `isSelectable` を検査していないため、タップすると `MERGED` 子商品が一括編集・一括削除対象に入ってしまう。
  2. 155–157 行の `LaunchedEffect(searchQuery, statusFilter, sortOption) { displayLimit = 20 }` は詳細画面や編集画面から一覧画面へ戻って再コンポーズされた際にも発火し、21 件目以降を閲覧中だった位置・件数が 20 件へリセットされる。
- **推奨修正**: `if (isSelectionMode) { if (isSelectable) viewModel.toggleItemSelection(...) }` に修正し、`displayLimit = 20` のリセットは検索・フィルタ条件が**実際に前回値から変化した時**のみ実行するか `ViewModel` 側で保持する。

#### `AUDIT-012` [軽量化] UI コンポーザブル群の描画・スクロール中の Main スレッド同期 `File.exists()`
- **対象**:
  - `ItemListScreen.kt` `849, 870` (`CompactItemCard`)
  - `TrashScreen.kt` `428`
  - `ItemEditScreen.kt` `249, 318, 340`
  - `PhotoPreviewDialog.kt` `102` (`toPreviewPhotoItem`)
  - `ItemQuickActionDialog.kt` `184`
- **事象**: `LazyColumn` / `LazyVerticalGrid` の各カード描画や再コンポーズごとに Main スレッド上で `File(path).exists()` を直接実行している。端末ストレージの I/O 遅延や写真点数増加時にスクロールジャンク（フレームドロップ）の原因となる。
- **推奨修正**: `ViewModel` / `Repository` から通知される存在状態キャッシュ、または `produceState(initialValue = false, path) { withContext(Dispatchers.IO) { File(path).exists() } }` へ逃がし、Main スレッド上の同期 `File.exists()` を排除する。

#### `AUDIT-013` [バグ / 競合 / 軽量化] `CameraXCaptureDialog.kt` の縮小完了前キャンセル競合と毎フレーム SharedPreferences 書込
- **対象**: `app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt` `236–243`, `457–486`, `848–856`, `1044–1055`
- **事象**:
  1. 通常解像度モード（`isHighResMode == false`）では `onImageSaved` 内で `coroutineScope` 上で非同期に `downscaleCapturedPhotoIfNeeded` を実行し、完了後に `capturedPaths` へ追加している。撮影直後（`isCapturing == true` 中）に閉じる／戻るボタンを押すと `capturedPaths.isEmpty()` と判定されてダイアログが即閉じ、`coroutineScope` がキャンセルされて縮小中の一時ファイルが孤立または破損する。
  2. 457–486 行の `LaunchedEffect` がピンチズームの `currentZoom` 変化や EV スライダー移動の**毎フレーム**、Main スレッド上で `CameraXCapturePreferences.save(context, ...)`（`SharedPreferences` 書き込み）を呼んでいる。
  3. 848–856 行のズームプリセット切り替えピルの高さが `48.dp` 未満で押しにくい。
- **推奨修正**:
  1. `isCapturing`（または処理中カウント）が 0 より大きい間は閉じる操作を「完了待ち」またはキャンセル確認ダイアログ対象とし、撮影直後に作成した未登録の一時パスも `pendingTempFiles` として追跡して破棄時に削除する。
  2. 設定保存は `snapshotFlow { ... }.debounce(300)` 等でデバウンスし、`apply()` の呼び出し頻度を抑制する。

#### `AUDIT-014` [バグ / 軽量化] `ItemDetailScreen.kt` のバックグラウンド同期未追従と重複ダイアログ分岐
- **対象**: `app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt` `115–124`, `370–374`
- **事象**:
  1. `LaunchedEffect(itemId, isPcServerConnected)` で `viewModel.getItemById(itemId)` をワンショット取得しているため、詳細画面を開いたままバックグラウンドで PC 同期や Claude ジョブ完了が起きても、画面上の商品情報・写真が更新されない。
  2. 370–374 行に冗長な `if / else if` 分岐があり、`needsServerFetch == false` の場合でも PC 原本取得確認ダイアログが表示され得る。
- **推奨修正**: `itemsWithPhotos` Flow を `map { list -> list.find { it.item.id == itemId } }` で購読してリアルタイム反映させ、370 行台のダイアログ分岐を整理する。

#### `AUDIT-015` [軽量化] `SalesAnalysisScreen.kt` と `SettingsStorageScreen.kt` の再コンポーズ負荷・バージョン表記オーバーフロー
- **対象**:
  - `app/src/main/java/com/example/myapplication/ui/screens/SalesAnalysisScreen.kt` `86`, `111–199`
  - `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt` `195–206`, `1053`
- **事象**:
  1. `SalesAnalysisScreen` が再コンポーズのたびに `NumberFormat.getNumberInstance(Locale.JAPAN)` や `Calendar.getInstance()`、全売却済み商品のフィルタ・価格帯集計・リードタイム集計を `remember` なしで再計算している（199 行の `highestMarginTier` は算出のみで未使用）。
  2. `SettingsStorageScreen`（195–206 行）はサムネイル品質・解像度スライダーをドラッグするたびにデバウンスなしで `estimateCompressedThumbnail`（JPEG デコード＋再圧縮）を起動している。また 1053 行の `versionCode % 100` は Build 100 到達時に 2 桁へ戻る。
- **推奨修正**: 集計処理は `remember(itemsWithPhotos, selectedRange)` へ集約し、`estimateCompressedThumbnail` には `delay(250)` デバウンスを追加、バージョン表示は `versionCode` をそのまま（または `Build ${BuildConfig.VERSION_CODE}`）表示する。

#### `AUDIT-016` [バグ] `MergeItemsDialog.kt` で子商品を削除してもタイトル・説明文・合計価格が初期値のまま残る
- **対象**: `app/src/main/java/com/example/myapplication/ui/components/MergeItemsDialog.kt` `64–86`, `150–157`
- **事象**: ダイアログ内で個別の子商品横の `×` を押して候補から除外すると、`totalOriginalPrice` と `totalChildCount` は更新されるが、`titleFieldValue`・`descriptionFieldValue`・`priceText`・`expenseText` はダイアログ起動時の `remember` 初期値のまま凍結され、除外したはずの商品のタイトル・価格がまとめ案に残る。
- **推奨修正**: ユーザーがテキストを手動編集していない状態（`isManuallyEdited == false`）の間は、候補リスト変化に応じてタイトル・説明文・希望価格・経費を再計算し、手動編集済みの場合は「候補が変わったため再生成」ボタンを提供する。

#### `AUDIT-017` [安定化] `PcServerClient.kt` の `CancellationException` 握りつぶし・`NetworkUtils.kt` のサブネット判定誤差
- **対象**:
  - `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt` (`fetchServerItems`, `createClaudeJob`, `fetchClaudeJobs`, `postClaudeApproval` ほか多数)
  - `app/src/main/java/com/example/myapplication/data/server/NetworkUtils.kt` `28–39`
- **事象**:
  1. コルーチン内の `catch (e: Exception)` で `if (e is CancellationException) throw e` を再スローせず `false` / `null` / エラー扱いにしており、画面遷移キャンセルやサービス停止時の協調的キャンセルを阻害する箇所がある。
  2. `NetworkUtils.getLocalIpAddress` は `wlan`/`ap`/`hotspot`/`rndis` の最初の一致を即返し（休眠・VPN 仮想 IF を拾う可能性）、さらに `ip.startsWith("172.")` で `172.16.0.0/12` 以外のパブリック帯までプライベート扱いしている。
- **推奨修正**: すべての `suspend` 関数で `CancellationException` を最優先で再スロー（または `runCatchingCancellable` ユーティリティ導入）する。`NetworkUtils` は `InetAddress.isSiteLocalAddress` を活用し、アクティブかつ非仮想（`!isVirtual && isUp && !isLoopback`）な `wlan0` 優先スコア付けを行う。

#### `AUDIT-018` [保守性 / ビルド衛生] リポジトリの巨大バイナリ追跡・未使用ファイル・デッドコード・依存ライブラリ重複
- **対象**:
  - Git 追跡中のバイナリ: `flea_market_app.apk` (約 25 MB), `flea_market_apk.zip` (約 22 MB), `flea_market_project.zip` (約 688 KB), `tools/出品写真_PC保存用.zip` (約 161 KB), `tools/furima-server.exe` (約 726 KB), `tools/furima-sync.exe` (約 9 KB), `tools/Newtonsoft.Json.dll` (約 572 KB) — **計 約 49 MB**
  - デッドコード: `ui/components/DefaultSettingsDialog.kt` (242 行 — 全プロジェクトから未参照、かつ 220–225 行が `presets`/`templates`/`thumbnail` 設定を欠落させた `DefaultSettings` を生成するため危険), `ui/components/SummaryHeaderCard.kt` (105 行 — 未参照)
  - 未使用テンプレート & 長大過去ドキュメント: `index.html`, `package.json`, `vite.config.ts`, `tsconfig.json`, `src/`（React/Vite 初期テンプレート残骸）、および `PLAN_CLAUDE_SEND_PHASE6.md` (265 KB), `REVIEW_CLAUDE_SEND_PHASE6.md` (518 KB)
  - 依存/設定: `app/build.gradle.kts` (`secrets-gradle-plugin`・`google-services` プラグインが有効だが Firebase 依存はコメントアウト状態。また JSON ライブラリが Moshi reflection + Moshi codegen + Ktor Gson + `org.json.JSONObject` で 3 系統混在)。`AndroidManifest.xml:33` の `usesCleartextTraffic="true"` は LAN HTTP 通信のため意図的設計だが、将来的に `network_security_config.xml` でローカル帯域限定または警告明記が望ましい。
  - Web UI CDN 依存: `DashboardHtml.cs` と `WebDashboardHtml.kt` が Tailwind CSS と FontAwesome を外部 CDN から読込んでおり、インターネット遮断状態のローカル LAN（機内モード＋テザリング／閉域ルータ等）ではレイアウト・アイコンが崩れる。
- **推奨修正**: 配布成果物（APK / ZIP / EXE）を Git 追跡から除外（`git rm --cached` + `.gitignore` 徹底＋GitHub Releases 運用へ移行）し、未使用テンプレートとデッドコード（`DefaultSettingsDialog.kt`, `SummaryHeaderCard.kt`）を削除する。

---

### Low（軽微な改善・設計統一）

#### `AUDIT-019` [保守性 / セキュリティ] 旧 `tools/FurimaSyncNative.cs` のレガシー設計と `TunnelQrForm.cs` リソース解放漏れ
- **対象**: `tools/FurimaSyncNative.cs` (`75–196`), `tools/TunnelQrForm.cs` (`508`)
- **事象**: `FurimaSyncNative.cs` は `Thread.Sleep(30000)` + 同期 `.Result` / `.Wait()` 呼び出し、Bearer トークン認証未対応、拡張子 `.jpg` 固定（166 行）、非アトミックファイル書込といった旧実装のまま残存している。`TunnelQrForm.cs` 508 行の `StringFormat sf` は `using` で囲まれていない。
- **推奨修正**: `FurimaSyncNative.cs` の利用有無を確認し、不要なら廃止・アーカイブ、継続利用なら Bearer トークン＋非同期化＋拡張子保持を適用する。`TunnelQrForm.cs:508` に `using` を追加する。

#### `AUDIT-020` [テスト網羅性] C# サーバー単体テストおよび Compose UI / ViewModel テストの不在
- **対象**: `tools/tests/` (JS のみ), `app/src/test/` (8 ファイル 1,393 行)
- **事象**: CSV 出力・MD5 算出・Claude ラベル導出・Build 17 写真整合性の単体テストは充実している（JS 18 件全通過）一方、**C# サーバー側（`SyncStore` / `ClaudeJobStore` / `FurimaServerNative` 同期ロジック）の自動テストが 0 件**、また Android の **Compose UI / ViewModel（`ItemEditScreen`, `CameraXCaptureDialog`, `ItemListScreen`）の Robolectric テストが 0 件**である。
- **推奨修正**: `AUDIT-001`〜`AUDIT-016` の修正時に、C# 側の JSON アトミック永続化・マージ・ラベル検証の簡易テストランナー（または `dotnet test` / PowerShell テスト）と、Android `ItemListScreen`（`MERGED` 非選択）や `MergeItemsDialog` の Robolectric Compose テストを追加する。

---

## 4. 修正実装ロードマップ（推奨フェーズ順）

一度に大規模変更を行わず、リスクの高い順に 4 段階で適用することを推奨する。

| Phase | テーマ | 対象課題 ID | 主な内容 |
| --- | --- | --- | --- |
| **Phase A** | **緊急：クラッシュ・データ保全・セキュリティ** | `AUDIT-001`, `002`, `003`, `004`, `008`, `010`(XSS/検証) | `HandleRequest` 外側 `try`、PC 全ストアのアトミック保存＋`.bak` 復旧、`lockObj` 外参照の解消、`LocalWebServer` の PIN/トークン認証とパストラバーサル修正、`PhotoStorageManager` の削除順序修正、`mergeItems` 検証 |
| **Phase B** | **軽量化・ロック競合・キャンセル耐性** | `AUDIT-005`, `006`, `007`, `012`, `015`, `017` | `ServePhotoByHash` ロック縮小＋キャッシュヘッダ、`/api/server/status` の `File.Exists` キャッシュ化、`mergeServerItemsFromJson` の画像検査トランザクション外出し、Android 4 秒二重ポーリング統合、`PROFIT_DESC` 再計算修正、Main スレッド `File.exists()` 排除、`CancellationException` 再スロー |
| **Phase C** | **UI バグ是正・操作体験改善** | `AUDIT-009`, `010`(UI), `011`, `013`, `014`, `016` | `ItemEditScreen` の状態退避＋一時写真掃除＋`LazyRow` `key`、`ItemListScreen` の `MERGED` 選択抑止とページ維持、`CameraXCaptureDialog` 閉じ競合・保存デバウンス、`ItemDetailScreen` Flow 購読化、`MergeItemsDialog` 削除時再計算、PC/Android Web UI 修正 |
| **Phase D** | **モノリス解体・リポ整理・テスト拡充** | `AUDIT-018`, `019`, `020` + 第 5 章 | 巨大ファイルの計画的分割（`partial class` / UseCase・Screen 部品分割）、バイナリ追跡解除、デッドコード削除、UI/サーバーテスト追加 |

---

## 5. モノリス解体・構造改善設計書 (Monolith Decomposition Blueprint)

現在 1,000 行超のファイルが **10 本（総コードの約 61%）** に達しており、機能追加時のコンフリクト・レビュー負荷・AI 編集時のコンテキスト超過を招いている。既存のビルド方式（PC は `tools/build-exe.bat` で `.NET 4.x csc.exe` に `.cs` を列挙、Android は Gradle Kotlin）を壊さずに安全に分割できる構成を示す。

### 5.1 PC サーバー側 (`tools/`) — `.NET 4.x csc.exe` 互換分割
`tools/build-exe.bat` は `csc.exe` にソースファイルを直接渡してコンパイルしているため、**`partial class` 分割**および**外部テキスト／HTML の `/resource:` 埋め込み**だけで既存の配布形態（単一 `.exe`）を維持したまま分割可能。

1. **`tools/FurimaServerNative.cs` (7,768 行) → 機能別 7〜8 本の `partial` / 補助クラスへ分割**:
   - `FurimaServerNative.Core.cs`: `Main`・リスナー起動停止・`HandleRequest` ルーティング・外側例外ガード
   - `FurimaServerNative.Auth.cs`: トークン／PIN／信頼済み IP・`LocalWebServer` 連携認証
   - `FurimaServerNative.Persistence.cs`: `AtomicFileWriter`・`items_db.json` ロード／セーブ・`.bak` リカバリ
   - `FurimaServerNative.Sync.cs`: `/api/sync/*`（Pull / Push / 差分同期 / 写真インデックス）
   - `FurimaServerNative.Items.cs`: 商品 CRUD・一括編集・まとめ・ゴミ箱・`商品情報_説明文.txt` 出力
   - `FurimaServerNative.Photos.cs`: 写真追加・配信（`ServePhotoByHash`）・ゴミ箱復元・キャッシュ統計
   - `FurimaServerNative.Claude.cs`: `/api/claude/*` 承認・ジョブ実行連携・画像マージ
   - `FurimaServerNative.MuseRef.cs`: Muse / Reference サブシステムとサムネイル生成
2. **`tools/DashboardHtml.cs` (5,522 行) → テンプレート分離または `partial` 化**:
   - 最もクリーンな方法：`tools/web/dashboard.html`, `tools/web/dashboard.css`, `tools/web/dashboard-*.js` に分離し、`build-exe.bat` の `csc.exe` に `/resource:...` で埋め込み、`DashboardHtml.cs`（200 行程度）が `Assembly.GetManifestResourceStream` で読み出して返す構成にする。これにより JS の構文ハイライト・ESLint・`node --test` による直接ユニットテストが容易になり、C# verbatim 文字列のエスケープ事故（`""` 問題）も根本解消する。
   - 次善策：`partial class DashboardHtml` で `Head.cs` / `Modals.cs` / `ScriptCore.cs` / `ScriptEdit.cs` / `ScriptClaude.cs` に分割する。

### 5.2 Android 側 (`app/src/main/java/`) — MVVM + UseCase + 部品分割
1. **`PcServerClient.kt` (1,847 行) → 責務別 4 クラスへ分割**:
   - `PcServerTransport.kt`: OkHttp/Ktor 設定・認証ヘッダ・リトライ・タイムアウト・エラー変換
   - `PcSyncApiClient.kt`: ハートビート・商品 Pull/Push・写真送受信
   - `PcClaudeApiClient.kt`: Claude ジョブ作成・監視・承認・取消・マージ
   - `ServerItemMergeEngine.kt`: `mergeServerItemsFromJson` の純粋マージロジック（DB トランザクションと画像修復 I/O を分離）
2. **`FleaMarketViewModel.kt` (1,730 行) → コントローラ / UseCase 委譲へ分割**:
   - `ItemFilterSorter.kt`: 検索・ステータス絞り込み・ソート（`PROFIT_DESC` 等、純粋関数化し単体テスト可能に）
   - `PcSyncController.kt`: ハートビート／Claude 監視／自動同期ステート管理
   - `ClaudeWorkflowController.kt`: ジョブ送信・承認・ラベル集約・既読管理
   - `FleaMarketViewModel.kt` 本体は画面状態（`StateFlow`）合成と UI イベント仲介（約 500 行以下目標）へ縮小。
3. **`ItemEditScreen.kt` (1,696 行) → ドラフト状態クラス + セクション Composable 分割**:
   - `ItemEditDraftState.kt`（または `ItemEditViewModel` / 保存可能ステートホルダー）: 入力保持と未保存変更判定、一時カメラファイル管理
   - `EditPhotoSection.kt`, `EditPricingProfitSection.kt`, `EditListingTemplatesSheet.kt` に分割
4. **`CameraXCaptureDialog.kt` (1,479 行) → 設定・CameraX バインド・UI オーバーレイ分割**:
   - `CameraXCapturePreferences.kt`, `CameraControllerHolder.kt`, `CameraCaptureControlsOverlay.kt`
5. **`ItemListScreen.kt` (1,261 行) / `SettingsStorageScreen.kt` (1,182 行) / `FleaMarketRepository.kt` (1,203 行)**:
   - `ItemListScreen`: `ItemListTopBar.kt`, `CompactItemCard.kt`, `ItemSelectionActionBar.kt`
   - `SettingsStorageScreen`: `StorageSettingsSection.kt`, `BackupRestoreSection.kt`, `DefaultPricingSection.kt`
   - `FleaMarketRepository`: `ItemMutationRepository.kt` と `TrashAndMergeRepository.kt` に責務分割

### 5.3 リポジトリ衛生・ビルドクリーンアップ手順
1. `.gitignore` に加え、既に誤追跡されている配布バイナリ（約 49 MB）を `git rm --cached` で履歴管理対象から外す（ローカル実体は残る）`flea_market_app.apk`, `flea_market_apk.zip`, `flea_market_project.zip`, `tools/*.exe`, `tools/Newtonsoft.Json.dll`, `tools/*.zip`。
2. 未使用ファイル削除：`DefaultSettingsDialog.kt`, `SummaryHeaderCard.kt`, React/Vite テンプレート残骸 (`index.html`, `package.json`, `vite.config.ts`, `tsconfig.json`, `src/`)。
3. 過去フェーズの巨大計画・レビュー記録は `docs/archive/` 等へ整理。

---

## 6. 引き継ぎメモ

- `5922ae5` 時点の PC サーバー・Android アプリ・ビルド設定・テスト全体のコード監査（Batch 1 〜 Batch 8）は **すべて完了** した。
- 本報告書作成時点では実装コード（`*.cs`, `*.kt`, Gradle, Manifest）への変更は一切行っていない。
- 次のステップでは、第 4 章のロードマップに従い **Phase A（緊急：データ保全・クラッシュ・セキュリティ）** から順次修正パッチを適用し、C# / Android 双方のテスト（および `tools/build-exe.bat` ビルド）で検証することを推奨する。
