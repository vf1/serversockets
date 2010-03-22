// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	class UdpServer
		: Server
	{
		private object sync;
		private Socket socket;

		public UdpServer()
			: base()
		{
			sync = new object();
		}

		public override void Start()
		{
			lock (sync)
			{
				isRunning = true;

				socket = new Socket(realEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
				socket.Bind(realEndPoint);

				ThreadPool.QueueUserWorkItem(new WaitCallback(EnqueueBuffers), 16);
			}
		}

		public override void Stop()
		{
			isRunning = false;

			lock (sync)
			{
				if (socket != null)
				{
					socket.Shutdown(SocketShutdown.Both);
					socket.Close();
					socket = null;
				}
			}
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			e.Completed = Send_Completed;

			if (socket.SendToAsync(e) == false)
				e.OnCompleted(socket);
		}

		private void EnqueueBuffers(Object stateInfo)
		{
			int count = (int)stateInfo;

			for (int i = 0; i < count; i++)
			{
				var e = buffersPool.Get();

				PrepareBuffer(e);
				if (socket.ReceiveFromAsync(e) == false)
					e.OnCompleted(socket);
			}
		}

		private void ReceiveFrom_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			while (isRunning)
			{
				if (e.SocketError == SocketError.Success)
					OnReceived(ref e);
				else
				{
					if (isRunning)
					{
						Stop();
						OnFailed(new ServerInfoEventArgs(realEndPoint, e.SocketError));
					}
				}

				if (e == null)
					e = buffersPool.Get();

				PrepareBuffer(e);
				if (socket.ReceiveFromAsync(e))
					break;
			}

			if (isRunning == false)
				buffersPool.Put(e);
		}

		private void PrepareBuffer(ServerAsyncEventArgs e)
		{
			e.Completed = ReceiveFrom_Completed;
			e.SetAnyRemote(realEndPoint.AddressFamily);
			e.SetBuffer();
		}
	}
}
