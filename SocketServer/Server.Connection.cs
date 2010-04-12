// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	partial class Server
	{
		public class Connection
		{
			private static int connectionCount;

			public Connection(Socket socket)
			{
				Id = NewConnectionId();
				Socket = socket;
			}

			public int Id;
			public Socket Socket;

			private int NewConnectionId()
			{
				int connectionId;
				do
				{
					connectionId = Interlocked.Increment(ref connectionCount);
				} while (
					connectionId == ServerAsyncEventArgs.AnyNewConnectionId || 
					connectionId == ServerAsyncEventArgs.AnyConnectionId
					);

				return connectionId;
			}
		}
	}
}
