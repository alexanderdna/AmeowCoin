using System;
using System.Collections.Generic;
using System.IO;

namespace Ameow
{
    /// <summary>
    /// Configurations of the coin.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Version of the protocol, in 0xMMmmpp00 format
        /// where MM is major number, mm is minor number
        /// and pp is patch number.
        /// </summary>
        public const int Version = 0x01000000;

        /// <summary>
        /// Maximum number of inputs a transaction can have.
        /// </summary>
        public const int MaxTransactionInputs = 32;

        /// <summary>
        /// Maximum number of transactions a block can have.
        /// </summary>
        public const int MaxTransactionsInBlock = 32;

        /// <summary>
        /// Maximum number of blocks a GetBlocks message can request,
        /// and that a Blocks message can deliver.
        /// </summary>
        public const int MaxGetBlocksCount = 32;

        /// <summary>
        /// When a conflict occurs in IBD progress, the local node will request
        /// blocks from the remote node starting from current conflict index minus
        /// this constant.
        /// </summary>
        public const int ConflictResolutionSteps = 4;

        /// <summary>
        /// Block timestamp can be a number of milliseconds ahead of local node's time.
        /// This constant represents that offset.
        /// </summary>
        public const long BlockTimestampMaxFutureOffset = 1000L * 60 * 60 * 30; // 30 hours

        /// <summary>
        /// Maximum number of pending transactions to send in a Mempool message.
        /// </summary>
        public const int MaxPendingTxToSend = 32;

        /// <summary>
        /// Transaction fee, in nekoshi.
        /// </summary>
        public const int FeeNekoshiPerTx = 50_000_000;

        /// <summary>
        /// Number of nekoshi per coin. This constant should not be changed whatsoever.
        /// Nekoshi is the equivalent of Satoshi in Ameow vocabulary.
        /// </summary>
        public const long NekoshiPerCoin = 100_000_000L;

        /// <summary>
        /// Maximum amount of nekoshi that can be sent in a transaction.
        /// </summary>
        public const long MaxSendableNekoshi = 1_000_000L * NekoshiPerCoin;

        /// <summary>
        /// Cached value of the Genesis block.
        /// </summary>
        private static Block genesisBlock = null;

        /// <summary>
        /// Calculates and returns the block reward in nekoshi.
        /// </summary>
        /// <param name="index">Block index.</param>
        public static long CalculateRewardInNekoshi(int index)
        {
            // rewards are cut in halves every 10,000 blocks
            int generation = index / 10_000;
            return (NekoshiPerCoin * 64) >> generation;
        }

        /// <summary>
        /// Calculates and returns the minimum number of milliseconds
        /// between the given block index and the preceding one.
        /// </summary>
        /// <param name="index">Block index.</param>
        public static long CalculateBlockTimestampMinDistance(int index)
        {
            // first 100 blocks will have a 30 second distance
            if (index <= 100) return 1000L * 30;

            // smallest minimum distance is 1 minute
            const long lowerBound = 1000L * 60;

            // starts at 10 minutes and decreases by 1 minute every 10,000 blocks
            const long startValue = 1000L * 60 * 10;
            const long decrement = startValue / 10;

            int generation = index / 10_000;
            return Math.Max(lowerBound, startValue - decrement * generation);
        }

        /// <summary>
        /// Creates and returns the Genesis block.
        /// </summary>
        public static Block GetGenesisBlock()
        {
            if (genesisBlock == null)
            {
                genesisBlock = new Block
                {
                    Index = 0,
                    Timestamp = 1_610_998_200_000L,
                    Transactions = new List<Transaction>(),
                    MerkleHash = "",
                    PreviousBlockHash = "4f571e9b08717e7627336808d26ea36958ccea7ff341cc2d218c3df61bd04d08",
                    Hash = "4fd2d32ca7af3219af42639d740781fa75ca956a5e100e0de2579731d120e9f2",
                    Nonce = 0,
                };
            }

            return genesisBlock;
        }

        /// <summary>
        /// Returns the default path to block index file.
        /// </summary>
        public static string GetBlockIndexFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "data/blocks", "index.json");
        }

        /// <summary>
        /// Returns the default path to a block file.
        /// </summary>
        public static string GetBlockFilePath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "data/blocks", fileName);
        }

        /// <summary>
        /// Returns the default path to the transaction database file.
        /// </summary>
        public static string GetTransactionIndexFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "data/txdb.json");
        }
    }
}