using System.Net;
using System.Net.Sockets;
using System.Threading;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using QuickFix.Internal;

namespace QuickFix
{
    // TODO v2.0 - consider changing to internal

    /// <summary>
    /// Handles incoming connections on a single endpoint. When a socket connection
    /// is accepted, a ClientHandlerThread is created to handle the connection
    /// </summary>
    public class ThreadedSocketReactor : Disposable
    {
        private const int Running = 1;
        private const int Stopped = 2;

        #region Properties

        private int _state = Stopped;
        private readonly SemaphoreSlim _wait = new SemaphoreSlim(1, 1);

        #endregion

        #region Private Members

        private long _nextClientId;
        private Thread _serverThread;
        private ConcurrentDictionary<long, ClientHandlerThread> _clientThreads = new ConcurrentDictionary<long, ClientHandlerThread>();
        private readonly TcpListener _tcpListener;
        private readonly SocketSettings _socketSettings;
        private readonly Dictionary _sessionDict;
        private readonly AcceptorSocketDescriptor _acceptorDescriptor;

        #endregion

        [Obsolete("Use the other constructor")]
        public ThreadedSocketReactor(IPEndPoint serverSocketEndPoint, SocketSettings socketSettings)
            : this(serverSocketEndPoint, socketSettings, null)
        { }

        public ThreadedSocketReactor(IPEndPoint serverSocketEndPoint, SocketSettings socketSettings,
            Dictionary sessionDict) : this(serverSocketEndPoint, socketSettings, sessionDict, null)
        {
            
        }
        internal ThreadedSocketReactor(IPEndPoint serverSocketEndPoint, SocketSettings socketSettings, Dictionary sessionDict, AcceptorSocketDescriptor acceptorDescriptor)
        {
            _socketSettings = socketSettings;
            _tcpListener = new TcpListener(serverSocketEndPoint);
            _sessionDict = sessionDict;
            _acceptorDescriptor = acceptorDescriptor;
        }

        public void Start()
        {
            ThrowIfDisposed();
            
            if (Interlocked.CompareExchange(ref _state, Running, Stopped) != Stopped)
                return;

            _wait.Wait();

            _serverThread = new Thread(Run);
            _serverThread.Start();
        }

        public void Shutdown()
        {
            ThrowIfDisposed();
            ShutdownCore();
        }

        private void ShutdownCore()
        {
            if (Interlocked.CompareExchange(ref _state, Stopped, Running) != Running)
                return;

            _wait.Wait();

            try
            {
                _tcpListener.Server.Close();
                try
                {
                    _tcpListener.Stop();
                    _serverThread.Join(5000);
                }
                catch (Exception e)
                {
                    Log("Error while stopping tcp listener: " + e.Message);
                }
                
                ShutdownClientHandlerThreads();
            }
            catch (Exception e)
            {
                Log("Error while closing server socket: " + e.Message);
            }
            finally
            {
                _wait.Release();
            }
        }

        /// <summary>
        /// TODO apply networking options, e.g. NO DELAY, LINGER, etc.
        /// </summary>
        private void Run()
        {
            try
            {
                _tcpListener.Start();
            }
            finally
            {
                _wait.Release();
            }

            while (Volatile.Read(ref _state) == Running)
            {
                try
                {
                    var client = _tcpListener.AcceptTcpClient();
                    if (Volatile.Read(ref _state) == Running)
                    {
                        ApplySocketOptions(client, _socketSettings);
                        var t = new ClientHandlerThread(client, GetNextClientId(), _sessionDict, _socketSettings, _acceptorDescriptor);
                        t.Exited += OnClientHandlerThreadExited;
                        
                        _clientThreads.TryAdd(t.Id, t);

                        // FIXME set the client thread's exception handler here
                        t.Log("connected");
                        t.Start();
                    }
                    else
                    {
                        client.Dispose();
                    }
                }
                catch (Exception e)
                {
                    if (Volatile.Read(ref _state) == Running)
                        Log("Error accepting connection: " + e.Message);
                }
            }
        }

        private void OnClientHandlerThreadExited(ClientHandlerThread client)
        {
            if (!_clientThreads.TryRemove(client.Id, out _)) return;
            client.Exited -= OnClientHandlerThreadExited;
            client.Dispose();
        }

        /// <summary>
        /// Apply socket options from settings
        /// </summary>
        /// <param name="client"></param>
        /// <param name="socketSettings"></param>
        public static void ApplySocketOptions(TcpClient client, SocketSettings socketSettings)
        {
            client.LingerState = new LingerOption(false, 0);
            client.NoDelay = socketSettings.SocketNodelay;
            if (socketSettings.SocketReceiveBufferSize.HasValue)
            {
                client.ReceiveBufferSize = socketSettings.SocketReceiveBufferSize.Value;
            }
            if (socketSettings.SocketSendBufferSize.HasValue)
            {
                client.SendBufferSize = socketSettings.SocketSendBufferSize.Value;
            }
            if (socketSettings.SocketReceiveTimeout.HasValue)
            {
                client.ReceiveTimeout = socketSettings.SocketReceiveTimeout.Value;
            }
            if (socketSettings.SocketSendTimeout.HasValue)
            {
                client.SendTimeout = socketSettings.SocketSendTimeout.Value;
            }
        }

        private void ShutdownClientHandlerThreads()
        {
            Log("shutting down...");

            var threads = Interlocked.Exchange(ref _clientThreads, new ConcurrentDictionary<long, ClientHandlerThread>());

            var completions = threads
                .Values
                .Select(x =>
                {
                    x.Exited -= OnClientHandlerThreadExited;
                    x.Shutdown("reactor is shutting down");
                    return x.Completion;
                })
                .ToArray();
            
            try
            {
                // wait in parallel
                // wait in parallel
                Task.WaitAll(completions, 10000);
            }
            catch (Exception e)
            {
                Log("Wait for shutdown timeout: " + e.Message);
            }
            
            foreach (var thread in threads.Values)
            {
                try
                {
                    thread.Dispose();
                }
                catch (Exception e)
                {
                    thread.Log("Error disposing: " + e.Message);
                }
            }
        }

        private long GetNextClientId() => Interlocked.Increment(ref _nextClientId);

        /// <summary>
        /// FIXME do real logging
        /// </summary>
        /// <param name="s"></param>
        private void Log(string s)
        {
            Console.WriteLine(s);
        }

        protected override void DisposeCore()
        {
            try
            {
                ShutdownCore();
            }
            catch (Exception e)
            {
                Log("Error disposing reactor: " + e.Message);
            }
        }
    }
}
