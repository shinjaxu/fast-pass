using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class MainForm : Form
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    readonly VaultStore store;
    List<Account> accounts;
    readonly TextBox search;
    readonly DataGridView grid;
    readonly Label status;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip trayMenu;
    readonly bool hotkeyRegistered;
    readonly int hotkeyError;
    bool editorHidden;

    internal MainForm(VaultStore store, List<Account> accounts)
    {
        this.store = store; this.accounts = accounts;
        Text = "快捷账户";
        Icon = SystemIcons.Application;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(960, 540);
        MinimumSize = new Size(720, 400);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(layout);
        var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.Controls.Add(new Label { Text = "搜索", AutoSize = true, Margin = new Padding(0, 7, 0, 0) }, 0, 0);
        search = new TextBox { Dock = DockStyle.Fill, Name = "Search" };
        searchRow.Controls.Add(search, 1, 0); layout.Controls.Add(searchRow, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddButton(toolbar, "新增账户", delegate { Edit(null); });
        AddButton(toolbar, "修改", delegate { var selected = Selected(); if (selected != null) Edit(selected); });
        AddButton(toolbar, "删除", delegate { DeleteSelected(); });
        AddButton(toolbar, "导入 CSV", delegate { ImportCsv(); });
        AddButton(toolbar, "导出 CSV", delegate { ExportCsv(); });
        toolbar.Controls.Add(new Label { Text = "Ctrl+P 显示或隐藏", AutoSize = true, Margin = new Padding(16, 8, 0, 0), ForeColor = Color.DimGray });
        layout.Controls.Add(toolbar, 0, 1);
        grid = new DataGridView {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false, RowHeadersVisible = false, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            Name = "Accounts", AllowUserToResizeRows = false
        };
        grid.RowTemplate.Height = 36;
        AddColumn("域名", 25); AddColumn("账户名", 25); AddColumn("备注", 35);
        grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "账户名", Text = "复制账户名", UseColumnTextForButtonValue = true, Width = 110, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "密码", Text = "复制密码", UseColumnTextForButtonValue = true, Width = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 3) return;
            var account = (Account)grid.Rows[e.RowIndex].Tag;
            Copy(e.ColumnIndex == 3 ? account.Username : account.Password, e.ColumnIndex == 3 ? "账户名" : "密码");
        };
        grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex < 3) Edit((Account)grid.Rows[e.RowIndex].Tag);
        };
        layout.Controls.Add(grid, 0, 2);
        status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
        layout.Controls.Add(status, 0, 3);
        search.TextChanged += delegate { RefreshRows(); };
        KeyPreview = true;
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
        Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示 / 隐藏 (Ctrl+P)", null, delegate { Toggle(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, delegate {
            // 正在编辑时先完成或取消编辑，避免托盘退出导致未保存输入丢失。
            if (OwnedForms.Length > 0) { ActivateEditor(); return; }
            Application.ExitThread();
        });
        tray = new NotifyIcon { Icon = Icon, Text = "快捷账户 · Ctrl+P 呼出", ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += delegate { Toggle(); };
        hotkeyRegistered = RegisterHotKey(Handle, 1, 0x4002, (uint)Keys.P);
        hotkeyError = hotkeyRegistered ? 0 : Marshal.GetLastWin32Error();
        RefreshRows();
        if (!hotkeyRegistered)
        {
            tray.Text = "快捷账户 · Ctrl+P 不可用，请双击呼出";
            tray.ShowBalloonTip(6000, Text, "Ctrl+P 注册失败（" + hotkeyError + "），可能已被其他程序占用。请双击托盘图标呼出。", ToolTipIcon.Warning);
        }
    }

    void AddColumn(string name, int weight)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }

    static void AddButton(FlowLayoutPanel toolbar, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += delegate { action(); };
        toolbar.Controls.Add(button);
    }

    Account Selected()
    {
        return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Account;
    }

    void RefreshRows()
    {
        grid.Rows.Clear();
        foreach (var account in accounts)
            if (account.Matches(search.Text))
            {
                int index = grid.Rows.Add(account.Domain, account.Username, account.Notes);
                grid.Rows[index].Tag = account;
            }
        status.Text = "显示 " + grid.Rows.Count + " / " + accounts.Count + " 条账户　·　关闭窗口继续后台运行";
    }

    void Edit(Account existing)
    {
        using (var dialog = new EditForm(existing))
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var changed = new List<Account>(accounts);
            if (existing == null) changed.Add(dialog.Result);
            else changed[changed.IndexOf(existing)] = dialog.Result;
            Save(changed);
        }
    }

    void DeleteSelected()
    {
        var selected = Selected();
        if (selected == null) return;
        if (MessageBox.Show(this, "确定删除选中的账户？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var changed = new List<Account>(accounts);
        changed.Remove(selected);
        Save(changed);
    }

    void ImportCsv()
    {
        using (var dialog = new OpenFileDialog { Title = "导入账户 CSV", Filter = "CSV 文件 (*.csv)|*.csv", CheckFileExists = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { ImportFile(dialog.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导入失败：" + ex.Message + "\n现有账户未变更。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    void ImportFile(string path)
    {
        int skipped;
        var imported = AccountCsv.Read(path, out skipped);
        if (imported.Count == 0)
        {
            status.Text = "已导入 0 条账户，跳过 " + skipped + " 条（url、username 或 password 为空）";
            return;
        }
        var changed = new List<Account>(accounts);
        changed.AddRange(imported);
        if (Save(changed)) status.Text = "已导入 " + imported.Count + " 条账户，跳过 " + skipped + " 条　·　总计 " + accounts.Count + " 条";
    }

    void ExportCsv()
    {
        if (MessageBox.Show(this, "CSV 将包含全部账户的明文密码，请妥善保管。确定导出？", "导出账户", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        using (var dialog = new SaveFileDialog { Title = "导出全部账户 CSV", Filter = "CSV 文件 (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true, FileName = "accounts.csv", OverwritePrompt = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (!String.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("导出文件必须使用 .csv 扩展名。");
                AccountCsv.Write(dialog.FileName, accounts);
                status.Text = "已导出全部 " + accounts.Count + " 条账户";
            }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    bool Save(List<Account> changed)
    {
        try
        {
            store.Save(changed);
            accounts = changed;
            RefreshRows();
            status.Text = "已保存　·　" + accounts.Count + " 条账户";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败，原有账户未变更。\n" + ex.Message + "\n请确认程序目录可写。", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    void Copy(string text, string field)
    {
        try
        {
            if (String.IsNullOrEmpty(text)) { status.Text = field + "为空，未更改剪贴板"; return; }
            Clipboard.SetText(text);
            status.Text = "已复制" + field;
        }
        catch (ExternalException) { MessageBox.Show(this, "剪贴板正被其他程序占用，请重试。", Text); }
    }

    void ActivateEditor()
    {
        foreach (Form owned in OwnedForms)
        {
            ShowWindow(Handle, 5); ShowWindow(owned.Handle, 5);
            editorHidden = false;
            owned.Activate(); SetForegroundWindow(owned.Handle); return;
        }
    }

    internal void Toggle()
    {
        // 编辑对话框打开时也允许整体隐藏，保留尚未保存的输入。
        if (OwnedForms.Length > 0)
        {
            foreach (Form owned in OwnedForms)
            {
                // 原生隐藏保留模态消息循环，避免 Hide() 结束对话框并丢弃输入。
                if (!editorHidden)
                {
                    ShowWindow(owned.Handle, 0); ShowWindow(Handle, 0); editorHidden = true;
                }
                else ActivateEditor();
                return;
            }
        }
        if (Visible) { Hide(); return; }
        WindowState = FormWindowState.Normal;
        Show(); Activate(); SetForegroundWindow(Handle);
        search.Focus(); search.SelectAll();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1) { Toggle(); return; }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (hotkeyRegistered) UnregisterHotKey(Handle, 1);
            tray.Visible = false; tray.Dispose(); trayMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
