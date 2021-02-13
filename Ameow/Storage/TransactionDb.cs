using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Ameow.Storage
{
    /// <summary>
    /// Manages loading and storing of transaction database in persistent storage.
    /// </summary>
    public sealed class TransactionDb
    {
        private readonly BlockDb _blockDb;

        private IndexFile _indexFile;
        private Dictionary<string, TransactionIndex> _transactionIndices;
        private List<UnspentTxOut> _unspentTxOuts;
        private List<PendingTransaction> _pendingTransactions = new List<PendingTransaction>();

        private Dictionary<string, Transaction> _transactions;

        public TransactionDb(BlockDb blockDb)
        {
            _blockDb = blockDb;
        }

        /// <summary>
        /// Loads transaction database from persistent storage into memory.
        /// </summary>
        /// <returns>True if succeeds.</returns>
        public bool Load()
        {
            _indexFile = DbFile.Read<IndexFile>(Config.GetTransactionIndexFilePath());
            if (_indexFile == null)
            {
                _indexFile = new IndexFile
                {
                    TransactionIndices = new Dictionary<string, TransactionIndex>(),
                    UnspentTxOuts = new List<UnspentTxOut>(),
                    PendingTransactions = new List<PendingTransaction>(),
                };
                DbFile.Write(Config.GetTransactionIndexFilePath(), _indexFile);
            }

            _transactionIndices = _indexFile.TransactionIndices;
            _unspentTxOuts = _indexFile.UnspentTxOuts;
            _pendingTransactions = _indexFile.PendingTransactions;

            _transactions = new Dictionary<string, Transaction>();

            return true;
        }

        /// <summary>
        /// Writes transaction database into persistent storage.
        /// </summary>
        /// <returns>True if succeeds.</returns>
        public bool Save()
        {
            DbFile.Write(Config.GetTransactionIndexFilePath(), _indexFile);
            return true;
        }

        /// <summary>
        /// Adds a new pending transaction to the mempool.
        /// </summary>
        /// <param name="tx">The transaction to add.</param>
        /// <param name="needSave">If true, the database will be saved immediately.</param>
        /// <remarks>
        /// In scenarios when there are multiple calls to this method,
        /// passing false to <paramref name="needSave"/> and then calling
        /// <see cref="Save"/> in the end would be a more efficient approach.
        /// </remarks>
        public void AddPendingTransaction(Transaction tx, bool needSave = true)
        {
            _pendingTransactions.Add(new PendingTransaction
            {
                Time = Utils.TimeUtils.MsSinceEpochToUtcNow(),
                Tx = tx,
            });

            if (needSave)
                Save();
        }

        /// <summary>
        /// Adds a new pending transaction to the mempool.
        /// </summary>
        /// <param name="ptx">The transaction to add.</param>
        /// <param name="needSave">If true, the database will be saved immediately.</param>
        /// <remarks>
        /// In scenarios when there are multiple calls to this method,
        /// passing false to <paramref name="needSave"/> and then calling
        /// <see cref="Save"/> in the end would be a more efficient approach.
        /// </remarks>
        public void AddPendingTransaction(PendingTransaction ptx, bool needSave = true)
        {
            _pendingTransactions.Add(ptx);

            if (needSave)
                Save();
        }

        /// <summary>
        /// Returns the pending transaction having the given ID.
        /// </summary>
        /// <returns>The found transaction or null if not found.</returns>
        public PendingTransaction GetPendingTransaction(string id)
        {
            return _pendingTransactions.Find(tx => tx.Tx.Id == id);
        }

        /// <summary>
        /// Collects a number of pending transactions.
        /// The transactions are sorted in oldest to newest order.
        /// </summary>
        /// <param name="container">Call-site-provided container for collected transactions.</param>
        /// <param name="maxCount">Maximum number of transactions to collect.</param>
        public void GetPendingTransactions(List<PendingTransaction> container, int maxCount)
        {
            _pendingTransactions.Sort((a, b) => a.Time == b.Time ? 0 : (a.Time > b.Time ? 1 : -1));

            for (int i = 0, c = Math.Min(_pendingTransactions.Count, maxCount); i < c; ++i)
            {
                container.Add(_pendingTransactions[i]);
            }
        }

        /// <summary>
        /// Collects pending transactions for a new block that will be mined and computes Merkle hash for the block.
        /// </summary>
        /// <param name="block">The block to contain collected transactions.</param>
        /// <param name="minerAddress">Address of the miner to include in coinbase transaction.</param>
        public void CollectPendingTransactionsForBlock(Block block, string minerAddress)
        {
            int nSelectedTxs;
            int nMaxSelectedTxs = Math.Min(Config.MaxTransactionsInBlock, _pendingTransactions.Count);
            long totalFeeInNekoshi = 0;
            List<Transaction> selectedTxs = new List<Transaction>();
            for (nSelectedTxs = 0; nSelectedTxs < nMaxSelectedTxs; ++nSelectedTxs)
            {
                selectedTxs.Add(_pendingTransactions[nSelectedTxs].Tx);
                totalFeeInNekoshi += Config.FeeNekoshiPerTx;
            }

            block.AddCoinbaseTx(minerAddress, totalFeeInNekoshi);
            block.Transactions.AddRange(selectedTxs);
            block.MerkleHash = Block.ComputeMerkleHash(block);
        }

        /// <summary>
        /// Adds a new transaction to the database.
        /// </summary>
        /// <param name="tx">The transaction to add.</param>
        /// <param name="blockIndex">Index of the block containing the given transaction.</param>
        /// <param name="positionIndex">Index of the transaction in the block.</param>
        /// <param name="needSave">If true, the database will be saved immediately.</param>
        /// <returns>Index object for the given transaction.</returns>
        /// <remarks>
        /// In scenarios when there are multiple calls to this method,
        /// passing false to <paramref name="needSave"/> and then calling
        /// <see cref="Save"/> in the end would be a more efficient approach.
        /// </remarks>
        public TransactionIndex AddTransaction(Transaction tx, int blockIndex, int positionIndex, bool needSave = true)
        {
            if (_transactions.ContainsKey(tx.Id) || _transactionIndices.ContainsKey(tx.Id))
                throw new InvalidOperationException("Duplicate transaction ID");

            var txIndex = new TransactionIndex
            {
                Id = tx.Id,
                BlockIndex = blockIndex,
                PositionIndex = positionIndex,
            };
            _transactionIndices.Add(tx.Id, txIndex);
            _transactions.Add(tx.Id, tx);

            for (int i = 0, c = tx.Inputs.Count; i < c; ++i)
            {
                var txIn = tx.Inputs[i];
                spendTxOut(txIn.TxId, txIn.TxOutIndex);
            }

            for (int i = 0, c = tx.Outputs.Count; i < c; ++i)
            {
                var txOut = tx.Outputs[i];
                var utxo = new UnspentTxOut
                {
                    TxId = tx.Id,
                    TxOutIndex = i,
                    Address = txOut.Address,
                };
                _unspentTxOuts.Add(utxo);
            }

            for (int i = 0, c = _pendingTransactions.Count; i < c; ++i)
            {
                var ptx = _pendingTransactions[i];
                if (ptx.Tx.Id == tx.Id)
                {
                    _pendingTransactions.RemoveAt(i);
                    break;
                }
            }

            if (needSave)
                Save();

            return txIndex;
        }

        /// <summary>
        /// Removes the given TxO off UTxO list.
        /// </summary>
        /// <param name="txId"></param>
        /// <param name="txOutIndex"></param>
        private void spendTxOut(string txId, int txOutIndex)
        {
            for (int i = 0, c = _unspentTxOuts.Count; i < c; ++i)
            {
                var utxo = _unspentTxOuts[i];
                if (utxo.TxId == txId && utxo.TxOutIndex == txOutIndex)
                {
                    _unspentTxOuts.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Removes the given transaction from the database.
        /// </summary>
        /// <param name="tx">The transaction to remove.</param>
        /// <param name="needSave">If true, the database will be saved immediately.</param>
        /// <returns>True if the transaction exists and can be removed.</returns>
        /// <remarks>
        /// In scenarios when there are multiple calls to this method,
        /// passing false to <paramref name="needSave"/> and then calling
        /// <see cref="Save"/> in the end would be a more efficient approach.
        /// </remarks>
        public bool RemoveTransaction(Transaction tx, bool needSave = true)
        {
            if (_transactionIndices.ContainsKey(tx.Id) is false)
                return false;

            _transactionIndices.Remove(tx.Id);
            _transactions.Remove(tx.Id);

            // Return the TxO's that this Tx has consumed
            for (int i = 0, c = tx.Inputs.Count; i < c; ++i)
            {
                var txIn = tx.Inputs[i];
                var refTx = GetTransaction(txIn.TxId);
                if (refTx == null)
                    continue;

                var refTxOut = refTx.Outputs[txIn.TxOutIndex];
                var utxo = new UnspentTxOut
                {
                    TxId = refTx.Id,
                    TxOutIndex = txIn.TxOutIndex,
                    Address = refTxOut.Address,
                };
                _unspentTxOuts.Add(utxo);
            }

            // Remove the TxO's that this Tx has produced
            for (int i = 0, c = _unspentTxOuts.Count; i < c; ++i)
            {
                var utxo = _unspentTxOuts[i];
                if (utxo.TxId == tx.Id)
                {
                    _unspentTxOuts.RemoveAt(i);
                    --i;
                    --c;
                }
            }

            if (needSave)
                Save();

            return true;
        }

        /// <summary>
        /// Returns true if a transaction with the given ID exists in the database.
        /// </summary>
        public bool HasTransaction(string id)
        {
            return _transactionIndices.ContainsKey(id);
        }

        /// <summary>
        /// Tries to find and return a transaction having the given ID.
        /// </summary>
        /// <returns>The found transaction or null if not found.</returns>
        /// <remarks>
        /// This method may call <see cref="BlockDb.GetBlock(BlockIndex)"/>
        /// which may in turn load a block file into memory.
        /// </remarks>
        public Transaction GetTransaction(string id)
        {
            if (_transactions.TryGetValue(id, out var tx) is false)
            {
                if (_transactionIndices.TryGetValue(id, out var txIndex) is false)
                    return null;

                var block = _blockDb.GetBlock(txIndex.BlockIndex);
                if (txIndex.PositionIndex < 0 || txIndex.PositionIndex >= block.Transactions.Count)
                    return null;

                tx = block.Transactions[txIndex.PositionIndex];
            }

            return tx;
        }

        /// <summary>
        /// Collects UTxO's of the given address.
        /// </summary>
        /// <param name="address">The address to look for.</param>
        /// <param name="resultContainer">Call-site-provided container for collected UTxO's.</param>
        /// <param name="pendingTxOutContainer">Call-site-provided container for TxO's in pending transactions.</param>
        public void CollectUnspentTxOuts(string address, List<UnspentTxOut> resultContainer, List<TxOut> pendingTxOutContainer)
        {
            for (int i = 0, c = _unspentTxOuts.Count; i < c; ++i)
            {
                var utxo = _unspentTxOuts[i];

                // utxo.Address is only an optimization filter
                // and cannot be relied upon. We have to check
                // the actual TxO.
                if (utxo.Address == address)
                {
                    var tx = GetTransaction(utxo.TxId);
                    if (tx == null)
                        throw new InvalidOperationException($"TxId {utxo.TxId} is invalid.");

                    if (utxo.TxOutIndex < 0 || utxo.TxOutIndex >= tx.Outputs.Count)
                        throw new InvalidOperationException($"UTxO has invalid TxOutIndex {utxo.TxOutIndex}");

                    // Here's the check.
                    if (tx.Outputs[utxo.TxOutIndex].Address != address)
                        throw new InvalidOperationException($"Inconsistent address in TxO {utxo.TxId}[{utxo.TxOutIndex}]");

                    resultContainer.Add(utxo);
                }
            }

            // Traverse mempool to exclude spent TxO's and include pending TxO's.
            for (int i = 0, c = _pendingTransactions.Count; i < c; ++i)
            {
                var tx = _pendingTransactions[i].Tx;
                for (int j = 0, n = tx.Inputs.Count; j < n; ++j)
                {
                    var txIn = tx.Inputs[j];
                    removeSpentTxOutputs(resultContainer, txIn);
                }

                for (int j = 0, n = tx.Outputs.Count; j < n; ++j)
                {
                    var txOut = tx.Outputs[j];
                    if (txOut.Address == address)
                        pendingTxOutContainer.Add(txOut);
                }
            }

            static void removeSpentTxOutputs(List<UnspentTxOut> fromList, TxIn txIn)
            {
                for (int i = 0, c = fromList.Count; i < c; ++i)
                {
                    var utxo = fromList[i];
                    if (utxo.TxId == txIn.TxId && utxo.TxOutIndex == txIn.TxOutIndex)
                    {
                        fromList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private sealed class IndexFile : DbFile
        {
            [JsonProperty("tx_indices")]
            public Dictionary<string, TransactionIndex> TransactionIndices;

            [JsonProperty("utxo")]
            public List<UnspentTxOut> UnspentTxOuts;

            [JsonProperty("mempool")]
            public List<PendingTransaction> PendingTransactions;
        }
    }
}