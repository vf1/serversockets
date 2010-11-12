// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SocketServers
{
	public class SocketRecycling
		: IDisposable
	{
		private LockFreeItem<Socket>[] array;
		private LockFreeStack<Socket> empty;
		private LockFreeStack<Socket> full4;
		private LockFreeStack<Socket> full6;
		private bool isEnabled;

		public SocketRecycling(int maxSocket)
		{
			if (maxSocket > 0)
			{
				isEnabled = true;

				array = new LockFreeItem<Socket>[maxSocket];

				empty = new LockFreeStack<Socket>(array, 0, maxSocket);
				full4 = new LockFreeStack<Socket>(array, -1, -1);
				full6 = new LockFreeStack<Socket>(array, -1, -1);
			}
		}

		public void Dispose()
		{
			if (isEnabled)
			{
				isEnabled = false;

				int index;
				while ((index = full4.Pop()) >= 0)
				{
					array[index].Value.Close();
					empty.Push(index);
				}

				while ((index = full6.Pop()) >= 0)
				{
					array[index].Value.Close();
					empty.Push(index);
				}
			}
		}

		public bool IsEnabled
		{
			get { return isEnabled; }
		}

		public Socket Get(AddressFamily family)
		{
			if (isEnabled)
			{
				int index = GetFull(family).Pop();

				if (index >= 0)
				{
					Socket socket = array[index].Value;
					array[index].Value = null;

					empty.Push(index);

					return socket;
				}
			}

			return null;
		}

		public bool Recycle(Socket socket, AddressFamily family)
		{
			if (isEnabled)
			{
				int index = empty.Pop();

				if (index >= 0)
				{
					array[index].Value = socket;
					GetFull(family).Push(index);

					return true;
				}
			}

			return false;
		}

		public int RecyclingCount
		{
			get { return (isEnabled) ? (full4.Length + full6.Length) : 0; }
		}

		private LockFreeStack<Socket> GetFull(AddressFamily family)
		{
			if (family == AddressFamily.InterNetwork)
				return full4;
			else if (family == AddressFamily.InterNetworkV6)
				return full6;
			throw new ArgumentOutOfRangeException();
		}
	}
}
