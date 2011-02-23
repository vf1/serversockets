// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;

namespace SocketServers
{
	public struct ProtocolPort
	{
		public ProtocolPort(ServerProtocol protocol, int port)
		{
			Protocol = protocol;
			Port = port;
		}

		public int Port;
		public ServerProtocol Protocol;
	}
}
