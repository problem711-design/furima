# フェーズ 2.15 〜 2.17 詳細実装計画書（コードレビュー総点検に基づく修復・改善計画）

策定日：2026-11-14  
基準バージョン：`v0.9β (Build 4)` (`3f87ea8`) ➔ 目標バージョン：**`v0.9β (Build 7)`**  
前提原則：`PROGRESS.md` 記載の開発・引き継ぎ基本原則（Rule 1〜9：小規模逐次実装、最小限編集、3箇所同時バージョン更新、テスト通過、実機インストール保証、`testOnly` 防止、フェーズ単位の GitHub 同期）を厳守する。

---

## 1. 全体方針と設計前提の整理

本計画書は、最新コミット（`3f87ea8`）に対する全コードレビュー結果をもとに、重大度 **「致命的」➔「警告」➔「提案」** の順に安全かつ小規模単位で順次修復・改善を行うための実装計画である。

### 設計前提と仕様進化の整理
1. **一人利用前提の逐次運用**：
   - 基本動線は「スマホで写真撮影・ひな形作成 ➔ PCで実際の出品作業（説明文調整・まとめ作成・Yahoo!フリマ等への転記・売却管理）」の逐次利用であり、スマホとPCの同時並列操作は想定しない。
2. **「スマホ常時接続ロック」から「PC単独編集 ＋ 安全な双方向同期」への移行（警告12の訂正反映）**：
   - 初期実装では「スマホアプリを常時起動・接続し、接続確認時のみPC管理画面から操作可能」という前提で設計されていたため、現在もPC管理画面上部に「操作・編集ロック中」というバナー文言が残存している。
   - 現在は**PC単独での編集と、ゴミ箱機能による復元可能な安全な双方向同期**を目指して開発されているため、UI文言を実態（PC単独編集可・再接続時自動同期）へ統一し、サーバー再起動やオフライン作業を挟む逐次ハンドオーバー時でも写真・メタデータが壊れない堅牢性を確保する。
3. **優先順位（`PROGRESS.md` Rule 1 準拠）**：
   - **フェーズ 2.15 (`Build 5`)**：重大度「致命的」の即時修復（写真消失・MD5破壊・ビルド不能の根絶）
   - **フェーズ 2.16 (`Build 6`)**：重大度「警告」の修復（デバッグ残骸撤去・PC単独編集整合・画像回転/形式・UTF-8/Null安全性）
   - **フェーズ 2.17 (`Build 7`)**：重大度「提案」の改善（ポーリング軽量化・孤立ファイル掃除・不要資産/コード整理）

---

## 2. フェーズ 2.15 詳細仕様（重大度「致命的」修復 ＋ Build 5 統一）

目標バージョン：**`v0.9β (Build 5)`**

### 2.1 サブフェーズ 2.15-A — PCサーバー `items_data.json` 再読込後の型不一致 (`JArray` / `JObject`) 根絶
- [x] **実装完了**
* **対象ファイル**：
  * `tools/FurimaServerNative.cs`
  * `tools/SyncStore.cs`
* **原因と問題点**：
  * `LoadItemsDbFromDisk()`（`FurimaServerNative.cs` 405行目）が `List<Dictionary<string, object>>` としてデシリアライズするため、サーバー再起動後にメモリへ読み込まれる `item["photos"]` は `Newtonsoft.Json.Linq.JArray`（各要素は `JObject`）になる。
  * これにより、PCブラウザからの写真追加（`HandleAddPhoto` 2404〜2406行目）で `item["photos"] is List<Dictionary<string, object>>` が `false` となり、**既存写真がすべて消去される**。
  * 同根のバグが `HandleSyncPhoto`（1181行目）、`HandleUnmergeItem`（2266行目）、`SyncStore.CountMissingFiles`（428行目）、`SyncStore.GetCleanupCandidates`（554行目）にも存在し、再起動後は写真フォルダ振り分け・欠損数集計・30日クリーンアップが全滅する。
* **実装要件**：
  1. `JArray` / `JObject` / `Dictionary<string, object>` のいずれが渡されても安全に `List<Dictionary<string, object>>` へ正規化する共通ヘルパー `NormalizePhotosList(object rawPhotos)`（または `ServerItem` / `ServerPhoto` の強型付けDTOクラス導入）を実装する。
  2. 上記5箇所の写真参照ロジックをすべて共通ヘルパー経由に統一し、サーバー再起動を挟んでも写真リスト・フォルダ配置・欠損数・クリーンアップ候補が完全に維持されるようにする。

### 2.2 サブフェーズ 2.15-B — スマホ側 原本削除・保存・まとめ買い共有写真の `isOriginalDeleted` / `md5Hash` 破壊修復
- [x] **実装完了**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/data/local/ItemDao.kt`
  * `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt`
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
* **原因と問題点**：
  1. `FleaMarketViewModel.cleanupSharedOriginalPhotos()`（694〜706行目）がディスク上の原本ファイルのみを削除し、Room DBの `PhotoEntity`（`isOriginalDeleted = true`, `filePath = thumbnailPath`）を更新していない。結果、`ItemEditScreen` で再保存した際に `FleaMarketRepository.saveItem()`（151〜182行目）がサムネイルを新規写真と誤認し、**サムネイルのサムネイルを再生成して `md5Hash` をサムネイルのMD5で上書き**し、PC原本との紐付けを破壊する。
  2. `PcServerClient.syncDeltaToPcServer()`（514〜552行目）において、まとめ買いの親子間で同一 `filePath` を共有する `PhotoEntity` のうち最初に処理した1件の原本ファイルを削除すると、もう一方（親または子）のレコードが削除済み `filePath` を指したまま `isOriginalDeleted = false` で取り残される。
  3. `PcServerClient.mergeServerItemsFromJson()`（834〜837行目、876行目）で原本削除済み写真の `localFilePath` に `thumbnailPath` を設定した上で `isOriginalDeleted = localFilePath.isBlank() && ...` を評価しているため、PC更新のPull時に `isOriginalDeleted` が誤って `false` に戻る。
* **実装要件**：
  1. `cleanupSharedOriginalPhotos()` 内で削除成功したファイルに対し `dao.updatePhotoOriginalDeleted(photo.id, true, photo.thumbnailPath)`（および同一 `filePath` を共有する全レコード）を更新する。
  2. `FleaMarketRepository.saveItem()` では `filePath` だけでなく `thumbnailPath` でも既存 `PhotoEntity` を照合できるようにし、既知サムネイルだった場合は既存の `md5Hash` と `isOriginalDeleted = true` を保持する。
  3. `ItemDao` に同一 `filePath` を持つ `PhotoEntity` 一括更新クエリを追加し、`syncDeltaToPcServer()` での原本削除時に親子双方のレコードを漏れなく同期更新する。
  4. `PcServerClient.mergeServerItemsFromJson()` 876行目の `isOriginalDeleted` 判定を「実原本ファイル（`thumbnailPath` と異なるパス）が実在せず、かつ `thumbnailPath` が存在するか」へ修正する。

### 2.3 サブフェーズ 2.15-C — `tools/build-exe.bat` ビルド復旧 ＋ DLL配置整理 ＋ `Build 5` 統一
- [x] **実装完了**
* **対象ファイル**：
  * `tools/build-exe.bat`
  * `tools/Newtonsoft.Json.dll` / `tools/lib/Newtonsoft.Json.dll`
  * `tools/furima-server.exe`（再ビルド反映）
  * バージョン3箇所：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
  * `PROGRESS.md`, `PLAN_PHASE_2_15_TO_2_17.md`
* **実装要件**：
  1. `tools/build-exe.bat` 18行目の `csc.exe` コンパイル対象に `TunnelQrForm.cs` を追加し、`furima-server.exe` がバッチ単体で確実にビルドできるよう復旧する。
  2. `tools/Newtonsoft.Json.dll` と `tools/lib/Newtonsoft.Json.dll` は同一バイナリ（v13.0.3.27908 / .NET Framework 4.0ターゲット）の二重管理になっているため、参照先を一本化してDLLバージョン不整合リスクを排除する（DLL自体のバージョンアップは不要）。また `DateTimeOffset.ToUnixTimeMilliseconds()` 利用に伴い動作要件（.NET Framework 4.6以上 / Win10・11標準4.8）をバッチに明記する。
  3. `compile_applet`・`gradle :app:testDebugUnitTest`（原本削除・再保存・まとめ買い共有に関するRobolectricテスト追加）・`build-exe.bat` 検証を実施し、バージョンを **`v0.9β (Build 5)`** へ3箇所同時統一する（Rule 9に従いAPK・EXEも同期更新）。

---

## 3. フェーズ 2.16 詳細仕様（重大度「警告」修復 ＋ Build 6 統一）

目標バージョン：**`v0.9β (Build 6)`**

### 3.1 サブフェーズ 2.16-A — デバッグ残骸 `#18` 撤去 ＋ まとめ買いステータス＆30日クリーンアップ安全化
- [x] **実装完了**
* **対象ファイル**：
  * `tools/DashboardHtml.cs`
  * `tools/FurimaServerNative.cs`
  * `tools/SyncStore.cs`
* **実装要件**：
  1. `DashboardHtml.cs` 1377行目（`if (item.id === 18) return true;`）および `FurimaServerNative.cs` 2294行目（`if (parentId == 18 && restoredCount == 0)`）のテスト用ハードコードを削除する。
  2. `FurimaServerNative.cs` `HandleMergeItems()`（2109行目）において、子商品の `statusLabel` を親の `asStatus` に応じて `"まとめ売却済"`（`SOLD`時）または `"まとめ出品中"`（`LISTED`時）に正しく設定する。
  3. `SyncStore.GetCleanupCandidates()`（523〜525行目）では `status == "SOLD"` の商品のみをクリーンアップ判定対象とし（`MERGED` 単体で削除対象にしない）、かつアクティブな他商品（`LISTED` / `DRAFT` 等）が参照中のハッシュ・ファイルは削除対象から除外する安全ガードを追加する。

### 3.2 サブフェーズ 2.16-B — PC単独編集・双方向同期・フォルダ整合・UTF-8/Null安全性修復
- [x] **実装完了**
* **対象ファイル**：
  * `tools/DashboardHtml.cs`
  * `tools/FurimaServerNative.cs`
  * `app/src/main/java/com/example/myapplication/data/server/LocalWebServer.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
* **実装要件**：
  1. **PC単独編集向けステータスバナー刷新（警告12訂正反映）**：`DashboardHtml.cs` 32行目・1241行目の「スマホアプリ未接続（操作・編集ロック中）」という旧仕様文言を、現在の「PC単独編集可 ＋ 再接続時自動同期」方針に合わせ「スマホアプリ未接続（PC単独で編集可／スマホ再接続時に自動同期）」等へ変更する。
  2. **PCサーバー堅牢化 (`FurimaServerNative.cs`)**：
     - `lastHeartbeatSec`（634行目）で `lastHeartbeat == DateTime.MinValue` 時は `-1` を返し `Int32` オーバーフローを防止する。
     - `HandleRequest` 終端 `catch`（762〜767行目）の `res.StatusCode = 500; res.Close();` を `try/catch` で保護し、切断済みソケットによるスレッドクラッシュを防止する。
     - `ParseBoolSafe` ヘルパーを導入し、`HandleUpdateItem`（1310〜1335行目）や `HandleSyncItems`（1071〜1074行目）で `JValue(null)` を安全に扱えるようにする。
     - リクエストボディ読込を `Encoding.UTF8` に統一し、Shift_JIS環境（日本語Windows）での `curl` 等からの文字化けを防ぐ。
  3. **フォルダ名整合とメタデータ維持**：
     - PCで商品名を変更後にスマホ同期しても `id_旧タイトル` と `id_新タイトル` にフォルダが分断されないよう、`HandleSyncItems` / `HandleSyncPhoto` 側でも既存 `id_*` フォルダを優先検索（または旧フォルダをリネーム統一）する。
     - `HandleSyncItems`（995〜1000行目）で `displayOrder` と `childItemId` を保存・引き継ぎする。
  4. **Android側データ整合 (`ItemEditScreen.kt` / `LocalWebServer.kt`)**：
     - `ItemEditScreen.kt` 746行目の `soldPrice` は `status == "SOLD"` の場合のみ算出し、未売却時は `0` にしてCSV出力等の不整合を防ぐ。
     - `LocalWebServer.kt` の `/api/photos/{photoId}/restore`（520行目）で、原本削除後に `filePath` がサムネイルパスへ切り替わっていてもサムネイルを上書きしないよう、新規の原本パスを生成して書き戻しDBを更新する。

### 3.3 サブフェーズ 2.16-C — 画像形式（PNG/WebP/EXIF回転）・クリップボード・旧モーダル整理 ＋ Build 6 統一
- [x] **実装完了**
* **対象ファイル**：
  * `tools/FurimaServerNative.cs`
  * `tools/DashboardHtml.cs`
  * `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt`
  * バージョン3箇所：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `WebDashboardHtml.kt`
* **実装要件**：
  1. `ServePhotoByHash`（`FurimaServerNative.cs` 2569行目）でファイルの先頭マジックバイトから `image/jpeg` / `image/png` / `image/webp` を判定して返却し、`HandleAddPhoto` も適切な拡張子で保存する（`ScanAndIndexLocalPhotos` も `*.jpg;*.jpeg;*.png;*.webp` 対応へ拡張）。
  2. `DashboardHtml.cs` の `copyPhotoBlob`（829〜847行目）で `<canvas>` 経由の `image/png` 変換を挟み、Edge/ChromeでJPEG画像でも一発でクリップボードへコピーできるよう改善する。
  3. Android `PhotoStorageManager.kt` の `createThumbnail` / `createThumbnailFromBytes` に `ExifInterface` による回転補正を組み込み、スマホ縦向き撮影写真のサムネイル横向き化を防止する。
  4. `DashboardHtml.cs` 1837〜1856行目の旧 `openImageModal`（`UNSHARED` フィルター条件不整合・ソート不一致）を整理し、`openPhotoViewer(item.id)` へ完全一本化する。
  5. 検証後、バージョンを **`v0.9β (Build 6)`** へ3箇所同時統一する（Rule 9に従いAPK・EXEも同期更新）。

---

## 4. フェーズ 2.17 詳細仕様（重大度「提案」改善 ＋ Build 7 統一）

目標バージョン：**`v0.9β (Build 7)`**

### 4.1 サブフェーズ 2.17-A — PC管理画面ポーリング最適化＆サーバーI/O・スレッド安全性強化
- [ ] **未着手**
* **対象ファイル**：
  * `tools/DashboardHtml.cs`
  * `tools/FurimaServerNative.cs`
* **実装要件**：
  1. `DashboardHtml.cs` の3秒ポーリング（885, 1215〜1247行目）を軽量な `/api/server/status` のみに絞る（タブ非表示時は停止）。`dataRevision` 変化時のみ `/api/items` と `/api/cleanup/preview` を取得・再描画し、`settingsRevision` 変化時のみ `/api/settings` を取得する。
  2. `FurimaServerNative.cs` 1094行目の `SaveItemsDbToDisk()` を `if (dataChanged)` の場合のみ実行するよう変更し、不要なディスク書き込みと `items_data.prev.json` 上書きを抑止する。
  3. `HandleSyncPhoto` 1150行目の未使用 `byte[] buffer` 確保を削除し、`hashToFilePathMap.Count` / `itemsDb.Count`（215, 635行目等）の参照をすべて `lock (lockObj)` 内に収める。

### 4.2 サブフェーズ 2.17-B — Androidストレージ・エラーハンドリング強化＆デッドコード／不要資産整理 ＋ Build 7 統一
- [ ] **未着手**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt`
  * `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt`
  * `app/build.gradle.kts`（未使用依存の確認・コメントアウト整理）
  * `tools/` 不要資産（`furima-sync.bat`, `furima-sync.ps1`, `出品写真_PC保存用.zip` 等）の整理確認
  * バージョン3箇所：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `WebDashboardHtml.kt`
* **実装要件**：
  1. `PhotoStorageManager.savePhotoFromUri()`（239〜251行目）で `openInputStream(sourceUri)` が `null` または書き出し後ファイルサイズ `<= 0` の場合に `null` を返却し、不正パスのDB登録を防ぐ。
  2. `FleaMarketRepository.saveItem()`（206〜207行目）で編集時に外された写真について、他アイテムからの参照がなければ端末内ファイルを削除するよう孤立ファイル掃除を追加する。
  3. 未実装の残骸メソッド `FleaMarketRepository.cleanupSharedPhotos()`（441〜450行目）および旧補助スクリプト（`furima-sync.bat` / `.ps1` / テストZIP）を整理し、可能であれば `FurimaServerNative.cs` のハンドラ分割（`SyncHandler` / `ItemHandler` / `PhotoHandler`）を検討する。
  4. `app/build.gradle.kts` 内の未使用ライブラリ（`retrofit` 等、実コードから参照されていない依存）を精査して整理し、APKサイズ・ビルド時間を最適化する。
  5. 検証後、バージョンを **`v0.9β (Build 7)`** へ3箇所同時統一する（Rule 9に従いAPK・EXEも同期更新）。

---

## 5. 各フェーズ共通の完了ゲート（テスト・検証基準）

1. **Android 検証**：`compile_applet` および `gradle :app:testDebugUnitTest`（フェーズ2.15以降は「手動原本削除→編集再保存でのMD5維持」「まとめ親子共有写真の原本削除整合」「PC更新Pull時の `isOriginalDeleted` 維持」のユニットテストを追加）が全件通過すること。
2. **PCサーバー検証**：`tools/build-exe.bat` により `furima-server.exe` / `furima-sync.exe` が警告・エラーなくコンパイルでき、再起動後の `items_data.json` 読み込み状態で写真追加・欠損数・クリーンアップ候補取得が正常動作すること。
3. **Rule 6 / 7 / 8 / 9 順守**：各フェーズ完了時に `app/build.gradle.kts`・`tools/DashboardHtml.cs`・`WebDashboardHtml.kt` のバージョン統一、`PROGRESS.md`・本計画書のステータス更新、APK/EXE更新、および選択的 `git commit` & `git push origin main` を実施すること。
