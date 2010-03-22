// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Threading;

namespace SocketServers
{
	struct SafeStackItem<T>
	{
		public volatile Int32 Next;
		public T Value;
	}

	/// <summary>
	/// Lock-free array based stack
	/// </summary>
	/// <typeparam name="T">Type of items</typeparam>
	class SafeStack<T> where T : class
	{
		private Int64 head;
		private SafeStackItem<T>[] array;

		public SafeStack(SafeStackItem<T>[] array, Int32 pushFrom, Int32 pushCount)
		{
			this.array = array;
			head = pushFrom;
			for (Int32 i = 0; i < pushCount - 1; i++)
				array[i + pushFrom].Next = pushFrom + i + 1;
			if (pushFrom >= 0)
				array[pushFrom + pushCount - 1].Next = -1;
		}

		public Int32 Pop()
		{
			UInt64 head1 = (UInt64)head;
			for (; ; )
			{
				Int32 next = (Int32)head1;
				if (next < 0)
					return -1;

				UInt64 xchg = (UInt64)(UInt32)array[next].Next | (head1 & 0xFFFFFFFF00000000);
				UInt64 head2 = (UInt64)Interlocked.CompareExchange(ref head, (Int64)xchg, (Int64)head1);
				
				if (head1 == head2)
					return next;
				
				head1 = head2;
			}
		}

		public void Push(Int32 index)
		{
			UInt64 head1 = (UInt64)head;
			for (; ; )
			{
				array[index].Next = (Int32)head1;

				UInt64 xchg = (UInt64)((head1 + 0x100000000) & 0xFFFFFFFF00000000) | ((UInt32)index);
				UInt64 head2 = (UInt64)Interlocked.CompareExchange(ref head, (Int64)xchg, (Int64)head1);
				
				if (head1 == head2)
					return;
				
				head1 = head2;
			}
		}

		public int Length
		{
			get
			{
				int length = 0;

				for (int i = (Int32)head; i >= 0; i = array[i].Next)
					length++;

				return length;
			}
		}
	}
}
