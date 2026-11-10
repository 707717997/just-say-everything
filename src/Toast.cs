using System;
using System.Drawing;
using System.Windows.Forms;

namespace SayAllVoice
{
    /// <summary>屏幕下方的小提示条。不抢焦点，指令刚执行完不能把键盘焦点拿走。</summary>
    public class Toast : Form
    {
        private readonly Label _label;
        private readonly Timer _timer;
        private readonly float S;

        public Toast()
        {
            S = Math.Max(1.0f, Native.DpiScalePercent() / 100.0f);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            _label = new Label();
            _label.Dock = DockStyle.Fill;
            _label.TextAlign = ContentAlignment.MiddleCenter;
            _label.BackColor = Theme.Bg;
            _label.Font = new Font("Microsoft YaHei UI", 12f * S, FontStyle.Bold);
            Controls.Add(_label);
            _timer = new Timer();
            _timer.Tick += delegate { _timer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;                      // CS_DROPSHADOW
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | 0x08000000 | 0x00000020;   // NOACTIVATE | TRANSPARENT
                return cp;
            }
        }

        /// <summary>tone：1 成功，2 失败或提醒，0 普通回答。</summary>
        public void Flash(string text, int tone, int ms)
        {
            if (text.Length > 90) text = text.Substring(0, 90) + "…";
            _label.Text = text;
            _label.ForeColor = tone == 1 ? Theme.Good : (tone == 2 ? Theme.Bad : Theme.Fg);
            Size need = TextRenderer.MeasureText(text, _label.Font);
            int w = Math.Min(need.Width + (int)(48 * S), (int)(900 * S));
            int h = need.Height + (int)(22 * S);
            Native.RECT wa = Native.WorkAreaForCursor();
            SetBounds(wa.Left + (wa.Right - wa.Left - w) / 2, wa.Bottom - h - (int)(70 * S), w, h);
            if (!Visible) Show();
            _timer.Stop();
            _timer.Interval = ms;
            _timer.Start();
        }
    }
}
