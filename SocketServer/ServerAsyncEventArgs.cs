// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;

namespace SocketServers
{
	public class ServerAsyncEventArgs
		: EventArgs
		, IBuffersPoolItem
	{
		private SocketAsyncEventArgs socketArgs;

		internal delegate void CompletedEventHandler(Socket socket, ServerAsyncEventArgs e);

		public ServerAsyncEventArgs()
		{
			socketArgs = new SocketAsyncEventArgs()
			{
				RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0),
				UserToken = this,
			};

			socketArgs.Completed += SocketArgs_Completed;
		}

		void IBuffersPoolItem.Reset()
		{
			Completed = null;
		}

		public ServerEndPoint LocalEndPoint
		{
			get;
			set;
		}

		#region SocketAsyncEventArgs

		public static implicit operator SocketAsyncEventArgs(ServerAsyncEventArgs serverArgs)
		{
			return serverArgs.socketArgs;
		}

		public int BytesTransferred 
		{
			get { return socketArgs.BytesTransferred; }
		}

		public int BytesUsed
		{
			get { return socketArgs.Offset + socketArgs.BytesTransferred; }
		}

		public SocketError SocketError 
		{
			get { return socketArgs.SocketError; }
			internal set { socketArgs.SocketError = value; }
		}

		public IPEndPoint RemoteEndPoint
		{
			get
			{
				return socketArgs.RemoteEndPoint as IPEndPoint;
			}
			set
			{
				(socketArgs.RemoteEndPoint as IPEndPoint).Address = new IPAddress(value.Address.GetAddressBytes());
				(socketArgs.RemoteEndPoint as IPEndPoint).Port = value.Port;
			}
		}

		public void SetAnyRemote(AddressFamily family)
		{
			if (family == AddressFamily.InterNetwork)
				RemoteEndPoint.Address = IPAddress.Any;
			else
				RemoteEndPoint.Address = IPAddress.IPv6Any;
			
			RemoteEndPoint.Port = 0;
		}

		public byte[] Buffer
		{
			get { return socketArgs.Buffer; } 
		}

		#endregion

		#region Buffer functions

		public void SetBuffer()
		{
			SetBuffer((socketArgs.Buffer != null) ? socketArgs.Buffer.Length : defaultSize);
		}

		public void SetBuffer(int length)
		{
			if (socketArgs.Buffer != null && length <= socketArgs.Buffer.Length)
				socketArgs.SetBuffer(0, length);
			else
				socketArgs.SetBuffer(NewBuffer(length), 0, length);
		}

		public void ContinueBuffer(int length)
		{
			int newOffset = socketArgs.Offset + socketArgs.BytesTransferred;

			if (length <= socketArgs.Buffer.Length)
				socketArgs.SetBuffer(newOffset, length - newOffset);
			else
			{
				byte[] newBuffer = NewBuffer(length);
				Array.Copy(socketArgs.Buffer, newBuffer, socketArgs.Buffer.Length);

				socketArgs.SetBuffer(newBuffer, newOffset, length - newOffset);
			}
		}

		public bool ContinueBuffer()
		{
			if (socketArgs.BytesTransferred < socketArgs.Count)
			{
				socketArgs.SetBuffer(socketArgs.Offset + socketArgs.BytesTransferred, socketArgs.Count - socketArgs.BytesTransferred);
				return true;
			}

			return false;
		}

		private byte[] NewBuffer(int length)
		{
			return new byte[GetBufferSize(length)];
		}


		private static int defaultSize = 2048;

		public int GetBufferSize(int requredSize)
		{
			if (defaultSize < requredSize)
				defaultSize = requredSize + 1024 - requredSize % 1024;

			return defaultSize;
		}

		#endregion

		#region Completed

		internal CompletedEventHandler Completed;

		internal void OnCompleted(Socket socket)
		{
			if (Completed != null)
				Completed(socket, this);
		}

		private static void SocketArgs_Completed(object sender, SocketAsyncEventArgs e)
		{
			var serverArgs = e.UserToken as ServerAsyncEventArgs;
			serverArgs.Completed(sender as Socket, serverArgs);
		}

		#endregion
	}
}
