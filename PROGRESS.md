# 開発進捗・引き継ぎログ (Progress & Handover Log) - 第2世代

このドキュメントは、第2世代アーキテクチャ（母艦PCマスターサーバー型 & MD5ハッシュ差分同期構成）における各実装フェーズの進捗状況、現状のギャップ調査結果、および修復記録を保持するためのものです。

> **旧開発進捗ログ アーカイブ**:
> フェーズ1〜7の完了記録および過去の変更履歴は [PROGRESS_ARCHIVE_PHASE1.md](PROGRESS_ARCHIVE_PHASE1.md) を参照してください。

---

## 開発・引き継ぎ基本原則 (Important Rules)

1. **小規模逐次実装**: 変更規模の小さなフェーズから優先的に1つずつ実装する。
2. **最小限のファイル編集**: 各フェーズごとに必要最小限のファイルのみを編集対象とし、不要なファイルを触らない。
3. **完全な進捗記録**: 各フェーズの完了時、または大きめの変更（データ保存・通信方式の変更、機能の追加・削除など）が発生するたびに、本ファイル (`PROGRESS.md`) に「フェーズ2.1-〇完了」と明確に記録を残す。
4. **既存UI・レイアウトの維持**: 特別な指示がない限り、ユーザーが気に入っているアプリの見た目・レイアウト・配色を変更しない。
5. **ビルド検証の最適化 (トークン消費節約)**: 純粋なWeb UI/HTML/CSS/JSの調整や、Kotlinロジックに変更のない軽微なデザイン調整時は、毎回 `gradle_build` を実行せずビルドを省略・スキップしてトークン消費と処理時間を抑える。
6. **厳格なバージョン表記・同時統一更新規則 (Strict Versioning Rule)**:
   - 各完了フェーズ・更新履歴ごとに「進捗状況」と「バージョン・Build番号」を完全対応させて明記する。
   - 表記フォーマット: `yyyy/mm/dd フェーズ2.1-〇完了 バージョン 2.x (Build x)`
   - バグ修正・レイアウト調整・内部処理順変更等の小規模変更: Build番号を加算（例: `Build 7` ➔ `Build 8`）。
   - 明確な大型新機能追加・基本構造移行: マイナーバージョンを繰り上げる（例: `2.0` ➔ `2.1`）。
   - **バージョン表記の更新義務**: バージョン更新時は、**スマホアプリ側 (`app/build.gradle.kts`)** と **PCブラウザ管理画面 (`WebDashboardHtml.kt`)** の両方を**必ず同時に統一して更新**する。
7. **仕様・実装計画書の併せての確認ルール (Plan File Verification Rule)**:
   - 作業開始時、およびビルドナンバー（Build番号）更新時には、本ファイル (`PROGRESS.md`) に加えて、仕様・計画書である `PLAN_PHASE_2_1.md` を**必ず同時に確認・参照**する。

---

## 全体ロードマップ (第2世代アーキテクチャ v2.1)

現在のバージョン: **`v2.1 (Build 13)`**

### フェーズ 2.0-SYNC-REPAIR (通信・同期疎通の根幹修復) 【全ステップ完了】
- [x] **ステップ 1: スマホアプリ側通信クライアント (`PcServerClient.kt`) の強化** 【完了】
- [x] **ステップ 2: スマホアプリ側 ViewModel (`FleaMarketViewModel.kt`) & 画面 (`PcSyncScreen.kt`) の通知改善** 【完了】
- [x] **ステップ 3: PC母艦サーバー (`FurimaServerNative.cs`) の受信ログ出力 & フォルダ/txt 書き込み保証** 【完了】
- [x] **ステップ 4: エミュレーター実環境での end-to-end 通信検証 & デシリアライズ修復** 【完了】

---

### フェーズ 2.1 (トンネル連携・QRコード接続・最適化UI・容量管理移行)
※ 実装規模・変更の小さい順にフェーズ分けを行い、各フェーズで編集するファイルを最小限に限定して逐次実装する。

#### [x] **フェーズ 2.1-A: スマホ側設定タブの断捨離 & MD5ハッシュ永続化修復** 【完了】
- **目的**: 
  - 不要になった「送信済み写真の元画像を削除」ボタンをスマホ設定画面から削除しUIを簡素化。
  - 初回同期で計算した元画像 MD5 ハッシュを Room DB (`photos` テーブル) に永続保存し、サムネイル化後の2回目以降同期でハッシュのズレが生じる問題を根本解消。
- **編集対象ファイル (完了: 3ファイル)**:
  1. `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt` (不要ボタン・ダイアログ削除)
  2. `app/src/main/java/com/example/myapplication/data/local/ItemDao.kt` (`updatePhotoMd5Hash` クエリ追加)
  3. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt` (ハッシュ計算時のDB永続化追加)

#### [x] **フェーズ 2.1-A2: 保存先の固定・原本削除3条件ガード・二重起動防止** 【完了 (2026/03/30 Build 9)】
- **目的**: 
  - 起動ディレクトリ移動に伴うPC側写真保存先の空ブレを解消するため、`store.json` による保存フォルダ・`storeId`（GUID）の固定。
  - 起動ログの1行目に採用絶対パスと `storeId` を出力。名前付き Mutex による exe の二重起動拒否。
  - `items_data.json` 保存時の `items_data.prev.json` 1世代バックアップ保持。
  - `/api/server/status` への `storeId`, `syncDir`, `indexedPhotosCount`, `missingFileCount` 追加。
  - PUSH同期応答への `storeId` とサーバー自己計算 `syncedHashes` の返却。`storeId` 不一致時のGC削除拒否。
  - スマホ側で応答 `storeId` を端末記憶し、高画質削除の3条件（`storeId` 一致、サーバー保存後自己計算 `syncedHashes` 含有、サムネイル実在）を厳格検証。
  - サムネイルのみでPC側に存在しない「原本なし」写真の計数とPC連携画面への表示。
  - `AppDatabase.kt` の `fallbackToDestructiveMigration()` 削除。
- **編集対象ファイル (完了)**:
  1. `tools/FurimaServerNative.cs`
  2. `tools/SyncStore.cs` [新規]
  3. `tools/DashboardHtml.cs` [新規]
  4. `tools/FurimaSyncNative.cs`
  5. `tools/furima-sync.ps1`
  6. `tools/build-exe.bat`
  7. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  8. `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt`
  9. `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  10. `app/src/main/java/com/example/myapplication/data/local/AppDatabase.kt`
  11. `app/build.gradle.kts`
  12. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`

#### [x] **フェーズ 2.1-A3: JSONライブラリ交換 (Newtonsoft / Moshi) & 写真の二段階POST** 【完了 (2026/03/30 Build 10)】
- **目的**: 
  - `JavaScriptSerializer` の配列キャスト依存を解消し、`Newtonsoft.Json` (PC) および `Moshi` (スマホ) に移行。
  - プロトコル v3 導入: JSONメタデータ送信 (`POST /api/sync/items`) と生バイト送信 (`POST /api/sync/photo` + `X-Photo-Hash` ヘッダー) による写真の個別分離送信。
  - サーバーが保存後に自己計算した MD5 ハッシュのみを正として検証。
- **編集対象ファイル (完了)**:
  1. `tools/lib/Newtonsoft.Json.dll` (配置済み)
  2. `tools/FurimaServerNative.cs`
  3. `tools/SyncStore.cs`
  4. `tools/FurimaSyncNative.cs`
  5. `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  6. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  7. `app/build.gradle.kts`
#### [x] **フェーズ 2.1-B: PC母艦サーバーの超軽量GUI化・認証トークン・QRコード生成** 【実装完了 (2026/03/30 Build 11)】
- **目的**: 
  - `furima-server.exe` 起動時に超軽量ウィンドウ（Windows Forms）を表示。
  - `cloudflared.exe` を背景起動して一時トンネルURLを発行、ランダム認証トークン付きの接続用QRコードを生成・描画。
  - APIエンドポイントにワンタイム認証トークン検証ガード（Bearer認証）を追加し、安全な接続を確立。
- **編集対象ファイル**:
  1. `tools/TunnelQrForm.cs` (新規)
  2. `tools/FurimaServerNative.cs` (GUI、cloudflared連携、自前QR描画、トークンガード)
  3. `tools/DashboardHtml.cs` (Build 11 表記統一)
  4. `app/build.gradle.kts` (versionCode 11 更新)

#### [x] **フェーズ 2.1-C: スマホアプリ側のQRスキャナ連携 & トークン対応** 【実装完了 (2026/03/30 Build 12)】
- **目的**: 
  - 「PC連携」画面に「QRコードを読み取る」ボタンを追加（Google Play Services Code Scanner 利用）。
  - QRから読み取ったトンネルURLと認証トークンを保持し、Wi-Fiアクセスポイント変更時もQRをかざすだけで即同期可能にする。
  - 旧「PCブラウザ連携」は「ローカルサーバー接続（直接/緊急用）」として保持し、明確に棲み分け。
- **編集対象ファイル**:
  1. `app/build.gradle.kts` (Code Scanner 依存関係追加 & versionCode 12 更新)
  2. `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt` (QR読み取りUI追加 & ローカル直接接続カード整理)
  3. `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt` (QRトークン・URL保持 & パース)
  4. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt` (Bearer認証ヘッダー付与 & StoreID検証)

#### [x] **フェーズ 2.1-D: 同期ステータス可視化 (スマホ側 & PC側)** 【実装完了 (2026/03/30 Build 13)】
- **目的**: 
  - スマホ側およびPC側の両方に同期ステータス（未同期バッジ・原本なし件数およびリスト表示）を追加。
  - 出品一覧カードに「未同期」および「原本なし」バッジを表示。
  - PC連携画面に原本なし写真の一覧とクリップボードコピー機能を追加。
  - 母艦ブラウザ管理画面のヘッダーにインデックス写真枚数と原本なし件数を表示。
- **編集対象ファイル**:
  1. `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt` (カードバッジ追加)
  2. `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt` (原本なし一覧 & コピー機能)
  3. `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt` (未同期・原本なし StateFlow & エクスポート)
  4. `tools/DashboardHtml.cs` (ステータス表示追加 & Build 13 更新)
  5. `app/build.gradle.kts` (versionCode 13 更新)

#### [ ] **フェーズ 2.1-E: PCブラウザ管理画面の本格統合 & 余白最適化・数値強調** 【未着手】
- **目的**: 
  - `WebDashboardHtml.kt` をベースにしたリッチなPCブラウザ管理画面を `furima-server.exe` から本格配信。
  - 各所の余白を詰めた高密度レイアウト（Compact & Dense）、重要数値（価格・利益・ステータス）の拡大・太字化。
  - 写真ファイルのローカル高速配信 (`/api/photos/{hash}`) と拡大プレビューモーダル。
- **編集対象ファイル (最小限: 1ファイル)**:
  1. `tools/FurimaServerNative.cs` (HTML/CSS/JSリッチ化、配信ロジック最適化)

#### [ ] **フェーズ 2.1-F: PC側ディスク容量表示 & 30日経過写真クリーンアップ機能の移行** 【未着手】
- **目的**: 
  - PC側画面に `出品写真_PC保存用` の総データサイズを表示。
  - スマホ側から移行した「売却から30日以上経過した写真の一括削除」機能、および削除による「削減見込みデータサイズ」を表示してワンクリック実行可能にする。
- **編集対象ファイル (最小限: 1ファイル)**:
  1. `tools/FurimaServerNative.cs` (容量計算、クリーンアップAPI & UI)

---

## 注意事項 (開発・編集ルール)
1. 返答・編集内容・コマンド・ログの要約のどこにも、アルファベット1文字の直後にコロンが付く書き方（ドライブ文字や短縮オプション等）を書かないこと。パスはプロジェクト相対パスで記述する。
2. `tools/build-exe.bat` は手動修正済みのため編集しないこと。
3. PowerShell の長い1行コマンドで変数を含むものは直接実行せず、必要に応じてスクリプトファイルを作成して実行すること。

---

## 更新履歴 (Changelog)

### [2026-03-30] フェーズ 2.1-D 実装完了 (同期ステータス可視化 - スマホ & PC) バージョン 2.1 (Build 13)
- スマホ側: 出品一覧カードに「未同期」および「原本なし」バッジを表示。
- スマホ側 (PC連携画面): 原本なし写真の一覧表示とクリップボードコピー機能を追加。
- PCブラウザ管理画面 (`DashboardHtml.cs`): ヘッダーにインデックス写真枚数と原本なし件数をリアルタイム表示し、バージョンを `Build 13` に更新。
- アプリ側 versionCode を `13` に更新し、Android / サーバー共にビルド成功を確認。

### [2026-03-30] フェーズ 2.1-C 実装完了 (スマホアプリQRスキャナ連携 & トークン対応) バージョン 2.1 (Build 12)
- Google Play Services Code Scanner (`play-services-code-scanner` / `GmsBarcodeScanning`) を導入し、スマホ「PC連携」画面に「QRコードを読み取る」ボタンを追加。
- `furima:{url}|{token}|{storeId}` 形式の QR コードテキストを自動パースして URL・トークン・Store ID を保存・保持するロジックを ViewModel に実装。
- すべての PC サーバー通信 (`checkServerStatus`, `sendHeartbeat`, `fetchServerHashes`, `syncDeltaToPcServer`) に `Authorization: Bearer` トークンを自動付与。
- 応答 `storeId` と記憶された `storeId` が異なる場合は同期を中止して事故を防止。
- アプリ側 versionCode を `12` に更新し、ビルド成功を確認。

### [2026-03-30] フェーズ 2.1-B 実装完了 (PC母艦サーバーGUI化・認証トークン・QRコード生成) バージョン 2.1 (Build 11)
- `furima-server.exe` 起動時に超軽量ウィンドウ (Windows Forms) を表示するコードを追加。
- `cloudflared.exe` を子プロセスで背景起動し、`trycloudflare.com` トンネルURLを自動取得して表示。
- トンネルURL・ワンタイム認証トークン・`storeId` を含んだ QRコードを自前描画 (System.Drawing) してウィンドウ上に提示。
- ループバック以外からの `/api/` へのアクセスに対し `Authorization: Bearer` トークンチェックによる 401 ガードを追加。
- `tools/DashboardHtml.cs` (Build 11) および `app/build.gradle.kts` (versionCode 11) を統一更新。

### [2026-03-30] フェーズ 2.1-A3 完了 (Newtonsoft / Moshi 交換 & 写真の二段階 POST) バージョン 2.1 (Build 10)
- PCサーバー側 (`tools/FurimaServerNative.cs` 等) から `JavaScriptSerializer` の依存を完全に除去し、`Newtonsoft.Json` に統一。
- スマホアプリ側 (`SyncDtos.kt`, `PcServerClient.kt`) で `Moshi` を使用したプロトコル v3 の実装。
- `POST /api/sync/items` (JSONメタデータ) と `POST /api/sync/photo` (生画像データ + `X-Photo-Hash` ヘッダー) の二段階通信を導入し、サーバー保存後の自己計算 MD5 のみを正として検証。
- バージョン表記を `Build 10` (versionCode 10) に繰り上げ更新。

### [2026-11-07] 差分同期のギャップ調査と原因特定完了（既存写真未送信問題）
- **現象**: PC側のハッシュ存在チェック（Integrity Check）および双方向クリーンアップ（Garbage Collection）を実装・再起動したものの、既存データの写真ファイルがPC側に再送・復元されない問題が発生。
- **原因究明 (Logcat調査)**: スマホ側 (`PcServerClient.kt`) において、送信ガード条件 `!photo.isOriginalDeleted` が設定されていたため、過去にスマホ側で元画像を削除（サムネイル化）した既存写真については、PC側からハッシュが要求されてもスマホ側が「元画像がない」と判断してBase64データの送信を自らブロック（スキップ）していたことが判明。
- **修正案**: スマホ側の送信条件において、元画像ファイル (`file.exists()`) がなくても、サムネイルや代替画像が存在するか、あるいはPC側が求めている場合は送信可能とする（またはフォールバックする）改修が必要。

### [2026-11-07] フェーズ 2.1-A 完了 (スマホ側設定タブ整理 & MD5ハッシュ永続化修復) バージョン 2.1 (Build 8)
- スマホ設定画面 (`SettingsStorageScreen.kt`) から不要になった「PC送信済み写真の元画像を削除」ボタンおよびダイアログを削除しUIを簡素化。
- Room DB (`ItemDao.kt`) に `updatePhotoMd5Hash` クエリを追加。
- クライアント (`PcServerClient.kt`) において、同期時に自動計算された写真の MD5 ハッシュを Room DB に永続保存するよう改修し、サムネイル化後の2回目以降の同期でハッシュズレや再送信が発生する問題を根本解消。
- アプリ側 (`build.gradle.kts` versionCode 8)、Web管理画面 (`WebDashboardHtml.kt` Build 8)、母艦サーバー (`FurimaServerNative.cs` Build 8) のバージョン表記を同時統一更新し、正常ビルド成功を確認。

### [2026-11-07] バージョン 2.1 (Build 7) 移行準備・フェーズ計画策定 & チェックポイント完了
- 通信方式を「Cloudflare Quick Tunnel + ワンタイム認証トークン + QRコード接続」に拡張する方針転換に伴い、マイナーバージョンを **v2.1 (Build 7)** に繰り上げ。
- アプリ側 (`build.gradle.kts` versionCode 7, versionName 2.1)、Web画面 (`WebDashboardHtml.kt` Build 7)、およびサーバー側 (`FurimaServerNative.cs` Build 7) を一斉更新。
- `cloudflared.exe` (v2026.10.7) を `tools/` 配下に配置完了。
- ユーザー要望（旧ローカルサーバー残し、余白詰め/数値強調、同期件数可視化、設定タブ整理、30日経過クリーンアップPC移行、ディスク容量表示）を反映し、実装規模の小さい順にフェーズ 2.1-A 〜 2.1-F へ分割・編集対象ファイルを明記。
- ロールバックおよび引き継ぎを可能にするため Git チェックポイントを策定。

### [2026-11-07] ステップ 4 (E2E通信検証・C#側ArrayListキャスト不具合修復・MD5差分同期実動作確認) 完了 バージョン 2.0 (Build 6)
- PC母艦サーバー (`FurimaServerNative.cs`) において、JSONデシリアライズ時の `ArrayList` キャスト不具合を `System.Collections.IEnumerable` に修正し、データ受信処理が正常にキックされるよう修正。
- エミュレーターアプリ (`com.example.myapplication`) からの差分同期＆容量解放ボタン押下により、5件の商品および8枚の画像ファイルがPC側ローカルディレクトリ (`出品写真_PC保存用`) へフォルダ・説明文.txtとともに完全同期保存されることを確認。
- ハッシュ照合による差分スキップ（2回目の同期時に写真転送なしで38ms完了）の実動作を確認。

### [2026-11-07] ステップ 3 (`FurimaServerNative.cs` PUSH受信用ログ＆個別フォルダ/説明文.txt生成) 完了 バージョン 2.0 (Build 5)
- PUSH リクエスト受信時のコンソール表示を明確化し、各商品の `ID_商品名` フォルダ生成および `商品情報_説明文.txt` の書き込み保存処理を完成。
- `furima-server.exe` の再ビルドコンパイル（ビルド成功）および `PROGRESS.md` の記録完了。
