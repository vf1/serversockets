// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32.Ssp;
using System.Security.Cryptography.X509Certificates;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SocketServers
{
	class SspiTlsServer
		: BaseTcpServer
	{
		// http://www.codeproject.com/KB/IP/sslclasses.aspx

		private X509Certificate certificate;
		private SafeCredHandle credential;
		private int maxTokenSize;

		public SspiTlsServer(ServersManagerConfig config)
			: base(config)
		{
			certificate = config.TlsCertificate;
		}

		public override void Start()
		{
			long expiry;
			Sspi.AcquireCredentialsHandle(
				CredentialUse.SECPKG_CRED_INBOUND,
				new SchannelCred(certificate, SchProtocols.TlsServer),
				out credential,
				out expiry);

			GetMaxTokenSize();

			base.Start();
		}

		public override void Dispose()
		{
			// When done, call ApplyControlToken with SCHANNEL_SHUTDOWN. 
			// Call AcceptSecurityContext and send out returned data blob.

			credential.Dispose();
			base.Dispose();
		}

		public override unsafe void SendAsync(ServerAsyncEventArgs e)
		{
			try
			{
				var connection = GetTcpConnection(e.RemoteEndPoint);
				var context = connection.SspiContext;
				var sizes = context.StreamSizes;

				if (connection == null)
				{
					e.Completed = Send_Completed;
					e.SocketError = SocketError.NotConnected;
					e.OnCompleted(null);
					return;
				}

				var dataCount = e.Count;

				if (e.OffsetOffset >= sizes.cbHeader)
					e.ResizeBufferCount(e.Offset - sizes.cbHeader,
						sizes.cbHeader + dataCount + sizes.cbTrailer);
				else
					throw new NotImplementedException("Ineffective way not implemented. Need to move buffer for SECBUFFER_STREAM_HEADER.");

				var message = new SecBufferDescEx(
					new SecBufferEx[]
						{ 
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_STREAM_HEADER, Buffer = e.Buffer, Size = sizes.cbHeader, Offset = e.Offset, },
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_DATA, Buffer = e.Buffer, Size = dataCount, Offset = e.Offset + sizes.cbHeader, },
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_STREAM_TRAILER, Buffer = e.Buffer, Size = sizes.cbTrailer, Offset = e.Offset + sizes.cbHeader + dataCount, },
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_EMPTY, },
						});

				Sspi.EncryptMessage(
					ref context.Handle,
					ref message,
					0,
					null);

				e.ResizeBufferCount(e.Offset, 
					message.Buffers[0].Size + message.Buffers[1].Size + message.Buffers[2].Size);

				base.SendAsync(e);
			}
			catch (SspiException ex)
			{
				e.SocketError = SocketError.Fault;
				OnFailed(new ServerInfoEventArgs(realEndPoint, ex));
			}
		}

		protected override void OnNewTcpConnection(Connection connection)
		{
			connection.SspiContext.Connected = false;
		}

		protected override void OnEndTcpConnection(int connectionId)
		{
			OnEndConnection(connectionId);
		}

		protected override bool OnTcpReceived(Connection connection, ref ServerAsyncEventArgs e)
		{
			bool result;
			bool oldConnected = connection.SspiContext.Connected;

			if (connection.SspiContext.Connected)
				result = DecryptData(ref e, connection);
			else
				result = Handshake(e, connection);

			// proccess all data from buffer if TLS connected status was changed
			//
			while (result && oldConnected != connection.SspiContext.Connected &&
				connection.SspiContext.Buffer.IsValid())
			{
				oldConnected = connection.SspiContext.Connected;

				ServerAsyncEventArgs nulle = null;
				if (connection.SspiContext.Connected)
					result = DecryptData(ref nulle, connection);
				else
					result = Handshake(nulle, connection);
			}

			return result;
		}

		unsafe private bool DecryptData(ref ServerAsyncEventArgs e, Connection connection)
		{
			var context = connection.SspiContext;
			var message = context.SecBufferDesc5;

			if (context.Buffer.IsValid() && e != null)
				context.CopyToBuffer(e, 0);

			for (; ; )
			{
				// prepare buffer
				//

				message.Buffers[0].BufferType = BufferType.SECBUFFER_DATA;
				message.Buffers[1].BufferType = BufferType.SECBUFFER_EMPTY;
				message.Buffers[2].BufferType = BufferType.SECBUFFER_EMPTY;
				message.Buffers[3].BufferType = BufferType.SECBUFFER_EMPTY;
				message.Buffers[4].BufferType = BufferType.SECBUFFER_EMPTY;

				if (context.Buffer.IsValid())
					SetSecBuffer(context, ref message.Buffers[0]);
				else
					SetSecBuffer(e, ref message.Buffers[0]);
				

				// call SSPI
				//
				var result = Sspi.SafeDecryptMessage(ref context.Handle, ref message, 0, null);


				// analize result
				//
				int extraIndex = message.GetBufferIndex(BufferType.SECBUFFER_EXTRA, 0);
				int dataIndex = message.GetBufferIndex(BufferType.SECBUFFER_DATA, 0);

				switch (result)
				{
					case SecurityStatus.SEC_E_OK:

						if (dataIndex >= 0)
						{
							if (context.Buffer.IsInvalid())
							{
								if (extraIndex >= 0)
									context.CopyToBuffer(message.Buffers[extraIndex]);

								e.ResizeBufferTransfered(message.Buffers[dataIndex].Offset,
									message.Buffers[dataIndex].Size);

								if (OnReceived(ref e) == false)
									return false;
							}
							else
							{
								var buffer = context.DetachBuffer();

								if (extraIndex >= 0)
									context.CopyToBuffer(message.Buffers[extraIndex]);


								// create eventarg and call onreceived event
								//
								var e2 = buffersPool.Get();

								base.PrepareEventArgs(connection, e2);

								e2.EmulateTransfer(buffer, message.Buffers[dataIndex].Offset,
									message.Buffers[dataIndex].Size);

								bool continue1 = OnReceived(ref e2);

								if (e2 != null)
									buffersPool.Put(e2);

								if (continue1 == false)
									return false;
							}

							if (extraIndex >= 0)
								continue;

							return true;
						}

						return false;


					case SecurityStatus.SEC_E_INCOMPLETE_MESSAGE:

						if (context.Buffer.IsInvalid())
							context.CopyToBuffer(e, 0);

						return true;


					case SecurityStatus.SEC_I_RENEGOTIATE:

						// MSDN: Renegotiation is not supported for Schannel kernel mode. The
						// caller should either ignore this return value or shut down the
						// connection.
						// If the value is ignored, either the client or the server might shut
						// down the connection as a result.

						return false;


					default:
						return false;
				}
			}
		}

		private unsafe bool Handshake(ServerAsyncEventArgs ie, Connection connection)
		{
			int contextAttr = 0;
			ServerAsyncEventArgs oe = null;
			SspiContext context = connection.SspiContext;

			try
			{
				if (context.Buffer.IsValid() && ie != null)
					context.CopyToBuffer(ie, 0);

				for (; ; )
				{
					// prepare input buffer
					//
					var input = new SecBufferDescEx(
						new SecBufferEx[]
						{
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_TOKEN, },
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_EMPTY, },
						});

					if (context.Buffer.IsValid())
						SetSecBuffer(context, ref input.Buffers[0]);
					else
						SetSecBuffer(ie, ref input.Buffers[0]);


					// prepare output buffer
					//
					if (oe == null)
						oe = buffersPool.Get();
					oe.SetBufferMax();

					var output = new SecBufferDescEx(
						new SecBufferEx[]
						{
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_TOKEN, Size = oe.BufferCapacity, Buffer = oe.Buffer, Offset = oe.Offset, },
							new SecBufferEx() { BufferType = BufferType.SECBUFFER_EMPTY, },
						});


					// prepare some args and call SSPI
					//
					int contextReq = (int)(ContextReq.ASC_REQ_SEQUENCE_DETECT |
											ContextReq.ASC_REQ_REPLAY_DETECT |
											ContextReq.ASC_REQ_CONFIDENTIALITY |
											ContextReq.ASC_REQ_EXTENDED_ERROR |
											ContextReq.ASC_REQ_STREAM);

					var newHandle = (context.Handle.IsInvalid) ? new SafeCtxtHandle() : context.Handle;

					long timeStamp;

					var result = Sspi.SafeAcceptSecurityContext(
						ref credential,
						ref context.Handle,
						ref input,
						contextReq,
						TargetDataRep.SECURITY_NATIVE_DREP,
						ref newHandle,
						ref output,
						out contextAttr,
						out timeStamp);

					if (context.Handle.IsInvalid)
						context.Handle = newHandle;


					// proccess non-critical errors
					//
					switch (result)
					{
						case SecurityStatus.SEC_E_INCOMPLETE_MESSAGE:

							if (context.Buffer.IsInvalid())
								context.CopyToBuffer(ie, 0);
							return true;


						case SecurityStatus.SEC_E_BUFFER_TOO_SMALL:

							if (oe.Count < maxTokenSize)
							{
								oe.SetBuffer(0, maxTokenSize);
								continue;
							}
							return false;
					}
		

					// send response to client
					//
					if (result == SecurityStatus.SEC_I_CONTINUE_NEEDED || result == SecurityStatus.SEC_E_OK ||
						(Sspi.Failed(result) && (contextAttr & (int)ContextAttr.ASC_RET_EXTENDED_ERROR) != 0))
					{
						if (output.Buffers[0].Size > 0)
						{
							oe.SetBuffer(0, output.Buffers[0].Size);
							oe.CopyAddressesFrom(ie);

							base.SendAsync(oe);
							oe = null;
						}
					}


					// move extra data to buffer
					//
					int extraIndex = input.GetBufferIndex(BufferType.SECBUFFER_EXTRA, 0);

					if (extraIndex < 0)
					{
						BufferManager.Free(ref context.Buffer);
					}
					else
					{
						if (context.Buffer.IsInvalid())
						{
							context.CopyToBuffer(ie,
							   ie.BytesTransferred - input.Buffers[extraIndex].Size);
						}
						else
						{
							Buffer.BlockCopy(context.Buffer.Array,
								context.Buffer.Offset + context.BufferCount - input.Buffers[extraIndex].Size,
								context.Buffer.Array, context.Buffer.Offset, input.Buffers[extraIndex].Size);

							context.BufferCount = input.Buffers[extraIndex].Size;
						}
					}


					// proccess error-codes
					//
					switch (result)
					{
						case SecurityStatus.SEC_E_OK:

							if (Sspi.SafeQueryContextAttributes(ref context.Handle, out context.StreamSizes)
								!= SecurityStatus.SEC_E_OK)
								return false;

							context.Connected = true;
							OnNewConnection(connection);

							return true;


						case SecurityStatus.SEC_I_CONTINUE_NEEDED:

							if (extraIndex >= 0)
								continue;
							
							return true;


						default:
							return false;
					}
				}
			}
			finally
			{
				if (oe != null)
					buffersPool.Put(ref oe);
			}
		}

		private void SetSecBuffer(ServerAsyncEventArgs e, ref SecBufferEx secBuffer)
		{
			secBuffer.Buffer = e.Buffer;
			secBuffer.Offset = e.Offset;
			secBuffer.Size = e.BytesTransferred;
		}

		public void SetSecBuffer(SspiContext context, ref SecBufferEx secBuffer)
		{
			secBuffer.Buffer = context.Buffer.Array;
			secBuffer.Offset = context.Buffer.Offset;
			secBuffer.Size = context.BufferCount;
		}

		private void GetMaxTokenSize()
		{
			int count;
			SafeContextBufferHandle secPkgInfos;
			if (Sspi.EnumerateSecurityPackages(out count, out secPkgInfos) != SecurityStatus.SEC_E_OK)
				throw new Win32Exception("Failed to EnumerateSecurityPackages");

			for (int i = 0; i < count; i++)
			{
				var item = secPkgInfos.GetItem<SecPkgInfo>(i);
				if (string.Compare(item.GetName(), @"Schannel", true) == 0)
				{
					maxTokenSize = item.cbMaxToken;
					break;
				}
			}

			if (maxTokenSize == 0)
				throw new Exception("Failed to retrive cbMaxToken for Schannel");
		}
	}
}
