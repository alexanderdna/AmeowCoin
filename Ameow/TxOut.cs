using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Transaction output.
    /// </summary>
    public sealed class TxOut
    {
        [JsonProperty("c")]
        public long AmountInNekoshi;

        [JsonProperty("a")]
        public string Address;
    }
}