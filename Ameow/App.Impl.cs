using Ameow.Utils;
using EllipticCurve;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ameow
{
    public sealed partial class App
    {
        public sealed class WalletOpenedException : Exception { }

        public sealed class WalletNotAvailableException : Exception { }

        public sealed class InvalidRecipientAddressException : Exception { }

        public sealed class SendAmountTooLowException : Exception { }

        public sealed class SendAmountTooHighException : Exception { }

        public sealed class SendToSelfException : Exception { }

        public sealed class ChainMutexLockedException : Exception { }

        public sealed class NotValidTimeForNewBlockException : Exception
        {
            public readonly long RemainingMilliseconds;

            public NotValidTimeForNewBlockException(long remainingMs)
            {
                RemainingMilliseconds = remainingMs;
            }
        }

        private sealed class CDefaultPathsProvider : IPathsProvider
        {
            string IPathsProvider.Wallet => Path.Combine(AppContext.BaseDirectory, "wallet.dat");

            string IPathsProvider.Peers => Path.Combine(AppContext.BaseDirectory, "peers.txt");
        }

        private sealed class CFileLogger : ILogger
        {
            void ILogger.Log(LogLevel level, string log)
            {
                var logFilePath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                using var logFileStream = File.Open(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var logWriter = new StreamWriter(logFileStream);
                logWriter.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                logWriter.Write(": [");
                logWriter.Write(level);
                logWriter.Write("] ");
                logWriter.WriteLine(log);
                logWriter.Flush();
            }
        }

        private sealed class CConsoleLogger : ILogger
        {
            void ILogger.Log(LogLevel level, string log)
            {
                if (Console.CursorLeft > 0)
                    Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("{0}: ", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Console.ForegroundColor = level switch
                {
                    LogLevel.Info => ConsoleColor.Gray,
                    LogLevel.Debug => ConsoleColor.White,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Fatal => ConsoleColor.DarkRed,
                    _ => ConsoleColor.Gray,
                };
                Console.Write("[{0}] ", level);
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(log);
            }
        }

        private sealed class CFileAndConsoleLogger : ILogger
        {
            void ILogger.Log(LogLevel level, string log)
            {
                FileLogger.Log(level, log);
                ConsoleLogger.Log(level, log);
            }
        }

        private sealed class InventoryNotifier : IInventoryNotifier
        {
            private readonly Func<PrivateKey> privateKeyGetter;
            private readonly Action<bool> callback;

            private PublicKey _cachedPublicKey;
            private string _cachedAddress;

            private PublicKey publicKey
            {
                get
                {
                    if (_cachedPublicKey == null)
                    {
                        var privateKey = privateKeyGetter();
                        if (privateKey != null)
                            _cachedPublicKey = privateKey.publicKey();
                    }
                    return _cachedPublicKey;
                }
            }

            private string address
            {
                get
                {
                    if (_cachedAddress == null)
                    {
                        var privateKey = privateKeyGetter();
                        if (privateKey != null)
                            _cachedAddress = AddressUtils.AddressFromPrivateKey(privateKey);
                    }
                    return _cachedAddress;
                }
            }

            public InventoryNotifier(Func<PrivateKey> privateKeyGetter, Action<bool> callback)
            {
                this.privateKeyGetter = privateKeyGetter;
                this.callback = callback;
            }

            void IInventoryNotifier.OnLatestBlock(Block block)
            {
                var publicKey = this.publicKey;
                var address = this.address;
                if (publicKey is null || address is null) return;

                if (hasRelatedAddress(block, publicKey, address))
                    callback(true);
                else
                    callback(false);
            }

            void IInventoryNotifier.OnBlocks(IList<Block> blocks)
            {
                var publicKey = this.publicKey;
                var address = this.address;
                if (publicKey is null || address is null) return;

                for (int i = 0, c = blocks.Count; i < c; ++i)
                {
                    if (hasRelatedAddress(blocks[i], publicKey, address))
                    {
                        callback(true);
                        break;
                    }
                }

                callback(false);
            }

            void IInventoryNotifier.OnMempool(IList<PendingTransaction> pendingTransactions)
            {
                var publicKey = this.publicKey;
                var address = this.address;
                if (publicKey is null || address is null) return;

                for (int i = 0, c = pendingTransactions.Count; i < c; ++i)
                {
                    if (hasRelatedAddress(pendingTransactions[i].Tx, publicKey, address))
                    {
                        callback(true);
                        break;
                    }
                }

                callback(false);
            }

            private static bool hasRelatedAddress(Block block, PublicKey publicKey, string address)
            {
                for (int i = 0, c = block.Transactions.Count; i < c; ++i)
                {
                    if (hasRelatedAddress(block.Transactions[i], publicKey, address))
                        return true;
                }

                return false;
            }

            private static bool hasRelatedAddress(Transaction tx, PublicKey publicKey, string address)
            {
                var inputs = tx.Inputs;
                for (int i = 0, c = inputs.Count; i < c; ++i)
                {
                    if (AddressUtils.TxInSignatureContainsPublicKey(inputs[i].Signature, publicKey))
                        return true;
                }

                var outputs = tx.Outputs;
                for (int i = 0, c = outputs.Count; i < c; ++i)
                {
                    if (outputs[i].Address == address)
                        return true;
                }

                return false;
            }
        }
    }
}