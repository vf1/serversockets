// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public struct ServersManagerConfig
	{
		public int MinPort;
		public int MaxPort;
		public int TcpInitialBufferSize;
		public int TcpInitialOffset;
		public int UdpQueueSize;
	}
}
