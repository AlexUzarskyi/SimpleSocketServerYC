namespace SimpleSocketServer.Models
{
    public class ClientSession
    {
        public System.Net.Sockets.TcpClient TcpClient { get; set; } = default!;
        public int Sum { get; set; }
    }
}