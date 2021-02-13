using Newtonsoft.Json;
using System.Collections.Generic;

namespace Ameow.Storage
{
    /// <summary>
    /// Manages loading and storing of blocks in persistent storage.
    /// </summary>
    public sealed class BlockDb
    {
        private IndexFile _indexFile;
        private List<BlockIndex> _blockIndices;
        private Dictionary<int, BlockFile> _blockFileByIndex;
        private Dictionary<int, Block> _blocks;

        public int BlockCount => _blockIndices.Count;

        public Block LatestBlock => GetBlock(_blockIndices.Count - 1);

        public bool IsReady { get; private set; }

        /// <summary>
        /// Loads block index from file into memory.
        /// </summary>
        /// <returns>True if succeeds.</returns>
        public bool Load()
        {
            _indexFile = DbFile.Read<IndexFile>(Config.GetBlockIndexFilePath());
            if (_indexFile == null)
            {
                _indexFile = new IndexFile
                {
                    BlockIndices = new List<BlockIndex>()
                };
                DbFile.Write(Config.GetBlockIndexFilePath(), _indexFile);
            }

            _blockIndices = _indexFile.BlockIndices;

            for (int i = 0, c = _blockIndices.Count; i < c; ++i)
            {
                var idx = _blockIndices[i].Index;
                if (idx != i)
                    return false;

                var hash = Utils.HexUtils.ByteArrayFromHex(_blockIndices[i].Hash);
                if (!Pow.IsValidHash(hash, Pow.CalculateDifficulty(idx)))
                    return false;
            }

            _blockFileByIndex = new Dictionary<int, BlockFile>();
            _blocks = new Dictionary<int, Block>();

            if (_blockIndices.Count == 0)
            {
                AddBlock(Config.GetGenesisBlock());
            }

            IsReady = true;

            return true;
        }

        /// <summary>
        /// Saves index and current blocks to file.
        /// </summary>
        /// <returns>True if succeeds.</returns>
        public bool Save()
        {
            DbFile.Write(Config.GetBlockIndexFilePath(), _indexFile);

            foreach (var p in _blockFileByIndex)
            {
                var blockFile = p.Value;
                if (blockFile.IsDirty())
                {
                    BlockFile.Write(blockFile);
                    blockFile.MarkBlocksSaved();
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a new block to the chain.
        /// </summary>
        /// <param name="block">The block to add.</param>
        /// <param name="needSave">If true, the index file and block file will be saved immediately.</param>
        /// <returns>Index to the new block.</returns>
        /// <remarks>
        /// In scenarios when there are multiple calls to this method,
        /// passing false to <paramref name="needSave"/> and then calling
        /// <see cref="Save"/> in the end would be a more efficient approach.
        /// </remarks>
        public BlockIndex AddBlock(Block block, bool needSave = true)
        {
            if (_blockIndices.Count > 0 && block.Index != LatestBlock.Index + 1)
                throw new System.ArgumentException("Given block is not right after latest block.");

            int fileIndex = block.Index / BlockFile.BlocksPerFile;
            if (_blockFileByIndex.ContainsKey(fileIndex))
            {
                var blockFile = _blockFileByIndex[fileIndex];
                blockFile.AddBlock(block);

                if (needSave)
                    BlockFile.Write(blockFile);
            }
            else
            {
                var blockFile = BlockFile.Read(block.Index);

                if (blockFile != null)
                {
                    blockFile.AddBlock(block);
                }
                else
                {
                    blockFile = new BlockFile(block);
                }

                _blockFileByIndex.Add(fileIndex, blockFile);

                if (needSave)
                    BlockFile.Write(blockFile);
            }

            if (needSave)
                block.IsSaved = true;

            var blockIndex = new BlockIndex
            {
                Index = block.Index,
                Hash = block.Hash,
            };

            _blockIndices.Add(blockIndex);
            _blocks.Add(block.Index, block);

            if (needSave)
            {
                DbFile.Write(Config.GetBlockIndexFilePath(), _indexFile);
            }

            return blockIndex;
        }

        /// <summary>
        /// Replaces current blocks in the chain with received blocks.
        /// This method may add new blocks when needed.
        /// </summary>
        /// <param name="receivedBlocks">The list containing the new blocks.</param>
        /// <param name="receivedStartIndex">Index in the given list to get new blocks.</param>
        /// <param name="removedBlocks">Call-site-provided container for old blocks that are removed from the chain.</param>
        public void ReplaceBlocks(IList<Block> receivedBlocks, int receivedStartIndex, List<Block> removedBlocks)
        {
            for (int i = receivedStartIndex, c = receivedBlocks.Count; i < c; ++i)
            {
                Block receivedBlock = receivedBlocks[i];
                int blockIndex = receivedBlock.Index;

                if (blockIndex < _blockIndices.Count)
                {
                    Block localBlock = GetBlock(blockIndex);
                    removedBlocks.Add(localBlock);
                }

                int fileIndex = blockIndex / BlockFile.BlocksPerFile;
                if (_blockFileByIndex.ContainsKey(fileIndex))
                {
                    var blockFile = _blockFileByIndex[fileIndex];
                    if (receivedBlock.Index > blockFile.EndIndex)
                        blockFile.AddBlock(receivedBlock);
                    else
                        blockFile.ReplaceBlock(receivedBlock);
                }
                else
                {
                    var blockFile = BlockFile.Read(blockIndex);
                    if (blockFile != null)
                    {
                        if (receivedBlock.Index > blockFile.EndIndex)
                            blockFile.AddBlock(receivedBlock);
                        else
                            blockFile.ReplaceBlock(receivedBlock);
                    }
                    else
                    {
                        int startIndexInFile = fileIndex * BlockFile.BlocksPerFile;
                        if (blockIndex != startIndexInFile)
                            throw new System.InvalidOperationException("Attempting to add a block in the middle of a non-existing block file.");

                        blockFile = new BlockFile(receivedBlock);
                    }

                    _blockFileByIndex.Add(fileIndex, blockFile);
                }

                if (blockIndex < _blockIndices.Count)
                {
                    _blockIndices[blockIndex].Hash = receivedBlock.Hash;
                }
                else
                {
                    _blockIndices.Add(new BlockIndex { Index = blockIndex, Hash = receivedBlock.Hash });
                }

                _blocks[blockIndex] = receivedBlock;
            }

            Save();
        }

        /// <summary>
        /// Tries to find and return a block having the given hash.
        /// </summary>
        /// <remarks>This method's complexity is O(n).</remarks>
        public Block GetBlock(string hash)
        {
            for (int i = 0, c = _blockIndices.Count; i < c; ++i)
            {
                var idx = _blockIndices[i];
                if (idx.Hash == hash)
                    return GetBlock(idx.Index);
            }
            return null;
        }

        /// <summary>
        /// Returns the block referenced by the given index.
        /// If the block is not in memory, reads the block file.
        /// </summary>
        public Block GetBlock(BlockIndex blockIndex)
        {
            return GetBlock(blockIndex.Index);
        }

        /// <summary>
        /// Returns the block referenced by the given index.
        /// If the block is not in memory, reads the block file.
        /// </summary>
        public Block GetBlock(int blockIndex)
        {
            if (!_blocks.TryGetValue(blockIndex, out var block))
            {
                int fileIndex = blockIndex / BlockFile.BlocksPerFile;
                if (!_blockFileByIndex.TryGetValue(fileIndex, out var blockFile))
                {
                    blockFile = BlockFile.Read(blockIndex);
                    if (blockFile == null)
                        return null;

                    _blockFileByIndex.Add(fileIndex, blockFile);
                }

                for (int i = 0, blkIdx = blockFile.StartIndex, c = blockFile.Blocks.Count; i < c; ++i, ++blkIdx)
                {
                    var blk = blockFile.Blocks[i];

                    if (!_blocks.ContainsKey(blkIdx))
                    {
                        _blocks.Add(blkIdx, blk);
                    }

                    if (blk.Index == blockIndex)
                    {
                        block = blk;
                    }
                }
            }

            return block;
        }

        private sealed class IndexFile : DbFile
        {
            [JsonProperty("block_indices")]
            public List<BlockIndex> BlockIndices;
        }
    }
}