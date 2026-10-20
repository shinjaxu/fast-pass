using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

[DataContract]
public sealed class Account
{
    [DataMember] public string Domain { get; set; }
    [DataMember] public string Username { get; set; }
    [DataMember] public string Password { get; set; }
    [DataMember] public string Notes { get; set; }

    public bool Matches(string query)
    {
        query = query.Trim();
        return Contains(Domain, query) || Contains(Username, query) || Contains(Notes, query);
    }

    static bool Contains(string value, string query)
    {
        return (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

public sealed class VaultStore : IDisposable
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("FASTACC1");
    readonly string path;
    readonly byte[] salt;
    readonly byte[] encryptionKey;
    readonly byte[] authenticationKey;

    VaultStore(string path, string password, byte[] salt)
    {
        this.path = path;
        this.salt = salt;
        // 两个独立密钥分别用于加密和认证，主密码不写入文件。
        using (var derive = new Rfc2898DeriveBytes(password, salt, 200000))
        {
            encryptionKey = derive.GetBytes(32);
            authenticationKey = derive.GetBytes(32);
        }
    }

    static byte[] RandomBytes(int count)
    {
        byte[] result = new byte[count];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(result);
        return result;
    }

    public static VaultStore Create(string path, string password)
    {
        if (File.Exists(path)) throw new IOException("数据文件已存在，请解锁现有文件。");
        return new VaultStore(path, password, RandomBytes(16));
    }

    public static VaultStore Open(string path, string password, out List<Account> accounts)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 88 || (data.Length - 72) % 16 != 0)
            throw new CryptographicException("数据文件格式无效或已损坏。");
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) throw new CryptographicException("无法识别数据文件版本。");
        byte[] salt = new byte[16];
        Buffer.BlockCopy(data, 8, salt, 0, 16);
        var store = new VaultStore(path, password, salt);
        try
        {
            accounts = store.Decrypt(data);
            return store;
        }
        catch { store.Dispose(); throw; }
    }

    List<Account> Decrypt(byte[] data)
    {
        // 先验证包含文件头的认证码，认证通过后才进行解密。
        byte[] tag;
        using (var mac = new HMACSHA256(authenticationKey)) tag = mac.ComputeHash(data, 0, data.Length - 32);
        int difference = 0;
        for (int i = 0; i < 32; i++) difference |= tag[i] ^ data[data.Length - 32 + i];
        if (difference != 0) throw new CryptographicException("主密码错误，或数据文件已损坏/被修改。");
        byte[] iv = new byte[16];
        Buffer.BlockCopy(data, 24, iv, 0, 16);
        byte[] plain;
        using (var aes = Aes.Create())
        {
            aes.Key = encryptionKey; aes.IV = iv;
            using (var decryptor = aes.CreateDecryptor()) plain = decryptor.TransformFinalBlock(data, 40, data.Length - 72);
        }
        try
        {
            using (var stream = new MemoryStream(plain))
                return (List<Account>)new DataContractJsonSerializer(typeof(List<Account>)).ReadObject(stream);
        }
        finally { Array.Clear(plain, 0, plain.Length); }
    }

    public void Save(List<Account> accounts)
    {
        byte[] plain;
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(List<Account>)).WriteObject(stream, accounts);
            plain = stream.ToArray();
        }
        byte[] encrypted;
        byte[] iv = RandomBytes(16);
        try
        {
            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey; aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor()) encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            }
        }
        finally { Array.Clear(plain, 0, plain.Length); }
        string temporary = path + ".tmp";
        // 临时文件也只有密文；完整落盘后原子替换，失败时保留原文件。
        using (var stream = new MemoryStream())
        {
            stream.Write(Magic, 0, Magic.Length);
            stream.Write(salt, 0, salt.Length);
            stream.Write(iv, 0, iv.Length);
            stream.Write(encrypted, 0, encrypted.Length);
            byte[] content = stream.ToArray();
            byte[] tag;
            using (var mac = new HMACSHA256(authenticationKey)) tag = mac.ComputeHash(content);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(content, 0, content.Length);
                file.Write(tag, 0, tag.Length);
                file.Flush(true);
            }
        }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    public void Dispose()
    {
        Array.Clear(encryptionKey, 0, encryptionKey.Length);
        Array.Clear(authenticationKey, 0, authenticationKey.Length);
    }
}
