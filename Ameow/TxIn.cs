using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Transaction input.
    /// </summary>
    public sealed class TxIn
    {
        [JsonProperty("t")]
        public string TxId;

        [JsonProperty("i")]
        public int TxOutIndex;

        [JsonProperty("s")]
        public string Signature;
    }
}