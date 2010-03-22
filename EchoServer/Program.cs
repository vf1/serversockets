// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using SocketServers;

namespace EchoServer
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.Write(@"Initialize...");

			int port = 6000;
			IPAddress address = IPAddress.Parse(@"200.200.200.200");
			var serversManager = new ServersManager(
				(ServerEndPoint real) => { return new IPEndPoint(address, port++); }, 256);
			serversManager.Bind(new ProtocolPort() { Protocol = ServerIpProtocol.Tcp, Port = 5070, InitialBufferSize = 128, });
			serversManager.Bind(new ProtocolPort() { Protocol = ServerIpProtocol.Udp, Port = 5070, });
			serversManager.ServerAdded += ServersManager_ServerAdded;
			serversManager.ServerRemoved += ServersManager_ServerRemoved;
			serversManager.ServerInfo += ServersManager_ServerInfo;
			serversManager.Received += ServersManager_Received;
			serversManager.Sent += ServersManager_Sent;

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
				serversManager.Stop();
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

			/////////////////////////////////////////////////////////////////////////

			System.Threading.Thread.Sleep(2000);
			if (serversManager.BuffersPool.Created != serversManager.BuffersPool.Queued)
			{
				Console.WriteLine(@"Lost buffers:");
				Console.WriteLine(@"  Buffers Created : {0}", serversManager.BuffersPool.Created);
				Console.WriteLine(@"  Buffers Queued  : {0}", serversManager.BuffersPool.Queued);
			}
		}

		static bool ServersManager_Received(ServersManager server, ref ServerAsyncEventArgs e)
		{
			if (e.LocalEndPoint.Protocol == ServerIpProtocol.Udp)
			{
				e.SetBuffer(e.BytesUsed);
				server.SendAsync(e);
				e = null;
			}
			else
			{
				if (e.ContinueBuffer() == false)
				{
					if (e.BytesUsed < 3072)
					{
						e.ContinueBuffer(3072);
					}
					else
					{
						e.SetBuffer(e.BytesUsed);
						server.SendAsync(e);
						e = null;
					}
				}
			}

			return true;
		}

		static void ServersManager_Sent(ServersManager server, ref ServerAsyncEventArgs e)
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
	}
}
