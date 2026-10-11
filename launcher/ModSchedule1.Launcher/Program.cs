using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ModSchedule1.Launcher
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            try { if (File.Exists(exe + ".old")) File.Delete(exe + ".old"); } catch { }   // sobra da auto-atualização

            // Uma janela só. Após auto-atualizar, a versão nova espera a antiga fechar.
            using var mutex = new Mutex(false, "ModSchedule1Launcher");
            bool owned;
            try { owned = mutex.WaitOne(args.Contains("--updated") ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                MessageBox.Show("O launcher do ModSchedule1 já está aberto.", "ModSchedule1");
                return;
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            mutex.ReleaseMutex();
        }
    }
}
