// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace SocketServers
{
	[StructLayout(LayoutKind.Explicit)]
	struct LockFreeQueueVars
	{
		[FieldOffset(64)]
		public Int64 Head;
		[FieldOffset(128)]
		public Int64 Tail;
		[FieldOffset(192)]
		public bool HasDequeuePredicate;
		[FieldOffset(256)]
		public Int32 padding;
	}

	/// <summary>
	/// Non-blocking queue implementation from:
	///		Simple, Fast, and Practical Non-Blocking and Blocking Concurrent Queue Algorithms
	///		Maged M. Michael, Michael L. Scott
	///		http://www.cs.rochester.edu/u/scott/papers/1996_PODC_queues.pdf
	/// </summary>
	class LockFreeQueue<T>
	{
		private LockFreeItem<T>[] array;
		private LockFreeQueueVars q;
		private Predicate<T> dequeuePredicate;

		public LockFreeQueue(LockFreeItem<T>[] array1, Int32 enqueueFromDummy, Int32 enqueueCount)
		{
			if (enqueueCount <= 0)
				throw new ArgumentOutOfRangeException(@"enqueueCount", @"Queue must include at least one dummy element");

			array = array1;
			q.Head = enqueueFromDummy;
			q.Tail = enqueueFromDummy + enqueueCount - 1;

			for (Int32 i = 0; i < enqueueCount - 1; i++)
				array[i + enqueueFromDummy].Next = enqueueFromDummy + i + 1;
			array[q.Tail].Next = 0xFFFFFFFFL;
		}

		public Predicate<T> DequeuePredicate
		{
			get { return dequeuePredicate; }
			set
			{
				dequeuePredicate = value;
				q.HasDequeuePredicate = dequeuePredicate == null;
			}
		}

		public void Enqueue(Int32 index)
		{
			UInt64 tail1, next1, next2, xchg;

			unchecked
			{
				array[index].Next |= 0xFFFFFFFFL;

				for (; ; )
				{
					tail1 = (UInt64)Interlocked.Read(ref q.Tail);
					next1 = (UInt64)Interlocked.Read(ref array[tail1 & 0xFFFFFFFFUL].Next);

					if (tail1 == (UInt64)q.Tail)
					{
						if ((next1 & 0xFFFFFFFFUL) == 0xFFFFFFFFUL)
						{
							xchg = ((next1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | ((UInt64)(UInt32)index);
							next2 = (UInt64)Interlocked.CompareExchange(ref array[tail1 & 0xFFFFFFFFUL].Next, (Int64)xchg, (Int64)next1);
							if (next2 == next1)
								break;
						}
						else
						{
							xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							Interlocked.CompareExchange(ref q.Tail, (Int64)xchg, (Int64)tail1);
						}
					}
				}

				xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | ((UInt64)(UInt32)index);
				Interlocked.CompareExchange(ref q.Tail, (Int64)xchg, (Int64)tail1);
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
					head1 = (UInt64)Interlocked.Read(ref q.Head);
					tail1 = (UInt64)Interlocked.Read(ref q.Tail);
					next1 = (UInt64)Interlocked.Read(ref array[head1 & 0xFFFFFFFFUL].Next);

					if (head1 == (UInt64)q.Head)
					{
						if ((head1 & 0xFFFFFFFFUL) == (tail1 & 0xFFFFFFFFUL))
						{
							if ((next1 & 0xFFFFFFFFUL) == 0xFFFFFFFFUL)
								return -1;

							xchg = ((tail1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							Interlocked.CompareExchange(ref q.Tail, (Int64)xchg, (Int64)tail1);
						}
						else
						{
							T value = array[next1 & 0xFFFFFFFFUL].Value;

							if (q.HasDequeuePredicate && DequeuePredicate(value) == false)
								return -1;

							xchg = ((head1 + 0x100000000UL) & 0xFFFFFFFF00000000UL) | (next1 & 0xFFFFFFFFUL);
							head2 = (UInt64)Interlocked.CompareExchange(ref q.Head, (Int64)xchg, (Int64)head1);
							if (head2 == head1)
							{
								index = (Int32)(head1 & 0xFFFFFFFFUL);
								array[index].Value = value;
								return index;
							}
						}
					}
				}
			}
		}
	}
}
