# フェーズ 3.1 実装計画書：ハイブリッドカメラ実装（CameraX ＋ 標準カメラ併用）＆ 拡大プレビュー原本取得ボタン上部移設

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン：**`v0.9β (Build 7)`** ／ 目標バージョン：**`v0.9β (Build 8)`**  
目的：標準カメラの制限（外部呼出時のプロモード不可）を回避しつつ、アプリ内 CameraX 撮影と標準カメラのプロモード撮影を場面に応じて併用できる実用的な撮影環境を構築する。あわせて拡大プレビューの操作性を改善する。

---

## 1. 背景・端末特性の整理（POCO X6 Pro / Xiaomi HyperOS 配慮事項）

1. **従来の課題 (`ActivityResultContracts.TakePicture`)**
   - 外部カメラを `ACTION_IMAGE_CAPTURE` で呼び出すと、Xiaomi HyperOS の標準カメラは簡易撮影モードに制限され、プロモード（ISO・シャッター速度・ホワイトバランス・彩度/フィルター調整・マクロ切替等）が利用できなくなる。
   - また、省電力制御により外部カメラ使用中に呼び出し元アプリが裏で終了した場合、`remember` のみで保持していた一時撮影パスが失われる懸念がある。
2. **CameraX 単独への全面移行を避け「ハイブリッド方式」にする理由**
   - POCO X6 Pro（MediaTek Dimensity 8300-Ultra / HyperOS）は Camera2 / CameraX の基本動作は良好である一方、メーカー独自制限により **8MP超広角・2MPマクロ等の物理レンズ ID がサードパーティ製カメラ API へ公開されない** ケースや、メーカー独自の画像エンジン（発色・彩度・HDR・OIS 最適化）を CameraX 側で完全再現できない制約がある。
   - そのため、**「アプリ内 CameraX（手早い連続撮影・明るさEV補正等）」** と **「標準カメラ直接起動（プロモード・マクロ・彩度調整をフル活用）」** を状況に応じて使い分けられるハイブリッド構成を採用する。

---

## 2. 概要と実装目標

### 2.1 編集画面 (`ItemEditScreen.kt`) の縦幅を変えない二分割ボタン UI
- 現在の「📸 カメラで写真を撮影する (推奨)」単一ボタン（高さ `52.dp`・横幅いっぱい）を、**同じ高さ (`52.dp`) のまま横並び 2 分割** に配置変更し、画面の縦方向が伸びないようにする。
  - **左側ボタン（プライマリ・`weight(1f)`）：`アプリ内カメラ`**  
    アプリ内組み込みの CameraX 撮影画面 (`CameraXCaptureDialog.kt`) を起動。出品作業中にアプリを離れず連続撮影してそのまま商品へ追加可能。
  - **右側ボタン（サブ・`weight(1f)`）：`標準カメラ`**  
    POCO X6 Pro 標準カメラを **通常起動モード (`MediaStore.INTENT_ACTION_STILL_IMAGE_CAMERA`)** で開き、プロモード（ISO・シャッター・WB・彩度/フィルター）・マクロ・64MP 等のフル機能を利用可能にする（非対応環境のみ従来の `TakePicture` へフォールバック）。撮影後はアプリへ戻り、すぐ下の「端末のギャラリーから写真を選択」から複数枚を一括取り込みできる導線（トースト案内付き）を提供する。
- **状態保護**：カメラ起動中のプロセス再生成に備え、編集中の撮影補助状態や一時パス管理は `rememberSaveable` で保護する。

### 2.2 アプリ内 CameraX 撮影画面 (`CameraXCaptureDialog.kt`) の機能仕様
- **必須機能（全端末で安定動作する標準機能）**
  - `AndroidView` + `PreviewView` によるリアルタイムプレビュー（`statusBarsPadding` / `navigationBarsPadding` 対応）。
  - 画面タップフォーカス＆測光 (`startFocusAndMetering`) ＋ フォーカスリング表示。
  - **露出補正（明るさ EV）スライダー**（`CameraControl.setExposureCompensationIndex`：物撮りの白飛び・暗部対策に直結）。
  - ピンチズーム＆ズーム倍率スライダー／倍率表示（`1x` / `2x` 等）。
  - フラッシュ切替（オフ / ON / 自動 / 常時点灯トーチ）。
  - 利用可能レンズ切替（背面 / 前面、および端末が CameraX へ公開している補助レンズが存在する場合のみ動的に追加表示）。
  - 構図補助用の **3×3 グリッド線・1対1出品枠ガイド** の ON/OFF トグル。
  - **連続撮影対応**：シャッターを押しても即ダイアログを閉じず、撮影した枚数をプレビューバッジに表示し、「完了 (N枚追加)」ボタンでまとめて商品へ反映（1枚撮影後すぐ確定する操作もワンタップで可能）。
- **端末対応時の拡張機能（`Camera2Interop` 簡易マニュアル調整）**
  - 折りたたみ式の「詳細設定」バーを設け、`CameraCharacteristics` で端末対応が確認できた範囲に限り、簡易的な **ISO 感度・シャッター速度・ホワイトバランス (WB)** の調整またはリセット（Auto 戻し）を提供する。非対応項目や未公開設定は自動で非表示にし、安全にフォールバックする。

### 2.3 写真拡大プレビュー原本取得ボタンの上部移設 (`PhotoPreviewDialog.kt`)
- 「PCから高画質原本を取得」ボタンを画面最下部から **画面上部ヘッダーエリア** へ移設する（ページ番号・閉じるボタンと共存するコンパクトな配置とし、`statusBarsPadding()` を適用）。
- 下部に残るジェスチャーナビゲーションバーや3ボタンバーとの重なり・誤タップを完全に解消する。

---

## 3. 編集対象ファイルと変更仕様

1. **依存関係・マニフェスト・設定維持**
   - `app/build.gradle.kts`
     - CameraX ライブラリ (`androidx.camera:camera-core`, `camera-camera2`, `camera-lifecycle`, `camera-view` / 既存 `libs.versions.toml` 定義の `1.5.0` を利用) を有効化。
     - バージョンを **`v0.9β (Build 8)`** (`versionCode = 8`) へ更新。`targetSdk = 35` は維持。
   - `gradle.properties`
     - `android.injected.testOnly=false` を維持（実機直接インストール保証）。
   - `app/src/main/AndroidManifest.xml`
     - `<uses-permission android:name="android.permission.CAMERA" />` を追加（`WRITE_EXTERNAL_STORAGE` 等既存の宣言は維持）。
     - 補足：`CAMERA` 権限は `ItemEditScreen` 等から「アプリ内カメラ」タップ時に Compose の `ActivityResultContracts.RequestPermission()` で実行時リクエストし、拒否時は「標準カメラ」や「ギャラリー選択」が使える旨を案内する。
2. **新規ファイル**
   - `app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt`
     - CameraX プレビュー、連続撮影、EV 露出補正スライダー、ズーム、タップフォーカス、フラッシュ/トーチ、グリッドガイド、`Camera2Interop` による対応端末向け簡易マニュアル設定（非対応時自動無効化）を実装。
3. **既存ファイル更新**
   - `app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
     - 撮影ボタンを横並び 2 分割（「アプリ内カメラ」＋「標準カメラ」）へ変更（高さ `52.dp` 維持）。
     - `CAMERA` 権限リクエスト処理および `CameraXCaptureDialog` 連携、`STILL_IMAGE_CAMERA` 起動＆ギャラリー取込案内、一時パス状態の `rememberSaveable` 保護を実装。
   - `app/src/main/java/com/example/myapplication/ui/components/PhotoPreviewDialog.kt`
     - 「PCから高画質原本を取得」ボタンを上部ヘッダーエリアへ移設し、下部操作説明テキストのみシンプルに配置。
   - `app/src/main/java/com/example/myapplication/data/server/WebDashboardHtml.kt` / `tools/DashboardHtml.cs`
     - バージョン表記を **`v0.9β (Build 8)`** へ統一更新。
   - `PROGRESS.md`
     - 実装完了後にフェーズ 3.1 の完了記録を追記。

---

## 4. 検証計画

1. `compile_applet` でビルド成功を確認し、`gradle :app:testDebugUnitTest` で既存および新規追加テスト（権限拒否時フォールバック・撮影ファイル保存/EXIF連携等）が全件通過することを確認する。
2. POCO X6 Pro 実機にて以下を確認する：
   - 実機直接インストール（`testOnly=false` ＆ `targetSdk = 35`）が正常に完了すること。
   - 「アプリ内カメラ」で連続撮影・明るさ EV スライダー・タップフォーカス・フラッシュ・拡大プレビュー上部「原本取得」ボタンの押しやすさが期待通り機能すること。
   - 「標準カメラ」から HyperOS 標準カメラ（プロモード・マクロ・彩度調整）が開き、撮影後に「ギャラリーから写真を選択」で複数枚スムーズに取り込めること。
