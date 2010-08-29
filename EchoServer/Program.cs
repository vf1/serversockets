// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using SocketServers;
using System.Security.Cryptography.X509Certificates;

namespace EchoServer
{
	class Program
	{
		static void Main(string[] args)
		{
			int serverPort = 5070;
			if (args.Length >= 1)
				int.TryParse(args[0], out serverPort);
			Console.WriteLine("Server Port: {0}", serverPort);

			IPEndPoint real = null, fake = null;
			if (args.Length >= 4)
			{
				int fakePort;
				IPAddress realIp, fakeIp;
				if (IPAddress.TryParse(args[1], out realIp) && IPAddress.TryParse(args[2], out fakeIp) &&
					int.TryParse(args[3], out fakePort))
				{
					real = new IPEndPoint(realIp, serverPort);
					fake = new IPEndPoint(fakeIp, fakePort);
					Console.WriteLine("Fake and real IP parsed.");
					Console.WriteLine("Real - Fake: {0} <=> {1}", real, fake);
				}
			}

			Console.Write(@"Load certificate...");
			var certificate = new X509Certificate2("SocketServers.pfx");
			Console.WriteLine(@"Ok");

			Console.Write(@"Initialize...");

			int port = 6000;
			IPAddress address = IPAddress.Parse(@"200.200.200.200");
			var serversManager = new ServersManager<BaseConnection>(2048, new ServersManagerConfig() { TcpOffsetOffset = 256, TlsCertificate = certificate });
			serversManager.FakeAddressAction =
				(ServerEndPoint real1) =>
				{
					if (real != null && real.Equals(real1))
						return fake;
					return new IPEndPoint(address, port++);
				};
			serversManager.Bind(new ProtocolPort() { Protocol = ServerIpProtocol.Tcp, Port = serverPort, });
			serversManager.Bind(new ProtocolPort() { Protocol = ServerIpProtocol.Udp, Port = serverPort, });
			serversManager.Bind(new ProtocolPort() { Protocol = ServerIpProtocol.Tls, Port = serverPort + 1, });
			serversManager.ServerAdded += ServersManager_ServerAdded;
			serversManager.ServerRemoved += ServersManager_ServerRemoved;
			serversManager.ServerInfo += ServersManager_ServerInfo;
			serversManager.Received += ServersManager_Received;
			serversManager.Sent += ServersManager_Sent;
			serversManager.NewConnection += ServersManager_NewConnection;
			serversManager.EndConnection += ServersManager_EndConnection;

			Console.WriteLine(@"Ok");

			/////////////////////////////////////////////////////////////////////////

			Console.WriteLine(@"Starting...");

			try
			{
				serversManager.Start();
				Console.WriteLine(@"Started!");
			}
			catch (Exception ex)
			{
				Console.WriteLine(@"Failed to start");
				Console.WriteLine(@"Error: {0}", ex.Message);
			}

			/////////////////////////////////////////////////////////////////////////

			Console.WriteLine(@"Press any key to stop server...");
			Console.ReadKey();
			Console.WriteLine();

			/////////////////////////////////////////////////////////////////////////

			Console.WriteLine(@"Stats:");
			Console.WriteLine(@"  Buffers Created : {0}", serversManager.BuffersPool.Created);
			Console.WriteLine(@"  Buffers Queued  : {0}", serversManager.BuffersPool.Queued);

			/////////////////////////////////////////////////////////////////////////

			Console.WriteLine(@"Stoping...");

			try
			{
				serversManager.Dispose();
				Console.WriteLine(@"Stopped");
			}
			catch (Exception ex)
			{
				Console.WriteLine(@"Failed to stop");
				Console.WriteLine(@"Error: {0}", ex.Message);
			}

			serversManager.ServerAdded -= ServersManager_ServerAdded;
			serversManager.ServerInfo -= ServersManager_ServerInfo;
			serversManager.ServerRemoved -= ServersManager_ServerRemoved;
			serversManager.Received -= ServersManager_Received;
			serversManager.Sent -= ServersManager_Sent;
			serversManager.NewConnection -= ServersManager_NewConnection;
			serversManager.EndConnection -= ServersManager_EndConnection;

			/////////////////////////////////////////////////////////////////////////

			System.Threading.Thread.Sleep(2000);
			if (serversManager.BuffersPool.Created != serversManager.BuffersPool.Queued)
			{
				Console.WriteLine(@"Lost buffers:");
				Console.WriteLine(@"  Buffers Created : {0}", serversManager.BuffersPool.Created);
				Console.WriteLine(@"  Buffers Queued  : {0}", serversManager.BuffersPool.Queued);
			}
		}

		static bool ServersManager_Received(ServersManager<BaseConnection> server, BaseConnection c, ref ServerAsyncEventArgs e)
		{
			e.SetBuffer(e.OffsetOffset, e.BytesTransferred);
			server.SendAsync(e);
			e = null;

			return true;
		}

		static void ServersManager_Sent(ServersManager<BaseConnection> server, ref ServerAsyncEventArgs e)
		{
		}

		static void ServersManager_ServerRemoved(object sender, ServerChangeEventArgs e)
		{
			Console.WriteLine(@"  - Removed: {0}", e.ServerEndPoint.ToString());
		}

		static void ServersManager_ServerAdded(object sender, ServerChangeEventArgs e)
		{
			Console.WriteLine(@"  -   Added: {0}", e.ServerEndPoint.ToString());
		}

		static void ServersManager_ServerInfo(object sender, ServerInfoEventArgs e)
		{
			Console.WriteLine(@"  -    Info: [ {0} ] {1}", e.ServerEndPoint.ToString(), e.ToString());
		}

		static void ServersManager_NewConnection(ServersManager<BaseConnection> s, BaseConnection e)
		{
			Console.WriteLine(@"  -    -    New Connection: [ {0} ] ID: {1}", e.LocalEndPoint.ToString(), e.Id);
		}

		static void ServersManager_EndConnection(ServersManager<BaseConnection> s, BaseConnection e)
		{
			Console.WriteLine(@"  -    -    End Connection: ID: {0}", e.Id);
		}
	}
}
