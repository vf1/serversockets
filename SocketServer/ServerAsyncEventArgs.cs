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
		public const int DefaultSize = 2048;

		private bool isPooled;

		private int count;
		private int offsetOffset;
		private int bytesTransferred;
		private ArraySegment<byte> segment;
		private SocketAsyncEventArgs socketArgs;

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

			// Use GC.KeepAlive(this); instead gcFix.., not tested, but it is good idea.
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
			AcceptSocket = null;

			if (segment.Array != null && segment.Count != DefaultSize)
				BufferManager.Free(ref segment);

			count = DefaultSize;
			offsetOffset = 0;
			bytesTransferred = 0;

			UserTokenForSending = 0;
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

		public int UserTokenForSending
		{
			get;
			set;
		}

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

		public ServerAsyncEventArgs DeepCopy()
		{
			var e2 = EventArgsManager.Get();

			e2.CopyAddressesFrom(this);

			e2.offsetOffset = offsetOffset;
			e2.count = count;
			e2.AllocateBuffer();

			e2.bytesTransferred = bytesTransferred;
			e2.UserTokenForSending = UserTokenForSending;

			System.Buffer.BlockCopy(Buffer, Offset, e2.Buffer, e2.Offset, e2.Count);

			return e2;
		}

		#region SocketAsyncEventArgs

		public static implicit operator SocketAsyncEventArgs(ServerAsyncEventArgs serverArgs)
		{
			if (serverArgs.Count > 0)
			{
				serverArgs.AllocateBuffer();
				serverArgs.ValidateBufferSettings();
				serverArgs.socketArgs.SetBuffer(serverArgs.Buffer, serverArgs.Offset, serverArgs.Count);
			}
			else
			{
				serverArgs.socketArgs.SetBuffer(null, -1, -1);
			}


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

		#region Buffer relayted

		public int OffsetOffset
		{
			get { Trace(); return offsetOffset; }
			set
			{
				Trace();
#if DEBUG
				if (value < 0)
					throw new ArgumentOutOfRangeException(@"OffsetOffset can not be negative number");
#endif
				offsetOffset = value;
			}
		}

		public int Offset
		{
			get { Trace(); return segment.Offset + offsetOffset; }
			set
			{
				Trace();
#if DEBUG
				if (segment.IsInvalid())
					throw new ArgumentOutOfRangeException(@"Call AllocateBuffer() before change Offset value");
				if (Offset < segment.Offset)
					throw new ArgumentOutOfRangeException(@"Offset is below than segment.Offset value");
#endif
				offsetOffset = value - segment.Offset;
			}
		}

		public int Count
		{
			get { Trace(); return count; }
			set
			{
				Trace();
#if DEBUG
				if (value < 0)
					throw new ArgumentOutOfRangeException(@"Count can not be negative number");
				if (value < offsetOffset)
					throw new ArgumentOutOfRangeException(@"Count can not be less than OffsetOffset");
#endif
				count = value;
			}
		}

		public int BytesTransferred
		{
			get { Trace(); return bytesTransferred; }
			set
			{
				Trace();
#if DEBUG
				if (value < 0)
					throw new ArgumentOutOfRangeException(@"BytesTransferred can not be negative number");
#endif
				bytesTransferred = value;
			}
		}

		public ArraySegment<byte> ArraySegment
		{
			get { return segment; }
			set
			{
				BufferManager.Free(segment);
				segment = value;
			}
		}

		public byte[] Buffer
		{
			get
			{
				Trace();

				if (offsetOffset + count > segment.Count)
					ReAllocateBuffer(false);

				return segment.Array;
			}
		}

		public void AllocateBuffer()
		{
			Trace();

			ReAllocateBuffer(false);
		}

		public void ReAllocateBuffer(bool keepData)
		{
			Trace();

			if (offsetOffset + count > segment.Count)
			{
				var newSegment = BufferManager.Allocate(offsetOffset + count);

				if (keepData && segment.IsValid())
					System.Buffer.BlockCopy(segment.Array, segment.Offset, newSegment.Array, newSegment.Offset, segment.Count);

				ArraySegment = newSegment;
			}
		}

		public void FreeBuffer()
		{
			Trace();

			BufferManager.Free(ref segment);
			Count = 0;
		}


		[Conditional("DEBUG")]
		internal void ValidateBufferSettings()
		{
			if (Offset < segment.Offset)
				throw new ArgumentOutOfRangeException(@"Offset is below than segment.Offset value");

			if (OffsetOffset >= segment.Count)
				throw new ArgumentOutOfRangeException(@"OffsetOffset is bigger than segment.Count");

			if (BytesTransferred >= segment.Count)
				throw new ArgumentOutOfRangeException(@"BytesTransferred is bigger than segment.Count");

			if (OffsetOffset + Count > segment.Count)
				throw new ArgumentOutOfRangeException(@"Invalid buffer settings: OffsetOffset + Count is bigger than segment.Count");
		}

		public void AttachBuffer(StreamBuffer buffer)
		{
			Trace();

			OffsetOffset = 0;
			BytesTransferred = buffer.BytesTransferred;

			ArraySegment = buffer.Detach();

			Count = segment.Count;
		}

		public ArraySegment<byte> DetachBuffer()
		{
			var result = segment;
			segment = new ArraySegment<byte>();

			count = DefaultSize;
			offsetOffset = 0;
			bytesTransferred = 0;

			return result;
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

			serverArgs.bytesTransferred = e.BytesTransferred;

			serverArgs.Completed(sender as Socket, serverArgs);
		}

		#endregion
	}
}
