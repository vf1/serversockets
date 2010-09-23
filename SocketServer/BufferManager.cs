// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public class BufferManager
	{
		private static SmartBufferPool pool;

		public static void Initialize(int maxMemoryUsageMb, int initialSizeMb, int extraBufferSizeMb)
		{
			pool = new SmartBufferPool(maxMemoryUsageMb, initialSizeMb, extraBufferSizeMb);
		}

		public static void Initialize(int maxMemoryUsageMb)
		{
			pool = new SmartBufferPool(maxMemoryUsageMb, maxMemoryUsageMb / 8, maxMemoryUsageMb / 16);
		}

		public static bool IsInitialized()
		{
			return pool != null;
		}

		public static ArraySegment<byte> Allocate(int size)
		{
			return pool.Allocate(size);
		}

		public static void Free(ref ArraySegment<byte> segment)
		{
			if (segment.IsValid())
			{
				pool.Free(segment);
				segment = new ArraySegment<byte>();
			}
		}

		public static long MaxMemoryUsage
		{
			get { return pool.MaxMemoryUsage; }
		}

		public static int MaxSize
		{
			get { return SmartBufferPool.MaxSize; }
		}
	}
}