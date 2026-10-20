using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class UiTests
{
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
    static int count;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("失败：" + name);
        Console.WriteLine("通过：" + name); count++;
    }
    static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
    static void Invoke(object target, string name, params object[] args)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string[] unrestrictedPasswords = { "", "1", " ", new string('密', 40000) + "!🔒" };
            for (int i = 0; i < unrestrictedPasswords.Length; i++)
            {
                string unrestrictedPath = Path.Combine(dir, "unrestricted-" + i + ".vault");
                using (var setup = new UnlockForm(unrestrictedPath))
                {
                    Check(Field<TextBox>(setup, "password").MaxLength == 0 && Field<TextBox>(setup, "confirmation").MaxLength == 0, "主密码与确认框不限制输入长度");
                    Field<TextBox>(setup, "password").Text = unrestrictedPasswords[i];
                    Field<TextBox>(setup, "confirmation").Text = unrestrictedPasswords[i];
                    Invoke(setup.AcceptButton, "OnClick", EventArgs.Empty);
                    Check(setup.DialogResult == DialogResult.OK, "允许空、单字符、空格及长中文主密码：场景 " + i);
                    setup.Store.Dispose();
                }
                using (var unlock = new UnlockForm(unrestrictedPath))
                {
                    Field<TextBox>(unlock, "password").Text = unrestrictedPasswords[i];
                    Invoke(unlock.AcceptButton, "OnClick", EventArgs.Empty);
                    Check(unlock.DialogResult == DialogResult.OK, "不限制格式的主密码可再次解锁：场景 " + i);
                    unlock.Store.Dispose();
                }
            }
            var original = new Account { Domain = "example.com", Username = "演示账户", Password = "demo-password-only", Notes = "测试环境" };
            string path = Path.Combine(dir, "accounts.vault");
            using (var store = VaultStore.Create(path, "test-master-password"))
            {
                store.Save(new List<Account> { original });
                using (var main = new MainForm(store, new List<Account> { original }))
                {
                    Check(!main.Visible, "主窗口初始隐藏");
                    bool probeRegistered = RegisterHotKey(main.Handle, 2, 0x4002, (uint)Keys.P);
                    int probeError = Marshal.GetLastWin32Error();
                    if (probeRegistered) UnregisterHotKey(main.Handle, 2);
                    bool registered = Field<bool>(main, "hotkeyRegistered");
                    if (registered)
                        Check(!probeRegistered && probeError == 1409, "程序已注册并占用 Ctrl+P 全局热键");
                    else
                    {
                        Check(Field<int>(main, "hotkeyError") == 1409 && !probeRegistered && probeError == 1409, "外部程序占用 Ctrl+P 时正确检测冲突");
                        Check(Field<NotifyIcon>(main, "tray").Text.Contains("不可用"), "快捷键冲突时托盘提供双击呼出提示");
                        Console.WriteLine("提示：Ctrl+P 当前被外部程序占用，本轮验证冲突处理及消息分发，不验证真实按键触发。");
                    }
                    Check(Field<NotifyIcon>(main, "tray").Visible, "后台托盘图标启用");
                    Invoke(main, "WndProc", Message.Create(main.Handle, 0x0312, new IntPtr(1), IntPtr.Zero)); Application.DoEvents();
                    Check(main.Visible, "呼出主窗口");
                    var search = Field<TextBox>(main, "search");
                    var grid = Field<DataGridView>(main, "grid");
                    Check(search.Focused && grid.Rows.Count == 1, "呼出后聚焦搜索并展示数据");
                    using (var image = new Bitmap(main.Width, main.Height))
                    {
                        main.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "main-preview.png"));
                    }
                    using (var editor = new EditForm(original))
                    {
                        editor.Shown += delegate
                        {
                            main.Toggle();
                            Check(!IsWindowVisible(main.Handle) && !IsWindowVisible(editor.Handle), "编辑期间热键整体隐藏且不丢失输入");
                            main.Toggle();
                            Check(IsWindowVisible(main.Handle) && IsWindowVisible(editor.Handle) && Field<TextBox>(editor, "username").Text == original.Username, "编辑期间热键恢复维护窗口与输入");
                            using (var image = new Bitmap(editor.Width, editor.Height))
                            {
                                editor.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "edit-preview.png"));
                            }
                            editor.DialogResult = DialogResult.Cancel;
                        };
                        editor.ShowDialog(main);
                        Check(editor.Result == null, "取消编辑不产生保存记录");
                    }
                    main.Toggle(); Check(!main.Visible, "再次呼出操作隐藏窗口");
                    main.Toggle(); main.Close(); Check(!main.Visible && !main.IsDisposed, "关闭窗口继续后台运行");
                    search.Text = "生产"; Check(grid.Rows.Count == 0, "界面实时过滤不匹配账户");
                    search.Text = "EXAMPLE"; Check(grid.Rows.Count == 1, "界面域名匹配忽略大小写");
                    search.Text = "测试"; Check(grid.Rows.Count == 1, "界面备注匹配");
                    Invoke(grid, "OnCellContentClick", new DataGridViewCellEventArgs(3, 0));
                    Check(Clipboard.GetText() == original.Username, "账户名按钮单独复制");
                    Invoke(grid, "OnCellContentClick", new DataGridViewCellEventArgs(4, 0));
                    Check(Clipboard.GetText() == original.Password, "密码按钮单独复制");
                    Clipboard.Clear();
                    var changed = new List<Account> { new Account { Domain = "new.com", Username = "修改账户", Password = "changed-demo", Notes = "修改备注" } };
                    Invoke(main, "Save", changed);
                    search.Text = "";
                    Check(grid.Rows.Count == 1 && (string)grid.Rows[0].Cells[1].Value == "修改账户", "保存修改后刷新界面");
                    List<Account> read;
                    using (var reopened = VaultStore.Open(path, "test-master-password", out read))
                        Check(read[0].Password == "changed-demo", "界面修改正确写入加密文件");
                    string csvPath = Path.Combine(dir, "import.csv");
                    File.WriteAllText(csvPath, "extra,url,username,password,note\nignored,https://import.test,导入账户,import-demo,导入备注");
                    Invoke(main, "ImportFile", csvPath);
                    Check(grid.Rows.Count == 2 && (string)grid.Rows[0].Cells[1].Value == "修改账户" && (string)grid.Rows[1].Cells[1].Value == "导入账户", "CSV 导入追加账户并保留现有记录");
                    using (var reopened = VaultStore.Open(path, "test-master-password", out read))
                        Check(read.Count == 2 && read[1].Password == "import-demo", "CSV 导入结果写入加密文件");
                    File.WriteAllText(csvPath, "url,username,password,note\nhttps://valid.test,name,pass,\nhttps://invalid.test,,pass,");
                    Invoke(main, "ImportFile", csvPath);
                    Check(grid.Rows.Count == 3 && Field<Label>(main, "status").Text.Contains("跳过 1 条"), "空必填项行被跳过，正常行继续追加且显示数量");
                    using (var reopened = VaultStore.Open(path, "test-master-password", out read))
                        Check(read.Count == 3 && read[2].Domain == "https://valid.test", "混合有效和空必填项行时仅持久化有效行");
                    byte[] beforeImport = File.ReadAllBytes(path);
                    File.WriteAllText(csvPath, "url,username,password,note\nhttps://invalid.test,,pass,");
                    Invoke(main, "ImportFile", csvPath);
                    Check(grid.Rows.Count == 3 && Field<Label>(main, "status").Text.Contains("已导入 0 条账户，跳过 1 条"), "全部行被跳过时显示零条导入并保留账户");
                    Check(Convert.ToBase64String(beforeImport) == Convert.ToBase64String(File.ReadAllBytes(path)), "全部行被跳过时不重写加密文件");
                    File.WriteAllText(csvPath, "url,username,password,note\nhttps://valid.test,name,pass,\nhttps://invalid.test,name,\"unfinished");
                    bool invalidRejected = false;
                    try { Invoke(main, "ImportFile", csvPath); }
                    catch (TargetInvocationException ex) { invalidRejected = ex.InnerException is FormatException; }
                    Check(invalidRejected && grid.Rows.Count == 3, "CSV 格式错误仍整批拒绝");
                    Check(Convert.ToBase64String(beforeImport) == Convert.ToBase64String(File.ReadAllBytes(path)), "CSV 格式错误时加密文件保持不变");
                    Invoke(main, "Save", new List<Account>());
                    Check(grid.Rows.Count == 0, "删除结果保存后刷新界面");
                }
                using (var second = new MainForm(store, new List<Account>()))
                    Check(Field<bool>(second, "hotkeyRegistered") || Field<int>(second, "hotkeyError") == 1409, "重建窗口后正确注册热键或检测外部占用");
            }
            using (var edit = new EditForm(original))
            {
                var password = Field<TextBox>(edit, "password");
                Check(password.UseSystemPasswordChar, "维护表单默认遮挡密码");
                Field<TextBox>(edit, "username").Text = " 新账户 ";
                password.Text = " new-secret ";
                Invoke(edit.AcceptButton, "OnClick", EventArgs.Empty);
                Check(edit.Result.Username == "新账户" && edit.Result.Password == " new-secret ", "维护表单去除账户名空白且保留密码原文");
                Check(original.Username == "演示账户", "编辑不直接变更原记录");
            }
            string firstPath = Path.Combine(dir, "first-run.vault");
            using (var setup = new UnlockForm(firstPath))
            {
                Field<TextBox>(setup, "password").Text = "first-master-password";
                Field<TextBox>(setup, "confirmation").Text = "first-master-password";
                Invoke(setup.AcceptButton, "OnClick", EventArgs.Empty);
                Check(setup.DialogResult == DialogResult.OK && File.Exists(firstPath), "首次设置主密码创建加密文件");
                Check(setup.Accounts.Count == 0, "首次创建不含默认账户");
                setup.Store.Dispose();
            }
            using (var unlock = new UnlockForm(firstPath))
            {
                Field<TextBox>(unlock, "password").Text = "first-master-password";
                Invoke(unlock.AcceptButton, "OnClick", EventArgs.Empty);
                Check(unlock.DialogResult == DialogResult.OK && unlock.Accounts.Count == 0, "后续启动使用主密码解锁");
                Check(Field<TextBox>(unlock, "password").Text.Length == 0, "解锁后清空主密码输入框");
                unlock.Store.Dispose();
            }
            Console.WriteLine("界面测试全部通过，共 " + count + " 项。");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(dir, true); }
    }
}
