using Serilog;
using SimpleSocketServer.Server;

namespace SimpleSocketServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

            // Could also set MinimumLevel.Debug()

            // Could also write to a file:
            // .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)

            try
            {
                // just random port used for F5 debug
                /*
                if (args.Length == 0)
                {
                    args = new string[] { "45577" };
                }
                */

                if (args.Length == 0 || !int.TryParse(args[0], out int port))
                {
                    Log.Error("No valid port argument specified. Exiting.");
                    return;
                }

                var server = new SocketServer(port, Log.Logger);

                // Handle Ctrl+C for graceful shutdown
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true; // Prevent immediate termination
                    server.Stop();
                };

                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Server encountered a fatal error.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
