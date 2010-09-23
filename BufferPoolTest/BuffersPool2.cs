using System;
using System.Threading;

namespace SocketServers
{
	class BuffersPool2<T>
		where T : class, ILockFreePoolItem, new()
	{
		private readonly int maxThread;
		private readonly Int32 maxRowLength;
		private volatile Int32[] locks;
		private volatile Int32[] lengths;
		private volatile Int32 count;
		private T[,] items;

		internal BuffersPool2(int maxThread, int length)
		{
			this.maxThread = maxThread;
			locks = new Int32[maxThread];
			lengths = new Int32[maxThread];
			maxRowLength = length;// length / maxThread + (length % maxThread > 0 ? 1 : 0);
			items = new T[maxThread, maxRowLength];
		}

#pragma warning disable 0420

		public T Get()
		{
			T item = null;

			for (int i = count % maxThread; count > 0 && item == null; i = (i + 1) % maxThread)
			{
				if (lengths[i] > 0 && locks[i] == 0)
				{
					if (Interlocked.CompareExchange(ref locks[i], -1, 0) == 0)
					{
						if (lengths[i] > 0)
						{
							Interlocked.Decrement(ref count);
							item = items[i, --lengths[i]];
						}

						Interlocked.Exchange(ref locks[i], 0);
					}
				}
			}

			if (item == null)
				item = new T();

			return item;
		}

		public void Put(T item)
		{
			item.SetDefaultValue();

			for (int i = count % maxThread; count < items.Length; i = (i + 1) % maxThread)
			{
				if (lengths[i] < maxRowLength && locks[i] == 0)
				{
					if (Interlocked.CompareExchange(ref locks[i], -1, 0) == 0)
					{
						if (lengths[i] < maxRowLength)
						{
							Interlocked.Increment(ref count);
							items[i, lengths[i]++] = item;
							Interlocked.Exchange(ref locks[i], 0);
							break;
						}

						Interlocked.Exchange(ref locks[i], 0);
					}
				}
			}
		}

#pragma warning restore 0420

	}
}
