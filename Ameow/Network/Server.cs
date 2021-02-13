using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Ameow.Network
{
    /// <summary>
    /// Instantiated to listen to remote peers. Wrapper of <see cref="TcpListener"/>.
    /// </summary>
    public sealed class Server
    {
        private readonly int port;
        private readonly TcpListener listener;
        private readonly App.ILogger logger;

        public event Action<Context, Message> OnMessageReceived;
        public event Action<Context> OnClientConnected;

        public Server(int port, App.ILogger logger)
        {
            this.port = port;
            this.listener = new TcpListener(IPAddress.Any, port);
            this.logger = logger;
        }

        /// <summary>
        /// Starts listening to remote connections in a loop that only ends when <paramref name="cancellationToken"/> is marked cancelled.
        /// </summary>
        public async void StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                logger.Log(App.LogLevel.Error, "Cannot listen: " + ex.Message);
                throw;
            }

            logger.Log(App.LogLevel.Info, "Listening at port " + port);

            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync();
                var task = processClientAsync(client, cancellationToken);

                if (task.IsFaulted)
                    await task;
            }

            listener.Stop();
        }

        /// <summary>
        /// Starts a thread to communicate with a remote peer.
        /// </summary>
        private async Task processClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            Context ctx = new Context(client, client.Client.RemoteEndPoint.ToString(), isOutbound: true);
            ctx.OnMessageReceived += oPeerMessageReceived;

            OnClientConnected?.Invoke(ctx);

            try
            {
                await ctx.RunLoop(cancellationToken);
            }
            catch (TaskCanceledException)
            {
            }
            catch (System.IO.IOException)
            {
                ctx.ShouldDisconnect = true;
            }
            finally
            {
                client.Close();
            }
        }

        private void oPeerMessageReceived(Context peerContext, Message message)
        {
            OnMessageReceived?.Invoke(peerContext, message);
        }
    }
}