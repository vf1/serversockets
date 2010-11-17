// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;

namespace SocketServers
{
	static class SocketEx
	{
		public static void SafeShutdownClose(this Socket socket)
		{
			try
			{
				try
				{
					socket.Shutdown(SocketShutdown.Both);
				}
				catch { }

				socket.Close();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public static void SendAsync(this Socket socket, ServerAsyncEventArgs e, ServerAsyncEventArgs.CompletedEventHandler handler)
		{
			e.Completed = handler;
			if (socket.SendAsync(e) == false)
				e.OnCompleted(socket);
		}

		public static void ConnectAsync(this Socket socket, ServerAsyncEventArgs e, ServerAsyncEventArgs.CompletedEventHandler handler)
		{
			e.Completed = handler;
			if (socket.ConnectAsync(e) == false)
				e.OnCompleted(socket);
		}

		public static void AcceptAsync(this Socket socket, ServerAsyncEventArgs e, ServerAsyncEventArgs.CompletedEventHandler handler)
		{
			e.Completed = handler;
			if (socket.AcceptAsync(e) == false)
				e.OnCompleted(socket);
		}
	}
}
