using System;
using System.Collections.Generic;

namespace Ameow.Network
{
    /// <summary>
    /// Stores states for Initial Block Download progress.
    /// </summary>
    public sealed class InitialBlockDownload
    {
        private sealed class PeerInfo
        {
            public Context Context;
            public bool IsReady;
            public Block LatestBlock;
            public DateTime RequestTime;
            public DateTime ResponseTime;
            public bool IsRemoved;
        }

        public class GetBlocksRange
        {
            public readonly int StartIndex;
            public readonly int Count;

            public GetBlocksRange(int startIndex, int count)
            {
                StartIndex = startIndex;
                Count = count;
            }
        }

        public enum Phase
        {
            None,
            Preparing,
            Running,
            Succeeded,
            Failed,
        }

        private List<PeerInfo> _peers;
        private int _currentPeerIndex;

        private List<GetBlocksRange> _getBlocksRanges;
        private int _currentGetBlocksRangeIndex;

        public Phase CurrentPhase { get; private set; } = Phase.None;

        public bool IsRunning => CurrentPhase is Phase.Running;
        public bool IsDone => CurrentPhase is Phase.None or Phase.Succeeded or Phase.Failed;

        public int LocalBlockIndex { get; private set; }
        public int ReceivedBlockIndex { get; private set; }

        public InitialBlockDownload()
        {
            _peers = new List<PeerInfo>();
            _currentPeerIndex = -1;
        }

        public void Prepare()
        {
            CurrentPhase = Phase.Preparing;
        }

        public bool HasPeer(Context ctx)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var p = _peers[i];
                if (p.Context == ctx)
                    return true;
            }

            return false;
        }

        public void AddPeer(Context ctx)
        {
            var peer = new PeerInfo
            {
                Context = ctx,
                IsReady = false,
                LatestBlock = null,
            };
            _peers.Add(peer);
        }

        public void RemovePeer(Context ctx)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var peer = _peers[i];
                if (peer.Context == ctx)
                {
                    peer.IsRemoved = true;
                    break;
                }
            }
        }

        public void MarkPeerReady(Context ctx)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var p = _peers[i];
                if (p.Context == ctx)
                {
                    p.IsReady = true;
                    break;
                }
            }
        }

        public void MarkRequestTime(Context ctx)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var p = _peers[i];
                if (p.Context == ctx)
                {
                    p.RequestTime = DateTime.UtcNow;
                    break;
                }
            }
        }

        public void ReceiveLatestBlock(Context ctx, Block block)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var p = _peers[i];
                if (p.Context == ctx)
                {
                    p.LatestBlock = block;
                    p.ResponseTime = DateTime.UtcNow;
                    break;
                }
            }
        }

        /// <summary>
        /// Returns true if all peers have sent the LatestBlock message.
        /// </summary>
        public bool AllPeersResponded()
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var peer = _peers[i];
                if (peer.IsReady is false || peer.LatestBlock is null) return false;
            }
            return true;
        }

        /// <summary>
        /// Sorts current peers by latest block index.
        /// Peers having the same block indices will be sorted by response time.
        /// </summary>
        public void SortPeers()
        {
            _peers.Sort((a, b) =>
            {
                if (a.LatestBlock == null && b.LatestBlock == null)
                    return 0;
                else if (a.LatestBlock == null)
                    return 1;
                else if (b.LatestBlock == null)
                    return -1;
                else
                {
                    if (a.LatestBlock.Index == b.LatestBlock.Index)
                        return (a.ResponseTime - a.RequestTime) > (b.ResponseTime - b.RequestTime) ? -1 : 1;
                    else
                        return b.LatestBlock.Index - a.LatestBlock.Index;
                }
            });
        }

        public void Start()
        {
            CurrentPhase = Phase.Running;

            _currentPeerIndex = -1;
        }

        public void Succeed()
        {
            CurrentPhase = Phase.Succeeded;
        }

        public void Fail()
        {
            CurrentPhase = Phase.Failed;
        }

        public (Context, Block) GetCurrentPeer()
        {
            var bestPeer = tryGetCurrentPeer();
            if (bestPeer != null)
            {
                return (bestPeer.Context, bestPeer.LatestBlock);
            }
            else
            {
                return (null, null);
            }
        }

        private PeerInfo tryGetCurrentPeer() => _currentPeerIndex >= 0 && _currentPeerIndex < _peers.Count ? _peers[_currentPeerIndex] : null;

        /// <summary>
        /// Tries to select the next working peer.
        /// A working peer is one that has sent LatestBlock and is not marked for removal.
        /// </summary>
        /// <returns>True if a working peer is available.</returns>
        public bool NextPeer()
        {
            ++_currentPeerIndex;
            while (_currentPeerIndex < _peers.Count)
            {
                var peer = _peers[_currentPeerIndex];
                if (peer.IsRemoved
                    || peer.Context == null
                    || peer.LatestBlock == null)
                {
                    ++_currentPeerIndex;
                }
                else
                {
                    break;
                }
            }

            return _currentPeerIndex < _peers.Count;
        }

        /// <summary>
        /// Returns true if there is no more working peers to select.
        /// </summary>
        public bool IsOutOfPeer()
        {
            return _currentPeerIndex >= _peers.Count;
        }

        /// <summary>
        /// Returns true if the given context is of the current selected peer.
        /// </summary>
        public bool IsCommunicatingWith(Context ctx)
        {
            var peer = tryGetCurrentPeer();
            return peer != null && peer.Context == ctx;
        }

        /// <summary>
        /// Prepares the list of ranges for GetBlocks messages.
        /// </summary>
        /// <param name="localIndex">Index of the local block. Ranges will start after this index.</param>
        /// <param name="receivedIndex">Index of the received block. Ranges will stop after this index.</param>
        public void PrepareGetBlocksRanges(int localIndex, int receivedIndex)
        {
            LocalBlockIndex = localIndex;
            ReceivedBlockIndex = receivedIndex;

            _getBlocksRanges = new List<GetBlocksRange>();
            _currentGetBlocksRangeIndex = 0;

            for (int i = localIndex + 1; i <= receivedIndex; i += Config.MaxGetBlocksCount)
            {
                int startIndex = i;
                int count = Config.MaxGetBlocksCount;
                _getBlocksRanges.Add(new GetBlocksRange(startIndex, count));
            }
        }

        public GetBlocksRange CurrentGetBlocksRange()
        {
            return _currentGetBlocksRangeIndex < _getBlocksRanges.Count ? _getBlocksRanges[_currentGetBlocksRangeIndex] : null;
        }

        public void ProceedToNextGetBlocksRange()
        {
            ++_currentGetBlocksRangeIndex;
        }

        /// <summary>
        /// Collects the peers whose heights are lower than the given height.
        /// </summary>
        /// <param name="height">The height to compare.</param>
        /// <param name="containers">The list to store the collected peer contexts.</param>
        public void GetPeersLower(int height, List<Context> containers)
        {
            for (int i = 0, c = _peers.Count; i < c; ++i)
            {
                var peer = _peers[i];
                if (peer.LatestBlock != null && peer.LatestBlock.Index < height)
                    containers.Add(peer.Context);
            }
        }
    }
}