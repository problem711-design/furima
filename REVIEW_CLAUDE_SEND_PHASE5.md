# フェーズ 5 実装計画書（`PLAN_CLAUDE_SEND_PHASE5.md`）事前コードレビュー・協議メモ

- **レビュー対象コミット**: `1303035`（`docs(plan): add Phase 5 implementation plan for Muse-send batch photo processing`）
- **レビュー実施日**: 2026-11-16
- **照合対象**: GitHub `main` 現行コード（`FurimaServerNative.cs`, `SyncStore.cs`, `DashboardHtml.cs`, `SyncDtos.kt`, `DefaultSettings.kt`, `DefaultSettingsManager.kt`, `PcServerClient.kt`, `FleaMarketRepository.kt`, `FleaMarketViewModel.kt`, `ItemEditScreen.kt`, `SettingsStorageScreen.kt`, `MainActivity.kt`）

---

## 0. 総括

### 設計上の優れている点
1. **データ境界の分離**: ジョブキューを `items_data.json` とは別ファイル（`claude_jobs.json` / `ClaudeJobStore.cs`）に分離しており、既存商品同期のスキーマ・安定性に悪影響を与えない。
2. **同期ループの保護**: `claudeRevision` を既存リビジョンと別管理にし、ViewModel のハートビートループ内で「既存のデータ/設定Pull・`isDirty` 保護に一切触れず、例外を握りつぶしてログのみに留める第3分岐」とする設計は安全。
3. **既存資産調査の精度**:
   - Android 側の設定/DTO が Moshi（`SyncDtos.kt`）、Gson（`DefaultSettingsManager.kt`）、サーバー側が Newtonsoft.Json（`SyncStore.cs` / `FurimaServerNative.cs`）である構造を正しく把握できている。
   - `POST /api/items/{id}/photos/add` が `photo_{md5先頭8桁}{ext}` の自動命名であり、`{元名}_ai{ext}` 返却に流用できない点も見抜いている。
   - `WebDashboardHtml.kt` をバージョン表記更新のみに限定し、スコープ膨張を防いでいる。

一方で、**現行コードと照合した結果、このまま実装に入るとバグ・データ不整合・更新漏れにつながる点が 11 件見つかりました。** 以下、重要度順に詳細を記しますので、実装着手前に方針を協議・確定してください。

---

## 1. 【重大】修正必須の設計・実装上の問題点

### 指摘 1: スマホとPCで `item.id` が一致しないケースがある（`syncUuid` 必須）
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 0.6 / 5-2-3 / 5-3-3
- **現状コードの仕様**:
  - `FurimaServerNative.cs`（`HandleSyncItems` 1835行目付近・`uuidToAssignedPcId`）および `PcServerClient.kt`（909行目）では、PCとスマホで整数 `id` が衝突した場合に別番号を自動採番し、**商品の同一性は `syncUuid` で解決**しています。
  - つまり、スマホ Room DB の `ItemEntity.id` と PC `items_data.json` の `item["id"]` は一致する保証がありません。
- **計画書の不具合**:
  - 5-2-3 の `POST /api/claude/jobs` が `{itemId, photos, templateId, origin}` のみを受け取り、5-3-3 でスマホがそのまま `POST /api/claude/jobs` を呼ぶと、**ID衝突が起きている環境では別商品のジョブが作られる、または 404 エラーになる**重大バグが発生します。
- **修正案**:
  - `POST /api/claude/jobs` のリクエストボディに `itemUuid`（オプショナル、スマホ発では必須送信）を追加する。
  - サーバー側解決ロジックは **`itemUuid` が指定されていれば `syncUuid` 一致を最優先で引き当て、PC側 `item["id"]` を確定**する（`itemUuid` 未指定のPC管理画面発は従来通り `itemId` で引き当て）。

---

### 指摘 2: `ItemEditScreen` の「編集バッファ方式」と 5-3（一括削除／Muse送信）の衝突
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 0.5 / 5-3-1 / 5-3-2 / 5-3-3
- **現状コードの仕様（`ItemEditScreen.kt`）**:
  - `ItemEditScreen` は編集中の写真をローカル状態リスト `existingFilePaths`（158行目〜 / 216行目）・`newImageUris`・`loadedPhotosByPath` で保持し、「保存する」ボタン押下時に `viewModel.saveItem(...)`（971行目）で一括保存する**ドラフト（下書き）編集画面**です。
  - 既存のプレビュー単品削除（353〜366行目）も「即時DB削除」ではなく、`existingFilePaths.removeAt(...)` で編集リストから外し、「保存する」で確定（破棄確認 `hasUnsavedChanges` は 239〜251行目）する仕組みです。
- **計画書 5-3-2 の問題（即時DB削除との齟齬）**:
  - 計画書 5-3-2 は「保存済み写真の一括削除は `FleaMarketRepository.deletePhotosByIds`（即時DB削除・実ファイル削除・`isDirty = true`）へ寄せる」と書いていますが、`ItemEditScreen` 上で `deletePhotosByIds` だけを呼ぶと以下の不整合が起きます:
    1. `existingFilePaths` / `loadedPhotosByPath` が更新されず、消したはずの写真が編集画面に表示され続ける。
    2. `initialExistingFilePaths`（235行目）も更新しないと `hasUnsavedChanges` が true になり、戻る際に「変更を破棄しますか？」と誤表示される。
    3. もしユーザーがそのあと「変更を破棄して戻る」や「保存する」を押した場合、`saveItem` や `existingFilePaths` の状態と DB が食い違う。
  - さらに `deletePhotosByIds` 単体では `triggerAutoSync()` が呼ばれません（`FleaMarketViewModel.removePhotoFromItem` 422〜432行目が `triggerAutoSync()` を呼んでいるのと同様の呼び出しが必要）。
- **計画書 5-3-3 の問題（未保存状態での Muse 送信）**:
  - 新規作成中（`itemId == 0`）や、タイトル・写真を編集してまだ「保存する」を押していない状態で「Museに送る」を押すと、PC側の `items_data.json` やフォルダ名（`{itemId}_{title}`）が古いまま、または商品自体が未登録の状態でジョブが作られてしまいます。
  - さらに、同じ写真でもまだPCへアップロード完了していない（`!photo.isPcSynced` または `photo.md5Hash.isBlank()`）ものはPC側に原本がありません。
- **修正案（協議して確定したい点）**:
  - **5-3-2 一括削除の方式統一**:
    - 案1（画面仕様と最も整合）: 単品削除と揃えて「確認ダイアログ後に `existingFilePaths` / `newImageUris` から一括除外し、保存時に確定する」（この場合は削除取り消しピルは不要・確認ダイアログのみで仕様整合が取れる）。
    - 案2（即時DB確定）: `deletePhotosByIds` を使う場合は、`existingFilePaths`・`loadedPhotosByPath`・`initialExistingFilePaths` を同時更新し、`triggerAutoSync()` を呼ぶ（ただし未保存の他入力がある場合は先に保存を促す等が必要）。
  - **5-3-3 Muse送信前のガード**:
    - `itemId == 0` または `hasUnsavedChanges == true` の場合は「未保存の変更を保存してからMuseに送ってください」とブロック（または「保存して送信」ボタンを用意）。
    - `loadedPhotosByPath[path]` で `photo.md5Hash.isNotBlank() && photo.isPcSynced` の写真のみ送信対象とし、未同期写真・新規URI写真が混在する場合の警告表示を定義する。

---

### 指摘 3: 承認ダイアログ（5-4-3）と完了通知（5-5-4）の配置場所が `ItemListScreen` 限定では不十分
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 5-4-3（199行目）、5-5-4（220行目）
- **現状コードの仕様（`MainActivity.kt` 91〜259行目）**:
  - アプリには `MainTabsContainer` の4タブ（出品一覧 / PC連携 / 売上分析 / 設定・容量）と、`NavHost` 内の別ルート（`item_detail/{itemId}`, `item_edit/{itemId}`, `trash`）があります。
- **問題点**:
  - ユーザーが `ItemEditScreen` から「Museに送る」を押した直後や、`PcSyncScreen`（PC連携タブ：トンネルURL・トークン確認画面）を開いたまま Muse 側の応答を待っている間に `request-approval` が届いた場合、`ItemListScreen` にしか承認ダイアログが無いと**一覧画面へ戻るまで承認ダイアログが表示されず、タイムアウトする**危険が高いです。
  - 同様に 5-5-4 の完了通知も全画面で受け取れた方が自然です。
  - 加えて、Android アプリがバックグラウンドや画面OFFへ移行すると `viewModelScope` 上の `heartbeatJob` は OS の省電力制御で停止・遅延します。
- **修正案**:
  - `pendingClaudeApproval`（5-4-3）と `unackedClaudeCompletions`（5-5-4）のダイアログ表示は `ItemListScreen` ではなく、**`MainActivity.kt` の `FleaMarketApp()` 内（`NavHost` の外側の共通オーバーレイ）** に配置する。
  - 「承認待機中はフリマ管理アプリをフォアグラウンドで開いておく必要がある（バックグラウンド時は次回フォアグラウンド復帰後のハートビートで表示される）」ことを仕様・運用（6-1）に明記する。

---

### 指摘 4: フェーズ 5-1（設定同期）で変更対象ファイルの記載漏れが 4 箇所ある
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 5-1-1 / 5-1-2 / 5-1-3 / 5-1-4
- **照合結果（実コードで漏れていた必須箇所）**:
  1. **`tools/FurimaServerNative.cs` の `ParseDefaultSettingsConfig()`（3058〜3124行目）**
     - `PUT /api/settings`（`HandleUpdateSettings` 3134行目）と `POST /api/sync/items`（`HandleSyncItems` 1981行目）の**両方**が `ParseDefaultSettingsConfig(JObject)` を通して設定をデシリアライズしており、`titleTemplates` と `descriptionTemplates` を手書きで抽出しています。
     - 計画書 5-1 には `SyncStore.cs` しか書かれておらず、`ParseDefaultSettingsConfig()` の更新が抜けると **設定保存／同期のたびにサーバー側の `promptTemplates` が空に初期化される** バグになります。
     - また `SyncStore.SaveDefaultSettings()`（280〜307行目）側にも `if (settings.promptTemplates == null) settings.promptTemplates = new List<TextTemplateConfig>();` が必要です。
  2. **`app/.../data/DefaultSettingsManager.kt`（45〜67行目、103〜104行目）**
     - 計画書 128行目に「永続化がデータクラス経由であることを確認し、不要か検証（手書きキー変換がある場合は追加）」とありますが、実コードは `SharedPreferences` へ `title_templates_json`・`description_templates_json` として**手書きキーで個別保存**しています。
     - そのため `prompt_templates_json` の読み書き追加が**必須**です（未対応だとアプリ再起動で `promptTemplates` が消えます）。
  3. **`app/.../ui/screens/SettingsStorageScreen.kt`（140〜174行目 `persistSettingsNow`）**
     - `persistSettingsNow()` が `currentSettings.copy(...)` ではなく `DefaultSettings(...)` コンストラクタを全引数手書きで呼び出しています（161〜172行目）。
     - ここに `targetPromptTemplates` 引数とリスト状態（`promptTemplatesList`）を渡さないと、別設定（手数料やサムネイル画質など）を保存した瞬間に `promptTemplates` が `emptyList()` で上書き消去されます。
  4. **`tools/DashboardHtml.cs` の設定受け渡し関数群（976〜990行目、1178〜1196行目、1237〜1286行目）**
     - `pcSettings` 初期定義（976行目）、`fetchSettings()`（1184行目）、`savePcSettingsPayload()`（1266行目）の3箇所すべてでフィールド名を手書きマッピングしているため、3箇所すべてに `promptTemplates` を追加する必要があります（`renderTemplateSelects()` 1237行目とも連動）。

---

### 指摘 5: 結果返却 API（5-5-1）とフェーズ 4-4 `FileSystemWatcher` の競合・拡張子矛盾・サニタイズ漏れ
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 5-5-1（211〜214行目）、6-2（231行目）
- **問題点 A（`FileSystemWatcher` との競合・二重更新リスク）**:
  - `FurimaServerNative.cs`（860〜1192行目）ではフェーズ 4-4 の `FileSystemWatcher` が `syncDir` を常時監視しており、ファイル生成を検知すると 400ms 後に `ProcessPendingWatchFiles()` → `CommitAutoImportedPhotosBatch()` が動きます。
  - 5-5-1 で「商品フォルダへファイルを書き込む」→「登録処理を行う」とき、書き込み完了と登録の間でウォッチャー側が別スレッドから同一ファイルへアクセスすると、ファイルロックリトライ（`retryPaths`）・MD5二重計算・`BumpDataRevision()` の二重発火が起き得ます。
  - **対策**: 5-5-1 の書き込み＆登録では、書き込み前にロック外でバイト列のマジックバイト検証＋MD5計算を行い、ファイル書き込み直後（またはロック保護下）で `photoStatCache`・`hashToFilePathMap`・`item["photos"]` を即時更新してウォッチャー側の再取込・再ハッシュを抑制する設計を明記してください。
- **問題点 B（ファイル拡張子仕様の矛盾）**:
  - 5-5-1（212行目）は「`{元ファイル名のベース}_ai{拡張子}` で保存」と記載する一方、6-2（231行目）は「結果はJPGへ変換し返却」としています。
  - 元画像が PNG/WebP だった場合、`元ファイル名の拡張子` をそのまま使うと「中身が JPEG なのにファイル名が `.png`」という破損ファイルになります。
  - **対策**: 拡張子は元ファイル名ではなく **`DetectImageExtension(photoBytes)`（マジックバイト判定結果）** を使うべきです。
- **問題点 C（`X-File-Name` のパストラバーサル対策と元ファイル名特定）**:
  - 外部（Claude）から受け取る `X-File-Name` ヘッダは `Path.GetFileName` ＋ `Path.GetInvalidFileNameChars()` 除去＋長さ制限・`IsIgnoredAutoImportPath` 非該当チェックが必須です。
  - また同一 MD5 を複数商品が共有している場合、`hashToFilePathMap` からは別商品フォルダのファイル名が返る可能性があるため、ベース名の優先順位を「`claude_jobs.json` の `job.photos[].fileName` → `X-File-Name` → `itemFolder` 内ファイル → フォールバック名」と明記すると確実です。

---

## 2. 【中】仕様の明確化・安全性向上のための指摘

### 指摘 6: まとめ商品（Bundle）の写真が送信された場合の挙動が未定義
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 0.5 / 5-2-3 / 5-5-1
- **現状コードの仕様**:
  - PCギャラリー（`DashboardHtml.cs` 2262〜2287行目）はまとめ親商品（`isBundle == true`）を開くと、各写真の `childItemId` を使って内訳表示します。
  - 一方 `HandleMovePhotoToTrash()`（`FurimaServerNative.cs` 3748〜3781行目）では、親から削除した際に子商品側の `photos` 配列からも整合削除しています。
- **未定義点**:
  - まとめ親商品から「Museに送る」を行った結果 `_ai` 写真が返却された場合、
    1. 返却先は「親商品フォルダ（`{parentId}_{parentTitle}`）」なのか、元写真の `childItemId` の「子商品フォルダ」なのか？
    2. `items_data.json` の更新は「親 `item["photos"]` のみ」か、「該当 `childItemId` の写真配列」にも追加するのか（`childItemId` / `childItemTitle` メタを付与するのか）？
- **対策**: まとめ親商品も対象にするなら親・子どちらのフォルダと写真配列に載せるかを仕様化し、複雑さを避けるなら初期リリース（フェーズ5）では「まとめ親は個別商品を開いて送信するか、親フォルダ＋親写真リストへ追加する」等のルールを明記すべきです。

---

### 指摘 7: PCギャラリーの選択状態・ライブ更新時のインデックスずれ・UI配置
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 0.5（62行目）、5-2-4（157〜158行目）
- **現状コードとの差異・問題**:
  1. **関数名の誤記**: 計画書 157行目の `updateViewerSelectionUi` は、現行 `DashboardHtml.cs`（2166行目）では `updateViewerSelectionBar` です。
  2. **ライブ更新での選択ずれ**: 現行の `selectedViewerPhotoIndices`（970行目）は配列インデックス `0, 1, 2...` を保持しています。フェーズ 4-4 のライブ更新（3秒ポーリング → `fetchItemsOnly()` → `renderViewerPhotos()` 1557行目）が裏で走って別端末やフォルダ監視から写真が追加・削除されると、同じインデックスが**別の写真**を指してしまう危険があります。少なくとも送信確認ダイアログで対象枚数・ファイル名を表示する、あるいは選択セットを `md5Hash`（`childItemId` 含むキー）ベースへ移行するのが安全です。
  3. **チェックボックスの文言**: `DashboardHtml.cs`（2338行目）の `title='選択して写真ゴミ箱へ移動'` を汎用文言（例：`写真を選択（ゴミ箱移動・Muse送信）`）に改める必要があります。
  4. **ツールバー幅の逼迫**: `#viewer-action-bar`（391行目）は狭い画面で既にボタンが並びます。「テンプレート選択 `<select>` ＋ Museに送る ＋ ジョブ一覧」を追加するため、2段構成や折り返ししやすいレスポンシブ配置にしておく必要があります。

---

### 指摘 8: セキュリティ・CORS・ジョブトークン境界と期限切れ処理の抜け
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 0.6 / 5-2-3 / 5-4-1 / 7-6
- **確認事項・不足点**:
  1. **CORS 許可ヘッダの追加（必須確認）**:
     - `FurimaServerNative.cs` 1358行目の `Access-Control-Allow-Headers` は現在 `Content-Type, Authorization, X-Photo-Hash` のみです。ブラウザやCORS対象ツールから叩く場合に備え、`X-Claude-Job-Token`, `X-Source-Md5`, `X-File-Name` を追加すべきです。
  2. **Bearer と `jobToken` の役割認識の整理（計画書 115行目、243行目）**:
     - 現行 `AuthenticateRequest`（1314〜1348行目）では `authToken`（Bearer）があれば `GET /api/items` や `GET /api/photos/{md5}` も通過します。したがって、`jobToken` は「Bearer トークン所有者を技術的に他APIから隔離する仕組み」ではなく、**「スマホ側で承認操作が完了しない限り Claude 側のジョブ実行APIが通らないセーフティロック（誤操作・無断実行防止ゲート）」** として定義を正確にしておくべきです（完全な権限分離が必要なら将来的に読み取り専用スコープ別トークン化が必要）。
  3. **一覧APIでの `jobToken` 非露出明記**:
     - 5-4-1（191行目）では `GET /api/claude/jobs/{jobId}` のみ条件付きで `jobToken` を返すとありますが、**`GET /api/claude/jobs`（一覧API）には状態に関わらず絶対に `jobToken` を含めない** ことを明記してください。
  4. **期限切れ判定の網羅と `approval_pending` タイムアウト**:
     - 5-4-1（192行目）では `GET /api/claude/jobs/{jobId}/photos/{md5}` のアクセス時に `jobTokenExpiresAt` 超過を検知して `approval_expired` に戻すとありますが、
       - 1枚も画像取得する前や処理途中で30分経過したときの `POST .../results` / `POST .../complete` / `GET .../jobs/{jobId}` でも期限切れ判定を行うべきです。
       - `approval_pending` のままスマホが応答せずタイムアウトした場合の扱い（例：一定時間経過や `approval_expired` からの再 `request-approval` 可否）も 5-4-1 で定義しておくと運用で詰まりません。

---

### 指摘 9: スマホ側の `_ai` 写真反映タイミング（`hasUnpushedLocalPhoto` ガードの影響）
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 5-5-4（221行目）
- **現状コードの仕様（`PcServerClient.kt` 941〜960行目）**:
  - `mergeServerItemsFromJson` では、対象商品に **「未同期のローカル写真（`!it.isPcSynced && !it.isOriginalDeleted && File(it.filePath).exists()`）」が1枚でも残っている場合、`shouldUpdatePhotos` が false になり、PC側で増えた写真（`_ai` 結果写真）の取り込みをスキップ**します。
- **影響・対策**:
  - 通常の運用（すべて同期済み写真の商品）では問題ありませんが、ユーザーが別写真を撮影・追加した直後（まだ同期完了前）の商品で Muse 結果が返ると、次の自動同期（`triggerAutoSync()`）で写真アップロードが完了するまでスマホ側に `_ai` 写真が見えません。5-5-5 の検証・仕様メモにこの挙動を明記しておくと原因切り分けが容易になります。

---

### 指摘 10: ジョブファイル肥大化・重複返却時の冪等性・リクエストサイズ保護
- **該当箇所**: `PLAN_CLAUDE_SEND_PHASE5.md` 5-2-1 / 5-5-1 / 5-5-2
- **懸念点**:
  1. `claude_jobs.json` に履歴が蓄積し続けるため、完了・確認済み（`acknowledged == true`）・取消済みジョブの自動整理ルール（例：30日経過後削除、または直近N件保持）があると安全です。
  2. 同一 `sourceMd5` に対して `POST /api/claude/jobs/{jobId}/results` が通信エラー等で再送された場合（冪等性）の扱い（二重登録・`_ai2` 増殖を防ぐため、既に `job.results` に同一 `sourceMd5` がある場合の扱い）を定義しておくと安定します。
  3. 結果画像サイズ上限と、メモリ上でマジックバイト検証後に保存するルールを明記しておくと破損ファイル混入を防げます。

---

### 指摘 11: 「Muse」と「Claude」の用語混在ルール
- **該当箇所**: 計画書全体
- **現状**: ファイル名・クラス名・API・JSON名・セクション6は `Claude`（`ClaudeJobStore.cs`, `/api/claude/jobs`, `claudeRevision`）、UIボタン・ダイアログ文言・タイトルは「Muse」（「Museに送る」）となっています。
- **対策**: 「内部識別子・ファイル名・APIパスは `claude` で統一し、ユーザー向けUI文言・ダイアログ・トースト・ドキュメント上のサービス呼称は `Muse` で統一する」という命名規約を冒頭に1行明記することを推奨します。

---

## 3. 協議事項（実装開始前に確定させたい 7 点）

1. **【Q1 / 5-2・5-3】`POST /api/claude/jobs` の商品特定キー**:
   - `itemUuid`（`syncUuid`）をボディに追加し、サーバー側で `itemUuid` 優先解決にすることで合意できるか？
2. **【Q2 / 5-3】`ItemEditScreen` の一括削除と未保存状態ガード**:
   - 一括削除は「即時DB削除＋画面リスト同期」か「編集バッファ除外（保存時確定）」のどちらにするか？
   - `itemId == 0` または `hasUnsavedChanges == true` の状態での「Museに送る」は「保存してから送信」で統一してよいか？
3. **【Q3 / 5-4】承認・完了ダイアログの配置**:
   - `ItemListScreen` 限定ではなく `MainActivity.kt` の `FleaMarketApp()` 全体オーバーレイへ移動してよいか？
4. **【Q4 / 5-5】結果返却の拡張子と `FileSystemWatcher` 排他**:
   - ファイル名は「元ファイル名（`job.photos[].fileName` 優先）ベース ＋ `_ai` ＋ **`DetectImageExtension(bytes)`（実バイト判定拡張子）**」で統一してよいか？
5. **【Q5 / 5-2・5-5】まとめ商品（Bundle）からの Muse 送信可否**:
   - フェーズ5の初期実装では「親フォルダ／親 `photos` のみ対象」「子商品にも連動」「まとめ商品は子商品単位で送る（親からは不可）」のどれを採用するか？
6. **【Q6 / 5-2】PCギャラリーの選択管理**:
   - ライブ更新によるインデックスずれ防止のため、選択状態の管理を配列インデックスから `md5Hash`（または `{md5Hash, childItemId}`）ベースへ切り替えるか、送信直前に一致検証を入れるか？
7. **【Q7 / 5-4】タイムアウト・再承認ルール**:
   - `approval_pending` の自動失効時間を設けるか？ `approval_expired` から再度 `request-approval` を受け付けるか、新規ジョブ作り直しにするか？

---

## 4. 改訂版計画書（`§10 協議結果` 反映後）に対する最終チェック結果（2026-11-16）

改訂版 `PLAN_CLAUDE_SEND_PHASE5.md` は初版レビューの指摘 11 件・協議事項 Q1〜Q7 をすべて適切に取り込んでいます。最終確認として残っていた以下の 4 点も計画書本文へ補完追記しました：

1. **5-1-1 への `ParseDefaultSettingsConfig()` / `SaveDefaultSettings()` 明記**：`tools/FurimaServerNative.cs`（3058〜3124行目）と `tools/SyncStore.cs`（280〜307行目）がチェックリスト本文から漏れていたため追記。
2. **5-4-1 サーバー側 EXIF 回転補正・リサイズの新規実装明記**：`FurimaServerNative.cs` には既存の EXIF 回転処理が存在しない（Android 側 `PhotoStorageManager.kt` のみ）ため、`System.Drawing.Image`（タグ `0x0112` → `RotateFlip`）＋ `MemoryStream` 完結方式での新規実装と、「2048px以下でも EXIF 回転タグ付きは正立化して返す」条件を明確化。
3. **まとめ親商品の UI 側無効化（5-2-4 / 5-3-3）**：サーバー 400 拒否だけでなく、PC ギャラリー（`checkIsBundle(item)`）およびスマホ編集画面（`mergedChildCount > 0`）でも送信ボタンを無効化＋理由表示するガードを追加。
4. **関数名修正（5-2-4）**：`updateViewerSelectionUi` → 現行実コードの `updateViewerSelectionBar`（`DashboardHtml.cs` 2166行目）へ修正。

---

## 5. フェーズ 5-1〜5-5 実装完了後（`e1a42b1` / `Build 12`）コードレビュー・ビルド検証サマリー（2026-11-16）

5-1〜5-5 実装後の実コード・計画書突合およびビルド検証（`compile_applet` 合格、`node --check` 合格、`gradle :app:testDebugUnitTest` 14テスト全合格）を実施した。詳細およびフェーズ 5-1〜5-5 担当エージェントとの協議項目は **`PLAN_CLAUDE_SEND_PHASE5.md` §11** へ統合記載済み。

### 要対応・協議項目の概要
1. **【重要度 高】`DetectImageMimeType()` フォールバックバグ（`tools/FurimaServerNative.cs` 5576〜5600行目）**：PNG/WebP/GIF/BMP 以外を無条件で `"image/jpeg"` 扱いしているため、`POST /results` の 415 拒否がデッドコード化し、`TryBuildClaudeDeliveryCopy` でも HEIF 等を JPEG 誤認する（`FF D8 FF` 判定追加・未一致時 `""` 返却が必須）。
2. **【重要度 高】バイナリ成果物の未更新**：`tools/furima-server.exe`（Phase 4-1 以前のまま）および `flea_market_app.apk`（Build 6 のまま）の更新が必要。
3. **【重要度 中】`ClaudeJobStore` スレッド安全＆ `POST /results` 冪等性競合**：`GetJob()`/`GetJobs()` の生参照返却による列挙例外リスク、および結果保存時の同一 `sourceMd5` 二重書き出し余地。
4. **【重要度 中】スマホ承認・完了ダイアログの失敗時UX**：HTTP 失敗時の通知・ローディング欠如と、`ClaudeCompletionDialog` が PC 不達時に閉じられない問題。
5. **【重要度 中／軽微】その他**：`LocalWebServer.kt` / `WebDashboardHtml.kt` の認証・差分検知ハッシュ・`readAsDataURL`（Phase 4 範囲）、`ItemEditScreen.kt` の `loadedPhotosByPath` 滞留、`ClaudeApprovalDialog.kt` の経過時間表示。

