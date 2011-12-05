using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Sockets;

namespace Pcap
{
	// http://wiki.wireshark.org/Development/LibpcapFileFormat
	// http://www.zytrax.com/tech/protocols/tcp.html
	// http://en.wikipedia.org/wiki/Transmission_Control_Protocol

	public enum Protocol
	{
		Tcp,
		Udp,
		Tls,
	}

	public class PcapWriter
		: IDisposable
	{
		public const int IPv4HeaderLength = 20;
		public const int IPv6HeaderLength = 40;
		public const int EthernetLength = 14;
		public const int UdpLength = 8;
		public const int TcpLength = 20;
		public const int TlsLength = 5;
		public const int MaxRecordLength = 65535;

		private readonly object sync;
		private readonly Stream stream;
		private readonly DateTime nixTimeStart;
		private readonly byte[] mac1;
		private readonly byte[] mac2;

		[ThreadStatic]
		private static MemoryStream cacheStream;
		[ThreadStatic]
		private static BinaryWriter writter;

		enum EtherType
			: ushort
		{
			None = 0xFFFF,
			IPv4 = 0x0800,
			IPv6 = 0x86DD,
		}

		public PcapWriter(Stream stream)
		{
			this.sync = new object();
			this.nixTimeStart = new DateTime(1970, 1, 1);
			this.mac1 = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, };
			this.mac2 = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, };

			this.stream = stream;

			CreateWritter();
			WriteGlobalHeader();
			WriteChangesToStream();
		}

		public void Dispose()
		{
			stream.Dispose();
		}

		public void WriteComment(string comment)
		{
			CreateWritter();

			var bytes = Encoding.UTF8.GetBytes(comment);
			WritePacketHeader(bytes.Length + EthernetLength);
			WriteEthernetHeader(EtherType.None);
			writter.Write(bytes);

			WriteChangesToStream();
		}

		public void Write(byte[] bytes, Protocol protocol, IPEndPoint source, IPEndPoint destination)
		{
			Write(bytes, 0, bytes.Length, protocol, source, destination);
		}

		public void Write(byte[] bytes, int offset, int length, Protocol protocol, IPEndPoint source, IPEndPoint destination)
		{
			CreateWritter();

			if (source.AddressFamily != destination.AddressFamily)
				throw new ArgumentException("source.AddressFamily != destination.AddressFamily");

			int tcpUdpLength = (protocol == Protocol.Udp) ? UdpLength :
				(TcpLength + ((protocol == Protocol.Tls) ? TlsLength : 0));

			if (source.AddressFamily == AddressFamily.InterNetwork)
			{
				WritePacketHeader(length + tcpUdpLength + IPv4HeaderLength + EthernetLength);
				WriteEthernetHeader(EtherType.IPv4);
				WriteIpV4Header(length + tcpUdpLength, protocol != Protocol.Udp, source.Address, destination.Address);
			}
			else if (source.AddressFamily == AddressFamily.InterNetworkV6)
			{
				WritePacketHeader(length + tcpUdpLength + IPv6HeaderLength + EthernetLength);
				WriteEthernetHeader(EtherType.IPv6);
				WriteIpV6Header(length + tcpUdpLength, protocol != Protocol.Udp, source.Address, destination.Address);
			}
			else
				throw new ArgumentOutOfRangeException(@"source.AddressFamily");

			if (protocol == Protocol.Udp)
			{
				WriteUdpHeader(length, (short)source.Port, (short)destination.Port);
			}
			else
			{
				WriteTcpHeader(length + ((protocol == Protocol.Tls) ? TlsLength : 0), (short)source.Port, (short)destination.Port);
				if (protocol == Protocol.Tls)
					WriteTlsHeader(length);
			}

			writter.Write(bytes, offset, length);

			WriteChangesToStream();
		}

		private void CreateWritter()
		{
			if (writter == null)
			{
				cacheStream = new MemoryStream();
				writter = new BinaryWriter(cacheStream);
			}
		}

		private void WriteChangesToStream()
		{
			cacheStream.Flush();

			lock (sync)
				stream.Write(cacheStream.GetBuffer(), 0, Math.Min((int)cacheStream.Length, MaxRecordLength));

			cacheStream.SetLength(0);
		}

		private void WriteGlobalHeader()
		{
			writter.Write(0xa1b2c3d4);
			writter.Write((ushort)0x0002);
			writter.Write((ushort)0x0004);
			writter.Write(0);
			writter.Write(0);
			writter.Write(MaxRecordLength);
			writter.Write(1);
		}

		private void WritePacketHeader(int length)
		{
			var nixTime = DateTime.UtcNow - nixTimeStart;

			writter.Write((int)nixTime.TotalSeconds);
			writter.Write(nixTime.Milliseconds);
			writter.Write(length);
			writter.Write(length);
		}

		private void WriteEthernetHeader(EtherType etherType)
		{
			writter.Write(mac1);
			writter.Write(mac2);
			writter.Write(IPAddress.HostToNetworkOrder((short)etherType));
		}

		private void WriteIpV4Header(int length, bool tcpUdp, IPAddress source, IPAddress destination)
		{
			writter.Write((ushort)0x0005);
			writter.Write(IPAddress.HostToNetworkOrder((short)(length + IPv4HeaderLength)));
			writter.Write(0x00000000);
			writter.Write((byte)0xff);
			writter.Write((byte)(tcpUdp ? 0x06 : 0x11));
			writter.Write((short)0);
#pragma warning disable 0618
			// This property is obsolete. Use GetAddressBytes.
			writter.Write((int)source.Address);
			writter.Write((int)destination.Address);
#pragma warning restore 0618
		}

		private void WriteIpV6Header(int length, bool tcpUdp, IPAddress source, IPAddress destination)
		{
			writter.Write(0x00000060);
			writter.Write(IPAddress.HostToNetworkOrder((short)length));
			writter.Write((byte)(tcpUdp ? 0x06 : 0x11));
			writter.Write((byte)0xff);
			writter.Write(source.GetAddressBytes());
			writter.Write(destination.GetAddressBytes());
		}

		private void WriteUdpHeader(int length, short sourcePort, short destinationPort)
		{
			writter.Write(IPAddress.HostToNetworkOrder(sourcePort));
			writter.Write(IPAddress.HostToNetworkOrder(destinationPort));
			writter.Write(IPAddress.HostToNetworkOrder((short)(UdpLength + length)));
			writter.Write((short)0);
		}

		private void WriteTcpHeader(int length, short sourcePort, short destinationPort)
		{
			writter.Write(IPAddress.HostToNetworkOrder(sourcePort));
			writter.Write(IPAddress.HostToNetworkOrder(destinationPort));
			writter.Write(IPAddress.HostToNetworkOrder(0));
			writter.Write(IPAddress.HostToNetworkOrder(0));
			writter.Write((byte)0x50);
			writter.Write((byte)0x02);
			writter.Write((ushort)16383);
			writter.Write((ushort)0);
			writter.Write((ushort)0);

			//sequenceNumber += length + TcpLength;
		}

		private void WriteTlsHeader(int length)
		{
			writter.Write((byte)0x17);
			writter.Write((ushort)0x0103);
			writter.Write(IPAddress.HostToNetworkOrder((short)length));
		}
	}
}
