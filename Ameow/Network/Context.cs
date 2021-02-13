using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ameow.Network
{
    /// <summary>
    /// Represents a peer node. Provides an interface for exchanging messages with the peer.
    /// TCP message-framing communication code is within this class, if you need it.
    /// </summary>
    public sealed class Context
    {
        /// <summary>
        /// The maximum length a message can have.
        /// Messages longer than this will be discarded
        /// and the peer will be marked for disconnection.
        /// </summary>
        public const int MaxMessageLength = 4096 * 1024; // 4MB

        /// <summary>
        /// Fixed size for in/out message buffers.
        /// </summary>
        private const int bufferSize = 1024;

        /// <summary>
        /// Underlying TCP client object.
        /// </summary>
        public readonly TcpClient Client;

        /// <summary>
        /// Address of the peer node in ip:port format.
        /// </summary>
        public readonly string ClientEndPoint;

        /// <summary>
        /// True if the connection was established by remote peer.
        /// </summary>
        public readonly bool IsOutbound;

        /// <summary>
        /// Used to build received message strings.
        /// </summary>
        private readonly StringBuilder sb = new StringBuilder();

        private readonly Queue<Message> inMessageQueue = new Queue<Message>();
        private readonly byte[] inBuffer = new byte[bufferSize];
        private readonly char[] inBufferAsString = new char[bufferSize];

        private readonly Queue<Message> outMessageQueue = new Queue<Message>();
        private readonly byte[] outBuffer = new byte[bufferSize];

        /// <summary>
        /// Indices of the blocks sent from this peer.
        /// Used to keep track of blocks.
        /// </summary>
        private readonly HashSet<int> storedBlockIndices = new HashSet<int>();

        /// <summary>
        /// Blocks sent from this peer.
        /// Used to keep track of blocks.
        /// </summary>
        private readonly List<Block> storedBlocks = new List<Block>();

        /// <summary>
        /// Set to true by <see cref="Daemon"/> after Version messages have been exchanged.
        /// </summary>
        public bool HasHandshake { get; set; } = false;

        /// <summary>
        /// Set to true by many tasks to mark this peer node for disconnection
        /// in the next house keeping loop.
        /// </summary>
        public bool ShouldDisconnect { get; set; } = false;

        /// <summary>
        /// When the last message was sent from this peer.
        /// </summary>
        public DateTime LastMessageInTime { get; private set; }

        /// <summary>
        /// When the last Ping message was sent to this peer.
        /// Used by <see cref="Daemon"/> in house keeping.
        /// </summary>
        public DateTime LastPingTime { get; set; }

        /// <summary>
        /// Version number sent from this peer.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Last received chain height from this peer.
        /// </summary>
        public int LastHeight { get; private set; }

        /// <summary>
        /// Triggered when a message from this peer was received, parsed and is ready for processing.
        /// </summary>
        public event Action<Context, Message> OnMessageReceived;

        public Context(TcpClient client, string endPoint, bool isOutbound)
        {
            Client = client;
            ClientEndPoint = endPoint;
            IsOutbound = isOutbound;
        }

        /// <summary>
        /// Runs the message IO loop for the peer node.
        /// </summary>
        public async Task RunLoop(CancellationToken cancellationToken)
        {
            LastMessageInTime = DateTime.Now;
            LastPingTime = LastMessageInTime;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                await readAsync();

                // FIXME: message queues are not locked for Enqueue/Dequeue,
                // which might be risky in a multi-threaded usage scenario.

                while (inMessageQueue.Count > 0 && ShouldDisconnect is false)
                {
                    LastMessageInTime = DateTime.Now;

                    // Also update ping timestamp to reduce unnecessary pings
                    LastPingTime = LastMessageInTime;

                    var msg = inMessageQueue.Dequeue();
                    OnMessageReceived?.Invoke(this, msg);
                }

                while (outMessageQueue.Count > 0 && ShouldDisconnect is false)
                {
                    var msg = outMessageQueue.Dequeue();
                    await writeAsync(msg);
                }

                await Task.Delay(100, cancellationToken);
            }
        }

        /// <summary>
        /// Immediately closes the connection.
        /// </summary>
        public void Close()
        {
            try
            {
                Client.GetStream().Close();
                Client.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Adds the given message to the message queue.
        /// The message will be sent as soon as possible.
        /// </summary>
        public void SendMessage(Message msg)
        {
            outMessageQueue.Enqueue(msg);
        }

        private async Task readAsync()
        {
            // How it works:
            // Messages are serialized as strings delimited by '\n' character.
            // They are read from the network stream and sometimes we can't
            // just read one whole message string in one ReadAsync call.
            // So we will add read characters to a string builder and call it
            // a complete message when encountering a '\n' character. Then
            // we clear the builder and add what would be the beginning of the
            // next message.

            if (ShouldDisconnect is false && Client.Available > 0)
            {
                var stream = Client.GetStream();

                int nRead = await stream.ReadAsync(inBuffer.AsMemory(0, inBuffer.Length));
                if (nRead == 0) return;

                // Assuming messages consist of only ASCII characters.
                // This will speed up reading because Encoding.UTF8.GetString is not used.
                for (int i = 0; i < nRead; ++i)
                {
                    inBufferAsString[i] = (char)(inBuffer[i] & 0x7f);
                }

                // Read characters may consist of: part of 1 message, 1 message or many messages.
                
                int pos, posNewLine = 0;
                do
                {
                    pos = -1;
                    for (int i = posNewLine; i < nRead; ++i)
                    {
                        if (inBufferAsString[i] == '\n')
                        {
                            pos = i;
                            break;
                        }
                    }

                    if (pos >= 0)
                    {
                        // Delimiter found. Try to complete the latest message and clear sb.

                        sb.Append(inBufferAsString, posNewLine, pos - posNewLine);

                        // Message is too long. Disconnect the peer now because we don't want any troubles.
                        if (sb.Length > MaxMessageLength)
                        {
                            sb.Clear();
                            ShouldDisconnect = true;
                            break;
                        }

                        posNewLine = pos + 1;

                        var msgJson = sb.ToString();
                        try
                        {
                            var msg = Message.Deserialize(msgJson);
                            inMessageQueue.Enqueue(msg);
                        }
                        catch (Newtonsoft.Json.JsonException)
                        {
                            ShouldDisconnect = true;
                        }

                        sb.Length = 0;
                    }
                    else
                    {
                        // Delimiter not found. Add what we have read to sb and wait for more data to be received.

                        sb.Append(inBufferAsString, posNewLine, nRead - posNewLine);

                        // Message is too long. Disconnect the peer now because we don't want any troubles.
                        if (sb.Length > MaxMessageLength)
                        {
                            sb.Clear();
                            ShouldDisconnect = true;
                            break;
                        }
                    }
                } while (pos >= 0 && pos < nRead);
            }
        }

        private async Task writeAsync(Message msg)
        {
            var msgJson = Message.Serialize(msg);
            var stream = Client.GetStream();
            int pos = 0, nRemaining = msgJson.Length;
            while (nRemaining > 0 && ShouldDisconnect is false)
            {
                int bytesToCopy = Math.Min(outBuffer.Length, msgJson.Length - pos);
                for (int i = pos, j = 0, c = pos + bytesToCopy; i < c; ++i, ++j)
                {
                    outBuffer[j] = (byte)(msgJson[i] & 0x7f);
                }
                nRemaining -= bytesToCopy;
                pos += bytesToCopy;

                await stream.WriteAsync(outBuffer.AsMemory(0, bytesToCopy));
            }

            stream.WriteByte((byte)'\n');
            await stream.FlushAsync();
        }

        /// <summary>
        /// Updates chain height of the peer node.
        /// </summary>
        public void UpdateHeightIfHigher(int height)
        {
            if (height > LastHeight)
                LastHeight = height;
        }

        /// <summary>
        /// Adds the received block to temporary collections for later usage.
        /// </summary>
        public void StoreReceivedBlocks(Block block)
        {
            if (storedBlockIndices.Contains(block.Index))
                return;

            storedBlockIndices.Add(block.Index);
            storedBlocks.Add(block);
        }

        /// <summary>
        /// Adds the received blocks to temporary collections for later usage.
        /// </summary>
        public void StoreReceivedBlocks(IList<Block> blocks)
        {
            for (int i = 0, c = blocks.Count; i < c; ++i)
            {
                var block = blocks[i];
                if (storedBlockIndices.Contains(block.Index))
                    continue;

                storedBlockIndices.Add(block.Index);
                storedBlocks.Add(block);
            }
        }

        /// <summary>
        /// Creates and returns a list consisting of stored and given blocks.
        /// The list is sorted by block index.
        /// </summary>
        public List<Block> GetStoredAndNewBlocks(Block newBlock)
        {
            var list = new List<Block>(storedBlocks.Count + 1);
            list.AddRange(storedBlocks);

            if (storedBlockIndices.Contains(newBlock.Index) is false)
                list.Add(newBlock);

            list.Sort((a, b) => a.Index - b.Index);

            return list;
        }

        /// <summary>
        /// Creates and returns a list consisting of stored and given blocks.
        /// The list is sorted by block index.
        /// </summary>
        public List<Block> GetStoredAndNewBlocks(IList<Block> newBlocks)
        {
            var list = new List<Block>(storedBlocks.Count + newBlocks.Count);
            list.AddRange(storedBlocks);

            for (int i = 0, c = newBlocks.Count; i < c; ++i)
            {
                var block = newBlocks[i];
                if (storedBlockIndices.Contains(block.Index))
                    continue;

                list.Add(block);
            }
            list.Sort((a, b) => a.Index - b.Index);

            return list;
        }

        public void ClearStoredBlocks()
        {
            storedBlockIndices.Clear();
            storedBlocks.Clear();
        }
    }
}