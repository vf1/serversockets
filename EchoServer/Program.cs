// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using SocketServers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace EchoServer
{
	class Program
	{
		class BaseConnection2 : BaseConnection, IDisposable
		{
			void IDisposable.Dispose() { }
		}

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
			var serversManager = new ServersManager<BaseConnection2>(new ServersManagerConfig() { /*TcpOffsetOffset = 256, */TlsCertificate = certificate, });
			serversManager.FakeAddressAction =
				(ServerEndPoint real1) =>
				{
					if (real != null && real.Equals(real1))
						return fake;
					return new IPEndPoint(address, port++);
				};
			serversManager.Bind(new ProtocolPort() { Protocol = ServerProtocol.Tcp, Port = serverPort, });
			serversManager.Bind(new ProtocolPort() { Protocol = ServerProtocol.Udp, Port = serverPort, });
			serversManager.Bind(new ProtocolPort() { Protocol = ServerProtocol.Tls, Port = serverPort + 1, });
			serversManager.ServerAdded += ServersManager_ServerAdded;
			serversManager.ServerRemoved += ServersManager_ServerRemoved;
			serversManager.ServerInfo += ServersManager_ServerInfo;
			serversManager.Received += ServersManager_Received;
			serversManager.Sent += ServersManager_Sent;
			serversManager.NewConnection += ServersManager_NewConnection;
			serversManager.EndConnection += ServersManager_EndConnection;

			//serversManager.Logger.Enable("test-log.pcap");

			//for (int i = 0; i < 10; i++)
			//	serversManager.BuffersPool.Get();

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

			//CreateSomeGarbage(serversManager);

			Console.WriteLine(@"Press any key to stop server...");

			while (Console.KeyAvailable == false)
			{
				Console.Write("Connections: {0} - {1} = {2}\t\r", openedConnections, closedConnections, openedConnections - closedConnections);
				Thread.Sleep(500);
			}
			Console.ReadKey(true);

			Console.WriteLine();

			/////////////////////////////////////////////////////////////////////////

			Console.WriteLine(@"Stats:");
			Console.WriteLine(@"  Buffers Created : {0}", EventArgsManager.Created);
			Console.WriteLine(@"  Buffers Queued  : {0}", EventArgsManager.Queued);

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

			//CreateSomeGarbage(serversManager);

			for (int i = 0; i < 240; i++)
			{
				if (EventArgsManager.Created == EventArgsManager.Queued)
					break;
				Console.Write("\rWaiting for buffers: {0} seconds", i / 2);
				Thread.Sleep(500);
			}
			Console.WriteLine();

			if (EventArgsManager.Created != EventArgsManager.Queued)
			{
				Console.WriteLine(@"Lost buffers:");
				Console.WriteLine(@"  Buffers Created : {0}", EventArgsManager.Created);
				Console.WriteLine(@"  Buffers Queued  : {0}", EventArgsManager.Queued);

				Console.WriteLine("  GC for gen #0 {0}, #1 {1}, #2 {2}", GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
				Console.Write(@"  Trying to garbage lost buffers...");
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				if (EventArgsManager.Created != EventArgsManager.Queued)
				{
					Console.WriteLine("Failed {0}", EventArgsManager.Created - EventArgsManager.Queued);
					Console.WriteLine("  GC for gen #0 {0}, #1 {1}, #2 {2}", GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
				}
				else
					Console.WriteLine("Ok");
				Console.ReadKey(true);
			}
		}

		static void CreateSomeGarbage(ServersManager<BaseConnection2> serversManager)
		{
			EventArgsManager.Get();
			EventArgsManager.Get();
			EventArgsManager.Get();
			EventArgsManager.Get();
			EventArgsManager.Get();
		}

		static bool ServersManager_Received(ServersManager<BaseConnection2> server, BaseConnection c, ref ServerAsyncEventArgs e)
		{
			e.Count = e.BytesTransferred;
			server.SendAsync(e);
			e = null;

			return true;
		}

		static void ServersManager_Sent(ServersManager<BaseConnection2> server, ref ServerAsyncEventArgs e)
		{
			if (e.SocketError != SocketError.Success)
				Console.WriteLine("Sent error: {0}", e.SocketError.ToString());
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

		private static int openedConnections;
		private static int closedConnections;

		static void ServersManager_NewConnection(ServersManager<BaseConnection2> s, BaseConnection e)
		{
			Interlocked.Increment(ref openedConnections);
		}

		static void ServersManager_EndConnection(ServersManager<BaseConnection2> s, BaseConnection e)
		{
			Interlocked.Increment(ref closedConnections);
		}
	}
}
