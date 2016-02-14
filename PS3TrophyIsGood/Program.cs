using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PS3TrophyIsGood {
    static class Program {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();
        /// <summary>
        /// 應用程式的主要進入點。
        /// </summary>
        [STAThread]
        static void Main() {
            // AllocConsole();
            // long tick = BitConverter.ToInt64("00E1951FAA626EC0".ToByteArray(), 0);
            // return;
            // Thread.CurrentThread.CurrentCulture = CultureInfo.CurrentCulture;
            // Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentCulture;
                
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
