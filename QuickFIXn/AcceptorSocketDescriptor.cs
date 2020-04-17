using System.Collections.Generic;
using System.Linq;
using System.Net;
using QuickFix.Internal;

namespace QuickFix
{
    internal class AcceptorSocketDescriptor : Disposable
    {
        #region Properties

        public ThreadedSocketReactor SocketReactor { get; }

        public IPEndPoint Address { get; }

        #endregion

        #region Private Members

        private readonly Dictionary<SessionID, Session> acceptedSessions_ = new Dictionary<SessionID, Session>();

        #endregion

        public AcceptorSocketDescriptor(IPEndPoint socketEndPoint, SocketSettings socketSettings, QuickFix.Dictionary sessionDict)
        {
            Address = socketEndPoint;
            SocketReactor = new ThreadedSocketReactor(Address, socketSettings, sessionDict, this);
        }

        public void AcceptSession(Session session)
        {
            lock (acceptedSessions_)
            {
                acceptedSessions_[session.SessionID] = session;
            }
        }

        /// <summary>
        /// Remove a session from those tied to this socket.
        /// </summary>
        /// <param name="sessionID">ID of session to be removed</param>
        /// <returns>true if session removed, false if not found</returns>
        public bool RemoveSession(SessionID sessionID)
        {
            lock (acceptedSessions_)
            {
                return acceptedSessions_.Remove(sessionID);
            }
        }

        public Dictionary<SessionID, Session> GetAcceptedSessions()
        {
            lock (acceptedSessions_)
            {
                return new Dictionary<SessionID, Session>(acceptedSessions_);
            }
        }

        protected override void DisposeCore()
        {
            SocketReactor.Dispose();
        }
    }
}