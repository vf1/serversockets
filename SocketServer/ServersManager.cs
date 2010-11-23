// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

namespace SocketServers
{
	public partial class ServersManager<C>
		: IDisposable
		where C : BaseConnection, new()
	{
		private object sync;
		private bool running;
		private SafeDictionary<ServerEndPoint, Server<C>> servers;
		private SafeDictionary<ServerEndPoint, Server<C>> fakeServers;
		private List<ProtocolPort> protocolPorts;
		private List<UnicastIPAddressInformation> networkAddressInfos;
		private ServersManagerConfig config;
		private int nextPort;

		public ServersManager(ServersManagerConfig config)
		{
			if (BufferManager.IsInitialized() == false)
				BufferManager.Initialize(2048); // Mb

			if (EventArgsManager.IsInitialized() == false)
				EventArgsManager.Initialize();

			this.running = false;

			this.sync = new object();
			this.protocolPorts = new List<ProtocolPort>();
			this.servers = new SafeDictionary<ServerEndPoint, Server<C>>();
			this.fakeServers = new SafeDictionary<ServerEndPoint, Server<C>>();

			this.AddressPredicate = DefaultAddressPredicate;
			this.FakeAddressAction = DefaultFakeAddressAction;

			this.config = config;

			this.nextPort = config.MinPort;
		}

		public event EventHandler<ServerChangeEventArgs> ServerRemoved;
		public event EventHandler<ServerChangeEventArgs> ServerAdded;
		public event EventHandler<ServerInfoEventArgs> ServerInfo;
		public event ServerEventHandlerRef<ServersManager<C>, C, ServerAsyncEventArgs, bool> Received;
		public event ServerEventHandlerRef<ServersManager<C>, ServerAsyncEventArgs> Sent;
		public event ServerEventHandlerVal<ServersManager<C>, C> NewConnection;
		public event ServerEventHandlerVal<ServersManager<C>, C> EndConnection;

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

		public void Dispose()
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

		public LockFreePool<ServerAsyncEventArgs> BuffersPool
		{
			get { return EventArgsManager.Pool; }
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
			var server = servers.GetValue(eventArgs.LocalEndPoint);

			if (server == null)
				server = fakeServers.GetValue(eventArgs.LocalEndPoint);

			if (server != null)
			{
				server.SendAsync(eventArgs);
			}
			else
			{
				eventArgs.SocketError = SocketError.NetworkDown;
				Server_Sent(null, eventArgs);
			}
		}

		public X509Certificate2 FindCertificateInStore(string thumbprint)
		{
			X509Store store = null;
			try
			{
				store = new X509Store(StoreLocation.LocalMachine);

				var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

				if (found.Count > 0)
					return found[0];

				return null;
			}
			finally
			{
				if (store != null)
					store.Close();
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
			List<Server<C>> created = new List<Server<C>>();

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

					var server = Server<C>.Create(info.ServerEndPoint, fakeEndpoint, info.AddressInformation.IPv4Mask, config);
					server.Received = Server_Received;
					server.Sent = Server_Sent;
					server.Failed = Server_Failed;
					server.NewConnection = Server_NewConnection;
					server.EndConnection = Server_EndConnection;

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
					server.Dispose();
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

		private bool Server_Received(Server<C> server, C c, ref ServerAsyncEventArgs e)
		{
			try
			{
				if (Received != null)
					return Received(this, c, ref e);
				return false;
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in Received event handler", ex);
			}
		}

		private void Server_Sent(Server<C> server, ServerAsyncEventArgs e)
		{
			try
			{
				if (Sent != null)
					Sent(this, ref e);

				if (e != null)
					EventArgsManager.Put(e);
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in Sent event handler", ex);
			}
		}

		private void Server_Failed(Server<C> server, ServerInfoEventArgs e)
		{
			servers.Remove(server.LocalEndPoint, server);
			OnServerRemoved(server);
			OnServerInfo(e);
		}

		private void Server_NewConnection(Server<C> server, C e)
		{
			try
			{
				if (NewConnection != null)
					NewConnection(this, e);
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in NewConnection event handler", ex);
			}
		}

		private void Server_EndConnection(Server<C> server, C e)
		{
			try
			{
				if (EndConnection != null)
					EndConnection(this, e);
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in EndConnection event handler", ex);
			}
		}

		private void OnServerAdded(Server<C> server)
		{
			try
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
			catch (Exception ex)
			{
				throw new Exception(@"Error in ServerAdded event handler", ex);
			}
		}

		private void OnServerRemoved(Server<C> server)
		{
			server.Dispose();

			try
			{
				if (server.FakeEndPoint != null)
				{
					fakeServers.Remove(server.FakeEndPoint, server);
					if (ServerRemoved != null)
						ServerRemoved(this, new ServerChangeEventArgs(server.FakeEndPoint));
				}

				if (ServerRemoved != null)
					ServerRemoved(this, new ServerChangeEventArgs(server.LocalEndPoint));
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in ServerRemoved event handler", ex);
			}
		}

		private void OnServerInfo(ServerInfoEventArgs e)
		{
			try
			{
				if (ServerInfo != null)
					ServerInfo(this, e);
			}
			catch (Exception ex)
			{
				throw new Exception(@"Error in ServerInfo event handler", ex);
			}
		}
	}
}
