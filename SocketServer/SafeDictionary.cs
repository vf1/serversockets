// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace SocketServers
{
	class SafeDictionary<K, T>
		where T : class
	{
		private object sync;
		private Int32 readerSync;
		private Dictionary<K, T> dictionary;

		public SafeDictionary()
		{
			sync = new object();
			readerSync = 0;
			dictionary = new Dictionary<K, T>();
		}

		public void Clear()
		{
			try
			{
				WriterIn();
				dictionary.Clear();
			}
			finally
			{
				WriterOut();
			}
		}

		public void Add(K key, T value)
		{
			try
			{
				WriterIn();
				dictionary.Add(key, value);
			}
			finally
			{
				WriterOut();
			}
		}

		public bool Remove(K key)
		{
			bool result;
			try
			{
				WriterIn();
				result = dictionary.Remove(key);
			}
			finally
			{
				WriterOut();
			}

			return result;
		}

		public void ForEach(Action<T> action)
		{
			try
			{
				ReaderIn();
				foreach (var pair in dictionary)
					action(pair.Value);
			}
			finally
			{
				ReaderOut();
			}
		}

		public void RemoveAll(Predicate<K> match, Action<T> removed)
		{
			try
			{
				WriterIn();

				List<K> keys = new List<K>();
				foreach (var pair in dictionary)
					if (match(pair.Key))
					{
						removed(pair.Value);
						keys.Add(pair.Key);
					}

				foreach (K key in keys)
					dictionary.Remove(key);
			}
			finally
			{
				WriterOut();
			}
		}

		public bool TryGetValue(K key, out T value)
		{
			bool result;

			try
			{
				ReaderIn();
				result = dictionary.TryGetValue(key, out value);
			}
			finally
			{
				ReaderOut();
			}

			return result;
		}

		public T GetValue(K key)
		{
			T value;
			try
			{
				ReaderIn();
				dictionary.TryGetValue(key, out value);
			}
			finally
			{
				ReaderOut();
			}

			return value;
		}

		public bool ContainsKey(K key)
		{
			bool result;
			try
			{
				ReaderIn();
				result = dictionary.ContainsKey(key);
			}
			finally
			{
				ReaderOut();
			}

			return result;
		}

		#region Synchronization

		private void ReaderIn()
		{
			bool locked = false;

			Int32 readerSync1;
			do
			{
				do
				{
					readerSync1 = readerSync;
					if (readerSync1 < 0)
					{
						if (locked == false)
						{
							Monitor.Enter(sync);
							locked = true;
						}
						readerSync1 = readerSync;
					}
				}
				while (readerSync1 < 0);
			}
			while (Interlocked.CompareExchange(ref readerSync, readerSync1 + 1, readerSync1) != readerSync1);

			if (locked)
				Monitor.Exit(sync);
		}

		private void ReaderOut()
		{
			Interlocked.Decrement(ref readerSync);
		}

		private void WriterIn()
		{
			Monitor.Enter(sync);

			while (Interlocked.CompareExchange(ref readerSync, -1, 0) != 0) ;
		}

		private void WriterOut()
		{
			Interlocked.Exchange(ref readerSync, 0);

			Monitor.Exit(sync);
		}

		#endregion
	}
}
