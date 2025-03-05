using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Serilog;
using SimpleSocketServer.Models;
using System.Text;

namespace SimpleSocketServer.Server
{
    public class SocketServer
    {
        private readonly TcpListener _listener;
        private bool _isRunning;

        private readonly ConcurrentDictionary<string, ClientSession> _clients
            = new ConcurrentDictionary<string, ClientSession>();

        private readonly ILogger _logger;

        public int Port { get; }

        public SocketServer(int port, ILogger logger)
        {
            _logger = logger;
            _listener = new TcpListener(IPAddress.Any, port);
            Port = port;
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            _logger.Information("Server started on port {Port}.", Port);

            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (SocketException ex) when (!_isRunning)
                {
                    _logger.Debug("AcceptTcpClientAsync threw {Exception} after stopping server.", ex.Message);
                    break;
                }
                catch (ObjectDisposedException ex) when (!_isRunning)
                {
                    _logger.Debug("AcceptTcpClientAsync threw an exception {Exception} during object disposal.", ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unexpected error in server accept loop.");
                }
            }

            _logger.Information("Server accept loop has ended.");
        }

        public void Stop()
        {
            _isRunning = false;
            _logger.Information("Stopping the server...");
            _listener.Stop();
        }

        private async Task HandleClientAsync(TcpClient tcpClient)
        {

            var clientId = tcpClient.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
            var session = new ClientSession { TcpClient = tcpClient, Sum = 0 };
            _clients[clientId] = session;

            _logger.Information("Client {ClientId} connected.", clientId);

            await SendMessageAsync(tcpClient, "Welcome! Please enter an integer number or 'list' command.\r\n");

            try
            {
                await ReadClientDataAsync(clientId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in client {ClientId} session.", clientId);
            }
            finally
            {
                CloseClient(clientId);
            }
        }

        private async Task ReadClientDataAsync(string clientId)
        {

            if (!_clients.TryGetValue(clientId, out var session))
                return;

            var tcpClient = session.TcpClient;
            var stream = tcpClient.GetStream();
            var buffer = new byte[1024];
            var receivedData = new StringBuilder();

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    // client disconnected
                    _logger.Debug("Client {ClientId} disconnected (read returned 0).", clientId);
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                receivedData.Append(chunk);

                // Check if the received data contains a newline
                if (chunk.Contains("\n"))
                {
                    string completeMessage = receivedData.ToString().Trim().TrimEnd('\r', '\n');

                    _logger.Debug("Received from {ClientId}: {Input}", clientId, receivedData);

                    if (string.Equals(completeMessage, "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        break; // Exit the loop to close the connection
                    }

                    await HandleClientInputAsync(clientId, completeMessage);

                    receivedData.Clear();
                }
            }
        }

        private async Task HandleClientInputAsync(string clientId, string input)
        {
            if (!_clients.TryGetValue(clientId, out var session))
                return;

            var tcpClient = session.TcpClient;
            int sum = session.Sum;

            if (string.Equals(input, "list", StringComparison.OrdinalIgnoreCase))
            {
                await SendListOfClientsAsync(tcpClient);
            }
            else if (int.TryParse(input, out int number))
            {
                sum += number;
                session.Sum = sum;
                _clients[clientId] = session;

                await SendMessageAsync(tcpClient, $"Current sum: {sum}\r\n");
            }
            else
            {
                await SendMessageAsync(tcpClient, "Error. Enter a valid integer or 'list' command.\r\n");
            }
        }

        private async Task SendListOfClientsAsync(TcpClient tcpClient)
        {
            var sb = new System.Text.StringBuilder("List of connected clients:\r\n");
            foreach (var kvp in _clients)
            {
                sb.AppendLine($" - {kvp.Key}, sum: {kvp.Value.Sum}");
            }
            sb.AppendLine();

            await SendMessageAsync(tcpClient, sb.ToString());
        }

        private async Task SendMessageAsync(TcpClient tcpClient, string message)
        {
            if (!tcpClient.Connected) return;

            try
            {
                var stream = tcpClient.GetStream();
                var msgBytes = Encoding.UTF8.GetBytes(message);
                await stream.WriteAsync(msgBytes, 0, msgBytes.Length);

                _logger.Debug("Sent {Length} bytes to client.", msgBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error sending message to client.");
            }
        }

        private void CloseClient(string clientId)
        {
            if (_clients.TryRemove(clientId, out var session))
            {
                _logger.Information("Client {ClientId} disconnected.", clientId);

                try
                {
                    session.TcpClient.Client.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                    // Ignore possible errors if the socket is already closed.
                }

                try
                {
                    var stream = session.TcpClient.GetStream();
                    stream.Close();
                    stream.Dispose();
                }
                catch
                {
                    // Ignore errors during closure
                }

                try
                {
                    session.TcpClient.Close();
                    session.TcpClient.Dispose();
                }
                catch
                {
                    // Ignore errors during closure
                }
            }
        }
    }
}