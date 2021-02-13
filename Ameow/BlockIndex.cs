using Newtonsoft.Json;

namespace Ameow
{
    /// <summary>
    /// Database index of a block.
    /// </summary>
    public sealed class BlockIndex
    {
        [JsonProperty("i")]
        public int Index;

        [JsonProperty("h")]
        public string Hash;
    }
}