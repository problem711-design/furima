# フェーズ 3.1 実装計画書：アプリ内CameraX統合 ＆ 拡大プレビュー原本取得ボタン上部移設

対象：個人利用のフリマ出品・売上管理システム（Androidアプリ + Windows母艦PCサーバー `furima-server.exe` + PCブラウザ管理画面）  
現行バージョン：**`v0.9β (Build 4)`**  
目的：標準カメラアプリ呼出（Intent）での各種設定限界の解消、および拡大プレビューの操作性向上。

---

## 1. 概要と修正目標

1. **アプリ内カメラ機能の CameraX 統合 (パターンB)**:
   - `ItemEditScreen.kt` の「📸 カメラで写真を撮影する」ボタン押下時、外部システムカメラではなくアプリ内組み込みの CameraX 撮影画面 (`CameraXCaptureDialog.kt`) を起動。
   - リアルタイムプレビュー、画面タップフォーカス＆自動露出調整、ピンチズーム/スライダー制御、フラッシュ切替（オフ/ON/自動/トーチ点灯）、レンズ切替（リア/フロント/広角）、高画質JPEG撮影をアプリ内で完結。
2. **写真拡大プレビュー原本取得ボタンの上部移設**:
   - `PhotoPreviewDialog.kt` の「PCから高画質原本を取得」ボタンを画面最下部から画面上部ヘッダーエリアへ移設。
   - スマホのシステムナビゲーションバー（ジェスチャーバー/3ボタン）との重なりを解消し、100%押しやすいレイアウトへ変更。

---

## 2. 編集対象ファイルと変更仕様

### 2.1 依存関係 ＆ パーミッション
- `app/build.gradle.kts`: CameraX ライブラリ (`camera-core`, `camera-camera2`, `camera-lifecycle`, `camera-view`) を `dependencies` に追加。
- `app/src/main/AndroidManifest.xml`: `<uses-permission android:name="android.permission.CAMERA" />` を追加。

### 2.2 カメラ機能
- 新規：`app/src/main/java/com/example/myapplication/ui/components/CameraXCaptureDialog.kt`
  - `AndroidView` + `PreviewView` によるカメラプレビュー。
  - ピンチズーム (`detectTransformGestures`)、タップフォーカス (`startFocusAndMetering`)、フラッシュ・レンズ切替コントロール。
- 更新：`app/src/main/java/com/example/myapplication/ui/screens/ItemEditScreen.kt`
  - カメラ撮影ボタン押下時に `CameraXCaptureDialog` を起動し、撮影写真を自動反映。

### 2.3 拡大プレビュー UI 改善
- 更新：`app/src/main/java/com/example/myapplication/ui/components/PhotoPreviewDialog.kt`
  - 「PCから高画質原本を取得」ボタンを画面上部ヘッダーへ配置。
