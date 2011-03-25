// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	public class EventArgsManager
	{
		private static ILockFreePool<ServerAsyncEventArgs> pool;

		internal static void Initialize()
		{
			pool = new LockFreePool<ServerAsyncEventArgs>(
				(int)(BufferManager.MaxMemoryUsage / ServerAsyncEventArgs.DefaultSize));
		}

		internal static void Initialize(int size)
		{
			pool = new LockFreePool<ServerAsyncEventArgs>(size);
		}

		internal static bool IsInitialized()
		{
			return pool != null;
		}

		public static ServerAsyncEventArgs Get()
		{
			return pool.Get();
		}

		public static void Put(ref ServerAsyncEventArgs value)
		{
			value.ResetTracing();

			pool.Put(ref value);
		}

		public static void Put(ServerAsyncEventArgs value)
		{
			value.ResetTracing();

			pool.Put(value);
		}

		internal static ILockFreePool<ServerAsyncEventArgs> Pool
		{
			get { return pool; }
		}
	}
}
