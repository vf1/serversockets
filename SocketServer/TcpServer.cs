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
	class TcpServer<C>
		: BaseTcpServer<C>
		where C : BaseConnection, IDisposable, new()
	{
		public TcpServer(ServersManagerConfig config)
			: base(config)
		{
		}

		protected override void OnNewTcpConnection(Connection<C> connection)
		{
			OnNewConnection(connection);
		}

		protected override void OnEndTcpConnection(Connection<C> connection)
		{
			OnEndConnection(connection);
		}

		protected override bool OnTcpReceived(Connection<C> connection, ref ServerAsyncEventArgs e)
		{
			return OnReceived(connection, ref e);
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			var connection = GetTcpConnection(e.RemoteEndPoint);

			OnBeforeSend(connection, e);

			base.SendAsync(connection, e);
		}
	}
}
