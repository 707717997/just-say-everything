using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SayAllVoice
{
    /// <summary>
    /// The floating "voice command" bar. It owns a standard TextBox on purpose:
    /// WeType (and any IME) can compose into it, which is how the spoken text arrives.
    /// </summary>
    public class PaletteForm : Form
    {
        private readonly float S;
        private TextBox _text;
        private Label _status;
        private Label _footer;
        private CandidateList _list;
        private List<Match> _matches = new List<Match>();

        public event EventHandler SubmitRequested;
        public event EventHandler CancelRequested;
        public event EventHandler LibraryRequested;
        public event EventHandler SelectionChanged;
        public event EventHandler Typed;

        public PaletteForm()
        {
            int pct = Native.DpiScalePercent();
            S = Math.Max(1.0f, pct / 100.0f);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "SayAll 语音指令";
            ClientSize = new Size(I(760), I(322));

            _status = new Label();
            _status.AutoSize = false;
            _status.BackColor = Theme.Bg;
            _status.ForeColor = Theme.Accent;
            _status.Font = new Font("Microsoft YaHei UI", 10.5f * S, FontStyle.Bold);
            _status.Text = "正在聆听…";
            _status.Bounds = new Rectangle(I(18), I(12), I(520), I(26));
            Controls.Add(_status);

            Label hint = new Label();
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleRight;
            hint.BackColor = Theme.Bg;
            hint.ForeColor = Theme.Dim;
            hint.Font = new Font("Microsoft YaHei UI", 9f * S);
            hint.Text = "Esc 取消";
            hint.Bounds = new Rectangle(I(560), I(12), I(182), I(26));
            Controls.Add(hint);

            _text = new TextBox();
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = Theme.BgSoft;
            _text.ForeColor = Theme.Fg;
            _text.Font = new Font("Microsoft YaHei UI", 20f * S);
            _text.Bounds = new Rectangle(I(30), I(58), I(700), I(40));
            _text.Multiline = false;
            _text.TextChanged += OnTextChanged;
            _text.KeyDown += OnTextKeyDown;
            Controls.Add(_text);

            _list = new CandidateList();
            _list.Font = new Font("Microsoft YaHei UI", 11.5f * S);
            _list.RowHeight = I(32);
            _list.Bounds = new Rectangle(I(18), I(112), I(724), I(168));
            _list.TabStop = false;
            _list.SelectionChanged += delegate { if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); };
            _list.ItemActivated += delegate { if (SubmitRequested != null) SubmitRequested(this, EventArgs.Empty); };
            Controls.Add(_list);

            _footer = new Label();
            _footer.AutoSize = false;
            _footer.BackColor = Theme.Bg;
            _footer.ForeColor = Theme.Dim;
            _footer.Font = new Font("Microsoft YaHei UI", 9f * S);
            _footer.Text = "回车执行 · ↑↓ 选择 · F1 指令库";
            _footer.Bounds = new Rectangle(I(18), I(292), I(724), I(22));
            Controls.Add(_footer);

            Panel accent = new Panel();
            accent.BackColor = Theme.Accent;
            accent.Bounds = new Rectangle(0, 0, I(4), I(322));
            Controls.Add(accent);
        }

        private int I(float v)
        {
            return (int)Math.Round(v * S);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int r = I(14);
            IntPtr h = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r, r);
            if (h != IntPtr.Zero) Native.SetWindowRgn(Handle, h, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle box = new Rectangle(I(18), I(44), I(724), I(54));
            using (GraphicsPath p = Round(box, I(10)))
            {
                using (SolidBrush b = new SolidBrush(Theme.BgSoft)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(0, radius) * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public string TextValue
        {
            get { return _text.Text; }
            set { _text.Text = value; }
        }

        public List<Match> Matches
        {
            get { return _matches; }
        }

        public Match Selected
        {
            get { return _list.Selected; }
        }

        public void SetStatus(string text, int tone)
        {
            _status.Text = text;
            _status.ForeColor = tone == 1 ? Theme.Good : (tone == 2 ? Theme.Bad : Theme.Accent);
        }

        public void SetFooter(string text)
        {
            _footer.Text = text;
        }

        public void SetEmptyText(string text)
        {
            _list.SetEmptyText(text);
        }

        public void SetCandidates(List<Match> matches)
        {
            _matches = matches ?? new List<Match>();
            _list.SetItems(_matches);
        }

        public void SelectOffset(int delta)
        {
            _list.MoveSelection(delta);
        }

        public void FocusInput()
        {
            try
            {
                Activate();
                _text.Focus();
                _text.SelectAll();
            }
            catch { }
        }

        public void ResetForSession()
        {
            _text.Text = "";
            _matches = new List<Match>();
            _list.SetItems(_matches);
            SetStatus("正在聆听…", 0);
            SetFooter("回车执行 · ↑↓ 选择 · F1 指令库");
        }

        public void PlaceOnScreen()
        {
            Native.RECT wa = Native.WorkAreaForCursor();
            int x = wa.Left + (wa.Right - wa.Left - Width) / 2;
            int y = wa.Top + I(90);
            Location = new Point(x, y);
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (Typed != null) Typed(this, EventArgs.Empty);
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private void OnTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (SubmitRequested != null) SubmitRequested(this, EventArgs.Empty);
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
                return;
            }
            if (e.KeyCode == Keys.Up) { SelectOffset(-1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Down) { SelectOffset(1); e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.F1) { e.SuppressKeyPress = true; if (LibraryRequested != null) LibraryRequested(this, EventArgs.Empty); return; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { if (CancelRequested != null) CancelRequested(this, EventArgs.Empty); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
