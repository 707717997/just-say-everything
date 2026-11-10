using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SayAllVoice
{
    /// <summary>The command library: browse, search, create and edit voice commands.</summary>
    internal sealed class LibraryForm : Form
    {
        public CommandStore Store;
        private readonly AppIndex _apps;
        private readonly float _k;

        private ListView _list;
        private TextBox _search;
        private ComboBox _catFilter;
        private TextBox _name, _aliases, _hint, _param;
        private ComboBox _cat, _action;
        private CheckBox _enabled;
        private TextBox _json;
        private Label _status, _paramLabel, _readonly;
        private Button _save, _delete, _clone, _new;

        private Command _current;
        private bool _loading;

        private static readonly string[] ActionValues = { "open_app", "url", "keys", "text", "system", "wait", "sequence" };
        private static readonly string[] ActionNames = { "打开应用", "打开网址", "发送按键", "输入文字", "系统动作", "等待", "组合步骤" };
        private static readonly string[] Categories = { "应用", "系统", "编辑", "窗口", "网页", "自定义" };

        public LibraryForm(CommandStore store, AppIndex apps)
        {
            Store = store;
            _apps = apps;
            _k = Native.DpiScalePercent() / 100f;

            SuspendLayout();
            Text = "SayAll 语音指令库";
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 10f);
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(P(1180), P(760));
            MinimumSize = new Size(P(960), P(640));
            KeyPreview = true;

            BuildEditor();
            BuildBody();
            BuildHeader();
            ResumeLayout(false);

            Load += delegate { FillList(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private int P(float v) { return (int)Math.Round(v * _k); }
        private Font F(float size) { return new Font("Microsoft YaHei UI", size); }
        private Font F(float size, bool bold) { return new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }

        // ------------------------------------------------------------------ layout

        private void BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = P(74);
            header.Padding = new Padding(P(22), P(20), P(22), 0);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Right;
            right.FlowDirection = FlowDirection.LeftToRight;
            right.WrapContents = false;
            right.AutoSize = true;
            right.Padding = new Padding(0, P(2), 0, 0);

            Button btnFolder = SmallButton("配置文件夹");
            btnFolder.Click += delegate { OpenDataFolder(); };
            Button btnIndex = SmallButton("重建应用索引");
            btnIndex.Click += delegate { RebuildIndex(); };
            _new = SmallButton("+ 新建指令");
            _new.ForeColor = Theme.Accent;
            _new.Click += delegate { NewCommand(); };
            right.Controls.Add(btnFolder);
            right.Controls.Add(btnIndex);
            right.Controls.Add(_new);

            Label title = new Label();
            title.Text = "指令库";
            title.Font = F(15f, true);
            title.ForeColor = Theme.Fg;
            title.AutoSize = true;
            title.Dock = DockStyle.Left;
            title.Padding = new Padding(0, P(4), P(20), 0);

            Panel searchWrap = new Panel();
            searchWrap.Dock = DockStyle.Fill;
            searchWrap.Padding = new Padding(0, P(6), P(14), 0);

            _search = new TextBox();
            _search.Dock = DockStyle.Fill;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.BackColor = Theme.BgSoft;
            _search.ForeColor = Theme.Dim;
            _search.Font = F(11f);
            _search.Text = "搜索指令…";
            _search.GotFocus += delegate
            {
                if (_search.Text == "搜索指令…") { _search.Text = ""; _search.ForeColor = Theme.Fg; }
            };
            _search.LostFocus += delegate
            {
                if (_search.Text.Trim().Length == 0) { _search.Text = "搜索指令…"; _search.ForeColor = Theme.Dim; }
            };
            _search.TextChanged += delegate { FillList(); };
            searchWrap.Controls.Add(_search);

            header.Controls.Add(searchWrap);
            header.Controls.Add(title);
            header.Controls.Add(right);
            Controls.Add(header);

            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 1;
            line.BackColor = Theme.Line;
            Controls.Add(line);
        }

        private void BuildBody()
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = P(470);
            left.Padding = new Padding(P(18), P(14), P(8), P(14));

            Panel filter = new Panel();
            filter.Dock = DockStyle.Top;
            filter.Height = P(42);

            _catFilter = new ComboBox();
            _catFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _catFilter.FlatStyle = FlatStyle.Flat;
            _catFilter.BackColor = Theme.BgSoft;
            _catFilter.ForeColor = Theme.Fg;
            _catFilter.Font = F(10f);
            _catFilter.Width = P(150);
            _catFilter.Items.Add("全部指令");
            for (int i = 0; i < Categories.Length; i++) _catFilter.Items.Add(Categories[i]);
            _catFilter.SelectedIndex = 0;
            _catFilter.SelectedIndexChanged += delegate { FillList(); };
            _catFilter.Location = new Point(0, P(3));
            filter.Controls.Add(_catFilter);

            Label hint = new Label();
            hint.Text = "选中后可编辑";
            hint.Font = F(9f);
            hint.ForeColor = Theme.Dim;
            hint.AutoSize = true;
            hint.Location = new Point(P(162), P(8));
            filter.Controls.Add(hint);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Theme.Bg;
            _list.ForeColor = Theme.Fg;
            _list.Font = F(10.5f);
            _list.OwnerDraw = true;
            _list.Columns.Add("指令", P(240), HorizontalAlignment.Left);
            _list.Columns.Add("分类", P(76), HorizontalAlignment.Left);
            _list.Columns.Add("动作", P(112), HorizontalAlignment.Left);
            _list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg))
                    e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, F(9.5f, true), e.Bounds,
                    Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            _list.DrawItem += delegate(object s, DrawListViewItemEventArgs e)
            {
                bool sel = (e.State & ListViewItemStates.Selected) != 0;
                using (SolidBrush b = new SolidBrush(sel ? Theme.BgSel : Theme.Bg))
                    e.Graphics.FillRectangle(b, e.Bounds);
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.Left, e.Bounds.Top + P(4), P(3), e.Bounds.Height - P(8)));
                }
            };
            _list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                Rectangle r = new Rectangle(e.Bounds.Left + (e.ColumnIndex == 0 ? P(12) : 0), e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, F(e.ColumnIndex == 0 ? 10.5f : 9.5f), r,
                    e.ColumnIndex == 0 ? Theme.Fg : Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            _list.SelectedIndexChanged += delegate { LoadSelected(); };

            left.Controls.Add(_list);
            left.Controls.Add(filter);

            Panel sep = new Panel();
            sep.Dock = DockStyle.Left;
            sep.Width = 1;
            sep.BackColor = Theme.Line;

            Controls.Add(sep);
            Controls.Add(left);
        }

        private void BuildEditor()
        {
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(P(24), P(16), P(24), P(12));

            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = P(26);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Theme.Dim;
            _status.Font = F(9f);
            _status.Text = "";

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            t.RowCount = 9;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(96)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, P(44)));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, P(34)));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, P(48)));
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, P(24)));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _name = Field();
            AddField(t, 0, "名称", _name);
            _aliases = Field();
            AddField(t, 1, "别名", _aliases);

            Panel catRow = new Panel();
            catRow.Dock = DockStyle.Fill;
            catRow.Margin = new Padding(0, P(9), 0, P(9));
            _cat = new ComboBox();
            _cat.DropDownStyle = ComboBoxStyle.DropDownList;
            _cat.FlatStyle = FlatStyle.Flat;
            _cat.BackColor = Theme.BgSoft;
            _cat.ForeColor = Theme.Fg;
            _cat.Font = F(10f);
            _cat.Width = P(140);
            for (int i = 0; i < Categories.Length; i++) _cat.Items.Add(Categories[i]);
            _cat.Location = new Point(0, 0);
            _action = new ComboBox();
            _action.DropDownStyle = ComboBoxStyle.DropDownList;
            _action.FlatStyle = FlatStyle.Flat;
            _action.BackColor = Theme.BgSoft;
            _action.ForeColor = Theme.Fg;
            _action.Font = F(10f);
            _action.Width = P(150);
            for (int i = 0; i < ActionNames.Length; i++) _action.Items.Add(ActionNames[i]);
            _action.Location = new Point(P(152), 0);
            _action.SelectedIndexChanged += delegate { OnActionChanged(); };
            catRow.Controls.Add(_cat);
            catRow.Controls.Add(_action);
            Label catCap = Caption("分类 / 动作");
            t.Controls.Add(catCap, 0, 2);
            t.Controls.Add(catRow, 1, 2);

            _param = Field();
            AddField(t, 3, "参数", _param);
            _hint = Field();
            AddField(t, 4, "提示语", _hint);

            _enabled = new CheckBox();
            _enabled.Text = "启用这条指令";
            _enabled.ForeColor = Theme.Fg;
            _enabled.AutoSize = true;
            _enabled.Font = F(10f);
            _enabled.Margin = new Padding(0, P(6), 0, P(4));
            t.Controls.Add(Caption(""), 0, 5);
            t.Controls.Add(_enabled, 1, 5);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Fill;
            btns.FlowDirection = FlowDirection.LeftToRight;
            btns.WrapContents = false;
            btns.Margin = new Padding(0, P(8), 0, 0);
            _save = SmallButton("保存");
            _save.ForeColor = Theme.Accent;
            _save.Click += delegate { SaveCurrent(); };
            _delete = SmallButton("删除");
            _delete.Click += delegate { DeleteCurrent(); };
            _clone = SmallButton("复制为自定义");
            _clone.Click += delegate { CloneCurrent(); };
            Button test = SmallButton("试一试");
            test.Click += delegate { TestCurrent(); };
            btns.Controls.Add(_save);
            btns.Controls.Add(_delete);
            btns.Controls.Add(_clone);
            btns.Controls.Add(test);
            t.Controls.Add(btns, 0, 6);
            t.SetColumnSpan(btns, 2);

            _readonly = new Label();
            _readonly.Dock = DockStyle.Fill;
            _readonly.ForeColor = Theme.Accent;
            _readonly.Font = F(9f);
            _readonly.TextAlign = ContentAlignment.MiddleLeft;
            _readonly.Text = "";
            t.Controls.Add(_readonly, 0, 7);
            t.SetColumnSpan(_readonly, 2);

            Panel jsonWrap = new Panel();
            jsonWrap.Dock = DockStyle.Fill;
            jsonWrap.Margin = new Padding(0, P(6), 0, 0);
            _json = new TextBox();
            _json.Dock = DockStyle.Fill;
            _json.Multiline = true;
            _json.ReadOnly = true;
            _json.ScrollBars = ScrollBars.Vertical;
            _json.BorderStyle = BorderStyle.FixedSingle;
            _json.BackColor = Theme.BgSoft;
            _json.ForeColor = Theme.Dim;
            _json.Font = new Font("Consolas", 9.5f);
            _json.Text = "";
            jsonWrap.Controls.Add(_json);
            t.Controls.Add(jsonWrap, 0, 8);
            t.SetColumnSpan(jsonWrap, 2);

            right.Controls.Add(t);
            right.Controls.Add(_status);
            Controls.Add(right);
        }

        private Label Caption(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = Theme.Dim;
            l.Font = F(9.5f);
            l.Margin = new Padding(0, P(2), P(10), P(2));
            return l;
        }

        private void AddField(TableLayoutPanel t, int row, string caption, Control c)
        {
            Label l = Caption(caption);
            t.Controls.Add(l, 0, row);
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, P(10), 0, P(10));
            t.Controls.Add(c, 1, row);
            if (caption == "参数") _paramLabel = l;
        }

        private TextBox Field()
        {
            TextBox b = new TextBox();
            b.BorderStyle = BorderStyle.FixedSingle;
            b.BackColor = Theme.BgSoft;
            b.ForeColor = Theme.Fg;
            b.Font = F(10.5f);
            b.TextChanged += delegate { UpdatePreview(); };
            return b;
        }

        private Button SmallButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Line;
            b.FlatAppearance.MouseOverBackColor = Theme.BgSel;
            b.BackColor = Theme.BgSoft;
            b.ForeColor = Theme.Fg;
            b.Font = F(9.5f);
            b.Padding = new Padding(P(10), P(4), P(10), P(4));
            b.Margin = new Padding(0, 0, P(8), 0);
            b.Cursor = Cursors.Hand;
            return b;
        }

        // ------------------------------------------------------------------ data

        private void FillList()
        {
            if (_list == null || Store == null) return;
            string q = (_search == null || _search.Text == "搜索指令…" ? "" : _search.Text).Trim().ToLowerInvariant();
            string cat = (_catFilter == null || _catFilter.SelectedIndex <= 0) ? "" : (string)_catFilter.SelectedItem;

            string keep = _current == null ? "" : _current.Id;
            _list.BeginUpdate();
            _list.Items.Clear();
            int shown = 0;

            AddRange(Store.Builtins, q, cat, ref shown);
            AddRange(Store.User, q, cat, ref shown);

            _list.EndUpdate();
            if (keep.Length > 0) SelectById(keep);
            else if (_current == null && _list.Items.Count > 0)
            {
                _list.Items[0].Selected = true;
                try { _list.TopItem = _list.Items[0]; } catch { }
            }
            _list.Invalidate();
            int total = Store.Builtins.Count + Store.User.Count;
            _status.Text = "内置 " + Store.Builtins.Count + " 条 · 自定义 " + Store.User.Count + " 条 · 应用索引 "
                + (_apps == null ? 0 : _apps.Apps.Count) + " 个"
                + (shown != total ? " · 筛选出 " + shown + " 条" : "");
        }

        private void AddRange(List<Command> list, string q, string cat, ref int shown)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Command c = list[i];
                if (cat.Length > 0 && c.Category != cat) continue;
                if (q.Length > 0 && !Matches(c, q)) continue;
                ListViewItem it = new ListViewItem(c.Display);
                it.SubItems.Add(c.Category);
                it.SubItems.Add(ActionLabel(c.Action));
                it.Tag = c;
                _list.Items.Add(it);
                shown++;
            }
        }

        private static bool Matches(Command c, string q)
        {
            if (c.Name != null && c.Name.ToLowerInvariant().Contains(q)) return true;
            if (c.Category != null && c.Category.ToLowerInvariant().Contains(q)) return true;
            if (c.Aliases != null) for (int i = 0; i < c.Aliases.Count; i++) if (c.Aliases[i].ToLowerInvariant().Contains(q)) return true;
            if (c.Action != null && c.Action.ToLowerInvariant().Contains(q)) return true;
            if (c.Hint != null && c.Hint.ToLowerInvariant().Contains(q)) return true;
            return false;
        }

        private static string ActionLabel(string a)
        {
            for (int i = 0; i < ActionValues.Length; i++) if (ActionValues[i] == a) return ActionNames[i];
            return a;
        }

        private void SelectById(string id)
        {
            for (int i = 0; i < _list.Items.Count; i++)
            {
                Command c = _list.Items[i].Tag as Command;
                if (c != null && c.Id == id) { _list.Items[i].Selected = true; _list.Items[i].EnsureVisible(); return; }
            }
        }

        private void LoadSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            Command c = _list.SelectedItems[0].Tag as Command;
            if (c == null) return;
            _current = c;
            _loading = true;
            _name.Text = c.Name;
            _aliases.Text = string.Join("，", c.Aliases.ToArray());
            _cat.SelectedItem = c.Category;
            if (_cat.SelectedIndex < 0 && _cat.Items.Count > 0) _cat.SelectedIndex = 0;
            int ai = Array.IndexOf(ActionValues, c.Action);
            _action.SelectedIndex = ai < 0 ? 2 : ai;
            _param.Text = c.Target.Length > 0 ? c.Target : (c.Keys.Length > 0 ? c.Keys : (c.Op.Length > 0 ? c.Op : c.Text));
            _hint.Text = c.Hint;
            _enabled.Checked = c.Enabled;
            _loading = false;
            UpdateEditable(c.Builtin);
            UpdatePreview();
        }

        private void UpdateEditable(bool builtin)
        {
            _name.ReadOnly = builtin;
            _aliases.ReadOnly = builtin;
            _param.ReadOnly = builtin;
            _hint.ReadOnly = builtin;
            _cat.Enabled = !builtin;
            _action.Enabled = !builtin;
            _enabled.Enabled = !builtin;
            _save.Enabled = !builtin;
            _delete.Enabled = !builtin;
            _clone.Enabled = builtin;
            _readonly.Text = builtin ? "内置指令为只读，修改请点「复制为自定义」" : "";
        }

        private void OnActionChanged()
        {
            if (_paramLabel != null)
            {
                string a = ActionValues[Math.Max(0, _action.SelectedIndex)];
                if (a == "open_app") _paramLabel.Text = "应用名";
                else if (a == "url") _paramLabel.Text = "网址";
                else if (a == "keys") _paramLabel.Text = "按键";
                else if (a == "system") _paramLabel.Text = "系统操作";
                else if (a == "text") _paramLabel.Text = "要输入的字";
                else if (a == "wait") _paramLabel.Text = "毫秒";
                else _paramLabel.Text = "步骤";
            }
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_json == null || _loading) return;
            _json.Text = Json.Stringify(BuildFromForm().ToJson(), true).Replace("\n", "\r\n");
        }

        private Command BuildFromForm()
        {
            Command c = new Command();
            c.Id = _current == null ? "preview" : _current.Id;
            c.Builtin = _current != null && _current.Builtin;
            c.Uses = _current == null ? 0 : _current.Uses;
            c.Name = _name.Text.Trim();
            c.Aliases = new List<string>();
            string[] parts = _aliases.Text.Split(new char[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length > 0) c.Aliases.Add(s);
            }
            c.Category = _cat.SelectedItem == null ? "自定义" : _cat.SelectedItem.ToString();
            c.Action = ActionValues[Math.Max(0, _action.SelectedIndex)];
            string v = _param.Text.Trim();
            c.Target = ""; c.Keys = ""; c.Op = ""; c.Text = "";
            if (c.Action == "open_app" || c.Action == "url") c.Target = v;
            else if (c.Action == "keys") c.Keys = v;
            else if (c.Action == "system") c.Op = v;
            else if (c.Action == "text") c.Text = v;
            else c.Target = v;
            c.Hint = _hint.Text.Trim();
            c.Enabled = _enabled.Checked;
            return c;
        }

        // ------------------------------------------------------------------ actions

        private void SaveCurrent()
        {
            if (_current == null) { Status("先选一条指令", true); return; }
            if (_current.Builtin) { Status("内置指令不能改，请先复制为自定义", true); return; }
            Command c = BuildFromForm();
            if (c.Name.Length == 0) { Status("名称不能为空", true); return; }
            int idx = Store.User.IndexOf(_current);
            if (idx < 0) { Status("找不到这条指令", true); return; }
            Store.User[idx] = c;
            Store.SaveUser();
            _current = c;
            FillList();
            Status("已保存：" + c.Name, false);
        }

        private void DeleteCurrent()
        {
            if (_current == null) return;
            if (_current.Builtin) { Status("内置指令不能删除", true); return; }
            if (MessageBox.Show("删除「" + _current.Name + "」？", "SayAll 语音指令库", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Store.User.Remove(_current);
            Store.SaveUser();
            _current = null;
            FillList();
            Status("已删除", false);
        }

        private void CloneCurrent()
        {
            if (_current == null) return;
            Command c = _current.Clone();
            c.Id = "user_" + DateTime.Now.Ticks.ToString();
            c.Builtin = false;
            c.Name = c.Name + "（自定义）";
            Store.User.Add(c);
            Store.SaveUser();
            _current = c;
            FillList();
            SelectById(c.Id);
            Status("已复制，可直接修改", false);
        }

        private void NewCommand()
        {
            Command c = new Command();
            c.Id = "user_" + DateTime.Now.Ticks.ToString();
            c.Name = "新指令";
            c.Category = "自定义";
            c.Action = "keys";
            c.Keys = "ctrl+alt+n";
            c.Hint = "";
            c.Enabled = true;
            Store.User.Add(c);
            Store.SaveUser();
            _current = c;
            FillList();
            SelectById(c.Id);
            _name.Focus();
            _name.SelectAll();
            Status("新建成功，改好后点保存", false);
        }

        private void TestCurrent()
        {
            Command c = BuildFromForm();
            if (c.Action == "open_app" || c.Action == "url" || c.Action == "system")
            {
                try { Executor.Run(c, IntPtr.Zero, false); Status("已执行：" + c.Display, false); }
                catch (Exception ex) { Status("执行失败：" + ex.Message, true); }
            }
            else
            {
                Status("这类指令会向当前窗口发按键，请按住语音键实测", true);
            }
        }

        private void RebuildIndex()
        {
            if (_apps == null) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                _apps.Rebuild();
                Status("应用索引已重建：" + _apps.Apps.Count + " 个", false);
            }
            catch (Exception ex) { Status("重建失败：" + ex.Message, true); }
            finally { Cursor = Cursors.Default; }
        }

        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                Process.Start("explorer.exe", "\"" + Settings.DataDir + "\"");
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        private void Status(string text, bool warn)
        {
            _status.Text = text;
            _status.ForeColor = warn ? Theme.Bad : Theme.Good;
        }
    }
}
