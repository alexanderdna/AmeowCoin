using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Ameow.Storage
{
    /// <summary>
    /// Represents a blkXXXXX.json file loaded into in memory.
    /// </summary>
    public sealed class BlockFile : DbFile
    {
        public const int BlocksPerFile = 100;
        public const string FileNameFormat = "blk{0:D5}.json";

        [JsonProperty("start_index")]
        public readonly int StartIndex;

        [JsonProperty("end_index")]
        public int EndIndex { get; private set; }

        [JsonProperty("blocks")]
        public readonly List<Block> Blocks;

        // For JSON
        public BlockFile()
        {
        }

        public BlockFile(Block firstBlock)
        {
            StartIndex = firstBlock.Index;
            EndIndex = StartIndex;
            Blocks = new List<Block> { firstBlock };
        }

        /// <summary>
        /// Adds a new block to the file.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">File is full of blocks.</exception>
        /// <exception cref="System.ArgumentException">Index of the given block is not equal to EndIndex + 1.</exception>
        public void AddBlock(Block block)
        {
            if (Blocks.Count == BlocksPerFile)
                throw new System.InvalidOperationException("File is full of blocks.");

            if (block.Index != EndIndex + 1)
                throw new System.ArgumentException("Block index is not EndIndex + 1");

            EndIndex += 1;
            Blocks.Add(block);
        }

        /// <summary>
        /// Replaces an existing block with a new block.
        /// </summary>
        public void ReplaceBlock(Block newBlock)
        {
            Blocks[newBlock.Index] = newBlock;
            newBlock.IsSaved = false;
        }

        /// <summary>
        /// Checks if any blocks in this file are not saved.
        /// </summary>
        /// <returns>True if some blocks are not saved.</returns>
        public bool IsDirty()
        {
            // Traverse backward because earlier blocks are more likely to be in file.
            for (int i = Blocks.Count - 1; i >= 0; --i)
            {
                if (Blocks[i].IsSaved is false)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Marks all blocks stored in this file as saved.
        /// </summary>
        public void MarkBlocksSaved()
        {
            for (int i = 0, c = Blocks.Count; i < c; ++i)
            {
                Blocks[i].IsSaved = true;
            }
        }

        /// <summary>
        /// Reads a block file into memory. This method will not validate its blocks.
        /// </summary>
        /// <param name="includedIndex">Index of a block that can be included in the file.
        /// Helps finding the correct file name.</param>
        public static BlockFile Read(int includedIndex)
        {
            var fileIndex = includedIndex / BlocksPerFile;
            var fileName = string.Format(FileNameFormat, fileIndex);
            var filePath = Config.GetBlockFilePath(fileName);
            if (!File.Exists(filePath))
                return null;

            var file = Read<BlockFile>(filePath);
            for (int i = 0, c = file.Blocks.Count; i < c; ++i)
            {
                file.Blocks[i].IsSaved = true;
            }
            return file;
        }

        /// <summary>
        /// Writes the given file to disk.
        /// </summary>
        public static void Write(BlockFile file)
        {
            int fileIndex = file.StartIndex / BlocksPerFile;
            var fileName = string.Format(FileNameFormat, fileIndex);
            var filePath = Config.GetBlockFilePath(fileName);
            Write(filePath, file);
        }
    }
}