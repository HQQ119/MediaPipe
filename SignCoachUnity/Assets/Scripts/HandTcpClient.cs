using System;
using System.Net.Sockets;
using System.Threading;

namespace SignCoach
{
    public sealed class HandTcpClient : IDisposable
    {
        readonly object gate = new object();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly string host;
        readonly int port;
        Thread worker;
        TcpClient socket;
        HandFrame latest;
        long revision;
        bool connected;
        int disposed;
        string status = "Disconnected";
        public event Action<HandFrame, string> ValidFrameReceived;
        public event Action<string> StreamInterrupted;
        public bool WorkerAlive { get { return worker != null && worker.IsAlive; } }
        public long ReceivedFrames { get; private set; }

        public HandTcpClient(string host = "127.0.0.1", int port = 5005) { this.host = host; this.port = port; }
        public void Start()
        {
            if (worker != null) throw new InvalidOperationException("Already started");
            worker = new Thread(ReceiveLoop) { IsBackground = true, Name = "SignCoach TCP" };
            worker.Start();
        }
        public void Snapshot(out HandFrame frame, out long version, out bool isConnected, out string message)
        {
            lock (gate) { frame = latest; version = revision; isConnected = connected; message = status; }
        }
        void State(bool active, string text)
        {
            lock (gate) { connected = active; status = text; latest = null; revision++; }
        }
        void ReceiveLoop()
        {
            while (!stop.WaitOne(0))
            {
                TcpClient client = null;
                try
                {
                    State(false, "Connecting / retrying...");
                    client = new TcpClient { NoDelay = true, ReceiveTimeout = 500 };
                    // 保存底层句柄，避免 Mono 在 TcpClient.Close 后把 Client 置空，
                    // 导致 EndConnect/Receive 与退出并发时出现空引用。
                    var transport = client.Client;
                    lock (gate) { socket = client; }
                    var connection = transport.BeginConnect(host, port, null, null);
                    using (var wait = connection.AsyncWaitHandle)
                    {
                        if (!wait.WaitOne(1000)) throw new TimeoutException("Connect timeout");
                        transport.EndConnect(connection);
                    }
                    if (stop.WaitOne(0)) break;
                    State(true, "Connected");
                    var lines = new JsonLineBuffer();
                    var buffer = new byte[8192];
                    long lastTimestamp = -1;
                    while (!stop.WaitOne(0))
                    {
                        int count;
                        try { count = transport.Receive(buffer); }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.WouldBlock) continue;
                            throw;
                        }
                        if (count == 0) break;
                        int rejectedBefore = lines.RejectedLines;
                        lines.Feed(buffer, count, line =>
                        {
                            try
                            {
                                var parsed = HandProtocol.Parse(line);
                                if (parsed.timestamp <= lastTimestamp) throw new FormatException("Non-increasing timestamp");
                                lastTimestamp = parsed.timestamp;
                                lock (gate) { latest = parsed; revision++; ReceivedFrames++; status = "Connected"; }
                                var receive = ValidFrameReceived;
                                if (receive != null) receive(parsed, line);
                            }
                            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
                            {
                                lock (gate) { latest = null; revision++; status = "Invalid JSON/frame ignored"; }
                                var invalid = StreamInterrupted;
                                if (invalid != null) invalid("收到无效或非递增数据帧");
                            }
                        });
                        if (lines.RejectedLines > rejectedBefore)
                        {
                            var invalidLine = StreamInterrupted;
                            if (invalidLine != null) invalidLine("UTF-8 或行长度异常");
                        }
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is TimeoutException || ex is ObjectDisposedException || ex is InvalidOperationException)
                { /* 断线仅触发重试；不在工作线程操作 Unity 对象。 */ }
                finally
                {
                    if (client != null) client.Close();
                    lock (gate) { if (socket == client) socket = null; }
                    State(false, "Disconnected / retrying...");
                    var interrupted = StreamInterrupted;
                    if (interrupted != null) interrupted("TCP 连接断开");
                }
                stop.WaitOne(500);
            }
            State(false, "Stopped");
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Set();
            lock (gate) { if (socket != null) socket.Close(); }
            if (worker != null && !worker.Join(2000)) throw new TimeoutException("TCP worker did not stop");
            stop.Dispose();
        }
    }
}
