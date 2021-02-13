using Ameow.Network;
using Ameow.Utils;
using CommandLine;
using System;
using System.Threading;

namespace Ameow.CLI
{
    internal partial class Program
    {
        private class CLOptions
        {
            [Option('p', "port", Required = false,
                Default = 0,
                HelpText = "Port to listen. Will run as node if provided. Node mode has no interactive interface.")]
            public int ListeningPort { get; set; }

            [Option("log", Required = false, Default = "file",
                HelpText = "Logging mode (file, console, both).")]
            public string LogMode { get; set; }

            public bool IsNode => ListeningPort > 0;
            public bool IsInteractive => ListeningPort == 0;
        }

        public static class LogMode
        {
            public const string File = "file";
            public const string Console = "console";
            public const string Both = "both";
        }

        private static App _app;
        private static App.ILogger _logger;

        private static CLOptions _options;

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<CLOptions>(args)
                .WithParsed(run);
        }

        private static void run(CLOptions options)
        {
            runMainLoop(options);
        }

        private static void runMainLoop(CLOptions options)
        {
            switch (options.LogMode)
            {
                case "console":
                    _logger = App.ConsoleLogger;
                    break;
                case "both":
                    _logger = App.FileAndConsoleLogger;
                    break;
                case "file":
                default:
                    _logger = App.FileLogger;
                    options.LogMode = LogMode.File;
                    break;
            }

            _app = new App(App.DefaultPathsProvider, _logger);

            _options = options;

            if (options.ListeningPort > 0)
            {
                _app.Listen(options.ListeningPort);
            }

            _app.ConnectToPeers();

            if (_app.IsInIbd)
            {
                ensureLogToConsole(App.LogLevel.Info, "Initial block download is running...");

                Console.Out.Flush();

                while (_app.IsInIbd)
                {
                    Thread.Sleep(100);
                }

                if (_app.Daemon.CurrentIbdPhase == InitialBlockDownload.Phase.Succeeded)
                {
                    ensureLogToConsole(App.LogLevel.Info, "Initial block download is finished.");
                    ensureLogToConsole(App.LogLevel.Info, $"Current height: {_app.ChainManager.Height}");
                }
                else
                {
                    ensureLogToConsole(App.LogLevel.Info, "Initial block download failed. Exitting...");

                    return;
                }
            }

            Console.CancelKeyPress += onSigInt;

            var token = _app.CancellationTokenSource.Token;
            if (options.IsNode)
            {
                while (!token.IsCancellationRequested)
                {
                    Thread.Sleep(500);
                }
            }
            else
            {
                Console.WriteLine("Welcome to ameow-cli. Enter `help` to show help message.");
                Console.WriteLine();

                var inputSeparator = new string[] { " " };
                while (!token.IsCancellationRequested)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(">>> ");
                    string input = Console.ReadLine();
                    Console.ForegroundColor = ConsoleColor.Gray;

                    if (input == null)
                    {
                        Thread.Sleep(100);
                        if (token.IsCancellationRequested)
                            break;
                    }

                    string[] args = input.Split(inputSeparator, StringSplitOptions.RemoveEmptyEntries);
                    if (args.Length == 0) continue;

                    string cmdName = args[0];
                    Command cmd = commands.Find(c => c.Name == cmdName);
                    if (cmd != null)
                        cmd.Handler(args);
                    else
                        cmd_help(args);
                }
            }

            try
            {
                _app.Shutdown();
            }
            catch (Exception ex)
            {
                _app.Logger.Log(App.LogLevel.Error, string.Format("Cannot shutdown properly: {0}", ex.Message));
            }
        }

        private static void ensureLogToConsole(App.LogLevel level, string log)
        {
            _logger.Log(level, log);

            if (_options.LogMode == LogMode.File)
                App.ConsoleLogger.Log(level, log);
        }

        private static void onSigInt(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _app.CancellationTokenSource.Cancel();
        }

        private static void runCreateWallet()
        {
            Console.WriteLine("Creating wallet.");
            Console.WriteLine("\tEsc to cancel, Backspace to reset, Enter to confirm.");

            var sb = StringBuilderPool.Acquire();
            while (true)
            {
                sb.Clear();

                Console.Write("\tPassphrase: ");
                var passphrase = readPassphrase(sb);

                if (passphrase == null)
                    break;

                if (passphrase.Length == 0)
                    continue;

                _app.CreateWallet(passphrase);

                Console.WriteLine("Wallet created.");
                break;
            }
            StringBuilderPool.Release(sb);

            runLogin();
        }

        private static void runLogin()
        {
            Console.WriteLine("Log-in wallet.");
            Console.WriteLine("\tEsc to cancel, Backspace to reset, Enter to confirm.");

            var sb = StringBuilderPool.Acquire();
            while (true)
            {
                Console.Write("\tPassphrase: ");
                var passphrase = readPassphrase(sb);

                if (passphrase == null)
                    break;

                if (passphrase.Length == 0)
                    continue;

                bool isLoggedIn = _app.LoginWallet(passphrase);
                if (isLoggedIn)
                    break;
                else
                    Console.WriteLine("Invalid passphrase. Please try again.");
            }
            StringBuilderPool.Release(sb);

            if (_app.WalletLoggedIn)
            {
                Console.WriteLine("Login successful.");
                Console.WriteLine("Your address is {0}", _app.WalletAddress);
            }
        }

        private static string readPassphrase(System.Text.StringBuilder sb)
        {
            sb.Clear();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Modifiers != 0) continue;

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return null;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    Console.WriteLine();
                    return string.Empty;
                }
                else if (key.KeyChar >= 20 && key.KeyChar < 256)
                {
                    Console.Write('*');
                    sb.Append(key.KeyChar);
                }
            }
            return sb.ToString();
        }
    }
}