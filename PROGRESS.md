# 開発進捗・引き継ぎログ (Progress & Handover Log) - 第2世代

このドキュメントは、第2世代アーキテクチャ（母艦PCマスターサーバー型 & MD5ハッシュ差分同期構成）における各実装フェーズの進捗状況、現状のギャップ調査結果、および修復記録を保持するためのものです。

> **過去の開発進捗ログ・詳細計画書 アーカイブ & 現行計画書**:
> - 第1世代〜初期ログ: [PROGRESS_ARCHIVE_PHASE1.md](PROGRESS_ARCHIVE_PHASE1.md)
> - フェーズ2.0〜2.11全詳細ログ・計画書: [PROGRESS_ARCHIVE_PHASE2.md](PROGRESS_ARCHIVE_PHASE2.md)
> - フェーズ 2.12〜2.14 計画書: [PLAN_PHASE_2_12_TO_2_14.md](PLAN_PHASE_2_12_TO_2_14.md)
> - フェーズ 3.1 計画書: [PLAN_CAMERA_AND_PREVIEW_FIX.md](PLAN_CAMERA_AND_PREVIEW_FIX.md)
> - フェーズ 3.2 計画書: [PLAN_PHASE_3_2.md](PLAN_PHASE_3_2.md)
> - **現行実装計画書 (フェーズ 4 / 重大バグ修復 ➔ PC写真ギャラリー改良＆写真ゴミ箱[案A] ➔ 同期ステータス％表示＆メモリ安定化)**: [PLAN_SYNC_STATUS_AND_MEMORY.md](PLAN_SYNC_STATUS_AND_MEMORY.md)
> - **現行実装計画書 (フェーズ 5 / 「Museに送る」一括画像加工連携)**: [PLAN_CLAUDE_SEND_PHASE5.md](PLAN_CLAUDE_SEND_PHASE5.md)（2026-11-16 策定・**協議済み（計画書 §10 に協議結果を記録）** / 事前レビュー: [REVIEW_CLAUDE_SEND_PHASE5.md](REVIEW_CLAUDE_SEND_PHASE5.md)）
> - **次期実装計画書 (フェーズ 6 / 「Museに送る」運用自動化・見える化・出品ステータス拡張)**: [PLAN_CLAUDE_SEND_PHASE6.md](PLAN_CLAUDE_SEND_PHASE6.md)（2026-11-16 策定・**大枠完全合意（計画書 §9・§10）。6-1 を 6-1a／6-1b に分割し Build 14〜21 に再編。次は 6-1a からサブフェーズ詳細化** / 事前レビュー: [REVIEW_CLAUDE_SEND_PHASE6.md](REVIEW_CLAUDE_SEND_PHASE6.md)）

---

## 開発・引き継ぎ基本原則 (Important Rules)

1. **小規模逐次実装**: 変更規模の小さなフェーズから優先的に1つずつ実装する。
2. **最小限のファイル編集**: 各フェーズごとに必要最小限のファイルのみを編集対象とし、不要なファイルを触らない。
3. **完全な進捗記録**: 各フェーズの完了時、または大きめの変更（データ保存・通信方式の変更、機能の追加・削除など）が発生するたびに、本ファイル (`PROGRESS.md`) に「フェーズ〇〇完了」と明確に記録を残す。
4. **既存UI・レイアウトの維持**: 特別な指示がない限り、ユーザーが気に入っているアプリの見た目・レイアウト・配色を変更しない。
5. **ビルド検証の最適化 (トークン消費節約)**: 純粋なWeb UI/HTML/CSS/JSの調整や、Kotlinロジックに変更のない軽微なデザイン調整時は、毎回 `gradle_build` を実行せずビルドを省略・スキップしてトークン消費と処理時間を抑える。
6. **厳格なバージョン表記・同時統一更新規則 (Strict Versioning Rule)**:
   - 各完了フェーズ・更新履歴ごとに「進捗状況」と「バージョン・Build番号」を完全対応させて明記する。
   - 表記フォーマット: `yyyy/mm/dd フェーズ〇〇完了 バージョン 0.x (Build x)`
   - バグ修正・レイアウト調整・内部処理順変更等の小規模変更: Build番号を加算（例: `Build 1` ➔ `Build 2`）。
   - **バージョン表記の更新義務**: バージョン更新時は、**スマホアプリ側 (`app/build.gradle.kts`)**、**PC管理画面 (`DashboardHtml.cs`)**、**スマホ緊急Web管理画面 (`WebDashboardHtml.kt`)** の3箇所を**必ず同時に統一して更新**する。
7. **仕様・実装計画書の併せての確認ルール (Plan File Verification Rule)**:
   - 作業開始時、およびビルドナンバー（Build番号）更新時には、本ファイル (`PROGRESS.md`) に加えて、最新の仕様・計画書を**必ず同時に確認・参照・更新**する。
8. **フェーズ完了ごとの Git コミット & GitHub 同期ルール**:
   - 各フェーズ（またはサブフェーズ）の完了ごとに、対象ファイルを選択的に `git add` して `git commit` を作成し、**`git push origin main` で GitHub リモートへ同期**する。
9. **実機直接インストール保証・`testOnly` 防止ルール (Physical Device Installation Rule)**:
   - スマホ実機への直接インストール（ファイルマネージャー等からのタップ）を常に保証するため、`gradle.properties` に `android.injected.testOnly=false` を常時固定し、`targetSdk = 35`（安定版）を維持する。
   - APK 再ビルド時は、生成された `app-debug.apk` をプロジェクトルートの `flea_market_app.apk` および `public/flea_market_app.apk` へ上書き更新して実機配布用ファイルとして提示する。

---

## 全体ロードマップ (第2世代アーキテクチャ)

現在のバージョン：**`v1.0.0` (Build 13)** （フェーズ 4-1 〜 4-4、5-1 〜 5-5、Build 13 レビュー修正 完了）

### これまでに完了した主要開発マイルストーン (詳細は `PROGRESS_ARCHIVE_PHASE2.md` を参照)
- [x] **フェーズ 2.0 〜 2.7 (基幹同期・まとめ買い・ゾンビ親商品根絶・選択ガード)** 【完了】
  - 差分同期の安定化、まとめ買い作成・解体復元、論理削除伝播によるゾンビ復活防止、売却済み選択ガード、まとめ点数合算表示の適正化。
- [x] **フェーズ 2.8 (同期競合防止・QR完全修復・接続時ブラウザ起動・Yahoo画面改善)** 【完了】
  - PULL先行型自動同期、ISO/IEC 18004 準拠 QRコード描画、緑ステータス確定時ブラウザダイアログ表示、Yahoo!フリマ画面の通常タブ化。
- [x] **フェーズ 2.9 (日時双方向同期・PC表示優先度ソート・CSVダウンロード強化)** 【完了】
  - `createdAt`/`updatedAt`/`soldAt` の完全同期、PC管理画面での「出品中まとめ→未出品まとめ→出品中単品→下書き単品→売却済み」優先度ソート、PC側 CSV ダウンロードエクスポート。
- [x] **フェーズ 2.10 (バックボタン安全化・サムネ容量可視化・ギャラリー保存刷新)** 【完了】
  - 未保存変更破棄ダイアログ・タブ復元・終了ダイアログ等のバックキーガード、圧縮率別削減容量リアルタイム表示、詳細画面「PCに送信」分離および原本取得付きギャラリー自己修復。
- [x] **フェーズ 2.11 (基本設定/プリセット同期・0円許容&写真削除API・PC編集モーダル同等化)** 【完了】
  - 基本費用/プリセットのスマホ→PC同期、送料・経費0円および手数料0%の許容、写真個別削除 API、PC新規/編集モーダルの完全スマホ同等化。
- [x] **実機判定による初期テストデータシード自動オフ機能** 【完了】
  - `FleaMarketRepository.kt` にて実機/エミュレーター判定を実施し、実機環境では初期テストデータ10件を自動挿入せず完全クリーン状態で起動するよう制御。
- [x] **実運用到達に伴うバージョン表記リセット (`v0.9β Build 1`)** 【完了】
  - アプリ・PC管理画面・Web画面すべてのバージョン表記を `v0.9β (Build 1)` に統一。

### これからの実装予定マイルストーン (詳細仕様は `PLAN_PHASE_2_12_TO_2_14.md` を参照)
- [x] **フェーズ 2.12 (`v0.9β Build 2`) — スマホUI不具合修正 ＋ 原本取得付き写真拡大プレビュー** 【完了】
  - `2.12-A` 長押しメニュー (`ItemQuickActionDialog`) のステータス表示不一致修正（4ステータス統一・下書き切替対応）
  - `2.12-B` 長押しメニューからの削除／まとめ解除実行後にメニューが閉じない不具合修正（二重送信防止ガード＋即時クローズ）
  - `2.12-C` 詳細画面＆編集画面でのサムネイルタップ全画面拡大プレビュー（左右スワイプ・ピンチズーム・ダブルタップズーム ＋ PCサーバー接続中の高画質原本取得ボタン付き）
- [x] **フェーズ 2.13 (`v0.9β Build 3`) — 設定双方向同期 ＋ デフォルトプリセット変更強化 ＋ テンプレート機能** 【完了】
  - `2.13-A` `DefaultSettings` / `store.json` 拡張による設定双方向同期基盤（`PUT /api/settings` ＋ `settingsUpdatedAt` 新旧判定、Room DB 無変更）
  - `2.13-B` デフォルトプリセットの切り替え・編集・即時同期（スマホ設定画面・編集画面チップ・PC管理画面モーダル）
  - `2.13-C` タイトル／商品説明テンプレートの登録・編集・カーソル位置選択挿入（スマホ＆PC管理画面共通）
- [x] **フェーズ 2.14 (`v0.9β Build 4`) — 本命「ゴミ箱」機能 ＋ 一人利用最適・競合防止同期への刷新** 【完了】
  - `2.14-A` Room DB 拡張 (`syncUuid`, `deletedAt`, `isDirty`, `MIGRATION_5_6`) ＆ 論理削除/復元/30日超過完全削除リポジトリ
  - `2.14-B` イベント駆動Push ＋ 軽量リビジョン検知 (`dataRevision`/`settingsRevision`) Pull ＋ `isDirty`/`updatedAt` 保護 ＋ UUID衝突防止同期エンジン
  - `2.14-C` スマホ＆PC共通「ゴミ箱」UI実装（復元・完全削除・一括削除・30日保持カウント表示）
- [x] **フェーズ 2.15 (`v0.9β Build 5`) — 重大度「致命的」バグ修復（型不一致・原本/MD5破壊・ビルド復旧）** 【完了】
  - [x] `2.15-A` PCサーバー `items_data.json` 再読込後の型不一致 (`JArray`/`JObject` vs `Dictionary`) 根絶（写真追加時消去・フォルダ振り分け・欠損数・30日掃除の復旧）
  - [x] `2.15-B` スマホ側 `cleanupSharedOriginalPhotos` / `saveItem` / `syncDeltaToPcServer` / `mergeServerItemsFromJson` での `isOriginalDeleted` & `md5Hash` 破壊修復
  - [x] `2.15-C` `tools/build-exe.bat` への `TunnelQrForm.cs` 追加 ＋ DLL配置整理 ＋ `Build 5` 統一
- [x] **フェーズ 2.16 (`v0.9β Build 6`) — 重大度「警告」修復（PC単独編集・同期整合・画像処理・UI修正）** 【完了】
  - [x] `2.16-A` デバッグ残骸 `#18` 撤去 ＋ まとめ買い子ステータス適正化＆30日クリーンアップ安全化
  - [x] `2.16-B` PC単独編集向け案内文言刷新（ロック表記撤廃）・`lastHeartbeatSec` / `HandleRequest` / `JValue(null)` / UTF-8安全性・フォルダ分断解消・`soldPrice` / 復元パス修復
  - [x] `2.16-C` 画像形式マジックバイト判定・Canvas経由クリップボードコピー・EXIF回転補正・旧モーダル整理 ＋ `Build 6` 統一
- [x] **フェーズ 2.17 (`v0.9β Build 7`) — 重大度「提案」改善（ポーリング最適化・孤立ファイル掃除・資産整理）** 【完了】
  - [x] `2.17-A` `DashboardHtml.cs` リビジョン検知ポーリング一本化・`SaveItemsDbToDisk` 条件付き保存・ロック保護＆未使用バッファ削除
  - [x] `2.17-B` `savePhotoFromUri` 失敗時null保証・`saveItem` 孤立ファイル掃除・デッドコード/旧スクリプト/未使用依存整理 ＋ `Build 7` 統一
- [x] **フェーズ 3.1 (`v0.9β Build 8`) — ハイブリッドカメラ（CameraX ＋ 標準カメラ）＆ 拡大プレビュー原本取得ボタン上部移設** 【完了】
  - [x] `ItemEditScreen.kt` 撮影ボタン横並び 2 分割（`52.dp` 維持：左側「アプリ内カメラ」＋右側「標準カメラ」＋ `CAMERA` 権限リクエスト／拒否時案内）
  - [x] `CameraXCaptureDialog.kt` 新設（連続撮影・明るさEVスライダー・ズーム・タップフォーカス・フラッシュ/トーチ・3×3/1対1出品枠ガイド・`Camera2Interop` 対応範囲簡易マニュアル調整）
  - [x] `PhotoPreviewDialog.kt` 「PCから高画質原本を取得」ボタンの上部ヘッダーエリア移設 ＋ `Build 8` 統一
- [x] **フェーズ 3.2 (`v0.9β Build 9`) — UI改善・テンプレート拡張・一括操作・出品前チェック** 【完了】
  - [x] `3.2-A` CameraX全画面オーバーレイ化によるナビバー被り根絶・プレビュー1本指スワイプ透過修復・出品前チェックアラート
  - [x] `3.2-B` テンプレート選択ドロップダウン化＆フルウィンドウ編集・`{title}`/`{price}`変数展開・PCワンクリックコピー強化
  - [x] `3.2-C` 選択モード拡張・ゴミ箱一括復元/完全削除・一括ゴミ箱移動(`batch-trash`)・部分一括編集(`batch-update`, 0円クランプ) ＋ `Build 9` 統一

### フェーズ 4 統合マイルストーン (詳細仕様は `PLAN_SYNC_STATUS_AND_MEMORY.md` を参照)
- [x] **フェーズ 4-1 (`v1.0.0 Build 4`) — 重大なバグ・エラーの緊急修復（最優先）** 【完了】
  - コミット `48a42ba` 混入不具合の修復（`WebDashboardHtml.kt` JS構文エラー解消、`FleaMarketRepository` 写真ID再採番回避、`LocalWebServer` 型変換安全化＆表記整合）
  - `PcServerClient.kt` 写真0枚同期漏れ修正、PC/スマホ両Web画面のドロップゾーン二重発火防止、`openCleanupModal` XSS修正、スマホWeb側フォルダ同期 (`WebPhotoDto.filePath`) 修復、`FurimaServerNative.cs` `CS0219` 警告解消
- [x] **フェーズ 4-2 (`v1.0.0 Build 5`) — PCブラウザ管理画面の写真ギャラリー改良・原寸送り戻し・写真単位ゴミ箱（案A）** 【完了】
  - `4-2a` PC母艦バックエンド（`SyncStore.cs` / `FurimaServerNative.cs`）：`_photo_trash` 退避＋`photo_trash.json` 管理、写真一括ゴミ箱移動/復元/完全削除/空にするAPI
  - `4-2b` PCブラウザUI（`DashboardHtml.cs`）：ギャラリー開時限定サムネイル右上チェックボックス、全画面原寸ライトボックス（`<`/`>`、キーボード `←`/`→`/`Esc`、下部ミニサムネ帯）、写真ゴミ箱モーダル
- [x] **フェーズ 4-3 (`v1.0.0 Build 6`) — 同期ステータス可視化 ＆ メモリ消費・アプリ安定性の抜本改善** 【完了】
  - `4-3a` 一覧画面ヘッダースリム化 ＆ リアルタイム同期ステータス・進捗％バッジ＆詳細ダイアログ
  - `4-3b` メモリ安定化 5本柱（保存IO完全移行＆ロック、16KBストリーミング送信、Coil上限設定、CameraX連写最適化＆`2048px/フル`切替記憶、サムネ`RGB_565`＋一時キャッシュ自動掃除＋`largeHeap`）
- [x] **フェーズ 4-4 (`v1.0.0 Build 7`) — 低負荷フォルダ自動取込（AI一括書き戻し対応）＆ 画像ファイル名比較表示** 【完了】
  - `4-4-1` `FileSystemWatcher` ＋ デバウンスバッチ取込 ＋ ロック外MD5計算 ＋ `photoStatCache`（終了時ハンドル完全解放）
  - `4-4-2` `POST /api/items/{id}/photos/scan` ＋ ギャラリー「フォルダ取込」ボタン ＋ サムネ/ライトボックスの `fileName` 表示 ＆ 表示中ライブ更新

### フェーズ 5 計画マイルストーン (詳細仕様は `PLAN_CLAUDE_SEND_PHASE5.md` を参照・2026-11-16 策定)
- [x] **フェーズ 5-1 (`v1.0.0 Build 8`) — プロンプトテンプレートの共有基盤（設定同期への相乗り）** 【完了】
  - `5-1-1` サーバー設定モデル・パーサー拡張（`SyncStore.cs` の `promptTemplates` 追加＋null正規化、`FurimaServerNative.cs` の `ParseDefaultSettingsConfig()` 手書き抽出追加）
  - `5-1-2` Android設定モデル・DTO・変換拡張（`DefaultSettings` / `SyncSettingsDto` / `PcServerClient` 相互変換 / `DefaultSettingsManager` の `prompt_templates_json` 永続化）
  - `5-1-3` PC管理画面に `prompt` カテゴリ追加（設定3箇所マッピング＋「画像加工プロンプト用テンプレート」セクション＋編集モーダル3分岐化）
  - `5-1-4` スマホ設定UIにプロンプトテンプレート編集追加（`TemplateCategory.PROMPT`＋`persistSettingsNow()` 引数追加）
- [x] **フェーズ 5-2 (`v1.0.0 Build 9`) — ジョブキュー基盤（サーバー保存＋作成API＋PCギャラリー送信UI）** 【完了】
  - `5-2-1` ジョブストア新設（`tools/ClaudeJobStore.cs`：`claude_jobs.json` 永続化・終端ジョブ30日自動整理、`build-exe.bat` のコンパイル対象へ追加）
  - `5-2-2` `claudeRevision` 新設（ステータス／ハートビート両応答へ追加、ジョブ作成・取消で更新。`dataRevision` は不変）
  - `5-2-3` ジョブ作成／一覧／詳細／取消API（商品特定は `itemUuid` 優先、MD5実在検証、テンプレートのスナップショット保存、まとめ親商品は拒否、応答に `jobToken` を含めない）
  - `5-2-4` PCギャラリー送信UI（テンプレート選択＋「Museに送る」＋送信直前照合＆ファイル名確認ダイアログ、まとめ親商品の無効化、ジョブ一覧モーダル＋`claudeRevision` ポーリング連動）
- [x] **フェーズ 5-3 (`v1.0.0 Build 10`) — スマホ側サムネイル複数選択（一括削除＋Muse送信）** 【完了】
  - `5-3-1` 編集画面サムネイル選択モード（長押し開始・チェックボックス重ね表示・アクションバー。プレビュー単品削除は不変）
  - `5-3-2` 選択一括削除は編集バッファ方式（確認ダイアログ後に編集リストから除外し「保存する」で確定。即時DB削除は不使用、取り消しピルなし）
  - `5-3-3` スマホ発「Museに送る」（`PcServerClient.createClaudeJob` で `itemUuid` 必須送信、未保存・新規作成中・まとめ親商品はガード、対象はPC同期済み写真のみ。テンプレート選択ダイアログ `ClaudeSendDialog` 新規）
- [x] **フェーズ 5-4 (`v1.0.0 Build 11`) — スマホ承認フロー（同期リビジョン経由）** 【完了】
  - `5-4-1` 承認API（承認リクエスト／承認・拒否でジョブ単位トークン発行（30分）／詳細応答でのみトークン開示／写真受け渡しは配達用コピー生成つき、CORS許可ヘッダ追加、失効の遅延評価）
  - `5-4-2` ハートビートへの `claudeRevision` 相乗り（独立第3分岐。データ／設定Pullと `isDirty` 保護には不干渉、例外は握りつぶしてログのみ）
  - `5-4-3` 承認ダイアログ（`MainActivity` のアプリレベルオーバーレイ。フォアグラウンド時のみ届く仕様を明記）
- [x] **フェーズ 5-5 (`v1.0.0 Build 12`) — 結果返却・完了通知・確認（ack）共有** 【完了】
  - `5-5-1` 結果返却API（ジョブトークン必須、マジックバイト検証、保存名は元名＋`_ai`＋バイト判定拡張子・同名は連番、25MB上限、同一 `sourceMd5` への再送は冪等。登録はフェーズ 4-4 のフォルダ取込と同一処理を再利用し Watcher 競合を回避）
  - `5-5-2` 完了API（全写真に結果またはエラーがある場合のみ `done` / `done_with_errors` へ）／確認（ack）API（Bearer のみ、両端末から可）
  - `5-5-3` PC管理画面：未確認の完了ジョブをハイライト＋成功/失敗枚数＋「確認」ボタン
  - `5-5-4` スマホ：完了通知ダイアログ（商品名・成功/失敗枚数）＋「確認」で ack（承認ダイアログ表示中は後回し）
- [x] **Build 13 — §11/§12 レビュー修正（13-1〜13-6）** 【完了】
  - `13-1` `DetectImageMimeType` 厳格化（JPEG マジックバイト追加・不一致時は空文字）＋写真配信2箇所のフォールバック
  - `13-2` `ClaudeJobStore` ディープコピー返却＋結果登録の直列化（§13 ガードA: ロック内再取得／ガードB: ロック順序の最外層固定）
  - `13-3` エラー記録の網羅（写真取得 404 でも記録＋エラー報告API `POST /api/claude/jobs/{jobId}/errors` 新設。§13 ガードC: results/errors 相互排他）
  - `13-4` 承認・完了ダイアログの失敗時UX（処理中フラグ・失敗時メッセージ・応答後の状態再整合・「いったん閉じる」導線）
  - `13-5` 編集画面の対象判定ライブ追従（同期成功時に写真エンティティのみ再読込）
- [x] **フェーズ 5-6 (Build更新なし・運用) — Claude側処理手順の固定と試運転** 【完了】
  - `5-6-1` 接続経路は trycloudflare トンネルが Cloudflare に遮断されたためハイブリッド Tailscale に確定（スマホ↔PC は従来トンネル、Muse↔PC は Tailscale 固定アドレス）。サーバーの localhost 縮退は URL 予約の管理者再登録で解消
  - `5-6-2` 試運転2件をエンドツーエンド処理（ジョブ1: 3枚中2枚成功＋1枚は Muse 側の拡張子ミスで失敗し `POST /errors` に記録、`done_with_errors` で完了・ack共有確認。ジョブ2: 1枚成功、`done`）。結果は同一商品フォルダへ `_ai` 名で返却・即時登録を確認

### フェーズ 6 計画マイルストーン (詳細仕様は `PLAN_CLAUDE_SEND_PHASE6.md` を参照・2026-11-16 策定／§9 で改訂)
- [ ] **フェーズ 6-1a (`v1.0.0 Build 14`) — ジョブ基盤の拡張（`queue` 新設・送信時承認・失効時の待機戻し（途中処理済みは終端）・`claudeRevision` 永続化＋JSON形式移行・送信3択UI・待機の最小限移動ボタン）** 【計画】
- [ ] **フェーズ 6-1b (`v1.0.0 Build 15`) — ジョブトークンの延長方式（サーバー単独）** 【計画】
- [ ] **フェーズ 6-2 (`v1.0.0 Build 16`) — 一括承認（チェックボックス式・最大10件・サムネイル付き）** 【計画】
- [ ] **フェーズ 6-3 (`v1.0.0 Build 17`) — 合図窓口と見張り（版番号窓口＋Muse側巡回 10:00〜22:00）** 【計画】
- [ ] **フェーズ 6-4 (`v1.0.0 Build 18`) — 出品ステータス拡張（下書き中／下書き済み分割＋発送準備中＋売却系ステータス導入）** 【計画】
- [ ] **フェーズ 6-5 (`v1.0.0 Build 19`) — 加工ラベル（サムネイル属性ラベル＋寿命管理）** 【計画】
- [ ] **フェーズ 6-6 (`v1.0.0 Build 20`) — ジョブ一覧の改善（依頼中／待機中切替・出品カード連携）** 【計画】
- [ ] **フェーズ 6-7 (`v1.0.0 Build 21`) — 右カラム再編とミューズカラム新設** 【計画】

---

## 更新履歴 (Changelog)

### [2026-11-16] フェーズ 6 大枠の最終合意 (`PLAN_CLAUDE_SEND_PHASE6.md` §10 / `REVIEW_CLAUDE_SEND_PHASE6.md` §8)
- **レビュー §7 の追加指摘5件＋軽微な確認2件にすべて同意し、大枠は完全合意**（ドキュメントのみ・コード変更なし）。根拠は実コードで再照合済み。
  - 7-1: 待機を戻すUIの空白期間（Build 14〜19）を防ぐため、6-1a に PC ジョブ一覧の最小限の移動ボタン（巡回へ／手動へ）を同梱。
  - 7-2: `done_with_errors` の警告解消を通知 ack から分離し、新フラグ `errorDismissed`（PC待機リストでの消去／再送作成時に立つ）で担う。加工ラベルのエラー警告の解除役も差し替え。
  - 7-3: 途中処理済みジョブのトークン失効は待機へ戻さず、未処理写真へ失効エラーを記録して終端（全件結果ありなら `done`）。読み取り時にも遅延評価で遷移を materialize させる。
  - 7-4: `claude_jobs.json` の配列→オブジェクト移行に後方互換パーサーを必須化。移行時の `claudeRevision` 初期値は Unix 秒。
  - 7-5: 承認オーバーレイの45秒固定遅延を取り下げ、編集画面滞在中のみの画面連動抑止＋非ブロッキングバー＋一覧復帰時に一括表示へ変更（PC発の保留があれば即時表示）。
- **次のアクション**: 6-1a から実装順にサブフェーズ詳細化を行い、計画書へ追記してレビューを受ける。各フェーズの着手は詳細化レビュー完了を前提とする。

### [2026-11-16] フェーズ 6 大枠レビューへの回答を追記 (`PLAN_CLAUDE_SEND_PHASE6.md` §9 / `REVIEW_CLAUDE_SEND_PHASE6.md` §6)
- **レビューの全指摘を実コードで再照合し、致命的2件を含む全指摘の方向性に同意**（ドキュメントのみ・コード変更なし）。
  - 致命的-1: `claudeRevision` は静的フィールドのみでサーバー再起動時に 1 へリセットされる（見張りがサイレント停止する）。ジョブストアへの永続化を **6-1a へ前倒し**して必須化。
  - 致命的-2: `SHIPPING` を素朴に追加すると売価・売却日がクリアされる（スマホ側リポジトリの3経路で確認）。**「売却系ステータス」{`SOLD`, `SHIPPING`}** を新設し保持・計算・クリア条件を統一。売上分析・CSV は `SHIPPING` を売上に含め、`soldAt` は初回遷移で確定。
  - その他確定事項: 所属リストは新フィールド `queue`（既存データの既定は `manual`）、失効遷移は一律 `queued` + `waiting` + トークン消去、トークン延長は実行系API限定・失効チェック後・リビジョン更新なし、一括承認でチェックを外したジョブは待機リストへ戻す（再ポップアップ防止）、サムネイルは `md5` 基準、既読マーカーはジョブストア側保持、ラベル表示優先度（エラー > 加工中 > 承認待ち > 待機中 > 加工済み）、ヘッダー折り返しは 6-4 で先行実施。
  - 実装担当からの追加提案: スマホ発送信直後45秒の承認オーバーレイ表示遅延（自己承認の割り込み対策）、巡回の開始不能・全件失敗時のみチャット警告（完了報告なしは維持）。
- **フェーズ構成を改訂**: 6-1 を 6-1a（Build 14）／6-1b（Build 15）に分割し、以降を Build 16〜21 に再編（マイルストーン側も更新）。
- **次のアクション**: レビュー側の確認をもって大枠合意とし、各フェーズのサブフェーズ詳細化へ進む。

### [2026-11-16] フェーズ 6 計画書を策定（大雑把なフェーズ策定まで）＆ フェーズ 5-6 試運転完了を記録
- **フェーズ 5-6 試運転完了**: ジョブ2件をエンドツーエンドで処理し、承認→配達用コピー→生成→結果返却→完了→ack共有まで実動作を確認（詳細はフェーズ 5 マイルストーン側に記録）。接続経路はハイブリッド Tailscale に確定。
- **フェーズ 6 計画書 (`PLAN_CLAUDE_SEND_PHASE6.md`) を新規策定**（ドキュメントのみ・コード変更なし）。ユーザー協議済み仕様（送信時承認リクエスト／送り先3分類（巡回・手動・待機）／チェックボックス式一括承認（最大10件）／トークン延長方式／期限切れの待機戻し＋手動再リクエスト／見張り 10:00〜22:00・チャット完了報告なし／版番号だけの合図窓口／加工ラベル／ジョブ一覧改善／右カラム再編＋ミューズカラム／出品ステータス拡張）を、大雑把なフェーズ 6-1〜6-7（Build 14〜20）に分割し、依存関係の調査結果を明記。
- **注意事項を計画書 §0 に明記**: ローカルWebUI の既知の問題は把握した上で意図的に無視する（ユーザー許可済み・別タスク切り分け済み）。本フェーズのレビューで再指摘は不要。
- **次のアクション**: 担当エージェントが計画書の大枠（フェーズ分割・順序・依存関係）をレビューし、合意後に各フェーズをサブフェーズへ詳細化する。

### [2026-11-16] Build 13 レビュー修正完了 バージョン 1.0.0 (Build 13)
- **§12.3 の修正チェックリスト 13-1〜13-6 を §13 のガード条件（A〜D）込みで実装**（計画書 §12.3 のチェックを完了に更新）。
  - サーバー: 画像形式判定の厳格化と配信側フォールバック、`ClaudeJobStore` のディープコピー返却、結果・エラー登録の単一ロック直列化（ロック内再取得・results/errors 相互排他）、写真取得 404 でのエラー記録、エラー報告APIの新設。
  - スマホ: 承認・完了ダイアログへ処理中表示・失敗時メッセージ・「いったん閉じる」導線を追加し、応答後は状態を再整合（二度押し時の誤表示防止）。編集画面は同期成功時に写真エンティティだけを再読込。
  - **バージョン統一更新**: `app/build.gradle.kts` (`versionCode = 13`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 13)`** へ3箇所同時統一。
- **次のアクション（§13.3 の順序）**: Windows で `tools/build-exe.bat` を実行（サーバー側 C# の初回コンパイル確認を兼ねる。エラーが出た場合は修正コミットを行う）→ Android APK 再ビルド・配置 → フェーズ 5-6 試運転。


### [2026-11-16] §12 回答へのレビュー側確認・実装時ガード追記 ＆ 条件付き同意 (`PLAN_CLAUDE_SEND_PHASE5.md` §13 / `REVIEW_CLAUDE_SEND_PHASE5.md` §7)
- **実コード再照合 ＆ 条件付き同意**: `DetectImageMimeType` 呼び出し 5 箇所、写真取得 404 による `complete` デッドロック、`FleaMarketViewModel.lastSyncSuccessAt`（845行目）、`claudeRevision` ハートビート挙動を実コードで確認し、§12（Build 13 計画 `13-1`〜`13-6`）へ同意する旨を計画書 §13・レビュー文書 §7 へ追記（ドキュメントのみ）。
- **同意条件（実装担当エージェントへの必須確認事項）**:
  1. `13-2`: 新設 `static` ロック内での `ClaudeJobStore.GetJob(jobId)` 再取得による TOCTOU 完全防止、最外層ロック順（`resultsLock → lockObj → _jobLock`）厳守、子リスト要素を含む完全ディープコピー。
  2. `13-3`: `POST /errors` の冪等・権限/期限/対象検証と、`results` / `errors` 間の同一 `sourceMd5` 相互排他。
  3. `13-4`: 処理中連打抑止＋失敗後 `refreshClaudeJobStates()` による二度押し救済。
- **次のアクション**: 実装担当エージェントが `PLAN_CLAUDE_SEND_PHASE5.md` §13 の全項目を確認した上で Build 13（`13-1`〜`13-6`）へ着手。

### [2026-11-16] §11 レビューへの実装担当エージェント回答を追記 (`PLAN_CLAUDE_SEND_PHASE5.md` §12 / `REVIEW_CLAUDE_SEND_PHASE5.md` §6)
- **§11 の全指摘を実コードで再照合し、項目別の判定と Build 13 修正チェックリストを計画書 §12 へ、要約をレビュー文書 §6 へ追記**（ドキュメントのみ・コード変更なし）。
  - 同意: 高-1（画像形式判定の厳格化。ただし呼び出し元は3箇所ではなく5箇所のため、既存の写真配信2箇所へのフォールバックをセットで実施）、高-2（成果物ビルド。サーバー側 C# が未コンパイルである前提を明記）、中-1（ストアのディープコピー返却＋結果登録の直列化）、中-2（失敗時UX。対象を承認ダイアログにも拡張）、軽微-1（同期成功時の対象判定再読込）。
  - 追加指摘（新規・高）: 写真取得が 404 の場合にエラーが記録されず、完了処理が永久に失敗するデッドロックを検出。エラー報告APIの新設と合わせて Build 13 で対応する。
  - ローカル管理画面（緊急Web）は要修正として確定し、5-6 試運転後の独立タスクとする（ユーザー判断）。軽微-2（経過時間表示）は現状維持。
- **次のアクション**: レビュー側の同意が得られ次第、Build 13（13-1〜13-6）の実装を開始する。


### [2026-11-16] フェーズ 4〜5-5 実装完了後コードレビュー・ビルド検証 ＆ 協議事項追記 (`PLAN_CLAUDE_SEND_PHASE5.md` §11)
- **ビルド・自動テスト実測（`e1a42b1` / `v1.0.0 Build 12`）**: `compile_applet` 合格、`node --check`（`DashboardHtml.cs` / `WebDashboardHtml.kt` 双方）合格、`gradle :app:testDebugUnitTest` 全6クラス・14テスト合格（失敗0・スキップ0）を確認。
- **計画書への調査結果＆協議事項追記**:
  - `PLAN_CLAUDE_SEND_PHASE5.md` §11（および `REVIEW_CLAUDE_SEND_PHASE5.md` §5、`PLAN_SYNC_STATUS_AND_MEMORY.md` §5）へ、検出された課題（高：`DetectImageMimeType` フォールバックバグによる 415/HEIF 判定漏れ、`.exe`・`.apk` バイナリ未更新／中：`ClaudeJobStore` スレッド安全＆`POST /results` 冪等性競合、承認・完了ダイアログ失敗時UX・閉塞、緊急Webサーバー認証・差分検知／軽微：`ItemEditScreen` `loadedPhotosByPath` 滞留・経過時間表示）とフェーズ 5-1〜5-5 担当エージェントとの協議事項 5 点を記録。

### 2026/11/16 フェーズ 5-5 完了 バージョン 1.0.0 (Build 12)
- **結果返却・完了通知・確認（ack）共有**：
  - 結果返却 `POST /api/claude/jobs/{jobId}/results`（ジョブトークン必須）：ヘッダ `X-Source-Md5` / `X-File-Name` を受け、ボディの画像バイトをマジックバイトで検証（画像でなければ 415）。受付上限 25MB。保存名は Q4 の決定通り「ジョブ記録の元ファイル名 → サニタイズ済み `X-File-Name` → フォルダ内実ファイル → フォールバック」の優先順位でベース名を決め、`_ai` ＋バイト判定の拡張子（`DetectImageExtension`）を付ける（元ファイル名の拡張子は使わない）。同名があれば `_ai2`, `_ai3`…と連番。同一ジョブ・同一 `sourceMd5` への再送は既存レコードを成功として返す冪等仕様（リトライで `_ai2` が増殖しない）。
  - 登録はフェーズ 4-4 のフォルダ取込スキャンと同一の登録処理 (`CommitAutoImportedPhotosBatch`) を再利用：検証とMD5計算はロック外で済ませ、書き込み直後に `photoStatCache` / `hashToFilePathMap` / `item["photos"]` をロック下で即時更新するため、`FileSystemWatcher` との競合・二重登録を抑制する。商品は `itemUuid` 優先で再解決し、`syncedToPhone = false` ＋ `BumpDataRevision()` でスマホ・PC双方へ伝播する。`POST /api/items/{id}/photos/add` は使わない（自動名のため）。
  - 完了 `POST /api/claude/jobs/{jobId}/complete`（ジョブトークン必須）：全写真に結果またはエラーの記録がある場合のみ、全成功なら `done`、一部失敗なら `done_with_errors` へ遷移し `completedAt` を記録、トークンを破棄する。
  - 確認 `POST /api/claude/jobs/{jobId}/ack`（Bearer認証のみ、PC／スマホどちらからでも可）：`acknowledged = true` と `acknowledgedAt` を記録し `BumpClaudeRevision()`。片方で確認すれば両端末の未確認表示が同時に消える（確定事項3）。
  - PC管理画面 (`tools/DashboardHtml.cs`)：ジョブ一覧モーダルで未確認の完了ジョブをハイライト（琥珀色）し、成功/失敗枚数と「確認」ボタンを表示。返却写真は既存の `dataRevision` 追従でギャラリーへ自動反映（追加実装なし）。
  - スマホ：完了通知ダイアログ (`ui/components/ClaudeCompletionDialog.kt` 新規) を `MainActivity` のアプリレベルオーバーレイへ追加。5-4 の `unackedClaudeCompletions` を使い、商品名・成功/失敗枚数・テンプレート名を表示し「確認」で ack を送る。承認ダイアログ表示中は完了ダイアログを後回しにする。写真そのものは既存データPullで自動的に届く（追加実装なし）。既知の反映遅延（未同期ローカル写真がある商品では `hasUnpushedLocalPhoto` ガードにより次回自動同期まで遅れることがある）は仕様メモとして計画書に記載済み。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 12`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 12)`** へ3箇所同時統一（※PC側バイナリ反映には Windows で `tools/build-exe.bat` 実行が必要。Android のビルド・実機確認はユーザー環境で実施。端末間の一気通貫の検証は 5-6 の試運転で行う）。
- 既存機能の削除・置き換え：なし。

### 2026/11/16 フェーズ 5-4 完了 バージョン 1.0.0 (Build 11)
- **スマホ承認フロー（ハートビート相乗り・ジョブ単位トークン）**：
  - サーバー (`tools/FurimaServerNative.cs`)：`POST /api/claude/jobs/{jobId}/request-approval`（`queued` / `approval_expired` から受付、`approval_pending` へ遷移）、`POST /api/claude/jobs/{jobId}/approval`（承認時はジョブ単位トークンを発行し有効期限30分で保存、拒否時は終端 `denied`）。`GET /api/claude/jobs/{jobId}` は `approved` / `processing` の場合のみ応答にトークンと有効期限を含める（他の応答には含めない）。状態を変える全APIで `BumpClaudeRevision()`。
  - 失効の遅延評価 (Q7)：`approval_pending` は10分で `queued` へ自動復帰。トークン期限切れは写真取得・詳細取得など全トークン対象APIで判定し `approval_expired` へ遷移（同じジョブへ再び承認リクエスト可能、作り直し不要）。
  - 写真受け渡し `GET /api/claude/jobs/{jobId}/photos/{md5}`：ヘッダ `X-Claude-Job-Token` が当該ジョブの有効なトークンと一致し、状態が `approved` / `processing` の場合のみ返却。初回取得で `processing` へ遷移。CORS許可ヘッダへ `X-Claude-Job-Token` / `X-Source-Md5` / `X-File-Name` を追加（レビュー指摘8-1）。トークンは権限分離ではなく「スマホ承認が完了しない限り実行APIが通らないセーフティロック」として定義（レビュー指摘8-2）。
  - 配達用コピー（確定事項9）：原本は返さず、JPEG/PNG・長辺2048px以下・5MB以下・EXIF Orientation 正立（1または未設定）の場合のみ無劣化パススルー。それ以外はサーバー側で新規実装した `System.Drawing` 処理で正立化＋長辺2048pxへ縮小（高品質バイキュービック）し Orientation を除去して JPEG 品質90で再エンコード（メモリ上で完結、原本は不変更）。HEIF等の未対応形式は写真単位のエラーとしてジョブへ記録し、他の写真は継続可能。
  - Android：`HeartbeatInfo` へ `claudeRevision` を追加（旧サーバーでも 0 で安全）。ハートビートループへ**独立第3分岐**を新設し、`claudeRevision` の変化でのみジョブ一覧を取得して承認待ち（最古の1件）と未確認の完了ジョブの `StateFlow` を更新する。既存のデータPull／設定Pull分岐と `isDirty` 保護には一切触れておらず、この分岐の例外は握りつぶしてログのみ（同期本体を止めない）。
  - 承認ダイアログ (`ui/components/ClaudeApprovalDialog.kt` 新規)：商品名・写真枚数・テンプレート名・返却先フォルダ・送信元・経過時間を表示し「実行を承認」「拒否」。表示場所は `MainActivity` の `FleaMarketApp()`（NavHost の外側）で、どの画面にいても承認できる (Q3)。承認はフリマ管理アプリがフォアグラウンドの間にだけ届き、閉じていた場合は次に開いた直後のハートビートで表示される。Muse アプリ側に承認UIは作らない（確定事項2）。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 11`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 11)`** へ3箇所同時統一（※PC側バイナリ反映には Windows で `tools/build-exe.bat` 実行が必要。Android のビルド・実機確認はユーザー環境で実施）。
- 既存機能の削除・置き換え：なし。

### 2026/11/16 フェーズ 5-3 完了 バージョン 1.0.0 (Build 10)
- **スマホ側サムネイル複数選択（一括削除＋Muse送信）**：
  - 編集画面 (`ItemEditScreen.kt`)：写真サムネイル（横並びリスト）へ選択モードを追加。長押しで開始し（商品一覧の選択モードと操作感を統一）、選択中はチェックボックス重ね表示＋上部アクションバー（選択件数／削除／Museに送る／解除）を表示。既存のプレビュー単品削除と取り消しピルは変更なし。
  - 一括削除は編集バッファ方式に統一（Q2）：確認ダイアログの後に編集リスト（既存パス／新規URI）から選択分を除外し、「保存する」で確定する。即時DB削除 (`deletePhotosByIds`) は使わない。一括削除に取り消しピルは付けず、実行前確認のみとする。
  - スマホ発「Museに送る」：`PcServerClient.createClaudeJob()`（`POST /api/claude/jobs`、`origin: "phone"`、商品特定のため `itemUuid` を必須送信）を追加し、`SyncDtos.kt` へジョブ作成用DTO、`FleaMarketViewModel` へ送信ラッパー（成功／失敗コールバック）を追加。送信前ガード：新規作成中 (`itemId == 0`)・未保存の変更あり・まとめ親商品 (`mergedChildCount > 0`) は送信不可として理由を案内。対象写真は `md5Hash` 確定かつ `isPcSynced` のものだけとし、対象外は件数と理由をダイアログに表示する。
  - テンプレート選択ダイアログ (`ui/components/ClaudeSendDialog.kt` 新規)：共有済み `promptTemplates` をプルダウン表示。PC未接続時は送信不可として理由を表示。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 10`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 10)`** へ3箇所同時統一（※Android のビルド・実機確認はユーザー環境で実施。`WebDashboardHtml.kt` はバージョン表記のみで、本機能の移植は対象外）。
- 既存機能の削除・置き換え：なし。

### 2026/11/16 フェーズ 5-2 完了 バージョン 1.0.0 (Build 9)
- **ジョブキュー基盤（「Museに送る」の受け皿）**：
  - ジョブストア (`tools/ClaudeJobStore.cs` 新規)：写真ゴミ箱と同じ流儀で `syncDir/claude_jobs.json` へ永続化（サーバー再起動後も復元）。確認済み完了・取消・拒否の終端ジョブは30日経過後に自動整理。`tools/build-exe.bat` のコンパイル対象へ追加。
  - `claudeRevision` (`tools/FurimaServerNative.cs`)：`dataRevision` / `settingsRevision` と並ぶ第3のリビジョンを新設し、`/api/server/status` と `/api/server/heartbeat` の両応答へ追加（後方互換）。ジョブの作成・取消でのみ更新し、商品データには触れない。
  - ジョブAPI：`POST /api/claude/jobs`（商品特定は `itemUuid`＝`syncUuid` 優先解決、写真MD5の実在検証、プロンプトテンプレートの内容をジョブへスナップショット保存、まとめ親商品・ゴミ箱内商品は400で拒否）、`GET /api/claude/jobs`（`status` 絞り込み可）、`GET /api/claude/jobs/{jobId}`、`POST /api/claude/jobs/{jobId}/cancel`（実行前のみ取消可）。応答には `jobToken` を一切含めない（発行は 5-4）。
  - PCギャラリー (`tools/DashboardHtml.cs`)：ツールバーへプロンプトテンプレート選択＋「Museに送る」ボタン＋「Museジョブ」ボタンを追加。送信直前に選択写真を現在の写真一覧と照合し、対象ファイル名の一覧で確認ダイアログを出す（Q6）。まとめ親商品を開いている間は送信を無効化し理由を表示（Q5）。チェックボックスのツールチップ文言を汎用化。ジョブ一覧モーダル（状態バッジ・商品名・枚数・テンプレート名・作成日時・取消ボタン）を新設し、`claudeRevision` の変化で自動再取得する。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 9`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 9)`** へ3箇所同時統一（※PC側バイナリ反映には Windows で `tools/build-exe.bat` 実行が必要。Android のビルド・実機確認はユーザー環境で実施）。
- 既存機能の削除・置き換え：なし。

### 2026/11/16 フェーズ 5-1 完了 バージョン 1.0.0 (Build 8)
- **プロンプトテンプレートの共有基盤（設定同期への相乗り・新規APIなし）**：
  - サーバー (`tools/SyncStore.cs`, `tools/FurimaServerNative.cs`)：`DefaultSettingsConfig` に `promptTemplates`（`TextTemplateConfig` のリスト）を追加し、`CreateDefault()` 初期化と `GetDefaultSettings()` / `SaveDefaultSettings()` の null→空リスト正規化を実施。あわせて `ParseDefaultSettingsConfig()` の手書き抽出へ `promptTemplates` を追加し、`PUT /api/settings`・`POST /api/sync/items` 経由で空に初期化される不具合を未然に防止（レビュー指摘4-1）。
  - Android (`DefaultSettings.kt`, `SyncDtos.kt`, `PcServerClient.kt`, `DefaultSettingsManager.kt`, `SettingsStorageScreen.kt`)：設定モデル・同期DTO・相互変換へ `promptTemplates` を追加し、`SharedPreferences` の手書きキー `prompt_templates_json` で永続化（レビュー指摘4-2）。設定画面には既存テンプレート編集ダイアログを流用した「画像加工プロンプト用テンプレート」セクションを追加し、`persistSettingsNow()` への引数追加で他設定保存時の消失を防止（レビュー指摘4-3）。
  - PC管理画面 (`tools/DashboardHtml.cs`)：`pcSettings` 初期値・`fetchSettings()`・`savePcSettingsPayload()` の3箇所マッピングへ `promptTemplates` を追加し（レビュー指摘4-4）、テンプレート編集モーダルのカテゴリ分岐（`title` / `desc`）へ `prompt` を追加、設定画面に「画像加工プロンプト用テンプレート」一覧セクションを新設。
  - 初期テンプレート（窓辺・海／窓辺・森／夕方の部屋）はコードへ埋め込まず、ユーザーが設定画面から登録する運用（計画書 5-1-3）。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 8`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 8)`** へ3箇所同時統一（※PC側バイナリ反映には Windows で `tools/build-exe.bat` 実行が必要。Android のビルド・実機確認はユーザー環境で実施）。
- 既存機能の削除・置き換え：なし（後方互換な追加のみ。旧バージョン相手との設定同期でも未知フィールド無視・欠落時空リストで安全）。

### 2026/11/15 フェーズ 4-4 完了 バージョン 1.0.0 (Build 7)
- **低負荷フォルダ監視による画像自動取込 (`tools/FurimaServerNative.cs`)**：
  - `FileSystemWatcher`（`syncDir` 配下監視、`InternalBufferSize = 65536`）と 400ms デバウンスバッチ処理を導入し、AI加工ツール等が `{itemId}_{title}` フォルダへ別名で書き戻した画像ファイルを出品数に依存せず低負荷で自動登録（`.tmp`・`_photo_trash`・ゴミ箱内商品は除外、終了時は `StopFolderAutoImportWatcher()` で完全解放）。
  - ファイルロック中の読み取りリトライ・マジックバイト検証（JPEG/PNG/WebP/GIF/BMP）・`lock (lockObj)` 外での MD5 計算・バッチ単位での1回保存＆`BumpDataRevision()`・`photoStatCache`（サイズ＋更新日時キャッシュ）を実装。
- **単体フォルダ取込 API ＆ PC管理画面の見比べ強化 (`tools/FurimaServerNative.cs`, `tools/DashboardHtml.cs`)**：
  - `POST /api/items/{id}/photos/scan` を新設し、ギャラリー上部の「フォルダ取込」ボタンから特定商品の1フォルダのみ即座に再スキャン取込できる機能を追加。
  - PC側 `/api/items` の `photos` に実ファイル名 `fileName` を付与し、ギャラリー内サムネイルカードおよび原寸ライトボックスにファイル名を表示。さらにバックグラウンド更新時（`fetchItemsOnly`）に開いているギャラリー／ライトボックスもライブ追従するよう改善。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 7`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 7)`** へ3箇所同時統一（※PC側バイナリ反映には Windows で `tools/build-exe.bat` 実行が必要）。

### 2026/11/15 フェーズ 4-3 完了 バージョン 1.0.0 (Build 6)
- **一覧画面ヘッダースリム化 ＆ リアルタイム同期ステータス／進捗％可視化 (`PcServerClient.kt`, `FleaMarketViewModel.kt`, `ItemListScreen.kt`)**：
  - `SyncPhase` / `PcSyncProgress` を新設し、`syncDeltaToPcServer`・`triggerAutoSync`・接続ハンドシェイクからバイト単位の送信進捗％と写真枚数（`n/m`）・エラー詳細をリアルタイム通知（従来のサイレント `catch` を廃止）。
  - `ItemListScreen.kt` 内側 `Scaffold` に `WindowInsets(0)` を適用して二重余白を排除し、ヘッダー縦幅を大幅スリム化。タイトル横に同期ステータスバッジ（`testTag("header_sync_status_badge")`）を配置し、タップで接続状態・最終同期時刻・進捗バー・エラー詳細・「今すぐ再同期」ダイアログを表示。
- **メモリ安定化 ＆ OOM・ANR防止（5本柱 / `MainActivity.kt`, `FleaMarketRepository.kt`, `PhotoStorageManager.kt`, `PcServerClient.kt`, `CameraXCaptureDialog.kt`, `PhotoPreviewDialog.kt`, `ItemEditScreen.kt`, `ItemDetailScreen.kt`, `ItemListScreen.kt`, `AndroidManifest.xml`)**：
  - `FleaMarketRepository.saveItem` を `withContext(Dispatchers.IO)` で完全バックグラウンド化し、`ItemEditScreen` に `isSaving` 保存ロック＋戻るガード＋スピナー表示を追加。
  - `PcServerClient.syncDeltaToPcServer` の `file.readBytes()` を廃止して 16KB チャンク読み出しの `StreamingFileRequestBody` へ移行し、`downloadPhotoByHash` / サムネイル自己修復も一時ファイルストリーミング化。
  - `MainActivity.kt` で Coil 専用 `ImageLoader`（メモリ15%上限 / ディスク64MB上限 / `RGB_565`）を初期化し、一覧・詳細・編集・カメラサムネ・全画面プレビュー（`2048px`）へ `ImageRequest` デコード解像度上限を適用（原本ファイル無変更）。
  - `CameraXCaptureDialog.kt` を `CAPTURE_MODE_MINIMIZE_LATENCY` 化＋上部保存解像度切替（`標準 2048px` / `フル解像度`、設定永続化）＋撮影直後 `Dispatchers.IO` 長辺2048pxリサイズ（`PhotoStorageManager.downscaleCapturedPhotoIfNeeded`）を実装。
  - サムネイル生成を `calculatePowerOfTwoSampleSize` + `Bitmap.Config.RGB_565` + `finally` `recycle()` へ統一し、起動時一時キャッシュ自動掃除 (`cleanupTemporaryCaches`) と `android:largeHeap="true"` を追加。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 6`, `targetSdk = 35` 維持)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 6)`** へ3箇所統一し、APK再生成・JVMユニットテスト通過を確認。

### 2026/11/15 フェーズ 4-2 完了 バージョン 1.0.0 (Build 5)
- **PC母艦バックエンド 写真単位ゴミ箱ストア＆API（案A / `tools/SyncStore.cs`, `tools/FurimaServerNative.cs`）**：
  - `_syncDir/_photo_trash` および `photo_trash.json` を新設し、`ScanAndIndexLocalPhotos` で `_photo_trash` をインデックス除外。
  - `POST /api/items/{id}/photos/delete` / `POST /api/photos/trash-batch` で商品（まとめ親・子双方の参照対応）から写真参照を外し、他アクティブ商品未参照の実ファイルのみ `_photo_trash` へ退避＋`syncedToPhone = false` + `BumpDataRevision()` を実行。
  - `GET /api/photo-trash`、`GET /api/photo-trash/{trashId}/image`、`POST /api/photo-trash/restore`、`POST /api/photo-trash/delete`、`POST /api/photo-trash/empty` を実装。
- **PCブラウザ管理画面 写真ギャラリー改良・原寸ライトボックス・写真ゴミ箱UI（`tools/DashboardHtml.cs`）**：
  - `#photo-viewer-modal` 内サムネイルのみ右上にチェックボックスを表示し、「全選択/解除」「選択した写真をゴミ箱へ移動」バーを追加（まとめ親商品では本来の所有子商品IDを保持）。
  - 全画面原寸ライトボックス（`<` / `>` ボタン、キーボード `←` / `→` / `Esc`、`1 / N` 表示、下部ミニサムネ帯、画像コピー、別タブ、ゴミ箱移動）および「写真ゴミ箱」モーダル（復元・完全削除・空にする）を追加。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 5`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 5)`** へ3箇所統一（※PCサーバー反映には Windows 側で `tools/build-exe.bat` の再ビルドが必要）。

### 2026/11/15 フェーズ 4-1 完了 バージョン 1.0.0 (Build 4)
- **スマホ緊急Web管理画面 JS 構文エラー・削除整合修復 (`WebDashboardHtml.kt`, `LocalWebServer.kt`, `FleaMarketRepository.kt`, `ItemDao.kt`)**：
  - `WebDashboardHtml.kt` の `closeImageViewer()` 直後に残留していた重複記述 `currentViewingPhoto = null; }` を削除し、`node --check` 通過を確認。
  - `FleaMarketRepository.deletePhotoById` / `deletePhotosByIds` から `saveItem(...)` 経由を廃止し、残存 `PhotoEntity.id`・`md5Hash`・`isOriginalDeleted` を保持したまま対象写真のみ削除（`ItemDao.deletePhotosByIds`）＋未参照ファイル削除＋`displayOrder` 再採番を行う安全な実装へ修正。
  - `LocalWebServer.kt` の `/api/photos/delete-batch` で `photoIds` を `List<*>` ＋ `mapNotNull` で安全キャストし、`WebPhotoDto` に `filePath` を追加してフォルダ差分同期を復旧、削除表記も実態に合わせて統一。
- **PC ⇄ スマホ写真0枚同期バグ ＆ Web管理画面不具合修復 (`PcServerClient.kt`, `tools/DashboardHtml.cs`, `tools/FurimaServerNative.cs`)**：
  - `PcServerClient.mergeServerItemsFromJson` でサーバー側 `photos` が空配列（0枚）の場合でも `dao.deletePhotosByItemId` を実行して同期反映するよう修正。
  - `DashboardHtml.cs` / `WebDashboardHtml.kt` の各 `<input type="file">` に `onclick="event.stopPropagation()"` を追加してクリック二重発火を解消し、`openCleanupModal()` へ `escapeHtml` を適用。
  - `FurimaServerNative.cs` の未使用変数 `settingsChanged` を削除（`CS0219` 解消）。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 4`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 4)`** へ3箇所統一。

### [2026-11-15] フェーズ 4 統合実装計画の再編成 ＆ 案A（写真単位ゴミ箱）確定 (`PLAN_SYNC_STATUS_AND_MEMORY.md`)
- `48a42ba` の実態調査（変更対象がスマホ側ローカルWeb管理画面のみでPC側未反映、かつ `WebDashboardHtml.kt` にJS構文エラー等を含む）を踏まえ、ユーザー指定の優先順位（①重大バグ修復 ➔ ②PC管理画面写真ギャラリー改良＋案A写真ゴミ箱 ➔ ③同期ステータス％表示＆メモリ安定化）へ `PLAN_SYNC_STATUS_AND_MEMORY.md` を統合再編成。
- 各サブフェーズ完了ごとの計画書更新＆GitHub `main` 即時同期ルールを明文化。

### [2026-11-15] ヘッダースリム化・同期％表示・メモリ安定化 詳細実装計画書 (`PLAN_SYNC_STATUS_AND_MEMORY.md`) 策定
- 一覧画面上部ヘッダーの縦幅スリム化およびタイトル横のリアルタイムPC同期ステータス（％・枚数・エラー詳細＆再同期ダイアログ）の実装計画を策定。
- 大量高画質撮影時の OOM/ANR 防止に向けた 5 項目（保存 `Dispatchers.IO` 化＆連打ガード、OkHttp ストリーミング送信化、Coil `ImageLoader` 上限＆表示サイズ制限、CameraX 連写負荷軽減＆`2048px/フル`切替記憶、サムネイル `RGB_565`＋一時キャッシュ自動掃除＋`largeHeap`）の具体的な編集対象ファイルと仕様を確定。

### 2026/11/15 アプリ内カメラ「完全記憶型」設定永続化 バージョン 1.0.0 (Build 3)
- **スタジオ撮影向けカメラ設定の完全永続化 (`CameraXCaptureDialog.kt`)**：
  - 端末ローカル `SharedPreferences` (`camerax_capture_prefs`) による `CameraXCapturePreferences` を実装し、ライト常時点灯（トーチ）を除く全撮影設定（使用レンズID、ズーム倍率、明るさ EV 補正、フラッシュ設定、構図ガイド種別、マニュアル詳細パネル開閉状態、ISO感度、シャッター速度、ホワイトバランス）を起動時に自動復元・変更時に自動保存。
  - 次回起動時は選択レンズの対応範囲（ズーム範囲・EV範囲・ISO範囲・露光時間範囲・AWB対応一覧）へ安全にクランプした上で復元するため、機種・レンズ仕様差や非対応状態でもクラッシュせず即座に固定ライティング環境の設定のまま連続撮影を開始可能。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 3`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 3)`** へ3箇所同時統一。

### 2026/11/15 サムネイル拡大プレビュー即削除 ＆ カメラ内連続選別強化 バージョン 1.0.0 (Build 2)
- **`PhotoPreviewDialog.kt` 連続選別対応 ＆ クラッシュ・タップ領域修正**：
  - 削除操作を `confirmBeforeDelete` フラグでモード分離し、撮影・編集中のプレビューでは右上の赤色ゴミ箱ボタン（`48.dp`）から**1タップで即時削除**できる高速ワークフローを実装。削除時は下部フローティングピル（「1枚削除しました — 元に戻す」・3.5秒自動消去）で誤削除を即座に元位置へ復元可能。
  - 最終ページ削除時や空リスト遷移時の `IndexOutOfBoundsException` 防止（`getOrNull` ガード＋`LaunchedEffect` ページクランプ＋空リスト自動クローズ）と、48dp タッチターゲット（閉じる／削除）への適正化を実施。
- **`CameraXCaptureDialog.kt` 撮影直後プレビュー・削除選別統合**：
  - 左下サムネイル（`camerax_thumbnail_preview_button`）をタップ可能にし、カメラ画面を閉じずに撮影済み写真を全画面スワイプ確認 ➔ 不要カットを1タップ即削除（＋Undo）➔ 続けて撮影または確定できる連続選別フローを完成。破棄された一時ファイルは確定／破棄クローズ時に確実に掃除。
- **`ItemEditScreen.kt` ＆ `ItemDetailScreen.kt` プレビュー削除連携**：
  - `ItemEditScreen.kt` のプレビュー削除に1タップ即時削除＋元位置Undo復元を追加。
  - `ItemDetailScreen.kt` の写真一覧プレビューにも確認ダイアログ付き削除（`FleaMarketRepository.removePhotoFromItem` / `FleaMarketViewModel.removePhotoFromItem`）を実装し、他商品未参照ファイルの整理・`displayOrder` 再採番・自動同期に対応。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 2`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0 (Build 2)`** へ3箇所同時統一。

### 2026/11/15 スマホ設定タブのテンプレートプルダウン化 ＆ 編集画面スクロール修正 ＆ PC管理画面エラー修復
- **スマホ設定タブのテンプレート管理プルダウン化 (`SettingsStorageScreen.kt`)**：タイトル用・商品説明用テンプレートの一覧を Material 3 の `ExposedDropdownMenuBox`（プルダウンメニュー）式へ刷新し、選択・プレビュー・編集・削除をすっきりと管理できるように改善。
- **テンプレート編集ダイアログのスクロール見切れ修正**：フルウィンドウテンプレート編集画面の `Column` に `verticalScroll` を追加し、テキストエリアの固定ウェイトを解除（`minLines` / `maxLines` 適用）することで長文でもナビゲーションバーに被らずスクロール・確認可能に修復。
- **PC管理画面のテンプレート管理エラー修復 (`DashboardHtml.cs`)**：設定モーダルオープン時に呼び出されていた未定義の `resetTemplateForm` 関数を追加定義し、`furima-server.exe` を再ビルド。

### 2026/11/14 正式リリース統合 バージョン 1.0.0 (Build 100)
- **`feature/v1.0-updates` の不具合精査 ＆ クリーン統合**：
  - `FurimaServerNative.cs` の `HandleBatchUpdateItems`（`商品情報_説明文.txt` 出力箇所）に残っていた C# 生改行コンパイルエラーを `\r\n` 連結へ修正（重複 `using` 混入を排除したクリーン状態で反映）。
  - 巻き戻っていた `tools/DashboardHtml.cs` をフェーズ 3.2（出品前チェック・大型テンプレート編集＆変数展開・一括編集＆ゴミ箱一括操作）フル実装版へ復元し、バージョン表記のみ `v1.0.0` へ更新。
  - 誤コミットされていた `tools/server.log` を追跡対象外（`.gitignore` に `/tools/server.log` を追加）とし、不要ブランチ `feature/v1.0-updates` を削除して `main` へ一本化。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 100`, `versionName = "1.0.0"`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v1.0.0`** へ 3 箇所同時統一し、`compile_applet` で Android ビルド正常通過を確認。

### 2026/11/14 PC母艦サーバー winexe化 ＆ 完全終了修復 バージョン 0.9β (Build 10)
- **CUIコンソール廃止 (`/target:winexe`) ＋ ログファイル出力 (`tools/build-exe.bat`, `FurimaServerNative.cs`, `TunnelQrForm.cs`)**：
  - `build-exe.bat` の `furima-server.exe` ビルドターゲットを `/target:winexe` へ変更し、起動時の CUI コンソール画面を廃止（Windows 11 ターミナル環境でも完全にタスクトレイ常駐のみ）。
  - `FurimaServerNative.cs` にスレッドセーフな `ServerLogWriter`（`server.log` 自動書き出し・2MB 超過時ローテーション・共有削除許可モード）を実装し、トレイ右クリックメニューに「サーバーログを開く(&L)」を追加。
- **タスクトレイ終了後のプロセス残留・フォルダロック根絶 (`FurimaServerNative.cs`, `TunnelQrForm.cs`)**：
  - サーバー初期化直後に `Environment.CurrentDirectory` および `cloudflared.exe` / 補助プロセスの `WorkingDirectory` を `Path.GetTempPath()` へ退避し、サーバー本体フォルダおよび `syncDir` のディレクトリハンドルロックを防止。
  - `ShutdownServerResources()` を新設して `HttpListener` の `Stop()` + `Close()` + `Abort()`、`cloudflared` の非同期出力キャンセル (`CancelOutputRead`/`CancelErrorRead`) ＋ `taskkill /F /T` によるプロセスツリー強制終了・`Dispose()`、`Mutex` 解放、`ServerLogWriter` 破棄を一元実行し、最後に `Environment.Exit(0)` で確実にプロセス終了。
  - `TunnelQrForm.cs` のバックグラウンド呼び出しをすべて非ブロッキング `SafeBeginInvoke`（`IsDisposed`/`isExiting` ガード付き）へ統一し、終了時の `BrowserSelectDialog` モーダル強制クローズと `Application.Exit()` を追加。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 10`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v0.9β (Build 10)`** へ 3 箇所同時統一（※Windows 上で `tools/build-exe.bat` の再実行が必要）。

### 2026/11/14 フェーズ 3.2 完了 バージョン 0.9β (Build 9)
- **3.2-C (選択モード拡張・ゴミ箱一括操作・一括ゴミ箱移動＆部分一括編集・Build 9統一)**：
  - `ItemListScreen.kt` / `FleaMarketViewModel.kt` / `DashboardHtml.cs`：選択モードの `SOLD` 選択禁止を解除（非削除・非内包商品は全ステータス選択可）し、1件選択から一括操作バーを表示。「まとめ買い」ボタンのみ `2件以上かつ未売却のみ` で有効化する安全ガードを追加。
  - `BatchEditDialog.kt` / `FleaMarketRepository.kt` / `DashboardHtml.cs`：ステータス変更（`soldAt`/`soldPrice`・まとめ子 `statusLabel` 連動）、費用プリセット適用（`基本設定に従う` 含む）、価格変更（固定額/±円/±%、**0円未満クランプ**）を個別ON/OFFできる部分一括編集 ＋ 変更サンプルプレビューを実装。
  - `TrashScreen.kt` / `FurimaServerNative.cs` / `DashboardHtml.cs`：ゴミ箱画面での複数選択一括復元・一括完全削除を追加し、PCサーバーに `POST /api/items/batch-trash`、`POST /api/items/batch-restore`、`DELETE /api/items/batch-permanent`、`POST /api/items/batch-update` を新設（バッチ毎に1回のみ `BumpDataRevision()` / `SaveItemsDbToDisk()` を実行）。
  - **コンパイル検証 ＆ バージョン統一更新**：`FleaMarketRepository.kt`（`getItemWithPhotosById` 参照修正）・`ItemEditScreen.kt`（`FlowRow` インポート/OptIn復元）・`ItemListScreen.kt`（`AlertDialog` インポート追加）・`SettingsStorageScreen.kt`（`ExperimentalMaterial3Api` OptIn追加・重複インポート整理）の4箇所を修復し、`compile_applet` で Android ビルド正常通過を確認。`app/build.gradle.kts` (`versionCode = 9`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を **`v0.9β (Build 9)`** へ3箇所同時統一（※Windows向け `.exe` および実機配布用 `.apk` のバイナリ再ビルドは各自環境で実行可能）。

### 2026/11/14 フェーズ 3.2-B 完了 (進行中 ➔ 目標 v0.9β Build 9)
- **3.2-B (テンプレートUI刷新・フルウィンドウ編集・変数展開・PCワンクリックコピー)**：
  - `ItemEditScreen.kt`：タイトル/商品説明テンプレート選択を `ExposedDropdownMenuBox` ドロップダウンへ移行し、選択挿入後即座に選択表示をリセット（連続挿入可能）。挿入時に `{title}` / `{price}` 変数を自動展開。
  - `SettingsStorageScreen.kt`：従来の小サイズ `AlertDialog` を廃止し、`Dialog(usePlatformDefaultWidth = false)` + `Scaffold` + `imePadding()` / `navigationBarsPadding()` + `{title}`/`{price}` 変数チップ付きフルウィンドウ大型テキストエリアエディタを実装。
  - `DashboardHtml.cs`：テンプレート編集モーダルを全画面スタイル (`#template-editor-modal`, `max-w-4xl h-[88vh]`) へ拡張、プルダウン挿入時の `{title}`/`{price}` 変数展開に対応。`copyText` にボタン上 `✓ コピー済` フィードバックを追加し、編集モーダル内にもタイトル/説明文ワンクリックコピーボタンを配置。

### 2026/11/14 フェーズ 3.2-A 完了 (進行中 ➔ 目標 v0.9β Build 9)
- **3.2-A (CameraXオーバーレイ化・プレビュー1本指スワイプ透過・出品前チェック)**：
  - `CameraXCaptureDialog.kt` / `ItemEditScreen.kt`：`Dialog` ウィンドウを廃止して `ItemEditScreen` ルート `Box` 最前面オーバーレイ (`zIndex(100f)`) へ移行し、`MainActivity` の `enableEdgeToEdge()` に基づく `navigationBarsPadding()` を確実に適用（Xiaomi HyperOS / Android 15 での3ボタンバー被りを根本解消）。
  - `PhotoPreviewDialog.kt`：`ZoomablePhotoPage` の `detectTransformGestures` を `awaitEachGesture` ベースへ刷新し、2本指ピンチまたは拡大中 (`scale > 1.05f`) のみイベント消費することで等倍時の1本指左右スワイプが `HorizontalPager` へ透過するよう修復。
  - `ListingValidationBanner.kt` / `ItemEditScreen.kt` / `ItemDetailScreen.kt` / `DashboardHtml.cs`：タイトル未入力/40文字超過・写真0枚/10枚超過・価格0円/300円未満・純利益0円以下・説明文未入力を事前検知する出品前チェックアラートをスマホ＆PC双方に追加。

### 2026/11/14 フェーズ 3.1 完了 バージョン 0.9β (Build 8)
- **ハイブリッドカメラ UI ＆ `rememberSaveable` 状態保護 (`ItemEditScreen.kt`, `AndroidManifest.xml`, `PhotoStorageManager.kt`)**：縦方向のレイアウト高さを増やさず従来の `52.dp` 高さのままで撮影ボタンを横並び 2 分割（「アプリ内カメラ」＋「標準カメラ」）へ改修。`AndroidManifest.xml` に `CAMERA` 権限と `STILL_IMAGE_CAMERA` 用 `<queries>` を追加し、左ボタンは `ActivityResultContracts.RequestPermission()` 経由で `CameraXCaptureDialog` を起動、右ボタンは `MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA` により POCO X6 Pro（HyperOS）標準カメラのフル機能（プロモード・マクロ・彩度調整等）を起動（非対応環境時は `TakePicture` へフォールバック）＋ギャラリー取込案内トーストを表示するよう実装。また一時撮影パス等の状態を `rememberSaveable` で保護。
- **アプリ内 CameraX 撮影画面 (`CameraXCaptureDialog.kt`)**：`PreviewView` (`ImplementationMode.COMPATIBLE`) による安定プレビュー、画面タップAF/AE・フォーカスリング、ピンチ＆プリセットズーム、露出補正（明るさ EV）スライダー、フラッシュ/トーチ切替、利用可能レンズ切替、3×3 グリッド＆1対1出品枠ガイド、1枚取消対応の連続撮影バッチ追加、および `Camera2Interop` による対応端末向け簡易マニュアル調整（ISO・シャッター速度・WB・Auto戻し、非対応項目自動非表示）を実装。
- **写真拡大プレビュー原本取得ボタンの上部移設 (`PhotoPreviewDialog.kt`) ＋ Build 8 統一**：「PCから高画質原本を取得」ボタンを画面最下部から上部ヘッダーエリア（`statusBarsPadding()` 適用）へ移設してシステムナビゲーションバーとの重なりを解消。`app/build.gradle.kts` (`versionCode = 8`, `targetSdk = 35` 維持)、`WebDashboardHtml.kt`、`tools/DashboardHtml.cs` を **`v0.9β (Build 8)`** へ統一し、`compile_applet` でコンパイル正常完了を確認。

### 2026/11/14 フェーズ 2.17 完了 バージョン 0.9β (Build 7)
- **2.17-A (ポーリング最適化・ディスクI/O削減・ロック安全化)**：`DashboardHtml.cs` の3秒ポーリングを軽量な `/api/server/status` のみへ一本化（`document.hidden` 時は自動停止・復帰時即時確認）し、`dataRevision` / `settingsRevision` 変化時のみ `/api/items`・`/api/cleanup/preview`・`/api/settings` を取得するよう最適化。`FurimaServerNative.cs` では `HandleSyncItems` の `SaveItemsDbToDisk()` を `dataChanged` 時のみ実行へ限定し、`HandleSyncPhoto` の未使用 `byte[] buffer` 確保を削除し、`hashToFilePathMap.Count` / `itemsDb.Count` 参照をすべて `lock (lockObj)` 内に保護。
- **2.17-B (ストレージ安全性・孤立ファイル掃除・不要資産/依存整理・Build 7 統一)**：`PhotoStorageManager.savePhotoFromUri` で `openInputStream` が `null` または書き出しサイズ `<= 0` 時に `null` を返却するよう保護。`FleaMarketRepository.saveItem` で編集時に外された写真について他商品未参照の端末ファイルを削除する孤立ファイル掃除を追加し、未実装スタブ `cleanupSharedPhotos()` と旧補助スクリプト (`tools/furima-sync.bat`, `tools/furima-sync.ps1`) を削除。`app/build.gradle.kts` から未使用依存（`retrofit`, `converter-moshi`, `logging-interceptor`, 未使用 `firebase-*`）をコメントアウト整理し、`v0.9β (Build 7)` (`versionCode = 7`) へ3箇所同時統一・APK更新・全ユニットテスト通過を確認。

### 2026/11/14 フェーズ 2.16 完了 バージョン 0.9β (Build 6)
- **2.16-A (デバッグ残骸撤去・まとめ買い子ステータス・クリーンアップ安全化)**：`DashboardHtml.cs` (`item.id === 18`) と `FurimaServerNative.cs` (`parentId == 18`) のテスト用ハードコードを撤去。`HandleMergeItems` の子 `statusLabel` を `asStatus` に応じて `"まとめ売却済"` / `"まとめ出品中"` へ適正化し、`SyncStore.GetCleanupCandidates` を `status == "SOLD"` のみに限定＋アクティブ他商品（`LISTED` / `DRAFT` / 未売却親に属する `MERGED` 子）が参照中のハッシュ・ファイル除外ガードを追加。
- **2.16-B (PC単独編集整合・サーバー堅牢化・フォルダ/復元修復)**：`DashboardHtml.cs` の旧「操作・編集ロック中」バナーを「PC単独で編集可／スマホ再接続時に自動同期」へ刷新。`FurimaServerNative.cs` では `lastHeartbeatSec` の `-1` 保護、`HandleRequest` 終端 `catch` 保護、`ParseBoolSafe` 導入による `JValue(null)` 回避、`Encoding.UTF8` 統一、`ResolveOrCreateItemFolder` によるタイトル変更時フォルダ分断解消、`displayOrder`/`childItemId` 保持を実施。Android 側も `ItemEditScreen.kt` の非売却時 `soldPrice = 0` 化および `LocalWebServer.kt` の `/api/photos/{photoId}/restore` サムネイル上書き防止を実装。
- **2.16-C (画像形式/EXIF/クリップボード対応・旧モーダル整理・Build 6 統一)**：`FurimaServerNative.cs` でマジックバイト判定 (`image/jpeg`, `image/png`, `image/webp`, `image/gif`) と `.jpg`/`.jpeg`/`.png`/`.webp` スキャンを実装。`DashboardHtml.cs` の `copyPhotoBlob` を Canvas 経由 `image/png` 変換へ改善し、旧 `openImageModal` / `#image-modal` を整理。`PhotoStorageManager.kt` に `ExifInterface` 回転補正を組み込み、`v0.9β (Build 6)` (`versionCode = 6`) へ3箇所同時統一・APK更新・全テスト通過を確認。

### 2026/11/14 フェーズ 2.15 完了 バージョン 0.9β (Build 5)
- **2.15-A (PCサーバー `JArray`/`JObject` 型不一致根絶)**：`SyncStore.cs` に `NormalizePhotosList(object)` を新設し、`CountMissingFiles` / `GetCleanupCandidates` および `FurimaServerNative.cs` の `HandleSyncPhoto` / `HandleUnmergeItem` / `HandleAddPhoto` の全5箇所を正規化経由へ統一。再起動後の `items_data.json` 読込状態でも写真追加時の既存写真消失や欠損数・クリーンアップ・フォルダ配置の破損が発生しないよう修復。
- **2.15-B (スマホ側 原本削除・再保存・共有写真の `isOriginalDeleted` / `md5Hash` 破壊修復)**：`ItemDao.kt` に同一 `filePath` 一括更新クエリ `markOriginalDeletedByFilePath` を追加し、`FleaMarketViewModel.cleanupSharedOriginalPhotos` および `PcServerClient.syncDeltaToPcServer` での原本削除時に親子双方の `PhotoEntity` を更新するよう修正。`FleaMarketRepository.saveItem` では `thumbnailPath` でも既存写真を照合して元の `md5Hash` と `isOriginalDeleted` を保持し、`PcServerClient.mergeServerItemsFromJson` では実原本の存在有無で `isOriginalDeleted` を正しく判定するよう修正。`Phase215PhotoIntegrityTest.kt`（3件）を追加し全テスト通過を確認。
- **2.15-C (`tools/build-exe.bat` 復旧・DLL整理・Build 5 統一)**：`build-exe.bat` に `TunnelQrForm.cs` を追加し、重複していた `tools/lib/Newtonsoft.Json.dll` を整理して `tools/Newtonsoft.Json.dll` へ参照を一本化（`.NET Framework 4.6+` 要件を明記）。`app/build.gradle.kts` (`versionCode = 5`)、`tools/DashboardHtml.cs`、`WebDashboardHtml.kt` を `v0.9β (Build 5)` へ統一し、`flea_market_app.apk` / `public/flea_market_app.apk` を更新（※Windows向け `.exe` 再生成は Windows 環境で `tools/build-exe.bat` 実行が必要）。

### [2026-11-14] コードレビュー総点検に基づく修復・改善計画書 (`PLAN_PHASE_2_15_TO_2_17.md`) 策定
- 最新コミット (`3f87ea8`) の徹底コードレビュー結果に基づき、重大度「致命的」➔「警告」➔「提案」の優先順で順次修復するための詳細実装計画書を `PLAN_PHASE_2_15_TO_2_17.md` に策定。
- 仕様変更点の反映：初期の「スマホ常時接続時のみPC操作許可」仕様から現在の「PC単独編集 ＋ ゴミ箱復元対応 ＋ 安全な双方向同期」方針への移行を明確化し、ロック表記の撤廃と逐次ハンドオーバー時の整合性修復を組み込んだ。

### 2026/11/14 フェーズ 2.14 完了 バージョン 0.9β (Build 4)
- **2.14-A (Room DB拡張 ＆ 論理削除/復元リポジトリ)**：`ItemEntity` に `syncUuid`・`deletedAt`・`isDirty` を追加し、`AppDatabase` を Version 6 (`MIGRATION_5_6`) へ移行。通常一覧/集計クエリへ `deletedAt IS NULL` を適用し、`FleaMarketRepository` に `moveToTrash`・`restoreFromTrash`・`permanentlyDeleteItem`・`emptyTrash`・`purgeExpiredTrash(30)`・`unmergeItem` 時トームストーン記録を実装。
- **2.14-B (イベント駆動Push ＋ リビジョン検知Pull ＋ 競合防止同期)**：`SyncDtos.kt` / `PcServerClient.kt` / `FleaMarketViewModel.kt` と PC側 `SyncStore.cs` / `FurimaServerNative.cs` を同期プロトコルv4対応へ刷新。`dataRevision` / `settingsRevision` による軽量ハートビート検知、変更発生時500msデバウンスPush、再接続時Push先行ハンドシェイク、`syncUuid` 基準の和集合マージ＆数値ID衝突回避、`isDirty`・`updatedAt` 保護、90日保持トームストーン (`deleted_uuids.json`) によるゾンビ復活防止および `HandleSyncItems` からの無条件全削除廃止を実装。
- **2.14-C (スマホ＆PC共通ゴミ箱UI ＋ Build 4 統一)**：`TrashScreen.kt`・`MainActivity.kt`・`ItemListScreen.kt`（件数バッジ付きアイコン導線）・`ItemDetailScreen.kt`・`ItemQuickActionDialog.kt` へゴミ箱移動/復元/完全削除/空にするUIおよび30日保持カウント表示を実装。PC側 `DashboardHtml.cs` にも `TRASH` フィルタータブ・バナー・復元/完全削除/空にする操作を追加し、`Phase214TrashAndSyncTest.kt`（Robolectric/JUnit）を追加。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 4`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 4)` へ統一。

### 2026/11/13 フェーズ 2.13 完了 バージョン 0.9β (Build 3)
- **2.13-A (設定双方向同期基盤)**：`DefaultSettings` / `DefaultSettingsManager` / `SyncDtos` / `PcServerClient` / `FleaMarketViewModel` および PC側 `SyncStore.cs` / `FurimaServerNative.cs` を拡張し、`titleTemplates`・`descriptionTemplates`・`settingsUpdatedAt` を含む設定モデルを追加。`PUT /api/settings` の実装および `settingsUpdatedAt` 比較による古い設定の先祖返りガードを追加（Room DB 無変更）。
- **2.13-B (デフォルトプリセット変更・編集強化)**：`SettingsStorageScreen.kt` にて初期・追加を含む全プリセットの編集・削除（最低1件ガード）・ワンタップ「標準にする」即時同期を実装。`ItemEditScreen.kt` の個別費用設定内にプリセット適用チップを追加し、PC管理画面 `DashboardHtml.cs` にも設定モーダルを追加。
- **2.13-C (タイトル／商品説明テンプレート機能 ＋ Build 3 統一)**：スマホ設定画面＆PC設定モーダルにタイトル用・商品説明用テンプレートの追加・編集・削除UIを実装。`ItemEditScreen.kt`・`MergeItemsDialog.kt`（`TextFieldValue` カーソル位置挿入）および `DashboardHtml.cs`（`insertTextAtCursor` による新規・編集・まとめモーダル挿入）へ統合。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 3`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 3)` へ統一し、`compile_applet` 成功を確認。

### 2026/11/13 フェーズ 2.12 完了 バージョン 0.9β (Build 2)
- **2.12-A (長押しメニューのステータス表示不一致修正)**：`ItemQuickActionDialog.kt` のステータスバッジを一覧カードと同じ4種類（売却済・まとめ済・下書き・出品中）に統一し、下書き時の切替アクション（「出品中にする」）および `ItemListScreen.kt` の遷移処理を修正。
- **2.12-B (長押しメニューの削除/解除後残留修正)**：`ItemQuickActionDialog.kt` の削除・まとめ解除確認ボタンに二重クリック防止ガードと即時 `onDismiss()` を追加し、`ItemListScreen.kt` 側でも即座に `quickActionItem = null` を適用。
- **2.12-C (写真拡大プレビュー ＋ Build 2 統一)**：`PhotoPreviewDialog.kt` を新設（`HorizontalPager`、ピンチ拡大縮小、ダブルタップズーム、原本キャッシュ確認・PCサーバーからの「PCから高画質原本を取得」ボタン）し、`ItemDetailScreen.kt` および `ItemEditScreen.kt` のサムネイルタップへ連携。
- **バージョン統一更新**：`app/build.gradle.kts` (`versionCode = 2`), `tools/DashboardHtml.cs`, `WebDashboardHtml.kt` を `v0.9β (Build 2)` へ統一し、`compile_applet` 成功を確認。

### [2026-11-13] フェーズ 2.12〜2.14 詳細実装計画書 (`PLAN_PHASE_2_12_TO_2_14.md`) の策定・追記
- スマホUIバグ2件（長押しステータス不一致・削除時メニュー残留）、原本取得ボタン付き拡大プレビュー、テンプレート＆デフォルトプリセット変更、および本命「ゴミ箱」＋一人利用最適のイベント駆動・競合防止同期設計の詳細仕様を `PLAN_PHASE_2_12_TO_2_14.md` に策定完了。
- 本計画書および進捗ログをローカル Git リポジトリへ先行コミット（GitHub リモート同期はトークン再共有時に実施）。

### [2026-11-13] 実運用開始に伴うバージョンリセット (`v0.9β Build 1`) & 履歴ドキュメント整理完了
- 全機能の安定動作および実運用到達を確認し、バージョン表記を `v0.9β (Build 1)` へ更新統一。
- **改修対象ファイル**:
  1. `app/build.gradle.kts` (`versionCode = 1`, `versionName = "0.9β"`)
  2. `tools/DashboardHtml.cs` (`v0.9β (Build 1)`)
  3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v0.9β (Build 1)`)
- 過去の全フェーズ・計画書ログ（Phase 1.0 〜 Phase 2.11）を `PROGRESS_ARCHIVE_PHASE2.md` に集約・隔離完了。
- 実機判定 (`isEmulator()`) によるクリーン起動機能の追加完了。
- 単体テスト (`testDebugUnitTest`) およびコンパイル検証の全件通過を確認。
