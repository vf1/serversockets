// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	static class ByteArraySegmentHelpers
	{
		public static bool IsValid(this ArraySegment<byte> segment)
		{
			return segment.Array != null && segment.Offset >= 0 && segment.Count > 0;
		}

		public static bool IsInvalid(this ArraySegment<byte> segment)
		{
			return segment.Array == null || segment.Offset < 0 || segment.Count <= 0;
		}

		public static void CopyArrayTo(this ArraySegment<byte> src, ArraySegment<byte> dst)
		{
			Buffer.BlockCopy(src.Array, src.Offset, dst.Array, dst.Offset, Math.Min(src.Count, dst.Count));
		}

		public static void CopyArrayFrom(this ArraySegment<byte> dst, ArraySegment<byte> src)
		{
			Buffer.BlockCopy(src.Array, src.Offset, dst.Array, dst.Offset, Math.Min(src.Count, dst.Count));
		}

		public static void CopyArrayFrom(this ArraySegment<byte> dst, byte[] srcBuffer, int srcOffset, int srcCount)
		{
			Buffer.BlockCopy(srcBuffer, srcOffset, dst.Array, dst.Offset, Math.Min(srcCount, dst.Count));
		}

		public static void CopyArrayFrom(this ArraySegment<byte> dst, int dstExtraOffset, byte[] srcBuffer, int srcOffset, int srcCount)
		{
			Buffer.BlockCopy(srcBuffer, srcOffset, dst.Array, dst.Offset + dstExtraOffset, Math.Min(srcCount, dst.Count));
		}

		public static void CopyArrayFrom(this ArraySegment<byte> dst, ServerAsyncEventArgs e)
		{
			Buffer.BlockCopy(e.Buffer, e.Offset, dst.Array, dst.Offset, Math.Min(e.Count, dst.Count));
		}

		public static void CopyArrayFrom(this ArraySegment<byte> dst, int dstExtraOffset, ServerAsyncEventArgs e)
		{
			Buffer.BlockCopy(e.Buffer, e.Offset, dst.Array, dst.Offset + dstExtraOffset, Math.Min(e.Count, dst.Count));
		}
	}
}