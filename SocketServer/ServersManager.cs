// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;

namespace SocketServers
{
	public partial class ServersManager
	{
		private object sync;
		private bool running;
		private SafeDictionary<ServerEndPoint, Server> servers;
		private SafeDictionary<ServerEndPoint, Server> fakeServers;
		private List<ProtocolPort> protocolPorts;
		private BuffersPool<ServerAsyncEventArgs> buffersPool;
		private List<UnicastIPAddressInformation> networkAddressInfos;
		private ServersManagerConfig config;
		private int nextPort;

		public ServersManager(int buffersPoolSize, ServersManagerConfig config)
			: this(new BuffersPool<ServerAsyncEventArgs>(buffersPoolSize), config)
		{
		}

		public ServersManager(BuffersPool<ServerAsyncEventArgs> buffersPool, ServersManagerConfig config)
		{
			this.running = false;

			this.sync = new object();
			this.protocolPorts = new List<ProtocolPort>();
			this.servers = new SafeDictionary<ServerEndPoint, Server>();
			this.fakeServers = new SafeDictionary<ServerEndPoint, Server>();

			this.AddressPredicate = DefaultAddressPredicate;
			this.FakeAddressAction = DefaultFakeAddressAction;
	
			this.buffersPool = buffersPool;
			this.config = config;

			this.nextPort = config.MinPort;
		}

		public event EventHandler<ServerChangeEventArgs> ServerRemoved;
		public event EventHandler<ServerChangeEventArgs> ServerAdded;
		public event EventHandler<ServerInfoEventArgs> ServerInfo;
		public event ServerEventHandlerRef<ServersManager, ServerAsyncEventArgs, bool> Received;
		public event ServerEventHandlerRef<ServersManager, ServerAsyncEventArgs> Sent;
		public event ServerEventHandlerVal<ServersManager, ServerConnectionEventArgs> NewConnection;

		private static bool DefaultAddressPredicate(NetworkInterface interface1, IPInterfaceProperties properties, UnicastIPAddressInformation addrInfo)
		{
			return true;
		}

		private static IPEndPoint DefaultFakeAddressAction(ServerEndPoint endpoint)
		{
			return null;
		}

		public void Start()
		{
			lock (sync)
			{
				running = true;

				NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;

				AddServers(GetEndpointInfos(protocolPorts), false);
			}
		}

		public void Stop()
		{
			NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;

			lock (sync)
			{
				running = false;
				servers.RemoveAll((endpoint) => { return true; }, OnServerRemoved);
			}
		}

		public Func<NetworkInterface, IPInterfaceProperties, UnicastIPAddressInformation, bool> AddressPredicate
		{
			get;
			set;
		}

		public Func<ServerEndPoint, IPEndPoint> FakeAddressAction
		{
			get;
			set;
		}

		public BuffersPool<ServerAsyncEventArgs> BuffersPool
		{
			get { return buffersPool; }
		}

		public SocketError Bind(ProtocolPort pp)
		{
			lock (sync)
			{
				protocolPorts.Add(pp);

				if (running)
					return AddServers(GetEndpointInfos(pp), false);

				return SocketError.Success;
			}
		}

		public SocketError Bind(ref ProtocolPort pp)
		{
			lock (sync)
			{
				if (nextPort < 0)
					throw new InvalidOperationException(@"Port range was not assigned");

				for (int i = 0; i < config.MaxPort - config.MinPort; i++)
				{
					pp.Port = nextPort++;

					var result = AddServers(GetEndpointInfos(pp), false);

					if (result != SocketError.AddressAlreadyInUse)
						return result;
				}

				return SocketError.TooManyOpenSockets;
			}
		}

		public void Unbind(ProtocolPort pp)
		{
			lock (sync)
			{
				protocolPorts.Remove(pp);

				if (running)
					servers.RemoveAll((endpoint) => { return endpoint.Port == pp.Port && endpoint.Protocol == pp.Protocol; }, OnServerRemoved);
			}
		}

		public void SendAsync(ServerAsyncEventArgs eventArgs)
		{
			SendAsync(eventArgs, true);
		}

		public void SendAsync(ServerAsyncEventArgs eventArgs, bool connect)
		{
			var server = servers.GetValue(eventArgs.LocalEndPoint);

			if (server == null)
				server = fakeServers.GetValue(eventArgs.LocalEndPoint);

			if (server != null)
			{
				server.SendAsync(eventArgs, connect);
			}
			else
			{
				eventArgs.SocketError = SocketError.NetworkDown;
				Server_Sent(null, eventArgs);
			}
		}

		private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
		{
			OnServerInfo(new ServerInfoEventArgs(ServerEndPoint.NoneEndPoint,
				@"NetworkChange.NetworkAddressChanged"));

			lock (sync)
			{
				networkAddressInfos = null;

				var infos = GetEndpointInfos(protocolPorts);

				AddServers(infos, true);

				servers.RemoveAll(
					(endpoint) =>
					{
						foreach (var info in infos)
							if (info.ServerEndPoint.IsEqual(endpoint))
								return false;
						return true;
					},
					OnServerRemoved);
			}
		}

		private List<UnicastIPAddressInformation> NetworkAddresses
		{
			get
			{
				if (networkAddressInfos == null)
				{
					networkAddressInfos = new List<UnicastIPAddressInformation>();

					NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
					foreach (var interface1 in interfaces)
					{
						if (interface1.OperationalStatus == OperationalStatus.Up)
						{
							var properties = interface1.GetIPProperties();
							foreach (var addressInfo in properties.UnicastAddresses)
							{
								if (AddressPredicate(interface1, properties, addressInfo))
									networkAddressInfos.Add(addressInfo);
							}
						}
					}
				}

				return networkAddressInfos;
			}
		}

		private IEnumerable<EndpointInfo> GetEndpointInfos(IEnumerable<ProtocolPort> pps)
		{
			foreach (var address in NetworkAddresses)
				foreach (var pp in pps)
					yield return new EndpointInfo(pp, address);
		}

		private IEnumerable<EndpointInfo> GetEndpointInfos(ProtocolPort pp)
		{
			foreach (var address in NetworkAddresses)
				yield return new EndpointInfo(pp, address);
		}

		private SocketError AddServers(IEnumerable<EndpointInfo> infos, bool ignoreErrors)
		{
			SocketError error = SocketError.Success;
			List<Server> created = new List<Server>();

			foreach (var info in infos)
			{
				if (servers.ContainsKey(info.ServerEndPoint) == false)
				{
					IPEndPoint fakeEndpoint = null;
					if (info.ServerEndPoint.AddressFamily == AddressFamily.InterNetwork)
					{
						if (info.AddressInformation.IPv4Mask != null)
							fakeEndpoint = FakeAddressAction(info.ServerEndPoint);
					}

					var server = Server.Create(info.ServerEndPoint, fakeEndpoint, info.AddressInformation.IPv4Mask, buffersPool, config);
					server.Received = Server_Received;
					server.Sent = Server_Sent;
					server.Failed = Server_Failed;
					server.NewConnection = Server_NewConnection;

					try
					{
						server.Start();
					}
					catch (Exception ex)
					{
						if (ignoreErrors)
							OnServerInfo(new ServerInfoEventArgs(info.ServerEndPoint, ex));
						else
						{
							if (ex is SocketException)
								error = (ex as SocketException).SocketErrorCode;
							else
								throw;
							break;
						}
					}

					created.Add(server);
				}
			}

			if (error != SocketError.Success)
			{
				foreach (var server in created)
					server.Stop();
			}
			else
			{
				foreach (var server in created)
				{
					servers.Add(server.LocalEndPoint, server);
					OnServerAdded(server);
				}
			}

			return error;
		}

		private bool Server_Received(Server server, ref ServerAsyncEventArgs e)
		{
			if (Received != null)
				return Received(this, ref e);
			return false;
		}

		private void Server_Sent(Server server, ServerAsyncEventArgs e)
		{
			if (Sent != null)
				Sent(this, ref e);

			if (e != null)
				buffersPool.Put(e);
		}

		private void Server_Failed(Server server, ServerInfoEventArgs e)
		{
			servers.Remove(server.LocalEndPoint);
			OnServerRemoved(server);
			OnServerInfo(e);
		}

		private void Server_NewConnection(Server server, ServerConnectionEventArgs e)
		{
			if (NewConnection != null)
				NewConnection(this, e);
		}

		private void OnServerAdded(Server server)
		{
			if (server.FakeEndPoint != null)
			{
				fakeServers.Add(server.FakeEndPoint, server);
				if (ServerAdded != null)
					ServerAdded(this, new ServerChangeEventArgs(server.FakeEndPoint));
			}

			if (ServerAdded != null)
				ServerAdded(this, new ServerChangeEventArgs(server.LocalEndPoint));
		}

		private void OnServerRemoved(Server server)
		{
			server.Stop();

			if (server.FakeEndPoint != null)
			{
				fakeServers.Remove(server.FakeEndPoint);
				if (ServerRemoved != null)
					ServerRemoved(this, new ServerChangeEventArgs(server.FakeEndPoint));
			}

			if (ServerRemoved != null)
				ServerRemoved(this, new ServerChangeEventArgs(server.LocalEndPoint));
		}

		private void OnServerInfo(ServerInfoEventArgs e)
		{
			if (ServerInfo != null)
				ServerInfo(this, e);
		}
	}
}
