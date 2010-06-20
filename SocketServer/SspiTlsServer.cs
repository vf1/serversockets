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
	class SspiTlsServer
		: TcpServer
	{
		// http://www.codeproject.com/KB/IP/sslclasses.aspx

		public SspiTlsServer(ServersManagerConfig config)
			: base(config)
		{
			throw new NotImplementedException();
		}

		public override void Start()
		{
			// Open Certificate store. Choose what certificate to use. 
			// Call AcquireCredentialsHandle.
		}

		public override void SendAsync(ServerAsyncEventArgs e)
		{
			// When need to send data out, call EncryptMessage and send 
			// returned encrypted data blob to remote party.

			// AcceptSecurityContext or InitializeSecurityContext when connection does not exist ??
		}

		protected override void OnNewConnection(Connection connection)
		{
			// When you receive new connection from client, call AcceptSecurityContext. 
			// Send out data blob that is returned.
		}

		protected override bool OnReceived(ref ServerAsyncEventArgs e)
		{
			// Go into handshake loop with remote party by calling AcceptSecurityContext  
			// passing in received data blobs and sending out returned data blobs until 
			// success is returned.

			// base.SendAsync(e);
			// base.OnNewConnection(new Connection() { Id=e.ConnectionId, Socket=???, } );

			// -- OR --

			// When need to decrypt received data, call DecryptMessage and process decrypted
			// data blob
	
			return false;
		}

		// When done, call ApplyControlToken with SCHANNEL_SHUTDOWN. 
		// Call AcceptSecurityContext and send out returned data blob.
	}
}
