# 統合実装計画書（フェーズ 5）：「Museに送る」一括画像加工連携（プロンプトテンプレート共有 ➔ ジョブキュー ➔ スマホ承認 ➔ 結果の同一フォルダ返却）

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ ＋ PC母艦サーバー ＋ スマホ緊急Web管理画面）
現行バージョン：**`v1.0.0 (Build 7)`**（フェーズ 4-1 〜 4-4 全完了済み）
策定日：**2026-11-16**（計画策定のみ。本書のコミット時点ではコード未変更）

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
- [] **5-1-1: サーバー設定モデル拡張 (`tools/SyncStore.cs`)**
- `DefaultSettingsConfig` に `public List<TextTemplateConfig> promptTemplates { get; set;}` を追加し、`CreateDefault()` で空リスト初期化。旧 `store.json`（フィールド無し）は Newtonsoft のデフォルトで null になり得るため、`GetDefaultSettings()` の正規化箇所で null→空リストに寄せる（既存 `titleTemplates` の扱いに合わせる）。
- [] **5-1-2: Android設定モデル・DTO・変換の拡張**
- `data/local/DefaultSettings.kt`：`promptTemplates: List<TextTemplate> = emptyList()` を追加。
- `data/server/SyncDtos.kt`：`SyncSettingsDto` に `@param:Json(name = "promptTemplates") val promptTemplates: List<SyncTextTemplateDto> = emptyList()` を追加。
- `data/server/PcServerClient.kt`：`toSyncSettingsDto` / `applyServerSettingsDto` に `promptTemplates` の相互変換を追加。
- `data/DefaultSettingsManager.kt`：永続化がデータクラス経由であることを確認し、フィールド追加以外の変更が不要なことを検証（手書きキー変換がある場合は同所へ追加）。
- [] **5-1-3: PC管理画面のテンプレート編集に `prompt` カテゴリ追加 (`tools/DashboardHtml.cs`)**
- `pcSettings` の初期値・設定読込（`titleTemplates` 処理箇所）・設定保存ペイロードに `promptTemplates` を追加。
- テンプレート編集モーダルのカテゴリ分岐（`title` / `desc`）へ `prompt` を追加し、設定画面に「プロンプトテンプレート」一覧セクション（`renderTemplateListHtml` 流用）を追加。文言は「画像加工プロンプト」であることが分かる表記にする。
- 初期データとして、既存の確定テンプレート（窓辺・海／窓辺・森／夕方の部屋）の3件をユーザーが設定画面から登録する運用とし、コードへの埋め込みはしない。
- [] **5-1-4: スマホ設定UIへのプロンプトテンプレート編集追加**
- `ui/screens/SettingsStorageScreen.kt` の既存テンプレート編集（タイトル／説明）に、プロンプト用セクションを追加（同じ編集ダイアログを流用）。
- [] **5-1-5: バージョン統一更新 (`Build 8`) ＆ 検証**
- 3箇所を **`v1.0.0 (Build 8)`** へ統一（`WebDashboardHtml.kt` は本フェーズではバージョン表記のみ）。
- 検証：PC⇔スマホ双方からプロンプトテンプレートを登録・編集し、他方へ同期されること。旧バージョンの相手とも設定同期が壊れないこと（未知フィールド無視・欠落時空リスト）。
- 新規API：なし。既存機能の削除・置き換え：なし。

---

## 2. ジョブキュー基盤（Target: `v1.0.0 Build 9`）

### 実装タスクチェックリスト
- [] **5-2-1: ジョブストア新設 (`tools/ClaudeJobStore.cs` 新規、`tools/build-exe.bat` 変更)**
- `PhotoTrashEntry` 管理のパターンに倣い、`ClaudeJob` クラスと静的ストア（`claude_jobs.json` の読込／保存、`lock` 保護、作成・取得・一覧・状態更新）を実装。保存先は `syncDir`。
- `build-exe.bat` のコンパイル対象へ `ClaudeJobStore.cs` を追加。
- [] **5-2-2: `claudeRevision` 新設と応答追加 (`tools/FurimaServerNative.cs`)**
- `dataRevision` の隣へ `claudeRevision` フィールド＋ `BumpClaudeRevision()` / `GetClaudeRevision()` を追加（155〜172行目のパターンを複製）。
- `/api/server/status` と `/api/server/heartbeat` の**両方**の応答に `claudeRevision` を追加。
- ジョブの作成・状態遷移・取消のたびに `BumpClaudeRevision()` を呼ぶ。商品データ自体は変えないため `dataRevision` は更新しない。
- [] **5-2-3: ジョブ作成／一覧／詳細／取消API (`tools/FurimaServerNative.cs` の `HandleRequest` へ分岐追加)**
- `POST /api/claude/jobs`：ボディ `{itemId, photos: [{md5, fileName}], templateId, origin}`。サーバー側で商品の存在と各MD5がその商品の写真に実在することを検証し、テンプレートの現在内容を `promptText` へスナップショットして保存。検証失敗は 400 で理由を返す。
- `GET /api/claude/jobs`（`status` 絞り込み可）／ `GET /api/claude/jobs/{jobId}` / `POST /api/claude/jobs/{jobId}/cancel`。
- 応答に `jobToken` を含めない（発行は 5-4。詳細応答でも `approved` 状態かつ 5-4 実装後に限る）。
- [] **5-2-4: PCギャラリーへの送信UI追加 (`tools/DashboardHtml.cs`)**
- ギャラリーツールバー（`viewer-trash-selected-btn` の隣）へ、テンプレート選択 `<select id='viewer-claude-template'>`（`pcSettings.promptTemplates` から生成。0件時は無効化＋設定への案内文）と「Museに送る」ボタン（`viewer-claude-send-btn`）を追加。選択数 0 の時はボタン無効（ゴミ箱ボタンと同じ `updateViewerSelectionUi` 系の更新に相乗り）。
- `sendViewerPhotosToClaude()`：`selectedViewerPhotoIndices` から対象写真の `md5` / `fileName` を集め（写真オブジェクトの保持形式は既存ギャラリー実装に合わせる）、`POST /api/claude/jobs`（`origin: "pc"`）を呼ぶ。成功時は選択を解除し、ジョブ一覧モーダルを開くかトースト相当の表示で知らせる（既存UIの通知手段に合わせる）。
- ジョブ一覧モーダル（`claude-jobs-modal`、写真ゴミ箱モーダルの型を流用）：状態バッジ、商品名、枚数、テンプレート名、作成日時、取消ボタンを表示。`pollServerRevisions()` で `claudeRevision` の変化を検知したら一覧を再取得する。
- [] **5-2-5: バージョン統一更新 (`Build 9`) ＆ 検証**
- 検証：PCギャラリーからジョブを作成し、サーバー再起動後も `claude_jobs.json` から復元されること。存在しないMD5での作成が弾かれること。テンプレートを後から編集しても、作成済みジョブの `promptText` が変わらないこと。
- 既存機能の削除・置き換え：なし（ハートビート／ステータス応答へのフィールド追加のみ＝後方互換）。

---

## 3. スマホ側サムネイル複数選択（Target: `v1.0.0 Build 10`）

### 実装タスクチェックリスト
- [] **5-3-1: 編集画面サムネイル選択モード (`app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`)**
- 写真グリッドに選択モードを追加：長押しで開始（商品一覧の選択モードと操作感を統一）、サムネイル右上にチェックボックスを重ね表示、選択中は上部にアクションバー（選択件数／「削除」／「Museに送る」／「解除」）を表示。
- 既存のプレビュー単品削除（`PhotoPreviewDialog` の `onDeletePhoto` と取り消しピル）は**変更しない**。
- [] **5-3-2: 選択一括削除**
- 保存済み写真の一括削除は `FleaMarketRepository.deletePhotosByIds`（フェーズ 4-1-2 の安全な一括削除）へ寄せる。未保存（URI）写真は既存の編集画面内削除フローで処理する。
- 一括削除の取り消しピル対応は行わず、代わりに実行前確認ダイアログを出す（単品削除の取り消し挙動とは異なることを仕様として明記）。
- [] **5-3-3: スマホ発「Museに送る」**
- `data/server/PcServerClient.kt` に `createClaudeJob(...)` を追加（保存済みサーバーURL＋Bearerトークンで `POST /api/claude/jobs`、`origin: "phone"`）。対象はPC同期済みの保存済み写真のみ（MD5を持つもの）。未保存写真が混ざる場合は対象から外し、その旨を表示する。
- テンプレート選択ダイアログ（新規 `ui/components/ClaudeSendDialog.kt`）：`promptTemplates` をプルダウン表示し、選択して送信。PC未接続時は送信不可とし、既存の同期ステータス表示の流儀で理由を示す。
- `ui/FleaMarketViewModel.kt` に送信ラッパーと結果状態（成功／失敗の表示）を追加。
- [] **5-3-4: バージョン統一更新 (`Build 10`) ＆ 検証**
- 検証：スマホから複数選択→一括削除が安全に行えること。スマホからジョブを作成し、PC管理画面のジョブ一覧に `origin: phone` として現れること。PC未接続時に送信が正しくブロックされること。
- `WebDashboardHtml.kt` はバージョン表記のみ（スマホ緊急Web管理画面への本機能の移植は本計画の対象外と明記）。

---

## 4. スマホ承認フロー（Target: `v1.0.0 Build 11`）★同期依存・最慎重

### 実装タスクチェックリスト
- [] **5-4-1: 承認API (`tools/FurimaServerNative.cs`、`tools/ClaudeJobStore.cs`)**
- `POST /api/claude/jobs/{jobId}/request-approval`：`queued` のみ受付。`approval_pending` へ遷移し `approvalRequestedAt` を記録、`BumpClaudeRevision()`。
- `POST /api/claude/jobs/{jobId}/approval`：ボディ `{approved: bool}`。承認時は `jobToken`（GUID）を発行し `jobTokenExpiresAt = 現在 + 30分` をジョブ記録に保存、`approved` へ遷移。拒否時は `denied`（終端）。いずれも `BumpClaudeRevision()`。
- `GET /api/claude/jobs/{jobId}` の応答に、`status == approved`（または `processing`）の場合のみ `jobToken` と有効期限を含めるよう変更（5-2-3 の制限をここで解除）。
- `GET /api/claude/jobs/{jobId}/photos/{md5}`：ヘッダ `X-Claude-Job-Token` が当該ジョブの有効なトークンと一致し、状態が `approved` / `processing` の場合のみ原本バイトを返す。初回取得時に `processing` へ遷移。トークン期限切れの場合はジョブを `approval_expired` へ戻し `BumpClaudeRevision()`（再承認が必要）。
- トークン検証はジョブ単位に閉じ、他のジョブ・他のAPIには使えないことを実装で保証する。
- [] **5-4-2: ハートビートへの `claudeRevision` 相乗り（Android）**
- `data/server/SyncDtos.kt` の `HeartbeatInfo` と `data/server/PcServerClient.kt` の `HeartbeatInfo` 構築箇所へ `claudeRevision: Long = 0L` を追加（旧サーバー応答にフィールドが無くても 0 で安全）。
- `ui/FleaMarketViewModel.kt`：`lastKnownClaudeRevision` を追加し、ハートビートループ内に**独立分岐**を新設：不一致を検知したら `GET /api/claude/jobs` から `approval_pending` と「未確認の完了ジョブ」を取得して `StateFlow`（`pendingClaudeApproval` / `unackedClaudeCompletions`）を更新する。**既存のデータPull／設定Pull分岐と `isDirty` 保護には一切触れない**。この分岐の例外は握りつぶしてログのみ（次回ハートビートで再試行）とし、同期本体を止めない。
- サーバー側の `claudeRevision` 更新漏れが無いよう、ジョブ状態を変える全APIで `BumpClaudeRevision()` を呼ぶことを 5-4-1 と合わせて実装する。
- [] **5-4-3: 承認ダイアログ（フリマ管理アプリ内）**
- 新規 `ui/components/ClaudeApprovalDialog.kt`：商品名、写真枚数、テンプレート名、返却先フォルダ名、リクエスト経過時間を表示し、「実行を承認」「拒否」の2ボタン。`pendingClaudeApproval` が非空の時に一覧画面（`ItemListScreen`）側で表示する。
- 承認／拒否は `PcServerClient.postClaudeApproval(jobId, approved)` → `POST /api/claude/jobs/{jobId}/approval`。応答後は `StateFlow` をクリアし、次のハートビートで整合させる。
- **Muse アプリ側に承認UIは作らない**（確定事項2）。
- [] **5-4-4: バージョン統一更新 (`Build 11`) ＆ 検証**
- 検証：外部（トンネル経由・Bearer付き）から承認リクエストを送ると、接続中のスマホにだけ承認ダイアログが出ること。スマホ未接続／無応答では画像取得が一切できないこと。拒否で終端すること。期限切れで再承認に戻ること。承認フロー実行中も通常の商品同期（データ／設定Pull）が従来通り動くこと。
- 既存機能の削除・置き換え：なし。

---

## 5. 結果返却・完了通知・確認共有（Target: `v1.0.0 Build 12`）

### 実装タスクチェックリスト
- [] **5-5-1: 結果返却API (`tools/FurimaServerNative.cs`)**
- `POST /api/claude/jobs/{jobId}/results`：ジョブトークン必須。ヘッダ `X-Source-Md5`（元写真のMD5）と `X-File-Name`（希望ベース名）を受け、ボディの画像バイトを既存のマジックバイト検証に通し、商品フォルダへ `{元ファイル名のベース}_ai{拡張子}` で保存する（同名があれば `_ai2`, `_ai3`…と連番）。
- 登録はフォルダ取込スキャン（フェーズ 4-4）と同一の登録処理を再利用し、`items_data.json` へ写真を追加、`syncedToPhone = false` 相当の扱い＋ `BumpDataRevision()` でスマホ・PC双方へ伝播させる。ジョブの `results` へ `{sourceMd5, resultFileName, resultMd5}` を追記。
- `POST /api/items/{id}/photos/add` は**使わない**（自動名のため。5-2 調査結果参照）。
- [] **5-5-2: 完了・確認API**
- `POST /api/claude/jobs/{jobId}/complete`：ジョブトークン必須。全写真に結果またはエラーが記録されていることを確認し、`done`（全成功）／ `done_with_errors`（一部失敗）へ遷移、`completedAt` 記録、`BumpClaudeRevision()`。
- `POST /api/claude/jobs/{jobId}/ack`：Bearer認証のみ（PC管理画面／スマホのどちらからでも可）。`acknowledged = true`、`acknowledgedAt` 記録、`BumpClaudeRevision()`。これにより両端末の未確認表示が同時に消える（確定事項7）。
- [] **5-5-3: PC管理画面の完了表示 (`tools/DashboardHtml.cs`)**
- ジョブ一覧モーダルで完了ジョブを未確認の間ハイライトし、「確認」ボタン（→ ack API）を表示。`claudeRevision` ポーリングで自動更新。返却された写真は既存の `dataRevision` 追従でギャラリーへ自動反映される（追加実装なし）。
- [] **5-5-4: スマホの完了表示**
- 5-4-2 の `unackedClaudeCompletions` を使い、完了ダイアログ／バナー（同期ステータスダイアログの流儀に合わせる）で「商品名・成功/失敗枚数」を通知し、「確認」ボタンで ack を送る。写真そのものは既存データPullで自動的に届く（追加実装なし）。
- Muse チャット側でも処理完了を報告する（運用。コード変更なし）。
- [] **5-5-5: バージョン統一更新 (`Build 12`) ＆ 検証**
- 検証：承認→取得→生成（手動で1枚）→返却→同一フォルダに `_ai` 名で登録され、PCギャラリーとスマホの両方に現れること。完了通知が両端末に出て、片方で確認すると両方から消えること。元写真が実行前に削除されていた場合は当該写真だけエラー記録となり、ジョブ全体は `done_with_errors` で完了できること。

---

## 6. Claude側の運用手順固定と試運転（コード変更なし）

- [] **6-1: 実行セッションの開始手順**：ユーザーがスマホから当該セッションのトンネルURLとBearerトークンを渡す → Claudeは `GET /api/claude/jobs?status=queued` で対象ジョブを確認 → `request-approval` を送り、スマホ承認を待つ（無応答・拒否・期限切れはその時点で中止し、ユーザーへ報告）。
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
