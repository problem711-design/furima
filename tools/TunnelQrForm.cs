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
        lblStatus.ForeColor = Color.DarkGreen;
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
        picQrCode.Anchor = AnchorStyles.Center;
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
        btnPanel.Alignment = ToolStripItemAlignment.Right;
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
        trayMenu.Items.Add("母艦サーバーを終了(&X)", null, (s, e) => { ExitApp(); });

        notifyIcon = new NotifyIcon();
        notifyIcon.Text = "フリマ母艦サーバー (稼働中)";
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.ContextMenuStrip = trayMenu;
        notifyIcon.Visible = true;
        notifyIcon.DoubleClick += (s, e) => { RestoreFromTray(); };
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
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(MinimizeToTray));
            return;
        }

        this.Hide();
        notifyIcon.ShowBalloonTip(2000, "フリマ母艦サーバー", "バックグラウンドで動作中です。トレイアイコンのダブルクリックで再表示できます。", ToolTipIcon.Info);
    }

    public void RestoreFromTray()
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action(RestoreFromTray));
            return;
        }

        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
    }

    public void ExitApp()
    {
        isExiting = true;
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        this.Close();
    }

    /// <summary>
    /// スマホアプリからの接続検知時に呼び出される処理
    /// </summary>
    public void OnAppConnected()
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(new Action(OnAppConnected));
            return;
        }

        lblStatus.Text = "● スマホアプリ接続中";
        lblStatus.ForeColor = Color.DarkGreen;

        if (hasPromptedBrowser) return;
        hasPromptedBrowser = true;

        // ウィンドウを前面に復元
        RestoreFromTray();

        // ブラウザ選択ダイアログを表示し、選択後に自動トレイ格納
        ShowBrowserSelectionDialog();
    }

    public void ShowBrowserSelectionDialog()
    {
        using (BrowserSelectDialog dlg = new BrowserSelectDialog())
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
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
                try { Process.Start("msedge.exe", url); return; } catch { }
            }
            else if (browserType == "chrome")
            {
                try { Process.Start("chrome.exe", url); return; } catch { }
            }
            Process.Start(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ブラウザ起動に失敗しました: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void UpdateTunnelUrl(string tunnelUrl)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(new Action<string>(UpdateTunnelUrl), tunnelUrl);
            return;
        }

        this.currentTunnelUrl = tunnelUrl;
        if (!string.IsNullOrEmpty(tunnelUrl))
        {
            txtTunnelUrl.Text = tunnelUrl;
            if (!lblStatus.Text.Contains("接続中"))
            {
                lblStatus.Text = "● トンネル接続完了 (QRコード準備完了)";
                lblStatus.ForeColor = Color.Blue;
            }
        }
        else
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
        if (this.InvokeRequired)
        {
            this.Invoke(new Action<int, int>(UpdateStatusInfo), indexedCount, missingCount);
            return;
        }

        lblInfo.Text = string.Format("スマホアプリ「PC連携」でQRを読込んでください。\n索引写真: {0}枚 | 原本欠落: {1}枚", indexedCount, missingCount);
    }

    private void UpdateDisplay()
    {
        string activeUrl = string.IsNullOrEmpty(currentTunnelUrl) ? currentLocalUrl : currentTunnelUrl;
        string qrText = string.Format("furima:{0}|{1}|{2}", activeUrl, currentToken, currentStoreId);

        try
        {
            Bitmap qrBmp = SimpleQrEncoder.GenerateQrBitmap(qrText, 240, 240);
            if (picQrCode.Image != null) picQrCode.Image.Dispose();
            picQrCode.Image = qrBmp;
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
        this.Size = new Size(420, 260);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

        TableLayoutPanel layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(20);
        layout.RowCount = 4;
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
        btnDefault.Text = "💻 標準ブラウザで開く / キャンセル";
        btnDefault.Font = new Font("Meiryo UI", 9F);
        btnDefault.Height = 32;
        btnDefault.Dock = DockStyle.Fill;
        btnDefault.Click += (s, e) => { SelectedBrowser = "default"; this.DialogResult = DialogResult.OK; };
        layout.Controls.Add(btnDefault);

        this.Controls.Add(layout);
    }
}

/// <summary>
/// 外部DLL依存なしでQRコードBitmapを自前生成する軽量エンコーダクラス
/// </summary>
public static class SimpleQrEncoder
{
    public static Bitmap GenerateQrBitmap(string content, int width, int height)
    {
        bool[,] modules = EncodeToMatrix(content);
        int matrixSize = modules.GetLength(0);

        Bitmap bmp = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            float scale = Math.Min((float)width / (matrixSize + 4), (float)height / (matrixSize + 4));
            float offsetX = (width - matrixSize * scale) / 2f;
            float offsetY = (height - matrixSize * scale) / 2f;

            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                for (int r = 0; r < matrixSize; r++)
                {
                    for (int c = 0; c < matrixSize; c++)
                    {
                        if (modules[r, c])
                        {
                            g.FillRectangle(blackBrush, offsetX + c * scale, offsetY + r * scale, scale + 0.5f, scale + 0.5f);
                        }
                    }
                }
            }
        }
        return bmp;
    }

    private static bool[,] EncodeToMatrix(string content)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(content);
        int version = 4;
        if (dataBytes.Length > 62) version = 7;
        if (dataBytes.Length > 120) version = 10;

        int size = 17 + version * 4;
        bool[,] matrix = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        // 位置検出パターン (3箇所)
        PlaceFinderPattern(matrix, reserved, 0, 0);
        PlaceFinderPattern(matrix, reserved, size - 7, 0);
        PlaceFinderPattern(matrix, reserved, 0, size - 7);

        // タイミングパターン
        for (int i = 8; i < size - 8; i++)
        {
            matrix[6, i] = (i % 2 == 0);
            reserved[6, i] = true;
            matrix[i, 6] = (i % 2 == 0);
            reserved[i, 6] = true;
        }

        // アライメントパターン (Version >= 2)
        if (version >= 2)
        {
            int alignPos = size - 7;
            PlaceAlignmentPattern(matrix, reserved, alignPos - 2, alignPos - 2);
        }

        // 簡易データビット設定 (8bit Byte Mode)
        List<bool> bits = new List<bool>();
        // Mode indicator for Byte (0100)
        bits.Add(false); bits.Add(true); bits.Add(false); bits.Add(false);

        // Character count indicator (8 bits for version 1-9 byte mode)
        int count = dataBytes.Length;
        for (int i = 7; i >= 0; i--) bits.Add(((count >> i) & 1) == 1);

        // Data payload
        foreach (byte b in dataBytes)
        {
            for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) == 1);
        }

        // Terminator
        for (int i = 0; i < 4; i++) bits.Add(false);

        // マトリックスへの配置
        int bitIdx = 0;
        int direction = -1; // up
        int x = size - 1;
        while (x > 0)
        {
            if (x == 6) x--; // Skip timing column
            for (int y = (direction == -1 ? size - 1 : 0); y >= 0 && y < size; y += direction)
            {
                for (int col = x; col > x - 2; col--)
                {
                    if (!reserved[y, col])
                    {
                        bool val = false;
                        if (bitIdx < bits.Count)
                        {
                            val = bits[bitIdx++];
                        }
                        else
                        {
                            // Padding Pattern
                            val = ((y + col) % 2 == 0);
                        }
                        matrix[y, col] = val;
                    }
                }
            }
            direction = -direction;
            x -= 2;
        }

        return matrix;
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
                    if (!reserved[row, col])
                    {
                        reserved[row, col] = true;
                        bool isBlack = (r == 0 || r == 4 || c == 0 || c == 4 || (r == 2 && c == 2));
                        matrix[row, col] = isBlack;
                    }
                }
            }
        }
    }
}
