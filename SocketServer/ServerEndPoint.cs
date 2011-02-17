// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;

namespace SocketServers
{
	public class ServerEndPoint
		: IPEndPoint, IEquatable<ServerEndPoint>
	{
		public ServerEndPoint(ProtocolPort protocolPort, IPAddress address)
			: base(address, protocolPort.Port)
		{
			Protocol = protocolPort.Protocol;
		}


		public ServerEndPoint(ServerIpProtocol protocol, IPAddress address, int port)
			: base(address, port)
		{
			Protocol = protocol;
		}

		public ServerEndPoint(ServerIpProtocol protocol, IPEndPoint endpoint)
			: base(endpoint.Address, endpoint.Port)
		{
			Protocol = protocol;
		}

		public ServerIpProtocol Protocol { get; set; }

		public ProtocolPort ProtocolPort
		{
			get { return new ProtocolPort(Protocol, Port); }
		}

		public bool Equals(ServerEndPoint p)
		{
			return AddressFamily == p.AddressFamily && Port == p.Port &&
				Address.Equals(p.Address) && Protocol == p.Protocol;
		}

		public new string ToString()
		{
			return string.Format(@"{0}:{1}", Protocol.ToString(), base.ToString());
		}

		public ServerEndPoint Clone()
		{
			return new ServerEndPoint(Protocol, Address, Port);
		}

		public static ServerEndPoint NoneEndPoint =
			new ServerEndPoint(ServerIpProtocol.Tcp, IPAddress.None, 0);

		public override int GetHashCode()
		{
			return base.GetHashCode() ^ (int)Protocol;
		}
	}
}
