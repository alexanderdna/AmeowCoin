using Ameow.Utils;
using System;
using System.IO;
using System.Text;

namespace Ameow
{
    /// <summary>
    /// Mining class for mining a new block.
    /// </summary>
    public sealed class BlockMining : IDisposable
    {
        public readonly Block Block;
        private int _difficulty;
        private int _startingNonce;
        private int _nonceRange;
        private bool _maxNonceReached;

        private MemoryStream _headerStream;
        private StreamWriter _headerStreamWriter;
        private long _streamOrgPosition;

        public bool IsExhausted => _maxNonceReached;

        /// <summary>
        /// Constructs a mining object.
        /// </summary>
        /// <param name="block">The block to mine.</param>
        /// <param name="nonceRange">Range of nonce values to try in each call to <see cref="Attempt"/>.</param>
        public BlockMining(Block block, int nonceRange)
        {
            Block = block;
            _difficulty = block.Difficulty;
            _startingNonce = 0;
            _nonceRange = nonceRange;
            _maxNonceReached = false;

            _headerStream = new MemoryStream();
            _headerStreamWriter = new StreamWriter(_headerStream, Encoding.UTF8);

            block.PrepareForPowHash(_headerStreamWriter);
            _streamOrgPosition = _headerStream.Position;
        }

        /// <summary>
        /// Repeatedly calls <see cref="Attempt"/> in a blocking manner until the right nonce is found.
        /// </summary>
        /// <returns>True if the block is mined. False if no nonce value satisfies the difficulty.</returns>
        public bool MineUntilFound()
        {
            while (!_maxNonceReached)
            {
                if (Attempt()) return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to mine the block with the next range of nonce values.
        /// </summary>
        /// <returns>True if the block is successfully mined.</returns>
        public bool Attempt()
        {
            int nonceRange = int.MaxValue - _startingNonce;
            if (nonceRange > _nonceRange) nonceRange = _nonceRange;

            for (int n = _startingNonce, c = _startingNonce + nonceRange; n < c; ++n)
            {
                HexUtils.AppendHexFromInt(_headerStreamWriter, n);
                _headerStreamWriter.Flush();

                var hash = Pow.Hash(_headerStream);
                if (Pow.IsValidHash(hash, _difficulty))
                {
                    Block.Nonce = n;
                    Block.Hash = HexUtils.HexFromByteArray(hash);
                    return true;
                }

                _headerStream.Seek(_streamOrgPosition, SeekOrigin.Begin);
            }

            _startingNonce += nonceRange;

            if (_startingNonce == int.MaxValue)
                _maxNonceReached = true;

            return false;
        }

        public void Dispose()
        {
            if (_headerStreamWriter != null)
            {
                _headerStreamWriter.Close();
                _headerStreamWriter.Dispose();
                _headerStreamWriter = null;

                _headerStream = null;
            }
        }
    }
}