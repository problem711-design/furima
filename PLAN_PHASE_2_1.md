# フェーズ 2.1 実装計画（エージェント引き継ぎ用）

対象: 個人利用のフリマ出品アプリ。Play ストアには出さない。スマホへ debug APK を直接インストールする。
現行バージョン: **v2.1 (Build 9)**。フェーズ 2.1-A2 は完了済み。この文書の最初の未完了フェーズは **2.1-A3**。

この文書が唯一の実装仕様である。1回の作業で **未完了の最若フェーズを1つだけ** 実装する。完了時にこの文書のチェックを付け、`PROGRESS.md` に既存の書式で1節追記し、バージョンの Build 番号を +1 する。

---

## 0. 固定した背景（推測で覆さない）

1. 高画質がスマホにも PC にも無い事故の主因は、サムネイル化のあと PC の同期フォルダが変わったこと。サーバーは `Directory.GetCurrentDirectory()` 配下に `出品写真_PC保存用` を作る。起動場所が変わると空の保存先になり、スマホは `PhotoEntity.isOriginalDeleted == true` の写真を再送しない。
2. 管理画面が2つあるのは意図どおり。
   - **母艦**: `tools/FurimaServerNative.cs` が配る PC ブラウザ画面。アプリとサーバーの接続中だけ操作する、という現行仕様の本体。
   - **緊急用**: スマホ内 `LocalWebServer` + `WebDashboardHtml.kt`。同一 Wi-Fi でスマホへ直接入る旧方式。残す。母艦の同期先にしない。見た目を作り変えない。
3. 接続方法は次の2つを併存させる。どちらも同じ母艦（同じ `storeId`）への同期である。
   - ローカル URL（既存の「PC連携」の直接接続）
   - Cloudflare Quick Tunnel + ワンタイムトークン + QR（新規）
4. 双方向編集（PC で商品を編集してスマホへ書き戻す）と、接続中の自動 PUSH は **この計画の対象外（2.2）**。2.1 の母艦画面は閲覧・写真・容量削除まで。

---

## 1. 作業ルール

- 各フェーズの「編集してよいファイル」以外は編集しない。
- 既存の出品一覧・詳細・編集・売上分析のレイアウトと配色は変えない。そのフェーズが明示した追加 UI だけ足す。
- PC 母艦は .NET Framework 4 の `csc.exe` 単一 exe のまま。ASP.NET にも Ktor にも移さない。`HttpListener` を維持する。
- JSON ライブラリ以外の新規 DLL は、各フェーズに書いたものだけ（`Newtonsoft.Json.dll`、`QRCoder.dll`）。
- 写真の同一性は MD5 のまま。アルゴリズムは替えない。
- 高画質をスマホから消してよい条件は、次をすべて満たすときだけ。
  1. 応答の `storeId` が、その起動中の母艦の `store.json` と一致する
  2. そのハッシュが、**サーバーが保存後に自分で計算した** `syncedHashes` に含まれる
  3. スマホ側にサムネイルファイルが存在する
- `isOriginalDeleted == true` の写真を、サムネイルのバイト列で穴埋めしない。原本は旧フォルダに残っている場合だけ戻せる。
- バージョン表記は `app/build.gradle.kts` の `versionCode` / `versionName` と、母艦画面に出している Build 番号を同時に上げる。`WebDashboardHtml.kt` に Build 番号の文字列があれば **数字だけ** 合わせ、レイアウトは変えない。
- アプリの確認ビルドは debug APK。リリース署名用の環境変数は前提にしない。JDK は `C:\Program Files\Android\Android Studio\jbr`、SDK は `C:\Users\user\AppData\Local\Android\Sdk`。リポジトリに `gradlew.bat` は無い。Wrapper が無ければ Android Studio 同梱の Gradle で `assembleDebug` する。`local.properties` が無ければ `sdk.dir` だけ書く。
- 母艦を変えたフェーズは `tools/build-exe.bat` で exe を再ビルドし、起動ログに保存パスと `storeId` が出ることを確認する。
- データを消すマイグレーションを足さない。`AppDatabase` の `fallbackToDestructiveMigration()` は **2.1-A2 で削除** する。以降のスキーマ変更は明示的な `Migration` だけ。

---

## 2. 実行順

| 順 | フェーズ | 目的 | 規模 |
|---|---|---|---|
| 1 | 2.1-A2 | 保存先の固定、原本を消す条件、原本なしの表示 | 小 |
| 2 | 2.1-A3 | JSON ライブラリ交換と、写真の別送信 | 小 |
| 3 | 2.1-B | 母艦 GUI、トンネル、QR、トークン | 中 |
| 4 | 2.1-C | スマホの QR 読み取りとローカル URL の併存 | 小 |
| 5 | 2.1-D | 未同期と原本なしの表示、一覧の書き出し | 小 |
| 6 | 2.1-R | ハッシュ指定で高画質を1枚戻す | 小 |
| 7 | 2.1-E | 母艦画面の密度と写真プレビュー | 中 |
| 8 | 2.1-F | 保存フォルダの容量と、売却30日写真の削除 | 小 |

---

## 3. フェーズ 2.1-A2（保存先と削除条件）

目的: 起動場所が変わっても別の空フォルダを正本にしない。一致していない保存先を根拠にスマホの高画質を消さない。

### 編集してよいファイル

- `tools/FurimaServerNative.cs`（待受、二重起動防止、ルーティングだけ残す）
- `tools/SyncStore.cs`（新規。保存先、`store.json`、索引、`items_data.json`、フォルダ掃除のガード）
- `tools/DashboardHtml.cs`（新規。今の HTML 文字列を移すだけ。見た目は変えない）
- `tools/FurimaSyncNative.cs`（保存先の決め方だけ母艦と揃える）
- `tools/furima-sync.ps1`（同上）
- `tools/build-exe.bat`（`FurimaServerNative.cs` と `SyncStore.cs` と `DashboardHtml.cs` を一緒にコンパイルする。参照は現状の `System.Web.Extensions.dll` のまま。このフェーズでは JSON ライブラリを変えない）
- `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
- `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`（同期結果の文言を出すために必要な範囲だけ）
- `app/src/main/java/com/example/myapplication/data/local/AppDatabase.kt`（`fallbackToDestructiveMigration()` の削除だけ）
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書のチェック

`LocalWebServer.kt` と `WebDashboardHtml.kt` は、後者に Build 番号文字列がある場合の数字以外、編集しない。

### 仕様

- 保存フォルダは次の順で1つに決める。カレントディレクトリでは決めない。
  1. exe と同じディレクトリの `store.json` に書かれた絶対パス（存在し、その中の `storeId` が一致する）
  2. それが無ければ、exe と同じディレクトリの `出品写真_PC保存用` を作り、`store.json` を新規作成する
- `store.json` の項目: `storeId`（GUID）、`path`、`createdAt`
- 名前付き Mutex で母艦 exe の二重起動を拒否し、メッセージを出して終了する
- `GET /api/server/status` に `storeId`、`syncDir`、`indexedPhotosCount`、`missingFileCount`（索引にあるがファイルが無いハッシュ数）を含める
- `POST /api/sync/push` の応答に `storeId` と、サーバーが保存済みファイルから計算した `syncedHashes` を含める。クライアントが送ったハッシュ文字列を、そのまま成功扱いしない
- スマホは応答 `storeId` を端末内に1つ記憶する（Room の新カラムは作らない。既存の `DefaultSettingsManager` と同じ SharedPreferences でよい。新規クラスは作らない）
- 高画質削除は第1節の3条件のみ。`existingServerHashes` に含まれるだけでは消さない
- サムネイルのみで、今回の `storeId` の索引にハッシュが無い写真の件数を、PC連携画面に「原本なし」として出す。同期成功件数に含めない
- `items_data.json` を上書きする前に、同フォルダへ `items_data.prev.json` を1世代残す
- フォルダの自動削除（現行の Garbage Collection）は、リクエストに含まれる `storeId` が `store.json` と一致するときだけ実行する。`storeId` が無い、または違う場合は削除しない
- 起動ログの1行目に、採用した絶対パスと `storeId` を出す

### 完了条件

- exe を別のカレントディレクトリから起動しても、同じ `store.json` のフォルダを開く
- 2つ目の exe はすぐ終了する
- サムネイル済みかつ PC にハッシュが無い状態で同期しても、成功ログだけ出して終わらない（原本なし件数が出る）
- debug APK がビルドできる

---

## 4. フェーズ 2.1-A3（JSON と写真の送り方）

目的: `JavaScriptSerializer` の配列キャストに依存しない。写真バイトを JSON の Base64 から外し、サーバーが書いたファイルの MD5 だけを正とする。

### 編集してよいファイル

- `tools/lib/Newtonsoft.Json.dll`（新規配置。NuGet の Newtonsoft.Json をこのパスに置く）
- `tools/FurimaServerNative.cs`
- `tools/SyncStore.cs`
- `tools/FurimaSyncNative.cs`（同じ JSON を読む箇所があれば Newtonsoft に替える）
- `tools/build-exe.bat`（`/r:tools\lib\Newtonsoft.Json.dll`。`System.Web.Extensions.dll` 参照は外す）
- `app/src/main/java/com/example/myapplication/data/server/SyncDtos.kt`（新規。Moshi の同期 DTO だけ）
- `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書

Moshi は既に依存関係にある。Retrofit は足さないし、同期には使わない。`org.json.JSONObject` を同期処理から外す。

### 仕様

プロトコル版は `3`。古い `POST /api/sync/push` は廃止してよい（利用端末はこの1台で、アプリと exe を揃えて入れる）。

1. `POST /api/sync/items`  
   商品メタデータと、各写真の `id` と `md5Hash` だけ。画像バイトは含めない。  
   応答: `protocolVersion`, `storeId`, `syncedHashes`（既にサーバ上でファイル MD5 が一致したもの）, `neededHashes`（サーバにファイルが無いもの）
2. `neededHashes` のうち、スマホに元ファイルが残っているものだけ `POST /api/sync/photo`  
   ボディは画像の生バイト。ヘッダ `X-Photo-Hash` にスマホ計算の MD5。  
   サーバーは保存後にファイルを MD5 し、ヘッダと一致したときだけ `stored: true` とハッシュを返す。一致しなければファイルを消して 400。
3. スマホは `stored: true` のハッシュだけ、第1節の条件で高画質を消す
4. `GET /api/sync/hashes` と `GET /api/photos/{hash}` は残す。ハッシュ一覧はディスク上のファイルから計算したものだけ

### 完了条件

- 元画像がある商品を同期すると、PC の商品フォルダに JPEG が書き出され、2回目は `neededHashes` が空で写真 POST が無い
- 保存ファイルの MD5 を故意に壊すテストは不要。コード上、申告ハッシュを `syncedHashes` にコピーしていないこと
- `System.Web.Script.Serialization` の使用が `tools/*.cs` から消えている

---

## 5. フェーズ 2.1-B（母艦 GUI・トンネル・QR・トークン）

目的: exe 起動時に軽い WinForms を出し、Quick Tunnel の URL とワンタイムトークンを QR で見せる。トンネル経由の API をトークン無しで受けない。

### 編集してよいファイル

- `tools/TunnelQrForm.cs`（新規）
- `tools/lib/QRCoder.dll`（新規配置）
- `tools/FurimaServerNative.cs`（フォーム起動とトークン検査だけ）
- `tools/build-exe.bat`（`/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:tools\lib\QRCoder.dll`）
- `tools/DashboardHtml.cs`（Build 番号の数字だけ）
- `app/build.gradle.kts`（Build +1。スマホの通信コードは次フェーズ）
- `PROGRESS.md` とこの文書

`cloudflared.exe` は `tools/` に配置済み。バージョンアップや差替えをしない。

### 仕様

- 起動時に WinForms を1枚出す。表示項目は保存パス、`storeId`、待受 URL、トンネル URL、トークン、索引枚数、原本欠落数。コンソールだけには戻さない
- トークンは起動ごとに乱数で1つ作る。設定ファイルに残さない
- `cloudflared.exe tunnel --url http://127.0.0.1:{port}` を子プロセスで起動し、標準出力から `trycloudflare.com` の URL を取る。失敗時もローカル待受は継続し、画面に失敗理由を出す
- QR の中身は1行のテキスト: `furima:{tunnelUrl}|{token}|{storeId}`  
  トンネルが無いときはローカル待受 URL を同じ形式で出す
- リクエスト元がループバック以外のとき、`/api/` は `Authorization: Bearer {token}` が一致しなければ 401。PC 自身のブラウザ（ループバック）はトークン無しで開いてよい
- トンネル URL を `storeId` の代わりに保存しない。URL は起動のたびに変わってよい

### 完了条件

- exe を起動するとウィンドウにパスと `storeId` と QR が出る
- トークン無しの LAN アクセスは 401、正しい Bearer は 200
- ループバックのブラウザは管理画面を開ける

---

## 6. フェーズ 2.1-C（スマホの QR とローカル URL）

目的: QR を読むとトンネル URL・トークン・`storeId` が入る。既存の URL 手入力も残す。

### 編集してよいファイル

- `gradle/libs.versions.toml`
- `app/build.gradle.kts`（Code Scanner 依存関係と Build +1）
- `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
- `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
- `PROGRESS.md` とこの文書

依存は Google Play services の Code Scanner（`com.google.android.gms:play-services-code-scanner`）。カメラ権限を自前では追加しない。読み取り結果のパースに失敗したら何も上書きしない。

### 仕様

- PC連携画面に「QRを読む」ボタンと、URL・トークンの手入力を残す。ローカル直接接続用のカードは削除しない
- QR テキスト `furima:{url}|{token}|{storeId}` を分解して保持する。保持先は ViewModel と、A2 で足した SharedPreferences
- 同期と heartbeat と status と写真 POST に `Authorization: Bearer` を付ける。トークンが空のローカル接続では付けない（ループバック用ではなく、ユーザーがトークン無し URL を手入力した場合はサーバー側の 401 をそのまま表示する）
- 応答 `storeId` が QR の `storeId` と違うときは同期を中止し、「別の保存先です」と出す。高画質は消さない
- `LocalWebServer` の起動スイッチは残す。母艦同期の URL と混ぜない

### 完了条件

- 手入力 URL でも従来どおりローカル母艦に同期できる
- QR 文字列をパースしたときだけトークン付きリクエストになる
- 画面の既存レイアウトを崩さない（ボタンと入力欄の追加だけ）

---

## 7. フェーズ 2.1-D（未同期と原本なし）

目的: サムネイル化済みを同期済みと表示しない。欠落を一覧で残す。

### 編集してよいファイル

- `app/src/main/java/com/example/myapplication/ui/screens/ItemListScreen.kt`（カード上のバッジだけ）
- `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
- `tools/DashboardHtml.cs`（ヘッダーに件数を出す最小の追記と Build 番号）
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書

### 仕様

- 件数は2種類だけ出す。  
  - 未同期: 元ファイルがスマホにあり、直近の成功同期の `syncedHashes` に無い  
  - 原本なし: `isOriginalDeleted` で、直近同期のサーバ索引にハッシュが無い
- 出品一覧のカードは、原本なしの商品だけバッジを足す。色は既存の売却済み赤と重ならないオレンジ1色。未同期の商品にも、既存カードの余白に短いラベルを1つ足す
- PC連携画面に原本なしの商品名とハッシュの一覧を出し、そのテキストをクリップボードへコピーできる
- 母艦画面ヘッダーに、status API の索引枚数と `missingFileCount` を表示する。レイアウト全面改修は次フェーズ

### 完了条件

- 原本なしの商品が、同期済みと同じ表示にならない
- コピーしたテキストに商品名と MD5 が含まれる

---

## 8. フェーズ 2.1-R（高画質を1枚戻す）【スキップ】

※ ユーザー協議によりスキップ決定（2026/03/30）。スマホ側で高画質原本を復元する実利用シーン（撮影後の再トリミング・詳細拡大等）が極めて限定的であるため、実装を見送り 2.1-E へ進行。

目的: 母艦にファイルが残っている写真だけ、スマホへ戻せる。30日削除より前に入れる。

### 編集してよいファイル

- `app/src/main/java/com/example/myapplication/data/server/PcServerClient.kt`
- `app/src/main/java/com/example/myapplication/data/PhotoStorageManager.kt`
- `app/src/main/java/com/example/myapplication/data/local/ItemDao.kt`（原本復活の UPDATE を1本追加）
- `app/src/main/java/com/example/myapplication/ui/screens/PcSyncScreen.kt`
- `app/src/main/java/com/example/myapplication/ui/FleaMarketViewModel.kt`
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書

### 仕様

- 対象は、直近同期でサーバがハッシュを持っている写真。原本なしの行に復元ボタンは出さない
- `GET /api/photos/{hash}` のバイト列を保存し、保存ファイルの MD5 が要求ハッシュと一致したときだけ `isOriginalDeleted = false` と `filePath` をそのファイルへ戻す
- 一致しなければ保存ファイルを消し、DB は変えない
- 認証ヘッダは 2.1-C と同じ

### 完了条件

- サーバに JPEG がある写真を1枚戻すと、スマホで元ファイルとして開き、もう一度同期しても再送されない
- 原本なしの写真はボタンが無く、失敗扱いにならない（操作自体が無い）

---

## 9. フェーズ 2.1-E（母艦画面の密度とプレビュー）

目的: 母艦ブラウザだけ、余白を詰め、価格・利益・状態を大きくする。写真は既存の `/api/photos/{hash}` で拡大する。

### 編集してよいファイル

- `tools/DashboardHtml.cs`
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書

`WebDashboardHtml.kt` は Build 番号の数字以外編集しない。商品編集 API は足さない。

### 仕様

- 接続中判定（heartbeat 10秒）は維持する。未接続時の操作ロック文言を残す
- 一覧の余白を詰め、価格・利益・状態の文字だけ大きく太くする
- 写真クリックで拡大する。画像 URL は `/api/photos/{md5}`。スマホ内サーバーの URL は使わない
- 2.1-D の件数表示を消さない

### 完了条件

- ループバックで母艦画面が開き、写真が拡大できる
- スマホアプリの画面レイアウトの diff が無い（`versionCode` 以外）

---

## 10. フェーズ 2.1-F（容量と売却30日の削除）

目的: 固定した保存フォルダのサイズを出し、売却から30日を超えた写真だけ、確認リストのあと削除できる。

### 編集してよいファイル

- `tools/SyncStore.cs`
- `tools/DashboardHtml.cs`
- `tools/FurimaServerNative.cs`（削除 API のルートを1本足す範囲だけ）
- `app/build.gradle.kts`（Build +1）
- `PROGRESS.md` とこの文書

スマホのストレージ削除 UI は復活させない。`SettingsStorageScreen.kt` は編集しない。

### 仕様

- 集計対象は `store.json` の `path` だけ
- 画面に総バイト数と、削除候補のファイル名・商品名・売却日・合計サイズを出してから実行する。即削除しない
- 候補は、母艦の商品メタデータで状態が売却済みかつ `soldAt` から30日を超えた写真ファイル。`soldAt` が無いものは候補にしない
- 索引にあってファイルが無いハッシュは、削減見込みに入れない
- 削除 API も、ループバック以外は 2.1-B の Bearer が必要
- 削除後に索引を作り直す。`items_data.json` 上書き前の1世代バックアップは A2 のまま維持する

### 完了条件

- 表示される容量が、カレントディレクトリではなく `store.json` のフォルダと一致する
- 候補一覧を出す前にファイルが消えない
- 出品中の写真は候補に出ない

---

## 11. この計画が終わったあとに残すもの（実装しない）

2.2 として別計画にする。

- 接続中に商品保存のたびに母艦へ自動 PUSH する
- 母艦画面から商品を編集し、スマホの Room へ書き戻す（正本がどちらになるかをその時点で決める）

---

## 12. 進捗チェック

- [x] 2.1-A 設定タブ整理と MD5 の DB 保存（Build 8、完了済み）
- [x] 2.1-A2 保存先の固定と原本削除条件（Build 9、完了済み）
- [x] 2.1-A3 Newtonsoft / Moshi と写真の別 POST（Build 10、完了済み）
- [x] 2.1-B 母艦 GUI、トンネル、QR、トークン（Build 11、コード実装完了）
- [x] 2.1-C スマホ QR とローカル URL（Build 12、実装完了）
- [x] 2.1-D 未同期と原本なし（Build 13、実装完了）
- [x] 2.1-R 高画質の復元（スキップ：利用機会が極めて限定的なため実装見送り）
- [ ] 2.1-E 母艦画面の密度とプレビュー
- [ ] 2.1-F 容量と売却30日削除
