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
	abstract class BaseTcpServer<C>
		: Server<C>
		where C : BaseConnection, new()
	{
		private object sync;
		private Socket listener;
		private SocketAsyncEventArgs[] acceptEventArgs;
		private SafeDictionary<EndPoint, Connection<C>> connections;
		private int acceptQueueSize;
		private int receiveQueueSize;
		private int offsetOffset;

		public BaseTcpServer(ServersManagerConfig config)
			: base()
		{
			sync = new object();

			acceptQueueSize = (config.TcpAcceptQueueSize > 0) ? config.TcpAcceptQueueSize : 64;
			receiveQueueSize = (config.TcpQueueSize > 0) ? config.TcpQueueSize : 8;
			offsetOffset = config.TcpOffsetOffset;
		}

		public override void Start()
		{
			lock (sync)
			{
				isRunning = true;

				connections = new SafeDictionary<EndPoint, Connection<C>>();

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
					connections.ForEach((c) => { c.Dispose(); });
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		protected Connection<C> GetTcpConnection(IPEndPoint remote)
		{
			Connection<C> connection = null;

			if (connections.TryGetValue(remote, out connection))
			{
				if (connection.Socket.Connected == false)
				{
					connections.Remove(remote);

					connection.Dispose();

					OnEndTcpConnection(connection);
					connection = null;
				}
			}

			return connection;
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			var connection = GetTcpConnection(e.RemoteEndPoint);

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
			Connection<C> connection = null;

			if (e.SocketError == SocketError.Success)
			{
				connection = new Connection<C>(socket1, receiveQueueSize);
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
							connection = new Connection<C>(socket1, receiveQueueSize);
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
			Connection<C> connection = null;

			if (isRunning == true && acceptEventArgs.SocketError == SocketError.Success)
			{
				connection = new Connection<C>(acceptEventArgs.AcceptSocket, receiveQueueSize);
				var oldConnection = connections.Replace(connection.RemoteEndPoint, connection);

				if (oldConnection != null && oldConnection.Close())
					OnEndTcpConnection(oldConnection);
			}
			else
			{
				if (acceptEventArgs.AcceptSocket != null)
					acceptEventArgs.AcceptSocket.SafeShutdownClose();
			}

			if (isRunning == true)
			{
				acceptEventArgs.AcceptSocket = null;
				if (listener.AcceptAsync(acceptEventArgs) == false)
					Accept_Completed(listener, acceptEventArgs);
			}

			if (connection != null)
				BeginReceive(connection);
		}

		protected abstract void OnNewTcpConnection(Connection<C> connection);
		protected abstract void OnEndTcpConnection(Connection<C> connection);
		protected abstract bool OnTcpReceived(Connection<C> connection, ref ServerAsyncEventArgs e);

		private void BeginReceive(Connection<C> connection)
		{
			OnNewTcpConnection(connection);

			ServerAsyncEventArgs e;
			for (int i = 0; i < receiveQueueSize; i++)
			{
				e = EventArgsManager.Get();

				if (TcpReceiveAsync(connection, e) == false)
					connection.ReceiveQueue.Put(e);
			}

			e = connection.ReceiveQueue.GetCurrent();
			if (e != null)
				Receive_Completed(connection.Socket, e);
		}

		private void Receive_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			try
			{
				Connection<C> connection;
				connections.TryGetValue(e.RemoteEndPoint, out connection);

				if (connection != null && connection.Socket == socket)
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
							connections.Remove(connection.RemoteEndPoint);

							if (connection.Close())
								OnEndTcpConnection(connection);

							break;
						}
						else
						{
							connection.ReceiveQueue.Next();
						}

						if (e == null)
							e = EventArgsManager.Get();

						if (TcpReceiveAsync(connection, e))
							e = null;
					}
				}
			}
			finally
			{
				if (e != null)
					EventArgsManager.Put(ref e);
			}
		}

		private bool TcpReceiveAsync(Connection<C> connection, ServerAsyncEventArgs e)
		{
			PrepareEventArgs(connection, e);

			try
			{
				connection.ReceiveSpinLock.Enter();

				e.SequenceNumber = connection.ReceiveQueue.SequenceNumber;

				try
				{
					if (connection.IsClosed == false)
					{
						bool result = connection.Socket.ReceiveAsync(e);
						connection.ReceiveQueue.SequenceNumber++;
						return result;
					}
				}
				finally
				{
					connection.ReceiveSpinLock.Exit();
				}
			}
			catch (ObjectDisposedException)
			{
			}

			EventArgsManager.Put(ref e);
			return true;
		}

		protected void PrepareEventArgs(Connection<C> connection, ServerAsyncEventArgs e)
		{
			e.ConnectionId = connection.Id;
			e.RemoteEndPoint = connection.RemoteEndPoint;
			e.Completed = Receive_Completed;
			e.SetBufferMax(offsetOffset);
		}
	}
}
