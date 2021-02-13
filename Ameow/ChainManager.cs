using Ameow.Storage;
using Ameow.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ameow
{
    /// <summary>
    /// Manages the blockchain.
    /// </summary>
    public sealed class ChainManager
    {
        public enum AddBlocksResultType
        {
            Empty,
            Added_SingleBlock,
            Added_MultipleBlocks,
            Replaced_MultipleBlocks,
            NothingChanged,
            Need_MoreBlocks,
            Need_MoreBlocks_ShouldStore,
            Rejected_InvalidSingleBlock,
            Rejected_InvalidMultipleBlocks,
            Rejected_ShorterChain,
        }

        public enum AddPendingTransactionsResult
        {
            Empty,
            Added,
            SoftRejected,
            HardRejected,
        }

        public class SendResult
        {
            public enum ErrorType
            {
                None,
                TooManyInputs,
                Insufficient,
                InvalidKey,
            }

            public ErrorType Error;
            public string TxId;
        }

        public sealed class AddBlocksResult
        {
            public readonly AddBlocksResultType Type;
            public readonly int RequestedStartIndex;

            private AddBlocksResult(AddBlocksResultType type)
            {
                Type = type;
            }

            private AddBlocksResult(AddBlocksResultType type, int requestStartIndex)
            {
                Type = type;
                RequestedStartIndex = requestStartIndex;
            }

            public static AddBlocksResult NeedMore(int requestStartIndex)
            {
                return new AddBlocksResult(AddBlocksResultType.Need_MoreBlocks, requestStartIndex);
            }

            public static AddBlocksResult NeedMore_ShoudlStore(int requestStartIndex)
            {
                return new AddBlocksResult(AddBlocksResultType.Need_MoreBlocks_ShouldStore, requestStartIndex);
            }

            public static implicit operator AddBlocksResult(AddBlocksResultType type)
            {
                return new AddBlocksResult(type);
            }
        }

        private struct TxOutPointer
        {
            public string TxId;
            public int TxOutIndex;
        }

        private readonly BlockDb blockDb;
        private readonly TransactionDb transactionDb;

        private BlockMining _currentMiningProgress = null;

        public Block LatestBlock => blockDb.LatestBlock;

        public int Height => blockDb.LatestBlock.Index;

        public ChainManager()
        {
            blockDb = new BlockDb();
            transactionDb = new TransactionDb(blockDb);
        }

        public bool Load()
        {
            return blockDb.Load() && transactionDb.Load();
        }

        public bool Save()
        {
            return blockDb.Save() && transactionDb.Save();
        }

        /// <summary>
        /// Returns remaining milliseconds until a new block can be generated.
        /// Returns 0 if no waiting is needed.
        /// </summary>
        public long GetRemainingMsToNewBlock()
        {
            int index = LatestBlock.Index + 1;
            long prevTimestamp = LatestBlock.Timestamp;
            long newBlockTimestamp = prevTimestamp + Config.CalculateBlockTimestampMinDistance(index);
            long now = TimeUtils.MsSinceEpochToUtcNow();
            if (newBlockTimestamp <= now)
                return 0;
            else
                return newBlockTimestamp - now;
        }

        /// <summary>
        /// Creates a block containing suitable pending transactions and a mining helper object.
        /// </summary>
        /// <param name="minerAddress">Wallet address of the miner to be added to coinbase transaction.</param>
        /// <param name="nonceRange">Range of nonce values to try in each call to <see cref="BlockMining.Attempt"/>.</param>
        /// <returns>The mining helper.</returns>
        public BlockMining PrepareMining(string minerAddress, int nonceRange = 100_000)
        {
            if (_currentMiningProgress != null) throw new InvalidOperationException("Mining in progress");

            var block = new Block
            {
                Index = LatestBlock.Index + 1,
                Timestamp = TimeUtils.MsSinceEpochToUtcNow(),
                PreviousBlockHash = LatestBlock.Hash,
            };

            transactionDb.CollectPendingTransactionsForBlock(block, minerAddress);

            _currentMiningProgress = new BlockMining(block, nonceRange);
            return _currentMiningProgress;
        }

        /// <summary>
        /// Finishes the current mining progress.
        /// If a valid block has been mined, adds it to the chain.
        /// </summary>
        /// <returns>True if a valid block has been mined and added.</returns>
        public bool FinishMining()
        {
            if (_currentMiningProgress == null) throw new InvalidOperationException("No mining is progress");

            var receivedTxMap = new Dictionary<string, Transaction>();
            var spentTxOutputs = new HashSet<TxOutPointer>();
            bool isValid = validateBlock(_currentMiningProgress.Block, LatestBlock, transactionDb, receivedTxMap, spentTxOutputs);
            if (isValid is false)
            {
                _currentMiningProgress.Dispose();
                _currentMiningProgress = null;
                return false;
            }

            addNewBlock(_currentMiningProgress.Block);
            _currentMiningProgress.Dispose();
            _currentMiningProgress = null;

            return true;
        }

        /// <summary>
        /// Cancels the current mining progress without checking for any blocks.
        /// </summary>
        public void CancelMining()
        {
            if (_currentMiningProgress == null) throw new InvalidOperationException("No mining is progress");

            _currentMiningProgress.Dispose();
            _currentMiningProgress = null;
        }

        /// <summary>
        /// Collects UTxO's of the given address.
        /// </summary>
        /// <seealso cref="TransactionDb.CollectUnspentTxOuts"/>
        private void getUnspentTxOutputs(string address, List<UnspentTxOut> resultContainer, List<TxOut> pendingTxOutContainer)
        {
            transactionDb.CollectUnspentTxOuts(address, resultContainer, pendingTxOutContainer);
        }

        /// <summary>
        /// Adds the given pending transactions to the transaction database.
        /// </summary>
        /// <returns>Result of the task.</returns>
        public AddPendingTransactionsResult AddPendingTransactions(IList<PendingTransaction> transactions)
        {
            if (transactions.Count == 0)
                return AddPendingTransactionsResult.Empty;

            for (int i = 0, c = transactions.Count; i < c; ++i)
            {
                var ptx = transactions[i];
                var tx = ptx.Tx;

                if (tx.Id != tx.GetId())
                    return AddPendingTransactionsResult.HardRejected;

                if (transactionDb.HasTransaction(tx.Id))
                    continue;

                if (transactionDb.GetPendingTransaction(tx.Id) != null)
                    continue;

                long totalInAmount = 0;
                long totalOutAmount = 0;
                bool isIgnored = false;

                for (int j = 0, n = tx.Inputs.Count; j < n; ++j)
                {
                    var txIn = tx.Inputs[j];

                    if (string.IsNullOrEmpty(txIn.TxId)
                        || txIn.TxOutIndex < 0
                        || string.IsNullOrEmpty(txIn.Signature))
                        return AddPendingTransactionsResult.HardRejected;

                    var refTx = transactionDb.GetTransaction(txIn.TxId);
                    if (refTx is null)
                    {
                        isIgnored = true;
                        break;
                    }

                    if (txIn.TxOutIndex >= refTx.Outputs.Count)
                        return AddPendingTransactionsResult.HardRejected;

                    var refTxOut = refTx.Outputs[txIn.TxOutIndex];

                    var (signature, publicKey) = AddressUtils.SignatureAndPublicKeyFromTxInSignature(txIn.Signature);
                    if (AddressUtils.AddressFromPublicKey(publicKey) != refTxOut.Address)
                        return AddPendingTransactionsResult.HardRejected;

                    if (EllipticCurve.Ecdsa.verify(tx.Id, signature, publicKey) == false)
                        return AddPendingTransactionsResult.HardRejected;

                    totalInAmount += refTxOut.AmountInNekoshi;
                }

                if (isIgnored) continue;

                for (int j = 0, n = tx.Outputs.Count; j < n; ++j)
                {
                    var txOut = tx.Outputs[j];

                    if (string.IsNullOrEmpty(txOut.Address)
                        || txOut.AmountInNekoshi <= 0)
                        return AddPendingTransactionsResult.HardRejected;

                    totalOutAmount += txOut.AmountInNekoshi;
                }

                if (totalInAmount != totalOutAmount + Config.FeeNekoshiPerTx)
                    return AddPendingTransactionsResult.HardRejected;
            }

            for (int i = 0, c = transactions.Count; i < c; ++i)
            {
                var ptx = transactions[i];
                transactionDb.AddPendingTransaction(ptx, needSave: false);
            }

            transactionDb.Save();

            return AddPendingTransactionsResult.Added;
        }

        /// <summary>
        /// Creates a transaction for sending coins from the given wallet to the given address.
        /// </summary>
        /// <param name="senderAddress">Address of the sender wallet.</param>
        /// <param name="recipientAddress">Address of the recipient wallet.</param>
        /// <param name="amountInNekoshi">Amount of nekoshi to send, not including transaction fee.</param>
        /// <param name="privateKey">Private key of the sender wallet.</param>
        /// <returns>Result of the task.</returns>
        /// <remarks>
        /// If the task succeeds, a pending transaction will be added to the mempool.
        /// The application is responsible for broadcasting the pending transaction
        /// to other nodes.
        /// </remarks>
        public SendResult Send(string senderAddress, string recipientAddress, long amountInNekoshi, EllipticCurve.PrivateKey privateKey)
        {
            var addrFromPrivateKey = AddressUtils.AddressFromPrivateKey(privateKey);
            if (senderAddress != addrFromPrivateKey) return new SendResult { Error = SendResult.ErrorType.InvalidKey };

            long amountInNekoshiToSend = amountInNekoshi;
            amountInNekoshi += Config.FeeNekoshiPerTx;

            List<UnspentTxOut> unspentTxOutputs = new List<UnspentTxOut>();
            List<TxOut> pendingTxOutputs = new List<TxOut>();
            getUnspentTxOutputs(senderAddress, unspentTxOutputs, pendingTxOutputs);

            long accumulatedAmount = 0;
            List<UnspentTxOut> ouputsToSpend = new List<UnspentTxOut>();
            List<TxOut> changeOutputs = new List<TxOut>();
            for (int i = 0, c = unspentTxOutputs.Count; i < c; ++i)
            {
                var utxo = unspentTxOutputs[i];

                ouputsToSpend.Add(utxo);

                var refTx = transactionDb.GetTransaction(utxo.TxId);
                var txOut = refTx.Outputs[utxo.TxOutIndex];

                long neededAmount = amountInNekoshi - accumulatedAmount;
                if (txOut.AmountInNekoshi > neededAmount)
                {
                    accumulatedAmount = amountInNekoshi;
                    changeOutputs.Add(new TxOut
                    {
                        Address = senderAddress,
                        AmountInNekoshi = txOut.AmountInNekoshi - neededAmount,
                    });
                }
                else
                {
                    accumulatedAmount += txOut.AmountInNekoshi;
                }

                if (accumulatedAmount >= amountInNekoshi)
                {
                    break;
                }
            }

            if (accumulatedAmount >= amountInNekoshi)
            {
                if (ouputsToSpend.Count > Config.MaxTransactionInputs)
                    return new SendResult { Error = SendResult.ErrorType.TooManyInputs };

                var tx = new Transaction();
                for (int i = 0, c = ouputsToSpend.Count; i < c; ++i)
                {
                    var unspentTxOut = ouputsToSpend[i];
                    var txIn = new TxIn
                    {
                        TxId = unspentTxOut.TxId,
                        TxOutIndex = unspentTxOut.TxOutIndex,
                    };
                    tx.Inputs.Add(txIn);
                }
                changeOutputs.Add(new TxOut
                {
                    Address = recipientAddress,
                    AmountInNekoshi = amountInNekoshiToSend,
                });
                tx.Outputs.AddRange(changeOutputs);
                tx.Id = tx.GetId();
                signTransation(tx, privateKey);
                transactionDb.AddPendingTransaction(tx);
                return new SendResult
                {
                    Error = SendResult.ErrorType.None,
                    TxId = tx.Id,
                };
            }
            else
            {
                return new SendResult { Error = SendResult.ErrorType.Insufficient };
            }
        }

        /// <summary>
        /// Signs the given transaction with the given private key.
        /// The result signature will be assigned to all inputs of the transaction.
        /// </summary>
        private static void signTransation(Transaction tx, EllipticCurve.PrivateKey privateKey)
        {
            var sig = EllipticCurve.Ecdsa.sign(tx.Id, privateKey);
            var sigStr = AddressUtils.TxInSignatureFromSignatureAndPublicKey(sig, privateKey.publicKey());

            for (int i = 0, c = tx.Inputs.Count; i < c; ++i)
            {
                tx.Inputs[i].Signature = sigStr;
            }
        }

        /// <summary>
        /// Calculates and returns the unspent coins of a wallet.
        /// </summary>
        /// <param name="address">Address of the wallet.</param>
        /// <returns>
        /// Usable: coins in UTxO's.
        /// Pending: coins in pending transactions, including "change" from earlier send tasks.
        /// </returns>
        public (long usable, long pending) GetUnspentAmountInNekoshi(string address)
        {
            long usable = 0;
            long pending = 0;
            List<UnspentTxOut> unspentTxOutputs = new List<UnspentTxOut>();
            List<TxOut> pendingTxOutputs = new List<TxOut>();
            getUnspentTxOutputs(address, unspentTxOutputs, pendingTxOutputs);

            for (int i = 0, c = unspentTxOutputs.Count; i < c; ++i)
            {
                var utxo = unspentTxOutputs[i];
                var refTx = transactionDb.GetTransaction(utxo.TxId);
                var txOut = refTx.Outputs[utxo.TxOutIndex];
                usable += txOut.AmountInNekoshi;
            }
            for (int i = 0, c = pendingTxOutputs.Count; i < c; ++i)
            {
                var txOut = pendingTxOutputs[i];
                pending += txOut.AmountInNekoshi;
            }
            return (usable, pending);
        }

        /// <summary>
        /// Finds and returns a transaction having the given ID.
        /// </summary>
        /// <returns>The found transaction or null if not found.</returns>
        public Transaction GetTransaction(string id)
        {
            return transactionDb.GetTransaction(id);
        }

        /// <summary>
        /// Finds and returns a pending transaction having the given ID.
        /// </summary>
        /// <returns>The found transaction or null if not found.</returns>
        public PendingTransaction GetPendingTransaction(string id)
        {
            return transactionDb.GetPendingTransaction(id);
        }

        /// <summary>
        /// Collects a number of pending transactions.
        /// </summary>
        /// <param name="container">Call-site-provided container for collected transactions.</param>
        /// <param name="maxCount">Maximum number of pending transactions to collect.</param>
        /// <remarks>
        /// The pending transactions will be sorted by time before being collected.
        /// See <see cref="TransactionDb.GetPendingTransactions(List{PendingTransaction}, int)"/>.
        /// </remarks>
        public void GetPendingTransactions(List<PendingTransaction> container, int maxCount)
        {
            transactionDb.GetPendingTransactions(container, maxCount);
        }

        /// <summary>
        /// Reeturns a string representation of <paramref name="count"/> blocks having indices starting from <paramref name="fromBlock"/>.
        /// </summary>
        public string ToDisplayString(int fromBlock, int count)
        {
            var sb = StringBuilderPool.Acquire();
            for (int i = Math.Max(0, fromBlock), c = Math.Min(fromBlock + count, blockDb.BlockCount); i < c; ++i)
            {
                sb.AppendLine(JsonConvert.SerializeObject(blockDb.GetBlock(i), Formatting.Indented));
            }
            return StringBuilderPool.GetStringAndRelease(sb);
        }

        /// <summary>
        /// Finds and returns the block having the given hash.
        /// </summary>
        /// <returns>The found block or null if not found.</returns>
        public Block GetBlock(string hash)
        {
            return blockDb.GetBlock(hash);
        }

        /// <summary>
        /// Collects a number of blocks.
        /// </summary>
        /// <param name="startIndex">Index of the first block.</param>
        /// <param name="maxCount">Maximum number of blocks to collect.</param>
        /// <param name="container">Call-site-provided container for collected blocks.</param>
        /// <returns>True if all required blocks can be retrieved.</returns>
        public bool GetBlocks(int startIndex, int maxCount, List<Block> container)
        {
            for (int i = startIndex, c = startIndex + maxCount, h = Height; i < c && i <= h; ++i)
            {
                var block = blockDb.GetBlock(i);
                if (block == null)
                    return false;

                container.Add(block);
            }

            return true;
        }

        /// <summary>
        /// Adds blocks received from a remote peer.
        /// </summary>
        /// <param name="blocks">The received blocks.</param>
        /// <param name="peerHeight">Chain height of the remote peer.</param>
        /// <remarks>
        /// This method will detect conflicts if any and validate the blocks
        /// to determine if they can be added immediately to the chain or not.
        /// </remarks>
        public AddBlocksResult AddBlocksFromPeer(IList<Block> blocks, int peerHeight)
        {
            if (blocks.Count == 0)
                return AddBlocksResultType.Empty;

            if (blocks[0].Index > LatestBlock.Index + 1)
            {
                // Received blocks are too high.
                // Request blocks starting from local height + 1.
                return AddBlocksResult.NeedMore(LatestBlock.Index + 1);
            }

            if (blocks.Count == 1)
            {
                var localTxMap = transactionDb;
                var receivedTxMap = new Dictionary<string, Transaction>();
                var spentTxOutputs = new HashSet<TxOutPointer>();

                var newBlock = blocks[0];
                if (newBlock.Index == LatestBlock.Index && newBlock.Equals(LatestBlock))
                {
                    return AddBlocksResultType.NothingChanged;
                }
                else if (newBlock.Index == LatestBlock.Index + 1)
                {
                    if (newBlock.PreviousBlockHash != LatestBlock.Hash)
                    {
                        // Conflict:
                        //   Peer has only 1 block ahead and it's not synced with local latest block.
                        // Solution:
                        //   Request earlier blocks from peer, if the responded blocks are valid then we can update the chain.
                        return AddBlocksResult.NeedMore_ShoudlStore(Math.Max(0, LatestBlock.Index - Config.ConflictResolutionSteps));
                    }
                }

                bool isValidBlock = validateBlock(newBlock, LatestBlock, localTxMap, receivedTxMap, spentTxOutputs);

                if (isValidBlock)
                {
                    addNewBlock(newBlock);
                    return AddBlocksResultType.Added_SingleBlock;
                }
                else
                {
                    return AddBlocksResultType.Rejected_InvalidSingleBlock;
                }
            }

            // Sanity check for index order.
            for (int i = 1, c = blocks.Count; i < c; ++i)
            {
                if (blocks[i].Index != blocks[i - 1].Index + 1)
                    return AddBlocksResultType.Rejected_InvalidMultipleBlocks;
            }

            if (blocks[0].Index == LatestBlock.Index + 1 && blocks[0].PreviousBlockHash == LatestBlock.Hash)
            {
                // Received blocks seem to be extended from current tip.
                // We must validate all of them before adding them to current chain.

                var prevBlock = LatestBlock;
                var localTxMap = transactionDb;
                var receivedTxMap = new Dictionary<string, Transaction>();
                var spentTxOutputs = new HashSet<TxOutPointer>();
                for (int i = 0, c = blocks.Count; i < c; ++i)
                {
                    var block = blocks[i];
                    var isValidBlock = validateBlock(block, prevBlock, localTxMap, receivedTxMap, spentTxOutputs);
                    if (!isValidBlock)
                        return AddBlocksResultType.Rejected_InvalidMultipleBlocks;

                    prevBlock = block;
                }

                // If we reach here, that means the received blocks are all valid.
                // We can add them to the chain.
                addNewBlocks(blocks);

                return AddBlocksResultType.Added_MultipleBlocks;
            }
            else if (blocks[^1].Index <= LatestBlock.Index)
            {
                if (peerHeight > LatestBlock.Index)
                {
                    // Received blocks are lower than local chain but peer's chain tip is higher.
                    // That means we have recently requested blocks from the past to solve a conflict.
                    // We'll store received blocks and request next sequence of blocks.
                    return AddBlocksResult.NeedMore_ShoudlStore(blocks[^1].Index + 1);
                }
                else
                {
                    // Peer's chain is lower than local.
                    // We'll reject it now and send LatestBlock later to help it sync.
                    return AddBlocksResultType.Rejected_ShorterChain;
                }
            }
            else
            {
                // In this branch, we have received a sequence of blocks,
                // of which the highest one is higher than local chain tip.

                var receivedTxMap = new Dictionary<string, Transaction>();
                var spentTxOutputs = new HashSet<TxOutPointer>();

                Block prevBlock = null;
                int receivedStartIndex = 0;
                int idx = 0;

                // Sequence of received blocks start from somewhere in the chain.
                // Problem is we are not sure yet if there was any blocks
                // in the sequence that started to diverge from what we have
                // in our local chain.

                int localHeight = LatestBlock.Index;

                for (int i = 0, c = blocks.Count; i < c; ++i)
                {
                    var receivedBlock = blocks[i];
                    if (receivedBlock.Index > localHeight)
                    {
                        // Block is ahead of local chain.

                        if (receivedBlock.PreviousBlockHash != LatestBlock.Hash)
                        {
                            // Conflict:
                            //   Received block at latest+1 points back to a different latest block.
                            // Solution:
                            //   Since peer's height is higher, we should update local chain to peer's chain.
                            //   We'll store received blocks and request earlier blocks.
                            return AddBlocksResult.NeedMore_ShoudlStore(Math.Max(0, localHeight - Config.ConflictResolutionSteps));
                        }
                        else
                        {
                            // This block points back to our local latest block.
                            // Let's just accept it temporarily.
                            prevBlock = LatestBlock;
                            receivedStartIndex = receivedBlock.Index;
                            idx = i;

                            break;
                        }
                    }
                    else
                    {
                        // Block is within local chain's range.

                        var localBlock = blockDb.GetBlock(receivedBlock.Index);
                        if (receivedBlock.Equals(localBlock))
                        {
                            // Same block. Nothing to worry. Just move on.
                            prevBlock = receivedBlock;
                            receivedStartIndex = receivedBlock.Index + 1;
                            idx = i + 1;
                        }
                        else
                        {
                            if (i == 0)
                            {
                                // Difference occured at the very first of the received sequence.

                                if (receivedBlock.Index == 0)
                                {
                                    // We went all the way back to Genesis.
                                    // So let's see if received block really is Genesis.
                                    if (receivedBlock.Equals(Config.GetGenesisBlock()))
                                    {
                                        if (blocks.Count == 1)
                                        {
                                            return AddBlocksResultType.NothingChanged;
                                        }
                                        else
                                        {
                                            // So, our local "Genesis" is not a real Genesis.
                                            // We'll update our chain with received blocks.
                                            prevBlock = blocks[0];
                                            receivedStartIndex = blocks[1].Index;
                                            idx = i + 1;
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        // Fake Genesis, reject it.
                                        return AddBlocksResultType.Rejected_InvalidMultipleBlocks;
                                    }
                                }
                                else
                                {
                                    // We are in the middle of the chain.
                                    // Chances are previous blocks in peer's data are different too.
                                    // Request them to see.
                                    return AddBlocksResult.NeedMore_ShoudlStore(Math.Max(0, receivedBlock.Index - Config.ConflictResolutionSteps));
                                }
                            }
                            else
                            {
                                // i > 0 means previous blocks matched.
                                // We can start updating local chain from this block onward.

                                prevBlock = blocks[i - 1];
                                receivedStartIndex = blocks[i].Index;
                                idx = i;

                                break;
                            }
                        }
                    }
                }

                for (int c = blocks.Count; idx < c; ++idx)
                {
                    var block = blocks[idx];

                    var isValidBlock = validateBlock(block, prevBlock, transactionDb, receivedTxMap, spentTxOutputs);
                    if (!isValidBlock)
                        return AddBlocksResultType.Rejected_InvalidMultipleBlocks;

                    prevBlock = block;
                }

                // If we reach here, all new blocks are validated.
                // We are ready to replace the blocks.

                replaceBlocks(blockDb, blocks, receivedStartIndex, transactionDb);

                return AddBlocksResultType.Replaced_MultipleBlocks;
            }
        }

        /// <summary>
        /// Adds a new block to the chain.
        /// </summary>
        private void addNewBlock(Block block)
        {
            blockDb.AddBlock(block);

            acceptBlockTransactions(block);
        }

        /// <summary>
        /// Adds a number of new blocks to the chain.
        /// </summary>
        private void addNewBlocks(IList<Block> blocks)
        {
            for (int i = 0, c = blocks.Count; i < c; ++i)
            {
                var block = blocks[i];
                blockDb.AddBlock(block, needSave: false);
                acceptBlockTransactions(block);
            }

            blockDb.Save();
        }

        /// <summary>
        /// Adds transactions of a block to the transaction database.
        /// </summary>
        private void acceptBlockTransactions(Block block)
        {
            for (int i = 0, c = block.Transactions.Count; i < c; ++i)
            {
                var tx = block.Transactions[i];
                transactionDb.AddTransaction(tx, block.Index, i, needSave: false);
            }

            transactionDb.Save();
        }

        /// <summary>
        /// Replaces part of the blockchain with some new blocks, adds some if any.
        /// </summary>
        /// <param name="blockDb"></param>
        /// <param name="receivedBlocks"></param>
        /// <param name="receivedStartIndex"></param>
        /// <param name="transactionDb"></param>
        private static void replaceBlocks(
            BlockDb blockDb, IList<Block> receivedBlocks, int receivedStartIndex,
            TransactionDb transactionDb)
        {
            List<Block> removedBlocks = new List<Block>();
            blockDb.ReplaceBlocks(receivedBlocks, receivedStartIndex, removedBlocks);

            // Return and remove TxO's
            for (int i = 0, c = removedBlocks.Count; i < c; ++i)
            {
                var block = removedBlocks[i];
                for (int j = 0, n = block.Transactions.Count; j < n; ++j)
                {
                    var tx = block.Transactions[j];
                    if (transactionDb.HasTransaction(tx.Id))
                    {
                        transactionDb.RemoveTransaction(tx, needSave: false);
                    }
                }
            }

            // Spend and add TxO's
            for (int i = receivedStartIndex, c = receivedBlocks.Count; i < c; ++i)
            {
                var transactions = receivedBlocks[i].Transactions;
                for (int j = 0, n = transactions.Count; j < n; ++j)
                {
                    var tx = transactions[j];
                    int blockIndex = i;
                    int positionIndex = j;
                    transactionDb.AddTransaction(tx, blockIndex, positionIndex, needSave: false);
                }
            }

            transactionDb.Save();
        }

        /// <summary>
        /// Checks for general correctness of a block, including
        /// value ranges and difficulty target of the hash.
        /// Transactions and other information are not validated.
        /// </summary>
        public static bool ValidateBlockSanity(Block block)
        {
            if (block.Index < 0)
                return false;

            if (block.Index == 0)
            {
                var genesisBlock = Config.GetGenesisBlock();
                if (block.Equals(genesisBlock) is false)
                    return false;

                return true;
            }

            if (block.Timestamp <= 0
                || string.IsNullOrEmpty(block.PreviousBlockHash)
                || string.IsNullOrEmpty(block.Hash)
                || string.IsNullOrEmpty(block.MerkleHash)
                || block.Transactions == null
                || block.Transactions.Count == 0
                || block.Nonce <= 0)
                return false;

            int expectedDifficulty = Pow.CalculateDifficulty(block.Index);
            using (var stream = new MemoryStream())
            {
                using var streamWriter = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
                block.PrepareForPowHash(streamWriter);
                HexUtils.AppendHexFromInt(streamWriter, block.Nonce);
                streamWriter.Flush();
                var hash = Pow.Hash(stream);
                if (!Pow.IsValidHash(hash, expectedDifficulty))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Validates the given block and adds its transactions and spent TxO's to the given containers.
        /// </summary>
        /// <param name="block">The block to validate.</param>
        /// <param name="prevBlock">The expected previous block (validated).</param>
        /// <param name="localTxMap">Transactions kept in current chain.</param>
        /// <param name="receivedTxMap">Transactions collected from received blocks.</param>
        /// <param name="spentTxOutputs">A set to keep track of spent TxO collected from received transactions.</param>
        /// <returns>True if the given block is valid. Otherwise false.</returns>
        private static bool validateBlock(Block block, Block prevBlock,
            TransactionDb localTxMap, IDictionary<string, Transaction> receivedTxMap,
            HashSet<TxOutPointer> spentTxOutputs)
        {
            if (block.Index != prevBlock.Index + 1)
                return false;

            // Block should not be too far in the future
            if (block.Timestamp > TimeUtils.MsSinceEpochToUtcNow() + Config.BlockTimestampMaxFutureOffset)
                return false;

            // Block should not be too close in time to previous block
            if (block.Timestamp - prevBlock.Timestamp < Config.CalculateBlockTimestampMinDistance(block.Index))
                return false;

            if (block.Transactions == null)
                return false;

            for (int i = 0, c = block.Transactions.Count; i < c; ++i)
            {
                var tx = block.Transactions[i];
                bool isValidTx = validateTransaction(block, i, tx, localTxMap, receivedTxMap, spentTxOutputs);
                if (isValidTx)
                {
                    receivedTxMap.Add(tx.Id, tx);
                }
                else
                {
                    return false;
                }
            }

            if (block.MerkleHash != Block.ComputeMerkleHash(block))
                return false;

            if (block.PreviousBlockHash != prevBlock.Hash)
                return false;

            int expectedDifficulty = Pow.CalculateDifficulty(block.Index);
            using (var stream = new MemoryStream())
            {
                using var streamWriter = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
                block.PrepareForPowHash(streamWriter);
                HexUtils.AppendHexFromInt(streamWriter, block.Nonce);
                streamWriter.Flush();
                var hash = Pow.Hash(stream);
                if (!Pow.IsValidHash(hash, expectedDifficulty))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Validates a list of transactions.
        /// </summary>
        /// <param name="transactions">Transactions to validate.</param>
        /// <param name="localTxMap">Transactions kept in current chain.</param>
        /// <param name="receivedTxMap">Transactions collected from received blocks.</param>
        /// <param name="spentTxOutputs">A set to keep track of spent TxO collected from received transactions.</param>
        /// <returns>True if the transactions are all valid. Otherwise false.</returns>
        private static bool validateTransactions(Block block, IList<Transaction> transactions,
            TransactionDb localTxMap, IDictionary<string, Transaction> receivedTxMap,
            HashSet<TxOutPointer> spentTxOutputs)
        {
            for (int i = 0, c = transactions.Count; i < c; ++i)
            {
                var tx = transactions[i];
                bool isValidTx = validateTransaction(block, i, tx, localTxMap, receivedTxMap, spentTxOutputs);
                if (isValidTx)
                {
                    receivedTxMap.Add(tx.Id, tx);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates the given transaction and adds its spent TxO's to the given container.
        /// </summary>
        /// <param name="tx">Transaction to validate.</param>
        /// <param name="localTxMap">Transactions kept in current chain.</param>
        /// <param name="receivedTxMap">Transactions collected from received blocks.</param>
        /// <param name="spentTxOutputs">A set to keep track of spent TxO collected from received transactions.</param>
        /// <returns>True if the transaction is valid. Otherwise false.</returns>
        private static bool validateTransaction(Block block, int txIndex, Transaction tx,
            TransactionDb localTxMap, IDictionary<string, Transaction> receivedTxMap,
            HashSet<TxOutPointer> spentTxOutputs)
        {
            // Invalid ID?
            if (tx.Id != tx.GetId())
                return false;

            // Duplicate?
            if (localTxMap.HasTransaction(tx.Id) || receivedTxMap.ContainsKey(tx.Id))
                return false;

            // Coinbase tx
            if (txIndex == 0)
            {
                if (tx.Inputs.Count != 1 || tx.Outputs.Count != 1)
                    return false;

                if (Block.IsValidCoinbaseTxIn(block, tx.Inputs[0]) is false)
                    return false;

                long reward = Config.CalculateRewardInNekoshi(block.Index);
                reward += Config.FeeNekoshiPerTx * (block.Transactions.Count - 1);

                var txOut = tx.Outputs[0];
                if (string.IsNullOrEmpty(txOut.Address)
                    || txOut.AmountInNekoshi != reward)
                    return false;

                return true;
            }

            long totalInAmount = 0;
            long totalOutAmount = 0;

            for (int j = 0, n = tx.Inputs.Count; j < n; ++j)
            {
                var txIn = tx.Inputs[j];

                if (string.IsNullOrEmpty(txIn.TxId)
                    || txIn.TxOutIndex < 0
                    || string.IsNullOrEmpty(txIn.Signature))
                    return false;

                var ptr = new TxOutPointer
                {
                    TxId = txIn.TxId,
                    TxOutIndex = txIn.TxOutIndex
                };

                if (spentTxOutputs.Contains(ptr))
                    return false;
                else
                    spentTxOutputs.Add(ptr);

                var refTx = localTxMap.GetTransaction(txIn.TxId);
                if (refTx == null)
                    if (receivedTxMap.TryGetValue(txIn.TxId, out refTx) is false)
                        return false;

                if (txIn.TxOutIndex >= refTx.Outputs.Count)
                    return false;

                var refTxOut = refTx.Outputs[txIn.TxOutIndex];

                var (signature, publicKey) = AddressUtils.SignatureAndPublicKeyFromTxInSignature(txIn.Signature);
                if (AddressUtils.AddressFromPublicKey(publicKey) != refTxOut.Address)
                    return false;

                if (EllipticCurve.Ecdsa.verify(tx.Id, signature, publicKey) == false)
                    return false;

                totalInAmount += refTxOut.AmountInNekoshi;
            }

            for (int j = 0, n = tx.Outputs.Count; j < n; ++j)
            {
                var txOut = tx.Outputs[j];

                if (string.IsNullOrEmpty(txOut.Address)
                    || txOut.AmountInNekoshi <= 0)
                    return false;

                totalOutAmount += txOut.AmountInNekoshi;
            }

            if (totalInAmount != totalOutAmount + Config.FeeNekoshiPerTx)
                return false;

            return true;
        }
    }
}