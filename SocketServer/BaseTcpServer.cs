// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;

namespace SocketServers
{
	abstract class BaseTcpServer
		: Server
	{
		private object sync;
		private Socket listener;
		private SocketAsyncEventArgs[] acceptEventArgs;
		private SafeDictionary<EndPoint, Connection> connections;
		private int acceptQueueSize;
		private int receiveQueueSize;
		private int offsetOffset;

		public BaseTcpServer(ServersManagerConfig config)
			: base()
		{
			sync = new object();

			acceptQueueSize = (config.TcpAcceptQueueSize > 0) ? config.TcpAcceptQueueSize : 16;
			receiveQueueSize = (config.TcpQueueSize > 0) ? config.TcpQueueSize : 8;
			offsetOffset = config.TcpOffsetOffset;
		}

		public override void Start()
		{
			lock (sync)
			{
				isRunning = true;

				connections = new SafeDictionary<EndPoint, Connection>();

				listener = new Socket(realEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
				listener.Bind(realEndPoint);
				listener.Listen(128);

				ThreadPool.QueueUserWorkItem(new WaitCallback(EnqueueAsyncAccepts), null);
			}
		}

		private void EnqueueAsyncAccepts(Object stateInfo)
		{
			acceptEventArgs = new SocketAsyncEventArgs[acceptQueueSize];
			for (int i = 0; i < acceptEventArgs.Length; i++)
			{
				acceptEventArgs[i] = new SocketAsyncEventArgs();
				acceptEventArgs[i].Completed += Accept_Completed;

				if (listener.AcceptAsync(acceptEventArgs[i]) == false)
					Accept_Completed(listener, acceptEventArgs[i]);
			}
		}

		public override void Dispose()
		{
			isRunning = false;

			lock (sync)
			{
				if (listener != null)
				{
					connections.ForEach((c) => { c.Dispose(buffersPool); });
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		protected Connection GetTcpConnection(IPEndPoint remote)
		{
			Connection connection = null;

			if (connections.TryGetValue(remote, out connection))
			{
				if (connection.Socket.Connected == false)
				{
					connections.Remove(remote);

					connection.Dispose(buffersPool);

					OnEndTcpConnection(connection);
					connection = null;
				}
			}

			return connection;
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			Connection connection = GetTcpConnection(e.RemoteEndPoint);

			if (connection == null)
			{
				if (e.ConnectionId == ServerAsyncEventArgs.AnyNewConnectionId)
				{
					Socket socket = new Socket(realEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
					socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
					socket.Bind(realEndPoint);

					socket.ConnectAsync(e, Connect_Completed);
				}
				else
				{
					e.Completed = Send_Completed;
					e.SocketError = SocketError.NotConnected;
					e.OnCompleted(null);
				}
			}
			else
			{
				if (e.ConnectionId == ServerAsyncEventArgs.AnyNewConnectionId ||
					e.ConnectionId == ServerAsyncEventArgs.AnyConnectionId ||
					e.ConnectionId == connection.Id)
				{
					connection.Socket.SendAsync(e, Send_Completed);
				}
				else
				{
					e.Completed = Send_Completed;
					e.SocketError = SocketError.NotConnected;
					e.OnCompleted(null);
				}
			}
		}

		private void Connect_Completed(Socket socket1, ServerAsyncEventArgs e)
		{
			bool beginReceive = false;
			Connection connection = null;

			if (e.SocketError == SocketError.Success)
			{
				connection = new Connection(socket1, receiveQueueSize);
				connections.Add(e.RemoteEndPoint, connection);
				beginReceive = true;
			}
			else
			{
				while (e.SocketError == SocketError.AddressAlreadyInUse)
				{
					if (connections.TryGetValue(e.RemoteEndPoint, out connection))
						break;

					Thread.Sleep(0);

					if (socket1.ConnectAsync(e))
						return;
					else
					{
						if (e.SocketError == SocketError.Success)
						{
							connection = new Connection(socket1, receiveQueueSize);
							connections.Add(e.RemoteEndPoint, connection);
							beginReceive = true;
						}
					}
				}
			}

			// send message or report connection error
			//
			if (e.SocketError == SocketError.Success)
			{
				connection.Socket.SendAsync(e, Send_Completed);

				if (beginReceive)
					BeginReceive(connection);
			}
			else
			{
				e.Completed = Send_Completed;
				e.OnCompleted(socket1);
			}
		}

		private void Accept_Completed(object sender, SocketAsyncEventArgs acceptEventArgs)
		{
			if (isRunning == false)
			{
				acceptEventArgs.AcceptSocket.SafeShutdownClose();
			}
			else
			{
				Connection connection = new Connection(acceptEventArgs.AcceptSocket, receiveQueueSize);

				if (acceptEventArgs.SocketError == SocketError.Success)
					connections.Add(connection.Socket.RemoteEndPoint, connection);

				acceptEventArgs.AcceptSocket = null;
				if (listener.AcceptAsync(acceptEventArgs) == false)
					Accept_Completed(listener, acceptEventArgs);

				if (isRunning)
					BeginReceive(connection);
			}
		}

		protected abstract void OnNewTcpConnection(Connection connection);
		protected abstract void OnEndTcpConnection(Connection connection);
		protected abstract bool OnTcpReceived(Connection connection, ref ServerAsyncEventArgs e);

		private void BeginReceive(Connection connection)
		{
			OnNewTcpConnection(connection);

			ServerAsyncEventArgs first = null;
			for (int i = 0; i < receiveQueueSize; i++)
			{
				var e = buffersPool.Get();

				if (SyncReceiveAsync(connection, e) == false)
				{
					if (i > 0)
						connection.ReceiveQueue.Put(e);
					else
						first = e;
				}
			}

			if (first != null)
				first.OnCompleted(connection.Socket);
		}

		private void Receive_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			try
			{
				Connection connection = null;
				if (connections.TryGetValue(e.RemoteEndPoint, out connection)
					&& connection.Socket == socket)
				{
					for (; ; )
					{
						if (e != null)
						{
							connection.ReceiveQueue.Put(e);
							e = null;
						}

						e = connection.ReceiveQueue.GetCurrent();
						if (e == null)
							break;

						bool close = true;
						if (isRunning && e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
							close = !OnTcpReceived(connection, ref e);

						if (close)
						{
							if (e != null)
								buffersPool.Put(ref e);

							connections.Remove(socket.RemoteEndPoint);
							connection.Dispose(buffersPool);
							OnEndTcpConnection(connection);

							break;
						}
						else
						{
							connection.ReceiveQueue.Next();
						}

						if (e == null)
							e = buffersPool.Get();

						if (SyncReceiveAsync(connection, e))
							e = null;
					}
				}
				else
				{
					if (e != null)
						buffersPool.Put(ref e);
				}
			}
			catch
			{
				if (isRunning)
					throw;

				if (e != null)
					buffersPool.Put(ref e);
			}
		}

		private bool SyncReceiveAsync(Connection connection, ServerAsyncEventArgs e)
		{
			PrepareEventArgs(connection, e);

			connection.ReceiveSpinLock.Enter();

			try
			{
				e.SequenceNumber = connection.ReceiveQueue.GetNextSequenceNumber();
				return connection.Socket.ReceiveAsync(e);
			}
			finally
			{
				connection.ReceiveSpinLock.Exit();
			}
		}

		protected void PrepareEventArgs(Connection connection, ServerAsyncEventArgs e)
		{
			e.ConnectionId = connection.Id;
			e.RemoteEndPoint = connection.Socket.RemoteEndPoint as IPEndPoint;
			e.Completed = Receive_Completed;
			e.SetBufferMax(offsetOffset);
		}
	}
}
