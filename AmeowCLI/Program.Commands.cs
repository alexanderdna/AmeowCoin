using Ameow.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace Ameow.CLI
{
    internal partial class Program
    {
        private class Command
        {
            public string Name { get; init; }
            public Action<string[]> Handler { get; init; }
            public string HelpText { get; init; }
        }

        private static readonly List<Command> commands = new()
        {
            new() { Name = "listen", Handler = cmd_listen, HelpText = "Start listening to remote connections." },
            new() { Name = "wallet", Handler = cmd_wallet, HelpText = "Log in your wallet or create new if wallet does not exist." },
            new() { Name = "mine", Handler = cmd_mine, HelpText = "Mine new block." },
            new() { Name = "bal", Handler = cmd_balance, HelpText = "Print out balance." },
            new() { Name = "balance", Handler = cmd_balance, HelpText = "Print out balance." },
            new() { Name = "address", Handler = cmd_address, HelpText = "Print out address." },
            new() { Name = "send", Handler = cmd_send, HelpText = "Send coins to a wallet address." },
            new() { Name = "print", Handler = cmd_print, HelpText = "Print out a range of blocks as JSON." },
            new() { Name = "print-tx", Handler = cmd_print_tx, HelpText = "Print out a transaction as JSON." },
            new() { Name = "help", Handler = cmd_help, HelpText = "Print out this help message." },
            new() { Name = "exit", Handler = cmd_exit, HelpText = "Exit the program." },
        };

        private static void cmd_listen(string[] args)
        {
            if (_app.Daemon.IsListening)
            {
                Console.WriteLine("Already listening.");
                return;
            }

            if (args.Length == 2)
            {
                if (int.TryParse(args[1], out int port))
                {
                    _app.Listen(port);
                }
                else
                {
                    cmd_help("listen");
                }
            }
            else
            {
                if (_options.ListeningPort > 0)
                    _app.Listen(_options.ListeningPort);
                else
                    Console.WriteLine("No port provided.");
            }
        }

        private static void cmd_wallet(string[] arr)
        {
            if (_app.WalletLoggedIn)
            {
                Console.WriteLine("You have logged in your wallet.");
                Console.WriteLine("Your address is {0}", _app.WalletAddress);
            }
            else
            {
                if (_app.OpenWallet() is true)
                    runLogin();
                else
                    runCreateWallet();
            }
        }

        private static void cmd_help(string[] arr)
        {
            if (arr.Length == 2)
            {
                cmd_help(arr[1]);
                return;
            }

            Console.WriteLine("Available commands:");
            for (int i = 0, c = commands.Count; i < c; ++i)
            {
                var cmd = commands[i];
                Console.WriteLine("\t{0,-20}{1}", cmd.Name, cmd.HelpText);
            }
            Console.WriteLine("Enter `help <command>` for more details.");
        }

        private static void cmd_help(string specificCmd)
        {
            if (specificCmd != null)
            {
                switch (specificCmd)
                {
                    case "mine":
                        Console.WriteLine("Will mine until a block is found.");
                        break;

                    case "address":
                        Console.WriteLine("Will print out your address.");
                        break;

                    case "bal":
                    case "balance":
                        Console.WriteLine("Will print out current balance.");
                        break;

                    case "send":
                        Console.WriteLine("Will send AMEOW to a chosen address.");
                        Console.WriteLine("\tsend <recipient> <amount>");
                        Console.WriteLine("\t\t<recipient> is recipient's address.");
                        Console.WriteLine("\t\t<amount> is amount of AMEOW to send.");
                        Console.WriteLine("Please note that <amount> doesn't include transaction fee of 0.5 AMEOW.");
                        break;

                    case "print":
                        Console.WriteLine("Will print out blocks in JSON format.");
                        Console.WriteLine("\tprint");
                        Console.WriteLine("\tprint <latest>");
                        Console.WriteLine("\tprint <from> <count>");
                        break;

                    case "print-tx":
                        Console.WriteLine("Will print out requested transaction in JSON format.");
                        Console.WriteLine("\tprint-tx <txid>");
                        Console.WriteLine("\t\t<txid> is transaction ID.");
                        break;

                    case "exit":
                        Console.WriteLine("Will stop the program.");
                        break;

                    default:
                        break;
                }
            }
        }

        private static void cmd_exit(string[] _)
        {
            _app.CancellationTokenSource.Cancel();
        }

        private static void cmd_mine(string[] _)
        {
            if (_app.WalletLoggedIn is false)
            {
                Console.WriteLine("You have not logged in your wallet.");
                Console.WriteLine("Enter `wallet` command to log in.");
                return;
            }

            try
            {
                _app.Mine();
            }
            catch (App.ChainMutexLockedException)
            {
                Console.WriteLine("Chain is being locked. Please wait a moment.");
                return;
            }
            catch (App.NotValidTimeForNewBlockException ex)
            {
                Console.WriteLine("Can only mine new block in {0:N2} seconds.", ex.RemainingMilliseconds / 1000.0);
                return;
            }

            while (_app.IsMining)
            {
                System.Threading.Thread.Sleep(500);
                Console.WriteLine("{0:N2} KH/s", _app.MiningHashrate / 1000);
            }

            if (_app.LastMiningSuccessful)
            {
                Console.WriteLine("\tDone.");

                (long usable, long pending) = _app.GetUnspentAmountInNekoshi();
                Console.WriteLine("\tBalance: {0}", (usable + pending) / 100_000_000.0);
            }
            else
            {
                Console.WriteLine("\tMining failed. Please check log for more details.");
            }
        }

        private static void cmd_print_tx(string[] arr)
        {
            if (arr.Length != 2)
            {
                cmd_help("print-tx");
                return;
            }

            var txId = arr[1];
            var tx = _app.ChainManager.GetTransaction(txId);
            if (tx != null)
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(tx, Newtonsoft.Json.Formatting.Indented);
                Console.WriteLine(json);
            }
            else
            {
                tx = _app.ChainManager.GetPendingTransaction(txId)?.Tx;
                if (tx != null)
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(tx, Newtonsoft.Json.Formatting.Indented);
                    Console.WriteLine(json);
                }
                else
                {
                    Console.WriteLine("Transaction not found.");
                }
            }
        }

        private static void cmd_address(string[] _)
        {
            if (_app.WalletLoggedIn is false)
            {
                Console.WriteLine("You have not logged in your wallet.");
                Console.WriteLine("Enter `wallet` command to log in.");
                return;
            }

            Console.WriteLine("Your address is: {0}", _app.WalletAddress);
        }

        private static void cmd_balance(string[] _)
        {
            if (_app.WalletLoggedIn is false)
            {
                Console.WriteLine("You have not logged in your wallet.");
                Console.WriteLine("Enter `wallet` command to log in.");
                return;
            }

            (long usable, long pending) = _app.GetUnspentAmountInNekoshi();
            Console.WriteLine("\tBalance: {0}", (usable + pending) / 100_000_000.0);
        }

        private static void cmd_send(string[] arr)
        {
            if (arr.Length != 3)
            {
                cmd_help("send");
                return;
            }

            if (_app.WalletLoggedIn is false)
            {
                Console.WriteLine("You have not logged in your wallet.");
                Console.WriteLine("Enter `wallet` command to log in.");
                return;
            }

            string recipientAddress = arr[1];
            if (recipientAddress == _app.WalletAddress)
            {
                Console.WriteLine("Recipient address seems to be your own address.");
                return;
            }

            if (!AddressUtils.VerifyAddress(recipientAddress))
            {
                Console.WriteLine("Recipient address seems to be invalid.");
                return;
            }

            if (!double.TryParse(arr[2], out double amount)
                || amount <= (Config.FeeNekoshiPerTx / (double)Config.NekoshiPerCoin)
                || amount > (Config.MaxSendableNekoshi / (double)Config.NekoshiPerCoin))
            {
                Console.WriteLine("Amount value is invalid.");
                return;
            }

            var sendResult = _app.Send(recipientAddress, (long)(amount * 100_000_000));
            if (sendResult.Error == ChainManager.SendResult.ErrorType.None)
            {
                Console.WriteLine("\tDone, TxId: {0}", sendResult.TxId);
            }
            else
            {
                Console.WriteLine("\tError: {0}", sendResult.Error);
            }
        }

        private static void cmd_print(string[] arr)
        {
            int from, count;
            if (arr.Length == 1)
            {
                count = 10;
                from = Math.Max(0, _app.ChainManager.Height - count + 1);
                Console.WriteLine(_app.ChainManager.ToDisplayString(from, count));
            }
            else if (arr.Length == 2)
            {
                if (int.TryParse(arr[1], out count))
                {
                    from = Math.Max(0, _app.ChainManager.Height - count + 1);
                    Console.WriteLine(_app.ChainManager.ToDisplayString(from, count));
                }
                else
                {
                    string blockHash = arr[1];
                    var block = _app.ChainManager.GetBlock(blockHash);
                    if (block != null)
                    {
                        Console.WriteLine(JsonConvert.SerializeObject(block, Formatting.Indented));
                    }
                    else
                    {
                        Console.WriteLine("Block not found.");
                    }
                }
            }
            else if (arr.Length == 3)
            {
                from = int.Parse(arr[1]);
                count = int.Parse(arr[2]);
                Console.WriteLine(_app.ChainManager.ToDisplayString(from, count));
            }
            else
            {
                return;
            }
        }
    }
}