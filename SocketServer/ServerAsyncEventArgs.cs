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
		public const int AnyNewConnectionId = -1;
		public const int AnyConnectionId = -2;
		public const int DefaultSize = 4096;

		private bool isPooled;
		private SocketAsyncEventArgs socketArgs;
		private ArraySegment<byte> segment;
		private int emulatedBytesTransfred;

#if EVENTARGS_TRACING
		private List<string> tracing = new List<string>();
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
		}

#if I_HAVE_A_LOT_TIME_TO_INVESTOGNATE_THIS_ISSUE

		// 1. gc call finalizer several times in some cases w/o dummy static link
		// 2. sometimes strange internal .net error ocurs after resurrection
		// 3. This requre to use finalization queue for each object -> decrese performance?!
		// 4. Does not it work because of SocketAsyncEventArgs is pinned after async method?! - No, pinned struct wrapped by S...Args

		private static ServerAsyncEventArgs[] gcFix=new ServerAsyncEventArgs[1024];
		private static int gcFixCount = 0;

		~ServerAsyncEventArgs()
		{
			gcFix[System.Threading.Interlocked.Increment(ref gcFixCount) % gcFix.Length] = this;

			bool reRegisterForFinalize = !isPooled;

			Dispose();

			if (reRegisterForFinalize)
				GC.ReRegisterForFinalize(this);
		}
#endif

		public void Dispose()
		{
			if (isPooled)
			{
				BufferManager.Free(ref segment);
				socketArgs.Dispose();
			}
			else
			{
				EventArgsManager.Put(this);
			}
		}

		#region ILockFreePoolItem

		bool ILockFreePoolItem.IsPooled
		{
			set { Trace(); isPooled = value; }
		}

		public void SetDefaultValue()
		{
			Trace();

			ConnectionId = AnyNewConnectionId;
			Completed = null;
			emulatedBytesTransfred = 0;
			AcceptSocket = null;

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
				using (var file = System.IO.File.AppendText("lost.txt"))
					file.WriteLine(GetTracingPath());
			}
		}
#endif

		[Conditional("EVENTARGS_TRACING")]
		public void Trace()
		{
#if EVENTARGS_TRACING
			var stackTrace = new StackTrace(0);

			for (int i = 0; i < stackTrace.FrameCount; i++)
			{
				var method = stackTrace.GetFrame(i).GetMethod();

				if (method.DeclaringType != typeof(ServerAsyncEventArgs)/* && method.Module.Name == @"SocketServers.dll"*/)
				{
					if (tracing.Count == 0 || tracing[tracing.Count - 1] != method.Name)
						tracing.Add(method.Name);
					break;
				}
			}
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
				path += "->[" + item + "]";

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

		public void CopyAddressesFrom(BaseConnection c)
		{
			ConnectionId = c.Id;
			LocalEndPoint = c.LocalEndPoint;
			RemoteEndPoint = c.RemoteEndPoint;
		}

		#region SocketAsyncEventArgs

		public static implicit operator SocketAsyncEventArgs(ServerAsyncEventArgs serverArgs)
		{
			return serverArgs.socketArgs;
		}

		internal Socket AcceptSocket
		{
			get { Trace(); return socketArgs.AcceptSocket; }
			set { Trace(); socketArgs.AcceptSocket = value; }
		}

		public SocketError SocketError
		{
			get { Trace(); return socketArgs.SocketError; }
			internal set { Trace(); socketArgs.SocketError = value; }
		}

		public IPEndPoint RemoteEndPoint
		{
			get
			{
				Trace();
				return socketArgs.RemoteEndPoint as IPEndPoint;
			}
			set
			{
				Trace();
				if ((socketArgs.RemoteEndPoint as IPEndPoint).Equals(value) == false)
				{
					(socketArgs.RemoteEndPoint as IPEndPoint).Address = new IPAddress(value.Address.GetAddressBytes());
					(socketArgs.RemoteEndPoint as IPEndPoint).Port = value.Port;
				}
			}
		}

		public void SetAnyRemote(AddressFamily family)
		{
			Trace();
			if (family == AddressFamily.InterNetwork)
				RemoteEndPoint.Address = IPAddress.Any;
			else
				RemoteEndPoint.Address = IPAddress.IPv6Any;

			RemoteEndPoint.Port = 0;
		}

		public bool DisconnectReuseSocket
		{
			get { Trace(); return socketArgs.DisconnectReuseSocket; }
			set { Trace(); socketArgs.DisconnectReuseSocket = value; }
		}

		#endregion

		#region Buffer functions

		public int OffsetOffset
		{
			get { Trace(); return socketArgs.Offset - segment.Offset; }
		}

		public int Offset
		{
			get { Trace(); return socketArgs.Offset; }
		}

		public byte[] Buffer
		{
			get { Trace(); return socketArgs.Buffer; }
		}

		public int BufferCapacity
		{
			get { Trace(); return (segment.IsValid()) ? segment.Count : DefaultSize; }
		}

		public int Count
		{
			get { Trace(); return socketArgs.Count; }
		}

		public int BytesTransferred
		{
			get { Trace(); return socketArgs.BytesTransferred + emulatedBytesTransfred; }
		}

		public void SetBufferMax()
		{
			Trace();
			SetBuffer(0, BufferCapacity);
		}

		public void SetBufferMax(int offsetOffset)
		{
			Trace();
#if DEBUG
			if (offsetOffset >= BufferCapacity)
				throw new ArgumentOutOfRangeException(@"SetBuffer offsetOffset <= BufferCapacity");
#endif
			SetBuffer(offsetOffset, BufferCapacity - offsetOffset);
		}

		public void SetBuffer(int offsetOffset, int count)
		{
			Trace();
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
			Trace();

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
			Trace();
			if (offset < segment.Offset)
				throw new ArgumentOutOfRangeException(@"offset");

			socketArgs.SetBuffer(offset, socketArgs.Count - (offset - segment.Offset));
			emulatedBytesTransfred = bytesTransfred - socketArgs.BytesTransferred;
		}

		public void SetBufferTransferred(StreamBuffer buffer)
		{
			Trace();
			var newSegment = buffer.Detach();
			int bytesTransfred = buffer.Count;

			BufferManager.Free(ref segment);

			segment = newSegment;
			socketArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

			emulatedBytesTransfred = bytesTransfred - socketArgs.BytesTransferred;
		}

		internal void EmulateTransfer(ArraySegment<byte> newSegment, int offset, int bytesTransfred)
		{
			Trace();
			if (offset < segment.Offset)
				throw new ArgumentOutOfRangeException(@"offset");

			BufferManager.Free(ref segment);

			segment = newSegment;
			socketArgs.SetBuffer(segment.Array, offset, segment.Count - (offset - segment.Offset));

			emulatedBytesTransfred = bytesTransfred - socketArgs.BytesTransferred;
		}

		internal void FreeBuffer()
		{
			Trace();
			BufferManager.Free(ref segment);

			emulatedBytesTransfred = 0;

			socketArgs.SetBuffer(null, 0, 0);
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
