// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	partial class Server<C>
		where C : BaseConnection, new()
	{
		internal class Connection<C2>
			: IDisposable
			where C2 : BaseConnection, new()
		{
			private static int connectionCount;
			private SspiContext sspiContext;

			public Connection(Socket socket, int receivedQueueSize)
			{
				Socket = socket;
				Id = NewConnectionId();
				ReceiveQueue = new CyclicBuffer(receivedQueueSize);
				ReceiveSpinLock = new SpinLock();
			}

			public void Dispose(BuffersPool<ServerAsyncEventArgs> bufferPool)
			{
				ReceiveQueue.Dispose(bufferPool);
				Dispose();
			}

			public void Dispose()
			{
				if (sspiContext != null)
					sspiContext.Dispose();

				if (UserConnection != null)
					UserConnection.Dispose();

				Socket.SafeShutdownClose();

				if (UserConnection != null)
					UserConnection.Dispose();
			}

			public readonly int Id;
			public readonly Socket Socket;
			public readonly SpinLock ReceiveSpinLock;
			public readonly CyclicBuffer ReceiveQueue;
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
			{
				private int size;
				private volatile int dequeueIndex;
				private int sequenceNumber;
				private ServerAsyncEventArgs[] queue;

				public CyclicBuffer(int size1)
				{
					size = size1;
					dequeueIndex = 0;
					sequenceNumber = 0;
					queue = new ServerAsyncEventArgs[size];
				}

				public void Dispose(BuffersPool<ServerAsyncEventArgs> bufferPool)
				{
					for (int i = 0; i < queue.Length; i++)
						if (queue[i] != null)
							bufferPool.Put(queue[i]);
				}

				public int GetNextSequenceNumber()
				{
					return sequenceNumber++;
				}

				public void Put(ServerAsyncEventArgs e)
				{
#if DEBUG
					if (queue[e.SequenceNumber % size] != null)
						throw new InvalidOperationException();
#endif
					Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[e.SequenceNumber % size], e);
				}

				public ServerAsyncEventArgs GetCurrent()
				{
					ServerAsyncEventArgs e =
						Interlocked.Exchange<ServerAsyncEventArgs>(ref queue[dequeueIndex], null);

					return e;
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
