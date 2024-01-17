using System.IO.Pipes;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace MultiPing
{
    internal class Program
    {
        private static ManualResetEvent startEvent = new ManualResetEvent(false);
        private static ManualResetEvent endEvent = new ManualResetEvent(false);

        private static bool exit = false;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                PrintUsage();
                return;
            }

            var dontFragment = false;
            var timeout = 4000;
            var rate = 1000;
            uint bufferSize = 32;
            var tTl = 64;
            var addresses = new string[0];
            var hostSpecified = false;

            for (int i = 0; i< args.Length; i++)
            {
                switch (args[i])
                {
                    case "/f":
                    case "-f":
                        {
                            dontFragment = true;
                            break;
                        }

                    case "/w":
                    case "-w":
                        {
                            if (!int.TryParse(args[++i], out timeout))
                            {
                                Console.WriteLine("Unable to parse value for timeout");
                                return;
                            }
                            break;
                        }

                    case "/i":
                    case "-i":
                        {
                            if (!int.TryParse(args[++i], out tTl))
                            {
                                Console.WriteLine("Unable to parse value for TTL");
                                return;
                            }
                            break;
                        }

                    case "/r":
                    case "-r":
                        {
                            if (!int.TryParse(args[++i], out rate))
                            {
                                Console.WriteLine("Unable to parse value for rate");
                                return;
                            }
                            break;
                        }

                    case "/l":
                    case "-l":
                        {
                            if (!uint.TryParse(args[++i], out bufferSize))
                            {
                                Console.WriteLine("Unable to parse value for send buffer size");
                                return;
                            }
                            break;
                        }

                    default:
                        {
                            addresses = args[0].Split(',');
                            hostSpecified = true;
                            break;
                        }
                }
            }

            if (!hostSpecified)
            {
                Console.WriteLine("Must specify target");
                PrintUsage();
                return;
            }

            var pingThreads = new Dictionary<PingWorkInfo, Thread>();
            var pingFinished = new List<WaitHandle>();

            var pingOptions = new PingOptions(tTl, dontFragment);

            Console.CancelKeyPress += Console_CancelKeyPress;

            foreach (var address in addresses)
            {

                var pingWorkInfo = new PingWorkInfo(address, pingOptions, bufferSize, timeout);
                var thread = new Thread(WorkerThread)
                {
                    IsBackground = true
                };

                pingThreads.Add(pingWorkInfo, thread);
                pingFinished.Add(pingWorkInfo.FinishedEvent.WaitHandle);
                thread.Start(pingWorkInfo);
            }

            Console.WriteLine($"Sending ping(s)....");

            foreach (var pingInfo in pingThreads.Keys)
            {
                Console.Write("");
            }

            while (!exit)
            {
                endEvent.Reset();
                startEvent.Set();

                WaitHandle.WaitAll(pingFinished.ToArray());

                var now = DateTime.Now;

                Console.Write($"{now:HH:mm:ss}\t");

                foreach (var pingInfo in pingThreads.Keys)
                {
                    Console.Write($"{pingInfo.LastRoundtripTime,10}ms\t");
                }

                Console.WriteLine();

                startEvent.Reset();
                endEvent.Set();

                Thread.Sleep(rate);
            }

            Console.WriteLine("Finished");
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            exit = true;
            e.Cancel = true;
        }

        private static void WorkerThread(object? data)
        {
            if (data is not PingWorkInfo pingData)
            {
                return;
            }

            Console.WriteLine($"Starting worker thread for {pingData.Address}");

            var random = new Random();

            var pingBuffer= new byte[pingData.BufferSize];
            for (var i = 0; i < pingBuffer.Length; i++)
            {
                pingBuffer[i] = (byte)random.Next(byte.MaxValue);
            }

            var ping = new Ping();

            while (true)
            {
                startEvent.WaitOne();
                if (exit)
                {
                    break;
                }

                try
                {
                    var pingResult = ping.Send(pingData.Address, pingData.Timeout, pingBuffer, pingData.Options);

                    pingData.LastStatus = pingResult.Status;
                    if (pingResult.Status == IPStatus.Success)
                    {
                        pingData.LastRoundtripTime = pingResult.RoundtripTime;
                    }

                    pingData.FinishedEvent.Set();
                    endEvent.WaitOne();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception in sending ping: {ex.Message} for {pingData.Address}");
                    pingData.FatalError = true;
                    break;
                }
            };

            pingData.FinishedEvent.Set();
            Console.WriteLine($"Exiting worker thread for {pingData.Address}");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: multiping [-l size] [-f] [-i TTL] [-r rate]");
            Console.WriteLine("                 [-w timeout] target_name[,target2_name][,targetXXX_name]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("    -l size        Send buffer size.");
            Console.WriteLine("    -f             Set Don't Fragment flag in packet (IPv4-only).");
            Console.WriteLine("    -i TTL         Time To Live. (64 default)");
            Console.WriteLine("    -r rate        Rate of pings. (1000ms default).");
            Console.WriteLine("    -w timeout     Timeout in milliseconds to wait for each reply. (4000ms default)");
        }

        private class PingWorkInfo
        {
            internal ManualResetEventSlim FinishedEvent { get; set; }

            internal PingOptions Options { get; set; }

            internal string Address { get; set; }

            internal int Timeout { get; set; }

            internal uint BufferSize { get; set; }

            internal long LastRoundtripTime { get; set; }

            internal IPStatus LastStatus { get; set; }

            internal bool FatalError { get; set; }

            internal PingWorkInfo(string address, PingOptions pingOptions, uint bufferSize, int timeout)
            {
                this.FinishedEvent = new ManualResetEventSlim();

                this.Address = address;
                this.Options = pingOptions;
                this.BufferSize = bufferSize > 65500 ? 65500 : bufferSize;
                this.Timeout = timeout;
            }
        }
    }
}
