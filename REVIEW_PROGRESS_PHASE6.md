# フェーズ6 コードレビュー進捗・引き継ぎ台帳 (`REVIEW_PROGRESS_PHASE6.md`)

> 本ファイルは、**フェーズ 6 の実装と並行して行うコードレビューの進捗（サブフェーズ単位）・未解決指摘・次期引き継ぎチェックリスト**を一元記録するための台帳です。
> トークン上限や担当交代で別のレビュー担当エージェントへ引き継ぐ場合も、本ファイルの「1. 現在地」「2. サブフェーズ別レビュー進捗」「4. 次期レビュー担当向け 引き継ぎチェックリスト」を参照すれば直後から再開できます。
>
> - 実装計画書: [PLAN_CLAUDE_SEND_PHASE6.md](PLAN_CLAUDE_SEND_PHASE6.md)
> - 設計協議・指摘履歴: [REVIEW_CLAUDE_SEND_PHASE6.md](REVIEW_CLAUDE_SEND_PHASE6.md)
> - 開発進捗: [PROGRESS.md](PROGRESS.md)

---

## 1. 現在地（最終更新: 2026-11-17 / 基準コミット `e82a18a`）

- **最後にコードレビューを完了したリモートコミット**: `e82a18a`（`feat(v1.1 build1): phase 6-2 batch approval ...`）
- **レビュー完了済み範囲**:
  - 大枠設計協議（`PLAN` §0〜§10 / `REVIEW` §0〜§8）
  - 実装レビュー: **`6-1a` (`Build 14`)・`6-1b` (`Build 15`)・§12是正 (`Build 16`)・ビルドエラー修正 (`Build 17`)・`6-2` (`v1.1 Build 1`)**
- **現在のステータス**:
  - `6-2` (`v1.1 Build 1`) は **コードレビュー完了（`REVIEW` §20・条件付き合格）**。ユーザー側でローカルビルド＆実機検証を実施中。
  - 指摘 `20-1`（`PROGRESS.md` の本台帳リンク消失）は本レビューコミットで復旧済み。
  - 指摘 `20-2`（サムネ解決の `File.exists()` 欠如・必須修正）と `20-3`（任意改善2件）は **`6-3`（`v1.1 Build 2`）時までに要回収**。
  - 次のレビュー待機対象: **`6-3`（合図窓口＋見張り / `v1.1 Build 2`）のサブフェーズ詳細化または実装コミット**。

---

## 2. サブフェーズ別コードレビュー進捗表

| ステップ | サブフェーズ | バージョン | 事前詳細計画レビュー | 実装後コードレビュー | ビルド/静的検証 | 指摘状態 | 根拠コミット / 参照節 |
|---|---|---|---|---|---|---|---|
| 事前 | 大枠設計協議 (`PLAN` §0〜§10) | — | 完了 | — | — | 合意済 | `80a8ddb` / `REVIEW` §0〜§8 |
| 準備 | **`6-1a`**（`queue`・送信時承認・失効戻し・`claudeRevision`永続化） | `v1.0.0 (Build 14)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4` Gradle/Robolectric14件/JS/C#5) | 是正済 (`Build 16`) | `9826052`, `bce7b14`, `62bb8b2`, `13edee9` |
| 準備 | **`6-1b`**（ジョブトークン実行時延長） | `v1.0.0 (Build 15)` | 完了 (`§10`) | 完了 (`§12`) | 完了 (`6d8fcf4`) | 指摘なし | `9826052`, `6d8fcf4` |
| 是正 | **§12 レビュー修正**（件数集計・セレクト無効化・ダイアログスクロール） | `v1.0.0 (Build 16)` | — | 完了 (`§16`) | 差分・ソース確認済（ローカルビルド前提） | 完了 | `62bb8b2`, `13edee9` / `REVIEW` §16 |
| 修正 | **`Encoder.Quality` CS0104 修正＋新体系移行** | `v1.0.0 (Build 17)` | — | 完了 (`§19`) | ユーザー側 `build-exe.bat` 成功報告済 | 完了 | `9636ec1`, `3c9dd33` / `REVIEW` §17〜§19 |
| **Step 1** | **`6-2`**（一括承認：最大10件・チェック式・サムネ・帯抑止） | **`v1.1 (Build 1)`** (`1101`) | 完了 (`§16.1` → `§14`反映済) | **完了 (`§20`)** | ユーザー側ローカルビルド実施中 | `20-1`復旧済 / **`20-2`未回収(`6-3`へ)** / `20-3`(任意) | `714533f`, `3c9dd33`, `e82a18a` / `REVIEW` §20 |
| **Step 1** | **`6-3`**（合図窓口 `/api/claude/revision` ＋ Muse側巡回 10:00〜22:00） | `v1.1 (Build 2)` (`1102`) | **未（詳細化待ち）** | **未着手（次のレビュー対象）** | 未 | — | `PLAN` §9.3, §16 |
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
| `16-1` | `6-2` Android | コンパイル漏れ | `ClaudeJobDto` (`SyncDtos.kt:129`) への `itemUuid` フィールド追加 | 反映済 (`PLAN` §14) | **解消・確認済** (`e82a18a`, `REVIEW` §20) |
| `16-2` | `6-2` Android | ロジックバグ | チェック状態は `uncheckedIds` 保持、一括応答対象は表示分最大10件 `displayedIds` に厳密限定 | 反映済 (`PLAN` §14) | **解消・確認済** (`e82a18a`, `REVIEW` §20) |
| `16-3` | `6-2` Android | 状態干渉・流儀 | 帯タップで抑止解除＋非表示集合クリア／「いったん閉じる」で抑止解除 `false`、`PhotoDao` `LIMIT 1`、`AsyncImage(File)` | 反映済 (`PLAN` §14) | **解消・確認済** (`e82a18a`, `REVIEW` §20) |
| **`20-1`** | `6-2` 文書 | ドキュメント | `PROGRESS.md` から `REVIEW_PROGRESS_PHASE6.md` リンクが消失 (`e82a18a`) | — | **本コミットで復旧済（今後維持注意）** |
| **`20-2`** | `6-2` Android | **バグ（必須修正）** | `FleaMarketViewModel.resolveClaudeApprovalThumbnails` で `File.exists()` チェックがなく、欠損時にフォールバックせず空白表示になる | — | **未回収（`6-3` までに修正要確認）** |
| `20-3` | `6-2` Android | 軽微（任意改善） | (1) `_claudeActionInProgress` 中のハートビート第3分岐スキップ、(2) `ClaudeApprovalDialog` タイトル `TextOverflow.Ellipsis` | — | **任意（`6-3` 等で併せて確認）** |

---

## 4. 次期レビュー担当向け 引き継ぎチェックリスト（絶対条件＆検証ポイント）

### 4.0 レビュー・運用上の絶対条件（引き継ぎ必須）
1. **スコープ境界（`PLAN` §0 / §6）**: `LocalWebServer.kt` / `WebDashboardHtml.kt`（スマホ緊急WebUI）の既存セキュリティ・差分ハッシュ問題はユーザー許可によりフェーズ 6 スコープ外。**バージョン表記 `v1.x (Build N)` の3箇所同時更新チェック以外の指摘は絶対に出さない**こと。
2. **共有ファイル競合防止**: 追記・push 前に必ず `git fetch origin && git log HEAD..origin/main` で最新を取り込むこと（`9636ec1`・`e82a18a` のような文書上書き／行消失の再発防止）。
3. **選択的ステージング厳守**: `tools/build-exe.bat` に CRLF 差分が出続けるため `git add -A` 禁止。必ずファイル指定で `git add` すること。
4. **C# 5 互換維持**: `tools/*.cs` は `.NET Framework v4.0.30319\csc.exe` ビルドのため、補間文字列 `$""`・null条件演算子 `?.`・`nameof(...)` 等の混入がないか常に確認すること（`DashboardHtml.cs` の JS リテラル内文字列は除く）。
5. **テストビルド方針**: テストビルドはユーザー指示時またはステップ区切り（Step 1 = `6-3` 後、Step 2 = `6-4` 後、Step 3 = `6-7` 後）を中心とし、中間レビューではコード精査＋軽量静的チェック（C# 5 構文・`DashboardHtml.cs` verbatim `""` バランス・抽出 JS の `node --check` 等）を優先する。

### 4.1 次の最優先レビュー対象: `6-3`（`v1.1 Build 2` / `versionCode = 1102`）＋ `20-2` 回収確認
1. **未回収指摘 `20-2` の修正確認**: `FleaMarketViewModel.resolveClaudeApprovalThumbnails`（`966-984`行）で `File(it).exists()` により実ファイル存在する候補のみを採用し、`md5` 欠損時に `itemUuid`／プレースホルダへ正しく落ちるか。あわせて `PROGRESS.md` の本台帳リンクが維持されているか。
2. **`6-3` 合図窓口（サーバー）**: `tools/FurimaServerNative.cs:1333` `AuthenticateRequest` で `rawPath == "/api/claude/revision" && req.HttpMethod == "GET"` のみ認証免除・応答が `{ "claudeRevision": N }` のみ・**`lastHeartbeat` を絶対に更新しない**こと、`claudeRevision` 永続値（`ClaudeJobStore`）と整合すること。
3. **`6-3` Muse側運用手順**: `PLAN` に記載される 10:00〜22:00 巡回・初回 `claudeRevision` 取得・変化時のみ詳細取得／処理手順に、認証やトークン露出・無限ループの穴がないか。
4. **バージョン3箇所統一**: `app/build.gradle.kts` (`versionName = "1.1"`, `versionCode = 1102`)、`tools/DashboardHtml.cs` (`v1.1 (Build 2)`)、`WebDashboardHtml.kt` (`v1.1 (Build 2)`)、`PROGRESS.md` 更新。

### 4.2 今後のサブフェーズ事前／実装レビュー重点ポイント
- **`6-4` (`v1.2 Build 3` 単独ステップ)**:
  - `DRAFT_DONE`・`SHIPPING` 追加に伴う Android/PC 全層のステータス分岐網羅（特に既存の `else -> "出品中"` フォールバックを残さず `when` 網羅／明示マッピングにすること）。
  - `{SOLD, SHIPPING}` 両方で `soldPrice` / `soldAt` 保持・売上集計・CSV 出力の整合（売却日確定と発送準備中の扱い確認）、`getItemSortPriority` 順位、`DashboardHtml.cs` `<header>` 折り返し崩れ・まとめ親子整合。
- **`6-5`〜`6-7` (`v1.3 Build 4〜6`)**:
  - `6-5`/`6-6`: `errorDismissed` 集計と30日削除条件の整合、待機タブ/依頼中タブ・編集モーダル「この商品のジョブを見る」連携。
  - `6-7`: `DashboardHtml.cs:85` `<aside>` の独立スクロール（`lg:sticky lg:top-14 lg:max-h-[calc(100vh-4.5rem)] lg:overflow-y-auto`）、および `DashboardHtml.cs:1601` `updateStatusBannerFromData` が `#stat-connection-status.className` を上書きするため `<header>`（濃紺背景 `bg-indigo-700`）移動時に明色ピル背景等へ修正されているか。
