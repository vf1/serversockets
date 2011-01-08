// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	public interface ILockFreePoolItemIndex
	{
		int Index { get; set; }
	}

	/// <summary>
	/// This class is faster than LockFreePool on ~100% theory and ~30% in BufferPoolTest.
	/// But LockFreeFastPool has one major disadvantage, it will not reuse array item slot if pool item lost.
	/// </summary>
	public class LockFreeFastPool<T>
		: ILockFreePool<T>
		where T : class, ILockFreePoolItem, ILockFreePoolItemIndex, IDisposable, new()
	{
		private LockFreeItem<T>[] array;
		private LockFreeStack<T> full;
		private Int32 created;

		internal LockFreeFastPool(int size)
		{
			array = new LockFreeItem<T>[size];
			full = new LockFreeStack<T>(array, -1, -1);
		}

		public T Get()
		{
			T result = default(T);

			int index = full.Pop();
			if (index >= 0)
			{
				result = array[index].Value;
				array[index].Value = default(T);
			}
			else
			{
				result = new T();
				result.SetDefaultValue();
				result.Index = -1;

				if (created < array.Length)
				{
					int newIndex = Interlocked.Increment(ref created) - 1;
					if (newIndex < array.Length)
						result.Index = newIndex;
#if DEBUG
					else
						throw new Exception(@"BufferPool too small");
#endif
				}
			}

			result.IsPooled = false;
			return result;
		}

		public void Put(ref T value)
		{
			Put(value);
			value = null;
		}

		public void Put(T value)
		{
			value.IsPooled = true;

			int index = value.Index;
			if (index >= 0)
			{
				value.SetDefaultValue();

				array[index].Value = value;

				full.Push(index);
			}
			else
			{
				value.Dispose();
			}
		}

		public int Queued
		{
			get { return full.Length; }
		}

		public int Created
		{
			get { return created; }
		}
	}
}
