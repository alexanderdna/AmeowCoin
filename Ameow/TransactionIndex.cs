using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Database index of a transaction.
    /// </summary>
    public sealed class TransactionIndex
    {
        [JsonIgnore]
        public string Id { get; set; }

        [JsonProperty("block")]
        public int BlockIndex;

        [JsonProperty("index")]
        public int PositionIndex;
    }
}