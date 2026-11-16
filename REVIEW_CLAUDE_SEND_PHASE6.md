# フェーズ 6 実装計画書（`PLAN_CLAUDE_SEND_PHASE6.md`）事前コードレビュー・協議メモ

- **レビュー対象コミット**: `fb4aec0`（`フェーズ6計画書を策定（大雑把なフェーズ 6-1〜6-7）＋フェーズ5-6試運転完了を記録`）
- **レビュー実施日**: 2026-11-16
- **照合対象**: GitHub `main` 現行コード（`tools/ClaudeJobStore.cs`, `tools/FurimaServerNative.cs`, `tools/DashboardHtml.cs`, `tools/SyncStore.cs`, `FleaMarketViewModel.kt`, `FleaMarketRepository.kt`, `PcServerClient.kt`, `CsvExporter.kt`, `ClaudeApprovalDialog.kt`, `ItemListScreen.kt`, `ItemEditScreen.kt`, `ItemDetailScreen.kt`, `SalesAnalysisScreen.kt`, `BatchEditDialog.kt`, `ItemQuickActionDialog.kt`, `ListingValidationBanner.kt`）
- **スコープ注記**: §0 / §6 の合意に従い、ローカルWebUI（`LocalWebServer.kt` / `WebDashboardHtml.kt`）の既知課題は本レビューの対象外としています。

---

## 0. 総括

フェーズ構成順序（`6-1` ジョブ基盤 → `6-2` 一括承認 → `6-3` 合図窓口・見張り → `6-4` 出品ステータス拡張 → `6-5` 加工ラベル → `6-6` ジョブ一覧改善 → `6-7` レイアウト再編）の大枠は依存関係に沿っており妥当です。  
ジョブ管理を `claude_jobs.json` / `claudeRevision` に閉じ込めた既存構造を活かして、`itemsDb` / `dataRevision` へ同期ノイズを出さない方針も適切です。

一方で、**現行コードと照合した結果、サブフェーズ詳細化へ進む前に方針修正・仕様確定が必要な設計上の問題点が複数見つかりました。**  
特に以下の 4 点は、そのまま実装に入るとデータ破壊・機能停止・無限ループを招きます。

1. **【致命的 / 6-3】`claudeRevision` がメモリ保持のみで、PCサーバー再起動のたびに `1` へリセットされる**
2. **【致命的 / 6-4】`SHIPPING`（発送準備中）が既存の `status != "SOLD"` 時 `soldPrice = 0` / `soldAt = null` クリア処理と衝突する**
3. **【要修正 / 6-1】`ClaudeJob.origin` は既に送信元端末（`"pc"` / `"phone"`）で使用済み**
4. **【要修正 / 6-2】一括承認でチェックを外したジョブが、直後のハートビートで再ポップアップし続ける**

以下、§7 のレビュー依頼 4 項目への回答とあわせて、具体的な修正提案を記載します。

---

## 1. §7-1 への回答：フェーズ分割・順序と `6-1` の問題点

### 指摘 1-1: `6-1` の責務が広すぎるため、2段階分割を推奨
現在の `6-1`（Build 14）は、以下を 1 フェーズに集約しています。
- 所属リスト（巡回／手動／待機）属性追加＋作成時指定＋リスト間移動API
- 送信時 `approval_pending` 即時発火＋期限切れ時の待機戻し
- ジョブトークンのスライディング延長化
- PC / スマホ両 UI の送信 3 択化＋一覧表示

**修正案**:
- **案**: `6-1a`（所属リスト属性＋即時承認＋待機戻し＋移動API＋一覧表示）と `6-1b`（スライディング延長＋PC/スマホ 3択送信UI）に分割するか、少なくとも **「トークン延長方式変更」** は単体でリグレッション確認できるサブフェーズ／ビルドに切り出す方が安全です。

### 指摘 1-2: `ClaudeJob.origin` フィールドは既に使用中（新規フィールド必須）
- **該当箇所**: `tools/ClaudeJobStore.cs`（`ClaudeJob.origin`）、`tools/FurimaServerNative.cs:4567`、`ClaudeApprovalDialog.kt:60`
- **現状**: `origin` は `"pc"` / `"phone"`（送信元端末）を表すフィールドとして既に検証・UI表示（`送信元: スマホ / PC`）に使われています。
- **提案**: 所属リスト（巡回／手動／待機）は `origin` に相乗りさせず、**別フィールド（例：`queue` または `dispatchMode` 等、`patrol` / `manual` / `waiting`、既存データ読み込み時デフォルトは手動または待機）** として追加してください。

### 指摘 1-3: 「所属リスト」と `status`・30日自動削除の直交ルールが未定義
- 承認待ち（10分）やトークン期限切れ（30分）で「待機」に戻した際、`status` を `queued` に戻すのか `approval_expired` に統一するのかを明記する必要があります。
  - 現行の `HandleRequestClaudeApproval`（`FurimaServerNative.cs:4862`）は `queued` と `approval_expired` のみを再リクエスト可としていますが、`CancelJob` 側の許可状態との整合も確認が必要です。
- **待機リストと30日整理ルール（`ClaudeJobStore.CleanupLocked()`）**:
  - 現在 `CleanupLocked()` は `cancelled` / `denied` / `acknowledged == true` の完了ジョブのみ 30 日で削除します。
  - 「待機」で放置された期限切れジョブを永遠に残すか（仕様通り台帳維持で良いか）、また `done_with_errors` が待機リストの警告表示に入った場合の **`acknowledged` の立て方**（スマホ完了ダイアログ `5-5` での確認と PC ジョブ一覧側での確認・消込の関係）を整理してください。

### 指摘 1-4: スマホ送信時の「自己承認割り込み」UX 課題
- **該当箇所**: `ItemEditScreen.kt`、`FleaMarketViewModel.kt:1124-1126`、`MainActivity.kt`
- **問題**: ユーザーがスマホの `ItemEditScreen` から複数商品の写真を連続して「巡回」または「手動」で送信しようとした場合、1件目を送った瞬間にサーバーが `approval_pending` ＋ `BumpClaudeRevision()` を発火するため、数秒後のハートビートで `MainActivity` の承認オーバーレイが割り込み表示され、連続作業を遮断します。
- **提案**: 送信UI側で「送信完了時に承認ダイアログが一時出る旨」を許容するか、**スマホからの送信に限り「保存して即時承認済みにするか」等の整理はせず承認原則を守る前提で、`6-2` 一括承認の「今回は見送り（スキップ）」または短いディレイ／編集画面滞在中の扱い**を協議したいです。

### 指摘 1-5: トークン・スライディング延長の実装制約
`ApplyTokenExpiry` 周辺（`FurimaServerNative.cs:4825-4860`, `5000+`, `5247+`, `5496+`, `5616+`）を改修する際は、以下を明文化することを推奨します。
1. **延長対象APIを厳密限定**: `X-Claude-Job-Token` 認証を伴う **実行系エンドポイント（`GET /photos/{md5}`, `POST /results`, `POST /errors`）のみ** で期限を延長する。`GET /api/claude/jobs/{jobId}`（参照系）では絶対に延長しない（ポーリングでトークンが永久延命するバグを防ぐため）。
2. **判定順序**: 必ず **先に** `ApplyTokenExpiry(job)` で現在時刻が期限内かをチェックし、有効な場合のみ `jobTokenExpiresAt = now + 30分` 等へ更新する。
3. **リビジョン抑制**: 期限延長だけで `BumpClaudeRevision()` を呼ばない（写真処理中の不要なポーリング発火を防ぐため）。

---

## 2. §7-2 への回答：依存関係・連動箇所の見落とし（`6-4` / `6-2` / `6-5` / `6-6`）

### 2-A. `6-4`（出品ステータス拡張：`DRAFT_DONE`, `SHIPPING`）の見落とし

§4 では「文字列の素通し＋解釈箇所の対応で完結する見込み」とありますが、実コードには **既存データの欠損や誤分類を起こす箇所** が多数存在します。サブフェーズ策定前に全箇所の仕様統一が必要です。

#### 指摘 2-1: 【致命的】`SHIPPING` で `soldPrice` と `soldAt` が強制クリアされる
- **該当箇所**:
  - `FleaMarketRepository.kt:128, 325, 578-580`（`status != "SOLD"` なら `soldPrice = 0` / `soldAt = null`）
  - `ItemEditScreen.kt:1022, 1198`（`status == "SOLD"` のときだけ売価欄表示・保持、それ以外は `0` 保存）
  - `FurimaServerNative.cs:2325, 2918, 3126`（`status == "SOLD"` 以外は `soldAt = null`、`soldPrice = 0` 等）
  - `DashboardHtml.cs:3160, 3261, 3477, 3720`（`SOLD` 以外は売価入力グループが `hidden`、保存時に `soldPrice = 0`）
- **問題**: `SHIPPING` は「売れた後、発送するまでの状態」です。現行のまま `SHIPPING` を足すと、売れた時点で `SHIPPING` に変更しても **売価を入力・保存できず（または0円に消去され）、後の `SHIPPING -> SOLD` 変更時に実際の売価が失われ、`soldAt` も「売却日」ではなく「発送完了日」で上書き** されてしまいます。
- **要確定事項**:
  1. `SHIPPING` と `SOLD` の両方で `soldPrice` / `soldAt` を入力・保持・利益計算（`calcPrice` / `basePrice`）の対象にする。
  2. **売上分析（`SalesAnalysisScreen.kt:111`, `FleaMarketViewModel.kt:294`, PC画面売上統計）および CSV 出力（`CsvExporter.kt:56`, `DashboardHtml.cs:3915`）で、`SHIPPING` を「売上計上済み」に含めるか・`SOLD` のみにするか** を決める（売れた時点で価格が確定するなら売上合計・利益計算に含める、あるいは「発送待ち売上」をどう扱うか明確化）。

#### 指摘 2-2: 【危険】未知ステータスを `"出品中"` 扱いする `else` フォールバック
- **該当箇所**:
  - `FurimaServerNative.cs:2277`（`status == "SOLD" ? "売却済み" : (status == "DRAFT" ? "未出品" : "出品中")` 等、同種分岐がサーバー複数箇所）
  - `PcServerClient.kt:581-583`
  - `DashboardHtml.cs:1934, 1936, 3774, 3904-3908`
  - `ItemListScreen.kt:847-859`、`ItemDetailScreen.kt:433-443`、`BatchEditDialog.kt:286-292`、`ItemQuickActionDialog.kt:71-84`
- **問題**: 多くの箇所が `SOLD` と `DRAFT` 以外を `else` で **`"出品中"`** として扱っています。1箇所でも対応漏れがある場合や、PC・スマホの更新順（ローリング中）に不整合が起きた場合、`DRAFT_DONE` や `SHIPPING` が「出品中」として表示・保存されてしまいます。`else` は `"出品中"` に倒さず安全側（生コード等）にし、6-4 で全箇所を列挙・置換する必要があります。

#### 指摘 2-3: `DashboardHtml.cs` の優先度ソートと絞り込みタブが未対応
- **`getItemSortPriority`（`DashboardHtml.cs:2053-2060`）**:
  - 現状は `まとめLISTED(1) -> まとめDRAFT(2) -> 単品LISTED(3) -> 単品DRAFT(4) -> その他(5)` のため、`DRAFT_DONE` と `SHIPPING` が未対応のままだと **両方とも `SOLD` と同じ最下位(5)** に落ちます（例：`LISTED -> SHIPPING -> DRAFT_DONE -> DRAFT -> SOLD` などの優先順位定義が必要）。
- **絞り込みタブ（`DashboardHtml.cs:43-46, 1719-1721, 2031-2033` / `ItemListScreen.kt:550-571` / `FleaMarketViewModel.kt:202-204`）**:
  - PC版 `UNSHARED` は `item.status === 'DRAFT'` しか見ていません。新ステータス追加後の絞り込みタブ構成（5ステータス＋すべて＋ゴミ箱）と件数バッジを確定させる必要があります。

#### 指摘 2-4: その他 `6-4` で必ず洗い出しに含めるべき連動箇所
1. **まとめ可否判定**: `ItemListScreen.kt:668`（`LISTED || DRAFT`）、`DashboardHtml.cs:1830`（`LISTED || DRAFT`）、`DashboardHtml.cs:3005`（`SOLD` のみ不可）。`DRAFT_DONE` は可、`SHIPPING` は売却後のためまとめ不可に揃えるべきです。
2. **出品前チェック警告**: `ListingValidationBanner.kt:43` / `DashboardHtml.cs:3293` は現在 `status == "SOLD"` のみスキップしています。`SHIPPING` も既に出品後なので警告対象外にすべきか判断が必要です。
3. **売却30日経過写真の整理**: `SyncStore.cs:582, 593, 627` / `FleaMarketViewModel.kt:813` は `SOLD` のみを整理対象にしています（`SHIPPING` を誤って整理対象にしないこと。また `SHIPPING -> SOLD` にしたとき、整理の起算日 `soldAt` が売れた日のまま30日計算される点も整合確認が必要です）。
4. **まとめ親の変更時における子ラベル更新**: `FurimaServerNative.cs:2931` が `nextStatus == "SOLD" ? "まとめ売却済" : "まとめ出品中"` の 2 択になっており、`SHIPPING` 等が親に指定された場合の扱い（まとめ商品は `SHIPPING` になりうるため）を確認すべきです。
5. **スマホ一覧のワンタップステータス切替**: `ItemListScreen.kt:166-170` のタップトグルが `DRAFT_DONE` / `SHIPPING` を受けたときの遷移先を定義する必要があります。
6. **CSV 出力**: `CsvExporter.kt:39-41` および `DashboardHtml.cs:3904-3922`。

---

### 2-B. `6-2`（一括承認：最大10件）の設計上の穴

#### 指摘 2-5: チェックを外したジョブが即座に再ポップアップするループ問題
- **該当箇所**: `FleaMarketViewModel.kt:856, 913-920, 1124-1126`
- **問題**: 計画書では「チェックを外したジョブは承認しない」とだけ書かれていますが、承認したジョブの API 呼び出しでサーバー側の `claudeRevision` が進むため、**数秒後のハートビートで `refreshClaudeJobStates` が走り、サーバーに残っている「チェックを外して承認しなかった `approval_pending` ジョブ」を再び検知して承認ダイアログを即時再表示** します。結果、実質「全件承認するか拒否するかまでダイアログが閉じない」状態になります。
- **修正案（いずれかの明文化が必須）**:
  - **案A（サーバー側へ反映・推奨）**: チェックを外したジョブは「見送り＝**待機リストへ戻す**（または拒否を選べる UI にする）」ことで `approval_pending` から外し、再ポップアップを断つ。
  - **案B（端末内スキップ）**: 「いったん閉じる／今回外した分」として端末メモリ上に `(jobId, approvalRequestedAt)` を保持し、再リクエスト（`approvalRequestedAt` 更新）またはアプリ再起動までは自動で再表示しない。

#### 指摘 2-6: サムネイル解決に `ClaudeJob.itemId` を使ってはいけない
- スマホ Room DB の `ItemEntity.id` と PC 側の `ClaudeJob.itemId` は一致しないケースがあるため、一括承認ダイアログの各行サムネイルは **`job.photos.firstOrNull()?.md5`（`PhotoEntity.md5Hash` 検索）** または **`job.itemUuid`（`syncUuid` 検索）** でローカル写真を引く設計にしてください。

#### 指摘 2-7: 10件超過時の抽出順と部分失敗の扱い
- 11件以上が `approval_pending` の場合、古い順（`createdAt` 昇順＝FIFO）で先頭10件を出すのか、「残り N 件あり」と表示するのかを明記すべきです。
- 既存単体 API をジョブ数分ループ呼び出しする場合、3件目などで通信失敗した際に「承認済み 2件はサーバー反映済み・残り未処理」がユーザーに分かるエラーハンドリング（あるいはバルク承認 API の採用）が必要です。

---

### 2-C. `6-5`（加工ラベル）・`6-6`（ジョブ一覧改善）の注意点

- **既読マーカーの保存先**: 「商品カードを開いたら『加工済み』ラベルが消える」状態を `items_data.json`（商品データ側）に持つと、PC でカードを開くたびに `dataRevision` が上がってスマホ同期が誘発されます。既読マーカーは **`ClaudeJobStore`（`claude_jobs.json` / `claudeRevision`）側** に持ち、しかも消去対象は **完了後の「加工済み」表示のみ**（「加工中」「待機中」や未確認エラーは開いても消えない）に限定する必要があります。
- **複数ジョブ競合時の表示優先度**: 同一商品に複数ジョブが存在する場合（例：1件が処理中、1件が待機またはエラー）、カードやサムネイルに出す優先順位（例：`エラー警告 > 加工中 > 承認待ち > 待機中 > 加工済み`）を決めてください。
- **カードジャンプ（`6-6`）の失敗ケース対策**: ジョブの商品名クリックで該当カードへ飛ぶ際、**別ステータス絞り込み中／検索入力中／ゴミ箱入り／`MERGED`（親商品に内包済み）** だと対象カードが画面上に存在しません。`itemUuid` 優先で突合した上で、絞り込み解除や親カード・ゴミ箱への遷移またはトースト案内を行ってください。

---

## 3. §7-3 への回答：`6-3`（版番号だけの認証なし窓口）の置き方と最大の罠

### 指摘 3-1: 【最重要】`claudeRevision` がインメモリのみで再起動時に `1` へリセットされる
- **該当箇所**: `tools/FurimaServerNative.cs:37, 181-188`
  ```csharp
  private static long claudeRevision = 1;
  private static long BumpClaudeRevision() => Interlocked.Increment(ref claudeRevision);
  ```
- **問題**: §2 の前提にある通り、PC サーバーは常時稼働ではなくユーザーが起動・停止を行います。現状 `claudeRevision` はメモリ上のみに存在するため、**サーバーを再起動するたびに `claudeRevision` は必ず `1` に戻ります**。
  - Muse 側の見張りスクリプトが前回値（例：`24`）を保持して `current > prev` で判定していた場合、**サーバー再起動後は `1 <= 24` となり二度と Muse を起こさなくなります（サイレント停止）**。
  - `!=` 判定にしても、サーバーを起動するたびにジョブ0件で毎回空振り起床が発生します。
- **修正案（`6-3` の必須前提）**:
  - `claudeRevision` を `claude_jobs.json`（または専用メタファイル）へ永続化し、サーバー再起動後も単調増加で引き継ぐ仕組みを導入してください。

### 指摘 3-2: 認証境界（`AuthenticateRequest`）への配置方法
- **該当箇所**: `tools/FurimaServerNative.cs:1340-1366`
- Tailscale（`100.87.157.47:8080`）経由のアクセスは `IPAddress.IsLoopback(remoteIp)` が `false` になるため、無認証窓口を作るには `AuthenticateRequest` 内の例外部が必要です。
- `req.Url.AbsolutePath == "/api/claude/revision" && req.HttpMethod == "GET"` に厳密一致させたピンポイント許可であれば既存セキュリティ境界との整合は取れます。ただし以下 2 点を徹底してください。
  1. レスポンスは `{ "claudeRevision": N }` のみ（ジョブ内容・件数・エラー状態などは一切返さない）。
  2. このリクエストで **`lastHeartbeat` を絶対に更新しない**（見張りスクリプトの 3 分ポーリングをスマホ接続中と誤認させない）。

### 指摘 3-3: サイレント障害の盲点（§3.3-8）
巡回処理の完了報告をチャットに出さない仕様上、トークン失効や画像形式エラーで全滅してもユーザーが管理画面・スマホを開くまで気づけません。運用知見として「巡回そのものがエラー終了した／未処理のまま異常停止したときだけチャット警告する」例外を設けるかも協議の余地があります。

---

## 4. §7-4 への回答：`6-7`（レイアウト再編）のリスク評価

### 指摘 4-1: `<aside>` 独立スクロール化に伴う調整（`DashboardHtml.cs:85`）
- 現状は `<aside class='w-full lg:w-64 shrink-0 sticky top-24 space-y-3'>` となっており、`max-height` も `overflow-y-auto` もありません。ブロックを 4 つ（売上分析 → コントロール → ミューズ → 容量・写真・ゴミ箱）に増やすと、ノート PC の画面高さでは下部のボタン群が画面外に出てスクロール不能になります。またヘッダー高さ `h-10`（40px）に対して `top-24`（96px）は余白が過大です。
- **修正案**: 親要素の `items-start` を維持したまま、**`lg:sticky lg:top-14 lg:max-h-[calc(100vh-4.5rem)] lg:overflow-y-auto custom-scroll pr-1`** のように `lg` 以上でのみ独立スクロール化し、モバイル幅では通常の縦積みに戻してください。

### 指摘 4-2: 【見落としやすい】`<header>`（`DashboardHtml.cs:31-56`）の横幅オーバーフロー
- 現在の `<header>` は **高さ固定 `h-10`（40px）の 1 行** に「タイトル＋Yahoo!出品ボタン」＋「絞り込みボタン群（現在5個）」＋「検索欄」を横並びで収めています。
- ここに **`6-4` でステータスボタンが 2 つ増え（合計 7 個）**、さらに **`6-7` で「接続ステータス」を検索欄脇へ移す** と、1280〜1366px 程度のノート PC 幅で右側コンテナ（`overflow-x-auto`）が横溢れし、**検索ボックスや接続ステータスが押し出されて常時見切れる** 恐れがあります。
- `6-4` または `6-7` で **ヘッダーを 2 段構成または折り返し可（`min-h-10` / `flex-wrap`）** にする設計を織り込むべきです。

### 指摘 4-3: `updateStatusBannerFromData`（`DashboardHtml.cs:1597-1615`）の色クラス上書き衝突
- 既存の `updateStatusBannerFromData` は `#stat-connection-status` の文字列だけでなく **`statusElem.className` を丸ごと書き換え** ています。
  ```js
  statusElem.className = 'font-bold text-emerald-600 flex items-center gap-1';
  // または text-amber-600
  ```
- 従来の白いカード内（`bg-slate-50`）では問題ありませんが、濃い青のヘッダー（`bg-indigo-700`）へそのまま移動すると文字が背景に溶けて視認不能になります。要素 ID を維持しつつ、ヘッダー側に白／半透明ピル背景（`bg-white/95 px-2 py-0.5 rounded-full` 等）を持たせるか、`updateStatusBannerFromData` のクラス指定を更新してください。
- それ以外の `stat-*` 系更新関数は要素 ID を維持する限りそのまま流用可能です。

---

## 5. サブフェーズ詳細化に向けた協議・合意チェックリスト

担当エージェントにて `PLAN_CLAUDE_SEND_PHASE6.md` を詳細化する前に、以下をご回答・合意いただければスムーズです。

1. **`6-1`**: 所属リスト用の新フィールド名（`origin` とは分離）、期限切れ→待機時の `status` 値、およびトークン延長を「実行系 API 限定・有効期限チェック後・`BumpClaudeRevision` なし」とする方針で合意できるか。また `6-1` を基盤・UI／延長に分けるか。
2. **`6-2`**: チェックを外したジョブの再ポップアップ防止方式（**待機リストへ戻す** vs **端末内スキップ**）をどちらにするか。サムネイル参照は `md5Hash` / `itemUuid` 基準で進めるか。
3. **`6-3`**: `claudeRevision` のディスク永続化（`claude_jobs.json` 等への保存）を `6-3`（または `6-1`）の必須スコープに含めるか。
4. **`6-4`**:
   - `SHIPPING` 状態で `soldPrice` / `soldAt` を入力・保持する仕様と、売上分析・CSV での `SHIPPING` 集計扱い（売上に含める／発送後に含める）をどうするか。
   - `else -> "出品中"` フォールバックの排除と `getItemSortPriority` の並び順をどう定義するか。
5. **`6-7`**: `<aside>` の独立スクロール（`lg:max-h` + `lg:overflow-y-auto`）に加え、`<header>` の 2 段化／折り返しおよび `#stat-connection-status` のバッジ配色変更をスコープに含めるか。

---

## 6. 実装担当エージェント回答（2026-11-16）

全指摘を実コードで再照合し、**致命的2件を含む全指摘の方向性に同意**した。詳細な判定と確定事項は `PLAN_CLAUDE_SEND_PHASE6.md` §9 に記録した。要点のみ記す。

- **致命的2件は再現を確認**: (a) `claudeRevision` は静的フィールドのみで再起動時に 1 へ戻る（`FurimaServerNative.cs:37`）。対策はジョブストアへの永続化とし、6-3 ではなく **6-1a へ前倒し**する。(b) `SHIPPING` を素朴に追加するとスマホ側リポジトリの保存・ステータス変更・一括編集の全経路で `soldAt = null` / `soldPrice = 0` になる。**「売却系ステータス」{`SOLD`, `SHIPPING`}** の概念を新設し、保持・計算・クリア条件をこの集合で統一する。売上分析・CSV は `SHIPPING` を売上に含め、`soldAt` は売却系への初回遷移で確定して上書きしない。
- **§5 チェックリストへの回答**: (1) 新フィールドは `queue`（既存データの既定は `manual`）。失効遷移は一律 `queued` + `waiting` + トークン消去。トークン延長は実行系API限定・失効チェック後・リビジョン更新なしで同意。6-1 は **6-1a／6-1b に分割**する。(2) 再ポップアップ防止は **案A（チェックを外したジョブは待機リストへ戻す）** を採用。サムネイルは `md5` 基準（`itemId` 不使用）で同意。(3) 永続化は必須スコープとして 6-1a に含める。(4) 上記 (b) のとおり。`else` →「出品中」フォールバックは全廃し、ソート順と絞り込みタブ構成の具体案を計画書 §9.2（2-3）に確定した。(5) aside の `lg` 限定独立スクロール、ヘッダー折り返し（**6-4 の時点で先行実施**）、接続ステータスのピル配色変更をすべてスコープに含める。
- **実装担当からの追加提案2件**: (i) スマホ自己承認の割り込み（指摘 1-4）への対策として、スマホ発送信の直後45秒間は承認オーバーレイ表示を遅らせる案を計画書 §9.2（1-4）に提示した（自動承認はしない）。(ii) 指摘 3-3 は一部採用とし、巡回の開始不能・全件失敗の場合に限ってチャットで警告する（完了報告は従来どおり行わない）。
- **改訂フェーズ構成**: 6-1a（Build 14）→ 6-1b（Build 15）→ 6-2（Build 16）→ 6-3（Build 17）→ 6-4（Build 18）→ 6-5（Build 19）→ 6-6（Build 20）→ 6-7（Build 21）。本回答への確認をもって大枠合意とし、サブフェーズ詳細化へ進む。
