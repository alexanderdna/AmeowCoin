using Ameow.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Ameow
{
    public sealed class Transaction
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("i")]
        public List<TxIn> Inputs = new List<TxIn>();

        [JsonProperty("o")]
        public List<TxOut> Outputs = new List<TxOut>();

        /// <summary>
        /// Calculates and returns the ID of the transaction.
        /// ID is actually a hash computed from all the inputs and outputs.
        /// </summary>
        /// <remarks>
        /// This method will not assign the computed value to <see cref="Id"/>
        /// nor will it return the value of said field. It has to compute
        /// the hash from what are actually in the inputs and outputs so that
        /// validation of transactions can compare the computed value with
        /// <see cref="Id"/> to check for its correctness.
        /// </remarks>
        public string GetId()
        {
            string hash;
            using (var stream = new MemoryStream())
            {
                using var streamWriter = new StreamWriter(stream);

                for (int i = 0, c = Inputs.Count; i < c; ++i)
                {
                    var txIn = Inputs[i];
                    streamWriter.Write(txIn.TxId);
                    HexUtils.AppendHexFromInt(streamWriter, txIn.TxOutIndex);
                }

                for (int i = 0, c = Outputs.Count; i < c; ++i)
                {
                    var txOut = Outputs[i];
                    streamWriter.Write(txOut.Address);
                    HexUtils.AppendHexFromLong(streamWriter, txOut.AmountInNekoshi);
                }

                streamWriter.Flush();

                hash = HashUtils.SHA256(stream);
            }

            return hash;
        }
    }
}