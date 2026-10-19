using System;
using System.Windows.Forms;
using log4net;

[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]

namespace KGIOptionPricing
{
    internal static class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            log.Info("==================================================");
            log.Info("Application Start: KGI Option Pricing...");
            log.Info("==================================================");

            // Register CodePages for Big5 Encoding support needed by KGI API
            log.Info("Register CodePages");
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // TradeCom still depends on BinaryFormatter. The compatibility package
            // supplies the implementation on .NET 10; this switch explicitly enables it.
            AppContext.SetSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", true);
            using var displayFonts = Infrastructure.WindowsFontBootstrap.Load(message => log.Info(message));
            log.Info("Program Initialize");
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
