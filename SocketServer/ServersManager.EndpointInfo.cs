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
		class EndpointInfo
		{
			public readonly ProtocolPort ProtocolPort;
			public readonly UnicastIPAddressInformation AddressInformation;
			public readonly ServerEndPoint ServerEndPoint;

			public EndpointInfo(ProtocolPort protocolPort, UnicastIPAddressInformation addressInformation)
			{
				ProtocolPort = protocolPort;
				AddressInformation = addressInformation;
				ServerEndPoint = new ServerEndPoint(ProtocolPort, AddressInformation.Address);
			}
		}
	}
}