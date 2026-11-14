using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

public class TunnelQrForm : Form
{
    private Label lblStatus;
    private Label lblSyncDir;
    private Label lblStoreId;
    private TextBox txtLocalUrl;
    private TextBox txtTunnelUrl;
    private TextBox txtToken;
    private PictureBox picQrCode;
    private Label lblInfo;

    private NotifyIcon notifyIcon;
    private ContextMenuStrip trayMenu;
    private bool isExiting = false;
    private bool hasPromptedBrowser = false;
    private bool isPhoneConnected = false;
    private int heartbeatCount = 0;
    private System.Windows.Forms.Timer browserPromptTimer;
    private BrowserSelectDialog activeBrowserDialog = null;

    private string currentStoreId;
    private string currentSyncDir;
    private string currentLocalUrl;
    private string currentTunnelUrl = "";
    private string currentToken;

    public TunnelQrForm(string storeId, string syncDir, string localUrl, string token)
    {
        this.currentStoreId = storeId;
        this.currentSyncDir = syncDir;
        this.currentLocalUrl = localUrl;
        this.currentToken = !string.IsNullOrEmpty(token) ? token : SyncStore.AuthToken;

        InitializeComponent();
        InitializeTrayIcon();
        UpdateDisplay();
    }

    private void InitializeComponent()
    {
        this.Text = "フリマ母艦サーバー - 接続情報 & QRコード";
        this.Size = new Size(520, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Application;

        TableLayoutPanel mainLayout = new TableLayoutPanel();
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(15);
        mainLayout.RowCount = 9;
        mainLayout.ColumnCount = 2;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // タイトル / ステータス
        lblStatus = new Label();
        lblStatus.Text = "● サーバー稼働中 (接続待機中)";
        lblStatus.Font = new Font("Meiryo UI", 11F, FontStyle.Bold);
        lblStatus.ForeColor = Color.FromArgb(180, 83, 9);
        lblStatus.AutoSize = true;
        mainLayout.Controls.Add(lblStatus, 0, 0);
        mainLayout.SetColumnSpan(lblStatus, 2);

        // 保存先
        Label l1 = new Label() { Text = "保存フォルダ:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        lblSyncDir = new Label() { Text = currentSyncDir, Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F, FontStyle.Regular) };
        mainLayout.Controls.Add(l1, 0, 1);
        mainLayout.Controls.Add(lblSyncDir, 1, 1);

        // Store ID
        Label l2 = new Label() { Text = "Store ID:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        lblStoreId = new Label() { Text = currentStoreId, Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 8.5F, FontStyle.Bold) };
        mainLayout.Controls.Add(l2, 0, 2);
        mainLayout.Controls.Add(lblStoreId, 1, 2);

        // ローカルURL
        Label l3 = new Label() { Text = "ローカル URL:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        txtLocalUrl = new TextBox() { Text = currentLocalUrl, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        mainLayout.Controls.Add(l3, 0, 3);
        mainLayout.Controls.Add(txtLocalUrl, 1, 3);

        // トンネルURL
        Label l4 = new Label() { Text = "トンネル URL:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F, FontStyle.Bold) };
        txtTunnelUrl = new TextBox() { Text = "取得中...", ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
        mainLayout.Controls.Add(l4, 0, 4);
        mainLayout.Controls.Add(txtTunnelUrl, 1, 4);

        // 認証トークン
        Label l5 = new Label() { Text = "ワンタイムトークン:", Anchor = AnchorStyles.Left, AutoSize = true, Font = new Font("Meiryo UI", 9F) };
        txtToken = new TextBox() { Text = currentToken, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F, FontStyle.Bold) };
        mainLayout.Controls.Add(l5, 0, 5);
        mainLayout.Controls.Add(txtToken, 1, 5);

        // QRコード画像
        picQrCode = new PictureBox();
        picQrCode.Size = new Size(240, 240);
        picQrCode.SizeMode = PictureBoxSizeMode.Zoom;
        picQrCode.Anchor = AnchorStyles.None;
        picQrCode.BorderStyle = BorderStyle.FixedSingle;
        mainLayout.Controls.Add(picQrCode, 0, 6);
        mainLayout.SetColumnSpan(picQrCode, 2);

        // インフォメーション
        lblInfo = new Label();
        lblInfo.Text = "スマホのフリマアプリ「PC連携」画面から上記QRコードを読み取ってください。";
        lblInfo.TextAlign = ContentAlignment.MiddleCenter;
        lblInfo.Dock = DockStyle.Fill;
        lblInfo.Font = new Font("Meiryo UI", 8.5F);
        mainLayout.Controls.Add(lblInfo, 0, 7);
        mainLayout.SetColumnSpan(lblInfo, 2);

        // 手動ブラウザ起動・トレイ格納ボタンエリア
        FlowLayoutPanel btnPanel = new FlowLayoutPanel();
        btnPanel.Dock = DockStyle.Fill;
        btnPanel.FlowDirection = FlowDirection.LeftToRight;
        btnPanel.Padding = new Padding(0, 5, 0, 0);

        Button btnOpenBrowser = new Button();
        btnOpenBrowser.Text = "🌐 PC管理画面を開く";
        btnOpenBrowser.Font = new Font("Meiryo UI", 9F, FontStyle.Bold);
        btnOpenBrowser.Size = new Size(160, 32);
        btnOpenBrowser.Click += (s, e) => { ShowBrowserSelectionDialog(); };
        btnPanel.Controls.Add(btnOpenBrowser);

        Button btnMinimizeTray = new Button();
        btnMinimizeTray.Text = "📥 タスクトレイへ格納";
        btnMinimizeTray.Font = new Font("Meiryo UI", 9F);
        btnMinimizeTray.Size = new Size(160, 32);
        btnMinimizeTray.Click += (s, e) => { MinimizeToTray(); };
        btnPanel.Controls.Add(btnMinimizeTray);

        mainLayout.Controls.Add(btnPanel, 0, 8);
        mainLayout.SetColumnSpan(btnPanel, 2);

        this.Controls.Add(mainLayout);
        this.FormClosing += TunnelQrForm_FormClosing;
    }

    private void InitializeTrayIcon()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("ウィンドウを表示(&S)", null, (s, e) => { RestoreFromTray(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("PC管理画面を開く (Edge)", null, (s, e) => { LaunchBrowser("edge"); });
        trayMenu.Items.Add("PC管理画面を開く (Chrome)", null, (s, e) => { LaunchBrowser("chrome"); });
        trayMenu.Items.Add("PC管理画面を開く (デフォルト)", null, (s, e) => { LaunchBrowser("default"); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("サーバーログを開く(&L)", null, (s, e) => { OpenServerLog(); });
        trayMenu.Items.Add("母艦サーバーを終了(&X)", null, (s, e) => { ExitApp(); });

        notifyIcon = new NotifyIcon();
        notifyIcon.Text = "フリマ母艦サーバー (稼働中)";
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.ContextMenuStrip = trayMenu;
        notifyIcon.Visible = true;
        notifyIcon.DoubleClick += (s, e) => { RestoreFromTray(); };
    }

    private void SafeBeginInvoke(Action action)
    {
        if (isExiting || this.IsDisposed || this.Disposing || !this.IsHandleCreated) return;
        try
        {
            this.BeginInvoke(action);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void CleanupTrayAndImages()
    {
        if (notifyIcon != null)
        {
            try { notifyIcon.Visible = false; } catch { }
            try { notifyIcon.Dispose(); } catch { }
            notifyIcon = null;
        }
        if (trayMenu != null)
        {
            try { trayMenu.Dispose(); } catch { }
            trayMenu = null;
        }
        if (picQrCode != null && picQrCode.Image != null)
        {
            try { picQrCode.Image.Dispose(); } catch { }
            picQrCode.Image = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (browserPromptTimer != null)
            {
                try { browserPromptTimer.Stop(); } catch { }
                try { browserPromptTimer.Dispose(); } catch { }
                browserPromptTimer = null;
            }
            CleanupTrayAndImages();
        }
        base.Dispose(disposing);
    }

    private void TunnelQrForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    public void MinimizeToTray()
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(MinimizeToTray));
            return;
        }

        this.Hide();
        if (notifyIcon != null)
        {
            try
            {
                notifyIcon.ShowBalloonTip(2000, "フリマ母艦サーバー", "バックグラウンドで動作中です。トレイアイコンのダブルクリックで再表示できます。", ToolTipIcon.Info);
            }
            catch { }
        }
    }

    public void RestoreFromTray()
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(RestoreFromTray));
            return;
        }

        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    public void OpenServerLog()
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server.log");
            if (!File.Exists(logPath))
            {
                MessageBox.Show("ログファイル (server.log) はまだ作成されていません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = logPath;
            psi.UseShellExecute = true;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ログファイルを開けませんでした：" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void ExitApp()
    {
        if (this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(ExitApp));
            return;
        }

        isExiting = true;
        if (browserPromptTimer != null)
        {
            try { browserPromptTimer.Stop(); } catch { }
            try { browserPromptTimer.Dispose(); } catch { }
            browserPromptTimer = null;
        }
        if (activeBrowserDialog != null && !activeBrowserDialog.IsDisposed)
        {
            try
            {
                activeBrowserDialog.DialogResult = DialogResult.Cancel;
                activeBrowserDialog.Close();
            }
            catch { }
        }
        CleanupTrayAndImages();
        try { this.Close(); } catch { }
        try { Application.Exit(); } catch { }
    }

    /// <summary>
    /// スマホアプリからのハートビート受信時に呼び出される処理
    /// 単発pingでは即ダイアログを出さず、緑ステータス確定後に遅延表示する
    /// </summary>
    public void OnHeartbeatReceived()
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(OnHeartbeatReceived));
            return;
        }

        isPhoneConnected = true;
        heartbeatCount++;
        lblStatus.Text = "● スマホアプリ接続中";
        lblStatus.ForeColor = Color.DarkGreen;

        if (!hasPromptedBrowser && heartbeatCount >= 2)
        {
            ScheduleBrowserPrompt(1000);
        }
    }

    /// <summary>
    /// スマホアプリからの有効な同期・接続確認時に呼び出される処理
    /// 緑ステータス反映を待ってからブラウザ選択ダイアログを開く
    /// </summary>
    public void OnAppConnected()
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(OnAppConnected));
            return;
        }

        isPhoneConnected = true;
        lblStatus.Text = "● スマホアプリ接続中";
        lblStatus.ForeColor = Color.DarkGreen;

        if (!hasPromptedBrowser)
        {
            ScheduleBrowserPrompt(800);
        }
    }

    private void ScheduleBrowserPrompt(int delayMs)
    {
        if (hasPromptedBrowser || browserPromptTimer != null) return;

        browserPromptTimer = new System.Windows.Forms.Timer();
        browserPromptTimer.Interval = delayMs;
        browserPromptTimer.Tick += (s, e) =>
        {
            if (browserPromptTimer != null)
            {
                browserPromptTimer.Stop();
                browserPromptTimer.Dispose();
                browserPromptTimer = null;
            }
            if (isExiting || this.IsDisposed || this.Disposing || hasPromptedBrowser || !isPhoneConnected) return;
            hasPromptedBrowser = true;
            RestoreFromTray();
            ShowBrowserSelectionDialog();
        };
        browserPromptTimer.Start();
    }

    public void ShowBrowserSelectionDialog()
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        using (BrowserSelectDialog dlg = new BrowserSelectDialog())
        {
            activeBrowserDialog = dlg;
            DialogResult dr = DialogResult.Cancel;
            try
            {
                dr = dlg.ShowDialog(this);
            }
            finally
            {
                if (activeBrowserDialog == dlg) activeBrowserDialog = null;
            }
            if (!isExiting && dr == DialogResult.OK)
            {
                LaunchBrowser(dlg.SelectedBrowser);
                MinimizeToTray();
            }
        }
    }

    public void LaunchBrowser(string browserType)
    {
        string url = currentLocalUrl;
        try
        {
            if (browserType == "edge")
            {
                try
                {
                    ProcessStartInfo psiEdge = new ProcessStartInfo("msedge.exe", url);
                    psiEdge.UseShellExecute = true;
                    psiEdge.WorkingDirectory = Path.GetTempPath();
                    Process.Start(psiEdge);
                    return;
                }
                catch { }
            }
            else if (browserType == "chrome")
            {
                try
                {
                    ProcessStartInfo psiChrome = new ProcessStartInfo("chrome.exe", url);
                    psiChrome.UseShellExecute = true;
                    psiChrome.WorkingDirectory = Path.GetTempPath();
                    Process.Start(psiChrome);
                    return;
                }
                catch { }
            }
            ProcessStartInfo psiDefault = new ProcessStartInfo(url);
            psiDefault.UseShellExecute = true;
            psiDefault.WorkingDirectory = Path.GetTempPath();
            Process.Start(psiDefault);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ブラウザ起動に失敗しました：" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void UpdateTunnelUrl(string tunnelUrl)
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(() => UpdateTunnelUrl(tunnelUrl)));
            return;
        }

        this.currentTunnelUrl = tunnelUrl;
        if (!string.IsNullOrEmpty(tunnelUrl) && tunnelUrl.StartsWith("https://") && tunnelUrl.Contains("trycloudflare.com"))
        {
            txtTunnelUrl.Text = tunnelUrl;
            if (!lblStatus.Text.Contains("接続中"))
            {
                lblStatus.Text = "● トンネル接続完了 (QRコード準備完了)";
                lblStatus.ForeColor = Color.Blue;
            }
        }
        else if (string.IsNullOrEmpty(tunnelUrl))
        {
            txtTunnelUrl.Text = "(トンネル接続失敗 - ローカル接続のみ)";
            if (!lblStatus.Text.Contains("接続中"))
            {
                lblStatus.Text = "● ローカル接続のみ可";
                lblStatus.ForeColor = Color.DarkOrange;
            }
        }

        UpdateDisplay();
    }

    public void UpdateStatusInfo(int indexedCount, int missingCount)
    {
        if (isExiting || this.IsDisposed || this.Disposing) return;
        if (this.InvokeRequired)
        {
            SafeBeginInvoke(new Action(() => UpdateStatusInfo(indexedCount, missingCount)));
            return;
        }

        lblInfo.Text = string.Format("スマホアプリ「PC連携」でQRを読込んでください。\n索引写真: {0}枚 | 原本欠落: {1}枚", indexedCount, missingCount);
    }

    private void UpdateDisplay()
    {
        if (string.IsNullOrEmpty(currentTunnelUrl) || !currentTunnelUrl.StartsWith("https://"))
        {
            if (picQrCode.Image != null) { picQrCode.Image.Dispose(); picQrCode.Image = null; }

            // 待機中または失敗時のビジュアルプレースホルダー描画（白紙や不正QRの誤表示を防止）
            Bitmap bmp = new Bitmap(240, 240);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(248, 250, 252));
                using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 2))
                {
                    g.DrawRectangle(p, 1, 1, 238, 238);
                }
                string msg = string.IsNullOrEmpty(currentTunnelUrl)
                    ? "トンネルURL発行中...\n\n準備完了まで\nお待ちください"
                    : "トンネル接続不可\n\nローカル接続を\nご利用ください";
                using (Font font = new Font("Meiryo UI", 9.5F, FontStyle.Regular))
                using (Brush brush = new SolidBrush(Color.FromArgb(100, 116, 139)))
                {
                    StringFormat sf = new StringFormat() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(msg, font, brush, new RectangleF(10, 10, 220, 220), sf);
                }
            }
            picQrCode.Image = bmp;
            lblInfo.Text = string.IsNullOrEmpty(currentTunnelUrl)
                ? "Cloudflare トンネルURLを発行中...\n準備ができるとQRコードが自動表示されます。"
                : "トンネル接続が利用できません。スマホ側でローカルURLを入力してください。";
            return;
        }

        string qrText = string.Format("furima:{0}|{1}|{2}", currentTunnelUrl, currentToken, currentStoreId);

        try
        {
            Bitmap qrBmp = SimpleQrEncoder.GenerateQrBitmap(qrText, 240, 240);
            if (picQrCode.Image != null) picQrCode.Image.Dispose();
            picQrCode.Image = qrBmp;
            lblInfo.Text = "スマホアプリ「PC連携」で上記QRコードを読み取ってください。";
        }
        catch (Exception ex)
        {
            Console.WriteLine("QRコード生成例外: " + ex.Message);
        }
    }
}

/// <summary>
/// スマホ接続検知時に起動ブラウザを選択させるダイアログフォーム
/// </summary>
public class BrowserSelectDialog : Form
{
    public string SelectedBrowser { get; private set; }

    public BrowserSelectDialog()
    {
        this.SelectedBrowser = "default";
        this.Text = "スマホ接続検知 - 管理画面ブラウザ選択";
        this.Size = new Size(420, 300);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

        TableLayoutPanel layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(20);
        layout.RowCount = 5;
        layout.ColumnCount = 1;

        Label lblMsg = new Label();
        lblMsg.Text = "スマホアプリからの接続を確認しました！\nPC側で管理画面を開くブラウザを選択してください。";
        lblMsg.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        lblMsg.ForeColor = Color.Navy;
        lblMsg.AutoSize = true;
        lblMsg.Dock = DockStyle.Fill;
        lblMsg.TextAlign = ContentAlignment.TopCenter;
        layout.Controls.Add(lblMsg);

        Button btnEdge = new Button();
        btnEdge.Text = "🌐 Microsoft Edge で開く";
        btnEdge.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        btnEdge.Height = 36;
        btnEdge.Dock = DockStyle.Fill;
        btnEdge.Click += (s, e) => { SelectedBrowser = "edge"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnEdge);

        Button btnChrome = new Button();
        btnChrome.Text = "🌐 Google Chrome で開く";
        btnChrome.Font = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
        btnChrome.Height = 36;
        btnChrome.Dock = DockStyle.Fill;
        btnChrome.Click += (s, e) => { SelectedBrowser = "chrome"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnChrome);

        Button btnDefault = new Button();
        btnDefault.Text = "💻 標準ブラウザで開く";
        btnDefault.Font = new Font("Meiryo UI", 9F);
        btnDefault.Height = 34;
        btnDefault.Dock = DockStyle.Fill;
        btnDefault.Click += (s, e) => { SelectedBrowser = "default"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnDefault);

        Button btnCancel = new Button();
        btnCancel.Text = "キャンセル（後で手動で開く）";
        btnCancel.Font = new Font("Meiryo UI", 9F);
        btnCancel.Height = 32;
        btnCancel.Dock = DockStyle.Fill;
        btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; };
        layout.Controls.Add(btnCancel);

        this.CancelButton = btnCancel;
        this.Controls.Add(layout);
    }
}

/// <summary>
/// 外部DLL依存なしでISO/IEC 18004規格準拠の正則QRコードBitmapを生成する純C#エンコーダ
/// (Level L, Version 1〜12, 8bit Byte Mode, マルチブロックRS ECC & インターリーブ, Version Info, 8マスク最適選択)
/// </summary>
public static class SimpleQrEncoder
{
    private class VersionSpec
    {
        public int Version;
        public int TotalDataBytes;
        public int EccPerBlock;
        public int Group1Blocks;
        public int Group1DataBytes;
        public int Group2Blocks;
        public int Group2DataBytes;
        public int RemainderBits;
        public int[] AlignPositions;

        public int MaxInputBytes
        {
            get
            {
                int headerBits = (Version <= 9) ? 12 : 20;
                return (TotalDataBytes * 8 - headerBits) / 8;
            }
        }
    }

    private static readonly VersionSpec[] LevelLSpecs = new VersionSpec[]
    {
        new VersionSpec { Version = 1,  TotalDataBytes = 19,  EccPerBlock = 7,  Group1Blocks = 1, Group1DataBytes = 19,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 0, AlignPositions = new int[0] },
        new VersionSpec { Version = 2,  TotalDataBytes = 34,  EccPerBlock = 10, Group1Blocks = 1, Group1DataBytes = 34,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 7, AlignPositions = new int[] { 6, 18 } },
        new VersionSpec { Version = 3,  TotalDataBytes = 55,  EccPerBlock = 15, Group1Blocks = 1, Group1DataBytes = 55,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 7, AlignPositions = new int[] { 6, 22 } },
        new VersionSpec { Version = 4,  TotalDataBytes = 80,  EccPerBlock = 20, Group1Blocks = 1, Group1DataBytes = 80,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 7, AlignPositions = new int[] { 6, 26 } },
        new VersionSpec { Version = 5,  TotalDataBytes = 108, EccPerBlock = 26, Group1Blocks = 1, Group1DataBytes = 108, Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 7, AlignPositions = new int[] { 6, 30 } },
        new VersionSpec { Version = 6,  TotalDataBytes = 136, EccPerBlock = 18, Group1Blocks = 2, Group1DataBytes = 68,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 7, AlignPositions = new int[] { 6, 34 } },
        new VersionSpec { Version = 7,  TotalDataBytes = 156, EccPerBlock = 20, Group1Blocks = 2, Group1DataBytes = 78,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 0, AlignPositions = new int[] { 6, 22, 38 } },
        new VersionSpec { Version = 8,  TotalDataBytes = 194, EccPerBlock = 24, Group1Blocks = 2, Group1DataBytes = 97,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 0, AlignPositions = new int[] { 6, 24, 42 } },
        new VersionSpec { Version = 9,  TotalDataBytes = 232, EccPerBlock = 30, Group1Blocks = 2, Group1DataBytes = 116, Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 0, AlignPositions = new int[] { 6, 26, 46 } },
        new VersionSpec { Version = 10, TotalDataBytes = 274, EccPerBlock = 18, Group1Blocks = 2, Group1DataBytes = 68,  Group2Blocks = 2, Group2DataBytes = 69, RemainderBits = 0, AlignPositions = new int[] { 6, 28, 50 } },
        new VersionSpec { Version = 11, TotalDataBytes = 324, EccPerBlock = 20, Group1Blocks = 4, Group1DataBytes = 81,  Group2Blocks = 0, Group2DataBytes = 0,  RemainderBits = 0, AlignPositions = new int[] { 6, 30, 54 } },
        new VersionSpec { Version = 12, TotalDataBytes = 370, EccPerBlock = 24, Group1Blocks = 2, Group1DataBytes = 92,  Group2Blocks = 2, Group2DataBytes = 93, RemainderBits = 0, AlignPositions = new int[] { 6, 32, 58 } }
    };

    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static SimpleQrEncoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfExp[i + 255] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        GfExp[510] = GfExp[0];
        GfExp[511] = GfExp[1];
    }

    public static Bitmap GenerateQrBitmap(string content, int targetWidth, int targetHeight)
    {
        bool[,] matrix = EncodeToMatrix(content);
        int matrixSize = matrix.GetLength(0);

        int quietZone = 4;
        int fullSize = matrixSize + quietZone * 2;
        int scale = Math.Max(1, Math.Min(targetWidth / fullSize, targetHeight / fullSize));
        int imgWidth = fullSize * scale;
        int imgHeight = fullSize * scale;

        Bitmap bmp = new Bitmap(imgWidth, imgHeight);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                for (int r = 0; r < matrixSize; r++)
                {
                    for (int c = 0; c < matrixSize; c++)
                    {
                        if (matrix[r, c])
                        {
                            g.FillRectangle(blackBrush, (c + quietZone) * scale, (r + quietZone) * scale, scale, scale);
                        }
                    }
                }
            }
        }
        return bmp;
    }

    public static bool[,] EncodeToMatrix(string content)
    {
        byte[] rawBytes = Encoding.UTF8.GetBytes(content ?? "");
        VersionSpec spec = null;
        foreach (var candidate in LevelLSpecs)
        {
            if (rawBytes.Length <= candidate.MaxInputBytes)
            {
                spec = candidate;
                break;
            }
        }
        if (spec == null)
        {
            throw new ArgumentException("QRコードのデータ長が上限を超えています (" + rawBytes.Length + " bytes)");
        }

        int version = spec.Version;
        int size = 17 + version * 4;
        bool[,] baseMatrix = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        // 1. ファインダパターン (3隅) + セパレータ
        PlaceFinderPattern(baseMatrix, reserved, 0, 0);
        PlaceFinderPattern(baseMatrix, reserved, size - 7, 0);
        PlaceFinderPattern(baseMatrix, reserved, 0, size - 7);

        // 2. タイミングパターン
        for (int i = 8; i < size - 8; i++)
        {
            if (!reserved[6, i]) { baseMatrix[6, i] = (i % 2 == 0); reserved[6, i] = true; }
            if (!reserved[i, 6]) { baseMatrix[i, 6] = (i % 2 == 0); reserved[i, 6] = true; }
        }

        // 3. アライメントパターン (ファインダ隅3箇所のみ除外し、タイミング線上は正しく配置する)
        int[] alignPos = spec.AlignPositions;
        if (alignPos.Length >= 2)
        {
            int first = alignPos[0];
            int last = alignPos[alignPos.Length - 1];
            foreach (int r in alignPos)
            {
                foreach (int c in alignPos)
                {
                    if ((r == first && c == first) || (r == first && c == last) || (r == last && c == first))
                    {
                        continue;
                    }
                    PlaceAlignmentPattern(baseMatrix, reserved, r - 2, c - 2);
                }
            }
        }

        // 4. フォーマット情報領域とダークモジュールの予約
        ReserveFormatInfo(baseMatrix, reserved, size);

        // 5. バージョン情報配置 (Version 7以上)
        if (version >= 7)
        {
            PlaceVersionInfo(baseMatrix, reserved, version, size);
        }

        // 6. データ構築 + ブロック分割RS ECC + インターリーブ + 余剰ビット
        List<bool> bits = BuildFinalBitStream(rawBytes, spec);

        // 7. マスクパターン 0〜7 をすべて試行し、ペナルティ最小のマスクを採用
        int bestPenalty = int.MaxValue;
        bool[,] bestMatrix = null;

        for (int maskPattern = 0; maskPattern < 8; maskPattern++)
        {
            bool[,] candidateMatrix = (bool[,])baseMatrix.Clone();
            PlaceDataBitsWithMask(candidateMatrix, reserved, bits, size, maskPattern);
            PlaceFormatInfo(candidateMatrix, size, maskPattern);

            int penalty = EvaluateMaskPenalty(candidateMatrix, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMatrix = candidateMatrix;
            }
        }

        return bestMatrix;
    }

    private static List<bool> BuildFinalBitStream(byte[] rawBytes, VersionSpec spec)
    {
        List<bool> dataBits = new List<bool>(spec.TotalDataBytes * 8);

        // モード指示子 (8bitバイトモード = 0100)
        dataBits.Add(false); dataBits.Add(true); dataBits.Add(false); dataBits.Add(false);

        // 文字数指示子 (Version 1〜9 は 8bit、Version 10〜26 は 16bit)
        int countBitLen = (spec.Version <= 9) ? 8 : 16;
        int len = rawBytes.Length;
        for (int i = countBitLen - 1; i >= 0; i--)
        {
            dataBits.Add(((len >> i) & 1) == 1);
        }

        // ペイロード
        foreach (byte b in rawBytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                dataBits.Add(((b >> i) & 1) == 1);
            }
        }

        int totalDataBits = spec.TotalDataBytes * 8;
        // 終端パターン (最大4ビットの0)
        for (int i = 0; i < 4 && dataBits.Count < totalDataBits; i++)
        {
            dataBits.Add(false);
        }

        // 8ビット境界パディング
        while (dataBits.Count % 8 != 0 && dataBits.Count < totalDataBits)
        {
            dataBits.Add(false);
        }

        // 埋め草コード語 (0xEC, 0x11)
        byte[] padBytes = new byte[] { 0xEC, 0x11 };
        int padIdx = 0;
        while (dataBits.Count < totalDataBits)
        {
            byte pad = padBytes[padIdx % 2];
            for (int i = 7; i >= 0; i--)
            {
                dataBits.Add(((pad >> i) & 1) == 1);
            }
            padIdx++;
        }

        byte[] dataCodewords = new byte[spec.TotalDataBytes];
        for (int i = 0; i < spec.TotalDataBytes; i++)
        {
            byte b = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if (dataBits[i * 8 + bit]) b |= (byte)(1 << (7 - bit));
            }
            dataCodewords[i] = b;
        }

        // ブロック分割とリード・ソロモンECC算出
        int totalBlocks = spec.Group1Blocks + spec.Group2Blocks;
        byte[][] dataBlocks = new byte[totalBlocks][];
        byte[][] eccBlocks = new byte[totalBlocks][];

        int offset = 0;
        int maxDataBlockLen = 0;
        for (int b = 0; b < totalBlocks; b++)
        {
            int blockLen = (b < spec.Group1Blocks) ? spec.Group1DataBytes : spec.Group2DataBytes;
            if (blockLen > maxDataBlockLen) maxDataBlockLen = blockLen;
            byte[] blockData = new byte[blockLen];
            Array.Copy(dataCodewords, offset, blockData, 0, blockLen);
            offset += blockLen;

            dataBlocks[b] = blockData;
            eccBlocks[b] = CalculateReedSolomonECC(blockData, spec.EccPerBlock);
        }

        // インターリーブ配置 (データ -> ECC)
        List<byte> interleaved = new List<byte>(spec.TotalDataBytes + totalBlocks * spec.EccPerBlock);
        for (int col = 0; col < maxDataBlockLen; col++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                if (col < dataBlocks[b].Length)
                {
                    interleaved.Add(dataBlocks[b][col]);
                }
            }
        }
        for (int col = 0; col < spec.EccPerBlock; col++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                interleaved.Add(eccBlocks[b][col]);
            }
        }

        List<bool> resultBits = new List<bool>(interleaved.Count * 8 + spec.RemainderBits);
        foreach (byte cw in interleaved)
        {
            for (int i = 7; i >= 0; i--)
            {
                resultBits.Add(((cw >> i) & 1) == 1);
            }
        }
        for (int i = 0; i < spec.RemainderBits; i++)
        {
            resultBits.Add(false);
        }

        return resultBits;
    }

    private static byte[] CalculateReedSolomonECC(byte[] data, int eccLen)
    {
        // 生成多項式 (降順: genPoly[0] = 1 * x^eccLen)
        byte[] genPoly = new byte[] { 1 };
        for (int i = 0; i < eccLen; i++)
        {
            byte[] next = new byte[i + 2];
            for (int j = 0; j < genPoly.Length; j++)
            {
                next[j] ^= genPoly[j];
                if (genPoly[j] != 0)
                {
                    next[j + 1] ^= GfExp[GfLog[genPoly[j]] + i];
                }
            }
            genPoly = next;
        }

        byte[] rem = new byte[eccLen];
        foreach (byte d in data)
        {
            byte factor = (byte)(d ^ rem[0]);
            for (int k = 0; k < eccLen - 1; k++)
            {
                rem[k] = rem[k + 1];
            }
            rem[eccLen - 1] = 0;
            if (factor != 0)
            {
                int logFactor = GfLog[factor];
                for (int k = 0; k < eccLen; k++)
                {
                    byte gp = genPoly[k + 1];
                    if (gp != 0)
                    {
                        rem[k] ^= GfExp[logFactor + GfLog[gp]];
                    }
                }
            }
        }
        return rem;
    }

    private static bool IsMaskBit(int maskPattern, int r, int c)
    {
        switch (maskPattern)
        {
            case 0: return ((r + c) % 2) == 0;
            case 1: return (r % 2) == 0;
            case 2: return (c % 3) == 0;
            case 3: return ((r + c) % 3) == 0;
            case 4: return (((r / 2) + (c / 3)) % 2) == 0;
            case 5: return (((r * c) % 2) + ((r * c) % 3)) == 0;
            case 6: return ((((r * c) % 2) + ((r * c) % 3)) % 2) == 0;
            case 7: return ((((r + c) % 2) + ((r * c) % 3)) % 2) == 0;
            default: return false;
        }
    }

    private static void PlaceDataBitsWithMask(bool[,] matrix, bool[,] reserved, List<bool> bits, int size, int maskPattern)
    {
        int bitIdx = 0;
        int direction = -1; // up
        int col = size - 1;
        while (col > 0)
        {
            if (col == 6) col--; // Skip vertical timing column
            int rowStart = (direction == -1) ? size - 1 : 0;
            int rowEnd = (direction == -1) ? -1 : size;
            for (int row = rowStart; row != rowEnd; row += direction)
            {
                for (int c = col; c > col - 2; c--)
                {
                    if (!reserved[row, c])
                    {
                        bool bitVal = (bitIdx < bits.Count) ? bits[bitIdx++] : false;
                        matrix[row, c] = bitVal ^ IsMaskBit(maskPattern, row, c);
                    }
                }
            }
            direction = -direction;
            col -= 2;
        }
    }

    private static void ReserveFormatInfo(bool[,] matrix, bool[,] reserved, int size)
    {
        for (int i = 0; i <= 8; i++)
        {
            if (i < size) { reserved[8, i] = true; reserved[i, 8] = true; }
        }
        for (int i = size - 8; i < size; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        reserved[size - 8, 8] = true;
        matrix[size - 8, 8] = true; // Dark module
    }

    private static void PlaceFormatInfo(bool[,] matrix, int size, int maskPattern)
    {
        // EC Level L (01) + 3bit Mask Pattern -> 5bit data, BCH(15,5) with 0x537, XOR 0x5412
        int data5 = (0x01 << 3) | (maskPattern & 0x07);
        int rem = data5 << 10;
        for (int i = 14; i >= 10; i--)
        {
            if (((rem >> i) & 1) != 0)
            {
                rem ^= (0x537 << (i - 10));
            }
        }
        int formatWord = ((data5 << 10) | rem) ^ 0x5412;

        // Top-Left Finder Format Info (MSB bit 14 to LSB bit 0)
        int bitPos = 14;
        for (int col = 0; col <= 8; col++)
        {
            if (col != 6)
            {
                matrix[8, col] = ((formatWord >> bitPos) & 1) == 1;
                bitPos--;
            }
        }
        for (int row = 7; row >= 0; row--)
        {
            if (row != 6)
            {
                matrix[row, 8] = ((formatWord >> bitPos) & 1) == 1;
                bitPos--;
            }
        }

        // Bottom-Left & Top-Right Split Format Info (MSB bit 14 to LSB bit 0)
        bitPos = 14;
        for (int row = size - 1; row >= size - 7; row--)
        {
            matrix[row, 8] = ((formatWord >> bitPos) & 1) == 1;
            bitPos--;
        }
        for (int col = size - 8; col < size; col++)
        {
            matrix[8, col] = ((formatWord >> bitPos) & 1) == 1;
            bitPos--;
        }
    }

    private static void PlaceVersionInfo(bool[,] matrix, bool[,] reserved, int version, int size)
    {
        // 6bit version, Golay BCH(18,6) with generator 0x1F25
        int rem = version << 12;
        for (int i = 17; i >= 12; i--)
        {
            if (((rem >> i) & 1) != 0)
            {
                rem ^= (0x1F25 << (i - 12));
            }
        }
        int versionWord = (version << 12) | rem;

        for (int i = 0; i < 18; i++)
        {
            bool bit = ((versionWord >> i) & 1) == 1;
            int a = size - 11 + (i % 3);
            int b = i / 3;

            matrix[a, b] = bit;
            reserved[a, b] = true;

            matrix[b, a] = bit;
            reserved[b, a] = true;
        }
    }

    private static int EvaluateMaskPenalty(bool[,] m, int size)
    {
        int penalty = 0;

        // N1 行・列ごとの連続同色モジュール (5以上 -> 3 + (run - 5))
        for (int r = 0; r < size; r++)
        {
            int runRow = 1;
            int runCol = 1;
            for (int c = 1; c < size; c++)
            {
                if (m[r, c] == m[r, c - 1]) runRow++;
                else { if (runRow >= 5) penalty += 3 + (runRow - 5); runRow = 1; }

                if (m[c, r] == m[c - 1, r]) runCol++;
                else { if (runCol >= 5) penalty += 3 + (runCol - 5); runCol = 1; }
            }
            if (runRow >= 5) penalty += 3 + (runRow - 5);
            if (runCol >= 5) penalty += 3 + (runCol - 5);
        }

        // N2 2x2 同色ブロック (+3)
        for (int r = 0; r < size - 1; r++)
        {
            for (int c = 0; c < size - 1; c++)
            {
                bool v = m[r, c];
                if (v == m[r, c + 1] && v == m[r + 1, c] && v == m[r + 1, c + 1])
                {
                    penalty += 3;
                }
            }
        }

        // N3 ファインダ様 1-1-3-1-1 比率パターン (+40)
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c <= size - 11; c++)
            {
                // Row check 10111010000 or 00001011101
                if ((m[r, c] && !m[r, c + 1] && m[r, c + 2] && m[r, c + 3] && m[r, c + 4] && !m[r, c + 5] && m[r, c + 6] && !m[r, c + 7] && !m[r, c + 8] && !m[r, c + 9] && !m[r, c + 10]) ||
                    (!m[r, c] && !m[r, c + 1] && !m[r, c + 2] && !m[r, c + 3] && m[r, c + 4] && !m[r, c + 5] && m[r, c + 6] && m[r, c + 7] && m[r, c + 8] && !m[r, c + 9] && m[r, c + 10]))
                {
                    penalty += 40;
                }
                // Col check
                if ((m[c, r] && !m[c + 1, r] && m[c + 2, r] && m[c + 3, r] && m[c + 4, r] && !m[c + 5, r] && m[c + 6, r] && !m[c + 7, r] && !m[c + 8, r] && !m[c + 9, r] && !m[c + 10, r]) ||
                    (!m[c, r] && !m[c + 1, r] && !m[c + 2, r] && !m[c + 3, r] && m[c + 4, r] && !m[c + 5, r] && m[c + 6, r] && m[c + 7, r] && m[c + 8, r] && !m[c + 9, r] && m[c + 10, r]))
                {
                    penalty += 40;
                }
            }
        }

        // N4 黒モジュール割合の50%からの乖離
        int darkCount = 0;
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (m[r, c]) darkCount++;
            }
        }
        int totalModules = size * size;
        int prev5 = (darkCount * 100 / totalModules) / 5 * 5;
        int next5 = prev5 + 5;
        int k1 = Math.Abs(prev5 - 50) / 5;
        int k2 = Math.Abs(next5 - 50) / 5;
        penalty += Math.Min(k1, k2) * 10;

        return penalty;
    }

    private static void PlaceFinderPattern(bool[,] matrix, bool[,] reserved, int top, int left)
    {
        for (int r = -1; r <= 7; r++)
        {
            for (int c = -1; c <= 7; c++)
            {
                int row = top + r;
                int col = left + c;
                if (row >= 0 && row < matrix.GetLength(0) && col >= 0 && col < matrix.GetLength(1))
                {
                    reserved[row, col] = true;
                    if (r >= 0 && r <= 6 && c >= 0 && c <= 6)
                    {
                        bool isBlack = (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                        matrix[row, col] = isBlack;
                    }
                    else
                    {
                        matrix[row, col] = false;
                    }
                }
            }
        }
    }

    private static void PlaceAlignmentPattern(bool[,] matrix, bool[,] reserved, int top, int left)
    {
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                int row = top + r;
                int col = left + c;
                if (row >= 0 && row < matrix.GetLength(0) && col >= 0 && col < matrix.GetLength(1))
                {
                    reserved[row, col] = true;
                    bool isBlack = (r == 0 || r == 4 || c == 0 || c == 4 || (r == 2 && c == 2));
                    matrix[row, col] = isBlack;
                }
            }
        }
    }
}
