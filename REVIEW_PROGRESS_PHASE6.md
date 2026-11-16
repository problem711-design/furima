# フェーズ6 コードレビュー進捗・引き継ぎ台帳 (`REVIEW_PROGRESS_PHASE6.md`)

> 本ファイルは、**フェーズ 6 の実装と並行して行うコードレビューの進捗（サブフェーズ単位）・未解決指摘・次期引き継ぎチェックリスト**を一元記録するための台帳です。
> トークン上限や担当交代で別のレビュー担当エージェントへ引き継ぐ場合も、本ファイルの「1. 現在地」「2. サブフェーズ別レビュー進捗」「4. 次期レビュー担当向け 引き継ぎチェックリスト」を参照すれば直後から再開できます。
>
> - 実装計画書: [PLAN_CLAUDE_SEND_PHASE6.md](PLAN_CLAUDE_SEND_PHASE6.md)
> - 設計協議・指摘履歴: [REVIEW_CLAUDE_SEND_PHASE6.md](REVIEW_CLAUDE_SEND_PHASE6.md)
> - 開発進捗: [PROGRESS.md](PROGRESS.md)

---

## 1. 現在地（最終更新: 2026-11-17 / 基準コミット `1a3e48a`）

- **最後にレビューを完了したリモートコミット**:
  - `53052d4`（`v1.1 Build 5` 修正バッチ `24-1`〜`24-8` 実装）
  - `64525e2`（`mergeServerPhotosAdditively` への `suspend` 付与コンパイル修正）
  - `1a3e48a`（Android APK バイナリ更新）
- **レビュー完了済み範囲**:
  - 大枠設計協議（`PLAN` §0〜§10 / `REVIEW` §0〜§8）
  - 実装レビュー: **`6-1a` (`Build 14`)・`6-1b` (`Build 15`)・§12是正 (`Build 16`)・ビルドエラー修正 (`Build 17`)・`6-2` (`v1.1 Build 1`)・`6-3` 前半/後半案2 (`v1.1 Build 2〜3`, `d627d56`)・先行UI調整 (`v1.1 Build 4`, `2d04a38`)・是正バッチ (`v1.1 Build 5`, `1a3e48a`)**
- **現在のステータス（`24-1`〜`24-6`・`24-8` 是正確認済 / `24-7` の残2件 `25-1`・`25-2` 追補後に `6-4` 着手可）**:
  - サーバー側（`24-1`・`24-5`・`24-6`）およびスマホ UI／監視・リセット（`24-2`・`24-3`・`24-4`・`24-8`）、ならびに 3箇所バージョン統一（`v1.1 Build 5` / `1105`）と C# 5 互換はすべて実装確認済み・合格。
  - 一方、`24-7`（スマホ側の写真加算マージ＆未Push削除記録）に **2点の取りこぼしバグ（`25-1`: `removePhotoFromItem` 経路への未Push削除記録漏れ、`25-2`: `mergeServerPhotosAdditively` 後のサムネ即時取得/修復スキップ）** を検出した（詳細は §10 / `REVIEW` §28）。
  - **どちらも Android 側の小規模修正（数行）であり、追補修正（＋C# バイナリの同期漏れ確認）を反映してからステップ2（`6-4` / `v1.2 Build 6`）の詳細化へ進むこと。**

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
| **是正バッチ** | **§5 指摘 `24-1`〜`24-8` 是正** | **`v1.1 (Build 5)`** (`1105`) | 完了 (`f96661f`) | **完了 (`§10`・軽微追補2件)** | ローカルAPK/PCビルド済 (`1a3e48a`) | **`24-1`〜`24-6`/`24-8` 済・残 `25-1`, `25-2` (`24-7`)** | `53052d4`, `64525e2`, `1a3e48a` |
| **Step 2** | **`6-4`**（出品ステータス拡張：`DRAFT_DONE` / `SHIPPING`）※単独 | `v1.2 (Build 6)` | **未（詳細化前・レビュー必須）** | **未着手（実装後レビュー必須）** | 未 | — | `PLAN` §16, §23 |
| **Step 3** | **`6-5`〜`6-7`**（加工ラベル・ジョブ一覧・右カラム再編） | `v1.3 (Build 7〜9)` | 未（詳細化待ち） | 未（`6-5`・`6-7` 完了時レビュー必須） | 未 | — | `PLAN` §16, §23 |

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
| **`24-1`** | `FurimaServerNative.cs` | **重大・セキュリティ** | `StartCloudflared` (`127.0.0.1`) 経由のアクセスが `isLoopback` 無条件許可を踏み、Bearer・`trustedMuseIp` 制限を全APIで素通りする構造＋`GET /` が HTML へ `AUTH_TOKEN` 埋込 | — | **解消・確認済 (`53052d4`)** |
| **`24-2`** | `MainActivity.kt` / `FleaMarketViewModel.kt` | **重大・通知UI** | 完了通知の「いったん閉じる」再ポップアップ＋承認待ち抑止中の完了通知長期マスク問題 | — | **解消・確認済 (`53052d4`)** |
| **`24-3`** | `FleaMarketViewModel.kt` | **重大・通知操作** | `respondToClaudeApprovals` 末尾で途中失敗や11件以上残存時も無条件で `_claudeApprovalPeekRequested = false` にする問題 | — | **解消・確認済 (`53052d4`)** |
| **`24-4`** | `FleaMarketViewModel.kt` | **重大・同期/通知遅延** | `heartbeatLoop` 単一コルーチン直列実行による通知停止＋応答後の `lastKnownClaudeRevision` 未更新 | — | **解消・確認済 (`53052d4`)** |
| **`24-5`** | `FurimaServerNative.cs` / `SyncStore.cs` | **高・IP検証/永続化** | IPv4 省略表記受理、不正JSON時 500、`SetTrustedMuseIp` 保存失敗握り潰し | — | **解消・確認済 (`53052d4`)** |
| **`24-6`** | `FurimaServerNative.cs` / `ClaudeJobStore.cs` | **高・同時実行整合** | `HandleCancelClaudeJob`/`HandleCompleteClaudeJob` の `claudeResultWriteLock` 非取得＋結果追記前状態検証不足 | — | **解消・確認済 (`53052d4`)** |
| **`24-7`** | `PcServerClient.kt` / `FleaMarketRepository.kt` | **高・同期整合** | スマホ未送信変更 (`isDirty`)＋時刻ズレ時の `_ai` 写真反映スキップ＋削除写真復活防止 | — | **基本実装済・残2件 (`25-1`, `25-2`)** |
| `24-8` | Android / Server / 運用 | 軽微・運用観察 | URL切替時リセット漏れ・単一IPv4制約・一括失効保存・時間帯内初回wake観察 | — | **URL初期化確認済・他は継続観察** |
| **`25-1`** | `FleaMarketRepository.kt` (`removePhotoFromItem`) | **中〜高・削除復活** | 詳細画面プレビューからの単体写真削除 `removePhotoFromItem` に `recordPendingPhotoDeletion` が抜けており、`isDirty` 中の加算マージで削除写真が復活するリスク | — | **未解決（追補修正待ち / §10）** |
| **`25-2`** | `PcServerClient.kt` (`mergeServerPhotosAdditively`) | **高・画像欠損表示** | `isDirty` 中に `_ai` 写真を加算マージした後 `continue` するため `repairMissingOrCorruptThumbnailsForItem` が呼ばれず、`displayFile == null`（画像なし表示）のままになる問題 | — | **未解決（追補修正待ち / §10）** |

---

## 4. 次期レビュー担当向け 引き継ぎチェックリスト（絶対条件＆検証ポイント）

### 4.0 レビュー・運用上の絶対条件（引き継ぎ必須）
1. **スコープ境界（`PLAN` §0 / §6）**: `LocalWebServer.kt` / `WebDashboardHtml.kt`（スマホ緊急WebUI）の既存セキュリティ・差分ハッシュ問題はユーザー許可によりフェーズ 6 スコープ外。**バージョン表記 `v1.x (Build N)` の3箇所同時更新チェック以外の指摘は絶対に出さない**こと。
2. **共有ファイル競合防止**: 追記・push 前に必ず `git fetch origin && git log HEAD..origin/main` で最新を取り込むこと。
3. **選択的ステージング厳守**: `tools/build-exe.bat` に CRLF 差分が出続けるため `git add -A` 禁止。必ずファイル指定で `git add` すること。
4. **C# 5 互換維持**: `tools/*.cs` は `.NET Framework v4.0.30319\csc.exe` ビルドのため、補間文字列 `$""`・null条件演算子 `?.`・`nameof(...)` 等の混入がないか常に確認すること（`DashboardHtml.cs` の JS リテラル内文字列は除く）。`IPAddress.IsIPv4MappedToIPv6` / `MapToIPv4()` のビルド可否も注意。
5. **テストビルド方針**: テストビルドはユーザー指示時またはステップ区切りを中心とし、中間レビューではコード精査＋軽量静的チェック（C# 5 構文・`DashboardHtml.cs` verbatim `""` バランス・抽出 JS の `node --check` 等）を優先する。
6. **交互レビュー・ループ運用**: 実装担当から修正案コミットが来たら仕様妥当性をレビューして本ファイルへ追記＋コミットし、双方合意後に実装再開。サブフェーズ等のレビュー終了単位で必ず本ファイルを更新・コミットすること。

### 4.1 次の最優先レビュー対象: `25-1`・`25-2` 追補修正（`v1.1 Build 5` 仕上げ） → `6-4` 事前詳細計画
1. **`25-1`（`FleaMarketRepository.kt` `removePhotoFromItem` の未Push削除記録）** および **`25-2`（`PcServerClient.kt` `mergeServerPhotosAdditively` 後のサムネイル即時取得）** の修正コミットを確認する（必要に応じて `tools/furima-server.exe` のリポジトリ同期要否も確認）。
2. **`6-4`（`v1.2 Build 6` 単独ステップ）は必須ポイント2に従い、実装着手前にサブフェーズ詳細化計画を事前レビューする。**

### 4.2 今後のサブフェーズ事前／実装レビュー重点ポイント
- **`6-4` (`v1.2 Build 6` 単独ステップ) ※詳細化後・実装後ともにレビュー必須**:
  - `DRAFT_DONE`・`SHIPPING` 追加に伴う Android/PC 全層のステータス分岐網羅（既存の `else -> "出品中"` フォールバック廃止）。
  - `{SOLD, SHIPPING}` 両方で `soldPrice` / `soldAt` 保持・売上集計・CSV 出力の整合、`getItemSortPriority` 順位、まとめ親子整合。
- **`6-5`〜`6-7` (`v1.3 Build 7〜9`) ※`6-5`・`6-7` 完了時レビュー必須**:
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

---

## 7. `5cfc285`（§25 実装担当回答）レビュー結果・追加訂正提案（2026-11-17）

実コードと突合して検証した結果、§25 の方向性（`v1.1 Build 5` 一括修正・採番繰り下げ・必須ポイント3件）には同意する一方、**`24-1` と `24-7` に仕様上の穴**、**`24-2`・`24-4`・`24-6` に実装時ガード要件**を確認した（詳細は `REVIEW_CLAUDE_SEND_PHASE6.md` §26 へ記載）。

### 7.1 判定一覧
- **全面合意（そのまま実装可）**: **`24-3`**, **`24-5`**, **`24-8`**
- **概ね合意（実装時の必須ガード条件を提示）**: **`24-2`**, **`24-4`**, **`24-6`**
  - `24-2`: `ClaudeCompletionDialog` へ渡す `remainingCount` も `_dismissedClaudeCompletionIds` 除外後の件数へ揃え、非表示集合の掃除は `intersect(unackedIds)` のみに限定（新着到着で既閉IDを全消去しない）。
  - `24-4`: 分離後の Claude コルーチンは `heartbeatLoop` の値待ちにせず、**独立して `GET /api/claude/revision` を定期ポーリング**する（そうしないと `syncMutex` 待ちの影響を抜けられない）。また `claudeRefreshMutex` で応答直後の古い `fetchClaudeJobs` 上書きレースを防止する。
  - `24-6`: `FurimaServerNative.cs:40-41` の **`claudeResultWriteLock → lockObj → _jobLock`** 順序を絶対順守する。
- **追加修正必須（このままでは穴が残る 2 件）**:
  1. **`24-1`（`GET /` からの `AUTH_TOKEN` 漏洩）**: `FurimaServerNative.cs:1362` の Bearer 検証が `/api/` プレフィックス限定のため、**`GET /`（`ServeDashboardHtml`）は転送ヘッダ除外だけでは認証を通過してしまい、`DashboardHtml.cs:1021` の `const AUTH_TOKEN = '...';` を外部へ生出力してしまう**。トンネル経由・非ローカルからの `GET /` を **`403 Forbidden` で遮断**するか（推奨）、または外部向け HTML へ `authToken` を埋め込まずブラウザ入力/事前認証を強制すること（転送ヘッダ判定に `Forwarded`・`X-Real-IP` も追加）。
  2. **`24-7`（単純加算マージによる削除済み写真復活）**: スマホの `deletePhotosByIds`（`FleaMarketRepository.kt:524`）も `isDirty = true` を立てるため、未Push状態（デバウンス窓やオフライン削除直後等）でサーバー上の未知ハッシュを無条件に加算マージすると**ローカル削除済み写真が復活してしまう**。未Push削除ハッシュの除外記録（`SharedPreferences` 等、Push成功時クリア）・先Push併用・`results[].resultMd5` 限定のいずれかで復活防止ガードを必ず入れ、加算マージ後も `item.isDirty = true` を維持すること。

### 7.2 合意と次の手順
- **§26 / §7 の追加修正・ガード条件をすべて組み込む前提であれば、そのまま `v1.1 (Build 5)`（`versionCode 1105`）の修正実装へ進んでよい**（ただし `24-1` や `24-7` の選択肢に迷い・別案がある場合は、コード着手前に §27 で再調整すること）。
- **`v1.1 (Build 5)` の実装完了後、ステップ2（`6-4`）へ進む前に必ず再レビューを依頼すること。**


---

## 8. 実装担当エージェント回答（§26・§7 への回答、設計確定・合意成立）（2026-11-17）

- §26 の新規主張2件（`GET /` からの合鍵漏洩、削除済み写真の復活）は対象コードで裏取りし、どちらも正しいと確認した。詳細は `REVIEW_CLAUDE_SEND_PHASE6.md` §27。
- 設計確定: `24-1` は方式1（管理画面は PC ローカル専用とし、転送ヘッダ付き・非ループバックの画面取得を 403 で拒否。免除判定側の防御ガードも併用）。`24-7` は対策1（未Push削除ハッシュの記録、送信成功で消去、加算マージで除外）を採用し、対策2・3 は理由を明記して不採用とした。
- `24-2` / `24-4` / `24-6` のガード条件はすべて組み込む。`24-3` / `24-5` / `24-8` は合意済み。
- **合意成立**: `v1.1 (Build 5)` の修正実装へ進み、完了後に再レビューを依頼する。

---

## 9. レビュー担当確認: `f96661f`（§27 / §8）確認結果・完全合意成立（2026-11-17）

- `f96661f`（`REVIEW_CLAUDE_SEND_PHASE6.md` §27・本台帳 §8）の内容を確認し、§7 / §26 で提示した全条件が過不足なく採用されていることを検証した。
- **判定: 完全合意成立（条件充足）** — 追加の協議往復なしで **`v1.1 (Build 5)`（`versionCode 1105`）の修正実装を開始して問題なし**。
- **Build 5 実装時（および完了後レビュー時）の細部確認メモ**:
  - `24-7` の未Push削除記録における「商品キー」はローカル数値 `id` ではなく **`syncUuid`（ID衝突時も一意）** を用いること、また削除後にユーザーが**同一MD5の写真をその場で再追加**した場合は該当ハッシュを未Push削除記録から解除することが望ましい（完了後レビュー時にも併せて確認する）。

---

## 10. `v1.1 Build 5`（`53052d4` / `64525e2` / `1a3e48a`）実装コードレビュー結果（2026-11-17）

### 10.1 検証済み（合格）項目
- **`24-1`（`FurimaServerNative.cs:1335-1384, 1434-1437`）**: `HasForwardedHeaders` で5ヘッダ（`Cf-Connecting-Ip`・`Cf-Ray`・`X-Forwarded-For`・`Forwarded`・`X-Real-IP`）を判定し、`GET /` の転送ヘッダ付き／非ループバックアクセスを `403` で拒否、ループバック免除および `IsTrustedClaudeExemptRequest` の両方から転送ヘッダ保有リクエストを除外していることを確認。`HandleRequest` の他ルートはすべて `/api/` 配下で Bearer 認証下にある。
- **`24-2`（`MainActivity.kt:140-156`, `FleaMarketViewModel.kt:883-917, 967-975`）**: `_dismissedClaudeCompletionIds` 導入、`remainingCount` 整合（除外後件数）、`intersect(unackedIds)` のみの掃除、`if (!showClaudeApprovalDialog)` 連動をすべて確認。
- **`24-3`（`FleaMarketViewModel.kt:1060-1065`）**: `!approveFailed && moveFailedCount == 0 && _pendingClaudeApprovals.value.isEmpty()` の時のみリセットされることを確認。
- **`24-4`（`FleaMarketViewModel.kt:849-851, 948-978, 1126-1145`, `PcServerClient.kt:263-305`）**: `startClaudeWatchLoop` が独立コルーチンで `GET /api/claude/revision` を直接ポーリングし、`claudeRefreshMutex` で `refreshClaudeJobStates` を直列化（ロック内 `_claudeActionInProgress` 確認・`ClaudeJobListResponseDto.claudeRevision` 反映）していることを確認。
- **`24-5`（`FurimaServerNative.cs:1452-1565`, `SyncStore.cs:349-377`）**: IPv4 4オクテット正規形＋特別アドレス除外、IPv6 正規形・IPv4-mapped 再検証、不正JSON・型不一致時 `400`、`SetTrustedMuseIp` の成否伝播（失敗時 `500`）を確認。`FurimaServerNative.cs` / `SyncStore.cs` に C# 5 非互換構文（`$""`・`?.`・`nameof(`）が無いことも確認。
- **`24-6`（`FurimaServerNative.cs:5657-5820, 5885-5967, 5977-6050, 6215-6260`）**: `HandleCancelClaudeJob`・`HandleCompleteClaudeJob` への最外層 `claudeResultWriteLock` 追加、`HandlePostClaudeJobResult`・`HandlePostClaudeJobError` でのロック内期限切れ再評価・トークン再検証・`MutateJob` 内ステータス再確認・追記失敗時の `resultPath` 掃除を確認。
- **`24-8` ＆ バージョン統一**: `updatePcServerUrl` での変更時リセット、および 3箇所バージョン統一（`app/build.gradle.kts` `1105`・`tools/DashboardHtml.cs` `v1.1 (Build 5)`・`WebDashboardHtml.kt` `v1.1 (Build 5)`）を確認。

### 10.2 新規指摘 2 件（いずれも `24-7` の実装詳細・追補修正推奨）

1. **【中〜高 `25-1`】`removePhotoFromItem`（詳細画面からの単体写真削除）に未Push削除記録が抜けている**
   - **箇所**: `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt:361-408`（呼出元は `ItemDetailScreen.kt:149` → `FleaMarketViewModel.kt:449`）
   - **事象**: `saveItem`（L316）と `deletePhotosByIds`（L602）には `recordPendingPhotoDeletion` が組み込まれている一方、**商品詳細画面の拡大プレビューから写真を1枚削除する `removePhotoFromItem` には `recordPendingPhotoDeletion` が入っていない**（L406 で `isDirty = true` だけ設定）。そのため、詳細画面から写真を消して未Push（500ms デバウンス中やオフライン中）のまま加算マージが走ると、消した写真が復活し得る。
   - **修正案**: `removePhotoFromItem` 内（`dao.deletePhotosByItemId(itemId)` の前）にも `if (targetPhoto.md5Hash.isNotBlank()) { recordPendingPhotoDeletion(itemWithPhotos.item.syncUuid, targetPhoto.md5Hash) }` を追加する。

2. **【高 `25-2`】`mergeServerPhotosAdditively` で追加された未保有 `_ai` 写真のサムネイル取得が走らず、`displayFile == null` のままになる**
   - **箇所**: `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt:960-1030, 1104-1105, 1343-1348`
   - **事象**: PC 側で生成されたばかりの `_ai` 写真はまだスマホに存在しないため、`mergeServerPhotosAdditively` では `filePath = ""`, `thumbnailPath = null` の `PhotoEntity` として Room へ挿入される（`PhotoEntity.displayFile` は `null` になる）。ところが `L1104-1105` が `mergeServerPhotosAdditively(...)` の直後に **`continue` するため、通常マージ末尾（`L1343-1348`）の `repairMissingOrCorruptThumbnailsForItem` が一切呼ばれない**。結果、`isDirty` が解消されて次の非 dirty Pull が走るまで、スマホ上では追加されたはずの `_ai` 写真が画像なし（欠損状態）のまま表示されてしまう。
   - **修正案**: `mergeServerPhotosAdditively` から追加有無（`Boolean`）を返す（または加算挿入ありの場合に同箇所から `if (repairThumbnails && baseUrl.isNotBlank())` の `repairMissingOrCorruptThumbnailsForItem(existing.item.id, baseUrl, token)` を呼ぶ）ようにし、加算マージ直後にサムネイルを取得・生成して即時表示できるようにする。

### 10.3 補足観察（配布バイナリ）
- コミット `1a3e48a` で Android APK（`flea_market_app.apk`・`public/flea_market_app.apk`）は更新されている一方、`tools/furima-server.exe` のバイナリ更新は `53052d4`〜`1a3e48a` に含まれていない（ローカルでのビルド通過自体は確認済みとの報告）。上記 `25-1`・`25-2` の追補修正後に再ビルド・必要に応じ配布バイナリも同期すること。



---

## 11. 実装担当エージェント回答（§28 再レビューへの回答）（2026-11-17）

- `24-1`〜`24-6`・`24-8` の合格を確認。詳細は `REVIEW_CLAUDE_SEND_PHASE6.md` §29。
- `25-1`・`25-2` は指摘どおり受け入れ、利用者の指示により **6-4（v1.2 Build 6）の実装と同一コミットで追補修正**する（計画 §25.5）。
- 6-4 の詳細化（計画 §25）を提出する。必須ポイント2のとおり、実装前の詳細化レビューをお願いする。
