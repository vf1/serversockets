// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace SocketServers
{
	/// <summary>
	/// Non-blocking queue implementation from:
	///		Simple, Fast, and Practical Non-Blocking and Blocking Concurrent Queue Algorithms
	///		Maged M. Michael, Michael L. Scott
	///		http://www.cs.rochester.edu/u/scott/papers/1996_PODC_queues.pdf
	/// </summary>
	class LockFreeQueue<T>
	{
		[StructLayout(LayoutKind.Sequential, Pack = 128)]
		struct PaddedVars
		{
			public Int64 head;
			public Int64 tail;
			public LockFreeItem<T>[] array;
		}

		private PaddedVars q;

		public LockFreeQueue(LockFreeItem<T>[] array, Int32 enqueueFromDummy, Int32 enqueueCount)
		{
			if (enqueueCount <= 0)
				throw new ArgumentOutOfRangeException(@"enqueueCount", @"Queue must include at least one dummy element");

			q.array = array;
			q.head = enqueueFromDummy;
			q.tail = enqueueFromDummy + enqueueCount - 1;

			for (Int32 i = 0; i < enqueueCount - 1; i++)
				q.array[i + enqueueFromDummy].Next = enqueueFromDummy + i + 1;
			q.array[q.tail].Next = 0xFFFFFFFFL;
		}

		public void Enqueue(Int32 index)
		{
			UInt64 tail1, next1, next2, xchg;

			unchecked
			{
				q.array[index].Next |= 0xFFFFFFFFL;

				for (; ; )
				{
					tail1 = (UInt64)Interlocked.Read(ref q.tail);
					next1 = (UInt64)Interlocked.Read(ref q.array[tail1 & 0xFFFFFFFFUL].Next);

					if (tail1 == (UInt64)q.tail)
					{
						if ((next1 & 0xFFFFFFFFUL) == 0xFFFFFFFFUL)
						{
							xchg = ((next1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | ((UInt64)(UInt32)index);
							next2 = (UInt64)Interlocked.CompareExchange(ref q.array[tail1 & 0xFFFFFFFFUL].Next, (Int64)xchg, (Int64)next1);
							if (next2 == next1)
								break;
						}
						else
						{
							xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							Interlocked.CompareExchange(ref q.tail, (Int64)xchg, (Int64)tail1);
						}
					}
				}

				xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | ((UInt64)(UInt32)index);
				Interlocked.CompareExchange(ref q.tail, (Int64)xchg, (Int64)tail1);
			}
		}

		public Int32 Dequeue()
		{
			UInt64 head1, head2, tail1, next1, xchg;
			Int32 index;

			unchecked
			{
				for (; ; )
				{
					head1 = (UInt64)Interlocked.Read(ref q.head);
					tail1 = (UInt64)Interlocked.Read(ref q.tail);
					next1 = (UInt64)Interlocked.Read(ref q.array[head1 & 0xFFFFFFFFUL].Next);

					if (head1 == (UInt64)q.head)
					{
						if ((head1 & 0xFFFFFFFFUL) == (tail1 & 0xFFFFFFFFUL))
						{
							if ((next1 & 0xFFFFFFFFUL) == 0xFFFFFFFFUL)
								return -1;

							xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							Interlocked.CompareExchange(ref q.tail, (Int64)xchg, (Int64)tail1);
						}
						else
						{
							T value = q.array[next1 & 0xFFFFFFFFUL].Value;

							xchg = ((head1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							head2 = (UInt64)Interlocked.CompareExchange(ref q.head, (Int64)xchg, (Int64)head1);
							if (head2 == head1)
							{
								index = (Int32)(head1 & 0xFFFFFFFFUL);
								q.array[index].Value = value;
								return index;
							}
						}
					}
				}
			}
		}
	}
}
