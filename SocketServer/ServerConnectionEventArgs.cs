// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;

namespace SocketServers
{
	public class ServerConnectionEventArgs
		: EventArgs
	{
		public ServerConnectionEventArgs(ServerEndPoint localEndPoint, IPEndPoint remoteEndPoint, int connectionId)
		{
			LocalEndPoint = localEndPoint;
			RemoteEndPoint = remoteEndPoint;
			ConnectionId = connectionId;
		}

		public ServerEndPoint LocalEndPoint { get; private set; }
		public IPEndPoint RemoteEndPoint { get; private set;}
		public int ConnectionId { get; private set; }
	}
}
