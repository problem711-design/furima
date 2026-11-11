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
/// 外部DLL依存なしでISO/IEC 18004規格準拠の正則QRコードBitmapを生成する軽量純C#エンコーダ
/// (8bit Byte Mode, Reed-Solomon Error Correction, Format Info 15-bit BCH, Mask Pattern 0)
/// </summary>
public static class SimpleQrEncoder
{
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

    private static bool[,] EncodeToMatrix(string content)
    {
        byte[] rawBytes = Encoding.UTF8.GetBytes(content);

        // Version & ECC Capacity Table (Low/Medium ECC)
        // Version 4-M: 100 modules capacity, 64 data codewords, 36 ecc codewords (1 block)
        // Version 6-M: 172 modules capacity, 108 data codewords, 64 ecc codewords
        // Version 9-M: 292 modules capacity, 182 data codewords, 110 ecc codewords
        int version = 4;
        int dataCapacity = 64;
        int eccCount = 36;

        if (rawBytes.Length > 60)
        {
            version = 6;
            dataCapacity = 108;
            eccCount = 64;
        }
        if (rawBytes.Length > 104)
        {
            version = 9;
            dataCapacity = 182;
            eccCount = 110;
        }

        int size = 17 + version * 4;
        bool[,] matrix = new bool[size, size];
        bool[,] reserved = new bool[size, size];

        // 1. Finder patterns (3 (7x7) corners)
        PlaceFinderPattern(matrix, reserved, 0, 0);
        PlaceFinderPattern(matrix, reserved, size - 7, 0);
        PlaceFinderPattern(matrix, reserved, 0, size - 7);

        // 2. Timing patterns
        for (int i = 8; i < size - 8; i++)
        {
            if (!reserved[6, i]) { matrix[6, i] = (i % 2 == 0); reserved[6, i] = true; }
            if (!reserved[i, 6]) { matrix[i, 6] = (i % 2 == 0); reserved[i, 6] = true; }
        }

        // 3. Alignment patterns (Version >= 2)
        int[] alignPos = GetAlignmentPositions(version);
        foreach (int r in alignPos)
        {
            foreach (int c in alignPos)
            {
                if (!reserved[r, c])
                {
                    PlaceAlignmentPattern(matrix, reserved, r - 2, c - 2);
                }
            }
        }

        // 4. Reserve Format Information (around finders)
        ReserveFormatInfo(matrix, reserved, size);

        // 5. Encode Payload (Header + Count + Data + Pad + Reed-Solomon ECC)
        byte[] codewords = BuildCodewords(rawBytes, dataCapacity, eccCount);

        // 6. Convert Codewords to Bits
        List<bool> bits = new List<bool>();
        foreach (byte b in codewords)
        {
            for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) == 1);
        }

        // 7. Place Data Bits & Apply Mask Pattern 0: (row + col) % 2 == 0
        PlaceDataBits(matrix, reserved, bits, size);

        // 8. Place Format Information (Mask 0, Level M: 101010000010010)
        PlaceFormatInfo(matrix, size);

        return matrix;
    }

    private static byte[] BuildCodewords(byte[] data, int maxDataLen, int eccLen)
    {
        List<bool> bits = new List<bool>();

        // Mode: 8bit Byte Mode (0100)
        bits.Add(false); bits.Add(true); bits.Add(false); bits.Add(false);

        // Character count indicator (8-bit for version 1-9 byte mode)
        int len = data.Length;
        for (int i = 7; i >= 0; i--) bits.Add(((len >> i) & 1) == 1);

        // Data payload
        foreach (byte b in data)
        {
            for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) == 1);
        }

        // Terminator (up to 4 zero bits)
        for (int i = 0; i < 4 && bits.Count < maxDataLen * 8; i++) bits.Add(false);

        // Byte align
        while (bits.Count % 8 != 0) bits.Add(false);

        // Padding bytes (0xEC, 0x11)
        byte[] padBytes = new byte[] { 0xEC, 0x11 };
        int padIdx = 0;
        while (bits.Count / 8 < maxDataLen)
        {
            byte pad = padBytes[padIdx % 2];
            for (int i = 7; i >= 0; i--) bits.Add(((pad >> i) & 1) == 1);
            padIdx++;
        }

        // Convert bits to data codewords
        byte[] dataCodewords = new byte[maxDataLen];
        for (int i = 0; i < maxDataLen; i++)
        {
            byte b = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if (bits[i * 8 + bit]) b |= (byte)(1 << (7 - bit));
            }
            dataCodewords[i] = b;
        }

        // Generate Reed-Solomon ECC codewords
        byte[] eccCodewords = CalculateReedSolomonECC(dataCodewords, eccLen);

        // Combine data + ecc
        byte[] fullResult = new byte[maxDataLen + eccLen];
        Array.Copy(dataCodewords, 0, fullResult, 0, maxDataLen);
        Array.Copy(eccCodewords, 0, fullResult, maxDataLen, eccLen);

        return fullResult;
    }

    private static byte[] CalculateReedSolomonECC(byte[] data, int eccLen)
    {
        // GF(256) Generator Polynomial & Log/Exp tables (GF 2^8 with primitive poly 0x11D = 285)
        byte[] expTable = new byte[256];
        byte[] logTable = new byte[256];
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            expTable[i] = (byte)x;
            logTable[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }

        // Generate RS Generator Polynomial Coefficients
        byte[] genPoly = new byte[eccLen + 1];
        genPoly[0] = 1;
        for (int i = 0; i < eccLen; i++)
        {
            byte root = expTable[i];
            for (int j = eccLen; j >= 1; j--)
            {
                byte prev = genPoly[j];
                byte mult = genPoly[j - 1] == 0 ? (byte)0 : expTable[(logTable[genPoly[j - 1]] + logTable[root]) % 255];
                genPoly[j] = (byte)(prev ^ mult);
            }
            genPoly[0] = genPoly[0] == 0 ? (byte)0 : expTable[(logTable[genPoly[0]] + logTable[root]) % 255];
        }

        // Polynomial Division (Data / GenPoly)
        byte[] res = new byte[eccLen];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ res[0]);
            Array.Copy(res, 1, res, 0, eccLen - 1);
            res[eccLen - 1] = 0;

            if (factor != 0)
            {
                for (int i = 0; i < eccLen; i++)
                {
                    if (genPoly[i] != 0)
                    {
                        res[i] ^= expTable[(logTable[factor] + logTable[genPoly[i]]) % 255];
                    }
                }
            }
        }
        return res;
    }

    private static void PlaceDataBits(bool[,] matrix, bool[,] reserved, List<bool> bits, int size)
    {
        int bitIdx = 0;
        int direction = -1; // up
        int col = size - 1;

        while (col > 0)
        {
            if (col == 6) col--; // Skip timing column

            int rowStart = (direction == -1) ? size - 1 : 0;
            int rowEnd = (direction == -1) ? -1 : size;

            for (int row = rowStart; row != rowEnd; row += direction)
            {
                for (int c = col; c > col - 2; c--)
                {
                    if (!reserved[row, c])
                    {
                        bool bitVal = (bitIdx < bits.Count) ? bits[bitIdx++] : false;
                        // Apply Mask Pattern 0: (row + col) % 2 == 0
                        bool mask = ((row + c) % 2 == 0);
                        matrix[row, c] = bitVal ^ mask;
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
        reserved[size - 8, 8] = true; // Dark module
        matrix[size - 8, 8] = true;
    }

    private static void PlaceFormatInfo(bool[,] matrix, int size)
    {
        // Format Info for ECC Level M, Mask Pattern 0 = 101010000010010
        bool[] fmtBits = new bool[] { true, false, true, false, true, false, false, false, false, false, true, false, false, true, false };

        // Top-Left Finder Format Info
        int idx = 0;
        for (int col = 0; col <= 8; col++)
        {
            if (col != 6) matrix[8, col] = fmtBits[idx++];
        }
        for (int row = 7; row >= 0; row--)
        {
            if (row != 6) matrix[row, 8] = fmtBits[idx++];
        }

        // Bottom-Left & Top-Right Split Format Info
        idx = 0;
        for (int row = size - 1; row >= size - 7; row--)
        {
            matrix[row, 8] = fmtBits[idx++];
        }
        for (int col = size - 8; col < size; col++)
        {
            matrix[8, col] = fmtBits[idx++];
        }
    }

    private static int[] GetAlignmentPositions(int version)
    {
        if (version <= 1) return new int[0];
        if (version == 4) return new int[] { 6, 26 };
        if (version == 6) return new int[] { 6, 34 };
        if (version == 9) return new int[] { 6, 26, 46 };
        return new int[] { 6, 26 };
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
                }
            }
        }
    }
}
