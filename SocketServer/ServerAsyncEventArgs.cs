// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;

namespace SocketServers
{
	public class ServerAsyncEventArgs
		: EventArgs
		, ILockFreePoolItem
		, IDisposable
	{
		public const int DefaultUserToken1 = -1;
		public const object DefaultUserToken2 = null;
		public const int AnyNewConnectionId = -1;
		public const int AnyConnectionId = -2;
		public const int DefaultSize = 4096;

		private bool isPooled;
		private SocketAsyncEventArgs socketArgs;
		private ArraySegment<byte> segment;
		private int emulatedBytesTransfred;

#if EVENTARGS_TRACING
		private List<string> tracing;
#endif

		internal delegate void CompletedEventHandler(Socket socket, ServerAsyncEventArgs e);

		public ServerAsyncEventArgs()
		{
			socketArgs = new SocketAsyncEventArgs()
			{
				RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0),
				UserToken = this,
			};

			socketArgs.Completed += SocketArgs_Completed;

			SetDefaultValue();

#if EVENTARGS_TRACING
			tracing = new List<string>();
#endif
		}

#if DISABLED_FOR_OPTIMIZATION
		~ServerAsyncEventArgs()
		{
		    if (isPooled)
		    {
		        BufferManager.Free(ref segment);
		    }
		    else
		    {
		        GC.ReRegisterForFinalize(this);
		        EventArgsManager.Put(this);
		    }
		}
#endif

		public void Dispose()
		{
			if (isPooled)
			{
				BufferManager.Free(ref segment);
			}
			else
			{
				EventArgsManager.Put(this);
			}
		}

		#region ILockFreePoolItem

		bool ILockFreePoolItem.IsPooled
		{
			set { isPooled = value; }
		}

		public void SetDefaultValue()
		{
			UserToken1 = DefaultUserToken1;
			UserToken2 = DefaultUserToken2;
			ConnectionId = AnyNewConnectionId;
			Completed = null;
			emulatedBytesTransfred = 0;

			if (segment.Array != null && segment.Count != DefaultSize)
				BufferManager.Free(ref segment);
		}

		#endregion

		#region Tracing

#if EVENTARGS_TRACING
        ~ServerAsyncEventArgs()
        {
            if (isPooled == false)
            {
                Console.WriteLine("Lost ServerAsyncEventArgs: {0}", GetTracingPath());
            }
        }
#endif

		[Conditional("EVENTARGS_TRACING")]
		public void Trace(string place)
		{
#if EVENTARGS_TRACING
			tracing.Add(place);
#endif
		}

		[Conditional("EVENTARGS_TRACING")]
		public void Trace()
		{
#if EVENTARGS_TRACING
			var stackTrace = new StackTrace();

			//stackTrace.GetFrame(1).GetMethod().DeclaringType.Name + @":" 
			tracing.Add(stackTrace.GetFrame(1).GetMethod().Name);
#endif
		}

		[Conditional("EVENTARGS_TRACING")]
		public void ResetTracing()
		{
#if EVENTARGS_TRACING
			tracing.Clear();
#endif
		}

		public string GetTracingPath()
		{
#if EVENTARGS_TRACING
			string path = "";

			foreach (var item in tracing)
				path += "[" + item + "]->";

			return path;
#else
			return @"NO TRACING";
#endif
		}

		#endregion

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

		public object UserToken2
		{
			get;
			set;
		}

		public int ConnectionId
		{
			get;
			set;
		}

		internal int SequenceNumber;

		public void CopyAddressesFrom(ServerAsyncEventArgs e)
		{
			ConnectionId = e.ConnectionId;
			LocalEndPoint = e.LocalEndPoint;
			RemoteEndPoint = e.RemoteEndPoint;
		}

		public void TransferData(byte[] buffer, int offset, int size)
		{
			System.Buffer.BlockCopy(buffer, offset, Buffer, Offset, size);
			socketArgs.SetBuffer(Offset + size, Count - size);
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
				if ((socketArgs.RemoteEndPoint as IPEndPoint).Equals(value) == false)
				{
					(socketArgs.RemoteEndPoint as IPEndPoint).Address = new IPAddress(value.Address.GetAddressBytes());
					(socketArgs.RemoteEndPoint as IPEndPoint).Port = value.Port;
				}
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

		public bool DisconnectReuseSocket
		{
			get { return socketArgs.DisconnectReuseSocket; }
			set { socketArgs.DisconnectReuseSocket = value; }
		}

		#endregion

		#region Buffer functions

		public int OffsetOffset
		{
			get { return socketArgs.Offset - segment.Offset; }
		}

		public int Offset
		{
			get { return socketArgs.Offset; }
		}

		public byte[] Buffer
		{
			get { return socketArgs.Buffer; }
		}

		public int BufferCapacity
		{
			get { return (segment.IsValid()) ? segment.Count : DefaultSize; }
		}

		public int Count
		{
			get { return socketArgs.Count; }
		}

		public int BytesTransferred
		{
			get { return socketArgs.BytesTransferred + emulatedBytesTransfred; }
		}

		public void SetBufferMax()
		{
			SetBuffer(0, BufferCapacity);
		}

		public void SetBufferMax(int offsetOffset)
		{
#if DEBUG
			if (offsetOffset >= BufferCapacity)
				throw new ArgumentOutOfRangeException(@"SetBuffer offsetOffset <= BufferCapacity");
#endif
			SetBuffer(offsetOffset, BufferCapacity - offsetOffset);
		}

		public void SetBuffer(int offsetOffset, int count)
		{
#if DEBUG
			if (count <= 0)
				throw new ArgumentOutOfRangeException(@"SetBuffer count <= 0");
#endif

			emulatedBytesTransfred = 0;

			if (socketArgs.Buffer != null && (offsetOffset + count) <= segment.Count)
				socketArgs.SetBuffer(segment.Offset + offsetOffset, count);
			else
			{
				BufferManager.Free(ref segment);
				segment = BufferManager.Allocate(offsetOffset + count);

				socketArgs.SetBuffer(segment.Array, segment.Offset + offsetOffset, count);
			}
		}

		public void ResizeBufferCount(int offset, int count)
		{
			if (offset < segment.Offset)
				throw new ArgumentOutOfRangeException(@"offset");

			int offsetOffset = offset - segment.Offset;

			if ((offsetOffset + count) > segment.Count)
			{
				var segment2 = BufferManager.Allocate(offsetOffset + count);

				System.Buffer.BlockCopy(segment.Array, 0, segment2.Array, 0, segment.Count);

				BufferManager.Free(ref segment);
				segment = segment2;

				socketArgs.SetBuffer(segment.Array, segment.Offset + offsetOffset, count);
			}
			else
			{
				socketArgs.SetBuffer(segment.Offset + offsetOffset, count);
			}
		}

		public void ResizeBufferTransfered(int offset, int bytesTransfred)
		{
			if (offset < segment.Offset)
				throw new ArgumentOutOfRangeException(@"offset");

			socketArgs.SetBuffer(offset, socketArgs.Count);
			emulatedBytesTransfred = bytesTransfred - socketArgs.BytesTransferred;
		}

		public void EmulateTransfer(ArraySegment<byte> newSegment, int offset, int bytesTransfred)
		{
			BufferManager.Free(ref segment);

			segment = newSegment;
			socketArgs.SetBuffer(segment.Array, offset, segment.Count + offset - segment.Offset);

			emulatedBytesTransfred = bytesTransfred - socketArgs.BytesTransferred;
		}

		public void FreeBuffer()
		{
			BufferManager.Free(ref segment);
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
