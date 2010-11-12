// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Security.Cryptography.X509Certificates;

namespace SocketServers
{
	public struct ServersManagerConfig
	{
		public int MinPort;
		public int MaxPort;
		public int UdpQueueSize;
		public int TcpOffsetOffset;
		public int TcpQueueSize;
		public int TcpAcceptQueueSize;
		public X509Certificate2 TlsCertificate;
		public int RequseSocketPoolSizePerServer;
		public bool ReuseSocketForConnect;
	}
}
