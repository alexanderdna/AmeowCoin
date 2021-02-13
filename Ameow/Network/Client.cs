using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ameow.Network
{
    /// <summary>
    /// Represents the connection to a remote peer. Wrapper of <see cref="Network.Context"/>.
    /// Instantiated in <see cref="Daemon.AcceptPeerList"/>.
    /// </summary>
    public sealed class Client
    {
        private readonly string remoteHost;
        private readonly int remotePort;
        private readonly App.ILogger logger;

        private TcpClient _client;
        private Context _context;

        public event Action<Context, Message> OnMessageReceived;
        
        public Context Context => _context;

        public Client(string remoteHost, int remotePort, App.ILogger logger)
        {
            this.remoteHost = remoteHost;
            this.remotePort = remotePort;
            this.logger = logger;

            _client = new TcpClient();
            _context = new Context(_client, string.Concat(remoteHost, ":", remotePort), isOutbound: false);
            _context.OnMessageReceived += (peerCtx, msg) => { OnMessageReceived?.Invoke(peerCtx, msg); };
        }

        public bool Connect()
        {
            try
            {
                _client.Connect(remoteHost, remotePort);
            }
            catch (SocketException ex)
            {
                logger.Log(App.LogLevel.Error, "Cannot connect to peer: " + ex.Message);
                return false;
            }

            return true;
        }

        public async void StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _context.RunLoop(cancellationToken);
            }
            catch (TaskCanceledException)
            {
            }
            catch (System.IO.IOException)
            {
                _context.ShouldDisconnect = true;
            }
            finally
            {
                _client.Close();
            }
        }

        public void SendMessage(Message msg)
        {
            _context.SendMessage(msg);
        }
    }
}