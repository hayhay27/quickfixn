using System.Net.Sockets;
using System.Threading;
using System;
using System.Threading.Tasks;
using QuickFix.Internal;

namespace QuickFix
{
    // TODO v2.0 - consider changing to internal


    /// <summary>
    /// Created by a ThreadedSocketReactor to handle a client connection.
    /// Each ClientHandlerThread has a SocketReader which reads
    /// from the socket.
    /// </summary>
    public class ClientHandlerThread : Disposable, IResponder
    {
        internal event Action<ClientHandlerThread> Exited;

        public long Id { get; }

        public Task Completion => _completion.Task;

        private readonly TaskCompletionSource<int> _completion = new TaskCompletionSource<int>();
        private Thread _thread;
        private volatile bool _isShutdownRequested;
        private SocketReader _socketReader;
        private FileLog _log;

        [Obsolete("Don't use this constructor")]
        public ClientHandlerThread(TcpClient tcpClient, long clientId)
            : this(tcpClient, clientId, new Dictionary())
        { }


        [Obsolete("Don't use this constructor")]
        public ClientHandlerThread(TcpClient tcpClient, long clientId, Dictionary settingsDict)
            : this(tcpClient, clientId, settingsDict, new SocketSettings())
        {
        }

        /// <summary>
        /// Creates a ClientHandlerThread
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <param name="clientId"></param>
        /// <param name="settingsDict"></param>
        /// <param name="socketSettings"></param>
        public ClientHandlerThread(TcpClient tcpClient, long clientId, Dictionary settingsDict, SocketSettings socketSettings)
            : this(tcpClient, clientId, settingsDict, socketSettings, null)
        {
            
        }

        internal ClientHandlerThread(TcpClient tcpClient, long clientId, Dictionary settingsDict,
            SocketSettings socketSettings, AcceptorSocketDescriptor acceptorDescriptor)
        {
            string debugLogFilePath = "log";
            if (settingsDict.Has(SessionSettings.DEBUG_FILE_LOG_PATH))
                debugLogFilePath = settingsDict.GetString(SessionSettings.DEBUG_FILE_LOG_PATH);
            else if (settingsDict.Has(SessionSettings.FILE_LOG_PATH))
                debugLogFilePath = settingsDict.GetString(SessionSettings.FILE_LOG_PATH);

            // FIXME - do something more flexible than hardcoding a filelog
            _log = new FileLog(debugLogFilePath, new SessionID(
                    "ClientHandlerThread", clientId.ToString(), "Debug-" + Guid.NewGuid().ToString()));

            Id = clientId;
            _socketReader = new SocketReader(tcpClient, socketSettings, this, acceptorDescriptor);
        }

        public void Start()
        {
            _thread = new Thread(Run);
            _thread.Start();
        }

        public void Shutdown(string reason)
        {
            Log("shutdown requested: " + reason);
            _isShutdownRequested = true;
        }

        private void Run()
        {
            while (!_isShutdownRequested)
            {
                try
                {
                    _socketReader.Read();
                }
                catch (Exception e)
                {
                    Shutdown(e.Message);
                }
            }

            Log("shutdown");
            OnExited();
        }

        private void OnExited()
        {
            Exited?.Invoke(this);
            _completion.TrySetResult(0);
        }

        /// FIXME do real logging
        public void Log(string s)
        {
            _log.OnEvent(s);
        }

        /// <summary>
        /// Provide StreamReader with access to the log
        /// </summary>
        /// <returns></returns>
        internal ILog GetLog()
        {
            return _log;
        }

        #region Responder Members

        public bool Send(string data)
        {
            return _socketReader.Send(data) > 0;
        }

        public void Disconnect()
        {
            Shutdown("Disconnected");
        }

        #endregion

        protected override void DisposeCore()
        {
            _completion.TrySetResult(0);
            if (_socketReader != null)
            {
                _socketReader.Dispose();
                _socketReader = null;
            }

            if (_log != null)
            {
                _log.Dispose();
                _log = null;
            }
        }
    }
}
