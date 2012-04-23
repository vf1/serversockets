// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace SocketServers
{
	abstract partial class Server<C>
		: IDisposable
		where C : BaseConnection, IDisposable, new()
	{
		protected volatile bool isRunning;
		protected ServerEndPoint realEndPoint;
		private ServerEndPoint fakeEndPoint;
		private long ip4Mask;
		private long ip4Subnet;

		public Server()
		{
		}

		public ServerEventHandlerVal<Server<C>, ServerInfoEventArgs> Failed;
		public ServerEventHandlerRef<Server<C>, C, ServerAsyncEventArgs, bool> Received;
		public ServerEventHandlerVal<Server<C>, ServerAsyncEventArgs> Sent;
		public ServerEventHandlerVal<Server<C>, C, ServerAsyncEventArgs> BeforeSend;
		public ServerEventHandlerVal<Server<C>, C> NewConnection;
		public ServerEventHandlerVal<Server<C>, C> EndConnection;

		public abstract void Start();
		public abstract void Dispose();
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
			Sent(this, e);
		}

		protected virtual bool OnReceived(Connection<C> c, ref ServerAsyncEventArgs e)
		{
			e.LocalEndPoint = GetLocalEndpoint(e.RemoteEndPoint.Address);

			return Received(this, c != null ? c.UserConnection : null, ref e);
		}

		protected virtual void OnFailed(ServerInfoEventArgs e)
		{
			Failed(this, e);
		}

		protected virtual void OnNewConnection(Connection<C> connection)
		{
			connection.UserConnection = new C()
			{
				LocalEndPoint = GetLocalEndpoint(connection.RemoteEndPoint.Address),
				RemoteEndPoint = connection.RemoteEndPoint,
				Id = connection.Id,
			};

			NewConnection(this, connection.UserConnection);
		}

		protected virtual void OnEndConnection(Connection<C> connection)
		{
			EndConnection(this, connection.UserConnection);
		}

		protected void OnBeforeSend(Connection<C> connection, ServerAsyncEventArgs e)
		{
			BeforeSend(this, (connection == null) ? null : connection.UserConnection, e);
		}

		public static Server<C> Create(ServerEndPoint real, IPEndPoint ip4fake, IPAddress ip4mask, ServersManagerConfig config)
		{
			Server<C> server = null;

			if (real.Protocol == ServerProtocol.Tcp)
				server = new TcpServer<C>(config);
			else if (real.Protocol == ServerProtocol.Udp)
				server = new UdpServer<C>(config);
			else if (real.Protocol == ServerProtocol.Tls)
				server = new SspiTlsServer<C>(config);
			else
				throw new InvalidOperationException(@"Protocol is not supported.");

			server.realEndPoint = real.Clone();

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

		public ServerEndPoint GetLocalEndpoint(IPAddress addr)
		{
			if (fakeEndPoint != null && IPAddress.IsLoopback(addr) == false)
			{
				long remote = GetIPv4Long(addr);

				if ((remote & ip4Mask) != ip4Subnet)
					return fakeEndPoint;
			}

			return realEndPoint;
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
