using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CBRE.Settings;

namespace CBRE.Editor
{
    public static class SingleInstance
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private static Mutex _appMutex;

        public static void Start(Type formType)
        {
            SettingsManager.Read();

            string mutexName = "Global\\CBRE-EX-Editor-Instance-Mutex";
            _appMutex = new Mutex(true, mutexName, out bool isNewInstance);

            if (!isNewInstance && CBRE.Settings.View.SingleInstance)
            {;
                HandleExistingInstance();
                return;
            }

            RunApplication(formType);
            GC.KeepAlive(_appMutex);
        }

        private static void RunApplication(Type formType)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string[] args = System.Environment.GetCommandLineArgs();
            Editor.ProcessArguments(args);

            using (var mainForm = (Form)Activator.CreateInstance(formType))
            {
                Application.Run(mainForm);
            }
        }

        private static void HandleExistingInstance()
        {
            var current = Process.GetCurrentProcess();
            var running = Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id);

            if (running != null && running.MainWindowHandle != IntPtr.Zero)
            {
                IntPtr handle = running.MainWindowHandle;

                if (IsIconic(handle))
                {
                    ShowWindowAsync(handle, SW_RESTORE);
                }
                else
                {
                    ShowWindowAsync(handle, SW_SHOW);
                }

                SetForegroundWindow(handle);
            }

            string[] args = System.Environment.GetCommandLineArgs();
            Editor.ProcessArguments(args);
        }
    }
}