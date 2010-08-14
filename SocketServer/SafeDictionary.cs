// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace SocketServers
{
	class SafeDictionary<K, T>
	{
		private ReaderWriterLockSlim sync;
		private Dictionary<K, T> dictionary;

		public SafeDictionary()
			: this(-1)
		{
		}

		public SafeDictionary(int capacity)
		{
			sync = new ReaderWriterLockSlim();

			if (capacity > 0)
				dictionary = new Dictionary<K, T>(capacity);
			else
				dictionary = new Dictionary<K, T>();
		}

		public void Clear()
		{
			try
			{
				sync.EnterWriteLock();
				dictionary.Clear();
			}
			finally
			{
				sync.ExitWriteLock();
			}
		}

		public void Add(K key, T value)
		{
			try
			{
				sync.EnterWriteLock();
				dictionary.Add(key, value);
			}
			finally
			{
				sync.ExitWriteLock();
			}
		}

		public bool Remove(K key)
		{
			bool result;
			try
			{
				sync.EnterWriteLock();
				result = dictionary.Remove(key);
			}
			finally
			{
				sync.ExitWriteLock();
			}

			return result;
		}

		public void ForEach(Action<T> action)
		{
			try
			{
				sync.EnterReadLock();
				foreach (var pair in dictionary)
					action(pair.Value);
			}
			finally
			{
				sync.ExitReadLock();
			}
		}

		public void RemoveAll(Predicate<K> match, Action<T> removed)
		{
			try
			{
				sync.EnterWriteLock();

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
				sync.ExitWriteLock();
			}
		}

		public bool TryGetValue(K key, out T value)
		{
			bool result;

			try
			{
				sync.EnterReadLock();
				result = dictionary.TryGetValue(key, out value);
			}
			finally
			{
				sync.ExitReadLock();
			}

			return result;
		}

		public T GetValue(K key)
		{
			T value;
			try
			{
				sync.EnterReadLock();
				dictionary.TryGetValue(key, out value);
			}
			finally
			{
				sync.ExitReadLock();
			}

			return value;
		}

		public bool ContainsKey(K key)
		{
			bool result;
			try
			{
				sync.EnterReadLock();
				result = dictionary.ContainsKey(key);
			}
			finally
			{
				sync.ExitReadLock();
			}

			return result;
		}
	}
}
