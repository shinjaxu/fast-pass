using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.vault");
        string identity;
        using (var hash = SHA256.Create())
            identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant()))).Replace("-", "");
        bool first;
        // 同一目录只允许一个实例写入账户文件，防止两个窗口相互覆盖。
        using (var mutex = new Mutex(true, "Local\\FastAccount-" + identity, out first))
        {
            if (!first) { MessageBox.Show("快捷账户已经运行，请按 Ctrl+P 或双击托盘图标呼出。", "快捷账户"); return; }
            try
            {
                using (var unlock = new UnlockForm(path))
                {
                    if (unlock.ShowDialog() != DialogResult.OK) return;
                    using (var store = unlock.Store)
                    using (var main = new MainForm(store, unlock.Accounts))
                    {
                        // 使用无主窗口的消息循环，启动解锁后保持托盘静默状态。
                        Application.Run(new ApplicationContext());
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("无法启动快捷账户：" + ex.Message, "快捷账户", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
