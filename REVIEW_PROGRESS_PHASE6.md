# フェーズ6 コードレビュー進捗・引き継ぎ台帳 (`REVIEW_PROGRESS_PHASE6.md`)

> 本ファイルは、**フェーズ 6 の実装と並行して行うコードレビューの進捗（サブフェーズ単位）・未解決指摘・次期引き継ぎチェックリスト**を一元記録するための台帳です。
> トークン上限や担当交代で別のレビュー担当エージェントへ引き継ぐ場合も、本ファイルの「1. 現在地」「2. サブフェーズ別レビュー進捗」「4. 次期レビュー担当向け 引き継ぎチェックリスト」を参照すれば直後から再開できます。
>
> - 実装計画書: [PLAN_CLAUDE_SEND_PHASE6.md](PLAN_CLAUDE_SEND_PHASE6.md)
> - 設計協議・指摘履歴: [REVIEW_CLAUDE_SEND_PHASE6.md](REVIEW_CLAUDE_SEND_PHASE6.md)
> - 開発進捗: [PROGRESS.md](PROGRESS.md)

---

## 1. 現在地（最終更新: 2026-11-17 / 基準コミット `2d04a38`）

- **最後にレビューを完了したリモートコミット**:
  - `d627d56`（`6-3` 案2 実装＋特別当直実地確認完了・ステップ1締め / `v1.1 Build 3`）
  - `2d04a38`（`6-4` 先行の小規模ダッシュボード UI 調整 / `v1.1 Build 4`）
- **レビュー完了済み範囲**:
  - 大枠設計協議（`PLAN` §0〜§10 / `REVIEW` §0〜§8）
  - 実装レビュー: **`6-1a` (`Build 14`)・`6-1b` (`Build 15`)・§12是正 (`Build 16`)・ビルドエラー修正 (`Build 17`)・`6-2` (`v1.1 Build 1`)・`6-3` 前半/後半案2 (`v1.1 Build 2〜3`, `d627d56`)・先行UI調整 (`v1.1 Build 4`, `2d04a38`)**
- **現在のステータス（要協議・修正案待ち）**:
  - ステップ1（`6-2`＋`6-3`）の正常系実地動作および `2d04a38`（`v1.1 Build 4`）の UI 調整・JS 構文・3箇所バージョン統一は確認済み。
  - ただし、ステップ1完了時点の**ネットワーク／認証・通知条件・同時実行の総点検（§5）**により、重大・高リスクの潜在バグ **`24-1`〜`24-7`** が判明した。
  - **ステップ2（`6-4` / `v1.2 Build 5`）着工前に、まず `24-1`〜`24-7` に対する実装担当からの修正案コミット → 双方合意 → 修正実装レビューを完了させること。**

---

## 2. サブフェーズ別コードレビュー進捗表

| ステップ | サブフェーズ | バージョン | 事前詳細計画レビュー | 実装後コードレビュー | ビルド/静的検証 | 指摘状態 | 根拠コミット / 参照節 |
|---|---|---|---|---|---|---|---|
| 事前 | 大枠設計協議 (`PLAN` §0〜§10) | — | 完了 | — | — | 合意済 | `80a8ddb` / `REVIEW` §0〜§8 |
| 準備 | **`6-1a`**（`queue`・送信時承認・失効戻し・`claudeRevision`永続化） | `v1.0.0 (Build 14)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4`) | 是正済 (`Build 16`) | `9826052`, `bce7b14`, `62bb8b2`, `13edee9` |
| 準備 | **`6-1b`**（ジョブトークン実行時延長） | `v1.0.0 (Build 15)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4`) | 指摘なし | `9826052`, `6d8fcf4` |
| 是正 | **§12 レビュー修正** | `v1.0.0 (Build 16)` | — | 完了 (`§16`) | 確認済 | 完了 | `62bb8b2`, `13edee9` |
| 修正 | **`Encoder.Quality` 修正＋新体系移行** | `v1.0.0 (Build 17)` | — | 完了 (`§19`) | 成功報告済 | 完了 | `9636ec1`, `3c9dd33` |
| **Step 1** | **`6-2`**（一括承認：最大10件・チェック式・サムネ・帯抑止） | **`v1.1 (Build 1)`** (`1101`) | 完了 (`§16.1`) | 完了 (`§20`＋`§22.1`＋`§5`) | 成功報告済 | **通知・同期バグ新規検出 (`24-2`〜`24-4`)** | `e82a18a`, `51957bb`, `d627d56` |
| **Step 1** | **`6-3` 前半**（公開窓口 `GET /api/claude/revision`＋監視スクリプト） | **`v1.1 (Build 2)`** (`1102`) | 完了 (`§22.2`) | 完了 (`§23`) | 実機疎通確認済 | `22-1`〜`22-2` 反映済 | `6db610c`, `adbe39c`, `5097143` |
| **Step 1** | **`6-3` 後半（案2）**（許可 Tailscale IP 免除＋特別当直検証） | **`v1.1 (Build 3)`** (`1103`) | 完了 (`§23`) | **完了 (`§5`・要修正)** | ローカル実機/PCビルド済 (`d627d56`) | **未解決 `24-1`, `24-5`, `24-6`, `24-7`（修正案待ち）** | `1468b8c`〜`d627d56` |
| **先行UI** | **ダッシュボード小規模 UI 調整**（ヘッダー折り返し・接続ピル・右カラム独立スクロール） | **`v1.1 (Build 4)`** (`1104`) | — | **完了 (`§5.3`)** | JS構文 `node --check` 通過 (`2d04a38`) | **指摘なし** | `2d04a38` (`PLAN` §23) |
| **Step 2** | **`6-4`**（出品ステータス拡張：`DRAFT_DONE` / `SHIPPING`）※単独 | `v1.2 (Build 5)` | **未（詳細化前・レビュー必須）** | **未着手（実装後レビュー必須）** | 未 | — | `PLAN` §16, §23 |
| **Step 3** | **`6-5`〜`6-7`**（加工ラベル・ジョブ一覧・右カラム再編） | `v1.3 (Build 6〜8)` | 未（詳細化待ち） | 未（`6-5`・`6-7` 完了時レビュー必須） | 未 | — | `PLAN` §16, §23 |

---

## 3. 指摘事項・追跡台帳

| ID | 対象 | 区分 | 内容要約 | 計画書反映 | 実装反映・確認状態 |
|---|---|---|---|---|---|
| `10-1`〜`10-5` | `6-1a`/`6-1b` | 事前設計 | `move` 許可条件統一、`CreateJob` 原子化 等 | 反映済 (`PLAN` §12) | **解消・確認済** (`bce7b14`, `6d8fcf4`) |
| `12-1`〜`12-3` | `Build 14` | 実装修正 | 件数集計、`#viewer-claude-queue`、`ClaudeSendDialog` スクロール | — | **解消・確認済** (`62bb8b2`) |
| `16-1`〜`16-3` | `6-2` Android | 事前設計 | `itemUuid` 追加、`uncheckedIds`＋`displayedIds`限定、帯タップ／閉じる干渉 等 | 反映済 (`PLAN` §14) | **解消・確認済** (`e82a18a`) |
| `20-1`〜`20-3` | `6-2` Android | バグ/改善 | 台帳リンク復旧、`File.exists()` 実在確認、ハートビート抑制、タイトル省略 | — | **解消・確認済** (`1294100`, `51957bb`) |
| `22-1`〜`22-2` | `6-3` 前半 | 事前補記 | bash 8進数回避（`date +%-H`）、正整数検証、0件無言終了、1件失効時継続 | 反映済 (`PLAN` §18.1) | **スクリプト側反映済 (`adbe39c`)** |
| `23-1`〜`23-2` | `6-3` 案2 | 事前条件 | 厳格パス一致＋IP正規化、`StoreConfig` トップレベル保存＋専用API分離 | 反映済 (`PLAN` §20) | **基本実装済 (`Build 3`)・細部要補強 (`24-5`)** |
| **`24-1`** | `FurimaServerNative.cs` | **重大・セキュリティ** | `StartCloudflared` (`127.0.0.1`) 経由のアクセスが `isLoopback` 無条件許可を踏み、Bearer・`trustedMuseIp` 制限を全APIで素通りする構造 | — | **未解決（修正案待ち）** |
| **`24-2`** | `MainActivity.kt` / `FleaMarketViewModel.kt` | **重大・通知UI** | 完了通知の「いったん閉じる」がID集合管理されていないため `claudeRevision` 更新ごとに強制復活＋承認待ち「いったん閉じる」／帯抑止中に完了通知が長期マスクされる問題 | — | **未解決（修正案待ち）** |
| **`24-3`** | `FleaMarketViewModel.kt` | **重大・通知操作** | `respondToClaudeApprovals` 末尾で途中失敗 (`approveFailed`) や11件以上残存時も無条件で `_claudeApprovalPeekRequested = false` にし、確認中ダイアログを閉じてしまう問題 | — | **未解決（修正案待ち）** |
| **`24-4`** | `FleaMarketViewModel.kt` | **重大・同期/通知遅延** | `heartbeatLoop` が単一コルーチン直列実行のため、写真同期 (`syncDeltaToPcServer`/`fetchServerItems`) 中に `claudeRevision` 検知が数十秒〜数分停止＋応答後の `lastKnownClaudeRevision` 未更新 | — | **未解決（修正案待ち）** |
| **`24-5`** | `FurimaServerNative.cs` / `SyncStore.cs` | **高・IP検証/永続化** | `TryNormalizeTrustedIp` が IPv4 省略表記（`"100"` 等）やブロードキャストを受理、不正JSONで 500 応答、`SetTrustedMuseIp` が書込失敗を握り潰し 200 を返す問題 | — | **未解決（修正案待ち）** |
| **`24-6`** | `FurimaServerNative.cs` / `ClaudeJobStore.cs` | **高・同時実行整合** | `HandlePostClaudeJobResult`/`Error` の検証がロック外かつ、`HandleCancelClaudeJob`/`HandleCompleteClaudeJob` が `claudeResultWriteLock` 非取得で競合（取消後の結果書込など） | — | **未解決（修正案待ち）** |
| **`24-7`** | `PcServerClient.kt` / `FurimaServerNative.cs` | **中・同期整合** | スマホ未送信変更あり (`isDirty`) かつ端末時計が PC より進んでいる場合、返却済み `_ai` 写真の同期マージ全体がスキップされるリスク | — | **未解決（修正案待ち）** |
| `24-8` | Android / Server / 運用 | 軽微・運用観察 | URL切替時のリビジョン・通知リセット漏れ、単一IPv4制約、一括失効時の連続保存、10〜22時自動wake実地未目撃 | — | **改善・継続観察** |

---

## 4. 次期レビュー担当向け 引き継ぎチェックリスト（絶対条件＆検証ポイント）

### 4.0 レビュー・運用上の絶対条件（引き継ぎ必須）
1. **スコープ境界（`PLAN` §0 / §6）**: `LocalWebServer.kt` / `WebDashboardHtml.kt`（スマホ緊急WebUI）の既存セキュリティ・差分ハッシュ問題はユーザー許可によりフェーズ 6 スコープ外。**バージョン表記 `v1.x (Build N)` の3箇所同時更新チェック以外の指摘は絶対に出さない**こと。
2. **共有ファイル競合防止**: 追記・push 前に必ず `git fetch origin && git log HEAD..origin/main` で最新を取り込むこと。
3. **選択的ステージング厳守**: `tools/build-exe.bat` に CRLF 差分が出続けるため `git add -A` 禁止。必ずファイル指定で `git add` すること。
4. **C# 5 互換維持**: `tools/*.cs` は `.NET Framework v4.0.30319\csc.exe` ビルドのため、補間文字列 `$""`・null条件演算子 `?.`・`nameof(...)` 等の混入がないか常に確認すること（`DashboardHtml.cs` の JS リテラル内文字列は除く）。`IPAddress.IsIPv4MappedToIPv6` / `MapToIPv4()` のビルド可否も注意。
5. **テストビルド方針**: テストビルドはユーザー指示時またはステップ区切りを中心とし、中間レビューではコード精査＋軽量静的チェック（C# 5 構文・`DashboardHtml.cs` verbatim `""` バランス・抽出 JS の `node --check` 等）を優先する。
6. **交互レビュー・ループ運用**: 実装担当から修正案コミットが来たら仕様妥当性をレビューして本ファイルへ追記＋コミットし、双方合意後に実装再開。サブフェーズ等のレビュー終了単位で必ず本ファイルを更新・コミットすること。

### 4.1 次の最優先レビュー対象: `24-1`〜`24-7` の修正案および修正実装
1. **ステップ2（`6-4`）へ進む前に、`24-1`〜`24-7` に対する実装担当の修正案コミットを確認する。**
2. 修正実装コミット時は以下を重点確認する。
   - `24-1`: `cloudflared` の起動制御またはトンネル経由ヘッダ有無による `isLoopback` 除外が安全に成立しているか。
   - `24-2`〜`24-4`: `ClaudeCompletionDialog` の `_dismissedClaudeCompletionIds` 管理・再表示制御、`respondToClaudeApprovals` 失敗時/残件時の一時確認フラグ保持、`claudeRevision` ポーリングと重い同期の分離。
   - `24-5`〜`24-6`: IPv4 4オクテット厳格検証・不正JSON時 400・`SetTrustedMuseIp` 保存成否伝播、`claudeResultWriteLock` とジョブ状態遷移の直列化。
   - バージョン表記3箇所同時更新（`PROGRESS.md` 基本原則 6 準拠）。

### 4.2 今後のサブフェーズ事前／実装レビュー重点ポイント
- **`6-4` (`v1.2 Build 5` 単独ステップ) ※詳細化後・実装後ともにレビュー必須**:
  - `DRAFT_DONE`・`SHIPPING` 追加に伴う Android/PC 全層のステータス分岐網羅（既存の `else -> "出品中"` フォールバック廃止）。
  - `{SOLD, SHIPPING}` 両方で `soldPrice` / `soldAt` 保持・売上集計・CSV 出力の整合、`getItemSortPriority` 順位、まとめ親子整合。
- **`6-5`〜`6-7` (`v1.3 Build 6〜8`) ※`6-5`・`6-7` 完了時レビュー必須**:
  - `6-5`/`6-6`: 加工ラベルの寿命管理、`errorDismissed` 集計と30日削除条件の整合、待機タブ/依頼中タブ・編集モーダル連携。
  - `6-7`: `DashboardHtml.cs` ミューズカラム新設・右カラム再編時の既存 DOM/JS との非干渉。

---

## 5. ステップ1完了時点（`d627d56` / `v1.1 Build 3`）＆ `Build 4`（`2d04a38`）総括レビュー・実装担当への依頼（2026-11-17）

### 5.1 総評
手動巡回（ジョブ取得→写真取得→生成→商品フォルダへの `_ai` 返却→`done` 完了）の正常系ルートおよび `v1.1 Build 3` の IP 免除境界（対象6本＝200、それ以外＝401）自体は計画どおり機能しています。  
一方で、**例外系・複数操作が重なった場合の通知条件・ネットワーク認証境界・並行実行ロック**を中心に、実運用前に潰しておくべき重大／高リスク指摘が7点（`24-1`〜`24-7`）存在します。

---

### 5.2 詳細指摘・修正提案（`24-1`〜`24-8`）

#### 【重大 `24-1`】`cloudflared` 起動時、`isLoopback` 無条件許可による全APIの認証素通りリスク
- **箇所**: `tools/FurimaServerNative.cs`（L421, L555 `StartCloudflared` / L1340-1344 `AuthenticateRequest`）
- **事象**: `StartCloudflared` は `cloudflared.exe tunnel --url http://127.0.0.1:<port>` で起動し、トンネル経由のリクエストもサーバーから見ると `req.RemoteEndPoint.Address == 127.0.0.1` になります。一方 `AuthenticateRequest` は冒頭で `if (isLoopback) return true;` としているため、**`cloudflared.exe` が実行ディレクトリに存在して起動した場合、公開 `trycloudflare.com` URL 経由の外部アクセスすべてで `Bearer` 認証および `trustedMuseIp` 制限が完全にバイパスされます**（`/api/server/trusted-muse-ip` 読み書き、ジョブ作成/承認、商品読み書きを含む）。
- **修正案**:
  - トンネルを今後一切使わないなら `cloudflared` 起動を無効化（または設定スイッチ化）する。
  - 併用・起動可能性を残すなら、`isLoopback` 免除の条件からトンネル経由の転送ヘッダ（`Cf-Connecting-Ip`・`X-Forwarded-For`・`Cf-Ray` 等）を含むリクエストを明確に除外し、`Bearer` トークン必須へ落とす。

#### 【重大 `24-2`】完了通知の「いったん閉じる」再ポップアップループ＆長期マスク問題
- **箇所**: `MainActivity.kt`（L142）、`FleaMarketViewModel.kt`（L905-907 `dismissClaudeCompletionForNow`, L950-952, L1076）
- **事象**:
  1. **強制再ポップアップ**: `dismissClaudeCompletionForNow` が `_unackedClaudeCompletions` から対象をフィルタ除外するだけで、承認側（`_dismissedClaudeApprovalIds`）のような非表示ID集合を持っていません。そのため、別ジョブの作成・移動・完了・確認等で `claudeRevision` が1つ動くたび `refreshClaudeJobStates` が走り、**閉じたはずの未確認完了ダイアログが何度でも強制再出現します**（1件「確認」を押した直後の L1076 再取得でも即復活します）。逆に他ジョブに変化が無ければスマホから再表示する手段もありません。
  2. **承認待ちによる長期マスク**: `MainActivity.kt:142` が `!showClaudeApprovalDialog` ではなく `if (pendingClaudeApprovals.isEmpty())` を見ているため、編集画面での帯抑止中や「いったん閉じる」で承認ダイアログを隠している間も、最大10分間（承認待ちが失効するまで）完了通知が一切表示されません。
- **修正案**:
  - 完了通知用に `_dismissedClaudeCompletionIds: StateFlow<Set<String>>`（または「閉じた後に新しく届いた完了IDが出たら再表示、確認済みは掃除」）を導入し、他ジョブの `claudeRevision` 更新で既閉ジョブが復活しないよう分離する。
  - `MainActivity.kt:142` の条件を `!showClaudeApprovalDialog`（承認ダイアログと画面上で重ならない時）に改める。

#### 【重大 `24-3`】一括承認の途中失敗時／11件以上の残存時に、確認ダイアログが即閉じてしまう
- **箇所**: `FleaMarketViewModel.kt`（L1036-1037 `respondToClaudeApprovals`）
- **事象**: 応答処理の最後に、途中エラー発生時（`approveFailed == true`）や未表示の11件目以降が残っている場合でも無条件で `_claudeApprovalPeekRequested.value = false`・`_dismissedClaudeApprovalIds.value = emptySet()` を実行しています。編集画面の帯から確認ダイアログを開いて実行した場合、通信エラーで途中停止してもダイアログが即座に閉じて帯へ戻ってしまい、続けてリトライや残件処理がしづらくなります。
- **修正案**: `!approveFailed && moveFailedCount == 0 && _pendingClaudeApprovals.value.isEmpty()`（あるいはエラー無しかつ操作完了）のときだけ `_claudeApprovalPeekRequested.value = false` に戻す。失敗時はダイアログを開いたまま残して残件の即時再試行を許容する。

#### 【重大 `24-4`】`startHeartbeatLoop` の単一ループ直列実行により、写真同期中に承認・完了通知が停止
- **箇所**: `FleaMarketViewModel.kt`（L1096-1241）
- **事象**: `heartbeatLoop` が1本のコルーチン内で `sendHeartbeatWithRevision` → `syncDeltaToPcServer` / `fetchServerItems`（`L1111-1219`）を同期完了させてから `L1224` の第3分岐（`claudeRevision` チェック）へ到達します。写真枚数が多い商品の同期中に数十秒〜数分ネットワーク転送が走ると、その間は PC 発の承認リクエストや完了通知の検知が丸ごとブロックされます。また、`respondToClaudeApprovals`（L1033）や `acknowledgeClaudeCompletion`（L1076）で `refreshClaudeJobStates` を呼んだ直後に `lastKnownClaudeRevision` を更新していないため、直後のループで毎回1回冗長な `GET /api/claude/jobs` が発生しています。
- **修正案**: 第3分岐（`claudeRevision` ポーリング・`refreshClaudeJobStates`）を `syncMutex` を握る商品・写真同期ループから独立コルーチンまたは非直列ディスパッチへ分離し、さらに `refreshClaudeJobStates` 成功時に最新の `claudeRevision` で `lastKnownClaudeRevision` を更新する（`fetchClaudeJobs` 応答の `claudeRevision` を返す）。

#### 【高 `24-5`】`TryNormalizeTrustedIp` の IPv4 省略表記受理・不正JSON時 500・保存失敗黙殺
- **箇所**: `tools/FurimaServerNative.cs`（L1425-1470）、`tools/SyncStore.cs`（L349-376 `SetTrustedMuseIp`）
- **事象**:
  1. .NET の `IPAddress.TryParse` は `"100"`（→ `0.0.0.100`）や `"192.168.1"`（→ `192.168.0.1`）といった省略形も正常パースするため、誤入力保存で意図しない IP が `200 OK` で登録されます（`IPAddress.Broadcast` `255.255.255.255` 等も弾かれていません）。
  2. `HandleUpdateTrustedMuseIp`（L1452-1453）は不正 JSON や文字列以外の型を受け取ると `400` ではなく `500` を返します。
  3. `SyncStore.SetTrustedMuseIp`（L366-374）が `File.WriteAllText` の例外を `catch` でログ出力のみして握り潰すため、ディスク書込失敗時も API は `200 OK` を返し、再起動で設定が巻き戻ります。
- **修正案**: IPv4 は必ず4オクテット（`a.b.c.d`）・非Loopback・非Unspecified・非Broadcast・非Multicast を検証（または IPv6 許容時も同様に正規形/特別アドレス除外）、`JObject.Parse` 等の入力不正は `400` 応答、`SetTrustedMuseIp` は `bool` 戻り値または例外再スローで `500` 失敗応答を返す。

#### 【高 `24-6`】`HandleCancelClaudeJob` / `HandleCompleteClaudeJob` と `HandlePostClaudeJobResult` の排他不備
- **箇所**: `tools/FurimaServerNative.cs`（L5477-5704, L5728-5830, L5839-5902, L6070-6102）、`tools/ClaudeJobStore.cs`（L390-415）
- **事象**:
  1. `HandlePostClaudeJobResult` / `HandlePostClaudeJobError` は `ApplyTokenExpiry` と `ValidateClaudeJobToken` を `claudeResultWriteLock` の外で評価しており、ロック取得後の `fresh` 参照には `ApplyTokenExpiry(fresh)` を再適用していません。
  2. `HandleCancelClaudeJob`（`approved` を取消可能）と `HandleCompleteClaudeJob` が **`claudeResultWriteLock` を取得せず**、かつ `HandlePostClaudeJobResult` L5684 の `MutateJob` ラムダ内に `j.status` チェックがありません。結果として、Muse が結果アップロード中に PC 側で取消が実行されると、`cancelled` に変わったジョブへ `File.WriteAllBytes` + `_ai` 商品登録 + `results` 追記が割り込む競合（または `HandleCompleteClaudeJob` と期限切れ評価のすれ違い上書き）が起き得ます。
- **修正案**: `HandleCompleteClaudeJob` および `HandleCancelClaudeJob` も `claudeResultWriteLock` で直列化し、さらに `HandlePostClaudeJobResult` 内の `MutateJob` コールバック先頭でも `j.status == "approved" || j.status == "processing"` を確認してから `results` 追記（不一致なら書込拒否／ファイル掃除）し、`HandlePostClaudeJobResult`/`Error` のロック内先頭でも期限切れを再検証する。

#### 【中 `24-7`】スマホ `isDirty` + 時刻ズレ（clock skew）での `_ai` 写真反映スキップリスク
- **箇所**: `PcServerClient.kt`（L990-994, L1113-1133）、`tools/FurimaServerNative.cs`（L1187 `CommitAutoImportedPhotosBatch`）
- **事象**: PC 側で `_ai` 結果が返却されると PC 時刻で `item["updatedAt"] = nowMs` が更新されますが、スマホ側 `mergeServerItemsFromJson`（`L990-994`）は `existing.item.isDirty && existing.item.updatedAt >= serverUpdatedAt` の場合、その商品のマージ全体（写真配列の同期含む）をスキップします。未送信のローカル変更がある状態でスマホ時計が PC より数秒〜数分進んでいると、PC 側に届いた `_ai` 写真がスマホへ降りてこない状態が発生し得ます。
- **修正案**: サーバー側の写真ハッシュ集合にローカル未保有の新規ハッシュ（`_ai` 等）が含まれているケースでは、テキスト等の未送信項目を維持しつつ写真差分・マージまたは再同期トリガーが漏れない導線を確認・補強する。

#### 【軽微・運用確認 `24-8`】その他の気づき・継続観察事項
- **接続先変更時のリセット漏れ（`FleaMarketViewModel.kt:1301-1309`）**: `updatePcServerUrl` で別サーバーへ切り替えた際、`lastKnownDataRevision` / `lastKnownSettingsRevision` / `lastKnownClaudeRevision` や通知 StateFlow がリセットされません。切替時はこれらを初期化する方が安全です。
- **`trustedMuseIp` の単一 IPv4 前提**: Tailscale IPv6（`fd7a:...`）経路や VM 再構築による IP 変更時は即 401（自動巡回停止・例外通知発報）となるため、運用前提としてドキュメント上の注意喚起を保つこと。
- **`HandleGetClaudeJobs` の連続ディスク書込（`L4890-4895`）**: 複数ジョブが同時に期限切れになった際、1件ごとに `MutateJob` + `BumpClaudeRevision` で最大 2N 回保存します（負荷は軽微だが将来改善余地あり）。
- **実地確認の残観察点（`PLAN` §22）**: 10:00〜22:00 の通常稼働時間帯内における見張りフックの初回自動 wake 発火は未目撃のため、時間帯内の最初の巡回で発火・処理・無言完了を観察・記録すること。

---

### 5.3 先行 UI 調整 `2d04a38`（`v1.1 Build 4`）の検証結果
- `tools/DashboardHtml.cs`（ヘッダー `min-h-10 py-1 flex-wrap`、接続ステータス `#stat-connection-status` の検索欄横移動＆明色ピル化、`updateStatusBannerFromData` のクラス同期、`<aside>` の `lg:sticky` 独立スクロール）を確認。
- 抽出 JS の `node --check` はエラー 0 で通過、3箇所のバージョン表記（`app/build.gradle.kts` `1104` / `tools/DashboardHtml.cs` `v1.1 (Build 4)` / `WebDashboardHtml.kt` `v1.1 (Build 4)`）の一致を確認。本変更単体に問題なし。

---

### 5.4 【実装担当への依頼】今後の必須コードレビュー挟み込みポイント
変更規模・依存範囲が大きいため、以下のタイミングでは**必ず次の実装へ進む前にレビューを挟むこと**を依頼します。

1. **【必須ポイント1】本レビュー指摘 `24-1`〜`24-7` の修正案合意 ＋ 修正実装完了時（ステップ2 `6-4` 着手前）**
   - まずは `24-1`〜`24-7` についてどこをどう直すかの修正案（設計判断が必要な箇所含む）をコミットし、レビュー合意後に修正実装・再チェックを完了してから `6-4` に進むこと。
2. **【必須ポイント2】ステップ2 `6-4`（出品ステータス拡張 `DRAFT_DONE` / `SHIPPING`）の 2段階レビュー**
   - DB・同期 DTO・PC/Android 全画面のステータス分岐・売上集計・CSV・ソート順位へ横断的に波及するため、**①サブフェーズ詳細化直後（コード着手前）** と **②実装完了直後（ステップ3着手前）** の2回、必ずコードレビューを挟むこと。
3. **【必須ポイント3】ステップ3 `6-5` 完了時 および `6-7` 完了時**
   - `6-5`（加工ラベル追加と寿命連動・一括保存影響）完了時点、および `6-7`（`DashboardHtml.cs` 右カラム大改修・既存 DOM/JS 干渉リスク）完了時点で必ず中間コードレビューを挟むこと。


---

## 6. 実装担当エージェント回答（§5 総括レビューへの回答）（2026-11-17）

- 指摘 `24-1`〜`24-8` は対象コードで全件裏取りし、**全件同意**した。詳細な回答は `REVIEW_CLAUDE_SEND_PHASE6.md` §25 に記録。
- 補正・具体化は2点のみ: (1) `24-1` の修正案(a)（cloudflared 無効化）はスマホの唯一の経路であるため不採用とし、転送ヘッダによる免除除外（案(b)）を採用。(2) `24-7` は「写真だけの加算マージ（未知ハッシュの追加のみ・削除/上書きなし）」として設計を具体化した。
- §5.4 の必須レビューポイント3件にも同意。修正は本回答への確認後、一括で `v1.1 (Build 5)` として実装し、完了時点で再チェックを依頼する。以後の採番は繰り下げ（6-4 = v1.2 Build 6、6-5〜6-7 = v1.3 Build 7〜9）。
