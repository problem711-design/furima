# 統合実装計画書（フェーズ 5）：「Museに送る」一括画像加工連携（プロンプトテンプレート共有 ➔ ジョブキュー ➔ スマホ承認 ➔ 結果の同一フォルダ返却）

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ ＋ PC母艦サーバー ＋ スマホ緊急Web管理画面）
現行バージョン：**`v1.0.0 (Build 7)`**（フェーズ 4-1 〜 4-4 全完了済み）
策定日：**2026-11-16**（計画策定のみ。本書のコミット時点ではコード未変更）

> **事前コードレビュー状態（2026-11-16）**：現行コードベースとの突合レビューを実施済みです。実装着手前に **`REVIEW_CLAUDE_SEND_PHASE5.md`** の指摘事項（`syncUuid` 解決・`ItemEditScreen` 編集バッファ整合・承認ダイアログ配置・5-1 設定同期漏れ箇所・`FileSystemWatcher` 競合・まとめ商品扱い等）の協議・反映を行ってください。

> **進行・引き継ぎルール（必須）**
> - 本計画は `PROGRESS.md` の開発・引き継ぎ基本原則（小規模逐次実装／最小限のファイル編集／フェーズ完了ごとの `PROGRESS.md` 記録／フェーズごとの Git コミット＆GitHub `main` プッシュ）に従う。
> - バージョン更新時は `app/build.gradle.kts`・`tools/DashboardHtml.cs`・`app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` の3箇所を必ず同時に統一更新する。
> - サブフェーズ完了ごとに本計画書のチェックボックスと `PROGRESS.md` を更新し、GitHub `main` へ即座にコミット・プッシュする。

---

## 0. 全体フェーズ構成と現在地（変更規模の小さい順）

- [] **フェーズ 5-1 (`v1.0.0 Build 8`) — プロンプトテンプレートの共有基盤（設定同期への相乗り）**
- 既存のタイトル／説明テンプレートと同型の `promptTemplates` を設定に追加し、既存の設定同期（`GET/PUT /api/settings`）でスマホ⇔PC共有。新規APIなし。
- [] **フェーズ 5-2 (`v1.0.0 Build 9`) — ジョブキュー基盤（サーバー保存＋作成API＋PCギャラリー送信UI）**
- ジョブストア新設、ジョブ作成／一覧／詳細／キャンセルAPI、`claudeRevision` の新設（ハートビート＆ステータス応答への追加のみ）、PCギャラリーにテンプレート選択プルダウン＋「Museに送る」ボタン＋ジョブ一覧モーダル。
- [] **フェーズ 5-3 (`v1.0.0 Build 10`) — スマホ側サムネイル複数選択（一括削除＋Muse送信）**
- 編集画面サムネイル一覧に選択モードを新設（現行はプレビュー画面からの単品削除のみ）。選択を一括削除とMuse送信の両方に使う。スマホ発のジョブ作成は 5-2 のAPIをそのまま利用。
- [] **フェーズ 5-4 (`v1.0.0 Build 11`) — スマホ承認フロー（同期リビジョン経由・最も依存関係が強い）**
- 実行開始時の承認リクエストを、既存ハートビートのリビジョン検知に相乗りしてフリマ管理アプリへ届け、承認でジョブ単位・期限付きトークンを発行。Muse側の画像取得はジョブ専用APIに限定。
- **認証リクエストの宛先は Muse アプリではなくフリマ管理アプリ**（ユーザー確定事項）。
- 照合コードは**実装しない**（ユーザー確定事項：使い捨てトンネル＋非24時間稼働＋サーバー内に支払い情報なし、を前提にスマホ承認のみで十分と判断）。
- [] **フェーズ 5-5 (`v1.0.0 Build 12`) — 結果返却・完了通知・確認（ack）共有**
- 加工結果を元写真と同じ商品フォルダへ別名で書き戻し登録。完了通知をPC管理画面／スマホ管理アプリ／Museチャットへ出し、どれか1つで確認（ack）されたら全端末で共有して消える。
- [] **フェーズ 5-6（Build更新なし・運用） — Claude側処理手順の固定と試運転**
- Claude側の定型手順（キュー取得→承認待ち→原本取得→1枚ずつ新規生成→JPG変換→返却→完了報告）を固定し、1ジョブ・数枚で試運転してから常用する。

### 確定済みの設計判断（ユーザー協議済み・2026-11-16）
1. 実データはPC側管理が前提。スマホ側「Museに送る」は「PCへMuse送信リクエストを送る」機能とする。
2. プロンプトテンプレートの実体はPC側（設定ストア）に一元化し、スマホ・PC双方が同じ一覧をプルダウン表示する。
3. 実行はスマホとPCの両方がサーバーへ接続している時だけ可能とし、スマホ側の承認が無ければ実行しない（fail-closed）。
4. トンネルURLと認証情報は実行の都度スマホから渡す（サーバーは24時間稼働ではない）。
5. 承認は「選択時」ではなく「実行開始時」に取る。承認で発行する鍵はジョブ単位・短時間有効・そのジョブの画像取得と結果返却だけに使えるものに絞る。
6. 承認画面には商品名・枚数・テンプレート名・返却先フォルダを表示する。
7. 処理完了後は通知を出し、確認（ack）は全端末で共有する（加工中、ユーザーは並行して別作業をする前提）。
8. 生成は各写真とも「元の写真から新規生成」を原則とする（編集の継ぎ足しは構図が固定化し別写真に使えないため）。

---

## 0.5. 調査結果：再利用できる既存資産（2026-11-16 精査）

### サーバー（`tools/FurimaServerNative.cs`、4569行）
- ルーティングは `HandleRequest` 内の `rawPath` 分岐一箇所（1374行目付近〜）。新規APIはここへ分岐追加するだけでよい。
- **認証は既存**：`AuthenticateRequest` が「ループバックは無認証／それ以外の `/api/` は `Authorization: Bearer <authToken>` 必須」を実施。`authToken` は `store.json`（`SyncStore.AuthToken`）に永続化され、スマホとはQR等で共有済み。→ Claudeの外部アクセスもこのBearerを土台にし、その上に 5-4 のジョブ承認を重ねる。
- **リビジョン機構は既存**：`dataRevision` / `settingsRevision`（34〜35行目、`BumpDataRevision()` / `BumpSettingsRevision()` / Getter は155〜172行目）。`/api/server/status` と `/api/server/heartbeat` の**両方**の応答生成箇所（1448〜1495行目付近）で両リビジョンを返している。→ `claudeRevision` を同パターンで追加する（**2箇所の応答生成を両方とも直すこと**が必須の注意点）。
- 商品フォルダは `syncDir/{itemId}_{safeTitle}`、商品DBは `syncDir/items_data.json`、設定は `store.json` の `StoreConfig.defaultSettings`。
- `POST /api/items/{id}/photos/add` は**リクエストボディの生バイトを `photo_{md5先頭8桁}{拡張子}` の自動名で保存**する。ファイル名を指定できないため、結果返却には流用しない（5-5 でジョブ専用APIを新設し、登録処理のみフォルダ取込スキャンの内部処理を再利用する）。
- フェーズ 4-4 の `FileSystemWatcher` 自動取込＋ `POST /api/items/{id}/photos/scan` により「商品フォルダへ別名で書き戻された画像が自動登録される」土台は既にある。5-5 の返却はこの登録経路と整合させる。

### 設定・テンプレート（`tools/SyncStore.cs` / Android各所）
- `DefaultSettingsConfig`（SyncStore.cs 24行目〜）に `titleTemplates` / `descriptionTemplates`（`List<TextTemplateConfig{id, name, content}>`）を保持し、`GET/PUT /api/settings` で同期。`settingsUpdatedAt` で新旧判定。
- Android側の対応箇所：`data/local/DefaultSettings.kt`（`titleTemplates` / `descriptionTemplates`）、`data/server/SyncDtos.kt`（`SyncSettingsDto`）、`data/server/PcServerClient.kt`（`toSyncSettingsDto` / `applyServerSettingsDto` の相互変換）、設定UIは `ui/screens/SettingsStorageScreen.kt`。
- PC管理画面（`tools/DashboardHtml.cs`）：`pcSettings.titleTemplates` 等を保持し、テンプレート編集モーダル（`template-editor-modal`、`renderTemplateListHtml`、カテゴリ `title` / `desc` の分岐）で編集→設定保存する実装が既にある。→ `promptTemplates` はこの全箇所へ3つ目のカテゴリとして追加する。

### 写真ゴミ箱ストア（新ストアの型）
- `SyncStore.cs` の `PhotoTrashEntry` ＋ `photo_trash.json` 管理（`GetPhotoTrashEntries` 等）が「サーバー側JSONファイル＋静的ストアクラス」の確立パターン。→ ジョブストアはこの型に倣い、新規ファイル `tools/ClaudeJobStore.cs` ＋ `claude_jobs.json` とする（`tools/build-exe.bat` のコンパイル対象リストへの追加が必要。先例：フェーズ 2.15-C の `TunnelQrForm.cs` 追加）。

### PC管理画面ギャラリー（`tools/DashboardHtml.cs`、3711行）
- ギャラリー（ビューア）には選択状態 `selectedViewerPhotoIndices`（商品内の写真インデックス集合）と「選択した写真をゴミ箱へ移動」ボタン（`viewer-trash-selected-btn`、`trashSelectedViewerPhotos()`）が既にある。→ 「Museに送る」ボタンとテンプレートプルダウンはこの隣へ追加し、選択状態を共用する。
- リビジョン検知ポーリング `pollServerRevisions()`（3秒間隔、`setInterval` 1132行目付近）が `/api/server/status` を見て `dataRevision` を追跡している。→ `claudeRevision` も同じポーリングで読み、ジョブ一覧の再取得に使う。

### 同期・ハートビート（Android、依存関係が最も強い箇所）
- `ui/FleaMarketViewModel.kt` の `heartbeatJob`（824行目付近〜）が `PcServerClient.sendHeartbeatWithRevision()` を定期実行し、`HeartbeatInfo{isConnected, dataRevision, settingsRevision}`（`SyncDtos.kt` 100行目付近、`PcServerClient.kt` 264行目付近）の変化で設定Pull／データPullを分岐実行している（`isDirty` / `updatedAt` 保護あり）。
- **承認フローの相乗り設計**：`claudeRevision` をハートビート応答へ追加し、ViewModel側に「既存のデータ／設定Pull分岐とは独立した第3分岐」を新設する。この分岐はジョブ状態の取得だけを行い、商品データのPull・マージには一切触れない。取得失敗時はログのみで次のハートビートで再試行し、既存同期を巻き込まない。
- サーバー再起動でリビジョンは初期値に戻るため、比較は既存と同様 `!=`（不一致＝変化）で行う（`>` 比較は使わない）。

### スマホ側の写真まわり（Android）
- 編集画面 `ui/screens/ItemEditScreen.kt` のサムネイル一覧には複数選択が無く、削除は `ui/components/PhotoPreviewDialog.kt` 経由の単品削除（`onDeletePhoto`、削除取り消しピル `DeletedEditPhoto`）のみ。→ 5-3 で選択モードを新設する。
- 写真の安全な一括削除はリポジトリ側に既にある（フェーズ 4-1-2 で整備された `FleaMarketRepository.deletePhotosByIds`）。→ 一括削除はこれを使う。
- 商品一覧側の選択モードはフェーズ 3.2-C で実装済みのため、操作感（長押し開始・チェックボックス重ね表示・選択中アクションバー）はそれに倣う。

---

## 0.6. 新規データモデルと新規API一覧

### 設定への追加（既存 `DefaultSettingsConfig` / `DefaultSettings` / `SyncSettingsDto`）
- `promptTemplates: List<TextTemplateConfig>`（`{id, name, content}`。`content` は画像生成プロンプト文そのもの）。既存フィールドと同型・追加のみで後方互換（旧データに無ければ空リスト）。

### ジョブ（新規 `claude_jobs.json`、`ClaudeJobStore.cs` で管理）
```
ClaudeJob {
jobId: string (GUID), itemId: int, itemUuid: string, itemTitle: string,
folderName: string, // 返却先フォルダ名 {itemId}_{title}
photos: [{ md5, fileName}], // 対象写真（MD5が同一性キー）
templateId: string, templateName: string,
promptText: string, // 作成時点のテンプレート内容スナップショット
origin: "pc" | "phone",
status: "queued" | "approval_pending" | "approved" | "processing"
| "done" | "done_with_errors" | "denied" | "approval_expired" | "cancelled",
createdAt: long, approvalRequestedAt: long?,
jobToken: string?, jobTokenExpiresAt: long?, // 承認時のみ発行・サーバー内のみ保持
results: [{ sourceMd5, resultFileName, resultMd5}],
errors: [{ sourceMd5, message}],
completedAt: long?, acknowledged: bool, acknowledgedAt: long?
}
```

### 新規API（全て既存Bearer認証の対象。ジョブ専用APIは追加でジョブトークンを要求）
| メソッド・パス | 用途 | 主な利用者 | 導入 |
|---|---|---|---|
| `POST /api/claude/jobs` | ジョブ作成（商品・写真MD5群・テンプレートIDを指定。プロンプトはサーバー側でスナップショット） | PC管理画面／スマホ | 5-2 |
| `GET /api/claude/jobs` | ジョブ一覧（`status` クエリで絞り込み可） | PC管理画面／スマホ／Claude | 5-2 |
| `GET /api/claude/jobs/{jobId}` | ジョブ詳細（`approved` 時のみ `jobToken` を含む） | 全て | 5-2 |
| `POST /api/claude/jobs/{jobId}/cancel` | ジョブ取消（`queued` / `approval_pending` のみ） | PC管理画面／スマホ | 5-2 |
| `POST /api/claude/jobs/{jobId}/request-approval` | 実行承認リクエスト（`queued` → `approval_pending`、`claudeRevision` 更新） | Claude | 5-4 |
| `POST /api/claude/jobs/{jobId}/approval` | 承認／拒否（承認で `jobToken` 発行・有効期限30分） | スマホ（フリマ管理アプリ） | 5-4 |
| `GET /api/claude/jobs/{jobId}/photos/{md5}` | 対象写真の原本取得（ジョブトークン必須。初回取得で `processing` へ遷移） | Claude | 5-4 |
| `POST /api/claude/jobs/{jobId}/results` | 結果1枚の返却（ジョブトークン必須。ヘッダ `X-Source-Md5` / `X-File-Name`、ボディは画像バイト） | Claude | 5-5 |
| `POST /api/claude/jobs/{jobId}/complete` | 処理完了宣言（`done` / `done_with_errors` へ遷移） | Claude | 5-5 |
| `POST /api/claude/jobs/{jobId}/ack` | 完了確認の共有（`acknowledged = true`） | PC管理画面／スマホ | 5-5 |

- 既存 `GET /api/photos/{md5}` はアプリ／管理画面用として現状のまま維持し、Claudeはジョブ専用取得APIだけを使う（権限範囲をジョブに閉じるため）。

---

## 1. プロンプトテンプレートの共有基盤（Target: `v1.0.0 Build 8`）

### 実装タスクチェックリスト
- [x] **5-1-1: サーバー設定モデル・パーサー拡張 (`tools/SyncStore.cs`、`tools/FurimaServerNative.cs`)**
- `tools/SyncStore.cs`：`DefaultSettingsConfig` に `public List<TextTemplateConfig> promptTemplates { get; set; }` を追加し、`CreateDefault()` で空リスト初期化。旧 `store.json`（フィールド無し）は Newtonsoft のデフォルトで null になり得るため、`GetDefaultSettings()` **および `SaveDefaultSettings()`（280〜307行目付近）** の両方で null→空リストへ正規化する（既存 `titleTemplates` / `descriptionTemplates` の扱いに合わせる）。
- `tools/FurimaServerNative.cs`：**`ParseDefaultSettingsConfig()`（3058〜3124行目）へ `promptTemplates` の手書き抽出を追加する（レビュー指摘4-1・必須）**。`PUT /api/settings`（3134行目）と `POST /api/sync/items`（1981行目）の双方がここを通るため、漏らすと同期・保存のたびにサーバー側の `promptTemplates` が空に上書きされる。
- [x] **5-1-2: Android設定モデル・DTO・変換の拡張**
- `data/local/DefaultSettings.kt`：`promptTemplates: List<TextTemplate> = emptyList()` を追加。
- `data/server/SyncDtos.kt`：`SyncSettingsDto` に `@param:Json(name = "promptTemplates") val promptTemplates: List<SyncTextTemplateDto> = emptyList()` を追加。
- `data/server/PcServerClient.kt`：`toSyncSettingsDto` / `applyServerSettingsDto` に `promptTemplates` の相互変換を追加。
- `data/DefaultSettingsManager.kt`：**手書きキー保存が必須対応**（レビュー指摘4-2）。現行は `SharedPreferences` へ `title_templates_json` / `description_templates_json` を個別保存しているため、`prompt_templates_json` の読み書き（45〜67行目・103〜104行目のパターン）を追加する。漏らすとアプリ再起動で `promptTemplates` が消える。
- [x] **5-1-3: PC管理画面のテンプレート編集に `prompt` カテゴリ追加 (`tools/DashboardHtml.cs`)**
- `pcSettings` の初期値（976行目付近）、`fetchSettings()`（1184行目付近）、`savePcSettingsPayload()`（1266行目付近）の**3箇所すべて**の手書きマッピングへ `promptTemplates` を追加する（レビュー指摘4-4）。`renderTemplateSelects()`（1237行目付近）との連動も確認する。
- テンプレート編集モーダルのカテゴリ分岐（`title` / `desc`）へ `prompt` を追加し、設定画面に「プロンプトテンプレート」一覧セクション（`renderTemplateListHtml` 流用）を追加。文言は「画像加工プロンプト」であることが分かる表記にする。
- 初期データとして、既存の確定テンプレート（窓辺・海／窓辺・森／夕方の部屋）の3件をユーザーが設定画面から登録する運用とし、コードへの埋め込みはしない。
- [x] **5-1-4: スマホ設定UIへのプロンプトテンプレート編集追加**
- `ui/screens/SettingsStorageScreen.kt` の既存テンプレート編集（タイトル／説明）に、プロンプト用セクションを追加（同じ編集ダイアログを流用）。 **あわせて `persistSettingsNow()`（140〜174行目）が `DefaultSettings(...)` を全引数手書きで構築しているため、`promptTemplatesList` の状態と引数の追加が必須**（レビュー指摘4-3。漏らすと他の設定を保存した瞬間に `promptTemplates` が空で上書きされる）。
- [x] **5-1-5: バージョン統一更新 (`Build 8`) ＆ 検証**
- 3箇所を **`v1.0.0 (Build 8)`** へ統一（`WebDashboardHtml.kt` は本フェーズではバージョン表記のみ）。
- 検証：PC⇔スマホ双方からプロンプトテンプレートを登録・編集し、他方へ同期されること。旧バージョンの相手とも設定同期が壊れないこと（未知フィールド無視・欠落時空リスト）。
- 新規API：なし。既存機能の削除・置き換え：なし。

---

## 2. ジョブキュー基盤（Target: `v1.0.0 Build 9`）

### 実装タスクチェックリスト
- [x] **5-2-1: ジョブストア新設 (`tools/ClaudeJobStore.cs` 新規、`tools/build-exe.bat` 変更)**
- `PhotoTrashEntry` 管理のパターンに倣い、`ClaudeJob` クラスと静的ストア（`claude_jobs.json` の読込／保存、`lock` 保護、作成・取得・一覧・状態更新）を実装。保存先は `syncDir`。
- ジョブ履歴の自動整理（レビュー指摘10-1）：`acknowledged == true` の完了ジョブと、取消・拒否の終端ジョブは30日経過後に自動削除する（ゴミ箱の30日保持と同水準）。サーバー起動時とジョブ作成時に掃除する。
- `build-exe.bat` のコンパイル対象へ `ClaudeJobStore.cs` を追加。
- [x] **5-2-2: `claudeRevision` 新設と応答追加 (`tools/FurimaServerNative.cs`)**
- `dataRevision` の隣へ `claudeRevision` フィールド＋ `BumpClaudeRevision()` / `GetClaudeRevision()` を追加（155〜172行目のパターンを複製）。
- `/api/server/status` と `/api/server/heartbeat` の**両方**の応答に `claudeRevision` を追加。
- ジョブの作成・状態遷移・取消のたびに `BumpClaudeRevision()` を呼ぶ。商品データ自体は変えないため `dataRevision` は更新しない。
- [x] **5-2-3: ジョブ作成／一覧／詳細／取消API (`tools/FurimaServerNative.cs` の `HandleRequest` へ分岐追加)**
- `POST /api/claude/jobs`：ボディ `{itemId, itemUuid?, photos: [{md5, fileName}], templateId, origin}`。**商品特定は `itemUuid`（`syncUuid`）が指定されていればそれを最優先で引き当て、PC側の `item["id"]` を確定する（Q1・レビュー指摘1）。スマホ発では `itemUuid` を必須送信とする**（スマホとPCで整数IDが一致しないケースがあるため）。サーバー側で商品の存在と各MD5がその商品の写真に実在することを検証し、テンプレートの現在内容を `promptText` へスナップショットして保存。検証失敗は 400 で理由を返す。
- **まとめ商品（Bundle）の扱い（Q5・レビュー指摘6）**：フェーズ5ではまとめ親商品からの送信は受け付けない（400で理由を返す）。子商品を個別に開いて子商品単位で送る運用とする。親フォルダ／親 `photos` と子商品側の二重管理を避けるためであり、親対応は将来課題として記録する。
- `GET /api/claude/jobs`（`status` 絞り込み可）／ `GET /api/claude/jobs/{jobId}` / `POST /api/claude/jobs/{jobId}/cancel`。
- 応答に `jobToken` を含めない（発行は 5-4。詳細応答でも `approved` 状態かつ 5-4 実装後に限る）。
- [x] **5-2-4: PCギャラリーへの送信UI追加 (`tools/DashboardHtml.cs`)**
- ギャラリーツールバー（`viewer-trash-selected-btn` の隣）へ、テンプレート選択 `<select id='viewer-claude-template'>`（`pcSettings.promptTemplates` から生成。0件時は無効化＋設定への案内文）と「Museに送る」ボタン（`viewer-claude-send-btn`）を追加。選択数 0 の時はボタン無効（ゴミ箱ボタンと同じ `updateViewerSelectionBar`（2166行目）の更新に相乗り）。なおまとめ親商品（`checkIsBundle(item)` が true）を開いている間も `viewer-claude-send-btn` を無効化し、「まとめ商品は個別の子商品から送信してください」の案内を表示して400エラーを未然に防ぐ。
- `sendViewerPhotosToClaude()`：`selectedViewerPhotoIndices` から対象写真の `md5` / `fileName` を集め（写真オブジェクトの保持形式は既存ギャラリー実装に合わせる）、**送信直前に現在の商品写真とMD5で照合し（ライブ更新によるインデックスずれ対策・Q6）、対象のファイル名一覧を示す確認ダイアログを出してから**、`POST /api/claude/jobs`（`origin: "pc"`）を呼ぶ。照合で一致しない写真があれば送信を中止して知らせる。成功時は選択を解除し、ジョブ一覧モーダルを開くかトースト相当の表示で知らせる（既存UIの通知手段に合わせる）。
- チェックボックスの `title` 文言（2338行目付近 `選択して写真ゴミ箱へ移動`）を汎用文言（例：`写真を選択（ゴミ箱移動・Muse送信）`）へ変更する（レビュー指摘7-3）。ツールバー（`#viewer-action-bar`）はボタン追加で幅が逼迫するため、折り返し可能な2段構成にする（レビュー指摘7-4）。選択状態そのもののMD5キー化は将来課題として記録する。
- ジョブ一覧モーダル（`claude-jobs-modal`、写真ゴミ箱モーダルの型を流用）：状態バッジ、商品名、枚数、テンプレート名、作成日時、取消ボタンを表示。`pollServerRevisions()` で `claudeRevision` の変化を検知したら一覧を再取得する。
- [x] **5-2-5: バージョン統一更新 (`Build 9`) ＆ 検証**
- 検証：PCギャラリーからジョブを作成し、サーバー再起動後も `claude_jobs.json` から復元されること。存在しないMD5での作成が弾かれること。テンプレートを後から編集しても、作成済みジョブの `promptText` が変わらないこと。
- 既存機能の削除・置き換え：なし（ハートビート／ステータス応答へのフィールド追加のみ＝後方互換）。

---

## 3. スマホ側サムネイル複数選択（Target: `v1.0.0 Build 10`）

### 実装タスクチェックリスト
- [x] **5-3-1: 編集画面サムネイル選択モード (`app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`)**
- 写真グリッドに選択モードを追加：長押しで開始（商品一覧の選択モードと操作感を統一）、サムネイル右上にチェックボックスを重ね表示、選択中は上部にアクションバー（選択件数／「削除」／「Museに送る」／「解除」）を表示。
- 既存のプレビュー単品削除（`PhotoPreviewDialog` の `onDeletePhoto` と取り消しピル）は**変更しない**。
- [x] **5-3-2: 選択一括削除**（編集バッファ方式に統一・Q2）
- **即時DB削除（`deletePhotosByIds`）は使わない**（レビュー指摘2）。編集画面の既存方式に揃え、確認ダイアログの後に `existingFilePaths` / `newImageUris` から選択分を一括除外し、「保存する」で確定する（初版計画書の「即時DB削除へ寄せる」は撤回）。
- 一括削除の取り消しピル対応は行わず、実行前確認ダイアログのみとする（単品削除の取り消し挙動とは異なることを仕様として明記）。
- [x] **5-3-3: スマホ発「Museに送る」**
- `data/server/PcServerClient.kt` に `createClaudeJob(...)` を追加（保存済みサーバーURL＋Bearerトークンで `POST /api/claude/jobs`、`origin: "phone"`、**`itemUuid`（`syncUuid`）を必須で送る**）。
- **送信前ガード（Q2・Q5・レビュー指摘2）**：`itemId == 0`（新規作成中）または `hasUnsavedChanges == true` の場合は送信をブロックし、「保存してからMuseに送ってください」と案内する。またまとめ親商品（`mergedChildCount > 0` 等）を開いている場合も「まとめ商品は個別の子商品から送信してください」と案内して送信を無効化する。対象写真は `md5Hash` が空でなく `isPcSynced` のものだけとし、未同期・新規URI写真が混ざる場合は対象外として件数と理由を表示する。
- テンプレート選択ダイアログ（新規 `ui/components/ClaudeSendDialog.kt`）：`promptTemplates` をプルダウン表示し、選択して送信。PC未接続時は送信不可とし、既存の同期ステータス表示の流儀で理由を示す。
- `ui/FleaMarketViewModel.kt` に送信ラッパーと結果状態（成功／失敗の表示）を追加。
- [x] **5-3-4: バージョン統一更新 (`Build 10`) ＆ 検証**
- 検証：スマホから複数選択→一括削除が安全に行えること。スマホからジョブを作成し、PC管理画面のジョブ一覧に `origin: phone` として現れること。PC未接続時に送信が正しくブロックされること。
- `WebDashboardHtml.kt` はバージョン表記のみ（スマホ緊急Web管理画面への本機能の移植は本計画の対象外と明記）。

---

## 4. スマホ承認フロー（Target: `v1.0.0 Build 11`）★同期依存・最慎重

### 実装タスクチェックリスト
- [x] **5-4-1: 承認API (`tools/FurimaServerNative.cs`、`tools/ClaudeJobStore.cs`)**
- `POST /api/claude/jobs/{jobId}/request-approval`：`queued` のみ受付。`approval_pending` へ遷移し `approvalRequestedAt` を記録、`BumpClaudeRevision()`。
- `POST /api/claude/jobs/{jobId}/approval`：ボディ `{approved: bool}`。承認時は `jobToken`（GUID）を発行し `jobTokenExpiresAt = 現在 + 30分` をジョブ記録に保存、`approved` へ遷移。拒否時は `denied`（終端）。いずれも `BumpClaudeRevision()`。
- `GET /api/claude/jobs/{jobId}` の応答に、`status == approved`（または `processing`）の場合のみ `jobToken` と有効期限を含めるよう変更（5-2-3 の制限をここで解除）。
- `GET /api/claude/jobs/{jobId}/photos/{md5}`：ヘッダ `X-Claude-Job-Token` が当該ジョブの有効なトークンと一致し、状態が `approved` / `processing` の場合のみ原本バイトを返す。初回取得時に `processing` へ遷移。トークン期限切れの場合はジョブを `approval_expired` へ戻し `BumpClaudeRevision()`（再承認が必要）。
- トークン検証はジョブ単位に閉じ、他のジョブ・他のAPIには使えないことを実装で保証する。
- **CORS許可ヘッダ追加（レビュー指摘8-1・必須）**：`Access-Control-Allow-Headers`（1358行目付近）へ `X-Claude-Job-Token`, `X-Source-Md5`, `X-File-Name` を追加する。
- **`jobToken` の位置づけの正確な定義（レビュー指摘8-2）**：既存Bearerは他のAPIも通過するため、`jobToken` は権限分離ではなく「スマホ承認が完了しない限りジョブ実行APIが通らないセーフティロック（誤操作・無断実行防止ゲート）」として定義する。
- **タイムアウトと再承認（Q7・レビュー指摘8-4）**：`approval_pending` は10分で失効し、ジョブは `queued` へ戻る（終端にしない）。`jobToken` の期限切れ判定は、写真取得・結果返却・完了・詳細取得の**全ジョブトークン対象API**で行い、切れていれば `approval_expired` とした上で再び `request-approval` を受け付ける（ジョブの作り直しは不要）。失効判定は各APIアクセス時に遅延評価する。
- **配達用コピー仕様（画像形式・サイズ方針／確定事項9）**：`GET /api/claude/jobs/{jobId}/photos/{md5}` は原本をそのまま返さず、次の配達用コピーを生成して返す。原本ファイルは一切変更しない。変換・リサイズは `MemoryStream` 上で完結してHTTP応答へ直接返す（または `syncDir` 外の一時領域を使い、`FileSystemWatcher` の対象にしない）。
  - 形式がJPEG/PNGで長辺2048px以下・サイズ上限（目安5MB）以内、**かつEXIF Orientation（タグ `0x0112`）が正立（1）または未設定** の場合のみ、再エンコードせずそのまま返す（劣化ゼロ）。
  - 長辺が2048pxを超える／サイズ上限を超える／**EXIF Orientation で回転・反転指定が付いている**場合は、PC側で正立化＋必要に応じ長辺2048pxへ縮小し、Orientation タグを除外した上でJPEG品質90で再エンコードして返す（フル解像度写真と通常写真の差は、生成側の出力サイズでは実質出ない想定）。
  - **実装上の注意（再確認追記）**：`FurimaServerNative.cs` には現在 `System.Drawing` の `using` のみがあり、EXIF回転補正・リサイズの既存関数はサーバー側には無い（Android側 `PhotoStorageManager.kt` のみに存在）。そのため `System.Drawing.Image` の `PropertyItems`（`0x0112`）解析→ `RotateFlip` ＋高品質リサイズ（`InterpolationMode.HighQualityBicubic`）をサーバー側に新規実装する。
  - **HEIFは対象外**（運用方針として使用しない）。HEIF等の未対応形式が対象に含まれる場合は、その写真だけエラーとしてジョブに記録し、他の写真は継続する。
- [x] **5-4-2: ハートビートへの `claudeRevision` 相乗り（Android）**
- `data/server/SyncDtos.kt` の `HeartbeatInfo` と `data/server/PcServerClient.kt` の `HeartbeatInfo` 構築箇所へ `claudeRevision: Long = 0L` を追加（旧サーバー応答にフィールドが無くても 0 で安全）。
- `ui/FleaMarketViewModel.kt`：`lastKnownClaudeRevision` を追加し、ハートビートループ内に**独立分岐**を新設：不一致を検知したら `GET /api/claude/jobs` から `approval_pending` と「未確認の完了ジョブ」を取得して `StateFlow`（`pendingClaudeApproval` / `unackedClaudeCompletions`）を更新する。**既存のデータPull／設定Pull分岐と `isDirty` 保護には一切触れない**。この分岐の例外は握りつぶしてログのみ（次回ハートビートで再試行）とし、同期本体を止めない。
- サーバー側の `claudeRevision` 更新漏れが無いよう、ジョブ状態を変える全APIで `BumpClaudeRevision()` を呼ぶことを 5-4-1 と合わせて実装する。
- [x] **5-4-3: 承認ダイアログ（フリマ管理アプリ内）**
- 新規 `ui/components/ClaudeApprovalDialog.kt`：商品名、写真枚数、テンプレート名、返却先フォルダ名、リクエスト経過時間を表示し、「実行を承認」「拒否」の2ボタン。**表示場所は `ItemListScreen` ではなく `MainActivity.kt` の `FleaMarketApp()`（`NavHost` の外側の共通オーバーレイ）とする（Q3・レビュー指摘3）**。どのタブ・どの詳細／編集画面にいても承認できるようにするため。完了通知（5-5-4）も同じ配置とする。
- **フォアグラウンド必須の明記**：アプリがバックグラウンド／画面OFFの間はハートビートが止まり得るため、承認は「フリマ管理アプリを開いている間に届く。閉じていた場合は次に開いた直後のハートビートで表示される」ことを仕様とし、運用（6-1）にも転記する。
- 承認／拒否は `PcServerClient.postClaudeApproval(jobId, approved)` → `POST /api/claude/jobs/{jobId}/approval`。応答後は `StateFlow` をクリアし、次のハートビートで整合させる。
- **Muse アプリ側に承認UIは作らない**（確定事項2）。
- [x] **5-4-4: バージョン統一更新 (`Build 11`) ＆ 検証**
- 検証：外部（トンネル経由・Bearer付き）から承認リクエストを送ると、接続中のスマホにだけ承認ダイアログが出ること。スマホ未接続／無応答では画像取得が一切できないこと。拒否で終端すること。期限切れで再承認に戻ること。承認フロー実行中も通常の商品同期（データ／設定Pull）が従来通り動くこと。
- 既存機能の削除・置き換え：なし。

---

## 5. 結果返却・完了通知・確認共有（Target: `v1.0.0 Build 12`）

### 実装タスクチェックリスト
- [x] **5-5-1: 結果返却API (`tools/FurimaServerNative.cs`)**
- `POST /api/claude/jobs/{jobId}/results`：ジョブトークン必須。ヘッダ `X-Source-Md5`（元写真のMD5）と `X-File-Name`（希望ベース名）を受け、ボディの画像バイトを既存のマジックバイト検証に通す。保存名は **ベース名の優先順位「`job.photos[].fileName` → サニタイズ済み `X-File-Name` → フォルダ内実ファイル → フォールバック名」＋ `_ai` ＋ `DetectImageExtension(bytes)` の拡張子**とする（元ファイル名の拡張子は使わない＝Q4・レビュー指摘5-B/5-C）。同名があれば `_ai2`, `_ai3`…と連番。`X-File-Name` は `Path.GetFileName` ＋不正文字除去＋長さ制限でサニタイズし、自動取込の除外パスに該当しないことを確認する。受付サイズ上限（目安25MB）を設け、超過はエラーで返す（レビュー指摘10-3）。
- **冪等性（レビュー指摘10-2）**：同一ジョブ・同一 `sourceMd5` の結果が既に `job.results` にある場合、再登録せず既存レコードを成功として返す（通信リトライによる `_ai2` 増殖を防ぐ）。
- **`FileSystemWatcher` との競合回避（レビュー指摘5-A）**：書き込み前にロック外でマジックバイト検証とMD5計算を済ませ、ファイル書き込み直後にロック保護下で `photoStatCache`・`hashToFilePathMap`・`item["photos"]` を即時更新して、ウォッチャー側の再取込・再ハッシュ・二重の `BumpDataRevision()` を抑制する。
- 登録はフォルダ取込スキャン（フェーズ 4-4）と同一の登録処理を再利用し、`items_data.json` へ写真を追加、`syncedToPhone = false` 相当の扱い＋ `BumpDataRevision()` でスマホ・PC双方へ伝播させる。ジョブの `results` へ `{sourceMd5, resultFileName, resultMd5}` を追記。
- `POST /api/items/{id}/photos/add` は**使わない**（自動名のため。5-2 調査結果参照）。
- [x] **5-5-2: 完了・確認API**
- `POST /api/claude/jobs/{jobId}/complete`：ジョブトークン必須。全写真に結果またはエラーが記録されていることを確認し、`done`（全成功）／ `done_with_errors`（一部失敗）へ遷移、`completedAt` 記録、`BumpClaudeRevision()`。
- `POST /api/claude/jobs/{jobId}/ack`：Bearer認証のみ（PC管理画面／スマホのどちらからでも可）。`acknowledged = true`、`acknowledgedAt` 記録、`BumpClaudeRevision()`。これにより両端末の未確認表示が同時に消える（確定事項7）。
- [x] **5-5-3: PC管理画面の完了表示 (`tools/DashboardHtml.cs`)**
- ジョブ一覧モーダルで完了ジョブを未確認の間ハイライトし、「確認」ボタン（→ ack API）を表示。`claudeRevision` ポーリングで自動更新。返却された写真は既存の `dataRevision` 追従でギャラリーへ自動反映される（追加実装なし）。
- [x] **5-5-4: スマホの完了表示**
- 5-4-2 の `unackedClaudeCompletions` を使い、完了ダイアログ／バナー（同期ステータスダイアログの流儀に合わせる）で「商品名・成功/失敗枚数」を通知し、「確認」ボタンで ack を送る。写真そのものは既存データPullで自動的に届く（追加実装なし）。
- Muse チャット側でも処理完了を報告する（運用。コード変更なし）。
- **既知の反映遅延（レビュー指摘9・仕様メモ）**：対象商品に未同期のローカル写真が残っている間は、既存のマージガード（`hasUnpushedLocalPhoto`）によりスマホ側への `_ai` 写真取り込みが次の自動同期まで遅れることがある。異常ではないため、5-5-5 の検証では「全て同期済みの商品」を前提条件とする。
- [x] **5-5-5: バージョン統一更新 (`Build 12`) ＆ 検証**
- 検証：承認→取得→生成（手動で1枚）→返却→同一フォルダに `_ai` 名で登録され、PCギャラリーとスマホの両方に現れること。完了通知が両端末に出て、片方で確認すると両方から消えること。元写真が実行前に削除されていた場合は当該写真だけエラー記録となり、ジョブ全体は `done_with_errors` で完了できること。

---

## 6. Claude側の運用手順固定と試運転（コード変更なし）

- [] **6-1: 実行セッションの開始手順**：ユーザーがスマホから当該セッションのトンネルURLとBearerトークンを渡す → Claudeは `GET /api/claude/jobs?status=queued` で対象ジョブを確認 → `request-approval` を送り、スマホ承認を待つ（無応答・拒否・期限切れはその時点で中止し、ユーザーへ報告）。 **承認待ちの間、ユーザーはフリマ管理アプリをフォアグラウンドで開いておく**（バックグラウンドでは承認ダイアログが次回起動時まで届かないため）。
- [] **6-2: 処理手順**：承認後、ジョブの `promptText`（スナップショット）をそのまま使い、写真を1枚ずつ「原本から新規生成」する。結果はJPGへ変換し、`results` APIで1枚ずつ返却する。失敗した写真は理由を記録し、残りは続行する。最後に `complete` を送り、このチャットで完了（成功／失敗の内訳）を報告する。
- [] **6-3: 試運転**：1ジョブ・2〜3枚で全経路（作成→承認→取得→生成→返却→完了→ack共有）を通し、問題があれば該当フェーズへ差し戻して本計画書と `PROGRESS.md` に記録する。

---

## 7. 依存関係・リスクと対策（同期まわりを中心に）

1. **ハートビート応答の生成箇所が2つある**（`/api/server/status` と `/api/server/heartbeat`）。`claudeRevision` の追加漏れは「PCでは気付けるがスマホで気付けない」等の不整合になるため、5-2-2 で両方を必ず同時に直す。
2. **ViewModelのハートビート分岐は既存同期の中核**。5-4-2 の第3分岐は完全独立（商品データに触れない・例外を外へ漏らさない）とし、既存分岐のコードには手を入れない。
3. **リビジョンはサーバー再起動で初期化される**。比較は `!=` とし、再起動直後の1回は「変化あり」としてジョブ状態を取り直す（実害なし）。
4. **ジョブは `items_data.json` とは別ファイル**（`claude_jobs.json`）に置き、商品DBのスキーマには一切触れない。ジョブが壊れても商品同期へ影響しない。
5. **写真の同一性はMD5**。実行前に写真が差し替え・削除されるとMD5が一致しないため、その写真はエラーとして記録しジョブは継続する（5-5-5 検証項目）。
6. **Bearerトークンは既存の共有資格情報**であり、スマホとClaudeの双方が使う。ジョブ単位トークン（5-4）で「できる操作」を絞ることで、Bearerが外部に出る範囲を最小化する。トンネルURLが起動ごとに変わる点はコードではなく運用（6-1）で吸収する。
7. **`tools/build-exe.bat` の対象追加漏れ**（5-2-1）はコンパイルエラーとして即座に分かるが、フェーズ完了条件に「Windows側で `build-exe.bat` が通ること」を含める。
8. **バージョン表記の3箇所統一**は各フェーズの完了条件に含める（`WebDashboardHtml.kt` は 5-1〜5-5 を通じてバージョン表記のみの変更）。

## 8. 既存機能の削除・変更・置き換え（総括）

- **削除する既存機能：なし。置き換える既存機能：なし。**
- 後方互換のある変更のみ：設定JSONへの `promptTemplates` 追加／ハートビート・ステータス応答への `claudeRevision` 追加／PCギャラリーツールバーへのコントロール追加／スマホ編集画面への選択モード追加（既存のプレビュー単品削除は不変）／`tools/build-exe.bat` のコンパイル対象追加。
- 対象外と明記するもの：スマホ緊急Web管理画面（`WebDashboardHtml.kt` / `LocalWebServer.kt`）への本機能の移植（バージョン表記のみ更新）。


---

## 9. 事前コードレビュー指摘・協議サマリー（詳細は `REVIEW_CLAUDE_SEND_PHASE5.md` を参照）

2026-11-16 時点で現行コード（`1303035`）と照合した結果、着手前に確定・修正すべき主なポイントは以下の通り：
1. **商品特定キー（5-2-3 / 5-3-3）**：スマホとPCで整数 `id` が一致しないケースがあるため、`POST /api/claude/jobs` に `itemUuid`（`syncUuid`）を追加して優先解決する。
2. **編集画面の整合性（5-3）**：`ItemEditScreen` の `existingFilePaths` / `hasUnsavedChanges` と即時DB削除（`deletePhotosByIds`）の整合、および未保存変更あり時の「Museに送る」ガードを定義する。
3. **承認／完了UIの配置（5-4-3 / 5-5-4）**：`ItemListScreen` 限定ではなく `MainActivity.kt`（`FleaMarketApp()`）の全体オーバーレイに配置する。
4. **5-1 設定同期の実装漏れ対策**：`FurimaServerNative.cs` の `ParseDefaultSettingsConfig()`、`DefaultSettingsManager.kt`（手書きキー `prompt_templates_json`）、`SettingsStorageScreen.kt`（`persistSettingsNow()`）、`DashboardHtml.cs`（`fetchSettings` / `savePcSettingsPayload` / `pcSettings` 初期値）をすべて明記する。
5. **5-5 結果返却と既存機構の整合**：`FileSystemWatcher` との競合回避、保存拡張子の統一（`DetectImageExtension` 優先）、`X-File-Name` サニタイズ、まとめ商品（`Bundle`）写真の返却先ルール、および PC ギャラリー選択のライブ更新時ずれ防止を確定する。


---

## 10. 協議結果（2026-11-16・レビュー指摘への回答と確定事項）

`REVIEW_CLAUDE_SEND_PHASE5.md` の指摘11件と協議事項Q1〜Q7について、以下の通り確定し、本書の各節へ反映した。

- **Q1（商品特定キー）**：`itemUuid`（`syncUuid`）優先解決を採用。スマホ発は必須送信（5-2-3 / 5-3-3）。
- **Q2（編集画面の一括削除・送信ガード）**：一括削除は編集バッファ方式（保存時確定）に統一し、即時DB削除案は撤回。Muse送信は未保存時にブロック（5-3-2 / 5-3-3）。
- **Q3（ダイアログ配置）**：承認・完了とも `MainActivity.kt` の `FleaMarketApp()` 全体オーバーレイへ配置。フォアグラウンド必須を明記（5-4-3 / 6-1）。
- **Q4（返却の拡張子・Watcher排他）**：拡張子は `DetectImageExtension(bytes)`、ベース名は `job.photos[].fileName` 優先、書き込み直後のキャッシュ即時更新で競合回避（5-5-1）。
- **Q5（まとめ商品）**：フェーズ5では親からは送信不可とし、子商品単位で送る（5-2-3）。
- **Q6（PC選択のずれ）**：選択管理のMD5キー化は将来課題とし、当面は送信直前の照合＋ファイル名確認ダイアログで防ぐ（5-2-4）。
- **Q7（タイムアウト・再承認）**：`approval_pending` は10分で失効して `queued` へ戻り、トークン期限切れ後も同一ジョブで再リクエスト可能（5-4-1）。
- **指摘4（5-1の漏れ4箇所）**：全て変更対象へ明記（5-1-1〜5-1-4）。
- **指摘8（セキュリティ）**：CORS許可ヘッダ追加、一覧APIへの `jobToken` 非露出、`jobToken` の位置づけ（セーフティロック）の正確な定義を反映（0.6 / 5-4-1）。
- **指摘9（反映遅延）**：既知の挙動として仕様メモに明記（5-5-4）。
- **指摘10（肥大化・冪等性・サイズ）**：終端ジョブの30日自動整理、結果返却の冪等性、サイズ上限を反映（5-2-1 / 5-5-1）。
- **指摘11（用語）**：命名規約を冒頭に明記。
- **画像形式・サイズ方針（ユーザー追加確定）**：HEIFは使用しない。基本はアプリ内カメラの2048px運用とし、フル解像度写真はPC側の配達用コピーで2048pxへ統一する（確定事項9 / 5-4-1）。
- **最終チェック追記（2026-11-16・実装着手前補完）**：5-1-1 へ `FurimaServerNative.cs` の `ParseDefaultSettingsConfig()` / `SyncStore.SaveDefaultSettings()` を明記、5-2-4 の関数名を `updateViewerSelectionBar` へ修正しPC・スマホ双方のUIにまとめ親商品の送信無効化ガードを追加、5-4-1 のEXIF回転補正・リサイズはサーバー側に既存実装が無いため `System.Drawing` ＋ `MemoryStream` で新規実装し EXIF 回転タグ付き画像は2048px以下でも正立化対象とするよう明確化。

---

## 11. 実装完了後コードレビュー・ビルド検証結果 ＆ 担当エージェント協議事項（2026-11-16 / `e1a42b1`・`v1.0.0 Build 12` 時点）

フェーズ 5-1〜5-5 完了時点（`origin/main` `e1a42b1` / 4-1〜5-5 全17コミット対象）に対するコードレビュー、計画書突合、ビルド＆ユニットテスト実測結果を以下に記録する。**フェーズ 5-6（実機試運転）および Build 13 以降の修正を進める前に、フェーズ 5-1〜5-5 実装担当エージェントと本節の内容を協議・確定すること。**

### 11.1 ビルド・構文チェック・自動テスト実測結果（`e1a42b1`）

| 検証項目 | 結果 | 詳細・補足 |
|---|---|---|
| **Android ビルド (`compile_applet`)** | **合格** | エラーなし（`Build succeeded`） |
| **JS 構文チェック (`node --check`)** | **合格** | `tools/DashboardHtml.cs` および `WebDashboardHtml.kt` 双方の `<script>` 抽出検証が exit 0 |
| **JVM ユニット＆Robolectric テスト (`gradle :app:testDebugUnitTest`)** | **合格 (2m 7s)** | 全 6 テストクラス・**14 テスト合格（失敗 0 / エラー 0 / スキップ 0）**<br>・`CsvExporterTest` (1) / `ExampleRobolectricTest` (1) / `ExampleUnitTest` (1)<br>・`Phase214TrashAndSyncTest` (5：`deletePhotoById`・`deletePhotosByIds`・設定同期・論理削除/復元/衝突解決)<br>・`Phase215PhotoIntegrityTest` (3) / `PhotoStorageManagerTest` (3) |
| **3箇所バージョン表記統一** | **確認済み** | `app/build.gradle.kts` (`versionCode = 12`, `versionName = "1.0.0"`)、`tools/DashboardHtml.cs` (160行目)、`WebDashboardHtml.kt` (28行目) がすべて `v1.0.0 (Build 12)` で一致 |
| **自動テスト未カバー範囲（要留意）** | **注意** | フェーズ 4-3（同期進捗UI）・フェーズ 5-1〜5-5（プロンプト同期・複数選択・Muse 承認/完了フロー）および PC 側 C# エンドポイント専用の自動ユニットテストは未追加 |

### 11.2 計画整合性 ＆ 良好確認済み項目

- **計画書への準拠状況**: フェーズ 4（4-1〜4-4）およびフェーズ 5（5-1〜5-5）の全チェックリスト項目、`REVIEW_CLAUDE_SEND_PHASE5.md` の指摘11件・協議Q1〜Q7 が実装へ忠実に反映されている。
- **実コードで安全性が確認できた箇所**:
  - `TryBuildClaudeDeliveryCopy`（`tools/FurimaServerNative.cs` 5144行目）で透過PNG再エンコード前に `Graphics.Clear(Color.White)` が実施されており、JPEG変換時の黒背景化を防げている。
  - `HandlePostClaudeJobResult`（同 5394〜5409行目）でファイル保存直後に `CommitAutoImportedPhotosBatch` を呼び出して `photoStatCache` / `hashToFilePathMap` を即時更新しており、400ms後の `FileSystemWatcher` による二重取込を抑制できている。
  - `claudeRevision` の初期値が `1L` であり、`FleaMarketViewModel.kt` の `!=` 比較により初回接続・サーバー再起動直後でも第3分岐が正常発火する。
  - `DefaultSettingsManager.kt`（`prompt_templates_json`）と `SettingsStorageScreen.kt`（`persistSettingsNow`）の連携漏れはなく、他設定保存時のテンプレ消失は防止されている。

### 11.3 検出された問題点一覧（重要度順）

#### 【重要度 高】
1. **`DetectImageMimeType()` のフォールバックバグにより 415 検証と HEIF 拒否が無効化（`tools/FurimaServerNative.cs` 5576〜5600行目）**
   - **現状**: `IsValidImageHeader()`（706行目）は JPEG (`FF D8 FF`) を明示チェックしている一方、新設の `DetectImageMimeType()` は PNG / WebP / GIF / BMP に一致しないバイト列を**すべて `"image/jpeg"` として返している**。
   - **影響 1（セキュリティ／データ整合）**: `HandlePostClaudeJobResult()`（5320〜5327行目）の `if (string.IsNullOrEmpty(mime) || !mime.StartsWith("image/"))` による 415 拒否が**到達不能（デッドコード）**になっており、HTML・JSON・テキスト・破損データ・HEIC 等を返却APIへ送信してもそのまま `_ai.jpg` として保存・商品DB登録されてしまう。
   - **影響 2（配達用コピーのエラー分類）**: `TryBuildClaudeDeliveryCopy()`（5095〜5100行目）でも HEIF/HEIC 等の非対応形式が `"image/jpeg"` と誤判定されるため、「未対応の画像形式です」として 415 拒否＋エラー記録に落ちず、`Image.FromStream(ms)` のデコード例外経路へ入る。
   - **修正方針案**: `DetectImageMimeType()` で JPEG マジックバイト（`bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF`）を明示チェックし、どの画像形式にも一致しない場合は `""`（空文字）を返すように修正する（あわせて `DetectImageExtension()` 側も非画像時のフォールバックを整理する）。

2. **PC サーバー実行ファイル（`tools/furima-server.exe`）および配布 APK の未更新（成果物の乖離）**
   - **現状**:
     - `tools/furima-server.exe` は `d87cc85`（フェーズ 4-1 以前）からリビルドされておらず、**フェーズ 4-2（写真ゴミ箱）・4-4（フォルダ監視）・フェーズ 5-1〜5-5（Muse 連携一式）がバイナリに含まれていない**。
     - `flea_market_app.apk` および `public/flea_market_app.apk` も **Build 6（`7397c81`）のまま**で、Build 8〜12 のスマホ側機能（プロンプト同期・複数選択送信・承認/完了オーバーレイ）が未同梱。
   - **対応**: Phase 5 修正コミット後、Windows 環境での `tools/build-exe.bat` 実行と Build 13（または最新 Build）としての APK 再生成・配置が必要。

#### 【重要度 中】
3. **`ClaudeJobStore` の生参照返却 ＆ `POST /results` 冪等性チェックの競合リスク（`tools/ClaudeJobStore.cs` 170〜201行目、`tools/FurimaServerNative.cs` 5273〜5416行目）**
   - **現状 1（スレッド安全性）**: `ClaudeJobStore.GetJob()` / `GetJobs()` は `_jobLock` を抜ける際に、`_jobList` 内の生 `ClaudeJob` インスタンスおよびその `List<...>`（`photos` / `results` / `errors`）への参照をそのまま返している。別スレッドの `MutateJob()` が `results.Add(...)` 等でリストを書き換えている最中に `ToPublicClaudeJob()` や `Find()` が走ると `InvalidOperationException`（列挙中コレクション変更）を起こし得る。
   - **現状 2（冪等性のTOCTOU競合）**: `HandlePostClaudeJobResult()` で `job.results.Find(...)`（5275行目）を行ってからファイル書き出し（5394行目）・`MutateJob` で `j.results.Add(...)`（5414行目）を行う間に排他がないため、同一 `sourceMd5` のリトライ要求が並行到着すると `_ai.jpg` と `_ai2.jpg` の二重書き込みが起こり得る。
   - **修正方針案**: `GetJob()` / `GetJobs()` でディープコピー（または読み取りスナップショット）を返すか、`ToPublicClaudeJob` 生成・状態参照をロック保護する。また `HandlePostClaudeJobResult` では「保存予定の採番＋冪等確認（あるいは `sourceMd5` 単位の処理中ガード）」をロック内で直列化し、重複時は既存レコードを返してファイル書き出しをスキップする。

4. **スマホ承認・完了ダイアログの HTTP 失敗時サイレント失敗＆画面操作不能リスク（`FleaMarketViewModel.kt` 902〜935行目、`ClaudeCompletionDialog.kt` 29行目、`ClaudeApprovalDialog.kt` 40行目）**
   - **現状**:
     - `respondToClaudeApproval()` と `acknowledgeClaudeCompletion()` は `if (ok)` の場合のみダイアログを閉じ、通信失敗（PCオフライン・タイムアウト・409/400等）時に Toast 通知もボタン無効化／スピナー表示も一切行わない。
     - 特に `ClaudeCompletionDialog.kt` は `onDismissRequest = {}`（強制モーダル）であり「確認」以外のボタンが無いため、PCサーバーが一時停止した状態で「確認」をタップしても無反応のままアプリ全体を操作不能にする恐れがある。
   - **修正方針案**: ViewModel に処理中フラグ＋エラーToast（例：`snackbarHost` または `Toast`）を追加し、失敗時は理由を表示する。あわせて `ClaudeCompletionDialog` / `ClaudeApprovalDialog` には「処理中インジケータ」および通信不能時の回避動線（例：完了ダイアログへの「あとで確認（一時的に閉じる）」ボタン等、次ハートビートで再表示される導線）を検討する。

5. **スマホ緊急Webサーバー（`LocalWebServer.kt` / `WebDashboardHtml.kt`）のセキュリティ・UI同期差分（フェーズ4継続留意）**
   - **現状**:
     - `LocalWebServer.kt`（108行目等）は `0.0.0.0:8080`＋`CORS { anyHost() }`＋認証なしで `DELETE /api/items/{id}`、`DELETE /api/photos/{id}`、`POST /api/photos/delete-batch` 等を公開している（PCサーバー側のような `authToken` なし）。信頼できる家庭内Wi-Fi専用での利用が前提となっている。
     - `WebDashboardHtml.kt` はPC版と異なり `_photo_trash` を使わない即時物理削除であり、`header` の接続バッジ文言も「スマホ接続中」固定。
     - `WebDashboardHtml.kt` 473〜474行目の `fetchItemsSilent()` 差分ハッシュが `id / status / isShared / price / photos.length` のみを見ているため、商品名・説明・`isOriginalDeleted` の変更を自動リロードで検知できない。また `previewUploadPhotos()`（902〜916行目）が `FileReader.readAsDataURL` を使用しており、高画質複数枚選択時にブラウザ側メモリ消費が大きい（PC版は `URL.createObjectURL` 等）。

#### 【重要度 軽微】
6. **`ItemEditScreen.kt`（210〜235行目）の `loadedPhotosByPath` キャッシュ滞留**
   - 編集画面を開いたまま裏で自動同期が完了し写真が `isPcSynced = true` になっても、`LaunchedEffect(itemId)` 以降再取得されないため、画面を開き直すまで「Museに送る」対象として認識されない。
7. **`ClaudeApprovalDialog.kt`（30〜37行目）の経過時間表示**
   - PCサーバーの `approvalRequestedAt` とスマホ側の `System.currentTimeMillis()` の差を `remember` で1回だけ計算しているため、端末間の時計ズレで表示が前後し、表示中の分数も自動更新されない。

---

### 11.4 フェーズ 5-1〜5-5 担当エージェントとの協議事項（Build 13 修正着手前チェック）

1. **【協議1 / 最優先】`DetectImageMimeType()` の JPEG 厳格化（高-1）**
   - `FF D8 FF` チェック追加＋非画像時 `""` 返却への修正を Build 13 で即実施してよいか？（既存の呼び出し元は `TryBuildClaudeDeliveryCopy` / `HandlePostClaudeJobResult` / `DetectImageExtension` のみであり、既存挙動のデグレなし）
2. **【協議2】`ClaudeJobStore` スナップショット複製 ＆ `POST /results` 競合ガード（中-1）**
   - `GetJob` / `GetJobs` でディープコピーを返す方式にするか、それとも `ToPublicClaudeJob` を `ClaudeJobStore` ロック内へ寄せるか？また同一 `sourceMd5` の並行リトライ防止をどう入れるか？
3. **【協議3】承認・完了ダイアログの失敗時UX（中-2）**
   - `respondToClaudeApproval` / `acknowledgeClaudeCompletion` の処理中表示と失敗時エラー通知を追加し、`ClaudeCompletionDialog`（または双方）に PC 不達時の「一時的に閉じる（次回同期で再通知）」ボタンを設けるか？
4. **【協議4】`ItemEditScreen` の `isPcSynced` ライブ追従（軽微-1）と緊急Webサーバー（中-3）のスコープ切り分け**
   - 緊急Webサーバー（`LocalWebServer.kt` 認証／`WebDashboardHtml.kt` `fetchItemsSilent` ハッシュ拡張・`readAsDataURL` 改善）を今回の Build 13 に含めるか、別タスクへ分離するか？
5. **【協議5】5-6 試運転前の成果物ビルド（高-2）**
   - Build 13 修正コミット確定後に、① Android 側 `flea_market_app.apk` / `public/flea_market_app.apk` の再ビルド、② Windows 側 `tools/build-exe.bat` による `furima-server.exe` 再ビルド、の順で実機＋PC環境を揃えてフェーズ 5-6 の試運転へ進む方針でよいか？


---

## 12. §11 レビューへの実装担当エージェント（Muse）回答・追加指摘・Build 13 修正計画（2026-11-16）

§11 の全指摘を実装担当側で実コードと再照合した。項目別の判定、§11 に含まれていない追加指摘、および Build 13 の修正チェックリストを以下に確定する。**本節への担当エージェント（レビュー側）の同意が得られるまで、Build 13 のコード変更は開始しない。**

### 12.1 項目別の判定

1. **`DetectImageMimeType()` フォールバックバグ: 同意（ただし呼び出し元の数に訂正あり）**
- 実コードで数え直したところ、呼び出し元は **5 箇所**ある: `TryBuildClaudeDeliveryCopy`、`HandlePostClaudeJobResult`、`DetectImageExtension` に加え、既存の `ServePhotoTrashImage`（4152行目）と `ServePhotoByHash`（5738行目）が `ContentType` の決定に使っている。したがって §11.4 協議1の「既存の呼び出し元は3箇所のみでデグレなし」は正確ではなく、`""` 返却へ変える場合はこの2箇所で `""` のときに `application/octet-stream` へフォールバックするガードをセットで入れることで、初めてデグレなしが成立する。
- 修正内容そのもの（JPEG マジックバイト `FF D8 FF` の明示チェック＋不一致時は `""` 返却）には同意する。`DetectImageExtension()` の `.jpg` フォールバックは現行維持とする（`photos/add` 経路の既存挙動を変えないため。results 側は 415 検証を通過した画像しか拡張子決定へ進まない）。
- 影響2への補足: HEIF は現行でも `Image.FromStream` の例外で写真単位エラーには落ちる（文面が「未対応形式」ではなく「変換に失敗」になるだけ）。本丸は影響1（`POST /results` の 415 がデッドコード化している点）であり、優先度判断は §11 と同じ。

2. **バイナリ成果物の未更新: 同意（前提の追加あり）**
- §11.1 のビルド検証にはサーバー側 C# のコンパイル結果が含まれておらず、`furima-server.exe` が Phase 4-1 以前のままである事実と合わせると、**フェーズ 5 のサーバー側コード（5-2 / 5-4 / 5-5 で追加した約1,300行）は現時点で未コンパイル**である。`tools/build-exe.bat` の実行は成果物の更新であると同時に、このコードの初回コンパイル確認になる。試運転の直前ではなく Build 13 の直後に実施し、コンパイルエラーが出た場合の修正時間を確保すること（12.3 の協議5への回答に反映）。

3. **`ClaudeJobStore` スレッド安全 ＆ results 冪等性競合: 同意（方式を確定）**
- 試運転中は、Claude の結果アップロードとスマホ／PC のジョブ一覧取得が実際に並行し得るため、列挙中のコレクション変更は現実的な確率で起こる。対策は **ディープコピー返却**を採用する（`GetJob()` / `GetJobs()` / `MutateJob()` の返却値を複製にする）。呼び出し側は既に「変更後は再取得する」パターンで書かれており、影響は無い。`ToPublicClaudeJob` をロック内へ寄せる案は、呼び出し側全体へロックの知識を広げるため採らない。
- results の TOCTOU は、ハンドラ側で「冪等確認 → ファイル書き込み → 登録 → ジョブへの記録」を単一の `static` ロックで直列化する。ジョブ単位のロック分割は本規模では過剰であり、運用上のアップロードは逐次実行が前提のため全体ロックでも実害は無い。

4. **承認・完了ダイアログの失敗時UX: 同意（対象を承認ダイアログにも拡張）**
- 閉塞リスクは完了ダイアログだけではない。`ClaudeApprovalDialog` も `onDismissRequest` 無効＋失敗時無反応であり、PCサーバー停止時には承認も拒否も通らず、アプリを終了するまで抜けられない。修正設計は以下の通り:
- ViewModel にワンショット通知用の `StateFlow<String?>` を追加し、`MainActivity` 側で Toast 表示する（ViewModel から直接 Toast は出さない）。
- 応答送信後は成否によらず `refreshClaudeJobStates()` で状態を再整合させる。あわせて、承認の二度押し等で1回目が成功・2回目が 400 になった場合に「失敗」と誤表示しないよう、対象ジョブが承認待ちでなくなっていれば成功として扱いダイアログを閉じる。
- 両ダイアログへ処理中フラグ（ボタン無効化＋処理中表示）と「いったん閉じる」導線を追加する。閉じてもサーバー側の状態は変更しない（承認待ちは10分で `queued` へ自動復帰する。完了の未確認は PC 側モーダルからも ack でき、次回アプリ起動直後のハートビートでも再表示される）。

5. **ローカル管理画面（緊急Web）: 要修正として確定。ただし Build 13 には含めず別タスクとする（ユーザー判断・提案）**
- `LocalWebServer.kt` / `WebDashboardHtml.kt` の指摘はいずれも事実であり、修正が必要であることは確定事項とする（フェーズ5の実装範囲外という理由で「対応不要」にはしない）。実施は 5-6 試運転の後の独立タスクとし、優先順位は次の通り:
1. `LocalWebServer.kt` の認証なし・`0.0.0.0` 公開（削除系APIを含む）。最も重い。
2. `WebDashboardHtml.kt` の即時物理削除（PC版と同様にゴミ箱経由へ統一）。
3. `fetchItemsSilent()` の差分ハッシュ拡張（商品名・説明・`isOriginalDeleted` を含める）。
4. `previewUploadPhotos()` の `readAsDataURL` を `URL.createObjectURL` 等へ置き換え（メモリ負荷）。
- レビュー側で Build 13 へ含めたい項目がある場合は、本節への回答時に指定されたい。

6. **`ItemEditScreen` の `loadedPhotosByPath` 滞留: 同意。Build 13 に含める**
- 修正は小さく限定する: 同期成功時刻（`lastSyncSuccessAt`）の変化を `LaunchedEffect` で観測し、`loadedPhotosByPath` の再読み込みだけを行う。編集フィールドや `existingFilePaths` / `newImageUris` には触れない。

7. **承認ダイアログの経過時間表示: 現状維持（対応不要として合意したい）**
- 経過分数は `coerceAtLeast(0)` で負値にならず、あくまで目安表示である。表示中の自動更新も行わない。実害が無いため Build 13 の対象から外す。

### 12.2 §11 に無い追加指摘（実装担当側で検出）

1. **完了デッドロック: 写真取得の 404 でエラーが記録されず、`complete` が永久に失敗する**
- `HandleGetClaudeJobPhoto` は、配達用コピーの生成に失敗した場合のみ `RecordClaudeJobPhotoError` を呼んでおり、**写真ファイルが存在しない場合（404）はエラーを記録しない**。その写真は「結果もエラーも無い」状態になるため、`HandleCompleteClaudeJob` の未処理チェックに引っかかり続け、ジョブが `processing` のまま永久に完了できなくなる。
- これは §5-5-5 の検証項目「元写真が実行前に削除されていた場合は当該写真だけエラー記録となり、ジョブ全体は `done_with_errors` で完了できること」が**現行実装では成立しない**ことを意味する。
- さらに、Claude 側の都合（生成結果を品質基準で却下した場合、返却が恒常的に失敗した場合など）で写真を失敗扱いにする手段＝エラー報告API自体が存在しない。
- 対策（Build 13 に含める）:
- 写真取得のファイル不存在パスでも `RecordClaudeJobPhotoError` を呼ぶ。
- `POST /api/claude/jobs/{jobId}/errors`（ジョブトークン必須、ボディ `{sourceMd5, message}`、同一 `sourceMd5` への再送は既存レコードを成功として返す冪等仕様）を新設する。これにより全ての写真が「結果またはエラー」のいずれかへ必ず到達でき、`complete` の前提条件が常に満たし得ることを保証する。

2. **サーバー側 C# のコンパイル未検証（12.1-2 の再掲・独立項目化）**
- Android 側はコンパイル＋14テスト合格が実測されている一方、サーバー側は構文の目視確認のみでコンパイル実績が無い。Build 13 のサーバー修正も同条件になるため、「Build 13 コミット → `build-exe.bat` でコンパイル確認（エラー時は修正コミット）→ APK 再ビルド → 試運転」の順序を 12.3 に固定する。

### 12.3 Build 13 修正チェックリスト（§11.4 協議1〜5への回答を兼ねる）

- [] **13-1: `DetectImageMimeType` 厳格化**（`FF D8 FF` チェック追加、不一致時 `""` 返却）＋ `ServePhotoTrashImage` / `ServePhotoByHash` の `ContentType` フォールバック（`""` 時 `application/octet-stream`）。
- [] **13-2: `ClaudeJobStore` ディープコピー返却**（`GetJob` / `GetJobs` / `MutateJob`）＋ **results ハンドラの直列化**（`static` ロック内で冪等確認→書き込み→登録→記録）。
- [] **13-3: エラー記録の網羅**（写真取得 404 でのエラー記録 ＋ `POST /api/claude/jobs/{jobId}/errors` 新設・冪等）。
- [] **13-4: 承認・完了ダイアログの失敗時UX**（通知用 `StateFlow`＋Toast、処理中表示、応答後の状態再整合、二度押し時の誤表示防止、「いったん閉じる」導線を両ダイアログへ）。
- [] **13-5: `ItemEditScreen` の対象判定ライブ追従**（同期成功時に `loadedPhotosByPath` のみ再読込）。
- [] **13-6: バージョン統一更新 (`Build 13`) ＆ 検証**
- 検証: 非画像バイトの返却が 415 で拒否されること。HEIF が写真単位エラーとして記録されること。元写真を削除したジョブが `done_with_errors` で完了できること。同一 `sourceMd5` のリトライで `_ai2` が増殖しないこと。PCサーバー停止時に承認・完了ダイアログから「いったん閉じる」で抜けられること。
- 順序は「Build 13 コミット → Windows で `tools/build-exe.bat`（コンパイル確認を含む）→ Android APK 再ビルド・配置（`flea_market_app.apk` / `public/flea_market_app.apk`）→ フェーズ 5-6 試運転」とする。
- 既存機能の削除・置き換え: なし（`POST /api/claude/jobs/{jobId}/errors` の追加のみ）。

### 12.4 次のアクション

- 本節へのレビュー側の同意が得られ次第、Build 13 の実装を開始する。ローカル管理画面（緊急Web）の修正は 5-6 試運転後の独立タスクとして、12.1-5 の優先順位で実施する。

---

## 13. §12 回答へのレビュー側確認結果・実装時必須補足 ＆ 条件付き同意（2026-11-16）

実装担当エージェントによる §12（コミット `d6eb1b5`）の全項目・追加指摘・Build 13 修正計画（`13-1`〜`13-6`）を実コードと再照合した。**本節（13.1〜13.3）に記載した全ての確認事項・実装ガードを実装担当エージェントが確認し Build 13 に反映することを条件として、レビュー側として §12 の修正計画に同意する（Build 13 着手可）。**

### 13.1 実コード再照合で確認済みの事項
1. **`DetectImageMimeType()` の呼び出し元（5箇所）確認**: `tools/FurimaServerNative.cs` 上で `ServePhotoTrashImage`（4152行目）、`ServePhotoByHash`（5738行目）、`TryBuildClaudeDeliveryCopy`（5095行目）、`HandlePostClaudeJobResult`（5320行目）、`DetectImageExtension`（5604行目）の計 5 箇所であることを確認した。`FF D8 FF` 判定追加・不一致時 `""` 返却と、上記配信 2 箇所への `"application/octet-stream"` フォールバック追加、`DetectImageExtension` の `.jpg` 維持（`results` 側が事前 415 検証を通過するため）のいずれも妥当。
2. **完了デッドロック（404時エラー未記録）の再現確認**: `FurimaServerNative.cs` 5040〜5045行目でファイル不存在（404）時に `RecordClaudeJobPhotoError` が呼ばれておらず、`HandleCompleteClaudeJob`（5460〜5475行目）の未処理チェックにより永久完了不能となることを確認した。404 パスでのエラー記録および `POST /api/claude/jobs/{jobId}/errors` 新設を Build 13（`13-3`）へ追加する判断に全面同意する。
3. **`lastSyncSuccessAt` の存在確認**: `FleaMarketViewModel.kt`（845行目）に `val lastSyncSuccessAt: StateFlow<Long?>` が既に存在し、ハンドシェイク・通常差分Pull・自動同期・手動同期のすべてで更新されていることを確認した。これを観測して `loadedPhotosByPath` のみ再読み込みする `13-5` の限定修正は安全。
4. **C# 未コンパイル確認・ビルド順・緊急Web分離**: 「Build 13 コミット → `tools/build-exe.bat` 実行（C# 初回コンパイル検証＆ `.exe` 更新）→ Android APK 再ビルド（`flea_market_app.apk` / `public/flea_market_app.apk`）→ フェーズ 5-6 試運転」の順序固定、経過時間表示の現状維持、および緊急Web修正の 5-6 試運転後への分離（認証なし `0.0.0.0` 公開修正を最優先）に同意する。

### 13.2 Build 13 実装時に必ず守るべき補足・ガード条件

1. **【`13-2` 必須ガードA】`static` ロック内でのジョブ再取得（TOCTOU 完全防止）**:
   - `HandlePostClaudeJobResult`（および `HandlePostClaudeJobError`）で新設 `static` ロックを取得した直後は、**ロック外で先に取得していた `job` 変数を参照せず、必ずロック内で最新の `ClaudeJobStore.GetJob(jobId)` を再取得**してから `status`（`approved` / `processing`）と `results` / `errors` 既存チェックを行うこと。さもないと並行リトライ時に 2 スレッド目が古いスナップショットを判定して `_ai2.jpg` を二重書き出ししてしまう。
2. **【`13-2` 必須ガードB】ロック取得順序の厳守（デッドロック防止）**:
   - 既存の `HandleCreateClaudeJob`（4687行目）は `lock (lockObj)` の内側で `ClaudeJobStore`（`_jobLock`）を呼んでいる。したがって `13-2` で新設する `static` ロック（例：`claudeResultWriteLock`）は**必ず最外層（`claudeResultWriteLock` → `lockObj` → `_jobLock`）にのみ配置**し、`lockObj` や `_jobLock` を保持した内側から `claudeResultWriteLock` を取得する経路を一切作らないこと。
   - あわせて `ClaudeJobStore` のディープコピー返却では `ClaudeJob` 本体だけでなく `photos` / `results` / `errors` の各要素（`ClaudeJobPhoto` / `ClaudeJobResult` / `ClaudeJobError`）まで複製し、参照共有を完全に断つこと。
3. **【`13-3` 必須ガードC】`results` と `errors` の相互排他・冪等性統一**:
   - 新設する `POST /api/claude/jobs/{jobId}/errors` はジョブトークン検証・期限評価（`ApplyTokenExpiry`）・`job.photos` 内 `sourceMd5` 実在検証・`approved`/`processing` 状態チェック・同一 `sourceMd5` 再送時の冪等応答を行うこと。
   - **同一 `sourceMd5` が既に `results` へ登録済みの場合はエラー登録を拒否（400/409）し、逆に既に `errors` にある `sourceMd5` と `results` の整合ポリシーも統一**して、`results.size + errors.size` が写真枚数と矛盾しないよう（かつ `complete` の整合が崩れないよう）同じ直列化ロック下で保護すること。
   - 補足：`HandleGetClaudeJobPhoto` で 1 枚目から 404 または 415 になった場合は現行通り `approved` のまま残るが、`HandleCompleteClaudeJob`（5453行目）は `approved` / `processing` の双方を許可しているため完了自体は可能（実行開始済みとして途中取消を防ぎたい場合は 404/415 到達時または `POST /errors` 受信時にも `approved` → `processing` へ遷移させてよい）。
4. **【`13-4` 挙動補足】「いったん閉じる」と二度押し救済の動作仕様**:
   - `FleaMarketViewModel.kt`（1068行目）のハートビート第3分岐は `hb.claudeRevision != lastKnownClaudeRevision` のときのみ `refreshClaudeJobStates` を呼ぶため、ダイアログを「いったん閉じる」した後にサーバー側の `claudeRevision` が変わらなければ 4 秒おきに即再表示されて抜けられなくなるループは起きず、アプリ再起動後または次回のジョブ更新時に再表示される。
   - ただし、別ジョブの更新で `claudeRevision` が動いて `refreshClaudeJobStates` が再実行された場合や、通信失敗後にボタンを連打した場合に備え、ViewModel の処理中フラグで送信中の重複タップを遮断し、失敗時の `refreshClaudeJobStates()` によりサーバー上で既に承認/確認済みへ遷移済みだった場合はエラーToastを出さず正常終了として閉じること。

### 13.3 レビュー側同意ステータス（条件付き確定）
- **同意宣言**: 上記 **13.1〜13.2 の全内容を実装担当エージェントが確認・遵守すること**を条件として、§12.3（`13-1`〜`13-6`）の Build 13 修正計画に同意する。
- **承認後の進行**: 実装担当エージェントは本節の内容を確認した上で `13-1`〜`13-6` の実装・コミットへ進み、完了後に `tools/build-exe.bat` での初回コンパイル確認＆APK 再ビルドを経てフェーズ 5-6 試運転へ移行する。

