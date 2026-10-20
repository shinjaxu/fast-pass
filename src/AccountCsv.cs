using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

internal static class AccountCsv
{
    sealed class Row
    {
        internal string[] Values;
    }

    internal static List<Account> Read(string path)
    {
        int skipped;
        return Read(path, out skipped);
    }

    internal static List<Account> Read(string path, out int skipped)
    {
        skipped = 0;
        var rows = Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        if (rows.Count == 0) throw new FormatException("CSV 文件为空。");
        string[] names = { "url", "username", "password", "note" };
        int[] columns = { -1, -1, -1, -1 };
        for (int i = 0; i < rows[0].Values.Length; i++)
            for (int j = 0; j < names.Length; j++)
                if (String.Equals(rows[0].Values[i].Trim(), names[j], StringComparison.OrdinalIgnoreCase))
                {
                    if (columns[j] >= 0) throw new FormatException("表头重复：" + names[j] + "。");
                    columns[j] = i;
                }
        for (int j = 0; j < names.Length; j++)
            if (columns[j] < 0) throw new FormatException("缺少表头：" + names[j] + "。需要 url,username,password,note 四列。");
        if (rows.Count == 1) throw new FormatException("CSV 中没有可导入的账户。");
        var accounts = new List<Account>();
        // 空必填字段只影响当前记录；全部解析完成后再统一保存有效账户。
        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string[] values = new string[4];
            for (int j = 0; j < names.Length; j++)
            {
                values[j] = columns[j] < row.Values.Length ? row.Values[columns[j]] : "";
            }
            if (String.IsNullOrWhiteSpace(values[0]) || String.IsNullOrWhiteSpace(values[1]) || String.IsNullOrWhiteSpace(values[2]))
            {
                skipped++;
                continue;
            }
            accounts.Add(new Account { Domain = values[0], Username = values[1], Password = values[2], Notes = values[3] });
        }
        return accounts;
    }

    internal static void Write(string path, List<Account> accounts)
    {
        var text = new StringBuilder("url,username,password,note\r\n");
        foreach (var account in accounts)
        {
            string[] values = { account.Domain, account.Username, account.Password, account.Notes };
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) text.Append(',');
                // 所有字段统一加引号，保留逗号、双引号及字段内换行。
                text.Append('"').Append((values[i] ?? "").Replace("\"", "\"\"")).Append('"');
            }
            text.Append("\r\n");
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    static List<Row> Parse(string text)
    {
        var rows = new List<Row>();
        var values = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, hasContent = false;
        int line = 1, startLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else
                {
                    field.Append(c);
                    if (c == '\n' || (c == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) line++;
                }
            }
            else if (c == ',' || c == '\r' || c == '\n')
            {
                values.Add(field.ToString()); field.Clear(); closed = false;
                if (c == ',') { hasContent = true; continue; }
                if (hasContent) rows.Add(new Row { Values = values.ToArray() });
                values.Clear(); hasContent = false;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                line++; startLine = line;
            }
            else
            {
                if (closed || (c == '"' && field.Length > 0))
                    throw new FormatException("第 " + line + " 行的 CSV 引号格式不正确。");
                hasContent = true;
                if (c == '"') quoted = true;
                else field.Append(c);
            }
        }
        if (quoted) throw new FormatException("第 " + startLine + " 行开始的 CSV 引号未闭合。");
        if (hasContent)
        {
            values.Add(field.ToString());
            rows.Add(new Row { Values = values.ToArray() });
        }
        return rows;
    }
}
