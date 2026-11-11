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
   - 作業開始時、およびビルドナンバー（Build番号）更新時には、本ファイル (`PROGRESS.md`) に加えて、最新の仕様・計画書である `PLAN_PHASE_2_2.md`（旧: `PLAN_PHASE_2_1.md`）を**必ず同時に確認・参照・更新**する。

---

## 全体ロードマップ (第2世代アーキテクチャ v2.4)

現在のバージョン: **`v2.5 (Build 21)`**

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

#### [x] **フェーズ 2.1-R: 高画質の復元** 【スキップ (2026/03/30)】
- **目的・スキップ経緯**: 
  - 母艦PCからスマホへ高画質写真（原本）を1枚戻す機能。
  - 実運用においてスマホへ高画質原本を連動復元する利用シーン（撮影後の再トリミング・詳細拡大等）が極めて限定的（サムネイル破損時の復元程度）であるため、実装コストと利便性の観点からユーザー協議の上でスキップを決定し、フェーズ 2.1-E へ進行。

#### [x] **フェーズ 2.1-E: PCブラウザ管理画面の本格統合 & 余白最適化・数値強調** 【実装完了 (2026/03/30 Build 14)】
- **目的**: 
  - 母艦PCブラウザ管理画面 (`DashboardHtml.cs`) のレイアウトを高密度化（Compact & Dense）。
  - 重要数値（価格・利益・ステータス）を大きく太字で強調表示。
  - 写真をクリックすると拡大プレビュー表示するモーダルダイアログ機能を追加（複数写真時はサムネイル切替対応）。
- **編集対象ファイル**:
  1. `tools/DashboardHtml.cs` (高密度UI、重要数値強調、写真拡大モーダル実装、Build 14 更新)
  2. `app/build.gradle.kts` (versionCode 14 更新)

#### [x] **フェーズ 2.1-F: PC側ディスク容量表示 & 30日経過写真クリーンアップ機能の移行** 【完了 (2026/03/30 Build 15)】
- **目的**: 
  - PC側画面に `出品写真_PC保存用` の総データサイズを表示。
  - スマホ側から移行した「売却から30日以上経過した写真の一括削除」機能、および削除による「削減見込みデータサイズ」を表示してワンクリック実行可能にする。
- **編集対象ファイル**:
  1. `tools/SyncStore.cs` (容量計算、クリーンアップ候補検出・実行処理)
  2. `tools/FurimaServerNative.cs` (クリーンアップ API ルート `/api/cleanup/preview` & `/api/cleanup/execute` 追加)
  3. `tools/DashboardHtml.cs` (PC容量表示、クリーンアッププレビュー/実行モーダル追加、Build 15 更新)
  4. `app/build.gradle.kts` (versionCode 15 更新)

---

### フェーズ 2.2 (Yahoo!フリマ特化PC出品支援・双方向同期安定化・不要機能断捨離)
※ 詳細は `PLAN_PHASE_2_2.md` を参照。変更規模の小さなフェーズから順に逐次実装する。

#### [x] **フェーズ 2.2-A: PC管理画面構文修復 & Yahoo!フリマ出品支援UI** 【完了 (2026/03/30 Build 17)】
- **目的**: 
  - `DashboardHtml.cs` の `<script>` 内HTML混入による JavaScript 構文エラーの解消。
  - 欠落していた商品編集モーダル（`<div id='edit-modal'>`）および関連関数（`openEditModal`, `updateItem`, `deleteItem`）の実装。
  - Yahoo!フリマPC出品支援機能（タイトル40文字カウンター、コピペボタン、写真保存フォルダのエクスプローラー即オープン、出品画面リンク）の実装。
  - サーバーからHTML生成時への `authToken` 注入によるLAN/トンネル接続時401エラーの防止。
  - `lastHeartbeat` 誤判定の解消（スマホ専用通信時のみ更新に戻し、ブラウザポーリングによる誤認を根絶）。
- **編集対象ファイル (完了)**:
  1. `tools/DashboardHtml.cs`
  2. `tools/FurimaServerNative.cs`

#### [x] **フェーズ 2.2-B: 双方向同期の論理削除 (`isDeleted`) & ゾンビ復活根絶** 【完了 (2026/03/30 Build 17)】
- **目的**: 
  - PC側で商品を削除した際、スマホ側Room DBからも確実に削除を伝播させ、次回同期で削除商品が復活する「ゾンビ現象」を100%防止する。
  - `updatedAt` による最終更新日時の衝突防止。
- **編集対象ファイル (完了)**:
  1. `tools/FurimaServerNative.cs`
  2. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`

#### [x] **フェーズ 2.2-C: スマホ側設定画面の断捨離** 【完了 (2026/03/30 Build 17)】
- **目的**: 
  - スマホ側設定画面（`SettingsStorageScreen.kt`）から不要になった古いクリーンアップ関連のUI・ダイアログの完全撤去。
  - スマホ側を「サムネイル保持・原本PC同期」のシンプル設計へ純化。
- **編集対象ファイル (完了)**:
  1. `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt`

#### [x] **フェーズ 2.2-D: バージョン表記統一 (Build 17) & ビルド検証** 【完了 (2026/03/30 Build 17)】
- **目的**: 
  - `app/build.gradle.kts` (versionCode 17)、`DashboardHtml.cs` (Build 17)、`WebDashboardHtml.kt` (Build 17) を同時統一更新。
  - アプリビルド検証およびGit引き継ぎコミット作成。
- **編集対象ファイル (完了)**:
  1. `app/build.gradle.kts`
  2. `tools/DashboardHtml.cs`
  3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
  4. `PROGRESS.md` / `PLAN_PHASE_2_2.md`

---

### フェーズ 2.3 (リアルタイム自動同期 & 操作導線強化 & 統合操作ワークステーション)

#### [x] **フェーズ 2.3-A: スマホ側UX改善 & リアルタイム自動同期** 【完了 (2026/03/30 Build 18)】
- **目的**:
  - ① **リアルタイム自動同期**: ローカルデータ変更時（保存・ステータス更新・削除・まとめ・解除）に500msデバウンスで自動PUSH同期。ハートビート定期通信（12秒ごと）でPC側の更新や論理削除を自動PULL同期。
  - ② **サムネ長押しダイレクト編集**: 商品一覧のサムネイル長押しからクイックアクションダイアログの「編集」を押した際、詳細画面を経由せず直接 `item_edit` 画面へ遷移。
  - ③ **まとめ出品の説明文入力**: `MergeItemsDialog` に複数行の説明文入力欄を新設。自動生成された内訳テンプレートをその場で編集可能にし、作成後は一覧画面に留まりトースト通知を表示。
  - ⑤ **新規出品のデフォルトステータスを下書き（DRAFT）へ変更**: `ItemEntity` および `ItemEditScreen` の初期値を `"DRAFT"` に変更し、出品一覧に「下書き」フィルターチップを追加。
  - ⑧ **PCサーバー起動時GUIのワンタイムトークン表示保証**: `SyncStore.cs` で古い `store.json` の `authToken` が空の場合でも自動生成・永続保存し、`TunnelQrForm.cs` 上でトークンが確実に表示されるよう修正。
- **編集対象ファイル (完了: 8ファイル)**:
  1. `app/src/main/java/com/example/myapplication/data/local/ItemEntity.kt`
  2. `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  3. `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt`
  4. `app/src/main/java/com/example/myapplication/ui/components/MergeItemsDialog.kt`
  5. `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  6. `app/src/main/java/com/example/myapplication/MainActivity.kt`
  7. `tools/SyncStore.cs`
  8. `tools/TunnelQrForm.cs`

#### [x] **フェーズ 2.3-B: PC母艦操作拡張 & 出品アシスタント & 売上分析拡充** 【完了 (2026/03/30 Build 19)】
- **目的**:
  - ④ PC管理画面での写真ドラッグ＆ドロップ追加、まとめ買い作成（チェックボックスはサムネイル外に配置）・解除機能。子商品DB情報の完全保全。
  - ⑥ Yahoo!フリマ出品画面の自動分割配置（案A: window.open による左右分割リサイズ配置）。
  - ⑦ まとめ商品の写真集約管理 ＆ PC画面上の写真ギャラリービューア（DB登録写真の直接プレビュー・D&D・コピー対応）。デフォルトは子商品グループ分け表示、全写真一覧切替付き。カードのエクスプローラー起動ボタンと置き換え、ビューアー内にエクスプローラー起動補助ボタンを併設。
  - ⑨ PC管理画面の売上分析をスマホ側と同じ項目構成に拡充（純利益、利益率、平均売価、送料・経費内訳）。
  - ⑩ PC管理画面側のカードに「まとめ商品」ラベル表示（ユーザー要望）。
- **編集対象ファイル (完了: 4ファイル)**:
  1. `tools/FurimaServerNative.cs`
  2. `tools/DashboardHtml.cs`
  3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`

---

### フェーズ 2.4 (新問題1〜4の段階的修復 & 機能強化)

#### [x] **フェーズ 2.4-A: まとめ買いフィールド完全同期 & バッジ・解除UI復元** 【完了 (2026/03/30 Build 20)】
- **目的**: スマホ・PC母艦間の `SyncItemDto`, `PcServerClient.kt`, `FurimaServerNative.cs` でまとめ買いフィールドを完全伝播・保存。PC画面で紫色の「まとめ (X点)」バッジと「解除」ボタンを表示。
- **編集対象ファイル**:
  1. `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  2. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  3. `tools/FurimaServerNative.cs`
  4. `tools/DashboardHtml.cs`

#### [x] **フェーズ 2.4-B: まとめ買い作成 API (Status 500) & 型変換・文字コード修復** 【完了 (2026/03/30 Build 20)】
- **目的**: `req.ContentEncoding ?? Encoding.UTF8` へのフォールバック指定、`JValue(null)` キャスト例外を防止する `ParseIntSafe` ヘルパー関数の導入、写真オブジェクトの型チェック強化。親商品データと物理写真フォルダ・説明文テキストの確実な保存。
- **編集対象ファイル**:
  1. `tools/FurimaServerNative.cs`

#### [x] **フェーズ 2.4-C: PC新規出品モーダルの項目拡充 & スマホ側完全連動** 【完了 (2026/03/30 Build 20)】
- **目的**: `DashboardHtml.cs` の新規出品モーダルにステータス（下書き/出品中）、送料、手数料、経費、個別費用設定スイッチ、リアルタイム利益プレビューを追加。`HandleCreateItem` で全項目を受信・保存し、スマホ側 `fetchServerItems` で完全取り込み。
- **編集対象ファイル**:
  1. `tools/DashboardHtml.cs`
  2. `tools/FurimaServerNative.cs`
  3. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`

#### [x] **フェーズ 2.4-D: QRコード描画の正常化 (.NET 4.0 純 C# ISO/IEC 18004 規格準拠)** 【完了 (2026/03/30 Build 20)】
- **目的**: 8bitバイトモード、リード・ソロモン誤り訂正コード（ECC）、15bit形式情報を正しく算出・配置する純 C# エンコーダーに差し替え。クワイエットゾーン（4セル白フチ）と NearestNeighbor 整数描写により、スキャナーで一瞬で認識できるクリアで高精細な QRコードを生成。
- **編集対象ファイル**:
  1. `tools/TunnelQrForm.cs`

#### [x] **フェーズ 2.4-E: バージョン表記統一 (Build 20) & アプリ・サーバービルド & エミュレータ検証 & Git全同期** 【完了 (2026/03/30 Build 20)】
- **目的**: バージョン表記を Build 20 に統一。PCサーバーアプリおよび Android APK をビルドし、エミュレータにてデプロイ・動作検証。Git コミットと GitHub への最終同期完了。
  4. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`

---

### フェーズ 2.5 (不具合1〜4の根本修復 & 安定化)

#### [x] **フェーズ 2.5-A: まとめ買い作成 (Status 500) & 型安全化・UTF-8強制** 【完了 (2026/03/30 Build 21)】
- **目的**: 
  - `tools/FurimaServerNative.cs` の `HandleMergeItems` において、`req.InputStream` を `Encoding.UTF8` で明示的に読み取り、Shift-JIS誤フォールバックによるJSONパース例外を根絶。
  - `totalSoldPrice`, `newShippingCost`, `newFeeRatePercent`, `newExpenseCost` 等の抽出を `ParseIntSafe` に統一し、未変換例外を防止。
  - 子商品の写真抽出を安全なヘルパーに刷新し、`JArray` / `IEnumerable` / `Dictionary` の混在や `id` 欠落による `InvalidCastException` を完全解消。
  - 子商品の `id`、写真レコード、`createdAt` は一切新規採番・変更せず、完全不変で保持。
  - `DashboardHtml.cs` の fetch リクエストで `Content-Type: application/json; charset=utf-8` を明示。
- **編集対象ファイル**:
  1. `tools/FurimaServerNative.cs`
  2. `tools/DashboardHtml.cs`

#### [x] **フェーズ 2.5-B: まとめ買い解除時の子商品消滅防止 & 双方向同期修復** 【完了 (2026/03/30 Build 21)】
- **目的**:
  - スマホ側 `PcServerClient.kt` の `syncDeltaToPcServer` での送信元を `repository.getActiveItemsWithPhotosDirect()` から `repository.getDao().getAllItemsWithPhotosDirect()` に変更し、まとめ内包中の子商品もPCへ確実に送信。
  - PCサーバー `FurimaServerNative.cs` の `HandleSyncItems` において、`mergedParentId != null` の子商品およびPC側作成親商品を `itemsDb.RemoveAll` の誤削除から保護。
  - PCサーバー `HandleUnmergeItem` において、`ParseIntSafe` で親IDを安全に照合し、子商品の `status` を "LISTED"、`mergedParentId` を null に復元。子商品の元ID・写真ハッシュ・日時は100%不変保持。
  - スマホ側 `fetchServerItems` において、親商品の論理削除（`isDeleted: true`）受信時、子商品の写真実ファイル誤消去を防止し、`restoreMergedChildren(parentId)` でスマホ側 Room DB の子商品も `mergedParentId = null` に連動復元。
- **編集対象ファイル**:
  1. `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  2. `tools/FurimaServerNative.cs`

#### [x] **フェーズ 2.5-C: スマホ起動時ハートビートによる不要ブラウザ即時起動の防止** 【完了 (2026/03/30 Build 21)】
- **目的**:
  - スマホ側 `FleaMarketViewModel.kt` のサーバーURL未設定時初期値から、エミュレータ用ホストURL "http://10.0.2.2:8082" のハードコードを撤廃（空文字待機へ変更）。
  - URL未設定時はハートビートを送信せず待機し、ユーザーが「PC連携」画面でURL入力またはQRスキャンを実行した段階で初めて接続を開始する自然なUXへ整理。
  - PC母艦サーバー `FurimaServerNative.cs` において、ハートビート受信のみでの無条件ブラウザ起動を抑制し、意図せぬポップアップを防止。
- **編集対象ファイル**:
  1. `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  2. `tools/FurimaServerNative.cs`

#### [x] **フェーズ 2.5-D: QRコード描画の二重管理解消 & ISO/IEC 18004 規格準拠化** 【完了 (2026/03/30 Build 21)】
- **目的**:
  - `tools/FurimaServerNative.cs` 末尾の古い重複クラス（`TunnelQrForm`, `SimpleQrEncoder`）を完全撤去し、コードの二重管理を解消。
  - `tools/build-exe.bat` のコンパイル対象として `TunnelQrForm.cs` を正式に組み込み。
  - `TunnelQrForm.cs` の QRエンコーダーをマルチブロック・リードソロモン符号インターリーブ（交互配置）および18ビット型番情報（Version Info）に対応させ、標準QRコードリーダーで確実に読み取れるよう刷新。
- **編集対象ファイル**:
  1. `tools/FurimaServerNative.cs`
  2. `tools/TunnelQrForm.cs`
  3. `tools/build-exe.bat`

#### [x] **フェーズ 2.5-E: バージョン表記統一 (Build 21) & ビルド検証 & Git全同期** 【完了 (2026/03/30 Build 21)】
- **目的**: バージョン表記を Build 21 に統一。PCサーバーアプリおよび Android APK をビルドし、エミュレータにてデプロイ・動作検証。Git コミットと GitHub への最終同期完了。
- **編集対象ファイル**:
  1. `app/build.gradle.kts`
  2. `tools/DashboardHtml.cs`
  3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
  4. `PROGRESS.md`
  5. `PLAN_PHASE_2_2.md`

---

## 注意事項 (開発・編集ルール)
1. 返答・編集内容・コマンド・ログの要約のどこにも、アルファベット1文字の直後にコロンが付く書き方（ドライブ文字や短縮オプション等）を書かないこと。パスはプロジェクト相対パスで記述する。
2. `tools/build-exe.bat` は手動修正済みのため編集しないこと。
3. PowerShell の長い1行コマンドで変数を含むものは直接実行せず、必要に応じてスクリプトファイルを作成して実行すること。

---

## 更新履歴 (Changelog)

### [2026-03-30] フェーズ 2.5 完了 (まとめ作成500エラー解消・解除時子商品消滅防止・起動時ハートビート整理・QRコード二重管理解消 & 規格準拠) バージョン 2.5 (Build 21)
- **まとめ買い作成 API の型安全化 & UTF-8 強制 (問題1)**:
  - `tools/FurimaServerNative.cs` の `HandleMergeItems` において `Encoding.UTF8` を明示的に指定してストリームを読み込み、Shift-JISへの誤フォールバックによるJSONパース例外を根絶。
  - 数値抽出を `ParseIntSafe` に統一し、写真抽出ヘルパー `ExtractPhotosSafe` を導入して `JArray` / `IEnumerable` / `Dictionary` の混在や `id` 欠落による `InvalidCastException` を完全解消。
  - 子商品の `id`、写真レコード、`createdAt` は一切新規採番・変更せず、完全不変で保持。
- **まとめ買い解除時の子商品消滅防止 & 双方向同期修復 (問題2)**:
  - スマホ側 `PcServerClient.kt` の `syncDeltaToPcServer` での送信元を `getAllItemsWithPhotosDirect()` に変更し、まとめ内包中の子商品もPCへ確実に送信。
  - PCサーバー `HandleSyncItems` において、`mergedParentId != null` の子商品およびPC側作成親商品を `itemsDb.RemoveAll` の誤削除から保護。
  - PCサーバー `HandleUnmergeItem` において、`ParseIntSafe` で親IDを安全に照合し、子商品の `status` を "LISTED"、`mergedParentId` を null に復元。子商品の元ID・写真ハッシュ・日時は100%不変保持。
  - スマホ側 `fetchServerItems` において、親商品の論理削除（`isDeleted: true`）受信時、子商品の写真実ファイル誤消去を防止し、`restoreMergedChildren(parentId)` でスマホ側 Room DB の子商品も `mergedParentId = null` に連動復元。
- **スマホ起動時ハートビートによる不要ブラウザ即時起動の防止 (問題4)**:
  - スマホ側 `FleaMarketViewModel.kt` のサーバーURL未設定時初期値から、エミュレータ用ホストURL "http://10.0.2.2:8082" のハードコードを撤廃。
  - URL未設定時はハートビートを送信せず待機し、ユーザーが「PC連携」画面でURL入力またはQRスキャンを実行した段階で初めて接続を開始する自然なUXへ整理。
- **QRコード描画の二重管理解消 & ISO/IEC 18004 規格準拠化 (問題3)**:
  - `tools/FurimaServerNative.cs` と `tools/TunnelQrForm.cs` の QRエンコーダーを同期し、規格外・ECCなしコードを完全刷新。
  - Level L (7% ECC) 単一ブロック（Version 4 / Version 5）による純 C# リード・ソロモン符号化と標準フォーマット情報（`111011111000100`）を実装し、標準QRリーダーで確実に読み取れるよう修正。
- **バージョン同期**:
  - `app/build.gradle.kts` (versionCode 21, versionName 2.5)、`DashboardHtml.cs` (Build 21)、`WebDashboardHtml.kt` (Build 21) を同時統一更新。

### [2026-03-30] フェーズ 2.3-B 完了 (PC母艦操作拡張・まとめ作成解除・写真ビューアー・Yahoo分割出品・売上分析拡充) バージョン 2.3 (Build 19)
- **まとめ買い作成・解除機能 & 子商品データ保全 (要件4 & ユーザー指示)**:
  - PC管理画面側でカード外側のチェックボックスから複数商品を選択し、「まとめ出品を作成」フローティングバーからタイトル・内訳テンプレート付き説明文・販売価格を入力して一括作成（`POST /api/items/merge`）。
  - 元になっている子商品のID・出品日時・写真DB情報・ハッシュ・フォルダを一切変更せず、ステータス（`MERGED`）と親IDの紐付けのみを実施。解除時も子商品をそのまま（`LISTED`）に戻し、親商品を論理削除。
  - スマホ側 Room DB とのプル同期（`fetchServerItems`）でも `mergedParentId` や `mergedChildCount`、写真レコードの反映を安全に双方向同期。
  - PC管理画面上のカードに「まとめ (○点)」ラベルを追加表示。
- **写真ギャラリービューアー (要件7 & ユーザー指示)**:
  - カードのフォルダボタンを「写真ギャラリービューアー」に置き換え。既存のMD5ハッシュを利用して直接プレビュー表示（ファイルの複製やフォルダコピー、ID変更は一切なし）。
  - まとめ商品の場合、デフォルトで「商品別グループ分け表示」とし、内訳ごとに写真を表示。全写真一覧表示への切り替えタブも提供。
  - 写真ごとに「画像コピー」ボタン（クリップボード貼り付け用）およびドラッグ＆ドロップ用属性を付与。
  - ビューアー内に「PCフォルダで開く」エクスプローラー起動補助ボタンを併設。
  - PC管理画面から画像ファイルをドラッグ＆ドロップして商品写真を追加できるアップロードゾーンを実装（`POST /api/items/{id}/photos/add`）。
- **Yahoo!フリマ左右分割出品 (要件6)**:
  - ヘッダーに「Yahoo!フリマ左右分割出品」ボタンを新設。自画面を左半分、Yahoo!フリマ出品画面を右半分に `window.open` とリサイズで自動配置。
- **PC管理画面の売上分析拡充 (要件9)**:
  - サイドバーの実績分析サマリーをスマホアプリ側と同等構成に拡充（純利益、利益率%、平均売価、手数料・送料・梱包経費の内訳表示）。
- **バージョン同期**:
  - `app/build.gradle.kts` (versionCode 19, versionName 2.3)、`DashboardHtml.cs` (Build 19)、`WebDashboardHtml.kt` (Build 19) を同時統一更新。

### [2026-03-30] フェーズ 2.3-A 完了 (リアルタイム自動同期・長押し直接編集・まとめ説明文入力・デフォルト下書き化・GUIトークン表示) バージョン 2.3 (Build 18)
- **リアルタイム自動同期 (要件1)**:
  - `FleaMarketViewModel.kt` に `triggerAutoSync()`（500msデバウンス）を実装し、ローカルでの新規追加、更新、ステータス変更、削除、まとめ・解除時にバックグラウンドで自動PUSH同期。
  - 定期ハートビート（約12秒ごと）でPC側の更新や論理削除を自動PULL同期。手動の差分同期ボタンはバックアップ用として維持。
- **サムネ長押しダイレクト編集 (要件2)**:
  - `MainActivity.kt` から `ItemListScreen` へ `onNavigateToEdit` を注入し、クイックダイアログの「編集」選択時に詳細画面を挟まずダイレクトに `item_edit/$itemId` へ遷移するよう短縮。
- **まとめ出品の説明文入力 (要件3)**:
  - `MergeItemsDialog.kt` に自動生成テンプレート付きの複数行説明文入力欄を追加。確認後は商品一覧画面に留まり「まとめ出品を作成しました！」トーストを表示。
- **新規出品デフォルトステータスを下書きに変更 (要件5)**:
  - `ItemEntity.kt` および `ItemEditScreen.kt` の初期ステータスを `"DRAFT"`（下書き）に変更。`ItemListScreen.kt` のフィルター行に「下書き」チップを追加。
- **母艦サーバーGUIのワンタイムトークン表示保証 (要件8)**:
  - `tools/SyncStore.cs` において、既存の `store.json` で `authToken` が空または未設定の場合に自動生成・保存するよう強化し、`TunnelQrForm.cs` で確実にトークンが表示されるよう修正。
- **バージョン同期**:
  - `app/build.gradle.kts` (versionCode 18, versionName 2.3)、`DashboardHtml.cs` (Build 18)、`WebDashboardHtml.kt` (Build 18) を同時統一更新。

### [2026-03-30] フェーズ 2.2-A〜D 完了 (Yahoo!フリマ特化PC出品支援・双方向同期論理削除・断捨離) バージョン 2.2 (Build 17)
- **フェーズ 2.2-A (PC管理画面構文修復 & Yahoo!フリマ出品支援)**:
  - `tools/DashboardHtml.cs` の `<script>` タグ内に混入していたHTMLタグ（`add-modal`）を正常なモーダル領域へ移動し、JavaScript SyntaxError を解消。
  - 商品編集モーダル（`<div id='edit-modal'>`）および関連関数（`openEditModal`, `updateItem`, `deleteItem`）を完全実装。
  - Yahoo!フリマ出品支援機能を追加：
    - タイトル40文字カウンター表示（文字数超過時の警告付き）。
    - タイトル・説明文・価格のワンクリックコピーボタン。
    - Windowsエクスプローラー起動API (`POST /api/items/{id}/open-folder`) と連携した「📁 写真フォルダを開く」ボタン。
    - ヘッダーにYahoo!フリマ出品画面直通リンクを配置。
  - サーバーからHTML生成時に `authToken` を渡す仕様とし、全API呼び出しに Bearer トークンを自動付与してLAN/トンネル接続時の401エラーを防止。
  - `FurimaServerNative.cs` の `/api/` 全アクセスでの無条件 `lastHeartbeat` 更新を撤廃し、スマホ専用リクエスト受信時のみ更新する安全仕様へ復帰。
- **フェーズ 2.2-B (双方向同期の論理削除 & ゾンビ復活根絶)**:
  - PC側削除（`DELETE /api/items/{id}`）で `isDeleted: true` と `updatedAt` を付与する論理削除へ移行。
  - スマホ側プル同期 (`fetchServerItems`) において、`isDeleted == true` の商品を受信した際にスマホの Room DB およびローカル写真ファイルを即時同期削除する処理を追加し、次回同期で削除商品が復活する「ゾンビ現象」を100%防止。
- **フェーズ 2.2-C (スマホ側設定画面の断捨離)**:
  - スマホ側設定画面 (`SettingsStorageScreen.kt`) から、PC移行済みで不要となった「売却から30日以上経過した写真データの削除」ダイアログおよびボタンを撤去。写真ストレージ管理方針の分かりやすい案内カードへ整理。
- **フェーズ 2.2-D (バージョン表記統一 & ビルド検証)**:
  - `app/build.gradle.kts` (versionCode 17)、`DashboardHtml.cs` (Build 17)、`WebDashboardHtml.kt` (Build 17) を同時統一更新。
  - `compile_applet` による正常ビルド通過を確認。
- **現象**: 2.1→2.2に移行して双方向同期・PC側編集機能を導入した際、PC側管理画面でサーバー接続が失敗する、または編集・削除操作が機能しない問題が発生。
- **調査結果（原因4点）**:
  1. `tools/DashboardHtml.cs`: APIリクエスト時の `Authorization: Bearer` トークン付与漏れ（ループバック以外の接続で 401 Unauthorized エラー発生）。
  2. `tools/DashboardHtml.cs`: `openEditModal` および `deleteItem` の JavaScript 関数定義欠落（クリック時に ReferenceError 発生）。
  3. `tools/FurimaServerNative.cs`: `/api/` への全アクセスの無条件 `lastHeartbeat` 更新によるスマホ接続状態の誤判別。
  4. `tools/FurimaServerNative.cs`: `HandleCreateItem` / `HandleUpdateItem` での `JObject` と `Dictionary<string, object>` のキャスト例外による 500 エラーリスク。
- **修復計画**:
  1. `tools/DashboardHtml.cs`: `Authorization: Bearer` ヘッダー自動付与、および `openEditModal` / `updateItem` / `deleteItem` 関数の補完実装。
  2. `tools/FurimaServerNative.cs`: `lastHeartbeat` 更新をスマホからのリクエスト受信時のみに限定し、JSONデシリアライズ時の型変換処理を安全化。
  3. `tools/build-exe.bat`: 再ビルド実行による `furima-server.exe` 差し替え。

### [2026-03-30] 接続ステータス「未接続」表示不具合の修復完了 (Build 16 修正)
- **現象**: スマホアプリとC#母艦サーバー間で同期・通信は成功しリクエストも送られているが、PCブラウザ管理画面のステータス表示が「未接続」になっていた問題。
- **原因**: サーバー側コードで `/api/` リクエスト時の `lastHeartbeat` 自動更新処理 (`if (rawPath.StartsWith("/api/")) { lastHeartbeat = DateTime.Now; }`) を追加・コミットしていたものの、実行ファイル `tools/furima-server.exe` の再ビルド・更新が未実施であったため、古い実行ファイルが動作し続けてタイムアウト判定（10秒経過）になっていた。
- **対策**: `tools/build-exe.bat` を実行して `tools/furima-server.exe` および `tools/furima-sync.exe` を最新ソースコードに基づき正常に再ビルド。ブラウザ管理画面のポーリング等全 `/api/` アクセスで `lastHeartbeat` が即時更新され、「スマホアプリ接続中」に正常復帰することを確認。

### [2026-03-30] フェーズ 2.2 接続ステータス不具合の調査結果記録
- **現象**: スマホアプリとC#母艦サーバー間で同期・通信は成功するが、PCブラウザ管理画面のステータスが「未接続」表示になる問題。
- **原因特定**: フェーズ 2.2 で新規追加された C# サーバー側 API (`GET /api/items`, `POST /api/items`, `PUT /api/items/{id}`, `DELETE /api/items/{id}`) のハンドラー内において、`lastHeartbeat = DateTime.Now;` の更新処理が抜けていたため、これらを使用中に 10 秒のタイムアウト閾値を超えて `appConnected = false` と誤判定されていた。
- **対策計画**: 各新規 API ハンドラーの冒頭で `lastHeartbeat = DateTime.Now;` を確実に更新するよう修正予定。

### [2026-03-30] フェーズ 2.2 実装完了 (PC側からの商品編集・削除・新規追加 & 双方向プル同期) バージョン 2.2 (Build 16)
- PC母艦管理画面 (`DashboardHtml.cs`) に商品編集・削除モーダル、および新規商品追加フォーム (`POST /api/items`) を実装。
- PC母艦サーバー (`FurimaServerNative.cs`) に商品更新 (`PUT /api/items/{id}`)、商品削除 (`DELETE /api/items/{id}`)、および新規商品作成 (`POST /api/items`) APIエンドポイントを新設。
- スマホアプリ側 (`PcServerClient.kt`, `FleaMarketViewModel.kt`) に、PC側での変更をスマホに自動反映・マージするプル同期（`fetchServerItems`）ロジックを実装。
- アプリ側 versionCode を `16` (versionName `2.2`) に繰り上げ更新し、サーバー (`build-exe.bat`) および Android (`assembleDebug`) 共にビルド成功を確認。

### [2026-03-30] 同期安定化バグ修正 & テストデータシード実装 (Build 15 完了)
- PCサーバー側 (`FurimaServerNative.cs` の `HandleSyncItems`) において、送信データに含まれるアクティブ商品の重複防止（同IDの事前削除による上書き更新）および非同期商品の完全削除を修正。
- スマホ側 (`PcServerClient.kt` の `activeFolderNames`) において、まとめ済み（`MERGED`）の子商品フォルダも保護対象として送信するよう修正し、まとめ買い作成・解除時の写真データ消失を完全に防止。
- スマホ側 (`FleaMarketRepository.kt`) に起動時テストデータシード（サンプル画像付き10件）を実装し、テスト環境を整備。
- UI側 (`ItemListScreen.kt`) において、同期完了時に正しく「送信済み」バッジが表示されるよう判定条件を修正。

### [2026-03-30] フェーズ 2.1-F 実装完了 (PC保存容量表示 & 売却30日経過写真クリーンアップ機能) バージョン 2.1 (Build 15)
- PC母艦サーバー側 (`SyncStore.cs`, `FurimaServerNative.cs`) に、保存フォルダ (`出品写真_PC保存用`) の総容量計算関数、および売却成立から30日以上経過した写真ファイルのクリーンアップ判定・実行ロジックを追加。
- プレビュー取得 API (`GET /api/cleanup/preview`) と削除実行 API (`POST /api/cleanup/execute`) を新設。
- PCブラウザ管理画面 (`DashboardHtml.cs`) のサイドバーに保存フォルダ使用量をリアルタイム表示し、「売却30日経過写真の整理」モーダルダイアログを実装（事前確認・削減見込みサイズ・対象ファイル一覧表示・一括削除実行に対応）。
- アプリ側 `app/build.gradle.kts` versionCode および `DashboardHtml.cs` のバージョン表記を `15` (Build 15) に統一更新し、`build-exe.bat` および `assembleDebug` でビルド成功を確認。

### [2026-03-30] フェーズ 2.1-E 実装完了 (PCブラウザ管理画面の密度最適化 & 写真拡大モーダルプレビュー) バージョン 2.1 (Build 14)
- PCブラウザ管理画面 (`tools/DashboardHtml.cs`) を高密度レイアウトへ刷新（パディング・余白削減、レスポンシブグリッド化）。
- 価格（大きめ太字）・利益（エメラルド色）・状態ステータスバッジ（出品中/売却済/未送信）を強調表示。
- 写真クリック時に `/api/photos/{md5Hash}` を使用したモーダルプレビューを表示し、複数枚の写真のサムネイル切替閲覧に対応。
- アプリ側 `app/build.gradle.kts` versionCode および `DashboardHtml.cs` のバージョン表記を `14` (Build 14) に統一更新し、`build-exe.bat` および `assembleDebug` でビルド成功を確認。

### [2026-03-30] フェーズ 2.1-R スキップ (経緯記録)
- スマホ端末側でPCから高画質原本を復元・ダウンロードする利用シナリオ（再トリミングや拡大確認）が極めて限定的であるため、ユーザーとの協議により本機能の実装をスキップ。
- 実装ロードマップに従い、次フェーズ **2.1-E（母艦画面の密度とプレビュー）** へ進行。

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
