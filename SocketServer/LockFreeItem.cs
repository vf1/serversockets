// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;

namespace SocketServers
{
	struct LockFreeItem<T>
	{
		public Int64 Next;
		public T Value;

		public new string ToString()
		{
			return string.Format("Next: {0}, Count: {1}, Value: {2}", (Int32)Next, (UInt32)(Next >> 32), (Value == null) ? @"null" : "full");
		}
	}
}
