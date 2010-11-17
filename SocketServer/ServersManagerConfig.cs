// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Security.Cryptography.X509Certificates;

namespace SocketServers
{
	public class ServersManagerConfig
	{
		public ServersManagerConfig()
		{
			TcpMinAcceptBacklog = 1024;
			TcpMaxAcceptBacklog = 2048;
			TcpQueueSize = 8;
		}

		public int MinPort;
		public int MaxPort;
		public int UdpQueueSize;
		public int TcpMinAcceptBacklog;
		public int TcpMaxAcceptBacklog;
		public int TcpOffsetOffset;
		public int TcpQueueSize;
		public X509Certificate2 TlsCertificate;
	}
}
