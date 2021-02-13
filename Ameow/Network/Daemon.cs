using Ameow.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ameow.Network
{
    /// <summary>
    /// Manages network activities, including listening for and connecting to remote peers.
    /// </summary>
    public sealed partial class Daemon
    {
        private readonly Mutex chainMutex;
        private readonly ChainManager chainManager;
        private readonly CancellationToken cancellationToken;
        private readonly App.ILogger logger;
        private readonly App.IInventoryNotifier inventoryNotifier;

        /// <summary>
        /// Nonce number to detect self-connection.
        /// See <see cref="MDVersion"/>.
        /// </summary>
        private readonly string nodeNonce;

        private readonly List<Context> peers;

        private readonly InitialBlockDownload ibd;

        private readonly object houseKeepingLock;

        private Server _server;

        public int PeerTimeoutSeconds { get; set; } = 60 * 10;

        public int PeerPingSeconds { get; set; } = 60 * 2;

        public bool IsListening => _server != null;

        public InitialBlockDownload.Phase CurrentIbdPhase => ibd.CurrentPhase;

        public Daemon(Mutex chainMutex, ChainManager chainManager, App.ILogger logger, App.IInventoryNotifier inventoryNotifier, CancellationToken cancellationToken)
        {
            this.chainMutex = chainMutex;
            this.chainManager = chainManager;
            this.logger = logger;
            this.inventoryNotifier = inventoryNotifier;
            this.cancellationToken = cancellationToken;

            nodeNonce = createNodeNonce();

            peers = new List<Context>();

            ibd = new InitialBlockDownload();

            houseKeepingLock = new object();

            runHouseKeeping();
        }

        public void Shutdown()
        {
            chainManager.Save();
        }

        private static string createNodeNonce()
        {
            var random = new Random();
            var bytes = new byte[32];
            for (int i = 0, c = bytes.Length; i < c; ++i)
            {
                bytes[i] = (byte)random.Next(256);
            }
            return HashUtils.SHA256(bytes);
        }

        /// <summary>
        /// Opens a TCP listener to accept remote connections.
        /// </summary>
        public void ListenToPeers(int port)
        {
            _server = new Server(port, logger);
            _server.OnMessageReceived += onMessageReceived;
            _server.OnClientConnected += onPeerConnected;
            _server.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Connects to a list of remote peers.
        /// This method will also prepare IBD progress.
        /// </summary>
        /// <returns>True if at least 1 peer has been connected.</returns>
        public bool AcceptPeerList(IList<(System.Net.IPAddress, int)> peerAddresses)
        {
            ibd.Prepare();

            var clients = new List<Client>();

            for (int i = 0, c = peerAddresses.Count; i < c; ++i)
            {
                var (host, port) = peerAddresses[i];
                var client = new Client(host.ToString(), port, logger);
                client.OnMessageReceived += onMessageReceived;
                bool isConnected = client.Connect();
                if (isConnected)
                {
                    client.StartAsync(cancellationToken);
                    clients.Add(client);
                    logger.Log(App.LogLevel.Info, "Connected to peer " + client.Context.ClientEndPoint);
                }
            }

            if (clients.Count > 0)
            {
                lock (houseKeepingLock)
                {
                    for (int i = 0, c = clients.Count; i < c; ++i)
                    {
                        var ctx = clients[i].Context;
                        if (ibd.HasPeer(ctx) is false)
                        {
                            ibd.AddPeer(ctx);
                            sendVersionMessage(ctx);
                        }
                        peers.Add(ctx);
                    }
                }

                logger.Log(App.LogLevel.Info, $"Connected to {clients.Count} peers.");
                return true;
            }
            else
            {
                ibd.Fail();
                logger.Log(App.LogLevel.Warning, "Cannot connect to any peers.");

                return false;
            }
        }

        /// <summary>
        /// Starts house keeping job in a separate thread.
        /// The job is to disconnect idle or misbehaving peers.
        /// </summary>
        private void runHouseKeeping()
        {
            Task.Run(async () =>
            {
                await Task.Delay(10_000);
                while (!cancellationToken.IsCancellationRequested)
                {
                    lock (houseKeepingLock)
                    {
                        var now = DateTime.Now;
                        for (int i = 0, c = peers.Count; i < c; ++i)
                        {
                            try
                            {
                                var peer = peers[i];

                                // We only have to check for remote peers that connected to our node.
                                if (peer.IsOutbound)
                                {
                                    var timeDiff = now - peer.LastMessageInTime;
                                    if (timeDiff.TotalSeconds > PeerTimeoutSeconds)
                                    {
                                        logger.Log(App.LogLevel.Info, $"Peer {peer.ClientEndPoint} has not communicated for {PeerTimeoutSeconds} seconds.");
                                        peer.ShouldDisconnect = true;
                                    }
                                }

                                if (peer.ShouldDisconnect)
                                {
                                    ibd.RemovePeer(peer);

                                    peer.Close();
                                    peers.RemoveAt(i);
                                    --i;
                                    --c;

                                    logger.Log(App.LogLevel.Info, $"Disconnected peer {peer.ClientEndPoint}.");
                                }
                                else
                                {
                                    var timeDiff = now - peer.LastPingTime;
                                    if (timeDiff.TotalSeconds >= PeerPingSeconds)
                                    {
                                        peer.LastPingTime = now;
                                        sendPingMessage(peer);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Log(App.LogLevel.Fatal, $"House keeping encountered an error: {ex.Message}\n{ex.StackTrace}.");
                            }
                        }
                    }

                    await Task.Delay(30_000);
                }
            });
        }

        public void Broadcast(Message msg)
        {
            broadcast(msg);
        }

        private void broadcast(Message msg, Context srcCtx = null)
        {
            lock (houseKeepingLock)
            {
                for (int i = 0, c = peers.Count; i < c; ++i)
                {
                    if (peers[i] == srcCtx) continue;
                    if (peers[i].ShouldDisconnect) continue;
                    peers[i].SendMessage(msg);
                }
            }
        }

        private void onPeerConnected(Context ctx)
        {
            logger.Log(App.LogLevel.Info, $"Peer connected: {ctx.ClientEndPoint}");
            lock (houseKeepingLock)
            {
                peers.Add(ctx);
            }
        }

        /// <summary>
        /// Chooses the next peer in list for IBD.
        /// </summary>
        private void tryNextIbdPeer()
        {
            if (ibd.NextPeer() is true)
            {
                var (ctx, _) = ibd.GetCurrentPeer();
                var range = ibd.CurrentGetBlocksRange();
                sendGetBlocksMessage(ctx, range.StartIndex, range.Count);
            }

            if (ibd.IsRunning && ibd.IsOutOfPeer())
            {
                ibd.Fail();
                logger.Log(App.LogLevel.Error, "Failed to run IBD. No appropriate peer.");
            }
        }

        /// <summary>
        /// Starts the Initial Block Download progress.
        /// </summary>
        private void startIbd()
        {
            ibd.Start();
            ibd.SortPeers();

            while (ibd.NextPeer() is true)
            {
                var (ctx, receivedLatestBlock) = ibd.GetCurrentPeer();

                chainMutex.WaitOne();
                var localLatestBlock = chainManager.LatestBlock;
                chainMutex.ReleaseMutex();

                // because peers are sorted,
                // if the block the peer sent is older or the same height
                // with local tip block then we are at the "alright" height
                // and need no more IBD
                if (receivedLatestBlock.Index <= localLatestBlock.Index)
                {
                    ibd.Succeed();
                    broadcast(createLatestBlockMessage(chainManager.LatestBlock), srcCtx: ctx);
                    break;
                }

                // peer has exactly 1 block ahead, try adding it
                if (receivedLatestBlock.Index == localLatestBlock.Index + 1)
                {
                    var blocksToAdd = ctx.GetStoredAndNewBlocks(receivedLatestBlock);

                    chainMutex.WaitOne();
                    var result = chainManager.AddBlocksFromPeer(blocksToAdd, ctx.LastHeight);
                    var resultType = result.Type;
                    chainMutex.ReleaseMutex();

                    if (resultType == ChainManager.AddBlocksResultType.Added_SingleBlock)
                    {
                        ctx.ClearStoredBlocks();

                        ibd.Succeed();
                        logger.Log(App.LogLevel.Info, "IBD finished. Height: " + chainManager.Height);

                        broadcast(createLatestBlockMessage(chainManager.LatestBlock), srcCtx: ctx);
                        break;
                    }
                    else if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks
                        || resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                    {
                        if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                            ctx.StoreReceivedBlocks(receivedLatestBlock);

                        sendGetBlocksMessage(ctx, result.RequestedStartIndex, Config.MaxGetBlocksCount);
                    }
                    else
                    {
                        continue;
                    }
                }

                // peer has more blocks to give, prepare a list of ranges to get
                ibd.PrepareGetBlocksRanges(localLatestBlock.Index, receivedLatestBlock.Index);

                var range = ibd.CurrentGetBlocksRange();
                if (range != null)
                {
                    sendGetBlocksMessage(ctx, range.StartIndex, range.Count);
                    break;
                }
                else
                {
                    // normally we shouldn't be here
                    // but if we were here, try another peer
                    continue;
                }
            }

            if (ibd.IsRunning && ibd.IsOutOfPeer())
            {
                ibd.Fail();
                logger.Log(App.LogLevel.Error, "Failed to run IBD. No appropriate peer.");
            }
        }
    }
}