// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

namespace SocketServers
{
	public enum ServerIpProtocol
	{
		Udp,
		Tcp,
		Tls,
	}

	public delegate void ServerEventHandlerVal<S, T>(S s, T e);
	public delegate void ServerEventHandlerVal<S, T1, T2>(S s, T1 t1, T2 t2);
	public delegate void ServerEventHandlerRef<S, T>(S s, ref T e);
	//public delegate R ServerEventHandlerVal<S, T, R>(S s, T e);
	public delegate R ServerEventHandlerRef<S, T1, T2, R>(S s, T1 t1, ref T2 t2);
}
