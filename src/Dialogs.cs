using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

internal static class FormStyle
{
    internal static TextBox Field(TableLayoutPanel layout, string label, bool secret, bool multiline)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 10, 12, 8) }, 0, row);
        var box = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = secret, Multiline = multiline, Margin = new Padding(0, 6, 0, 6) };
        if (multiline) { box.Height = 90; box.ScrollBars = ScrollBars.Vertical; box.AcceptsReturn = true; }
        layout.Controls.Add(box, 1, row);
        return box;
    }

    internal static TableLayoutPanel Layout(Form form)
    {
        form.Font = new Font("Microsoft YaHei UI", 10F);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false; form.MinimizeBox = false;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.Controls.Add(layout);
        return layout;
    }

    internal static Button Buttons(Form form, TableLayoutPanel layout, string text)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = text, AutoSize = true };
        panel.Controls.Add(cancel); panel.Controls.Add(ok);
        int row = layout.RowCount++;
        layout.Controls.Add(panel, 0, row); layout.SetColumnSpan(panel, 2);
        form.AcceptButton = ok; form.CancelButton = cancel;
        return ok;
    }
}

internal sealed class UnlockForm : Form
{
    readonly string path;
    readonly bool creating;
    readonly TextBox password;
    readonly TextBox confirmation;
    internal VaultStore Store;
    internal List<Account> Accounts;

    internal UnlockForm(string path)
    {
        this.path = path;
        creating = !File.Exists(path);
        Text = creating ? "快捷账户 · 设置主密码" : "快捷账户 · 解锁";
        ClientSize = new Size(480, creating ? 255 : 205);
        var layout = FormStyle.Layout(this);
        var hint = new Label { Text = creating ? "主密码用于加密本地数据，请牢记，遗忘后无法恢复。" : "请输入主密码，解锁后程序将在托盘后台运行。", AutoSize = true, MaximumSize = new Size(430, 0), Margin = new Padding(0, 0, 0, 12) };
        layout.Controls.Add(hint, 0, layout.RowCount++); layout.SetColumnSpan(hint, 2);
        password = FormStyle.Field(layout, "主密码", true, false);
        password.MaxLength = 0;
        if (creating)
        {
            confirmation = FormStyle.Field(layout, "确认密码", true, false);
            confirmation.MaxLength = 0;
        }
        Button ok = FormStyle.Buttons(this, layout, creating ? "创建并进入后台" : "解锁并进入后台");
        ok.Click += delegate
        {
            if (creating && password.Text != confirmation.Text) { MessageBox.Show(this, "两次输入的密码不一致。", Text); return; }
            ok.Enabled = false; UseWaitCursor = true;
            VaultStore candidate = null;
            try
            {
                if (creating)
                {
                    candidate = VaultStore.Create(path, password.Text);
                    Accounts = new List<Account>();
                    candidate.Save(Accounts);
                }
                else candidate = VaultStore.Open(path, password.Text, out Accounts);
                Store = candidate;
                password.Clear(); if (confirmation != null) confirmation.Clear();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                if (candidate != null) candidate.Dispose();
                MessageBox.Show(this, "无法打开账户文件：" + ex.Message + "\n\n请检查主密码和文件权限。数据文件不会被重置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                password.SelectAll(); password.Focus();
            }
            finally { ok.Enabled = true; UseWaitCursor = false; }
        };
        Shown += delegate { password.Focus(); };
    }
}

internal sealed class EditForm : Form
{
    readonly TextBox domain;
    readonly TextBox username;
    readonly TextBox password;
    readonly TextBox notes;
    internal Account Result;

    internal EditForm(Account account)
    {
        Text = account == null ? "新增账户" : "修改账户";
        ClientSize = new Size(520, 355);
        var layout = FormStyle.Layout(this);
        domain = FormStyle.Field(layout, "域名", false, false);
        username = FormStyle.Field(layout, "账户名", false, false);
        password = FormStyle.Field(layout, "密码", true, false);
        var show = new CheckBox { Text = "显示密码", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        show.CheckedChanged += delegate { password.UseSystemPasswordChar = !show.Checked; };
        layout.Controls.Add(show, 1, layout.RowCount++);
        notes = FormStyle.Field(layout, "备注", false, true);
        if (account != null)
        {
            domain.Text = account.Domain; username.Text = account.Username;
            password.Text = account.Password; notes.Text = account.Notes;
        }
        Button save = FormStyle.Buttons(this, layout, "保存");
        save.Click += delegate
        {
            if (String.IsNullOrWhiteSpace(username.Text))
            {
                MessageBox.Show(this, "请填写账户名。", Text); username.Focus(); return;
            }
            Result = new Account { Domain = domain.Text.Trim(), Username = username.Text.Trim(), Password = password.Text, Notes = notes.Text };
            DialogResult = DialogResult.OK;
        };
    }
}
