// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public class ServerChangeEventArgs
		: EventArgs
	{
		public ServerChangeEventArgs(ServerEndPoint serverEndPoint)
		{
			ServerEndPoint = serverEndPoint;
		}

		public ServerEndPoint ServerEndPoint { get; private set; }
	}
}
