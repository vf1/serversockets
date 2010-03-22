using System;
using System.Net.Sockets;

namespace SocketServers
{
	public class ServerInfoEventArgs
		: ServerChangeEventArgs
	{
		public ServerInfoEventArgs(ServerEndPoint serverEndPoint, SocketError error)
			: base(serverEndPoint)
		{
			SocketError = error;
		}

		public ServerInfoEventArgs(ServerEndPoint serverEndPoint, Exception error)
			: base(serverEndPoint)
		{
			Exception = error;
		}

		public ServerInfoEventArgs(ServerEndPoint serverEndPoint, string error)
			: base(serverEndPoint)
		{
			Error = error;
		}

		public SocketError SocketError { get; private set; }
		public Exception Exception { get; private set; }
		public string Error { get; private set; }

		public override string ToString()
		{
			if (Error != null)
				return Error;

			if (Exception != null)
				return Exception.ToString();

			return @"SocketError :" + SocketError.ToString();
		}
	}
}
