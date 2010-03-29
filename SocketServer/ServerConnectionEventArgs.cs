// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public class ServerConnectionEventArgs
		: EventArgs
	{
		public ServerConnectionEventArgs(ServerEndPoint serverEndPoint, int connectionId)
		{
			ServerEndPoint = serverEndPoint;
			ConnectionId = connectionId;
		}

		public ServerEndPoint ServerEndPoint { get; private set; }
		public int ConnectionId { get; private set; }
	}
}
