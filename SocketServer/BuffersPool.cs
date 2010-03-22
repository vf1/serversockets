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
	public class BuffersPool<T>
		where T : class, IBuffersPoolItem, new()
	{
		private SafeStackItem<T>[] array;
		private SafeStack<T> empty;
		private SafeStack<T> full;
		private Int32 created;

		internal BuffersPool(int size)
		{
			array = new SafeStackItem<T>[size];

			full = new SafeStack<T>(array, -1, -1);
			empty = new SafeStack<T>(array, 0, array.Length);
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
				Interlocked.Increment(ref created);
			}

			return result;
		}

		public void Put(ref T value)
		{
			Put(value);
			value = null;
		}

		public void Put(T value)
		{
			value.Reset();

			int index = empty.Pop();
			if (index >= 0)
			{
				array[index].Value = value;

				full.Push(index);
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

	public interface IBuffersPoolItem
	{
		void Reset();
	}
}
