using System;
using System.Windows.Forms;

namespace KGIOptionPricing
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Register CodePages for Big5 Encoding support needed by KGI API
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Enable BinaryFormatter for KGI APIs on modern .NET
            AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);

            // Handle legacy Thread.Abort calls inside TradeCom.dll / PushClient.dll
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) =>
            {
                if (e.Exception is PlatformNotSupportedException && e.Exception.Message.Contains("Thread abort"))
                {
                    return; // Ignore legacy Thread.Abort() in .NET Core / .NET 10
                }
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is PlatformNotSupportedException ex && ex.Message.Contains("Thread abort"))
                {
                    return; // Ignore legacy Thread.Abort()
                }
            };

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}