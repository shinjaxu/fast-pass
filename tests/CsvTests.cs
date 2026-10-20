using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

internal static class CsvTests
{
    static int count;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("失败：" + name);
        Console.WriteLine("通过：" + name); count++;
    }
    static void Reject(string path, string text, string name)
    {
        File.WriteAllText(path, text, new UTF8Encoding(true));
        bool failed = false;
        try { AccountCsv.Read(path); } catch (FormatException) { failed = true; }
        Check(failed, name);
    }
    static void Skip(string path, string text, int validCount, string name)
    {
        File.WriteAllText(path, text, new UTF8Encoding(true));
        int skipped;
        Check(AccountCsv.Read(path, out skipped).Count == validCount && skipped == 1, name);
    }
    static int Main()
    {
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "csv-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "accounts.csv");
        try
        {
            File.WriteAllText(path, "extra, password ,USERNAME,note,URL,unused\r\nx, secret ,测试账户,,https://example.com,y\r\n", new UTF8Encoding(true));
            var read = AccountCsv.Read(path);
            Check(read.Count == 1 && read[0].Domain == "https://example.com" && read[0].Username == "测试账户" && read[0].Password == " secret " && read[0].Notes == "", "按表头识别乱序列并忽略额外列，保留密码空格");
            File.WriteAllText(path, "name,url,username,password,note\n网站标题,https://example.com,实际账户,p,n", new UTF8Encoding(true));
            Check(AccountCsv.Read(path)[0].Username == "实际账户", "同时存在 name 和 username 时只读取 username 作为账户名");
            Reject(path, "url,name,password,note\na,b,p,n", "只有旧 name 表头时提示缺少 username");
            var entries = new List<Account> { new Account { Domain = "https://a.com/?x=1,2", Username = "用户\"甲", Password = "密,码\"\r\n下一行", Notes = "备注\r\n多行\n末行" } };
            AccountCsv.Write(path, entries);
            byte[] bytes = File.ReadAllBytes(path);
            Check(bytes.Length > 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, "导出 UTF-8 BOM，兼容中文");
            Check(File.ReadAllText(path).StartsWith("url,username,password,note\r\n"), "导出固定四列表头");
            read = AccountCsv.Read(path);
            Check(read.Count == 1 && read[0].Domain == entries[0].Domain && read[0].Username == entries[0].Username && read[0].Password == entries[0].Password && read[0].Notes == entries[0].Notes, "逗号、双引号与多行内容导出再导入无损");
            Skip(path, "url,username,password,note\na,b,p,n\n,c,p,n\nd,e,q,", 2, "缺少 url 的行被跳过，前后有效记录继续导入");
            Skip(path, "url,username,password,note\na,,p,n", 0, "username 为空的行被跳过");
            Skip(path, "url,username,password,note\na,b,,n", 0, "password 为空的行被跳过");
            Skip(path, "url,username,password,note\na,b,   ,n", 0, "必填列只有空白的行被跳过");
            Reject(path, "url,username,note\na,b,n", "缺少必需表头时拒绝");
            Skip(path, "url,username,password,note\na,b", 0, "缺失必填字段的行被跳过");
            Reject(path, "url,username,password,note\na,b,\"unfinished,n", "未闭合引号时拒绝");
            Reject(path, "url,username,password,note\na,b,\"p\"junk,n", "闭合引号后非法字符时拒绝");
            Reject(path, "url,username,password,note,password\na,b,p,n,other", "重复目标表头时拒绝");
            Reject(path, "", "空文件时拒绝");
            Reject(path, "url,username,password,note\n", "只有表头时拒绝");
            File.WriteAllText(path, "url,username,password,note\n\na,b,p,\n\n", new UTF8Encoding(false));
            Check(AccountCsv.Read(path).Count == 1, "接受无 BOM 的 UTF-8 与空行");
            AccountCsv.Write(path, new List<Account>());
            Check(File.ReadAllText(path) == "url,username,password,note\r\n", "无账户时导出表头");
            Console.WriteLine("CSV 测试全部通过，共 " + count + " 项。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(dir, true); }
    }
}
