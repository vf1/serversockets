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
		public const int DefaultUserToken1 = -1;

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

			UserToken1 = DefaultUserToken1;
		}

		void IBuffersPoolItem.Reset()
		{
			UserToken1 = DefaultUserToken1;
			Completed = null;
		}

		public ServerEndPoint LocalEndPoint
		{
			get;
			set;
		}

		public int UserToken1
		{
			get;
			set;
		}

		public int ConnectionId
		{
			get;
			internal set;
		}

		#region SocketAsyncEventArgs

		public static implicit operator SocketAsyncEventArgs(ServerAsyncEventArgs serverArgs)
		{
			return serverArgs.socketArgs;
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

		#endregion

		#region Buffer functions

		/// <summary>
		/// Gets the data buffer to use with an asynchronous socket method.
		/// </summary>
		public byte[] Buffer
		{
			get { return socketArgs.Buffer; }
		}

		public int BufferCapacity
		{
			get { return (socketArgs.Buffer != null) ? socketArgs.Buffer.Length : defaultSize; }
		}

		/// <summary>
		/// Gets the offset, in bytes, into the data buffer referenced by the Buffer property.
		/// </summary>
		public int Offset
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the number of total bytes transferred in a continue socket operations.
		/// </summary>
		public int BytesTransferred
		{
			get { return socketArgs.Offset - Offset + socketArgs.BytesTransferred; }
		}

		/// <summary>
		/// Sets the data buffer to use with an asynchronous socket method.
		/// </summary>
		public void SetMaxBuffer()
		{
			SetBuffer(0, BufferCapacity);
		}

		/// <summary>
		/// == SetBuffer(Offset, BytesTransferred)
		/// </summary>
		/// <param name="length"></param>
		public void SetBuffer()
		{
			SetBuffer(Offset, BytesTransferred);
		}

		/// <summary>
		/// == SetBuffer(e.Offset, length)
		/// </summary>
		/// <param name="length"></param>
		public void SetBuffer(int length)
		{
			SetBuffer(Offset, length);
		}

		/// <summary>
		/// Sets the data buffer to use with an asynchronous socket method.
		/// </summary>
		/// <param name="offset"></param>
		/// <param name="length"></param>
		public void SetBuffer(int offset, int length)
		{
			Offset = offset;

			if (socketArgs.Buffer != null && (offset + length) <= socketArgs.Buffer.Length)
				socketArgs.SetBuffer(offset, length);
			else
				socketArgs.SetBuffer(NewBuffer(offset + length), offset, length);
		}

		/// <summary>
		/// Extend buffer size for socket operations.
		/// </summary>
		/// <param name="newLength"></param>
		public void ContinueBuffer(int newLength)
		{
			int newBufferLength = Offset + newLength;

			if (newBufferLength <= socketArgs.Buffer.Length)
				socketArgs.SetBuffer(ContinueOffset, newLength - BytesTransferred);
			else
			{
				byte[] newBuffer = NewBuffer(newBufferLength);
				Array.Copy(socketArgs.Buffer, newBuffer, socketArgs.Buffer.Length);

				socketArgs.SetBuffer(newBuffer, ContinueOffset, newLength - BytesTransferred);
			}
		}

		/// <summary>
		/// Prepare buffer for next operation if not all expected data was receieved.
		/// </summary>
		/// <returns>False when all data received</returns>
		public bool ContinueBuffer()
		{
			if (socketArgs.BytesTransferred < socketArgs.Count)
			{
				socketArgs.SetBuffer(ContinueOffset, socketArgs.Count - socketArgs.BytesTransferred);
				return true;
			}

			return false;
		}

		private int ContinueOffset
		{
			get { return socketArgs.Offset + socketArgs.BytesTransferred; }
		}

		private byte[] NewBuffer(int length)
		{
			return new byte[GetBufferSize(length)];
		}


		internal static int defaultSize = 4096;

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
