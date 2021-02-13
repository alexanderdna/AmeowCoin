using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Represents a pending transaction in the mempool.
    /// </summary>
    public sealed class PendingTransaction
    {
        [JsonProperty("t")]
        public long Time;

        [JsonProperty("tx")]
        public Transaction Tx;
    }
}