using System;
using System.IO;
using System.Windows.Forms;

namespace Ameow.GUI
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var app = new App(App.DefaultPathsProvider, App.FileLogger);
            if (app.OpenWallet() is true)
                runLogin(app);
            else
                runCreateWallet(app);
        }

        private static void runLogin(App app)
        {
            var loginForm = new LoginForm(app);
            Application.Run(loginForm);

            if (loginForm.FormResult == LoginForm.Result.LoggedIn)
            {
                runIbd(app);
            }
            else if (loginForm.FormResult == LoginForm.Result.CreatingWallet)
            {
                app.CloseWallet();
                runCreateWallet(app);
            }
        }

        private static void runCreateWallet(App app)
        {
            var createWalletForm = new CreateWalletForm(app);
            Application.Run(createWalletForm);

            if (createWalletForm.WalletCreated is true)
            {
                runLogin(app);
            }
        }

        private static void runIbd(App app)
        {
            var ibdForm = new IbdForm(app);
            Application.Run(ibdForm);

            if (ibdForm.WillContinue)
            {
                Application.Run(new MainForm(app));
                app.Shutdown();
            }
        }
    }
}