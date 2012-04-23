// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	partial class Server<C>
		where C : BaseConnection, IDisposable, new()
	{
		internal class Connection<C2>
			: IDisposable
			where C2 : IDisposable
		{
			private static int connectionCount;
			private SspiContext sspiContext;
			private int closeCount;

			public Connection(Socket socket, bool isSocketAccepted, int receivedQueueSize)
			{
				Id = NewConnectionId();

				ReceiveQueue = new CyclicBuffer(receivedQueueSize);
				SpinLock = new SpinLock();

				Socket = socket;
				IsSocketAccepted = isSocketAccepted;
				RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
			}

			internal bool Close()
			{
				// ?!
				//SpinLock.Enter();
				bool result = Interlocked.Increment(ref closeCount) == 1;
				//SpinLock.Exit();

				if (result)
				{
					ReceiveQueue.Dispose();

					if (sspiContext != null)
						sspiContext.Dispose();

					if (UserConnection != null)
						UserConnection.Dispose();
				}

				return result;
			}

			internal bool IsClosed
			{
				get { return Thread.VolatileRead(ref closeCount) > 0; }
			}

			void IDisposable.Dispose()
			{
				if (Close())
					Socket.SafeShutdownClose();
			}

			public readonly int Id;
			public readonly Socket Socket;
			public readonly bool IsSocketAccepted;
			public readonly SpinLock SpinLock;
			public readonly CyclicBuffer ReceiveQueue;
			public readonly IPEndPoint RemoteEndPoint;
			public C2 UserConnection;

			public SspiContext SspiContext
			{
				get
				{
					if (sspiContext == null)
						sspiContext = new SspiContext();
					return sspiContext;
				}
			}

			private int NewConnectionId()
			{
				int connectionId;
				do
				{
					connectionId = Interlocked.Increment(ref connectionCount);
				} while (
					connectionId == ServerAsyncEventArgs.AnyNewConnectionId ||
					connectionId == ServerAsyncEventArgs.AnyConnectionId
					);

				return connectionId;
			}

			#region class CyclicBuffer {...}

			internal class CyclicBuffer
				: IDisposable
			{
				private bool disposed;
				private int size;
				private volatile int dequeueIndex;
				private ServerAsyncEventArgs[] queue;

				public CyclicBuffer(int size1)
				{
					disposed = false;
					size = size1;
					dequeueIndex = 0;
					SequenceNumber = 0;
					queue = new ServerAsyncEventArgs[size];
				}

				public void Dispose()
				{
					disposed = true;

					var e = default(ServerAsyncEventArgs);

					for (int i = 0; i < queue.Length; i++)
					{
						if (queue[i] != null)
							e = Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[i], null);

						if (e != null)
							EventArgsManager.Put(ref e);
					}
				}

				public volatile int SequenceNumber;

				public void Put(ServerAsyncEventArgs e)
				{
					int index = e.SequenceNumber % size;
#if DEBUG
					if (queue[index] != null)
						throw new InvalidOperationException();
#endif

					Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[index], e);

					if (disposed)
					{
						if (Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[index], null) != null)
							EventArgsManager.Put(e);
					}
				}

				public ServerAsyncEventArgs GetCurrent()
				{
					return Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[dequeueIndex], null);
				}

#pragma warning disable 0420

				public void Next()
				{
					Interlocked.Exchange(ref dequeueIndex, (dequeueIndex + 1) % size);
				}

#pragma warning restore 0420

			}

			#endregion
		}
	}
}
