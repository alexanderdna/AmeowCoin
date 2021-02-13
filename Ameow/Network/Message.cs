using Newtonsoft.Json;
using System.Collections.Generic;

namespace Ameow.Network
{
    /// <summary>
    /// Communication packet between peer nodes.
    /// </summary>
    public sealed class Message
    {
        [JsonProperty("t")]
        public MessageType Type;

        [JsonProperty("c")]
        public int Checksum;

        [JsonProperty("d")]
        public string Data;

        /// <summary>
        /// Converts the given message into network representation (JSON).
        /// </summary>
        public static string Serialize(Message msg)
        {
            return JsonConvert.SerializeObject(msg, Formatting.None);
        }

        /// <summary>
        /// Converts the given network representation (JSON) into a message.
        /// </summary>
        public static Message Deserialize(string networkRepresentation)
        {
            return JsonConvert.DeserializeObject<Message>(networkRepresentation);
        }

        /// <summary>
        /// Calculates checksum of <see cref="Data"/> field and stores it in <see cref="Checksum"/> field of the given message.
        /// </summary>
        /// <remarks>
        /// Checksum is a 32-bit integer in Big-Endian order read from the first 4 bytes of the SHA-256 digest of <see cref="Data"/>.
        /// </remarks>
        /// <seealso cref="Utils.HashUtils.SHA256(string)"/>
        /// <seealso cref="Utils.HexUtils.IntFromHex(string)"/>
        public static void CalculateChecksum(Message msg)
        {
            var hash = Utils.HashUtils.SHA256(msg.Data);
            msg.Checksum = Utils.HexUtils.IntFromHex(hash);
        }

        /// <summary>
        /// Calcuates checksum of <see cref="Data"/> field and compares that with <see cref="Checksum"/> field of the given message.
        /// Returns true if the calculated and the stored checksums match.
        /// </summary>
        /// <remarks>
        /// Checksum is a 32-bit integer in Big-Endian order read from the first 4 bytes of the SHA-256 digest of <see cref="Data"/>.
        /// </remarks>
        /// <seealso cref="Utils.HashUtils.SHA256(string)"/>
        /// <seealso cref="Utils.HexUtils.IntFromHex(string)"/>
        public static bool VerifyChecksum(Message msg)
        {
            var hash = Utils.HashUtils.SHA256(msg.Data);
            var validChecksum = Utils.HexUtils.IntFromHex(hash);
            return msg.Checksum == validChecksum;
        }
    }

    public enum MessageType
    {
        Version = 1,
        VersionAck = 2,

        GetLatestBlock = 10,
        GetBlocks = 11,
        LatestBlock = 15,
        Blocks = 16,

        GetMempool = 50,
        Mempool = 55,

        Ping = 1000,
        Pong = 1001,
    }

    public abstract class MessageData
    {
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }

        public static T FromJson<T>(string json) where T : MessageData
        {
            if (json == null) return null;
            return JsonConvert.DeserializeObject<T>(json);
        }
    }

    public sealed class MDVersion : MessageData
    {
        [JsonProperty("ver")]
        public int Version;

        [JsonProperty("height")]
        public int Height;

        [JsonProperty("nonce")]
        public string Nonce;
    }

    public sealed class MDVersionAck : MessageData
    {
    }

    public sealed class MDGetLatestBlock : MessageData
    {
    }

    public sealed class MDGetBlocks : MessageData
    {
        [JsonProperty("start_index")]
        public int StartIndex;

        [JsonProperty("max_count")]
        public int MaxCount;
    }

    public sealed class MDLatestBlock : MessageData
    {
        [JsonProperty("block")]
        public Block Block;
    }

    public sealed class MDBlocks : MessageData
    {
        [JsonProperty("blocks")]
        public List<Block> Blocks;
    }

    public sealed class MDGetMempool : MessageData
    {
    }

    public sealed class MDMempool : MessageData
    {
        [JsonProperty("rel")]
        public bool IsRelayed;

        [JsonProperty("txs")]
        public List<PendingTransaction> Transactions;
    }
}