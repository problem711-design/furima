# 巡回拾い漏れ・到達性修正計画 (Patrol Reliability Fix Plan)

- 策定: 2026-11-25（ユーザー合意・実装着手）
- 改訂: 2026-11-25（案B「自動待機落ち完全廃止＋`processing` 起動時延長なし＆`done_with_errors` 終端」・`v1.6 (Build 1)` 採番を確定）
- 前提となる現在地: 全体コードレビュー＆安定化・軽量化・構造見直し（監査 Phase A〜D、`REVIEW_FULL_CODEBASE_AUDIT.md` §6 で完了記録）適用済み。現行バージョンは **v1.5 (Build 20) / versionCode 1520** → 本改修をもって **v1.6 (Build 1) / versionCode 1601** へ統一更新する。
- 対象の問題: 「Museに送る」の自動巡回（Muse 側フック `claude-revision-watch`、180 秒間隔ポーリング）が、ジョブを長期間拾えないことがある。

---

## 1. 実測事実と運用前提

### 1.1 実測の要点
1. **到達失敗の長期継続**: 2026-11-24 09:57〜11-25 14:45 にかけて約 28 時間 48 分、`GET /api/claude/revision` が連続失敗。また Muse 側の VM 自体も間欠稼働（停止・差し替えが発生）。
2. **トークン失効による待機落ち**: 未着手 30 分失効により承認済みジョブが `waiting` へ落ちていた。一方で、起床して処理へ入った後は正常に完了する。
3. **固定条件・権限制約**:
   - PC サーバーは常時稼働ではなく、再起動・再接続・AP 切り替えが発生する。
   - ユーザーには LAN ルーターのポート開放、Windows ファイアウォール設定、URL 予約（`urlacl`）変更に関する管理者権限がない。
   - 運用開始以来 `localhost:8080` 以外が使用された実績はない（Tailscale / `cloudflared` 構成のためルーター開放は不要）。

### 1.2 設計方針
- **「どちら側が落ちていても、サーバー起動・復帰したときに自動で合図を出し直し、未着手ジョブが勝手に待機落ちしない」構造にする**。
- **トークン消費の異常防止（最優先）**:
  - トークン消費が始まっていない **`approval_pending`（承認待ち）** と **`approved`（開始待ち）** の時間切れ自動待機落ちは仕様上 **完全廃止（案B）** とする。
  - 1 枚でも写真取得が始まった **`processing`（処理中）** のみを厳格に保護し、**`waiting` 戻しを完全禁止＋サーバー再起動時の期限自動延長なし（`ClaudeDelivery.cs` による通常処理中の 1 枚ごとの +30 分スライディング延長のみ維持）＋期限切れ時は `done_with_errors`（エラー終端）で停止** させる。

---

## 2. 現行コードの問題箇所

1. **合図と起床条件**: `claudeRevision` は `claude_jobs.json` に永続化されているため、サーバー再起動だけでは値が進まず、過去リビジョンで空振りしたジョブで再起床しない。`GET /api/claude/revision` は `claudeRevision` のみ返し、`executableCount` や起動世代を持たない。
2. **タイムアウトと待機落ち**（`FurimaServerNative.Claude.cs`）:
   - `ApplyApprovalPendingExpiry` が 10 分で `waiting` へ戻す。
   - `ApplyTokenExpiry` が `approved` を 30 分で `waiting` へ戻し、さらに `results`/`errors` がともに空の `processing` まで `waiting` へ戻してしまう穴がある。
3. **待ち受け・ポート確保**（`FurimaServerNative.Core.cs` `TryStartHttpListener` / `Main`）: `http://+:{port}/` 失敗時にサイレントで `localhost` 限定へ縮小しても `boundUrl` が同じで見分けがつかない。また、直前プロセス終了直後の一時占有で即 8081 へズレる。
4. **ミューズカラム**（`tools/web/dashboard.html`）: 巡回の生死（最終監視／最終巡回）・手動ウェイク操作・`trustedMuseIp` 未設定時の案内が無い。

---

## 3. 修正フェーズ計画（各フェーズ完了ごとにコミット＆プッシュ）

### Phase P1. 監視・実巡回コンタクトの記録・表示 ＋ 信頼済み IP 未設定警告
- `FurimaServerNative.Auth.cs`: `IsTrustedClaudeIp(HttpListenerRequest req)` を共通化（転送ヘッダなし＋`trustedMuseIp` 一致、IPv4-mapped IPv6 正規化）。
- `FurimaServerNative.Core.cs` / `SyncStore.cs`:
  - `GET /api/claude/revision` 到達かつ `IsTrustedClaudeIp` 一致時に **`lastClaudeWatchContactAt`** をメモリへ記録（ディスク I/O 回避）。
  - `IsTrustedClaudeExemptRequest` 通過時に **`lastClaudePatrolContactAt`** をメモリ＋ `store.json`（トップレベル `StoreConfig`）へ記録。
  - `GET /api/server/status` に `lastClaudeWatchContactAt`、`lastClaudePatrolContactAt`、`trustedMuseIpConfigured`（bool）を追加。
- `tools/web/dashboard.html`:
  - ミューズカラムに「最終監視／最終巡回」を表示（10:00〜22:00 で最終監視が 15 分超経過なら控えめな注意色）。
  - `trustedMuseIpConfigured === false` の場合、ミューズカラム内に控えめな未設定案内＋設定モーダルを開く導線を表示。
- `tools/tests/FurimaServerTests.cs`: `SyncStore` / `IsTrustedClaudeIp` / コンタクト記録に関する単体テスト追加。

### Phase P2. 手動「巡回を起こし直す」ボタン ＋ サーバー起動時の自動ウェイク
- `ClaudeJobStore.cs`: 起動時用の `WakeOnStartupIfExecutable()` を追加（`approved` または `processing` ジョブが 1 件以上ある場合のみ `claudeRevision` を +1 保存）。
- `FurimaServerNative.Core.cs` / `.Claude.cs`:
  - `Main` の `ClaudeJobStore.CleanupOnStartup()` 直後に `WakeOnStartupIfExecutable()` を実行。
  - `POST /api/claude/wake`（Bearer 必須・PC ローカル経路のみ、Muse 免除対象外）を追加し、ジョブ状態を変えず `BumpClaudeRevision()` のみ実行してサマリと `claudeRevision` を返す。
- `tools/web/dashboard.html`: ミューズカラムに「巡回を起こし直す」ボタン＋短い説明（「巡回の合図を出し直します」）を追加。
- `tools/tests/FurimaServerTests.cs`: `WakeOnStartupIfExecutable` の単体テスト追加。

### Phase P3. 自動待機落ち完全廃止（案B）＋ `processing` 起動時延長なし＆`done_with_errors` 停止
- `FurimaServerNative.Claude.cs`:
  - `ApplyApprovalPendingExpiry`: 10 分タイムアウトによる自動 `waiting` 落ちを **廃止**（常に `false`）。
  - `ApplyTokenExpiry`:
    - `approved`（未着手）の期限切れ待機落ちを **廃止**（`job.status != "processing"` なら即 `false`）。
    - **`processing`（処理中）のみ**、起動時延長なしで `jobTokenExpiresAt` 超過を評価し、`results` / `errors` が 0 件であっても **絶対に `waiting` には戻さず**、未処理写真へ期限切れエラーを記録して `done` / `done_with_errors` で終端停止させる。
    - `HandleClaudeApproval` では安全ロック用 `jobToken` を発行し、`jobTokenExpiresAt` は `0`（開始時に 30 分計測開始）として、`HandleGetClaudeJob` では既存のワーカー互換のため未設定時は `now + 30分` を返す。
- `tools/tests/FurimaServerTests.cs`:
  - `approval_pending` / `approved` が時間経過で `waiting` に落ちないテスト
  - `processing` が結果・エラー 0 件で期限切れになった際、`waiting` にならず `done_with_errors` へ終端するテスト
  - `processing` が 30 分経過後に延長されず停止するテスト

### Phase P4 / P5. `/api/claude/revision` 補助情報 ＋ 待ち受け可視化・8080 リトライ ＋ `v1.6 (Build 1)` 統一
- `FurimaServerNative.Core.cs`:
  - `serverBootId`（起動ごとの 8 文字ランダム識別子）を保持。
  - `GET /api/claude/revision` 応答に `executableCount`（`approved` + `processing` 件数）および `serverBootId` を追加（後方互換・個人情報なし）。
  - `TryStartHttpListener` で `listenerMode`（`"all"` / `"localhost"`）を返し、コンソール起動表示と `GET /api/server/status`（`listenerMode`, `actualPort`）へ追加。
  - `Main` で `desiredPort`（8080）確保時のみ短い待機リトライ（300ms × 5 回）を行ってから他ポート探索へ進む。
- `tools/web/dashboard.html`: 設定モーダルにサーバー待ち受け情報（`listenerMode` / `actualPort`）を表示。
- **バージョン統一 (`v1.6 (Build 1)`)**:
  - `app/build.gradle.kts`: `versionName = "1.6"`, `versionCode = 1601`
  - `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`: `v1.6 (Build 1)`
  - `tools/web/dashboard.html`: `v1.6 (Build 1)`
  - `tools/tests/photo-sync-integrity-build17.test.mjs`: バージョン一致テストを `v1.6 (Build 1)` / `versionCode = 1601` に更新。
- 検証: `node --test` 全件パス、Android `gradle testDebugUnitTest` パス、C# テストおよび実機チェック準備完了。

---

## 4. 決定事項サマリ（2026-11-25 確定）
1. **P3 方式**: 案B（自動待機落ち完全廃止）＋ `processing` は**サーバー再起動時の延長なし**（通常処理中の 1 枚ごとの +30 分スライディング延長は維持）で期限切れ時に必ず `done_with_errors` へ停止。
2. **P4 サーバー補助**: 同梱（`executableCount` / `serverBootId`）。
3. **P5 待ち受け可視化＋8080 リトライ**: 同梱（権限不要の範囲）。
4. **信頼済み IP 未設定警告**: 同梱。
5. **バージョン**: 本改修完了をもって **`v1.6 (Build 1)`**（`versionCode = 1601`）へ全箇所統一。
