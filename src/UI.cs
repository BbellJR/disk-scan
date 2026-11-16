// UI ของ Disk Scan — หน้าจอเรียบง่าย 3 ขั้น: เลือกไดร์ฟ -> กำลังสแกน -> ผลลัพธ์

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiskScan
{
    static class Theme
    {
        public static float Scale = 1f;
        public static readonly Color Bg = Color.FromArgb(244, 246, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(225, 229, 236);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentSoft = Color.FromArgb(232, 240, 254);
        public static readonly Color Hover = Color.FromArgb(247, 249, 252);
        public static readonly Color Track = Color.FromArgb(229, 231, 235);
        public const string FontName = "Leelawadee UI";

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Font F(float size) { return new Font(FontName, size); }
        public static Font F(float size, FontStyle st) { return new Font(FontName, size, st); }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, Rectangle r, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, Rectangle r, int radius, float width)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static Color LevelColor(Level l)
        {
            switch (l)
            {
                case Level.Danger: return Color.FromArgb(220, 38, 38);
                case Level.Caution: return Color.FromArgb(217, 119, 6);
                case Level.Info: return Color.FromArgb(22, 163, 74);
                default: return Accent;
            }
        }

        public static Color LevelSoft(Level l)
        {
            switch (l)
            {
                case Level.Danger: return Color.FromArgb(254, 226, 226);
                case Level.Caution: return Color.FromArgb(254, 243, 199);
                case Level.Info: return Color.FromArgb(220, 252, 231);
                default: return Color.FromArgb(239, 243, 250);
            }
        }

        public static Color LevelText(Level l)
        {
            switch (l)
            {
                case Level.Danger: return Color.FromArgb(185, 28, 28);
                case Level.Caution: return Color.FromArgb(146, 64, 14);
                case Level.Info: return Color.FromArgb(21, 128, 61);
                default: return Text;
            }
        }

        public static Button Btn(string text, bool primary)
        {
            Button b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Padding = new Padding(S(12), S(4), S(12), S(4)),
                Font = F(10f, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? Accent : Card,
                ForeColor = primary ? Color.White : Text,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Hover;
            return b;
        }

        public static string Fmt(long b)
        {
            if (b >= 1L << 40) return (b / (double)(1L << 40)).ToString("0.00") + " TB";
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            if (b >= 1L << 10) return (b / (double)(1L << 10)).ToString("0.0") + " KB";
            return b + " B";
        }

        public static string Pct(long part, long total)
        {
            if (total <= 0) return "0%";
            double p = part * 100.0 / total;
            return (p > 0 && p < 0.1) ? "<0.1%" : p.ToString("0.0") + "%";
        }

        public const TextFormatFlags One = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
    }

    // ------------------------------------------------------------------ การ์ดไดร์ฟ

    class DriveCard : Control
    {
        public readonly DriveInfo Drive;
        readonly string label;
        readonly long total, free;
        readonly bool isSystem;
        bool hover;

        public DriveCard(DriveInfo d)
        {
            Drive = d;
            total = d.TotalSize;
            free = d.TotalFreeSpace;
            label = string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
            string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            isSystem = string.Equals(sys, d.Name, StringComparison.OrdinalIgnoreCase);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Theme.S(330), Theme.S(132));
            Margin = new Padding(0, 0, Theme.S(16), Theme.S(16));
            Cursor = Cursors.Hand;
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s8 = Theme.S(8), s16 = Theme.S(18);
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            Theme.FillRound(g, hover ? Theme.AccentSoft : Theme.Card, r, Theme.S(10));
            Theme.DrawRound(g, hover ? Theme.Accent : Theme.Border, r, Theme.S(10), hover ? 2f : 1f);

            // ไอคอนไดร์ฟ
            Rectangle ic = new Rectangle(s16, s16, Theme.S(46), Theme.S(46));
            Theme.FillRound(g, Theme.Accent, ic, Theme.S(10));
            using (Font f = Theme.F(14f, FontStyle.Bold))
                TextRenderer.DrawText(g, Drive.Name.TrimEnd('\\'), f, ic, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int tx = ic.Right + Theme.S(14);
            using (Font f = Theme.F(12f, FontStyle.Bold))
                TextRenderer.DrawText(g, label, f, new Rectangle(tx, s16 - Theme.S(2), Width - tx - s16, Theme.S(26)), Theme.Text, Theme.One);
            using (Font f = Theme.F(9.5f))
                TextRenderer.DrawText(g, hover ? "คลิกเพื่อสแกน →" : (isSystem ? "ไดร์ฟระบบ (Windows)" : "ไดร์ฟเก็บข้อมูล"),
                    f, new Rectangle(tx, s16 + Theme.S(24), Width - tx - s16, Theme.S(22)), hover ? Theme.Accent : Theme.Muted, Theme.One);

            // แถบพื้นที่
            long used = total - free;
            double frac = total > 0 ? used / (double)total : 0;
            Rectangle bar = new Rectangle(s16, Theme.S(80), Width - s16 * 2, Theme.S(10));
            Theme.FillRound(g, Theme.Track, bar, Theme.S(5));
            Color bc = frac > 0.9 ? Theme.LevelColor(Level.Danger) : frac > 0.75 ? Theme.LevelColor(Level.Caution) : Theme.Accent;
            Theme.FillRound(g, bc, new Rectangle(bar.X, bar.Y, Math.Max(bar.Height, (int)(bar.Width * frac)), bar.Height), Theme.S(5));

            using (Font f = Theme.F(9.5f))
                TextRenderer.DrawText(g, string.Format("ใช้ไป {0} จาก {1}   •   ว่าง {2}", Theme.Fmt(used), Theme.Fmt(total), Theme.Fmt(free)),
                    f, new Rectangle(s16, Theme.S(96), Width - s16 * 2, Theme.S(22)), Theme.Muted, Theme.One);
        }
    }

    // ------------------------------------------------------------------ รายการผลลัพธ์ (วาดเอง)

    class Row
    {
        public string Name, Sub, Right;
        public double Frac;          // ความยาวแถบ 0..1
        public string PctText;
        public Level Level;
        public int Kind;             // 0 โฟลเดอร์, 1 ไฟล์, 2 ไฟล์เล็กรวม, 3 ชนิดไฟล์
        public object Tag;
    }

    class ItemList : Control
    {
        List<Row> rows = new List<Row>();
        readonly VScrollBar vs = new VScrollBar();
        int selected = -1, hover = -1;
        public string EmptyText = "";

        public event EventHandler SelectionChanged;
        public event EventHandler RowActivated;

        public ItemList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Card;
            vs.Dock = DockStyle.Right;
            vs.Scroll += (s, e) => Invalidate();
            vs.ValueChanged += (s, e) => Invalidate();
            Controls.Add(vs);
        }

        int RowH { get { return Theme.S(58); } }
        int Offset { get { return vs.Visible ? vs.Value : 0; } }

        public Row SelectedRow { get { return selected >= 0 && selected < rows.Count ? rows[selected] : null; } }

        public void SetRows(List<Row> list)
        {
            rows = list;
            selected = -1;
            hover = -1;
            UpdateScroll();
            vs.Value = 0;
            Invalidate();
        }

        void UpdateScroll()
        {
            int content = rows.Count * RowH;
            if (content > Height && Height > 0)
            {
                vs.Visible = true;
                vs.Minimum = 0;
                vs.LargeChange = Math.Max(1, Height);
                vs.SmallChange = RowH;
                vs.Maximum = content - 1;
                if (vs.Value > content - Height) vs.Value = Math.Max(0, content - Height);
            }
            else
            {
                vs.Visible = false;
                vs.Value = 0;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

        void ScrollBy(int dy)
        {
            if (!vs.Visible) return;
            int max = vs.Maximum - vs.LargeChange + 1;
            vs.Value = Math.Max(0, Math.Min(max, vs.Value + dy));
        }

        void EnsureVisible(int i)
        {
            if (!vs.Visible || i < 0) return;
            int top = i * RowH;
            if (top < vs.Value) ScrollBy(top - vs.Value);
            else if (top + RowH > vs.Value + Height) ScrollBy(top + RowH - (vs.Value + Height));
        }

        int HitTest(int y)
        {
            int i = (y + Offset) / RowH;
            return i >= 0 && i < rows.Count ? i : -1;
        }

        public void SelectIndex(int i) { Select(i); }

        void Select(int i)
        {
            if (i == selected) return;
            selected = i;
            EnsureVisible(i);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { ScrollBy(-e.Delta / 120 * RowH * 3); base.OnMouseWheel(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Y);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int h = HitTest(e.Y);
            if (h >= 0) Select(h);
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (HitTest(e.Y) >= 0 && RowActivated != null) RowActivated(this, EventArgs.Empty);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys k)
        {
            if (k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End) return true;
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(1, Height / RowH);
            if (rows.Count > 0)
            {
                if (e.KeyCode == Keys.Down) Select(Math.Min(rows.Count - 1, selected + 1));
                else if (e.KeyCode == Keys.Up) Select(Math.Max(0, selected - 1));
                else if (e.KeyCode == Keys.PageDown) Select(Math.Min(rows.Count - 1, selected + page));
                else if (e.KeyCode == Keys.PageUp) Select(Math.Max(0, selected - page));
                else if (e.KeyCode == Keys.Home) Select(0);
                else if (e.KeyCode == Keys.End) Select(rows.Count - 1);
                else if (e.KeyCode == Keys.Enter && selected >= 0 && RowActivated != null) RowActivated(this, EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Card);
            int w = Width - (vs.Visible ? vs.Width : 0);

            if (rows.Count == 0)
            {
                using (Font f = Theme.F(11f))
                    TextRenderer.DrawText(g, EmptyText, f, new Rectangle(0, 0, w, Theme.S(120)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int rh = RowH;
            int first = Offset / rh;
            int last = Math.Min(rows.Count - 1, (Offset + Height) / rh);
            using (Font fName = Theme.F(10.5f, FontStyle.Bold))
            using (Font fSmall = Theme.F(9f))
            using (Font fBadge = Theme.F(8.5f, FontStyle.Bold))
            using (Font fSize = Theme.F(11f, FontStyle.Bold))
            using (Font fChev = Theme.F(16f))
            using (Pen sep = new Pen(Theme.Border))
            {
                for (int i = first; i <= last; i++)
                {
                    Row r = rows[i];
                    int y = i * rh - Offset;
                    Rectangle rr = new Rectangle(0, y, w, rh);

                    if (i == selected) using (SolidBrush b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, rr);
                    else if (i == hover) using (SolidBrush b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, rr);
                    if (i == selected) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, y, Theme.S(4), rh);
                    g.DrawLine(sep, Theme.S(16), y + rh - 1, w - Theme.S(16), y + rh - 1);

                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawIcon(g, r.Kind, new Rectangle(Theme.S(18), y + (rh - Theme.S(30)) / 2, Theme.S(34), Theme.S(30)));

                    int x = Theme.S(66);
                    int rightW = Theme.S(r.Kind == 0 ? 150 : 120);
                    int nameMax = w - x - rightW - Theme.S(110);

                    // ชื่อ
                    Size ns = TextRenderer.MeasureText(g, r.Name, fName, new Size(int.MaxValue, rh), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    int nw = Math.Min(ns.Width, Math.Max(Theme.S(60), nameMax));
                    TextRenderer.DrawText(g, r.Name, fName, new Rectangle(x, y + Theme.S(7), nw, Theme.S(24)), r.Kind == 2 ? Theme.Muted : Theme.Text, Theme.One);

                    // ป้าย
                    if (r.Level != Level.None)
                    {
                        string bt = Safety.Word(r.Level);
                        Size bs = TextRenderer.MeasureText(g, bt, fBadge, new Size(int.MaxValue, rh), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        Rectangle br = new Rectangle(x + nw + Theme.S(8), y + Theme.S(9), bs.Width + Theme.S(14), Theme.S(20));
                        Theme.FillRound(g, Theme.LevelSoft(r.Level), br, Theme.S(10));
                        TextRenderer.DrawText(g, bt, fBadge, br, Theme.LevelText(r.Level), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }

                    // แถบ + %
                    int by = y + Theme.S(36);
                    int bw = Math.Min(Theme.S(220), Math.Max(Theme.S(80), (w - x) / 4));
                    Rectangle track = new Rectangle(x, by, bw, Theme.S(8));
                    Theme.FillRound(g, Theme.Track, track, Theme.S(4));
                    int fw = (int)(bw * Math.Max(0, Math.Min(1, r.Frac)));
                    if (fw > 0) Theme.FillRound(g, Theme.LevelColor(r.Level), new Rectangle(x, by, Math.Max(Theme.S(6), fw), track.Height), Theme.S(4));
                    int px = x + bw + Theme.S(10);
                    TextRenderer.DrawText(g, r.PctText ?? "", fSmall, new Rectangle(px, by - Theme.S(8), Theme.S(60), Theme.S(24)), Theme.Muted, Theme.One);
                    if (!string.IsNullOrEmpty(r.Sub))
                        TextRenderer.DrawText(g, r.Sub, fSmall, new Rectangle(px + Theme.S(62), by - Theme.S(8), Math.Max(0, w - px - Theme.S(62) - rightW), Theme.S(24)), Theme.Muted, Theme.One | TextFormatFlags.PathEllipsis);

                    // ขนาด + ลูกศร
                    int chevW = r.Kind == 0 ? Theme.S(34) : Theme.S(16);
                    TextRenderer.DrawText(g, r.Right, fSize, new Rectangle(w - rightW - chevW, y, rightW, rh), Theme.Text,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    if (r.Kind == 0)
                        TextRenderer.DrawText(g, "›", fChev, new Rectangle(w - chevW, y, chevW - Theme.S(8), rh), Theme.Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        static void DrawIcon(Graphics g, int kind, Rectangle r)
        {
            if (kind == 0)
            {
                // โฟลเดอร์
                Color tab = Color.FromArgb(234, 170, 40), body = Color.FromArgb(251, 191, 36);
                Theme.FillRound(g, tab, new Rectangle(r.X, r.Y, r.Width / 2, r.Height / 2), Theme.S(3));
                Theme.FillRound(g, body, new Rectangle(r.X, r.Y + Theme.S(5), r.Width, r.Height - Theme.S(5)), Theme.S(4));
            }
            else
            {
                Color c = kind == 2 ? Color.FromArgb(203, 213, 225) : kind == 3 ? Color.FromArgb(167, 139, 250) : Color.FromArgb(96, 165, 250);
                Rectangle fr = new Rectangle(r.X + Theme.S(5), r.Y - Theme.S(1), r.Width - Theme.S(10), r.Height + Theme.S(2));
                Theme.FillRound(g, c, fr, Theme.S(4));
                using (Pen p = new Pen(Color.FromArgb(200, Color.White), Math.Max(1f, Theme.Scale * 1.5f)))
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int ly = fr.Y + Theme.S(9) + k * Theme.S(6);
                        g.DrawLine(p, fr.X + Theme.S(5), ly, fr.Right - Theme.S(5) - (k == 2 ? Theme.S(6) : 0), ly);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ หน้าต่างหลัก

    class MainForm : Form
    {
        // หน้าต่างย่อย
        Panel homePane, scanPane, resultPane;

        // home
        FlowLayoutPanel driveFlow;

        // scanning
        Label scanTitle, scanStats, scanPath;
        ProgressBar scanBar;
        Button btnStop;

        // result
        Label resTitle, resSub, noteText;
        Panel notePanel, crumbPanel;
        FlowLayoutPanel crumbFlow;
        Button btnBack, btnViewFolders, btnViewFiles, btnViewTypes;
        ItemList list;
        Label selName, selWarn;
        TextBox selPath;
        Button btnCopy, btnOpen;
        ContextMenuStrip menu;

        Scanner scanner;
        Stopwatch watch;
        long driveUsed;
        string scanDrive;
        readonly string autoScan;
        System.Windows.Forms.Timer timer;

        DirNode current;
        int view; // 0 โฟลเดอร์, 1 ไฟล์, 2 ชนิด
        List<FileEntry> topFiles;
        List<KeyValuePair<string, ExtStat>> extList;
        string selectedPath;

        public MainForm(string autoScan)
        {
            this.autoScan = autoScan;
            AutoScaleMode = AutoScaleMode.None;
            using (Graphics g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            Font = Theme.F(10f);
            Text = "Disk Scan";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.S(1240), (int)(wa.Width * 0.92)), Math.Min(Theme.S(960), (int)(wa.Height * 0.92)));
            MinimumSize = new Size(Theme.S(860), Theme.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            menu = new ContextMenuStrip { Font = Theme.F(10f) };
            menu.Items.Add("คัดลอก Path", null, (s, e) => CopyPath());
            menu.Items.Add("คัดลอกชื่อ", null, (s, e) => { if (selectedPath != null) SetClipboard(Path.GetFileName(selectedPath.TrimEnd('\\'))); });
            menu.Items.Add("เปิดตำแหน่งใน Explorer", null, (s, e) => OpenInExplorer());

            BuildHome();
            BuildScan();
            BuildResult();
            Controls.Add(homePane);
            Controls.Add(scanPane);
            Controls.Add(resultPane);
            Controls.Add(BuildWarningStrip());
            Controls.Add(BuildHeader());

            timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (s, e) => UpdateProgress();

            KeyPreview = true;
            KeyDown += OnFormKeyDown;
            Shown += OnShown;
            ShowPane(homePane);
        }

        static int S(int v) { return Theme.S(v); }

        // ---------------- ส่วนหัว + คำเตือน

        Control BuildHeader()
        {
            TableLayoutPanel h = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = S(72),
                BackColor = Theme.Card,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(S(24), S(10), S(24), S(8))
            };
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            h.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            h.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            h.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            Label t = new Label { Text = "Disk Scan", AutoSize = true, Font = Theme.F(16f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0) };
            Label st = new Label { Text = "ดูว่าอะไรกินพื้นที่ในไดร์ฟ", AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.Muted, Margin = new Padding(S(2), 0, 0, 0) };
            h.Controls.Add(t, 0, 0);
            h.Controls.Add(st, 0, 1);

            Control right;
            if (Native.IsAdmin())
                right = new Label { Text = "✔ โหมด Administrator (สแกนได้ครบ)", AutoSize = true, ForeColor = Theme.LevelText(Level.Info), Font = Theme.F(9.5f, FontStyle.Bold), Anchor = AnchorStyles.Right };
            else
            {
                Button b = Theme.Btn("รันแบบ Admin  (สแกนได้ครบทุกโฟลเดอร์)", false);
                b.Anchor = AnchorStyles.Right;
                b.Click += (s, e) => RestartAsAdmin(null);
                right = b;
            }
            h.Controls.Add(right, 2, 0);
            h.SetRowSpan(right, 2);
            h.Paint += (s, e) => { using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, h.Height - 1, h.Width, h.Height - 1); };
            return h;
        }

        Control BuildWarningStrip()
        {
            Label w = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = S(36),
                BackColor = Theme.LevelSoft(Level.Danger),
                ForeColor = Theme.LevelText(Level.Danger),
                Font = Theme.F(9.5f),
                Padding = new Padding(S(24), 0, S(24), 0),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "⛔  รายการที่ติดป้าย \"ห้ามลบ\" คือไฟล์ระบบของ Windows ห้ามลบเด็ดขาด ไม่งั้นเครื่องอาจเปิดไม่ติด   •   โปรแกรมนี้แค่แสดงผล ไม่ลบไฟล์ใดๆ"
            };
            return w;
        }

        // ---------------- หน้า 1: เลือกไดร์ฟ

        void BuildHome()
        {
            homePane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(32), S(28), S(32), S(16)), AutoScroll = true };

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            Label h1 = new Label { Text = "เลือกไดร์ฟที่ต้องการสแกน", AutoSize = true, Font = Theme.F(15f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, S(4)) };
            Label h2 = new Label
            {
                Text = "คลิกที่ไดร์ฟ โปรแกรมจะสแกนทุกโฟลเดอร์ในไดร์ฟนั้น แล้วบอกว่าอะไรใช้พื้นที่มากที่สุด",
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = new Padding(0, 0, 0, S(20))
            };
            driveFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0), MaximumSize = new Size(S(1100), 0) };

            t.Controls.Add(h1);
            t.Controls.Add(h2);
            t.Controls.Add(driveFlow);

            Button refresh = Theme.Btn("↻  รีเฟรชรายการไดร์ฟ", false);
            refresh.Margin = new Padding(0, S(4), 0, 0);
            refresh.Click += (s, e) => LoadDrives();
            t.Controls.Add(refresh);

            if (!Native.IsAdmin())
            {
                Label tip = new Label
                {
                    Text = "💡 แนะนำ: กดปุ่ม \"รันแบบ Admin\" มุมขวาบนก่อน เพื่อให้สแกนโฟลเดอร์ระบบที่ถูกล็อกไว้ได้ครบทุกโฟลเดอร์",
                    AutoSize = true,
                    ForeColor = Theme.LevelText(Level.Caution),
                    Margin = new Padding(0, S(20), 0, 0)
                };
                t.Controls.Add(tip);
            }
            homePane.Controls.Add(t);
            LoadDrives();
        }

        void LoadDrives()
        {
            driveFlow.SuspendLayout();
            driveFlow.Controls.Clear();
            List<DriveInfo> drives = new List<DriveInfo>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)) drives.Add(d);
                }
                catch { }
            }
            foreach (DriveInfo d in drives)
            {
                try
                {
                    DriveCard c = new DriveCard(d);
                    c.Click += (s, e) => StartScan(((DriveCard)s).Drive);
                    driveFlow.Controls.Add(c);
                }
                catch { }
            }
            driveFlow.ResumeLayout();
        }

        // ---------------- หน้า 2: กำลังสแกน

        void BuildScan()
        {
            scanPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            TableLayoutPanel center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 60));

            TableLayoutPanel card = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                BackColor = Theme.Card,
                Padding = new Padding(S(32), S(28), S(32), S(28)),
                Width = S(640)
            };
            scanTitle = new Label { AutoSize = true, Font = Theme.F(16f, FontStyle.Bold), Text = "กำลังสแกน...", Margin = new Padding(0, 0, 0, S(4)) };
            Label sub = new Label { AutoSize = true, ForeColor = Theme.Muted, Text = "กำลังสแกนทุกโฟลเดอร์ในไดร์ฟ อาจใช้เวลาประมาณ 1-3 นาที", Margin = new Padding(0, 0, 0, S(18)) };
            scanBar = new ProgressBar { Width = S(576), Height = S(14), Maximum = 1000, Margin = new Padding(0, 0, 0, S(14)) };
            scanStats = new Label { AutoSize = true, Font = Theme.F(11f, FontStyle.Bold), Margin = new Padding(0, 0, 0, S(4)) };
            scanPath = new Label { AutoSize = false, Width = S(576), Height = S(24), ForeColor = Theme.Muted, Font = Theme.F(9f), AutoEllipsis = true, Margin = new Padding(0, 0, 0, S(18)) };
            btnStop = Theme.Btn("หยุดสแกน", false);
            btnStop.Click += (s, e) => { if (scanner != null) { scanner.Cancel = true; btnStop.Enabled = false; btnStop.Text = "กำลังหยุด..."; } };

            card.Controls.Add(scanTitle);
            card.Controls.Add(sub);
            card.Controls.Add(scanBar);
            card.Controls.Add(scanStats);
            card.Controls.Add(scanPath);
            card.Controls.Add(btnStop);
            center.Controls.Add(card, 1, 1);
            scanPane.Controls.Add(center);
        }

        // ---------------- หน้า 3: ผลลัพธ์

        void BuildResult()
        {
            resultPane = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(S(24), S(16), S(24), S(16)) };

            // รายการ (Fill) — ใส่ในกรอบสีขาว
            Panel listCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
            list = new ItemList { Dock = DockStyle.Fill, ContextMenuStrip = menu, EmptyText = "ไม่มีรายการ" };
            list.SelectionChanged += (s, e) => OnRowSelected();
            list.RowActivated += (s, e) => OnRowActivated();
            listCard.Controls.Add(list);
            resultPane.Controls.Add(listCard);

            // รายละเอียดที่เลือก (Bottom)
            resultPane.Controls.Add(BuildDetail());

            // breadcrumb (Top)
            crumbPanel = new Panel { Dock = DockStyle.Top, Height = S(42), BackColor = Theme.Bg };
            btnBack = Theme.Btn("←  ย้อนกลับ", false);
            btnBack.Location = new Point(0, S(4));
            btnBack.Click += (s, e) => { if (current != null && current.Parent != null) Navigate(current.Parent, current); };
            crumbFlow = new FlowLayoutPanel { Location = new Point(S(130), S(4)), Height = S(36), AutoSize = false, WrapContents = false, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            crumbPanel.Controls.Add(btnBack);
            crumbPanel.Controls.Add(crumbFlow);
            crumbPanel.Resize += (s, e) =>
            {
                crumbFlow.Left = btnBack.Right + S(12);
                crumbFlow.Width = Math.Max(0, crumbPanel.Width - crumbFlow.Left);
            };
            resultPane.Controls.Add(crumbPanel);

            // ปุ่มเลือกมุมมอง (Top)
            FlowLayoutPanel views = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0), Padding = new Padding(0, S(6), 0, S(6)) };
            btnViewFolders = Theme.Btn("📁  โฟลเดอร์", false);
            btnViewFiles = Theme.Btn("📄  ไฟล์ใหญ่ที่สุด", false);
            btnViewTypes = Theme.Btn("🗂  แยกตามชนิดไฟล์", false);
            btnViewFolders.Click += (s, e) => SetView(0);
            btnViewFiles.Click += (s, e) => SetView(1);
            btnViewTypes.Click += (s, e) => SetView(2);
            foreach (Button b in new Button[] { btnViewFolders, btnViewFiles, btnViewTypes })
            {
                b.Margin = new Padding(0, 0, S(8), 0);
                views.Controls.Add(b);
            }
            resultPane.Controls.Add(views);

            // แจ้งเตือนสแกนไม่ครบ (Top)
            notePanel = new Panel { Dock = DockStyle.Top, Height = S(48), BackColor = Theme.LevelSoft(Level.Caution), Padding = new Padding(S(14), 0, S(10), 0), Visible = false };
            noteText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.LevelText(Level.Caution), AutoEllipsis = true };
            Button noteBtn = Theme.Btn("รันแบบ Admin แล้วสแกนใหม่", true);
            noteBtn.Dock = DockStyle.Right;
            noteBtn.Click += (s, e) => RestartAsAdmin(scanDrive);
            Panel noteBtnWrap = new Panel { Dock = DockStyle.Right, Width = S(250), Padding = new Padding(0, S(8), 0, S(8)) };
            noteBtnWrap.Controls.Add(noteBtn);
            notePanel.Controls.Add(noteText);
            notePanel.Controls.Add(noteBtnWrap);
            resultPane.Controls.Add(notePanel);
            Panel noteGap = new Panel { Dock = DockStyle.Top, Height = S(4), BackColor = Theme.Bg };

            // สรุป (Top)
            TableLayoutPanel sum = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 0, 0, S(10)) };
            sum.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sum.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            resTitle = new Label { AutoSize = true, Font = Theme.F(15f, FontStyle.Bold), Margin = new Padding(0) };
            resSub = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, S(2), 0, 0) };
            Button again = Theme.Btn("←  เลือกไดร์ฟอื่น / สแกนใหม่", false);
            again.Anchor = AnchorStyles.Right;
            again.Click += (s, e) => { LoadDrives(); ShowPane(homePane); };
            sum.Controls.Add(resTitle, 0, 0);
            sum.Controls.Add(resSub, 0, 1);
            sum.Controls.Add(again, 1, 0);
            sum.SetRowSpan(again, 2);
            resultPane.Controls.Add(noteGap);
            resultPane.Controls.Add(sum);
        }

        Control BuildDetail()
        {
            Panel wrap = new Panel { Dock = DockStyle.Bottom, Height = S(150), Padding = new Padding(0, S(10), 0, 0), BackColor = Theme.Bg };
            TableLayoutPanel d = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = Theme.Card,
                Padding = new Padding(S(18), S(8), S(18), S(10))
            };
            d.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            d.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            d.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            d.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
            d.RowStyles.Add(new RowStyle(SizeType.Absolute, S(42)));
            d.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            selName = new Label { Dock = DockStyle.Fill, Font = Theme.F(11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) };
            selPath = new TextBox { ReadOnly = true, BackColor = Theme.Bg, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11f), Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, S(8), 0) };
            btnCopy = Theme.Btn("📋  คัดลอก Path", true);
            btnCopy.Anchor = AnchorStyles.None;
            btnCopy.Margin = new Padding(0, 0, S(8), 0);
            btnCopy.Click += (s, e) => CopyPath();
            btnOpen = Theme.Btn("เปิดใน Explorer", false);
            btnOpen.Anchor = AnchorStyles.None;
            btnOpen.Margin = new Padding(0);
            btnOpen.Click += (s, e) => OpenInExplorer();
            selWarn = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(S(12), 0, S(12), 0), Margin = new Padding(0, S(8), 0, 0), AutoEllipsis = true };

            d.Controls.Add(selName, 0, 0);
            d.SetColumnSpan(selName, 3);
            d.Controls.Add(selPath, 0, 1);
            d.Controls.Add(btnCopy, 1, 1);
            d.Controls.Add(btnOpen, 2, 1);
            d.Controls.Add(selWarn, 0, 2);
            d.SetColumnSpan(selWarn, 3);
            wrap.Controls.Add(d);
            return wrap;
        }

        void ShowPane(Panel p)
        {
            homePane.Visible = p == homePane;
            scanPane.Visible = p == scanPane;
            resultPane.Visible = p == resultPane;
        }

        // ---------------- สแกน

        void OnShown(object sender, EventArgs e)
        {
            if (autoScan == null) return;
            foreach (DriveInfo d in DriveInfo.GetDrives())
                if (string.Equals(d.Name, autoScan, StringComparison.OrdinalIgnoreCase)) { StartScan(d); return; }
        }

        void RestartAsAdmin(string drive)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = drive != null ? "--scan \"" + drive.TrimEnd('\\') + "\\\\\"" : ""
                };
                Process.Start(psi);
                Application.Exit();
            }
            catch (Win32Exception) { }
        }

        void StartScan(DriveInfo d)
        {
            if (scanner != null && timer.Enabled) return;
            scanDrive = d.Name;
            try { driveUsed = d.TotalSize - d.TotalFreeSpace; } catch { driveUsed = 0; }

            scanner = new Scanner();
            watch = Stopwatch.StartNew();
            scanTitle.Text = "กำลังสแกนไดร์ฟ " + scanDrive;
            scanBar.Value = 0;
            scanStats.Text = "";
            scanPath.Text = "";
            btnStop.Enabled = true;
            btnStop.Text = "หยุดสแกน";
            ShowPane(scanPane);
            timer.Start();

            Scanner sc = scanner;
            string drive = scanDrive;
            Task.Factory.StartNew(() => sc.Run(drive), TaskCreationOptions.LongRunning)
                .ContinueWith(t => BeginInvoke(new Action(() => ScanFinished(sc, t.Exception))));
        }

        void UpdateProgress()
        {
            if (scanner == null) return;
            long bytes = Interlocked.Read(ref scanner.BytesScanned);
            if (driveUsed > 0) scanBar.Value = (int)Math.Min(1000, bytes * 1000 / driveUsed);
            scanStats.Text = string.Format("{0}   •   {1:N0} ไฟล์   •   {2:N0} โฟลเดอร์   •   {3:mm\\:ss}",
                Theme.Fmt(bytes), Interlocked.Read(ref scanner.FilesScanned), Interlocked.Read(ref scanner.DirsScanned), watch.Elapsed);
            scanPath.Text = scanner.CurrentPath ?? "";
        }

        void ScanFinished(Scanner sc, Exception ex)
        {
            timer.Stop();
            watch.Stop();
            if (ex != null)
            {
                MessageBox.Show(this, "เกิดข้อผิดพลาดระหว่างสแกน:\n" + ex.GetBaseException().Message, "Disk Scan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPane(homePane);
                return;
            }

            DirNode root = sc.Root;
            resTitle.Text = string.Format("ไดร์ฟ {0}  ใช้พื้นที่ {1}", root.Name, Theme.Fmt(root.Size));
            resSub.Text = string.Format("{0}พบ {1:N0} ไฟล์ ใน {2:N0} โฟลเดอร์   •   ใช้เวลา {3:mm\\:ss}   •   ดับเบิลคลิกที่โฟลเดอร์เพื่อดูข้างใน",
                sc.Cancel ? "(หยุดกลางคัน — ผลไม่ครบ)   " : "", sc.FilesScanned, sc.DirsScanned, watch.Elapsed);

            notePanel.Visible = sc.Errors > 0 && !Native.IsAdmin();
            noteText.Text = string.Format("⚠  มี {0:N0} โฟลเดอร์ที่ไม่มีสิทธิ์เข้าถึง (สแกนได้ {1} จากที่ใช้จริง {2}) — รันแบบ Admin เพื่อให้สแกนได้ครบ",
                sc.Errors, Theme.Fmt(root.Size), Theme.Fmt(driveUsed));

            topFiles = sc.Top.ToSortedList();
            extList = new List<KeyValuePair<string, ExtStat>>(sc.Exts);
            extList.Sort((a, b) => b.Value.Size.CompareTo(a.Value.Size));
            if (extList.Count > 300) extList.RemoveRange(300, extList.Count - 300);

            current = root;
            ShowPane(resultPane);
            SetView(0);
            list.Focus();
        }

        long RootSize { get { return scanner != null && scanner.Root != null ? scanner.Root.Size : 0; } }

        // ---------------- มุมมอง

        void SetView(int v)
        {
            view = v;
            Button[] bs = { btnViewFolders, btnViewFiles, btnViewTypes };
            for (int i = 0; i < bs.Length; i++)
            {
                bool on = i == v;
                bs[i].BackColor = on ? Theme.Accent : Theme.Card;
                bs[i].ForeColor = on ? Color.White : Theme.Text;
                bs[i].FlatAppearance.BorderSize = on ? 0 : 1;
                bs[i].FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.Hover;
                bs[i].Font = Theme.F(10f, on ? FontStyle.Bold : FontStyle.Regular);
            }
            crumbPanel.Visible = v == 0;

            if (v == 0) Navigate(current ?? scanner.Root, null);
            else if (v == 1) ShowFiles();
            else ShowTypes();
        }

        void Navigate(DirNode d, DirNode selectChild)
        {
            current = d;
            long total = RootSize;
            long baseSize = Math.Max(1, d.Size);
            List<Row> rows = new List<Row>();

            if (d.Children != null)
                foreach (DirNode c in d.Children)
                {
                    Safety sf = Safety.Classify(c.FullPath, true, 0);
                    rows.Add(new Row
                    {
                        Kind = 0, Tag = c, Name = c.Name, Level = sf.Level,
                        Right = Theme.Fmt(c.Size), Frac = c.Size / (double)baseSize, PctText = Theme.Pct(c.Size, baseSize),
                        Sub = c.AccessDenied ? "ไม่มีสิทธิ์เข้าถึง" : string.Format("{0:N0} ไฟล์", c.TotalFiles)
                    });
                }
            long bigSum = 0;
            if (d.BigFiles != null)
                foreach (FileEntry f in d.BigFiles)
                {
                    bigSum += f.Size;
                    Safety sf = Safety.Classify(f.FullPath, false, f.Attributes);
                    rows.Add(new Row
                    {
                        Kind = 1, Tag = f, Name = f.Name, Level = sf.Level,
                        Right = Theme.Fmt(f.Size), Frac = f.Size / (double)baseSize, PctText = Theme.Pct(f.Size, baseSize),
                        Sub = f.Modified == DateTime.MinValue ? "" : "แก้ไขล่าสุด " + f.Modified.ToString("d MMM yyyy")
                    });
                }
            int other = d.FileCount - (d.BigFiles != null ? d.BigFiles.Count : 0);
            if (other > 0)
            {
                long os = d.FilesSize - bigSum;
                rows.Add(new Row
                {
                    Kind = 2, Tag = new OtherFilesTag { Dir = d, Size = os, Count = other },
                    Name = string.Format("ไฟล์เล็กๆ อื่นๆ ในโฟลเดอร์นี้ ({0:N0} ไฟล์)", other),
                    Right = Theme.Fmt(os), Frac = os / (double)baseSize, PctText = Theme.Pct(os, baseSize), Sub = "ไฟล์ละไม่ถึง 10 MB"
                });
            }
            rows.Sort((a, b) => b.Frac.CompareTo(a.Frac));
            list.EmptyText = d.AccessDenied ? "ไม่มีสิทธิ์เข้าถึงโฟลเดอร์นี้ — ลองรันแบบ Admin" : "โฟลเดอร์ว่าง";
            list.SetRows(rows);

            // breadcrumb
            crumbFlow.SuspendLayout();
            crumbFlow.Controls.Clear();
            List<DirNode> chain = new List<DirNode>();
            for (DirNode p = d; p != null; p = p.Parent) chain.Insert(0, p);
            for (int i = 0; i < chain.Count; i++)
            {
                DirNode target = chain[i];
                bool last = i == chain.Count - 1;
                LinkLabel l = new LinkLabel
                {
                    Text = target.Parent == null ? target.Name.TrimEnd('\\') : target.Name,
                    AutoSize = true,
                    LinkColor = last ? Theme.Text : Theme.Accent,
                    ActiveLinkColor = Theme.Accent,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    Font = Theme.F(10.5f, last ? FontStyle.Bold : FontStyle.Regular),
                    Margin = new Padding(0, S(7), 0, 0)
                };
                l.LinkClicked += (s, e) => Navigate(target, null);
                crumbFlow.Controls.Add(l);
                if (!last) crumbFlow.Controls.Add(new Label { Text = "›", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.F(11f), Margin = new Padding(S(2), S(5), S(2), 0) });
            }
            crumbFlow.ResumeLayout();
            btnBack.Enabled = d.Parent != null;

            ShowDetail(d);
            if (selectChild != null)
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Tag == selectChild) { SelectIndex(i); break; }
        }

        void SelectIndex(int i)
        {
            list.Focus();
            list.SelectIndex(i);
        }

        void ShowFiles()
        {
            long total = RootSize;
            long max = topFiles.Count > 0 ? Math.Max(1, topFiles[0].Size) : 1;
            List<Row> rows = new List<Row>();
            foreach (FileEntry f in topFiles)
            {
                string full = f.FullPath;
                Safety sf = Safety.Classify(full, false, f.Attributes);
                rows.Add(new Row
                {
                    Kind = 1, Tag = f, Name = f.Name, Level = sf.Level, Right = Theme.Fmt(f.Size),
                    Frac = f.Size / (double)max, PctText = Theme.Pct(f.Size, total), Sub = f.Dir.FullPath
                });
            }
            list.EmptyText = "ไม่มีไฟล์";
            list.SetRows(rows);
            ShowDetail(null);
        }

        void ShowTypes()
        {
            long total = RootSize;
            long max = extList.Count > 0 ? Math.Max(1, extList[0].Value.Size) : 1;
            List<Row> rows = new List<Row>();
            foreach (KeyValuePair<string, ExtStat> kv in extList)
            {
                rows.Add(new Row
                {
                    Kind = 3, Tag = null, Name = kv.Key, Level = Level.None, Right = Theme.Fmt(kv.Value.Size),
                    Frac = kv.Value.Size / (double)max, PctText = Theme.Pct(kv.Value.Size, total),
                    Sub = string.Format("{0:N0} ไฟล์", kv.Value.Count)
                });
            }
            list.EmptyText = "ไม่มีข้อมูล";
            list.SetRows(rows);
            ShowDetail(null);
        }

        void OnRowSelected()
        {
            Row r = list.SelectedRow;
            if (r == null) return;
            if (r.Kind == 3)
            {
                selectedPath = null;
                selName.Text = string.Format("ไฟล์นามสกุล {0}   •   {1}   •   {2}", r.Name, r.Right, r.Sub);
                selPath.Text = "";
                btnCopy.Enabled = btnOpen.Enabled = false;
                SetWarn(Level.None, "ℹ  ดูไฟล์ใหญ่ๆ ของชนิดนี้ได้ที่แท็บ \"ไฟล์ใหญ่ที่สุด\"");
                return;
            }
            ShowDetail(r.Tag);
        }

        void OnRowActivated()
        {
            Row r = list.SelectedRow;
            if (r == null) return;
            DirNode d = r.Tag as DirNode;
            if (d != null) { Navigate(d, null); return; }
            if (r.Tag is FileEntry) OpenInExplorer();
        }

        void ShowDetail(object tag)
        {
            DirNode d = tag as DirNode;
            FileEntry f = tag as FileEntry;
            OtherFilesTag ot = tag as OtherFilesTag;
            long total = RootSize;
            Safety sf;

            if (d != null)
            {
                selectedPath = d.FullPath;
                sf = Safety.Classify(selectedPath, true, 0);
                selName.Text = string.Format("📁  {0}   •   {1}   •   {2} ของทั้งไดร์ฟ   •   {3:N0} ไฟล์",
                    d.Parent == null ? d.Name : d.Name, Theme.Fmt(d.Size), Theme.Pct(d.Size, total), d.TotalFiles);
            }
            else if (f != null)
            {
                selectedPath = f.FullPath;
                sf = Safety.Classify(selectedPath, false, f.Attributes);
                selName.Text = string.Format("📄  {0}   •   {1}   •   {2} ของทั้งไดร์ฟ", f.Name, Theme.Fmt(f.Size), Theme.Pct(f.Size, total));
            }
            else if (ot != null)
            {
                selectedPath = ot.Dir.FullPath;
                sf = Safety.Classify(selectedPath, true, 0);
                selName.Text = string.Format("ไฟล์เล็กๆ {0:N0} ไฟล์ในโฟลเดอร์นี้   •   รวม {1}", ot.Count, Theme.Fmt(ot.Size));
            }
            else
            {
                selectedPath = null;
                selName.Text = "คลิกที่รายการเพื่อดูรายละเอียดและคัดลอก Path";
                selPath.Text = "";
                btnCopy.Enabled = btnOpen.Enabled = false;
                SetWarn(Level.None, "");
                return;
            }

            selPath.Text = selectedPath;
            btnCopy.Enabled = btnOpen.Enabled = true;
            btnCopy.Text = "📋  คัดลอก Path";
            string icon = sf.Level == Level.Danger ? "⛔  " : sf.Level == Level.Caution ? "⚠  " : sf.Level == Level.Info ? "✅  " : "ℹ  ";
            SetWarn(sf.Level, icon + sf.Message);
        }

        void SetWarn(Level l, string text)
        {
            selWarn.Text = text;
            selWarn.BackColor = text == "" ? Theme.Card : Theme.LevelSoft(l);
            selWarn.ForeColor = Theme.LevelText(l);
            selWarn.Font = Theme.F(10f, l == Level.Danger ? FontStyle.Bold : FontStyle.Regular);
        }

        // ---------------- actions

        void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (!resultPane.Visible) return;
            if (e.Control && e.KeyCode == Keys.C && !selPath.Focused && selectedPath != null) { CopyPath(); e.Handled = true; }
            else if (e.KeyCode == Keys.Back && view == 0 && current != null && current.Parent != null && !selPath.Focused) { Navigate(current.Parent, current); e.Handled = true; }
        }

        static void SetClipboard(string s)
        {
            try { Clipboard.SetText(s); } catch { }
        }

        void CopyPath()
        {
            if (selectedPath == null) return;
            SetClipboard(selectedPath);
            btnCopy.Text = "✔  คัดลอกแล้ว";
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!btnCopy.IsDisposed) btnCopy.Text = "📋  คัดลอก Path"; };
            t.Start();
        }

        void OpenInExplorer()
        {
            if (selectedPath == null) return;
            try
            {
                if (Directory.Exists(selectedPath))
                    Process.Start("explorer.exe", "\"" + selectedPath + "\"");
                else
                    Process.Start("explorer.exe", "/select,\"" + selectedPath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "เปิดไม่ได้: " + ex.Message, "Disk Scan");
            }
        }
    }
}
