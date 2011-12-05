using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pcap;

namespace PcapWriterTest
{
	class Program
	{
		static void Main(string[] args)
		{
			using (var writer = new PcapWriter(File.Create("test.pcap")))
			{
				//writer.Write("Hello W!", false);
				//writer.Write("Hello W!", true);

				writer.WriteComment("It is comment!");

				//var message = Encoding.UTF8.GetBytes("Hello World!");

				var message = Encoding.UTF8.GetBytes(
					"REGISTER sip:officesip.local SIP/2.0\r\n" +
					"Via: SIP/2.0/UDP 192.168.1.100:50554;rport;branch=z9hG4bKPjw.pkUaV7ZttbG6-bLQ8mkSpjLHXSbZ0z\r\n" +
					"Route: <sip:192.168.1.15;lr>\r\n" +
					"Max-Forwards: 70\r\n" +
					"From: \"a\" <sip:111@officesip.local>;tag=8oiu15i7rANaIkeCgOo7VD.CqjuLy6ny\r\n" +
					"To: \"a\" <sip:111@officesip.local>\r\n" +
					"Call-ID: eCUdTyjBT3d5I0cLdL5EYr5QOMHHUVay\r\n" +
					"CSeq: 7304 REGISTER\r\n" +
					"User-Agent: CSipSimple r1099 / thunderg-10\r\n" +
					"Contact: \"a\" <sip:111@192.168.1.100:50554;ob>\r\n" +
					"Expires: 300\r\n" +
					"Allow: PRACK, INVITE, ACK, BYE, CANCEL, UPDATE, SUBSCRIBE, NOTIFY, REFER, MESSAGE, OPTIONS\r\n" +
					"Content-Length:  0\r\n" +
					"\r\n");



				for (int i = 0; i < 1000; i++)
				{
					writer.Write(message, Protocol.Udp, new IPEndPoint(IPAddress.Loopback, 1234), new IPEndPoint(IPAddress.Broadcast, 4321));
					writer.Write(message, Protocol.Tcp, new IPEndPoint(IPAddress.Loopback, 1234), new IPEndPoint(IPAddress.Broadcast, 4321));
					writer.Write(message, Protocol.Tls, new IPEndPoint(IPAddress.Loopback, 5678), new IPEndPoint(IPAddress.Broadcast, 8765));
					writer.Write(message, Protocol.Udp, new IPEndPoint(IPAddress.IPv6Loopback, 1234), new IPEndPoint(IPAddress.IPv6Any, 4321));
					writer.Write(message, Protocol.Tcp, new IPEndPoint(IPAddress.IPv6Loopback, 1234), new IPEndPoint(IPAddress.IPv6Any, 4321));
					writer.Write(message, Protocol.Tls, new IPEndPoint(IPAddress.IPv6Loopback, 5678), new IPEndPoint(IPAddress.IPv6Any, 8765));
				}
			}
		}
	}
}
