# フェーズ 2.8〜2.11 実装計画書（エージェント引き継ぎ用仕様書）

対象: 個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン: **`v2.9 (Build 25)`**（リポジトリ `main` ブランチ最新状態基準）  
前提: Google Play ストア公開用ではなく、ローカルビルド APK および `furima-server.exe` として運用する。

---

## 0. 本計画書の運用・引き継ぎルール（必須遵守）

1. **1フェーズ（または1サブフェーズ）ずつ逐次実装する**:
   - 最小単位（例 `2.8-A` → `2.8-B`）ごとに実装・検証・記録・コミットを行い、途中でトークン上限や環境切替が発生しても直後から別のエージェントが迷わず再開できる状態を保つ。
2. **各フェーズ完了時の必須作業（`PROGRESS.md` ＋ Git ＋ GitHub 同期）**:
   - 各フェーズ／サブフェーズ完了時、必ず以下の3点をセットで実施すること。
     1. 本ファイル (`PLAN_PHASE_2_8_TO_2_11.md`) の該当チェックボックス `[ ]` を `[x]` に更新する。
     2. `PROGRESS.md` のロードマップ・更新履歴・現在バージョン表記を更新する。
     3. 変更した対象ファイルを選択的に `git add` して `git commit` し、**`git push origin main` で GitHub リモートへ反映する**（サンドボックス等にプッシュ権限がない場合は、ローカルコミットを完了させた上でユーザーへプッシュ操作を明示的に依頼する）。
3. **バージョン表記・同時統一更新規則**:
   - コード変更を含むサブフェーズ（またはフェーズ統合時）には、`app/build.gradle.kts`（`versionCode` / `versionName`）、`tools/DashboardHtml.cs`、および `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` のバージョン表記を **必ず同時に統一更新** する。
4. **既存UI・禁止事項**:
   - `debug.keystore` / `debug.keystore.base64` / `signingConfigs` は絶対に変更しない。
   - `tools/build-exe.bat` は手動調整済みのため原則変更しない。
   - アルファベット1文字の直後にコロンが付く文字列をコードコメント・ドキュメント・返答に含めない。
   - 確認済みテストコマンドは `gradle :app:testDebugUnitTest`（C# 側は Windows 環境の `tools/build-exe.bat` または `csc` でビルド検証）。

---

## 1. 確定済みの設計方針（ユーザー合意事項）

実装着手前に以下3点の方針が確定している（推測で変更しないこと）。

1. **Yahoo!フリマ画面の表示方式（案A採用）**:
   - `DashboardHtml.cs` の `openYahooSplit()` で `window.open(..., 'left=...,menubar=no,...')` のようにウィンドウサイズやUI無効化オプションを指定しているために「半端なサイズの簡易ブラウザ（ポップアップ）」になっている問題を解消する。
   - **オプション指定を外して通常のブラウザ新規タブまたは通常ウィンドウとして開き**、画面左右配置は Windows 標準のスナップ操作（`Win + ←` / `Win + →`）を用いる方針（確実性重視・案内トースト表示）。
2. **CSV出力の扱い（スマホ側修正維持 ＋ PC側追加）**:
   - スマホ側の `CsvExporter.kt` は小規模修正で不具合を直せるため削除せず修復して残し、**あわせて PC管理画面 (`DashboardHtml.cs`) 側にも BOM付き UTF-8 CSV ダウンロード機能を追加** する。
3. **PC母艦サーバーのブラウザ選択ダイアログ表示タイミング**:
   - 起動直後の単発ハートビートや未確認通信で即座に開くのではなく、**PC管理画面およびトレイGUIのステータスが「スマホアプリ接続中」（緑バナー状態＝有効な接続確認・認証通信が成立した状態）になってから** ブラウザ選択ダイアログを開く。

---

## 2. 全体フェーズ構成・ロードマップ

| 順番 | フェーズ | 内容概要 | 変更規模 | 目標バージョン |
| :--- | :--- | :--- | :--- | :--- |
| 1 | **2.8** | 同期競合防止・QRコード完全修復・接続後ブラウザ起動・Yahoo画面開き方修正 | 中 | v2.8 (Build 24) |
| 2 | **2.9** | 作成/更新/売却日時の双方向同期・PC表示優先度ソート・CSV修正&PC追加 | 中 | v2.9 (Build 25) |
| 3 | **2.10** | バックボタン安全化・サムネ容量可視化・詳細「PCに送信」分離&「ギャラリーに保存」刷新 | 中 | v2.10 (Build 26) |
| 4 | **2.11** | PC新規/編集モーダルのスマホ同等化（プリセット・チップ・写真追加削除・0円許容） | 大 | v2.11 (Build 27) |

---

## 3. フェーズ 2.8 詳細仕様（サーバー安定化・QR修復・接続導線）

### 3.1 フェーズ 2.8-A — まとめ作成直後の同期競合防止
* **問題点**:
  * スマホ側 `FleaMarketViewModel.kt` の `syncDeltaToPcServer()` および `triggerAutoSync()` は、先に PUSH (`syncDeltaToPcServer`) を実行してから PULL (`fetchServerItems`) を行っている。
  * サーバー側 `FurimaServerNative.cs` (`HandleSyncItems`) では、`itemsArr`（スマホ側送信リスト）に含まれないまとめ親商品を削除する際、`itemsArr.Any(p => mPid == 0)`（1つでも未まとめ商品があれば真）という条件で親商品を `isDeleted = true` にしてしまう。これにより PC でまとめを作成した直後にスマホから PUSH が飛ぶと、PC上の新まとめ親が即座に削除・巻き戻される恐れがある。
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `tools/FurimaServerNative.cs`
* **実装要件**:
  1. `FleaMarketViewModel.kt` の `triggerAutoSync()` および `syncDeltaToPcServer()` を **PULL先行型** に変更する（接続URL有効時はまず `fetchServerItems()` でPC側の最新状態・新規まとめ・削除を取り込んでから `syncDeltaToPcServer()` を実行し、完了後に必要に応じ再度状態反映する。また多重起動排他用 `Mutex` または同期中ガードを検討する）。
  2. `FurimaServerNative.cs` の `HandleSyncItems` において、PC側で作成したまとめ親商品に対する誤 `isDeleted = true` 判定を厳格化する（まとめ解除は明示 API `POST /api/items/{id}/unmerge` を正とし、PUSH で親を削除扱いする場合は「PC側にあるその親の具体的な子商品ID群が、親作成時刻より後の状態で実際にすべて `mergedParentId == null` になっている場合」等の厳密条件に限定、または `HandleSyncItems` での自動親削除を廃止する）。
* **完了条件**: PCでまとめ商品を作成した直後にスマホ側で他商品の編集・自動同期を行っても、まとめ親商品が消滅せずスマホ側にも正しく反映されること。

### 3.2 フェーズ 2.8-B — QRコード生成の完全修復（ISO/IEC 18004 準拠化）
* **問題点**:
  * `tools/TunnelQrForm.cs` の `SimpleQrEncoder` には以下の不具合があり、「QRコード風画像」になっていて読み取れない。
    1. `CalculateReedSolomonECC` の生成多項式生成ループで `genPoly[0]` 側をシフト乗算しており、ECC計算が全バイト誤っている。
    2. QR格納文字列 `furima:<trycloudflare-url>|<token>|<storeId>` は約 115〜125 バイトあるが、`EncodeToMatrix` が Version 5-L（最大 106 バイト）までしか対応しておらずデータが切り捨てられている。
* **対象ファイル**:
  * `tools/TunnelQrForm.cs`
* **実装要件**:
  1. `SimpleQrEncoder` を ISO/IEC 18004 仕様に完全準拠した .NET 4.0 純C# エンコーダに置き換える（外部DLL不要構成を維持、または `tools/lib` 構成方針に従う）。
     * **必須対応範囲**: Version 1〜10（最大 271 バイト / Level L なら十分余裕あり）の8bitバイトモード、Version 1〜9 (8bit) と Version 10 (16bit) の文字数指示子切替、ブロック分割表に基づくマルチブロック RS ECC 計算とデータ/ECC インターリーブ、Remainder bits、全 Version 別のアライメントパターン座標配置、Version 7以上で必要な 18bit Version Information 配置、ダークモジュール `(4*V + 9, 8)`、8パターンマスクのペナルティスコア評価による最適マスク＋対応 Format Information 配置、クワイエットゾーン 4 セル。
  2. 生成したQR文字列の自己検証（または簡易デコードテスト用スクリプト/確認手順）で、`furima:https://example-abc.trycloudflare.com|1234567890ab|xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`（120バイト超）が正しくQR画像化されスキャンできることを確認する。
* **完了条件**: 120バイト以上のトンネル接続文字列を含むQRコード画像が、一般的なQRスキャナーおよびスマホアプリの「QRコードを読み取る」で即時認識されること。

### 3.3 フェーズ 2.8-C — ブラウザ選択ダイアログの表示タイミング改善
* **問題点**:
  * 現在は `/api/server/heartbeat` 等の初回到達瞬間に即 `OnAppConnected()` が呼ばれて `BrowserSelectDialog` が開く。ユーザーがスマホ側の接続確認（緑バナー状態）を見る前や起動直後のバックグラウンド疎通でも立ち上がることがある。
* **対象ファイル**:
  * `tools/FurimaServerNative.cs`
  * `tools/TunnelQrForm.cs`
* **実装要件**:
  1. `lastHeartbeat` 更新後、サーバー側の接続状態（`isConnected`＝緑バナー相当）が確立した上で、`BrowserSelectDialog` を開くように整える。具体的には、起動直後の自動待機通信のみではなく「有効なトークン認証を通過した接続確認・同期（または連続したハートビート確認）によりフォーム上の表示が緑『● スマホアプリ接続中』に切り替わり、ユーザーが接続成立を確認できる状態になってから（わずかなUI反映後・または確認ダイアログ付きで）」ブラウザ選択画面を開く。
  2. 2回目以降の通常ハートビートや同期で何度も勝手にダイアログが出ないよう、`hasPromptedBrowser` ガードを維持する。
* **完了条件**: 未接続時（黄色ステータス時）は勝手に開かず、スマホと正規接続されて緑ステータスになった後に1度だけブラウザ選択ダイアログが開くこと。

### 3.4 フェーズ 2.8-D — Yahoo!フリマ出品画面の開き方調整（案A）＋ Version Build 24 統一
* **問題点**:
  * `DashboardHtml.cs` の `openYahooSplit()` が `window.open` にサイズや `menubar=no` を渡しているため、Chrome/Edge でお気に入りや拡張機能の使えないポップアップ（簡易ブラウザ）として開いてしまう。
* **対象ファイル**:
  * `tools/DashboardHtml.cs`
  * `app/build.gradle.kts` (`versionCode = 24`, `versionName = "2.8"`)
  * `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v2.8 (Build 24)`)
  * `PROGRESS.md` / `PLAN_PHASE_2_8_TO_2_11.md`
* **実装要件**:
  1. `openYahooSplit()` の実装を変更し、第3引数（`left=...` / `menubar=no` 等）を渡さず `window.open('https://paypayfleamarket.yahoo.co.jp/sell', '_blank')` で **通常のフル機能ブラウザタブ** として開く（またはボタンラベルを「Yahoo!フリマ出品画面を開く」＋補足ツールチップ「タブを切り離して Win + ← / → で左右に並べて利用できます」に調整）。
  2. トースト通知で「Yahoo!フリマを通常タブで開きました（左右分割する場合はタブを別ウィンドウに出して Win + → で配置できます）」と案内する。
  3. 3箇所のバージョン表記を `v2.8 (Build 24)` に統一し、ビルド・単体テスト通過後、`PROGRESS.md`・本計画書を更新してコミット＆GitHubプッシュを行う。

---

## 4. フェーズ 2.9 詳細仕様（日時同期・PC表示優先度・CSV修正/PC追加）

### 4.1 フェーズ 2.9-A — 作成・更新・売却日時のスマホ⇔PC双方向完全同期
* **問題点**:
  * `SyncItemDto.kt` に `createdAt`, `updatedAt`, `soldAt` が含まれておらず、`HandleSyncItems` および `fetchServerItems` が同期時刻で `updatedAt` / `createdAt` を上書きしてしまっている。このままではPC画面の「登録・変更の新しい順」ソートやPC側CSV出力の日時が正確にならない。
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  * `tools/FurimaServerNative.cs`
* **実装要件**:
  1. `SyncItemDto` に `createdAt: Long? = null`, `updatedAt: Long? = null`, `soldAt: Long? = null` を追加。
  2. スマホ PUSH (`syncDeltaToPcServer`) 時は Room DB の実タイムスタンプを送信し、サーバー `HandleSyncItems` では受信した値を保持する（PC側レコードに既に新しい `updatedAt` がある場合の保護や、`createdAt` 未設定時の既存値保持を含む）。
  3. スマホ PULL (`fetchServerItems`) 時はサーバー JSON の `createdAt`, `updatedAt`, `soldAt` を反映し、不要な `System.currentTimeMillis()` 上書きをやめる。
  4. `HandleUpdateItem` / `HandleMergeItems` / `HandleCreateItem` でも `soldAt`（`SOLD` 遷移時設定、非`SOLD`時クリア）を正しく管理する。
* **完了条件**: スマホ⇔PC間で同期を繰り返しても商品の元々の `createdAt`・`updatedAt`・`soldAt` が維持されること。

### 4.2 フェーズ 2.9-B — PC管理画面の表示優先度（並び順）変更
* **対象ファイル**:
  * `tools/DashboardHtml.cs`
* **実装要件**:
  1. `renderItems()` の描画直前に、以下の **5段階優先度＋新着順ソート** を適用する。
     * 優先度1 — **出品中まとめ商品** (`checkIsBundle(item) && item.status === 'LISTED'`)
     * 優先度2 — **未出品（下書き）まとめ商品** (`checkIsBundle(item) && item.status === 'DRAFT'`)
     * 優先度3 — **通常・出品中商品** (`!checkIsBundle(item) && item.status === 'LISTED'`)
     * 優先度4 — **通常・下書き商品** (`!checkIsBundle(item) && item.status === 'DRAFT'`)
     * 優先度5 — **売却済み商品** (`item.status === 'SOLD'`)
     * **同一グループ内の並び順**: `Math.max(item.updatedAt || 0, item.createdAt || 0)` の降順（新しい順）、同値の場合は `item.id` の降順。
  2. あわせて、「未出品」タブ・バッジ判定 (`updateStats` / `renderItems`) で `item.status === 'DRAFT' || !item.isShared` としている箇所を見直し、PC側もスマホ側と整合するように `status === 'DRAFT'` を「未出品（下書き）」、`status === 'LISTED'` を「出品中」として明確に分類する（未同期を示す場合は別の同期ステータス表記にする）。
* **完了条件**: 上記5段階の順番、および各グループ内で最新作成/変更アイテムが先頭に並ぶこと。

### 4.3 フェーズ 2.9-C — スマホ側CSVバグ修正 & PC管理画面CSV出力追加 ＋ Version Build 25 統一
* **問題点（調査で判明した既存バグ）**:
  * スマホ側 `CsvExporter.kt` は `photo.syncStatus == "SYNCED"` のみを参照しているため、`isPcSynced = true` のみに更新される現行プロトコルv3同期環境では、原本削除前の商品が常に「未送信」と出力される。また商品説明文列が欠落している。
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/data/CsvExporter.kt`
  * `tools/DashboardHtml.cs`
  * `app/build.gradle.kts` (`versionCode = 25`, `versionName = "2.9"`)
  * `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v2.9 (Build 25)`)
  * `PROGRESS.md` / `PLAN_PHASE_2_8_TO_2_11.md`
* **実装要件**:
  1. `CsvExporter.kt`:
     * `syncStatusText` の判定条件を `photo.isPcSynced || photo.syncStatus == "SYNCED" || photo.isOriginalDeleted` に修正する。
     * 「商品説明」列（改行含む安全なCSVダブルクォートエスケープ対応）を追加し、単体テスト (`CsvExporterTest` 等) で出力内容を検証する。
  2. `DashboardHtml.cs`:
     * 右サイドバー「コントロール」パネルに **「売上・全商品CSVダウンロード」ボタン** を追加。
     * 現在の `allItems`（論理削除除外、必要に応じ `MERGED` 子商品の扱いも注記または親＋一覧として出力）から、スマホ側 `CsvExporter` と互換のある列構成（BOM付き UTF-8 `flea_market_pc_YYYYMMDD_HHMM.csv`）で即時ブラウザダウンロードさせる。
  3. バージョン表記を `v2.9 (Build 25)` に統一し、テスト・ビルド検証後に `PROGRESS.md`・本計画書を更新してコミット＆GitHubプッシュを行う。

---

## 5. フェーズ 2.10 詳細仕様（スマホ側操作安全・容量可視化・詳細画面写真周りの刷新）

### 5.1 フェーズ 2.10-A — バックボタン挙動改善
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  * `app/src/main/java/com/example/myapplication/MainActivity.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt`
* **実装要件**:
  1. **`ItemEditScreen.kt`（商品作成・編集時の未保存破棄確認）**:
     * 初期ロード完了時点のフォーム値・既存写真パス一覧を `initialSnapshot` として保持し、現在の入力値・写真リスト・`newImageUris` と比較して `hasUnsavedChanges` を算出する。
     * `BackHandler(enabled = hasUnsavedChanges)` と TopAppBar の「←」アイコン両方で `showDiscardDialog = true` とし、「変更を保存せずに戻りますか？（破棄して戻る / 編集を続ける）」の確認 `AlertDialog` を表示する。
     * 「破棄して戻る」選択時、編集画面内で新規撮影された未登録の一時カメラファイルがあれば削除してクリーンに破棄する。
  2. **`MainActivity.kt` / `ItemListScreen.kt`（メインタブ戻り & 終了確認）**:
     * `MainTabsContainer` 内で、「出品一覧」タブ（`selectedTab == 0`）をメインタブとして扱う。
     * `selectedTab != 0` のときバックボタン押下 → `selectedTab = 0`（出品一覧）へ戻す。
     * `selectedTab == 0` のとき、もし `isSelectionMode`（まとめ買い複数選択モード）が有効なら先に `viewModel.clearSelection()` で解除する。通常状態であれば終了確認 `AlertDialog`（「アプリを終了しますか？」）を出し、確定で `Activity.finish()` を呼ぶ。
* **完了条件**: 編集中に誤ってバックしても確認ダイアログが出てデータが消えないこと。設定タブ等からバックすると出品一覧に戻り、出品一覧からバックすると終了確認が出ること。

### 5.2 フェーズ 2.10-B — サムネイル保存設定の確認と容量可視化
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt`
  * `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt`
* **実装要件**:
  1. `PhotoStorageManager.kt` に以下2つの計測ヘルパーを追加する。
     * 端末内の現在保存中サムネイルフォルダ総容量・原本フォルダ総容量を取得する関数。
     * 現在のスライダー設定値（`thumbQuality`, `thumbMaxDim`）を使って、メモリ上の代表的なテスト画像（または実存写真1枚をソースにしたインメモリ試算）をJPEG圧縮した際の **1枚あたり予想容量 (KB) と削減率** を算出する関数。
  2. `SettingsStorageScreen.kt` の「サムネイル保存画質設定」カード内に、**「現在の端末内サムネイル使用量（〇枚・合計〇〇 KB/MB）」** と **「現在の品質・解像度設定での1枚あたり目安サイズ（テスト圧縮実測値〇〇 KB）」**、および「※設定変更は今後登録する写真に適用されます」の案内を表示する。
  3. `FleaMarketRepository.kt` の `seedInitialTestDataIfNeeded` でも、必要に応じ `createThumbnail` を通してサムネイルを別生成するよう整理する（実写と同じ圧縮設定挙動に合わせる）。
* **完了条件**: 設定画面で品質・解像度スライダーを動かすと、容量目安の変化が即座に数値で確認できること。

### 5.3 フェーズ 2.10-C — 「PCに送信」の LocalSend/QuickShare 専用化（同期誤認の解消）
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt`
* **実装要件**:
  1. `FleaMarketViewModel.kt` の `shareItemWithZip()` から `repository.markItemShared(item.id)` の呼び出しを **完全に削除** する（単発の LocalSend / QuickShare 送信で `isShared` フラグを汚さない・原本削除対象に誤認させない）。
  2. ZIP同梱時の写真パス取得を `it.filePath` 固定から `it.displayFile?.absolutePath`（原本が存在すれば原本、削除済みならサムネイルへフォールバック）に改め、母艦同期後の商品でも空ZIPにならないようにする。
  3. `ItemDetailScreen.kt` のボタンラベル・表記を「他アプリでZIP共有 (LocalSend / QuickShare等)」等に変更し、サーバー自動同期機能と混同しない表示に整える。
* **完了条件**: 本ボタンからZIP共有しても商品の `isShared` 状態やサーバー同期ステータスが勝手に変わらないこと。

### 5.4 フェーズ 2.10-D — 「ギャラリーに保存」刷新 & 認証付き原本取得・MD5自己修復基盤 ＋ Version Build 26 統一
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  * `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt`
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt`
  * `app/build.gradle.kts` (`versionCode = 26`, `versionName = "2.10"`)
  * `tools/DashboardHtml.cs` (`v2.10 (Build 26)`)
  * `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v2.10 (Build 26)`)
  * `PROGRESS.md` / `PLAN_PHASE_2_8_TO_2_11.md`
* **実装要件**:
  1. **認証付き写真ダウンロード＆自己修復基盤 (`PcServerClient.kt`)**:
     * `downloadPhotoByHash(baseUrl, token, md5Hash): ByteArray?` を実装（`GET /api/photos/{md5Hash}` へ `Authorization: Bearer <token>` ヘッダーを付与して取得し、取得後にローカルでもMD5一致を検証可能）。
     * **サムネイル自己修復機構**: 商品表示や詳細取得・同期時に、端末のサムネイルファイルが欠損している、あるいは実ファイルの MD5 と DB上の `md5Hash` が食い違う（かつ原本不在等）場合、サーバーオンライン中であれば `/api/photos/{md5Hash}` から画像を取得してサムネイルを生成・修復する仕組みを組み込む。
  2. **`ItemDetailScreen.kt` の「ギャラリーに保存」フロー刷新**:
     * ボタン押下時、まず商品の写真一覧（サムネイル＋選択チェックボックス＋全選択/解除）を選べるダイアログまたはボトムシートを表示する。
     * 保存確定時、`viewModel.isPcServerConnected` によって分岐する：
       * **PCサーバーオフライン時**: 選択された写真の端末内ファイル（原本優先・なければサムネイル `displayFile`）を `Pictures/FleaMarket/{商品名}` に保存して完了トースト表示（原本なしの写真が含まれる場合は「未接続のためサムネイルを保存しました」等と案内）。
       * **PCサーバーオンライン時**: 選択された写真について「PCサーバーから高画質オリジナル画像を取得してギャラリーに保存しますか？（PCから高画質を取得して保存 / 端末内の写真のみで保存）」の確認ダイアログを表示する。「高画質を取得」が選ばれた場合は `downloadPhotoByHash` でサーバーから取得した原本バイト列を直接 `MediaStore`（`Pictures/FleaMarket/{商品名}`）へ書き出す（端末ストレージを不必要に圧迫しない。失敗時は端末内ファイルへフォールバック）。
  3. バージョン表記を `v2.10 (Build 26)` に統一し、テスト・ビルド検証後に `PROGRESS.md`・本計画書を更新してコミット＆GitHubプッシュを行う。

---

## 6. フェーズ 2.11 詳細仕様（PC管理画面 新規・編集モーダルのスマホ同等化）

※ 改修規模が大きいため、バックエンド同期基盤 → API整備 → フロントエンドモーダルUI の3段階に分けて実装する。

### 6.1 フェーズ 2.11-A — 基本費用設定・プリセット一覧のスマホ→PC同期
* **対象ファイル**:
  * `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  * `tools/SyncStore.cs`
  * `tools/FurimaServerNative.cs`
* **実装要件**:
  1. `SyncItemsRequest`（または専用 `/api/settings` エンドポイント）に、スマホの `DefaultSettings`（`defaultFeeRatePercent`, `defaultShippingCost`, `defaultExpenseCost`, `activePresetId`, `presets: List<FeePreset>`）を含めて送信する。
  2. `SyncStore.cs` の `StoreConfig`（`store.json`）に基本設定・プリセット配列を保存・読み込みできるようにし、`GET /api/settings`（または `GET /api/server/status`）で PC管理画面の JS が取得できるようにする。
* **完了条件**: スマホ側で設定・追加したモール別プリセット（Yahoo!フリマ、メルカリ、ラクマ、カスタム等）を PC管理画面側でも参照できること。

### 6.2 フェーズ 2.11-B — PC側作成/更新の0円許容 & 写真削除API・スマホ同期追従
* **対象ファイル**:
  * `tools/FurimaServerNative.cs`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
* **実装要件**:
  1. `HandleCreateItem` の `if (shippingCost <= 0) shippingCost = 210` 等のハードコード強制置換を修正し、`json["shippingCost"] == null`（未指定）の場合のみ `SyncStore` のデフォルト値を使う（**0円設定を正しく許容** する）。
  2. 個別写真削除API `POST /api/items/{id}/photos/delete`（リクエストボディ `{ "md5Hash": "..." }`）を `FurimaServerNative.cs` に追加し、`itemsDb` 内の該当商品の `photos` 配列から対象を削除して `updatedAt` を更新し `items_data.json` を保存する。
  3. スマホ側 `PcServerClient.kt` の `fetchServerItems` において、PC側で写真が追加・削除されて枚数やハッシュ集合が変わった際（0枚に削除された場合も含む）、スマホ側 Room DB の `photos` もサーバー側の最新構成へ追従同期するよう改善する。
* **完了条件**: PCから送料0円・経費0円で商品作成できること。PC側で商品の写真を削除した際、次回同期でスマホ側からもその写真紐付けが外れること。

### 6.3 フェーズ 2.11-C — PC新規出品・編集モーダルUIのスマホ同等化 ＋ Version Build 27 統一
* **対象ファイル**:
  * `tools/DashboardHtml.cs`
  * `app/build.gradle.kts` (`versionCode = 27`, `versionName = "2.11"`)
  * `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` (`v2.11 (Build 27)`)
  * `PROGRESS.md` / `PLAN_PHASE_2_8_TO_2_11.md`
* **実装要件**:
  1. **PC新規追加モーダル (`add-modal`)・PC編集モーダル (`edit-modal`) 両方へのスマホ同等UI実装（カメラ除く）**:
     * **写真管理セクション**: モーダル内に「画像ドラッグ＆ドロップ／ファイル選択」エリアとサムネイル一覧（削除ボタン付き）を実装。
       * 新規追加時は選択ファイルをJSメモリ配列に保持し、「出品を追加」押下時に `POST /api/items` → 返却された `id` に対して `POST /api/items/{id}/photos/add` を順次送信する。
       * 編集時はモーダル内で既存写真をプレビュー＆削除（`POST /api/items/{id}/photos/delete`）および新規ファイル追加（`POST /api/items/{id}/photos/add`）を完結できるようにする。
     * **ステータス選択チップ**: 下書き / 出品中 / 売却済み（編集時は売却時のみ実売価格欄表示）。
     * **費用設定・プリセット＆チップ**: スマホ側と同期したモール別プリセットチップ、個別費用設定スイッチ、手数料率チップ (`5%`, `10%`)、送料チップ (`210円`, `450円`, `750円`)、経費入力欄、およびリアルタイム純利益シミュレーションカードを搭載。
  2. まとめ作成モーダル (`merge-modal`) も必要に応じデフォルト費用参照を `SyncStore` の設定値に連動させる。
  3. バージョン表記を `v2.11 (Build 27)` に統一し、テスト・ビルド検証後に `PROGRESS.md`・本計画書を更新してコミット＆GitHubプッシュを行う。

---

## 7. 進捗チェックリスト（他エージェント引き継ぎ用）

- [x] **フェーズ 2.8-A**: まとめ作成直後の同期競合防止（PULL先行化・Mutex排他・サーバー側保護と解除判定厳格化）
- [x] **フェーズ 2.8-B**: QRコード生成の完全修復（ISO/IEC 18004 準拠純C#エンコーダ化 Version 1〜12対応）
- [x] **フェーズ 2.8-C**: ブラウザ選択ダイアログの表示タイミング改善（緑ステータス確定後＋キャンセルボタン正常化）
- [x] **フェーズ 2.8-D**: Yahoo!フリマ画面の通常タブ開き（案A）＋ Version 2.8 (Build 24) 統一
- [x] **フェーズ 2.9-A**: `createdAt` / `updatedAt` / `soldAt` の双方向完全同期
- [x] **フェーズ 2.9-B**: PC管理画面の5段階優先度ソート & 未出品ステータス判定整頓
- [x] **フェーズ 2.9-C**: スマホ側 `CsvExporter` 修正 & PC側CSVエクスポート追加 ＋ Version 2.9 (Build 25) 統一
- [ ] **フェーズ 2.10-A**: バックボタン改善（編集破棄確認 ＋ タブ戻り/選択解除/終了確認）
- [ ] **フェーズ 2.10-B**: サムネイル保存設定の検証 & 圧縮後容量・使用容量の可視化
- [ ] **フェーズ 2.10-C**: 商品詳細「PCに送信」の LocalSend/QuickShare 専用化（`isShared` 分離）
- [ ] **フェーズ 2.10-D**: 商品詳細「ギャラリーに保存」刷新 & 認証付き原本取得/MD5自己修復基盤 ＋ Version 2.10 (Build 26) 統一
- [ ] **フェーズ 2.11-A**: 基本費用設定・プリセット一覧のスマホ→PC同期 (`store.json`)
- [ ] **フェーズ 2.11-B**: 0円設定許容 & 個別写真削除API & スマホ写真削除追従
- [ ] **フェーズ 2.11-C**: PC新規/編集モーダルUIのスマホ同等化 ＋ Version 2.11 (Build 27) 統一
