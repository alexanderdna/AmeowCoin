using Ameow.Network;
using Ameow.Utils;
using EllipticCurve;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ameow
{
    /// <summary>
    /// Heart of the Ameow application.
    /// Wrapper of several other classes.
    /// Contains logic for listening to peers, connecting to peers,
    /// managing blocks and transactions, doing Initial Block Download,
    /// managing the wallet and sending coins to folks.
    /// </summary>
    public sealed partial class App
    {
        /// <summary>
        /// Interface for retrieving paths to necessary files.
        /// For simplicity, you can use <see cref="DefaultPathsProvider"/> instead of implementing a new class.
        /// </summary>
        public interface IPathsProvider
        {
            string Wallet { get; }
            string Peers { get; }
        }

        /// <summary>
        /// Interfaces for logging events in the app.
        /// For simplicity, you can use default loggers instead of implementing a new class.
        /// </summary>
        public interface ILogger
        {
            void Log(LogLevel level, string log);
        }

        /// <summary>
        /// Interface for notifying changes on the blockchain and mempool.
        /// This interface is internally implemented by App so you don't have to pay much attention to it.
        /// </summary>
        public interface IInventoryNotifier
        {
            void OnLatestBlock(Block block);
            void OnBlocks(IList<Block> blocks);
            void OnMempool(IList<PendingTransaction> pendingTransactions);
        }

        public enum LogLevel
        {
            Info,
            Debug,
            Warning,
            Error,
            Fatal,
        }

        public enum ConnectPeersResult
        {
            NoPeersFile,
            Success,
            Failure,
        }

        public static readonly IPathsProvider DefaultPathsProvider = new CDefaultPathsProvider();

        public static readonly ILogger FileLogger = new CFileLogger();
        public static readonly ILogger ConsoleLogger = new CConsoleLogger();
        public static readonly ILogger FileAndConsoleLogger = new CFileAndConsoleLogger();

        private readonly Mutex chainMutex;

        public readonly ChainManager ChainManager;
        public readonly CancellationTokenSource CancellationTokenSource;
        public readonly Daemon Daemon;

        public readonly IPathsProvider PathsProvider;
        public readonly ILogger Logger;

        private Wallet _wallet;
        private PrivateKey _walletPrivateKey;
        private string _walletAddress;

        public bool WalletOpened => _wallet != null;
        public bool WalletLoggedIn => _walletPrivateKey != null;
        public string WalletAddress => _walletAddress;

        public bool IsMining { get; private set; }
        public double MiningHashrate { get; private set; }
        public bool LastMiningSuccessful { get; private set; }

        public bool IsInIbd =>
            Daemon.CurrentIbdPhase is InitialBlockDownload.Phase.Preparing or InitialBlockDownload.Phase.Running;

        public event Action OnRelatedTransactionsReceived;

        public App(IPathsProvider pathsProvider, ILogger logger)
        {
            chainMutex = new Mutex();

            ChainManager = new ChainManager();
            ChainManager.Load();

            CancellationTokenSource = new CancellationTokenSource();

            var inventoryNotifier = new InventoryNotifier(
                () => _walletPrivateKey,
                isRelated =>
                {
                    if (isRelated)
                        OnRelatedTransactionsReceived?.Invoke();
                });

            Daemon = new Daemon(chainMutex, ChainManager, logger, inventoryNotifier, CancellationTokenSource.Token);

            this.PathsProvider = pathsProvider;
            this.Logger = logger;
        }

        public void Shutdown()
        {
            Daemon.Shutdown();
        }

        /// <summary>
        /// Opens and loads wallet into memory.
        /// </summary>
        /// <returns>True if wallet is loaded.</returns>
        /// <exception cref="WalletOpenedException"/>
        public bool OpenWallet()
        {
            if (_wallet is not null)
                throw new WalletOpenedException();

            try
            {
                _wallet = Wallet.TryOpen(PathsProvider.Wallet);
                return _wallet is not null;
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "Cannot open wallet: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Closes the current wallet, probably for creating a new one.
        /// </summary>
        public void CloseWallet()
        {
            if (_wallet is null)
                throw new WalletNotAvailableException();

            _wallet = null;
            _walletPrivateKey = null;
            _walletAddress = null;
        }

        /// <summary>
        /// Creates a new wallet and saves it to file.
        /// </summary>
        /// <exception cref="WalletOpenedException"/>
        public void CreateWallet(string passphrase)
        {
            if (_wallet is not null)
                throw new WalletOpenedException();

            var random = new Random((int)TimeUtils.MsSinceEpochToUtcNow());
            var secret = Wallet.GenerateSecret(random);

            _wallet = new Wallet(passphrase, secret);
            Wallet.Save(PathsProvider.Wallet, _wallet);
        }

        /// <summary>
        /// Logs in the opened wallet.
        /// </summary>
        /// <returns>True if login succeeds.</returns>
        /// <exception cref="WalletNotAvailableException">Wallet must be opened earlier.</exception>
        public bool LoginWallet(string passphrase)
        {
            if (_walletPrivateKey is not null)
                throw new WalletNotAvailableException();

            var secret = _wallet.TryGetSecret(passphrase);
            if (secret is null)
                return false;

            _walletPrivateKey = new PrivateKey(secret: secret);
            _walletAddress = AddressUtils.AddressFromPrivateKey(_walletPrivateKey);
            return true;
        }

        /// <summary>
        /// Starts listening to incoming connections.
        /// </summary>
        /// <exception cref="SocketException">Thrown by <see cref="TcpListener.Start"/>.</exception>
        public void Listen(int port)
        {
            Daemon.ListenToPeers(port);
        }

        /// <summary>
        /// Opens peers file and tries connecting to those.
        /// </summary>
        /// <returns>True if at least 1 peer has been connected.</returns>
        public ConnectPeersResult ConnectToPeers()
        {
            var peersFile = PathsProvider.Peers;
            if (File.Exists(peersFile) is false)
            {
                Logger.Log(LogLevel.Warning, "Cannot find peers file.");
                return ConnectPeersResult.NoPeersFile;
            }

            List<(IPAddress, int)> addresses = new();
            try
            {
                var lines = File.ReadAllLines(peersFile);
                for (int i = 0, c = lines.Length; i < c; ++i)
                {
                    if (lines[i] == "") continue;

                    int posOfColon = lines[i].LastIndexOf(':');
                    if (posOfColon < 0)
                    {
                        Logger.Log(LogLevel.Warning, "Peers file seems to have invalid addresses.");
                        continue;
                    }

                    var host = IPAddress.Parse(lines[i].Substring(0, posOfColon));
                    var port = int.Parse(lines[i].Substring(posOfColon + 1));
                    if (port <= 0 || port > 65535)
                    {
                        Logger.Log(LogLevel.Warning, "Peers file seems to have invalid addresses.");
                        continue;
                    }

                    addresses.Add((host, port));
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "Cannot connect to peers: " + ex.Message);
                return ConnectPeersResult.Failure;
            }

            return Daemon.AcceptPeerList(addresses) ? ConnectPeersResult.Success : ConnectPeersResult.Failure;
        }

        /// <summary>
        /// Starts a mining job in another thread.
        /// Caller should check <see cref="IsMining"/> and <see cref="LastMiningSuccessful"/> for results.
        /// </summary>
        /// <exception cref="WalletNotAvailableException"/>
        /// <exception cref="ChainMutexLockedException"/>
        /// <exception cref="NotValidTimeForNewBlockException"/>
        public void Mine()
        {
            if (WalletLoggedIn is false)
                throw new WalletNotAvailableException();

            if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
            {
                Logger.Log(LogLevel.Warning, "Cannot start mining: chainMutex is held too long.");
                throw new ChainMutexLockedException();
            }
            long remainingMs = ChainManager.GetRemainingMsToNewBlock();
            chainMutex.ReleaseMutex();

            if (remainingMs > 0)
                throw new NotValidTimeForNewBlockException(remainingMs);

            Task.Run(() =>
            {
                IsMining = true;

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    Logger.Log(LogLevel.Warning, "Cannot start mining: chainMutex is held too long.");
                    LastMiningSuccessful = false;
                    return;
                }

                var mining = ChainManager.PrepareMining(_walletAddress, nonceRange: 100_000);
                chainMutex.ReleaseMutex();

                var token = CancellationTokenSource.Token;
                while (token.IsCancellationRequested is false)
                {
                    bool isFound = mining.Attempt();

                    double hashMs = Pow.LastMiningMilliseconds;
                    MiningHashrate = 1000.0 / hashMs;

                    if (isFound || mining.IsExhausted)
                        break;
                }

                var foundBlock = mining.Block;
                if (foundBlock is not null)
                {
                    if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                    {
                        Logger.Log(LogLevel.Warning, "Cannot finish mining: chainMutex is held too long.");
                        LastMiningSuccessful = false;
                        return;
                    }

                    var isAdded = ChainManager.FinishMining();
                    chainMutex.ReleaseMutex();

                    if (isAdded is true)
                        Daemon.BroadcastLatestBlock();
                    else
                        foundBlock = null;
                }
                else
                {
                    ChainManager.CancelMining();
                }

                IsMining = false;
                LastMiningSuccessful = foundBlock is not null;
            });

            Thread.Sleep(100);
        }

        /// <summary>
        /// Returns unspent coins of the current wallet, in usable and pending amounts.
        /// </summary>
        /// <returns>
        /// Usable: coins in unspent transaction outputs.
        /// Pending: coins in pending transactions, including "change" from earlier send tasks.
        /// </returns>
        /// <exception cref="WalletNotAvailableException"/>
        public (long usable, long pending) GetUnspentAmountInNekoshi()
        {
            if (WalletLoggedIn is false)
                throw new WalletNotAvailableException();

            return ChainManager.GetUnspentAmountInNekoshi(_walletAddress);
        }

        /// <summary>
        /// Sends coins to the given address.
        /// </summary>
        /// <returns>Result of the task.</returns>
        /// <exception cref="WalletNotAvailableException"/>
        /// <exception cref="SendToSelfException"/>
        /// <exception cref="InvalidRecipientAddressException"/>
        /// <exception cref="SendAmountTooLowException"/>
        /// <exception cref="SendAmountTooHighException"/>
        public ChainManager.SendResult Send(string recipientAddress, long amountInNekoshi)
        {
            if (WalletLoggedIn is false)
                throw new WalletNotAvailableException();

            if (recipientAddress == _walletAddress)
                throw new SendToSelfException();

            if (AddressUtils.VerifyAddress(recipientAddress) is false)
                throw new InvalidRecipientAddressException();

            if (amountInNekoshi <= Config.FeeNekoshiPerTx)
                throw new SendAmountTooLowException();

            if (amountInNekoshi > Config.MaxSendableNekoshi)
                throw new SendAmountTooHighException();

            chainMutex.WaitOne();
            var sendResult = ChainManager.Send(_walletAddress, recipientAddress, amountInNekoshi, _walletPrivateKey);
            chainMutex.ReleaseMutex();

            if (sendResult.Error == ChainManager.SendResult.ErrorType.None)
            {
                Daemon.BroadcastTransaction(sendResult.TxId);
            }

            return sendResult;
        }
    }
}