// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace SocketServers
{
	abstract class Server
	{
		protected bool isRunning;
		protected BuffersPool<ServerAsyncEventArgs> buffersPool;
		protected ServerEndPoint realEndPoint;
		protected int initialBufferSize;
		private ServerEndPoint fakeEndPoint;
		private long ip4Mask;
		private long ip4Subnet;

		public Server()
		{
		}

		public ServerEventHandlerVal<Server, ServerInfoEventArgs> Failed;
		public ServerEventHandlerRef<Server, ServerAsyncEventArgs, bool> Received;
		public ServerEventHandlerVal<Server, ServerAsyncEventArgs> Sent;

		public abstract void Start();
		public abstract void Stop();
		public abstract void SendAsync(ServerAsyncEventArgs e);

		public ServerEndPoint LocalEndPoint
		{
			get { return realEndPoint; }
		}

		public ServerEndPoint FakeEndPoint
		{
			get { return fakeEndPoint; }
		}

		protected void Send_Completed(Socket socket, ServerAsyncEventArgs e)
		{
			if (Sent != null)
				Sent(this, e);
		}

		protected virtual bool OnReceived(ref ServerAsyncEventArgs e)
		{
			if (Received != null)
			{
				e.LocalEndPoint = null;

				if (fakeEndPoint != null && IPAddress.IsLoopback(e.RemoteEndPoint.Address) == false)
				{
					long remote = GetIPv4Long(e.RemoteEndPoint.Address);

					if ((remote & ip4Mask) != ip4Subnet)
						e.LocalEndPoint = fakeEndPoint;
				}

				if (e.LocalEndPoint == null)
					e.LocalEndPoint = realEndPoint;

				return Received(this, ref e);
			}

			return false;
		}

		protected virtual void OnFailed(ServerInfoEventArgs e)
		{
			if (Failed != null)
				Failed(this, e);
		}

		public static Server Create(ServerEndPoint real, IPEndPoint ip4fake, IPAddress ip4mask, BuffersPool<ServerAsyncEventArgs> buffersPool, int initialBufferSize)
		{
			Server server = null;

			if (real.Protocol == ServerIpProtocol.Tcp)
				server = new TcpServer();
			else if (real.Protocol == ServerIpProtocol.Udp)
				server = new UdpServer();
			else
				throw new InvalidOperationException(@"Protocol is not supported.");

			server.realEndPoint = real.Clone();
			server.buffersPool = buffersPool;
			server.initialBufferSize = initialBufferSize;

			if (ip4fake != null)
			{
				if (ip4mask == null)
					throw new ArgumentNullException(@"ip4mask");

				server.fakeEndPoint = new ServerEndPoint(server.realEndPoint.Protocol, ip4fake);
				server.ip4Mask = GetIPv4Long(ip4mask);
				server.ip4Subnet = GetIPv4Long(real.Address) & server.ip4Mask;
			}

			return server;
		}

		private static long GetIPv4Long(IPAddress address)
		{
#pragma warning disable 0618
			// This property is obsolete. Use GetAddressBytes.
			return address.Address;
#pragma warning restore 0618
		}
	}
}
