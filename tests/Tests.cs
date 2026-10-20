using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

internal static class Tests
{
    static int count;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("失败：" + name);
        count++;
        Console.WriteLine("通过：" + name);
    }
    static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (CryptographicException) { rejected = true; }
        Check(rejected, name);
    }
    [STAThread]
    static int Main()
    {
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "accounts.vault");
            var entries = new List<Account> { new Account { Domain = "Example.COM", Username = "测试账户", Password = "秘密<&>\"\n密碼", Notes = "生产环境\n管理员" } };
            using (var store = VaultStore.Create(path, "测试主密码123"))
            {
                store.Save(entries);
                byte[] first = File.ReadAllBytes(path);
                Check(!Encoding.UTF8.GetString(first).Contains("秘密"), "文件不包含明文密码");
                store.Save(entries);
                Check(Convert.ToBase64String(first) != Convert.ToBase64String(File.ReadAllBytes(path)), "每次保存使用随机 IV");
            }
            List<Account> read;
            using (var store = VaultStore.Open(path, "测试主密码123", out read))
            {
                Check(read.Count == 1 && read[0].Password == entries[0].Password && read[0].Notes == entries[0].Notes, "中文、特殊字符与换行持久化");
                read.Add(new Account { Domain = "second", Username = "two", Password = "new", Notes = "" });
                store.Save(read);
            }
            using (var store = VaultStore.Open(path, "测试主密码123", out read))
                Check(read.Count == 2, "重新启动后读取多条记录");
            Check(entries[0].Matches("example") && entries[0].Matches("试账") && entries[0].Matches("管理员"), "域名、账户名、备注包含查询");
            Check(entries[0].Matches("  ") && !entries[0].Matches("秘密"), "空查询返回全部且不搜索密码");
            Reject(delegate { using (VaultStore.Open(path, "错误密码", out read)) { } }, "错误主密码被拒绝");
            byte[] good = File.ReadAllBytes(path);
            byte[] bad = (byte[])good.Clone(); bad[45] ^= 1; File.WriteAllBytes(path, bad);
            Reject(delegate { using (VaultStore.Open(path, "测试主密码123", out read)) { } }, "密文篡改被拒绝");
            bad = (byte[])good.Clone(); bad[12] ^= 1; File.WriteAllBytes(path, bad);
            Reject(delegate { using (VaultStore.Open(path, "测试主密码123", out read)) { } }, "盐篡改被拒绝");
            File.WriteAllBytes(path, new byte[10]);
            Reject(delegate { using (VaultStore.Open(path, "测试主密码123", out read)) { } }, "截断文件被拒绝");
            File.WriteAllBytes(path, good);
            using (var store = VaultStore.Open(path, "测试主密码123", out read))
            {
                string temporary = path + ".tmp";
                Directory.CreateDirectory(temporary);
                bool failed = false;
                try { store.Save(new List<Account>()); } catch (UnauthorizedAccessException) { failed = true; }
                Check(failed && Convert.ToBase64String(good) == Convert.ToBase64String(File.ReadAllBytes(path)), "保存失败保留原文件");
                Directory.Delete(temporary);
                store.Save(new List<Account>());
            }
            using (var store = VaultStore.Open(path, "测试主密码123", out read))
                Check(read.Count == 0, "删除全部记录后可重新读取");
            Console.WriteLine("全部通过，共 " + count + " 项。");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(dir, true); }
    }
}
