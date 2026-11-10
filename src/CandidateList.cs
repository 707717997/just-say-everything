using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SayAllVoice
{
    /// <summary>
    /// Custom drawn candidate list. A plain ListBox paints its own background only
    /// when it has items, which left stale pixels on the layered palette window,
    /// so the list is drawn here instead.
    /// </summary>
    public class CandidateList : Control
    {
        private List<Match> _items = new List<Match>();
        private int _sel = -1;
        private int _hover = -1;
        private string _empty = "";

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;

        public int RowHeight = 30;

        public CandidateList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
        }

        public List<Match> Items { get { return _items; } }

        public Match Selected
        {
            get { return (_sel >= 0 && _sel < _items.Count) ? _items[_sel] : null; }
        }

        public int SelectedIndex
        {
            get { return _sel; }
            set
            {
                if (value == _sel) return;
                _sel = value;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public void SetEmptyText(string text)
        {
            _empty = text ?? "";
            Invalidate();
        }

        public void SetItems(List<Match> items)
        {
            _items = items ?? new List<Match>();
            _sel = _items.Count > 0 ? 0 : -1;
            _hover = -1;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void MoveSelection(int delta)
        {
            if (_items.Count == 0) return;
            int i = _sel < 0 ? 0 : _sel + delta;
            if (i < 0) i = _items.Count - 1;
            if (i >= _items.Count) i = 0;
            SelectedIndex = i;
        }

        private int HitTest(Point p)
        {
            if (RowHeight <= 0) return -1;
            int i = p.Y / RowHeight;
            return (i >= 0 && i < _items.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i != _hover) { _hover = i; Invalidate(); }
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = HitTest(e.Location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = HitTest(e.Location);
            if (i >= 0)
            {
                SelectedIndex = i;
                if (ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);

            if (_items.Count == 0)
            {
                if (!string.IsNullOrEmpty(_empty))
                {
                    TextRenderer.DrawText(g, _empty, Font, ClientRectangle, Theme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                return;
            }

            int rh = RowHeight;
            for (int i = 0; i < _items.Count; i++)
            {
                Match m = _items[i];
                Rectangle row = new Rectangle(0, i * rh, Width, rh);
                if (row.Bottom > Height) break;

                bool sel = (i == _sel);
                bool hot = (i == _hover);
                if (sel || hot)
                {
                    Rectangle r = new Rectangle(row.Left + 2, row.Top + 2, row.Width - 4, row.Height - 4);
                    using (GraphicsPath p = Round(r, 7))
                    using (SolidBrush b = new SolidBrush(sel ? Theme.BgSel : Color.FromArgb(34, 36, 42)))
                        g.FillPath(b, p);
                    if (sel)
                    {
                        using (SolidBrush ab = new SolidBrush(Theme.Accent))
                            g.FillRectangle(ab, new Rectangle(r.Left, r.Top + 8, 3, r.Height - 16));
                    }
                }

                string num = (i + 1).ToString();
                TextRenderer.DrawText(g, num, Font, new Rectangle(row.Left + 12, row.Top, 20, rh),
                    sel ? Theme.Accent : Theme.Dim, TextFormatFlags.VerticalCenter);

                string cat = m.Cmd.Category ?? "";
                int catW = 0;
                if (cat.Length > 0)
                {
                    Size cs = TextRenderer.MeasureText(g, cat, Font);
                    catW = cs.Width + 10;
                    TextRenderer.DrawText(g, cat, Font, new Rectangle(row.Right - catW - 10, row.Top, catW, rh),
                        Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }

                int nameX = row.Left + 40;
                int nameW = row.Width - nameX - catW - 16;
                TextRenderer.DrawText(g, m.Cmd.Name, Font, new Rectangle(nameX, row.Top, nameW, rh),
                    sel ? Theme.Fg : Color.FromArgb(214, 219, 226),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}

