// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public class BufferManager
	{
		private static SmartBufferPool manager =
			new SmartBufferPool(2048, 128, 64);

		public static ArraySegment<byte> Allocate(int size)
		{
			return manager.Allocate(size);
		}

		public static void Free(ref ArraySegment<byte> segment)
		{
			if (segment.IsValid())
				manager.Free(segment);
			segment = new ArraySegment<byte>();
		}

		public static int MaxSize
		{
			get { return SmartBufferPool.MaxSize; }
		}
	}
}