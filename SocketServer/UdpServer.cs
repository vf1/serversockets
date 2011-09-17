// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	class UdpServer<C>
		: Server<C>
		where C : BaseConnection, new()
	{
		private const int SIO_UDP_CONNRESET = -1744830452; // 0x9800000C

		private object sync;
		private Socket socket;
		private int queueSize;

		public UdpServer(ServersManagerConfig config)
			: base()
		{
			sync = new object();

			queueSize = (config.UdpQueueSize > 0) ? config.UdpQueueSize : 16;
		}

		public override void Start()
		{
			lock (sync)
			{
				isRunning = true;

				socket = new Socket(realEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
				socket.Bind(realEndPoint);

				// http://support.microsoft.com/kb/263823/en-us
				socket.IOControl(SIO_UDP_CONNRESET, new byte[4], null);

				ThreadPool.QueueUserWorkItem(new WaitCallback(EnqueueBuffers), queueSize);
			}
		}

		public override void Dispose()
		{
			isRunning = false;

			lock (sync)
			{
				if (socket != null)
				{
					socket.SafeShutdownClose();
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

			lock (sync)
			{
				if (socket != null)
				{
					for (int i = 0; i < count && isRunning; i++)
					{
						var e = EventArgsManager.Get();

						PrepareBuffer(e);
						if (socket.ReceiveFromAsync(e) == false)
							e.OnCompleted(socket);
					}
				}
			}
		}

		private void ReceiveFrom_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			while (isRunning)
			{
				if (e.SocketError == SocketError.Success)
				{
					OnReceived(null, ref e);

					if (e == null)
						e = EventArgsManager.Get();

					PrepareBuffer(e);

					try
					{
						if (socket.ReceiveFromAsync(e))
						{
							e = null;
							break;
						}
					}
					catch (ObjectDisposedException)
					{
					}
				}
				else
				{
					if (isRunning)
					{
						Dispose();
						OnFailed(new ServerInfoEventArgs(realEndPoint, e.SocketError));
					}
				}
			}

			if (e != null)
				EventArgsManager.Put(e);
		}

		private void PrepareBuffer(ServerAsyncEventArgs e)
		{
			e.Completed = ReceiveFrom_Completed;
			e.SetAnyRemote(realEndPoint.AddressFamily);
			e.AllocateBuffer();
		}
	}
}
