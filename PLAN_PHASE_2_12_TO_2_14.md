# フェーズ 2.12〜2.14 実装計画書（エージェント引き継ぎ用仕様書）

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン：**`v0.9β (Build 2)`**  
前提：実機インストール済み（本運用開始前のクリーン段階）。Google Play ストア公開用ではなく、ローカルビルド APK および `furima-server.exe` として運用する。

---

## 0. 本計画書の運用・引き継ぎルール（必須遵守）

1. **1サブフェーズずつ逐次実装する**：
   - 最小単位（`2.12-A` ➔ `2.12-B` ➔ `2.12-C` ...）ごとに実装・検証・記録・ローカルコミットを行い、途中で別のエージェントへ引き継ぐ場合も直後から迷わず再開できる状態を保つ。
2. **各フェーズ完了時の必須作業（`PROGRESS.md` ＋ Git コミット）**：
   - 各フェーズ／サブフェーズ完了時、必ず以下を実施すること。
     1. 本計画書 (`PLAN_PHASE_2_12_TO_2_14.md`) の該当チェックボックス `[ ]` を `[x]` に更新する。
     2. `PROGRESS.md` のロードマップ・更新履歴・現在バージョン表記を更新する。
     3. 変更した対象ファイルを選択的に `git add` して `git commit` を実行する（※ GitHub リモートへの `git push` は、トークンが共有されている場合のみ実施し、未共有時はローカルコミットまでに留めてユーザーへ報告・委ねる）。
3. **厳格なバージョン表記・同時統一更新規則**：
   - フェーズ完了時（またはコード変更を含むビルド更新時）には、以下3箇所のバージョン表記を**必ず同時に統一更新**する。
     - `app/build.gradle.kts`（`versionCode` / `versionName`）
     - `tools/DashboardHtml.cs`（ヘッダーバナー右側のバッジ）
     - `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`（ヘッダー右側のバッジ）
4. **禁止事項・環境制約**：
   - `debug.keystore` / `debug.keystore.base64` / `signingConfigs` は絶対に変更しない。
   - `tools/build-exe.bat` は手動調整済みのため変更しない。
   - アルファベット1文字の直後に半角コロンが付く文字列をコードコメント・ドキュメント・返答に含めない。
   - Kotlin ロジック変更時は `compile_applet` および `gradle :app:testDebugUnitTest` でビルド・単体テスト通過を検証する。純粋な HTML/CSS/JS のみの調整時は不要な Gradle ビルドを省略する。

---

## 1. 確定済みの設計方針（ユーザー合意事項）

実装着手前に以下4点の方針が確定している（推測で覆さないこと）。

1. **拡大プレビュー（写真ビューアー）の原本取得対応**：
   - 詳細画面・編集画面のサムネイルタップで全画面拡大プレビューを開く際、スワイプ・ピンチズームに加えて、**端末内に高画質原本が無い場合（PC同期後にサムネイル化済み等）でも PCサーバー接続中なら「PCから高画質原本を取得」ボタンから原本を取得・拡大表示できる**ようにする。
2. **「ゴミ箱」機能の最初から本命実装**：
   - 一時退避等の応急処置ではなく、**最初からスマホ・PC共通の正式な「ゴミ箱（論理削除＋復元＋完全削除＋30日自動削除）」を実装**する。本運用前のため必要に応じた DB スキーマ更新・初期化も許容される（ただし既存データ保全のため Room マイグレーション `v5 ➔ v6` も併せて丁寧に定義する）。
3. **一人利用最適の同期アーキテクチャへの刷新**：
   - スマホとPCを「同時に」編集することはほぼなく、「スマホで作業 ➔ 後からPCを見る」「PCで編集 ➔ スマホに反映」「外出先でオフライン操作 ➔ 帰宅後に再接続」という1人利用のワークフローに最適化する。
   - **従来の「○秒ごとの無条件全件送信/全件取得」および「スマホに無いものをPCから即削除」を廃止**し、**「変更操作ごとのイベント駆動Push ＋ リビジョン番号検知による必要な時だけPull ＋ `isDirty`/`updatedAt` 保護 ＋ ゴミ箱（`deletedAt`）和集合マージ ＋ UUID衝突防止」** へ移行する。
4. **テンプレート＆デフォルトプリセットの最小影響実装**：
   - 商品 DB (`ItemEntity` / `ItemDao`) には手を加えず、既に `SharedPreferences` ⇄ `store.json` 間で同期されている `DefaultSettings` / `SyncSettingsDto` / `DefaultSettingsConfig` を拡張して実装する。

---

## 2. 全体フェーズ構成・ロードマップ

| 順番 | フェーズ | 内容概要 | 目標バージョン |
| :--- | :--- | :--- | :--- |
| 1 | **2.12** | スマホ長押しメニュー不具合2件修正 ＋ PC原本取得ボタン付き写真拡大プレビュー | **`v0.9β (Build 2)`** |
| 2 | **2.13** | 設定双方向同期基盤 ＋ デフォルトプリセット変更強化 ＋ タイトル/説明文テンプレート機能 | **`v0.9β (Build 3)`** |
| 3 | **2.14** | 本命「ゴミ箱」機能（論理削除/復元/完全削除） ＋ イベント駆動・競合防止同期への刷新 | **`v0.9β (Build 4)`** |

---

## 3. フェーズ 2.12 詳細仕様（スマホUIバグ修正＆高画質拡大プレビュー）

目標バージョン：**`v0.9β (Build 2)`**

### 3.1 サブフェーズ 2.12-A — 長押しメニューのステータス表示不一致修正
- [x] **実装完了**
* **問題点**：
  * `app/src/main/java/com/example/myapplication/ui/components/ItemQuickActionDialog.kt`（70行目・173〜185行目）が `val isSold = item.status == "SOLD"` の2択のみで判定しているため、`DRAFT`（下書き）や `MERGED`（まとめ済）の商品でもタイトル脇バッジが緑色の「出品中」と表示される。
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/ui/components/ItemQuickActionDialog.kt`
* **実装要件**：
  1. `CompactItemCard` (`ItemListScreen.kt`) と同じ4ステータス判定に統一する。
     - `"SOLD"` ➔ 背景 `Color(0xFFC62828)` / 表示 `"売却済"`
     - `"MERGED"` ➔ 背景 `Color(0xFF6A1B9A)` / 表示 `"まとめ済"`
     - `"DRAFT"` ➔ 背景 `Color.Gray` / 表示 `"下書き"`
     - その他 (`"LISTED"`) ➔ 背景 `Color(0xFF2E7D32)` / 表示 `"出品中"`
  2. `DRAFT` 商品のステータス切り替えアクション表記も実態に合わせて違和感のない表示にする（「出品中にする」または「売却済みにする」を意図通り分かりやすく整理）。
* **完了条件**：下書きカード・出品中カード・売却済カードを長押しした際、メニュー上部のステータスバッジがカード表示と100%一致すること。

### 3.2 サブフェーズ 2.12-B — 長押しメニューからの削除/解除後にメニューが閉じない不具合修正
- [x] **実装完了**
* **問題点**：
  * `ItemQuickActionDialog.kt` の削除確認 `confirmButton`（84〜87行目）およびまとめ解除確認 `confirmButton`（110〜113行目）で、`showDeleteConfirm = false` の後に `onDismiss()` が呼ばれていない。
  * さらに呼び出し元の `ItemListScreen.kt`（144〜153行目）の `onDelete` / `onUnmerge` でも `quickActionItem = null` にしていないため、確認ダイアログ終了後も長押しメニューが開いたまま残り連続タップ可能になる。
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/ui/components/ItemQuickActionDialog.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt`
* **実装要件**：
  1. `ItemQuickActionDialog.kt` の削除確認確定・まとめ解除確認確定のクリック時に、`showDeleteConfirm = false` / `showUnmergeConfirm = false` に続けて即座に `onDismiss()` を呼び出す（二重クリック防止フラグも併用）。
  2. `ItemListScreen.kt` の `onDelete` および `onUnmerge` 開始/完了時にも `quickActionItem = null` を設定し確実に閉じるようにする。
* **完了条件**：長押しメニューから削除またはまとめ解除を実行した直後、確認ダイアログと長押しメニューの両方が即座に閉じること。

### 3.3 サブフェーズ 2.12-C — サムネイル拡大プレビュー（PC高画質原本取得ボタン付き）＆ Build 2 統一
- [x] **実装完了**
* **対象ファイル**：
  * 新規：`app/src/main/java/com/example/myapplication/ui/components/PhotoPreviewDialog.kt`
  * 更新：`app/src/main/java/com/example/myapplication/ui/screens/ItemDetailScreen.kt`
  * 更新：`app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  * 更新：`app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`（原本バイナリ取得ヘルパー公開）
  * バージョン更新：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
* **実装要件**：
  1. `PhotoPreviewDialog.kt` の新設：
     - `Dialog(properties = DialogProperties(usePlatformDefaultWidth = false))` による全画面黒背景モーダル。
     - `HorizontalPager` による左右スワイプ切替、上部にページ番号（例 `1 / 4`）と閉じるボタン。
     - `detectTransformGestures`（ピンチ拡大 1倍〜4倍・パン移動）およびダブルタップでの拡大/等倍切替。
     - `PreviewPhotoItem` モデルで「端末ファイル（`File`）」「選択中 `Uri`」「`PhotoEntity`（`md5Hash`, `isOriginalDeleted`, `displayFile`）」を統一表現できるようにする。
     - **PC原本取得機能**：表示中の写真について端末内原本が存在せず（`isOriginalDeleted == true` または原本パス不在/サムネイルと同パス）、かつ `md5Hash` があり `isPcServerConnected == true` の場合、下部アクションバーに **「PCから高画質原本を取得」** ボタンを表示する。タップで `PcServerClient.downloadPhotoByHash` を呼び出し、一時キャッシュまたはメモリバイト列（またはキャッシュファイル）として高解像度画像を表示に切り替える（※通常時はスマホストレージを無駄に圧迫しないよう、明示タップ時のみキャッシュ領域へ取得し、再表示時はキャッシュ優先・終了時/アプリキャッシュ管理に委ねる）。
  2. `ItemDetailScreen.kt` の写真グリッド（618行目〜）に `.clickable` を追加し、タップしたインデックスを初期ページとして `PhotoPreviewDialog` を開く。
  3. `ItemEditScreen.kt` の写真サムネイル `LazyRow`（346〜402行目）に `.clickable` を追加し、既存写真（`displayFile` 補完含む）＋新規追加 `Uri` をまとめたリストを `PhotoPreviewDialog` に渡して拡大表示する（右上の削除 `×` ボタンとはタップ判定を競合させない）。
  4. バージョンを **`v0.9β (Build 2)`** へ3箇所同時更新し、`compile_applet` & `gradle :app:testDebugUnitTest` で検証する。

---

## 4. フェーズ 2.13 詳細仕様（設定双方向同期・デフォルトプリセット変更・テンプレート機能）

目標バージョン：**`v0.9β (Build 3)`**

### 4.1 サブフェーズ 2.13-A — 設定の双方向同期基盤＆モデル拡張（Room DB 無変更）
- [x] **実装完了**
* **設計方針**：
  * 商品テーブルには手を加えず、既存の `DefaultSettings` (`SharedPreferences`) ⇄ `SyncSettingsDto` ⇄ PC側 `DefaultSettingsConfig` (`store.json`) の経路を拡張する。
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/data/local/DefaultSettings.kt`
  * `app/src/main/java/com/example/myapplication/data/DefaultSettingsManager.kt`
  * `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `tools/SyncStore.cs`
  * `tools/FurimaServerNative.cs`
* **実装要件**：
  1. `TextTemplate`（`id: String`, `name: String`, `content: String`）モデルを追加し、`DefaultSettings` / `SyncSettingsDto` / `DefaultSettingsConfig` に以下を追加する：
     - `titleTemplates: List<TextTemplate>`（初期サンプルまたは空リスト）
     - `descriptionTemplates: List<TextTemplate>`（初期サンプルまたは空リスト）
     - `settingsUpdatedAt: Long`（設定の最終更新UNIXミリ秒。双方向同期での新旧判定用）
  2. PCサーバー（`FurimaServerNative.cs`）に **`PUT /api/settings`** エンドポイントを追加：
     - PCブラウザ管理画面からデフォルトプリセットやプリセット一覧・テンプレートを保存でき、`store.json` へ永続化するとともに `settingsUpdatedAt` を更新する。
  3. 双方向設定同期ロジック（`PcServerClient.kt` / `FleaMarketViewModel.kt`）：
     - スマホの `updateDefaultSettings` 呼び出し時に `settingsUpdatedAt = System.currentTimeMillis()` を刻み、接続中なら `triggerAutoSync()`（または設定Push）を自動実行する。
     - スマホ側が `GET /api/settings` を取得した際、PC側の `settingsUpdatedAt` がスマホ側より新しい場合はスマホ側の `DefaultSettingsManager` へ自動反映し、逆にスマホ側が新しい場合は次回の `POST /api/sync/items` で PC側を上書き（`HandleSyncItems` 側でも `settingsUpdatedAt` を比較し、古い情報で新しいPC設定を先祖返りさせないよう保護）する。

### 4.2 サブフェーズ 2.13-B — デフォルトプリセット変更・編集・両端適用UI
- [x] **実装完了**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  * `tools/DashboardHtml.cs`
* **実装要件**：
  1. **スマホ設定画面 (`SettingsStorageScreen.kt`)**：
     - 現在デフォルトに設定されているプリセットを明確に識別表示（「★ デフォルト」表示）。
     - プリセットの「追加」だけでなく、既存プリセットの「名称・手数料・送料・経費の編集」および「デフォルトに設定（即時保存＋PC同期）」を分かりやすい操作で可能にする（初期4プリセットも値の編集やデフォルト指定が可能、最低1件は残るガード付きでカスタムプリセット削除可）。
  2. **スマホ編集画面 (`ItemEditScreen.kt`)**：
     - 「個別費用設定」が有効なとき、PC側と同じように登録済みプリセット一覧からワンタップで手数料・送料・経費を入力欄へ反映できるチップ行を追加する。
  3. **PC管理画面 (`DashboardHtml.cs`)**：
     - 右コントロールパネルに「設定・プリセット・テンプレート管理」ボタン（モーダル）を追加し、PCブラウザからもデフォルトプリセット（`activePresetId`）の切り替え、プリセットの新規追加・編集・削除、`PUT /api/settings` による保存が完結できるようにする。保存直後から「新規出品追加」や非カスタム商品の利益表示へ即反映する。

### 4.3 サブフェーズ 2.13-C — タイトル／商品説明テンプレート登録・編集・選択挿入UI ＋ Build 3 統一
- [x] **実装完了**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/ui/screens/SettingsStorageScreen.kt`
  * `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  * `app/src/main/java/com/example/myapplication/ui/components/MergeItemsDialog.kt`
  * `tools/DashboardHtml.cs`
  * バージョン更新：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
* **実装要件**：
  1. **テンプレート管理UI（スマホ `SettingsStorageScreen.kt` ＋ PC `DashboardHtml.cs` 設定モーダル）**：
     - 「タイトル用テンプレート」「商品説明用テンプレート」の2カテゴリで、追加・編集・削除・並び替え（または登録順表示）が可能。
  2. **入力欄への選択挿入UI（スマホ `ItemEditScreen.kt` / `MergeItemsDialog.kt` ＋ PC `DashboardHtml.cs` 追加/編集/まとめモーダル）**：
     - タイトル欄・説明文欄の近くに「定型文を挿入」ドロップダウン／チップボタンを配置。
     - スマホ側は `TextFieldValue` の `selection`（または末尾追記＋上書きオプション）を使って**現在のカーソル位置へ自然に文字列を挿入**できるよう実装する。
     - PC側は `selectionStart` / `selectionEnd` を用いた JS ヘルパー `insertTextAtCursor(elementId, text)` でカーソル位置へ挿入（＋ `checkTitleLength` と変更通知発火）を行う。
  3. バージョンを **`v0.9β (Build 3)`** へ3箇所同時更新し、`compile_applet` & `gradle :app:testDebugUnitTest` で検証する。

---

## 5. フェーズ 2.14 詳細仕様（本命「ゴミ箱」機能 ＋ 一人利用最適・競合防止同期の刷新）

目標バージョン：**`v0.9β (Build 4)`**

### 5.1 サブフェーズ 2.14-A — Room DB拡張（`syncUuid` / `deletedAt` / `isDirty`）＆ 論理削除/復元リポジトリ
- [ ] **実装完了**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/data/local/ItemEntity.kt`
  * `app/src/main/java/com/example/myapplication/data/local/AppDatabase.kt`
  * `app/src/main/java/com/example/myapplication/data/local/ItemDao.kt`
  * `app/src/main/java/com/example/myapplication/data/FleaMarketRepository.kt`
* **実装要件**：
  1. `ItemEntity` へ同期・論理削除用フィールドを追加（Room Version 5 ➔ 6、`MIGRATION_5_6` 実装。既存データがある場合はマイグレーションで各行に一意 UUID と `isDirty = 1` または初期値付与）：
     - `syncUuid: String`（一意な同期識別子 `UUID.randomUUID().toString()`。PC新規作成とのID衝突防止用）
     - `deletedAt: Long? = null`（非nullなら「ゴミ箱」入り日時。通常一覧・売上集計からは除外）
     - `isDirty: Boolean = true`（スマホで変更・新規・削除・復元があり PC へ未同期のとき `true`）
  2. `ItemDao.kt` のクエリ整理：
     - 既存の通常一覧・集計クエリ（`getAllActiveItemsWithPhotos`, `getActiveItemsWithPhotosDirect`, `getAllActiveItems` 等）すべてに **`AND deletedAt IS NULL`** を適用し、ゴミ箱アイテムが一覧や売上計算に混入しないようにする。
     - 同期用全件取得（`getAllItemsWithPhotosForSync()`）はゴミ箱アイテムも含めて取得（状態伝播のため）。
     - ゴミ箱専用クエリ追加：`getTrashedItemsWithPhotos()`（`WHERE deletedAt IS NOT NULL ORDER BY deletedAt DESC`）、`restoreFromTrash(id, updatedAt)`、`markPendingPushCleared(ids)` 等。
  3. `FleaMarketRepository.kt` の削除仕様刷新：
     - 通常の「削除」(`moveToTrash`) は **`deletedAt = now`, `updatedAt = now`, `isDirty = true` を設定する論理削除** とし、この時点では写真ファイルも削除しない（即座に元に戻せる）。
     - 「ゴミ箱から復元」(`restoreFromTrash`) で `deletedAt = null`, `updatedAt = now`, `isDirty = true` に戻す。
     - 「完全削除」(`permanentlyDeleteItem`) または「ゴミ箱から30日以上経過した商品の自動完全削除」で初めて写真ファイルと DB レコードを物理削除する。
     - まとめ買い商品の削除時・解除時の親子整合（親がゴミ箱に入った場合の子商品扱い、またはゴミ箱からの単体復元ルール）も破綻しないよう明記して制御する。

### 5.2 サブフェーズ 2.14-B — イベント駆動Push ＋ リビジョン検知Pull ＋ 競合防止同期エンジン
- [ ] **実装完了**
* **対象ファイル**：
  * `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`
  * `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
  * `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
  * `tools/FurimaServerNative.cs`
  * `tools/SyncStore.cs`
* **実装要件**：
  1. **PCサーバー側のリビジョン管理 (`FurimaServerNative.cs`)**：
     - `dataRevision`（商品・写真・まとめ・ゴミ箱・完全削除の変更ごとにインクリメント）と `settingsRevision`（`PUT /api/settings` ごとにインクリメント）を管理し、`/api/server/heartbeat` および `/api/server/status` の JSON 応答に含める。
     - 各アイテムに `syncUuid`（未指定ならサーバー側で採番、PC新規作成時は新UUID採番）および `deletedAt`（論理削除日時）、`permanentlyDeletedUuids`（完全削除されたUUIDの履歴リスト：再接続時のゾンビ復活防止用、一定期間保持）を保持する。
     - **`HandleSyncItems` における「スマホに無いものを無条件削除（`itemsDb.RemoveAll` ＋ `Directory.Delete`）」を廃止**：
       - スマホから届いた商品リストを `syncUuid`（フォールバックで旧データ用に `id`）で照合し、和集合マージする。
       - 双方に存在する場合は **`updatedAt` が新しい方を採用**。
       - PC側で新規作成・更新されたアイテム（スマホ側の知識より新しいもの）はスマホの送信リストに含まれていなくても保護して残す。
       - フォルダ物理削除は「明示的な完全削除 API（`DELETE /api/items/{id}/permanent`）または30日超過ゴミ箱クリーンアップ」時にのみ実行する。
  2. **スマホ側のイベント駆動 ＋ リビジョン監視同期 (`FleaMarketViewModel.kt` / `PcServerClient.kt`)**：
     - **変更発生時 Push**：商品保存・ステータス変更・まとめ・解除・ゴミ箱移動・復元・完全削除・設定変更の直後に `triggerAutoSync()` を実行（接続中なら500msデバウンス後にPush）。
     - **再接続時ハンドシェイク**：ハートビートが `未接続 ➔ 接続中` に切り替わった瞬間、まず未同期（`isDirty == true` または設定未送信）があれば **Push を先行**してサーバー側へ反映し、その直後に `GET /api/items`（差分確認）で PC側の新規/更新分を取り込む。
     - **軽量ハートビートループ**：接続中の定期ループ（4〜5秒ごと）では毎回全商品を取得せず、ハートビート応答の `dataRevision` / `settingsRevision` をチェックし、**前回取得時と値が変化していたときだけ** `fetchServerItems()` / 設定Pull を実行する。
     - **`isDirty` 保護**：`fetchServerItems()` はローカルで `isDirty == true` かつローカル `updatedAt > serverUpdatedAt` のアイテムを**絶対に上書きしない**。
     - **ID衝突防止**：PC側で作成された未知の `syncUuid` をスマホ側 Room に取り込む際、スマホ側に既に同じ数値 `id` が別商品として存在していたら `id = 0` で自動採番インサートする（`syncUuid` を真のキーとすることでオフライン新規作成の番号被りによる上書きを根絶）。

### 5.3 サブフェーズ 2.14-C — スマホ＆PC共通「ゴミ箱」UI ＋ Build 4 統一
- [ ] **実装完了**
* **対象ファイル**：
  * スマホ：`app/src/main/java/com/example/myapplication/ui/screens/TrashScreen.kt`（または `ItemListScreen.kt` 内のフィルター／専用ダイアログ・画面）およびナビゲーション導線（トップバーアイコン等）
  * PC：`tools/DashboardHtml.cs`（フィルターに「ゴミ箱」タブ、復元ボタン、完全削除ボタン、「ゴミ箱を空にする」操作）
  * バージョン更新：`app/build.gradle.kts`, `tools/DashboardHtml.cs`, `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt`
* **実装要件**：
  1. **スマホ側 UI**：
     - 出品一覧画面のトップバー等（既存レイアウト・配色を崩さないアイコンボタン等）から「ゴミ箱（件数バッジ付き）」へ遷移／表示できるようにする。
     - 削除確認ダイアログの文言も「ゴミ箱へ移動します（後で復元できます）」へ更新。
     - ゴミ箱画面では各削除済み商品について「元に戻す（復元）」「完全に削除（写真ファイルも消去）」および一括「ゴミ箱を空にする」を実行可能にする（30日自動削除の残り日数も表示）。
  2. **PC管理画面 UI (`DashboardHtml.cs`)**：
     - ステータスフィルターに「ゴミ箱 (`TRASH`)」を追加し、ゴミ箱入り商品一覧を表示可能にする。
     - 各カードに「復元」ボタンと「完全削除」ボタンを用意し、通常一覧での削除ボタンも「ゴミ箱へ移動（復元可能）」確認に変更。
  3. 30日超過ゴミ箱アイテムの自動完全削除（起動時およびPC側クリーンアップ時）を組み込み、バージョンを **`v0.9β (Build 4)`** へ3箇所同時更新、`compile_applet` & `gradle :app:testDebugUnitTest`（同期競合・ゴミ箱復元・マイグレーションの新規ユニットテスト追加）で検証する。

---

## 6. テスト計画（検証観点）

各フェーズ完了時に少なくとも以下を確認する：
1. **Android コンパイル & 単体テスト**：`compile_applet` および `gradle :app:testDebugUnitTest` がエラーなく全件通過すること。
2. **フェーズ 2.14 向け新規ユニットテスト追加（Robolectric / JUnit）**：
   - 論理削除 ➔ 復元で商品・写真パスが維持されること。
   - `isDirty == true` のローカル未同期更新が `fetchServerItems` 相当の古いサーバーデータで上書きされないこと。
   - 異なる `syncUuid` で同じ `id` が衝突した場合でも、両方の商品が別レコードとして保持されること。
   - `DefaultSettings` のテンプレート追加/更新が JSON シリアライズ・復元で失われないこと。
