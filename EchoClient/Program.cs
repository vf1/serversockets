// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;

namespace EchoClient
{
	class Program
	{
		static void Main(string[] args)
		{
			IPEndPoint server1 = new IPEndPoint(IPAddress.Parse(@"192.168.1.15"), 5070);
			IPEndPoint server2 = new IPEndPoint(IPAddress.Parse(@"::1"), 5070);

			Console.WriteLine(@"EchoClient");
			Console.WriteLine(@"Sleep 5 seconds");
			System.Threading.Thread.Sleep(5000);

			EchoTcp(server1);
			EchoTcp(server2);
			EchoUdp(server1);
			EchoUdp(server2);

			Console.WriteLine(@"Press any key to stop client...");
			Console.ReadKey();
			Console.WriteLine();
		}

		private static void EchoUdp(IPEndPoint server)
		{
			var data1 = new byte[1024];
			for (int i = 0; i < data1.Length; i++)
				data1[i] = (byte)i;

			var data2 = new byte[data1.Length];

			Socket socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint((server.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Any : IPAddress.IPv6Any, 0));

			Console.WriteLine(@"UDP: Send from {0} to {1}", socket.LocalEndPoint.ToString(), server.ToString());

			int start = Environment.TickCount;

			for (int i = 0; i < 1024 * 256; i++)
			{
				data1[i % 16] = (byte)i;
				socket.SendTo(data1, server);
				socket.SendTo(data1, server);
				socket.SendTo(data1, server);

				socket.Receive(data2);
				socket.Receive(data2);

				if (socket.Receive(data2) != data1.Length)
					Console.WriteLine(@"UDP: Echo Error #1");
				else
					for (int j = 0; j < 16; j++)
						if (data1[j] != data2[j])
						{
							Console.WriteLine(@"UDP: Echo Error #2");
							break;
						}
			}

			socket.Close();

			Console.WriteLine(@"Elapsed: {0} ms", Environment.TickCount - start);
		}

		private static void EchoTcp(IPEndPoint server)
		{
			int start = Environment.TickCount;

			Socket[] sockets = new Socket[64];

			Console.WriteLine(@"TCP: Create {0} TCP connections", sockets.Length);

			for (int i = 0; i < sockets.Length; i++)
			{
				sockets[i] = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				sockets[i].Bind(new IPEndPoint((server.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Any : IPAddress.IPv6Any, 0));
				sockets[i].Connect(server);
			}

			var data1 = new byte[1024];
			for (int i = 0; i < data1.Length; i++)
				data1[i] = (byte)i;

			var data2 = new byte[data1.Length];

			Console.WriteLine(@"TCP: Send to {0}", server.ToString());

			for (int i = 0; i < 1024 * 256; i++)
			{
				int x = i % sockets.Length;

				sockets[x].Send(data1);
				sockets[x].Send(data1);
				data1[i % 16] = (byte)i;
				sockets[x].Send(data1);

				TcpReceive(sockets[x], data2);
				TcpReceive(sockets[x], data2);

				if (TcpReceive(sockets[x], data2) != data1.Length)
					Console.WriteLine(@"TCP: Echo Error #1");
				else
					for (int j = 0; j < 16; j++)
						if (data1[j] != data2[j])
						{
							Console.WriteLine(@"TCP: Echo Error #2");
							break;
						}
			}

			for (int i = 0; i < sockets.Length; i++)
			{
				sockets[i].Shutdown(SocketShutdown.Both);
				sockets[i].Close();
			}

			Console.WriteLine(@"Elapsed: {0} ms", Environment.TickCount - start);
		}

		static int TcpReceive(Socket socket, byte[] buffer)
		{
			int offset = 0;
			for (; offset < buffer.Length; )
				offset += socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
			return offset;
		}
	}
}
