using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Transaction output that has not been consumed by any later transaction.
    /// </summary>
    public class UnspentTxOut
    {
        [JsonProperty("tx")]
        public string TxId;

        [JsonProperty("index")]
        public int TxOutIndex;

        /// <summary>
        /// Recipient address of this TxO. Only for quick fetching of UTxO's and cannot be relied upon.
        /// </summary>
        [JsonProperty("addr")]
        public string Address;
    }
}