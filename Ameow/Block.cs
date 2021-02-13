using Ameow.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Ameow
{
    /// <summary>
    /// Represents a block in the blockchain.
    /// </summary>
    public sealed class Block
    {
        [JsonProperty("i")]
        public int Index;

        [JsonProperty("t")]
        public long Timestamp;

        [JsonProperty("txs")]
        public List<Transaction> Transactions = new List<Transaction>();

        [JsonProperty("merkle")]
        public string MerkleHash;

        [JsonProperty("prev")]
        public string PreviousBlockHash;

        [JsonProperty("h")]
        public string Hash;

        [JsonProperty("n")]
        public int Nonce;

        [JsonIgnore]
        public int Difficulty => Pow.CalculateDifficulty(Index);

        [JsonIgnore]
        public bool IsSaved { get; set; } = false;

        /// <summary>
        /// Returns true if the given TxI is a valid coinbase TxI of the given block.
        /// </summary>
        public static bool IsValidCoinbaseTxIn(Block block, TxIn txIn)
        {
            return txIn.TxId == HexUtils.HexFromInt(block.Index)
                && txIn.TxOutIndex == 0
                && txIn.Signature == "";
        }

        /// <summary>
        /// Returns true if this block's components are equal to those of the given blocks.
        /// Components are <see cref="Index"/>, <see cref="Timestamp"/>,
        /// <see cref="MerkleHash"/>, <see cref="PreviousBlockHash"/>,
        /// <see cref="Hash"/> and <see cref="Nonce"/>.
        /// </summary>
        public bool Equals(Block other)
        {
            return Index == other.Index
                && Timestamp == other.Timestamp
                && MerkleHash == other.MerkleHash
                && PreviousBlockHash == other.PreviousBlockHash
                && Hash == other.Hash
                && Nonce == other.Nonce;
        }

        /// <summary>
        /// Creates a coinbase transaction and adds it the block.
        /// </summary>
        /// <returns>The created coinbase transaction.</returns>
        /// <remarks>
        /// Since coinbase transaction must be the first transaction in a block,
        /// this method will throw an exception if the block already contains more than 0 transactions.
        /// </remarks>
        public Transaction AddCoinbaseTx(string address, long feeInNekoshi)
        {
            if (Transactions.Count > 0)
                throw new System.InvalidOperationException("Coinbase must be the first transaction.");

            long reward = Config.CalculateRewardInNekoshi(Index);
            var txIn = new TxIn
            {
                TxId = HexUtils.HexFromInt(Index),
                TxOutIndex = 0,
                Signature = "",
            };
            var txOut = new TxOut
            {
                AmountInNekoshi = reward + feeInNekoshi,
                Address = address,
            };
            var tx = new Transaction();
            tx.Inputs.Add(txIn);
            tx.Outputs.Add(txOut);
            tx.Id = tx.GetId();
            Transactions.Add(tx);
            return tx;
        }

        /// <summary>
        /// Prepares header of the block to serve PoW hashing.
        /// This is similar to `getblocktemplate` in Bitcoin protocol.
        /// </summary>
        /// <param name="writer">Writer of a stream that will receive the block header.</param>
        public void PrepareForPowHash(TextWriter writer)
        {
            HexUtils.AppendHexFromInt(writer, Index);
            HexUtils.AppendHexFromLong(writer, Timestamp);
            writer.Write(MerkleHash);
            writer.Write(PreviousBlockHash);

            writer.Flush();
        }

        /// <summary>
        /// Builds a Merkle tree from the given block's transactions and computes the hash of it.
        /// </summary>
        /// <returns>Hash of the Merkle tree.</returns>
        public static string ComputeMerkleHash(Block block)
        {
            if (block.Transactions.Count == 0)
            {
                return "";
            }

            var hashes = new List<string>(block.Transactions.Count);
            for (int i = 0, c = block.Transactions.Count; i < c; ++i)
            {
                hashes.Add(block.Transactions[i].Id);
            }

            if (hashes.Count % 2 != 0) hashes.Add(hashes[^1]);

            int count = hashes.Count;
            int step = 1;
            while (count > 1)
            {
                for (int i = 0, c = hashes.Count; i < c; i += step * step)
                {
                    var h1 = hashes[i];
                    var h2 = (i + step < c) ? hashes[i + step] : h1;
                    var combined = HashUtils.SHA256(string.Concat(h1, h2));
                    hashes[i] = combined;
                }

                if (count % 2 == 0)
                    count /= 2;
                else
                    count = (count + 1) / 2;

                step *= 2;
            }

            return hashes[0];
        }
    }
}