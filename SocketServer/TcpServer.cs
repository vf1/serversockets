// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;

namespace SocketServers
{
	class TcpServer
		: BaseTcpServer
	{
		public TcpServer(ServersManagerConfig config)
			: base(config)
		{
		}

		protected override void OnNewTcpConnection(Connection connection)
		{
			OnNewConnection(connection);
		}

		protected override void OnEndTcpConnection(Connection connection)
		{
			OnEndConnection(connection.Id);
		}

		protected override bool OnTcpReceived(Connection connection, ref ServerAsyncEventArgs e)
		{
			return OnReceived(ref e);
		}
	}
}
