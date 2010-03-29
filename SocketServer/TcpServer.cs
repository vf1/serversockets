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
		private SafeDictionary<EndPoint, Socket> connections;
		private static int connectionCount;
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

				connections = new SafeDictionary<EndPoint, Socket>();

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

		public override void Stop()
		{
			isRunning = false;

			lock (sync)
			{
				if (listener != null)
				{
					connections.ForEach((socket) => { socket.SafeShutdownClose(); });
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		public override void SendAsync(ServerAsyncEventArgs e, bool connect)
		{
			Socket socket = null;

			if (connections.TryGetValue(e.RemoteEndPoint, out socket))
			{
				if (socket.Connected == false)
				{
					socket.SafeShutdownClose();
					socket = null;

					connections.Remove(e.RemoteEndPoint);
				}
			}

			if (socket == null)
			{
				if (connect)
				{
					socket = new Socket(realEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
					socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
					socket.Bind(realEndPoint);

					socket.ConnectAsync(e, Connect_Completed);
				}
				else
				{
					e.Completed = Send_Completed;
					e.SocketError = SocketError.NotConnected;
					e.OnCompleted(socket);
				}
			}
			else
			{
				socket.SendAsync(e, Send_Completed);
			}
		}

		private void Connect_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			bool beginReceive = false;

			if (e.SocketError == SocketError.Success)
			{
				connections.Add(e.RemoteEndPoint, socket);
				beginReceive = true;
			}
			else
			{
				while (e.SocketError == SocketError.AddressAlreadyInUse)
				{
					if (connections.TryGetValue(e.RemoteEndPoint, out socket))
						break;

					Thread.Sleep(0);

					if (socket.ConnectAsync(e))
						return;
					else
					{
						if (e.SocketError == SocketError.Success)
						{
							connections.Add(e.RemoteEndPoint, socket);
							beginReceive = true;
						}
					}
				}
			}

			// send message or report connection error
			//
			if (e.SocketError == SocketError.Success)
			{
				socket.SendAsync(e, Send_Completed);

				if (beginReceive)
					BeginReceive(socket);
			}
			else
			{
				e.Completed = Send_Completed;
				e.OnCompleted(socket);
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
				Socket socket = acceptEventArgs.AcceptSocket;

				if (acceptEventArgs.SocketError == SocketError.Success)
					connections.Add(socket.RemoteEndPoint, socket);

				acceptEventArgs.AcceptSocket = null;
				if (listener.AcceptAsync(acceptEventArgs) == false)
					Accept_Completed(listener, acceptEventArgs);

				if (isRunning)
					BeginReceive(socket);
			}
		}

		private void BeginReceive(Socket socket)
		{
			int connectionId = Interlocked.Increment(ref connectionCount);

			var e = buffersPool.Get();
			PrepareBuffer(e, socket.RemoteEndPoint, connectionId);

			OnNewConnection(socket.RemoteEndPoint, connectionId);

			if (socket.ReceiveAsync(e) == false)
				e.OnCompleted(socket);
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
				e.SetBuffer(initialOffset);
		}
	}
}
