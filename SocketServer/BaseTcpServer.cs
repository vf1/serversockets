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
		private readonly object sync;
		private Socket listener;
		private SafeDictionary<EndPoint, Connection<C>> connections;
		private readonly int receiveQueueSize;
		private readonly int offsetOffset;
		private bool socketReuseEnabled;
		private readonly int maxAcceptBacklog;
		private readonly int minAcceptBacklog;
		private int acceptBacklog;

		public BaseTcpServer(ServersManagerConfig config)
			: base()
		{
			sync = new object();

			receiveQueueSize = config.TcpQueueSize;
			offsetOffset = config.TcpOffsetOffset;

			minAcceptBacklog = config.TcpMinAcceptBacklog;
			maxAcceptBacklog = config.TcpMaxAcceptBacklog;
			socketReuseEnabled = minAcceptBacklog < maxAcceptBacklog;
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
				listener.Listen(0);

				ThreadPool.QueueUserWorkItem(new WaitCallback(EnqueueAsyncAccepts), null);
			}
		}

		private void EnqueueAsyncAccepts(Object stateInfo)
		{
			lock (sync)
			{
				for (; ; )
				{
					int backlog = Thread.VolatileRead(ref acceptBacklog);
					if (isRunning && backlog < minAcceptBacklog)
					{
						if (Interlocked.CompareExchange(ref acceptBacklog, backlog + 1, backlog) == backlog)
						{
							var e = EventArgsManager.Get();
							e.FreeBuffer();

							listener.AcceptAsync(e, Accept_Completed);
						}
					}
					else
					{
						break;
					}
				}
			}
		}

		public override void Dispose()
		{
			isRunning = false;

			lock (sync)
			{
				if (listener != null)
				{
					connections.ForEach(EndTcpConnection);
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		protected abstract void OnNewTcpConnection(Connection<C> connection);
		protected abstract void OnEndTcpConnection(Connection<C> connection);
		protected abstract bool OnTcpReceived(Connection<C> connection, ref ServerAsyncEventArgs e);

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
			bool newConnection = false;
			Connection<C> connection = null;

			if (e.SocketError == SocketError.Success)
			{
				connection = new Connection<C>(socket1, false, receiveQueueSize);
				connections.Add(e.RemoteEndPoint, connection);
				newConnection = true;
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
							connection = new Connection<C>(socket1, false, receiveQueueSize);
							connections.Add(e.RemoteEndPoint, connection);
							newConnection = true;
						}
					}
				}
			}

			// send message or report connection error
			//
			if (e.SocketError == SocketError.Success)
			{
				connection.Socket.SendAsync(e, Send_Completed);

				if (newConnection)
					NewTcpConnection(connection);
			}
			else
			{
				e.Completed = Send_Completed;
				e.OnCompleted(socket1);
			}
		}

		private Connection<C> CreateConnection(Socket socket, SocketError error)
		{
			Connection<C> connection = null;

			if (isRunning == true && error == SocketError.Success)
			{
				connection = new Connection<C>(socket, true, receiveQueueSize);
				var oldConnection = connections.Replace(connection.RemoteEndPoint, connection);

				if (oldConnection != null)
					EndTcpConnection(oldConnection);
			}
			else
			{
				if (socket != null)
					socket.SafeShutdownClose();
			}

			return connection;
		}

		private void Accept_Completed(Socket none, ServerAsyncEventArgs e)
		{
			Socket acceptSocket = e.AcceptSocket;
			SocketError error = e.SocketError;

			for (; ; )
			{
				int backlog = Thread.VolatileRead(ref acceptBacklog);
				if (isRunning && backlog <= minAcceptBacklog)
				{
					e.AcceptSocket = null;

					listener.AcceptAsync(e, Accept_Completed);
					break;
				}
				else
				{
					if (Interlocked.CompareExchange(ref acceptBacklog, backlog - 1, backlog) == backlog)
					{
						EventArgsManager.Put(e);
						break;
					}
				}
			}

			var connection = CreateConnection(acceptSocket, error);

			if (connection != null)
				NewTcpConnection(connection);
		}

		private void NewTcpConnection(Connection<C> connection)
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

		private void EndTcpConnection(Connection<C> connection)
		{
			if (connection.Close())
			{
				OnEndTcpConnection(connection);

				if (connection.Socket.Connected)
					try { connection.Socket.Shutdown(SocketShutdown.Both); }
					catch (SocketException) { }

				if (connection.IsSocketAccepted && socketReuseEnabled)
				{
					try
					{
						try
						{
							var e = EventArgsManager.Get();

							e.FreeBuffer();
							e.DisconnectReuseSocket = true;
							e.Completed = Disconnect_Completed;

							if (connection.Socket.DisconnectAsync(e) == false)
								e.OnCompleted(connection.Socket);
						}
						catch (SocketException) { }
					}
					catch (NotSupportedException)
					{
						socketReuseEnabled = false;
					}
				}

				if (socketReuseEnabled == false)
					connection.Socket.Close();
			}
		}

		private void Disconnect_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			for (; ; )
			{
				int backlog = Thread.VolatileRead(ref acceptBacklog);
				if (isRunning && backlog < maxAcceptBacklog)
				{
					if (Interlocked.CompareExchange(ref acceptBacklog, backlog + 1, backlog) == backlog)
					{
						e.AcceptSocket = socket;

						listener.AcceptAsync(e, Accept_Completed);
						break;
					}
				}
				else
				{
					EventArgsManager.Put(e);
					break;
				}
			}
		}

		private void Receive_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			try
			{
				Connection<C> connection;
				connections.TryGetValue(e.RemoteEndPoint, out connection);

				if (connection != null && connection.Socket == socket && connection.Id == e.ConnectionId)
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
							connections.Remove(connection.RemoteEndPoint, connection);
							EndTcpConnection(connection);

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
				else
				{
					if (e.BytesTransferred > 0)
						Console.WriteLine("ERROR: {0}, conn-handle: {1}, socket-handle: {2}, ip: {3}", e.BytesTransferred,
							(connection == null) ? "Null" : connection.Socket.Handle.ToString(), socket.Handle.ToString(),
							e.RemoteEndPoint.ToString());
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
				connection.SpinLock.Enter();

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
					connection.SpinLock.Exit();
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

		protected Connection<C> GetTcpConnection(IPEndPoint remote)
		{
			Connection<C> connection = null;

			if (connections.TryGetValue(remote, out connection))
			{
				if (connection.Socket.Connected == false)
				{
					connections.Remove(remote, connection);
					EndTcpConnection(connection);

					connection = null;
				}
			}

			return connection;
		}
	}
}
