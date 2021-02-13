using System;
using System.Collections.Generic;

namespace Ameow.Network
{
    public sealed partial class Daemon
    {
        /// <summary>
        /// Processes a message sent from a peer node.
        /// </summary>
        /// <param name="ctx">Context of the sender.</param>
        /// <param name="msg">Message to process.</param>
        private void onMessageReceived(Context ctx, Message msg)
        {
            if (!Message.VerifyChecksum(msg))
                return;

            if (msg.Type == MessageType.Version)
            {
                if (ctx.HasHandshake && ctx.Version > 0)
                {
                    // Version should be the first message a peer sends
                    // and it should be sent only once.
                    ctx.ShouldDisconnect = true;
                    return;
                }

                var md = MessageData.FromJson<MDVersion>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                // The node is connecting to itself.
                if (md.Nonce == nodeNonce)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                // Currently we decide to reject all nodes having lower version number.
                if (md.Version < Config.Version)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (ctx.HasHandshake is false)
                {
                    logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} version 0x{md.Version:x8} height {md.Height}.");

                    ctx.Version = md.Version;
                    ctx.UpdateHeightIfHigher(md.Height);

                    if (ctx.IsOutbound)
                        sendVersionMessage(ctx);
                    else
                        sendVersionAckMessage(ctx);
                }
                return;
            }

            // At this point, a Version message should have been sent by the peer.
            // Otherwise it is not behaving properly.
            if (ctx.Version == 0)
            {
                ctx.ShouldDisconnect = true;
                return;
            }

            if (msg.Type == MessageType.VersionAck)
            {
                var md = MessageData.FromJson<MDVersion>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (ctx.HasHandshake is false)
                {
                    ctx.HasHandshake = true;
                    sendVersionAckMessage(ctx);

                    if (ctx.IsOutbound is false)
                    {
                        logger.Log(App.LogLevel.Info, $"Successful handshake with {ctx.ClientEndPoint}. Sending GetLatestBlock.");

                        ibd.MarkPeerReady(ctx);
                        ibd.MarkRequestTime(ctx);
                        sendGetLatestBlockMessage(ctx);
                    }
                }
                return;
            }

            if (ctx.HasHandshake is false)
            {
                logger.Log(App.LogLevel.Warning, $"{ctx.ClientEndPoint} sent something before ver/verack. Marked for disconnection.");
                ctx.ShouldDisconnect = true;
                return;
            }

            if (msg.Type == MessageType.GetLatestBlock)
            {
                // Ignore this when IBD is not done
                if (ibd.IsDone is false)
                    return;

                var md = MessageData.FromJson<MDGetLatestBlock>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                    return;
                }

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent GetLatestBlock. Responding.");

                sendLatestBlockMessage(ctx, chainManager.LatestBlock);
                chainMutex.ReleaseMutex();

                return;
            }

            if (msg.Type == MessageType.LatestBlock)
            {
                var md = MessageData.FromJson<MDLatestBlock>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (md.Block == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (!ChainManager.ValidateBlockSanity(md.Block))
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent LatestBlock.");

                if (ibd.CurrentPhase == InitialBlockDownload.Phase.Preparing)
                {
                    ibd.ReceiveLatestBlock(ctx, md.Block);

                    // If all peers have sent LatestBlock, we'll proceed IBD
                    if (ibd.AllPeersResponded() is true)
                    {
                        logger.Log(App.LogLevel.Info, "All connected peers have sent latest blocks. Starting IBD.");

                        startIbd();
                    }
                }
                else
                {
                    var blocksToAdd = ctx.GetStoredAndNewBlocks(md.Block);
                    ctx.UpdateHeightIfHigher(blocksToAdd[^1].Index);

                    if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                    {
                        logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                        return;
                    }

                    var result = chainManager.AddBlocksFromPeer(blocksToAdd, ctx.LastHeight);
                    var resultType = result.Type;
                    chainMutex.ReleaseMutex();

                    if (resultType == ChainManager.AddBlocksResultType.NothingChanged
                        || resultType == ChainManager.AddBlocksResultType.Added_SingleBlock)
                    {
                        logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent latest block. Accepted. Height: {chainManager.Height}.");

                        inventoryNotifier.OnLatestBlock(md.Block);

                        ctx.ClearStoredBlocks();
                    }
                    else if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks
                        || resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                    {
                        if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                            ctx.StoreReceivedBlocks(md.Block);

                        logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent latest block. Too high. Sending GetBlocks to request lower blocks.");
                        sendGetBlocksMessage(ctx, chainManager.Height + 1, Config.MaxGetBlocksCount);
                    }
                    else
                    {
                        logger.Log(App.LogLevel.Warning, $"{ctx.ClientEndPoint} sent invalid blocks. Rejected and marked for disconnection.");

                        ctx.ShouldDisconnect = true;
                        return;
                    }

                    if (resultType == ChainManager.AddBlocksResultType.Added_SingleBlock
                        || resultType == ChainManager.AddBlocksResultType.Added_MultipleBlocks)
                    {
                        broadcast(createLatestBlockMessage(chainManager.LatestBlock), srcCtx: ctx);
                    }
                }

                return;
            }

            if (msg.Type == MessageType.GetBlocks)
            {
                // Ignore this when IBD is not done
                if (ibd.IsDone is false)
                    return;

                var md = MessageData.FromJson<MDGetBlocks>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (md.StartIndex < 0
                    || md.MaxCount < 1
                    || md.MaxCount > Config.MaxGetBlocksCount)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                    return;
                }

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent GetBlocks({md.StartIndex}, {md.MaxCount}). Responding.");

                var blocks = new List<Block>();
                chainManager.GetBlocks(md.StartIndex, md.MaxCount, blocks);
                chainMutex.ReleaseMutex();

                sendBlocksMessage(ctx, blocks);
                return;
            }

            if (msg.Type == MessageType.Blocks)
            {
                // When in IBD, we should ignore blocks from peers other than the current peer
                if (ibd.IsRunning && ibd.IsCommunicatingWith(ctx) is false)
                {
                    return;
                }

                var md = MessageData.FromJson<MDBlocks>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (md.Blocks == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                var blocksToAdd = ctx.GetStoredAndNewBlocks(md.Blocks);
                ctx.UpdateHeightIfHigher(blocksToAdd[^1].Index);

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent Blocks. Amount: {md.Blocks.Count}. Total: {blocksToAdd.Count}.");

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                    return;
                }

                var result = chainManager.AddBlocksFromPeer(blocksToAdd, ctx.LastHeight);
                var resultType = result.Type;
                chainMutex.ReleaseMutex();

                if (resultType == ChainManager.AddBlocksResultType.Added_MultipleBlocks
                    || resultType == ChainManager.AddBlocksResultType.Added_SingleBlock
                    || resultType == ChainManager.AddBlocksResultType.Replaced_MultipleBlocks)
                {
                    ctx.ClearStoredBlocks();

                    if (ibd.IsRunning)
                    {
                        ibd.ProceedToNextGetBlocksRange();
                        var range = ibd.CurrentGetBlocksRange();
                        if (range is not null)
                        {
                            logger.Log(App.LogLevel.Info, $"Accepted blocks. Sending GetBlocks({range.StartIndex}, {range.Count}).");

                            sendGetBlocksMessage(ctx, range.StartIndex, range.Count);
                        }
                        else
                        {
                            logger.Log(App.LogLevel.Info, $"IBD finished. Height: {chainManager.Height}.");

                            ibd.Succeed();

                            broadcast(createLatestBlockMessage(chainManager.LatestBlock), srcCtx: ctx);
                            sendGetMempoolMessage(ctx);
                        }
                    }
                    else
                    {
                        inventoryNotifier.OnBlocks(md.Blocks);

                        logger.Log(App.LogLevel.Info, $"Accepted. Height: {chainManager.Height}.");
                    }
                }
                else if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks
                    || resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                {
                    logger.Log(App.LogLevel.Info, $"Cannot accept yet. Sending GetBlocks{result.RequestedStartIndex}, {Config.MaxGetBlocksCount}) to request more.");

                    if (resultType == ChainManager.AddBlocksResultType.Need_MoreBlocks_ShouldStore)
                        ctx.StoreReceivedBlocks(md.Blocks);

                    sendGetBlocksMessage(ctx, result.RequestedStartIndex, Config.MaxGetBlocksCount);
                }
                else
                {
                    logger.Log(App.LogLevel.Warning, "Invalid blocks. Rejected and marked for disconnection.");

                    if (ibd.IsRunning)
                    {
                        ctx.ShouldDisconnect = true;
                        tryNextIbdPeer();
                    }
                    else
                    {
                        ctx.ShouldDisconnect = true;
                    }
                }

                return;
            }

            if (msg.Type == MessageType.GetMempool)
            {
                // Ignore this when IBD is not done
                if (ibd.IsDone is false)
                    return;

                var md = MessageData.FromJson<MDGetMempool>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                    return;
                }

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent GetMempool. Responding.");

                List<PendingTransaction> mempool = new List<PendingTransaction>();
                chainManager.GetPendingTransactions(mempool, Config.MaxPendingTxToSend);
                chainMutex.ReleaseMutex();

                sendMempoolMessage(ctx, mempool);
                return;
            }

            if (msg.Type == MessageType.Mempool)
            {
                // Ignore this when IBD is not done
                if (ibd.IsDone is false)
                    return;

                var md = MessageData.FromJson<MDMempool>(msg.Data);
                if (md == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                if (md.Transactions == null)
                {
                    ctx.ShouldDisconnect = true;
                    return;
                }

                logger.Log(App.LogLevel.Info, $"{ctx.ClientEndPoint} sent Mempool.");

                if (chainMutex.WaitOne(millisecondsTimeout: 3000) is false)
                {
                    logger.Log(App.LogLevel.Warning, "chainMutex was held too long.");
                    return;
                }

                var addResult = chainManager.AddPendingTransactions(md.Transactions);
                chainMutex.ReleaseMutex();

                if (addResult == ChainManager.AddPendingTransactionsResult.Added)
                {
                    logger.Log(App.LogLevel.Info, "Accepted. Broadcasting to other peers.");

                    inventoryNotifier.OnMempool(md.Transactions);

                    broadcast(createRelayedMempoolMessage(md), srcCtx: ctx);
                }
                else if (addResult == ChainManager.AddPendingTransactionsResult.HardRejected)
                {
                    logger.Log(App.LogLevel.Info, "Rejected. Marked for disconnection.");
                    ctx.ShouldDisconnect = true;
                }

                return;
            }

            if (msg.Type == MessageType.Ping)
            {
                sendPongMessage(ctx);
                return;
            }

            if (msg.Type == MessageType.Pong)
            {
                return;
            }
        }

        private void sendVersionMessage(Context ctx)
        {
            var msg = new Message
            {
                Type = MessageType.Version,
                Data = new MDVersion
                {
                    Version = Config.Version,
                    Height = chainManager.Height,
                    Nonce = nodeNonce,
                }.ToJson()
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendVersionAckMessage(Context ctx)
        {
            var msg = new Message
            {
                Type = MessageType.VersionAck,
                Data = new MDVersion().ToJson()
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendGetLatestBlockMessage(Context ctx)
        {
            var mdGetLatestBlock = new MDGetLatestBlock { };
            var msg = new Message
            {
                Type = MessageType.GetLatestBlock,
                Data = mdGetLatestBlock.ToJson(),
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendLatestBlockMessage(Context ctx, Block block)
        {
            var msg = createLatestBlockMessage(block);
            ctx.SendMessage(msg);
        }

        private static Message createLatestBlockMessage(Block block)
        {
            var mdLatestBlock = new MDLatestBlock
            {
                Block = block,
            };
            var msg = new Message
            {
                Type = MessageType.LatestBlock,
                Data = mdLatestBlock.ToJson(),
            };
            Message.CalculateChecksum(msg);
            return msg;
        }

        private static Message createRelayedMempoolMessage(MDMempool formerMD)
        {
            var md = new MDMempool
            {
                IsRelayed = true,
                Transactions = formerMD.Transactions,
            };
            var msg = new Message
            {
                Type = MessageType.Mempool,
                Data = md.ToJson(),
            };
            Message.CalculateChecksum(msg);
            return msg;
        }

        public void BroadcastLatestBlock()
        {
            chainMutex.WaitOne();
            var block = chainManager.LatestBlock;
            chainMutex.ReleaseMutex();

            broadcast(createLatestBlockMessage(block));
        }

        public void BroadcastTransaction(string txId)
        {
            chainMutex.WaitOne();
            var tx = chainManager.GetPendingTransaction(txId);
            chainMutex.ReleaseMutex();

            // tx has been added to a mined block?
            if (tx == null) return;

            var msg = new Message
            {
                Type = MessageType.Mempool,
                Data = new MDMempool
                {
                    IsRelayed = true,
                    Transactions = new List<PendingTransaction> { tx },
                }.ToJson(),
            };
            Message.CalculateChecksum(msg);
            broadcast(msg);
        }

        private static void sendGetBlocksMessage(Context ctx, int startIndex, int count)
        {
            var mdGetBlocks = new MDGetBlocks
            {
                StartIndex = startIndex,
                MaxCount = count,
            };
            var msg = new Message
            {
                Type = MessageType.GetBlocks,
                Data = mdGetBlocks.ToJson(),
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendBlocksMessage(Context ctx, List<Block> blocks)
        {
            var mdBlocks = new MDBlocks
            {
                Blocks = blocks,
            };
            var msg = new Message
            {
                Type = MessageType.Blocks,
                Data = mdBlocks.ToJson(),
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendGetMempoolMessage(Context ctx)
        {
            var msg = new Message
            {
                Type = MessageType.GetMempool,
                Data = new MDGetMempool { }.ToJson(),
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendMempoolMessage(Context ctx, List<PendingTransaction> mempool)
        {
            var mdMempool = new MDMempool
            {
                IsRelayed = false,
                Transactions = mempool,
            };
            var msg = new Message
            {
                Type = MessageType.Mempool,
                Data = mdMempool.ToJson(),
            };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendPingMessage(Context ctx)
        {
            var msg = new Message { Type = MessageType.Ping, Data = "" };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }

        private static void sendPongMessage(Context ctx)
        {
            var msg = new Message { Type = MessageType.Pong, Data = "" };
            Message.CalculateChecksum(msg);
            ctx.SendMessage(msg);
        }
    }
}