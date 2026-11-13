using System;
using System.Windows.Forms;
using PhotoDeduplicatorGUI.UI;

namespace PhotoDeduplicatorGUI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
