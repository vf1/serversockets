// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public enum ServerProtocol
	{
		Udp,
		Tcp,
		Tls,
	}

	public static class ServerProtocolHelper
	{
		public static bool TryConvertTo(this string protocolName, out ServerProtocol protocol)
		{
			if (string.Compare(protocolName, @"udp", true) == 0)
			{
				protocol = ServerProtocol.Udp;
				return true;
			}
			else if (string.Compare(protocolName, @"tcp", true) == 0)
			{
				protocol = ServerProtocol.Tcp;
				return true;
			}
			else if (string.Compare(protocolName, @"tls", true) == 0)
			{
				protocol = ServerProtocol.Tls;
				return true;
			}

			protocol = ServerProtocol.Udp;
			return false;
		}

		public static ServerProtocol ConvertTo(this string protocolName)
		{
			ServerProtocol protocol;
			if (TryConvertTo(protocolName, out protocol) == false)
				throw new ArgumentOutOfRangeException(@"protocolName");

			return protocol;
		}
	}

	public delegate void ServerEventHandlerVal<S, T>(S s, T e);
	public delegate void ServerEventHandlerVal<S, T1, T2>(S s, T1 t1, T2 t2);
	public delegate void ServerEventHandlerRef<S, T>(S s, ref T e);
	public delegate R ServerEventHandlerRef<S, T1, T2, R>(S s, T1 t1, ref T2 t2);
}
