# フェーズ6 コードレビュー進捗・引き継ぎ台帳 (`REVIEW_PROGRESS_PHASE6.md`)

> 本ファイルは、**フェーズ 6 の実装と並行して行うコードレビューの進捗（サブフェーズ単位）・未解決指摘・次期引き継ぎチェックリスト**を一元記録するための台帳です。
> トークン上限や担当交代で別のレビュー担当エージェントへ引き継ぐ場合も、本ファイルの「1. 現在地」「2. サブフェーズ別レビュー進捗」「4. 次期レビュー担当向け 引き継ぎチェックリスト」を参照すれば直後から再開できます。
>
> - 実装計画書: [PLAN_CLAUDE_SEND_PHASE6.md](PLAN_CLAUDE_SEND_PHASE6.md)
> - 設計協議・指摘履歴: [REVIEW_CLAUDE_SEND_PHASE6.md](REVIEW_CLAUDE_SEND_PHASE6.md)
> - 開発進捗: [PROGRESS.md](PROGRESS.md)

---

## 1. 現在地（最終更新: 2026-11-17 / 基準コミット `3c9dd33`）

- **最後に確認済みのリモートコミット**: `3c9dd33`（`docs(review): restore §16 ... amend plan §14 per 16-1..16-3; adopt step structure + v1.1 versioning`）および `9636ec1`（`Build 17`: `System.Drawing.Imaging.Encoder.Quality` 修正）
- **レビュー完了済み範囲**:
  - 大枠設計協議（`PLAN` §0〜§10 / `REVIEW` §0〜§8）
  - 実装レビュー: **`6-1a` (`Build 14`)・`6-1b` (`Build 15`)・§12是正 (`Build 16`)・ビルドエラー修正 (`Build 17`)**
  - 事前詳細レビュー: **`6-2`（計画書 §14・`16-1`〜`16-3` 反映済・合意完了）**
- **現在のステータス**: **反論なし・実装待機中**。次に着弾する **`6-2`（一括承認 / `v1.1 Build 1`, `versionCode = 1101`）の実装コミット** のコードレビューから再開する。

---

## 2. サブフェーズ別コードレビュー進捗表

| ステップ | サブフェーズ | バージョン | 事前詳細計画レビュー | 実装後コードレビュー | ビルド/静的検証 | 指摘状態 | 根拠コミット / 参照節 |
|---|---|---|---|---|---|---|---|
| 事前 | 大枠設計協議 (`PLAN` §0〜§10) | — | 完了 | — | — | 合意済 | `80a8ddb` / `REVIEW` §0〜§8 |
| 準備 | **`6-1a`**（`queue`・送信時承認・失効戻し・`claudeRevision`永続化） | `v1.0.0 (Build 14)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4` Gradle/Robolectric14件/JS/C#5) | 是正済 (`Build 16`) | `9826052`, `bce7b14`, `62bb8b2`, `13edee9` |
| 準備 | **`6-1b`**（ジョブトークン実行時延長） | `v1.0.0 (Build 15)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4`) | 指摘なし | `9826052`, `6d8fcf4` |
| 是正 | **§12 レビュー修正**（件数集計・セレクト無効化・ダイアログスクロール） | `v1.0.0 (Build 16)` | — | 完了 (`§16`) | 差分・ソース確認済（ローカルビルド前提） | 完了 | `62bb8b2`, `13edee9` / `REVIEW` §16 |
| 修正 | **`Encoder.Quality` CS0104 修正＋新体系移行** | `v1.0.0 (Build 17)` | — | 完了 (`§19`) | ユーザー側 `build-exe.bat` 成功報告済 | 完了 | `9636ec1`, `3c9dd33` / `REVIEW` §17〜§19 |
| **Step 1** | **`6-2`**（一括承認：最大10件・チェック式・サムネ・帯抑止） | **`v1.1 (Build 1)`** (`1101`) | **完了 (`§16.1` → `§14`反映済)** | **未着手（コミット待ち）** | 未 | 合意済（実装時要確認3件） | `714533f`, `3c9dd33` / `PLAN` §14 |
| **Step 1** | **`6-3`**（合図窓口 `/api/claude/revision` ＋ Muse側巡回 10:00〜22:00） | `v1.1 (Build 2)` (`1102`) | 未（詳細化待ち） | 未着手 | 未 | — | `PLAN` §9.3, §16 |
| **Step 2** | **`6-4`**（出品ステータス拡張：`DRAFT_DONE` / `SHIPPING` 導入）※単独 | `v1.2 (Build 3)` (`1203`) | 未（詳細化待ち） | 未着手 | 未 | — | `PLAN` §9.3, §16 |
| **Step 3** | **`6-5`**（加工ラベル：サムネ属性ラベル＋寿命管理） | `v1.3 (Build 4)` (`1304`) | 未（詳細化待ち） | 未着手 | 未 | — | `PLAN` §9.3, §16 |
| **Step 3** | **`6-6`**（ジョブ一覧改善：依頼中/待機中切替・出品カード連携） | `v1.3 (Build 5)` (`1305`) | 未（詳細化待ち） | 未着手 | 未 | — | `PLAN` §9.3, §16 |
| **Step 3** | **`6-7`**（右カラム再編・ミューズカラム新設・接続ステータス移動） | `v1.3 (Build 6)` (`1306`) | 未（詳細化待ち） | 未着手 | 未 | — | `PLAN` §9.3, §16 |

---

## 3. 指摘事項・追跡台帳

| ID | 対象 | 区分 | 内容要約 | 計画書反映 | 実装反映・確認状態 |
|---|---|---|---|---|---|
| `10-1`〜`10-5` | `6-1a`/`6-1b` | 事前設計 | `move` 許可条件統一、`CreateJob` 原子化、`dismiss-error` 冪等、状態ラベルマトリクス、延長呼出位置 | 反映済 (`PLAN` §12) | **解消・確認済** (`bce7b14`, `6d8fcf4`) |
| `12-1` | `Build 14` PC | バグ | `DashboardHtml.cs` 件数バッジ集計の共通化（`waiting` 除外漏れ） | — | **解消・確認済** (`62bb8b2`, `REVIEW` §16) |
| `12-2` | `Build 14` PC | UI連動 | `#viewer-claude-queue` の `disabled` 連動抜け | — | **解消・確認済** (`62bb8b2`, `REVIEW` §16) |
| `12-3` | `Build 14` Android | UI崩れ | `ClaudeSendDialog` 小画面向け `verticalScroll` 抜け | — | **解消・確認済** (`62bb8b2`, `REVIEW` §16) |
| `16-1` | `6-2` Android | コンパイル漏れ | `ClaudeJobDto` (`SyncDtos.kt:126`) への `itemUuid` フィールド追加 | 反映済 (`PLAN` §14) | **実装時要確認 (`6-2`待ち)** |
| `16-2` | `6-2` Android | ロジックバグ | チェック状態は `uncheckedIds` 保持、一括応答対象は表示分最大10件 `displayedIds` に厳密限定 | 反映済 (`PLAN` §14) | **実装時要確認 (`6-2`待ち)** |
| `16-3` | `6-2` Android | 状態干渉・流儀 | 帯タップで抑止解除＋非表示集合クリア／「いったん閉じる」で抑止解除 `false`、`PhotoDao` `LIMIT 1`、`AsyncImage(File)` | 反映済 (`PLAN` §14) | **実装時要確認 (`6-2`待ち)** |

---

## 4. 次期レビュー担当向け 引き継ぎチェックリスト（絶対条件＆検証ポイント）

### 4.0 レビュー・運用上の絶対条件（引き継ぎ必須）
1. **スコープ境界（`PLAN` §0 / §6）**: `LocalWebServer.kt` / `WebDashboardHtml.kt`（スマホ緊急WebUI）の既存セキュリティ・差分ハッシュ問題はユーザー許可によりフェーズ 6 スコープ外。**バージョン表記 `v1.x (Build N)` の3箇所同時更新チェック以外の指摘は絶対に出さない**こと。
2. **共有ファイル競合防止**: 追記・push 前に必ず `git fetch origin && git log HEAD..origin/main` で最新を取り込むこと（`9636ec1` による §16 上書き事故の再発防止）。
3. **選択的ステージング厳守**: `tools/build-exe.bat` に CRLF 差分が出続けるため `git add -A` 禁止。必ずファイル指定で `git add` すること。
4. **C# 5 互換維持**: `tools/*.cs` は `.NET Framework v4.0.30319\csc.exe` ビルドのため、補間文字列 `$""`・null条件演算子 `?.`・`nameof(...)` 等の混入がないか常に確認すること（`DashboardHtml.cs` の JS リテラル内文字列は除く）。
5. **テストビルド方針**: テストビルドはユーザー指示時またはステップ区切り（Step 1 = `6-3` 後、Step 2 = `6-4` 後、Step 3 = `6-7` 後）を中心とし、中間レビューではコード精査＋軽量静的チェック（C# 5 構文・`DashboardHtml.cs` verbatim `""` バランス・抽出 JS の `node --check` 等）を優先する。

### 4.1 次の最優先レビュー対象: `6-2`（`v1.1 Build 1` / `versionCode = 1101`）実装到着時
1. `SyncDtos.kt`: `ClaudeJobDto` に `@param:Json(name = "itemUuid") val itemUuid: String = ""` が追加されているか（指摘 `16-1`）。
2. `PhotoDao.kt` / `ItemDao.kt` / `FleaMarketRepository.kt`: `md5Hash` 検索が `LIMIT 1` になっているか、フォールバック `itemUuid` → `syncUuid` で先頭写真パスを取得できているか、サムネファイル欠損時の例外・クラッシュがないか。
3. `PcServerClient.kt`: `postClaudeJobMove(jobId, queue)` が既存 `ack` と同型・適切なエラーメッセージ処理になっているか。
4. `FleaMarketViewModel.kt`: `respondToClaudeApprovals(checkedIds, displayedIds)` が **`displayedIds`（最大10件）に厳密限定**されているか（指摘 `16-2`）、承認失敗時途中停止＋経過件数表示／待機戻し失敗時継続＋件数集計、完了後の状態再取得と `dismissedApprovalIds` クリアが正しいか。
5. `ClaudeApprovalDialog.kt`: `uncheckedIds` でチェック除外状態を維持し、新着ジョブは初期ON・消滅IDのみ掃除されるか（指摘 `16-2`）、`AsyncImage(File(path))` 使用、縦スクロールで小画面対応、「承認する（N件）」/「全て待機へ戻す」＋拒否ボタン廃止、Claude→Muse 表記修正。
6. `MainActivity.kt`: `currentBackStackEntryAsState()` 等で `item_edit/...` 判定が正確か、PC発ジョブ含有一時は非抑止になっているか、帯タップで抑止解除＋`dismissedApprovalIds` クリア、「いったん閉じる」で抑止解除を `false` に戻すか（指摘 `16-3`）、`ClaudeCompletionDialog` との競合制御が保たれているか。
7. バージョン3箇所統一: `app/build.gradle.kts` (`versionName = "1.1"`, `versionCode = 1101`)、`tools/DashboardHtml.cs` (`v1.1 (Build 1)`)、`WebDashboardHtml.kt` (`v1.1 (Build 1)`)、`PROGRESS.md` 更新。

### 4.2 今後のサブフェーズ事前／実装レビュー重点ポイント
- **`6-3` (`v1.1 Build 2`)**: `tools/FurimaServerNative.cs:1333` `AuthenticateRequest` で `rawPath == "/api/claude/revision" && req.HttpMethod == "GET"` のみ認証免除・応答が `{ "claudeRevision": N }` のみ・**`lastHeartbeat` を絶対に更新しない**こと、`claudeRevision` 永続値（`ClaudeJobStore`）と整合すること。
- **`6-4` (`v1.2 Build 3` 単独ステップ)**:
  - `DRAFT_DONE`・`SHIPPING` 追加に伴う Android/PC 全層のステータス分岐網羅（特に既存の `else -> "出品中"` フォールバックを残さず `when` 網羅／明示マッピングにすること）。
  - `{SOLD, SHIPPING}` 両方で `soldPrice` / `soldAt` 保持・売上集計・CSV 出力の整合（売却日確定と発送準備中の扱い確認）、`getItemSortPriority` 順位、`DashboardHtml.cs` `<header>` 折り返し崩れ・まとめ親子整合。
- **`6-5`〜`6-7` (`v1.3 Build 4〜6`)**:
  - `6-5`/`6-6`: `errorDismissed` 集計と30日削除条件の整合、待機タブ/依頼中タブ・編集モーダル「この商品のジョブを見る」連携。
  - `6-7`: `DashboardHtml.cs:85` `<aside>` の独立スクロール（`lg:sticky lg:top-14 lg:max-h-[calc(100vh-4.5rem)] lg:overflow-y-auto`）、および `DashboardHtml.cs:1601` `updateStatusBannerFromData` が `#stat-connection-status.className` を上書きするため `<header>`（濃紺背景 `bg-indigo-700`）移動時に明色ピル背景等へ修正されているか。
