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

		public TcpServer()
			: base()
		{
			sync = new object();
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
					connections.ForEach((socket) => { Close(socket); });
					connections.Clear();

					listener.Close();
					listener = null;
				}
			}
		}

		private void Close(Socket socket)
		{
			try
			{
				try
				{
					socket.Shutdown(SocketShutdown.Both);
				}
				catch { }
				
				socket.Close();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			Socket socket = null;

			if (connections.TryGetValue(e.RemoteEndPoint, out socket))
			{
				if (socket.Connected == false)
				{
					Close(socket);
					socket = null;

					connections.Remove(e.RemoteEndPoint);
				}
			}

			if (socket == null)
			{
				socket = new Socket(realEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
				socket.Bind(realEndPoint);

				e.Completed = Connect_Completed;
				if (socket.ConnectAsync(e) == false)
					e.OnCompleted(socket);
			}
			else
			{
				e.Completed = Send_Completed;
				if (socket.SendAsync(e) == false)
					e.OnCompleted(socket);
			}
		}

		private void Connect_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			if (e.SocketError == SocketError.Success)
			{
				connections.Add(e.RemoteEndPoint, socket);
			}
			else
			{
				while (e.SocketError == SocketError.AddressAlreadyInUse)
				{
					if (connections.TryGetValue(e.RemoteEndPoint, out socket))
						break;

					Thread.Sleep(25);

					if (socket.ConnectAsync(e))
						return;
				}
			}

			if (socket.Connected)
			{
				e.Completed = Send_Completed;
				if (socket.SendAsync(e) == false)
					e.OnCompleted(socket);
			}
			else
			{
				e.OnCompleted(socket);
			}
		}

		private void Accept_Completed(object sender, SocketAsyncEventArgs acceptEventArgs)
		{
			if (isRunning == false)
			{
				Close(acceptEventArgs.AcceptSocket);
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
			var e = buffersPool.Get();
			PrepareBuffer(e, socket.RemoteEndPoint);

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

					if (socket.Connected && e.BytesTransferred > 0 &&
						e.SocketError == SocketError.Success)
					{
						close = !OnReceived(ref e);
					}

					if (close)
					{
						if (e != null)
							buffersPool.Put(ref e);

						connections.Remove(socket.RemoteEndPoint);
						Close(socket);
						break;
					}

					if (e == null)
					{
						e = buffersPool.Get();
						PrepareBuffer(e, socket.RemoteEndPoint);
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

		private void PrepareBuffer(ServerAsyncEventArgs e, EndPoint remote)
		{
			e.Completed = Receive_Completed;
			e.RemoteEndPoint = remote as IPEndPoint;
			e.SetBuffer(initialBufferSize);
		}
	}
}
