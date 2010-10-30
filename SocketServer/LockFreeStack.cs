// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Threading;
using System.Runtime.InteropServices;

namespace SocketServers
{
	[StructLayout(LayoutKind.Explicit)]
	struct LockFreeStackVars
	{
		[FieldOffset(0)]
		public Int64 Head;
		[FieldOffset(64)]
		public Int32 padding;
	}

	class LockFreeStack<T>
	{
		private LockFreeStackVars s;
		private LockFreeItem<T>[] array;

		public LockFreeStack(LockFreeItem<T>[] array1, Int32 pushFrom, Int32 pushCount)
		{
			array = array1;
			s.Head = pushFrom;
			for (Int32 i = 0; i < pushCount - 1; i++)
				array[i + pushFrom].Next = pushFrom + i + 1;
			if (pushFrom >= 0)
				array[pushFrom + pushCount - 1].Next = 0xFFFFFFFFL;
		}

		public Int32 Pop()
		{
			unchecked
			{
				UInt64 head1 = (UInt64)Interlocked.Read(ref s.Head);
				for (; ; )
				{
					Int32 index = (Int32)head1;
					if (index < 0)
						return -1;

					// or Interlocked.Read(ref array[index].Next) ?
					UInt64 xchg = (UInt64)array[index].Next & 0xFFFFFFFFUL | head1 & 0xFFFFFFFF00000000UL;
					UInt64 head2 = (UInt64)Interlocked.CompareExchange(ref s.Head, (Int64)xchg, (Int64)head1);

					if (head1 == head2)
						return index;

					head1 = head2;
				}
			}
		}

		public void Push(Int32 index)
		{
			unchecked
			{
				UInt64 head1 = (UInt64)Interlocked.Read(ref s.Head);
				for (; ; )
				{
					array[index].Next = (Int64)((UInt64)array[index].Next & 0xFFFFFFFF00000000L | head1 & 0xFFFFFFFFL);

					UInt64 xchg = (UInt64)(head1 + 0x100000000 & 0xFFFFFFFF00000000 | (UInt32)index);
					UInt64 head2 = (UInt64)Interlocked.CompareExchange(ref s.Head, (Int64)xchg, (Int64)head1);

					if (head1 == head2)
						return;

					head1 = head2;
				}
			}
		}

		public int Length
		{
			get
			{
				int length = 0;

				for (int i = (Int32)s.Head; i >= 0; i = (int)array[i].Next)
					length++;

				return length;
			}
		}
	}
}
