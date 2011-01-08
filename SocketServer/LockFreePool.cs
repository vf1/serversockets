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
	public interface ILockFreePoolItem
	{
		bool IsPooled { set; }
		void SetDefaultValue();
	}

	public interface ILockFreePool<T>
		: IDisposable
	{
		void Dispose();
		T Get();
		void Put(ref T value);
		void Put(T value);
		int Queued { get; }
		int Created { get; }
	}

	public class LockFreePool<T>
		: ILockFreePool<T>
		where T : class, ILockFreePoolItem, IDisposable, new()
	{
		private LockFreeItem<T>[] array;
		private LockFreeStack<T> empty;
		private LockFreeStack<T> full;
		private Int32 created;

		public LockFreePool(int size)
		{
			array = new LockFreeItem<T>[size];

			full = new LockFreeStack<T>(array, -1, -1);
			empty = new LockFreeStack<T>(array, 0, array.Length);
		}

		public void Dispose()
		{
			for (; ; )
			{
				int index = full.Pop();

				if (index < 0)
					break;

				array[index].Value.Dispose();
				array[index].Value = default(T);
			}
		}

		public T Get()
		{
			T result = default(T);

			int index = full.Pop();
			if (index >= 0)
			{
				result = array[index].Value;
				array[index].Value = default(T);

				empty.Push(index);
			}
			else
			{
				result = new T();
				result.SetDefaultValue();

				Interlocked.Increment(ref created);
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

			int index = empty.Pop();
			if (index >= 0)
			{
				value.SetDefaultValue();

				array[index].Value = value;

				full.Push(index);
			}
			else
			{
				value.Dispose();
#if DEBUG
				throw new Exception(@"BufferPool too small");
#endif
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
