// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;

namespace SocketServers
{
	public class BaseConnection
		: IDisposable
	{
		private StreamBuffer buffer;

		public void Dispose()
		{
			if (buffer != null)
				buffer.Dispose();
		}

		public ServerEndPoint LocalEndPoint { get; internal set; }
		public IPEndPoint RemoteEndPoint { get; internal set; }
		public int Id { get; internal set; }

		public StreamBuffer Buffer
		{
			get
			{
				if (buffer == null)
					buffer = new StreamBuffer();
				return buffer;
			}
		}
	}
}
