# 開発進捗・引き継ぎログ (Progress & Handover Log)

**現在バージョン: `v1.6 (Build 2)` (`versionName = "1.6"`, `versionCode = 1602`)**（2026-11-27 時点）

本ドキュメントは、第2世代アーキテクチャ（母艦PCマスターサーバー型 ＆ MD5ハッシュ差分同期構成）の現行構成、実装・レビュー規則、および主要マイルストーンを管理するための引き継ぎ台帳です。過去のフェーズ別詳細ログ・計画書・レビュー台帳は [`docs/archive/`](docs/archive/) に整理・保管しています。

---

## 1. 現行アーキテクチャ構成（Phase D リファクタリング後）

| 領域 | 構成・主要ファイル |
| --- | --- |
| **Android アプリ (`app/`)** | Kotlin / Jetpack Compose / Material 3 / Room / Navigation Compose / CameraX (`targetSdk = 35`, `compileSdk = 36`, `minSdk = 24`) |
| ├ データ・同期層 | `FleaMarketRepository.kt`, `SyncTombstoneStore.kt`, `TrashAndMergeRepository.kt`, `PhotoStorageManager.kt`, `DefaultSettingsManager.kt`, `CsvExporter.kt`, `data/local/` |
| ├ 通信・サービス層 | `PcServerClient.kt`（窓口）, `PcServerTransport.kt`, `PcClaudeApiClient.kt`, `PcDeltaSyncUploader.kt`, `ServerItemMergeEngine.kt`, `PcSyncForegroundService.kt`, `SyncCoordinator.kt`, `ClaudeLabelDerivation.kt` |
| ├ 緊急ローカル Web サーバー | `LocalWebServer.kt`（Ktor CIO・12桁認証コード必須・同一オリジン CORS）, `WebDashboardHtml.kt` |
| └ ViewModel・UI 層 | `FleaMarketViewModel.kt`, `ItemFilterSorter.kt`, `MaintenanceExportHelper.kt`, `ClaudeWorkflowController.kt`, `PcSyncController.kt`, `ui/screens/*`, `ui/components/*` |
| **PC 母艦サーバー (`tools/`)** | C# 5 (.NET Framework 4.x `csc.exe` ビルド、9分割 `partial class`) |
| ├ サーバー本体 | `FurimaServerNative.Core.cs`, `.Auth.cs`, `.Persistence.cs`, `.Sync.cs`, `.Items.cs`, `.Photos.cs`, `.MuseRef.cs`, `.Claude.cs`, `.ClaudeDelivery.cs` |
| ├ ストア・UI・補助ツール | `SyncStore.cs`, `ClaudeJobStore.cs`, `TunnelQrForm.cs`（WinForms＋トレイ＋再起動）, `DashboardHtml.cs`（埋め込みリソース `tools/web/dashboard.html`）, `FurimaSyncNative.cs` |
| **自動テスト ＆ CI** | `.github/workflows/build-check.yml`: Windows C# ビルド＆`tools/tests/FurimaServerTests.cs` (`furima-server-tests.exe`), `node --test tools/tests/*.mjs`, Android `assembleDebug` ＆ `testDebugUnitTest`（Robolectric 含む） |

---

## 2. 開発・実装・レビュー基本原則（継承ルール）

1. **小規模逐次実装 ＆ 最小限の変更**:
   - 機能追加や構造変更は計画書を策定したうえで、影響範囲の小さいフェーズ／バッチ単位で進める。関係のない既存コードや安定した画面レイアウト・配色は勝手に変更しない。
2. **仕様・レビュー・進捗の連動規則**:
   - 着工前に計画書とレビュー指摘の合意を確認し、仕様変更やレビュー是正が発生した場合は該当計画書（および必要に応じ本ファイル）も同時に更新する。
   - 過去の完了済み詳細ログ・旧計画書は `docs/archive/` を参照し、`PROGRESS.md` には現行運用・規則・主要マイルストーンの要点のみを簡潔に保つ。
3. **C# 5 互換厳守（PC サーバー側）**:
   - `tools/*.cs` は Windows CI（`.NET Framework v4.0.30319 csc.exe`）でコンパイルするため、C# 6 以降の構文（`?.`、`$""` 文字列補間、`=>` 式メンバー、`out var`、`nameof` 等）を使用しない。
4. **検証・ビルド最適化**:
   - 純粋な Web UI（`tools/web/dashboard.html`）調整や軽微な文言修正時は `node --test tools/tests/*.mjs` と構文検証を優先し、不要な全量ビルドで作業コストを増やさない。
   - ロジック変更・リリース時は CI（`build-check.yml`）で C# 実ビルド／単体テスト、JS テスト、Android ビルド＆単体テストの全通過を確認する。
5. **厳格なバージョン表記・同時統一更新規則**:
   - 新機能マイルストーンで `v1.x` を更新し、バグ修正・小規模改善ごとに `Build N` を加算する。Android `versionCode` は `major * 1000 + minor * 100 + build`（例: `v1.6 (Build 2)` → `1602`）で単調増加を維持する。
   - バージョン更新時は次の **4 箇所を必ず同時に統一** する:
     1. `app/build.gradle.kts`（`versionName` / `versionCode`）
     2. `tools/web/dashboard.html`（管理画面ヘッダ／バージョン表示）
     3. `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`（緊急 Web 管理画面）
     4. `tools/tests/photo-sync-integrity-build17.test.mjs`（バージョン一致検証テスト）
6. **実機直接インストール設定の維持**:
   - `gradle.properties` の `android.injected.testOnly=false` および安定版 `targetSdk`・署名設定を維持する（生成済み `.apk` / `.exe` / `store.json` 等は `.gitignore` 対象とし Git 追跡しない）。
7. **Git コミット ＆ GitHub 同期**:
   - 各フェーズ／修正の完了ごとに変更ファイルのみを選択的にステージして `commit` し、`main` ブランチへ同期する。`.idea/` 等の個人環境差分やビルド成果物の混入を避ける。

---

## 3. 主要マイルストーン履歴（整理版）

### `v1.6 (Build 2)` — 2026-11-27（最新）
- **母艦サーバー再起動機能 ＆ セキュリティ強化**:
  - `FurimaServerNative.Core.cs` / `TunnelQrForm.cs` / `tools/web/dashboard.html` に、WinForms ボタン・トレイメニュー・Web 管理画面（`POST /api/server/restart`）からの再起動機能を追加（`--wait-pid=` で旧プロセス終了待機後に 8080 再確保）。
  - `TunnelQrForm.cs` に 25 秒無通信時の待機表示復帰タイマーとトンネル発行中表示を追加。
  - レビュー指摘対応として `POST /api/server/restart` を `HasForwardedHeaders(req)` 拒否＋ループバック限定の PC ローカル専用へ厳格化し、`TunnelQrForm.cs` の未使用 `SetTunnelIssuing()` 削除と `StringFormat` の `using` 復元を実施。
  - 冗長化していた `PROGRESS.md` を現行構成・規則継承のまま整理し、全バージョン表記を **`v1.6 (Build 2)` (`1602`)** へ統一。

### `v1.6 (Build 1)` — 2026-11-25〜26
- **Muse 自動巡回の拾い漏れ・到達性修正（[PLAN_PATROL_RELIABILITY_FIX.md](PLAN_PATROL_RELIABILITY_FIX.md) / Phase P1〜P5 ＋ レビュー是正）**:
  - **P1**: 監視コンタクト（`lastClaudeWatchContactAt`、メモリ）と実巡回コンタクト（`lastClaudePatrolContactAt`、`store.json` 30秒間引き保存）を分離記録し、ダッシュボード表示と `trustedMuseIp` 未設定警告を追加。
  - **P2**: サーバー起動時自動ウェイク（実行可能ジョブ存在時のみリビジョン更新）と「巡回を起こし直す」ボタン（`POST /api/claude/wake`）を追加。
  - **P3**: 案B（`approval_pending` / `approved` の自動待機落ち完全廃止）を確定し、`processing` はサーバー再起動時の期限延長なし（`ClaudeDelivery.cs` の通常処理中 1 枚ごと +30 分スライディング延長は維持）＋期限切れ時は `waiting` へ戻さず `done` / `done_with_errors` で終端停止へ統一。
  - **P4 / P5**: `/api/claude/revision` へ `executableCount` / `serverBootId` を同梱。`TryStartHttpListener` の待ち受けモード（`all` / `localhost`）可視化と既定ポート 8080 の 300ms×5 回リトライを実装。
  - **レビュー是正**: `wakeClaudePatrol()` の `lastKnownClaudeRevision` 先行上書きを除去し、`SyncStore._lastClaudePatrolPersistMs` を `Initialize()` / `SetSyncDirForTest()` で初期化。

### 全体コードレビュー ＆ 安定化・モノリス分割（[REVIEW_FULL_CODEBASE_AUDIT.md](REVIEW_FULL_CODEBASE_AUDIT.md) / Phase A〜D）— 2026-11-24〜25
- **Phase A（クラッシュ・データ保全・セキュリティ）**: `HandleRequest` 外側例外捕捉、PC 全 JSON ストアのアトミック保存＋`.bak` 復旧、`lockObj` 外参照の解消、`LocalWebServer.kt` 認証コード必須化・同一オリジン CORS・パストラバーサル防止、`WebDashboardHtml.kt` XSS 対策、まとめ買い検証強化。
- **Phase B（軽量化・ロック競合解消）**: 写真配信ロック縮小＋ETag/304、状態監視ポーリングの I/O キャッシュ化、Android 側 4 秒二重ポーリング統合、`PROFIT_DESC` 再計算修正、`CancellationException` 再スロー、Main スレッド `File.exists()` 排除。
- **Phase C（UI/UX 不具合是正・§51〜§52）**: `ItemEditScreen` 状態退避＋一時写真掃除、他画面でのバックグラウンド同期追従、PC 管理画面のヘッダ 1 行化・モーダルスクロール枠分離・トースト最前面化（`z-[200]`）・ドラッグアウト誤クローズ防止（`bindSafeBackdropClose`）。
- **Phase D（モノリス分割・リポ整理・テスト拡充）**: 追跡バイナリ／未使用 Vite テンプレート整理、過去ドキュメントの `docs/archive/` 退避、C# サーバー 9 分割＆ `FurimaSyncNative` 近代化＆ `FurimaServerTests.cs` 導入、Android データ層／ViewModel／UI 分割＆ Robolectric UI テスト追加。

### 過去バージョン実績サマリ（詳細は `docs/archive/` 参照）
- **`v1.5 (Build 18〜20)`**: タップ順送信統一・PC ライトボックス Muse チェック（6-14 / Build 18）、参考画像コレクション機能（6-13 / Build 19）、番号スタンプ任意選択（6-15 / Build 20）。
- **`v1.4 (Build 14〜17)`**: 手動/待機/巡回キュー・即時実行・加工ラベル連動（6-8〜6-10 / Build 14〜16）、空 MD5 ゴースト遮断・画面オフ同期耐性強化・救済削除（Build 17）。
- **`v1.0〜v1.3 (Build 1〜13)`**: 母艦 PC マスターサーバー構成・MD5 差分同期・CameraX 撮影・まとめ機能・「Muse に送る」パイプライン基盤・ミューズカラム構築。

---

## 4. ドキュメント索引 ＆ バックログメモ

- **現行計画・監査ドキュメント**:
  - [PLAN_PATROL_RELIABILITY_FIX.md](PLAN_PATROL_RELIABILITY_FIX.md)（巡回信頼性修正計画）
  - [REVIEW_FULL_CODEBASE_AUDIT.md](REVIEW_FULL_CODEBASE_AUDIT.md)（全体コードレビュー報告書 ＆ Phase A〜D 完了記録）
- **アーカイブ（完了済み計画・レビュー・旧ログ）**: [`docs/archive/`](docs/archive/)
  - `PROGRESS_ARCHIVE_PHASE1.md`, `PROGRESS_ARCHIVE_PHASE2.md`, `PROGRESS_ARCHIVE_PHASE3-5.md`
  - `PLAN_CLAUDE_SEND_PHASE5.md`, `PLAN_CLAUDE_SEND_PHASE6.md`, `PLAN_PHOTO_SYNC_INTEGRITY_FIX.md`, `PLAN_SYNC_STATUS_AND_MEMORY.md`, `PLAN_CAMERA_AND_PREVIEW_FIX.md` 他
  - `REVIEW_CLAUDE_SEND_PHASE5.md`, `REVIEW_CLAUDE_SEND_PHASE6.md`, `REVIEW_PROGRESS_PHASE6.md`
- **今後の参考メモ（急ぎではないバックログ）**:
  - **Android タブレット・折りたたみ端末対応（Play ストア `targetSdk 36` 見据え）**: 向き固定はすでに無いため基礎はあるが、将来的に `selectedTab` 等の `rememberSaveable` 化、`GridCells.Adaptive`、最大幅制限（`widthIn`）・`NavigationRail` 対応、`CameraXCaptureDialog` 横画面調整が候補。
  - **超大規模データ時（商品千件超）の最適化**: `items_data.json` の遅延フラッシュ／インデント省略化、PC 一覧検索のデバウンス・ページング導入。
