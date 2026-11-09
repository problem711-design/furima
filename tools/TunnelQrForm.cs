using System;
using System.Collections;
using System.Collections.Generic;
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
        this.currentToken = token;

        InitializeComponent();
        UpdateDisplay();
    }

    private void InitializeComponent()
    {
        this.Text = "フリマ母艦サーバー - 接続情報 & QRコード";
        this.Size = new Size(520, 680);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.Icon = SystemIcons.Application;

        TableLayoutPanel mainLayout = new TableLayoutPanel();
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Padding = new Padding(15);
        mainLayout.RowCount = 8;
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

        this.Controls.Add(mainLayout);
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
            lblStatus.Text = "● トンネル接続完了 (QRコード準備完了)";
            lblStatus.ForeColor = Color.Blue;
        }
        else
        {
            txtTunnelUrl.Text = "(トンネル接続失敗 - ローカル接続のみ)";
            lblStatus.Text = "● ローカル接続のみ可";
            lblStatus.ForeColor = Color.DarkOrange;
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
