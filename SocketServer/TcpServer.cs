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
	class TcpServer
		: Server
	{
		private object sync;
		private Socket listener;
		private SocketAsyncEventArgs[] acceptEventArgs;
		private SafeDictionary<EndPoint, Connection> connections;
		private int initialBufferSize;
		private int initialOffset;

		public TcpServer(ServersManagerConfig config)
			: base()
		{
			sync = new object();

			initialBufferSize = config.TcpInitialBufferSize;
			initialOffset = config.TcpInitialOffset;
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

				ThreadPool.QueueUserWorkItem(new WaitCallback(EnqueueAsyncAccepts), 4);
			}
		}

		private void EnqueueAsyncAccepts(Object stateInfo)
		{
			int count = (int)stateInfo;

			acceptEventArgs = new SocketAsyncEventArgs[count];
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
					connections.ForEach((c) => { c.Socket.SafeShutdownClose(); });
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			Connection connection = null;

			if (connections.TryGetValue(e.RemoteEndPoint, out connection))
			{
				if (connection.Socket.Connected == false)
				{
					connection.Socket.SafeShutdownClose();
					connection = null;

					connections.Remove(e.RemoteEndPoint);
				}
			}

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
				connection = new Connection(socket1);
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
							connection = new Connection(socket1);
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
				Connection connection = new Connection(acceptEventArgs.AcceptSocket);

				if (acceptEventArgs.SocketError == SocketError.Success)
					connections.Add(connection.Socket.RemoteEndPoint, connection);

				acceptEventArgs.AcceptSocket = null;
				if (listener.AcceptAsync(acceptEventArgs) == false)
					Accept_Completed(listener, acceptEventArgs);

				if (isRunning)
					BeginReceive(connection);
			}
		}

		private void BeginReceive(Connection connection)
		{
			var e = buffersPool.Get();
			PrepareBuffer(e, connection.Socket.RemoteEndPoint, connection.Id);

			OnNewConnection(connection);

			if (connection.Socket.ReceiveAsync(e) == false)
				e.OnCompleted(connection.Socket);
		}

		private void Receive_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			try
			{
				do
				{
					bool close = true;
					int connectionId = e.ConnectionId;

					if (socket.Connected && ((SocketAsyncEventArgs)e).BytesTransferred > 0 &&
						e.SocketError == SocketError.Success)
					{
						close = !OnReceived(ref e);
					}

					if (close)
					{
						if (e != null)
							buffersPool.Put(ref e);

						connections.Remove(socket.RemoteEndPoint);
						socket.SafeShutdownClose();
						break;
					}

					if (e == null)
					{
						e = buffersPool.Get();
						PrepareBuffer(e, socket.RemoteEndPoint, connectionId);
					}
				}
				while (socket.ReceiveAsync(e) == false);
			}
			catch
			{
				if (isRunning)
					throw;

				if (e != null)
					buffersPool.Put(ref e);
			}
		}

		private void PrepareBuffer(ServerAsyncEventArgs e, EndPoint remote, int connectionId)
		{
			e.ConnectionId = connectionId;
			e.RemoteEndPoint = remote as IPEndPoint;
			e.Completed = Receive_Completed;
			if (initialBufferSize > 0)
				e.SetBuffer(initialOffset, initialBufferSize);
			else
			{
				int length = e.BufferCapacity - initialOffset;
				if (length < 1024)
					length = 1024;
				e.SetBuffer(initialOffset, length);
			}
		}
	}
}
