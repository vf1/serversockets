// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using Microsoft.Win32.Ssp;

namespace SocketServers
{
	public class StreamBuffer
		: IDisposable
	{
		private ArraySegment<byte> segment;

		public byte[] Array
		{
			get { return segment.Array; }
		}

		public int Offset
		{
			get { return segment.Offset; }
		}

		public int Count
		{
			get;
			private set;
		}

		public bool Resize(int maxSize)
		{
			if (maxSize > BufferManager.MaxSize)
				return false;

			if (maxSize < Count)
				return false;

			MaxSize = maxSize;

			if (maxSize > segment.Count)
			{
				var old = segment;
				segment = BufferManager.Allocate(maxSize);

				if (old.IsValid())
				{
					Buffer.BlockCopy(old.Array, old.Offset, segment.Array, segment.Offset, Count);
					BufferManager.Free(ref old);
				}
			}

			return true;
		}

		public void Free()
		{
			Count = 0;
			BufferManager.Free(ref segment);
		}

		public void Dispose()
		{
			Free();
		}

		public int MaxSize
		{
			get;
			private set;
		}

		public int BytesTransferred
		{
			get { return Count; }
		}

		public bool CopyTransferredFrom(ServerAsyncEventArgs e, int skipBytes)
		{
#if DEBUG
			if (e.Count < skipBytes)
				throw new ArgumentOutOfRangeException();
#endif
			return CopyFrom(e.Buffer, e.Offset + skipBytes, e.BytesTransferred - skipBytes);
		}

		public bool CopyFrom(ArraySegment<byte> segmnet, int skipBytes)
		{
#if DEBUG
			if (segmnet.Count < skipBytes)
				throw new ArgumentOutOfRangeException();
#endif
			return CopyFrom(segmnet.Array, segmnet.Offset + skipBytes, segmnet.Count - skipBytes);
		}

		public bool CopyFrom(ArraySegment<byte> segmnet)
		{
			return CopyFrom(segmnet.Array, segmnet.Offset, segmnet.Count);
		}

		internal bool CopyFrom(SecBufferEx secBuffer)
		{
			return CopyFrom(secBuffer.Buffer as byte[], secBuffer.Offset, secBuffer.Size);
		}

		public bool CopyFrom(byte[] array, int offset, int count)
		{
			if (count > MaxSize - Count)
				return false;

			if (count == 0)
				return true;

			Create();

			Buffer.BlockCopy(array, offset, segment.Array, segment.Offset + Count, count);

			Count += count;

			return true;
		}

		public void MoveToBegin(int offsetOffset, int count)
		{
			Buffer.BlockCopy(segment.Array, segment.Offset + offsetOffset, 
				segment.Array, segment.Offset, count);

			Count = count;
		}

		public ArraySegment<byte> Detach()
		{
			var segment1 = segment;

			segment = new ArraySegment<byte>();
			Count = 0;

			return segment1;
		}

		public bool IsValid()
		{
			return segment.Array != null && segment.Offset >= 0 && segment.Count > 0;
		}

		public bool IsInvalid()
		{
			return segment.Array == null || segment.Offset < 0 || segment.Count <= 0;
		}

		private void Create()
		{
			if (segment.IsInvalid())
			{
				Count = 0;
				segment = BufferManager.Allocate(MaxSize);
			}
		}
	}
}
