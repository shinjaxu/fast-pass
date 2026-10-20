# 快捷账户

Windows 10 托盘账户工具，Ctrl+P 呼出/隐藏，主密码加密的本地文件，支持账户维护、域名/账户名/备注包含搜索、独立复制账户名与密码。

支持 CSV 导入/导出。导入按 `url,username,password,note` 表头识别，忽略额外列，列顺序不限，表头不区分大小写。`url`、`username`、`password` 任一为空或纯空白时跳过该行，`note` 可以为空；有效记录追加导入，不覆盖或去重，并显示导入及跳过数量。全部行被跳过时不改动现有数据。表头缺失或 CSV 格式错误仍整批失败。导出全部账户，使用 UTF-8 BOM 和标准 CSV 引号，密码为明文。

运行 `dist/FastAccount.exe`。使用与迁移说明见 `dist/使用说明.txt`。首次运行的账户文件为空，不包含测试账户或默认主密码。

## 构建与验证

在 Windows PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

无需 SDK、NuGet 或第三方依赖。脚本调用 Windows 自带 .NET Framework C# 编译器，生成 AnyCPU EXE，并运行核心与界面集成测试。界面测试使用临时生成的演示数据，测试结束清理测试文件。

源码：`src/Vault.cs` 负责加密持久化与搜索，`src/AccountCsv.cs` 负责 CSV 校验及读写，`src/Dialogs.cs` 负责解锁/维护，`src/MainForm.cs` 负责托盘、热键、列表与复制，`src/Program.cs` 负责启动和单实例。

## 数据格式

`accounts.vault`：8 字节版本标识 `FASTACC1`、16 字节随机盐、16 字节随机 IV、AES-256-CBC / PKCS7 密文、32 字节 HMAC-SHA256 标签。标签认证文件头及密文，解密前先验证标签。PBKDF2-HMAC-SHA1 200000 次产生两个 32 字节密钥，分别用于加密和认证。主密码不持久化。盐在创建文件时生成，IV 每次保存重新生成。

使用同目录加密临时文件及 `File.Replace` 替换已有文件；写入失败不更新界面内存记录。文件加密保护静态数据，解锁会在当前进程内保留记录及会话密钥。备份、迁移前应退出程序。
