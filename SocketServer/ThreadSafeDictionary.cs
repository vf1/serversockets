// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Collections.Generic;
using System.Threading;

namespace SocketServers
{
	public class ThreadSafeDictionary<K, T>
		where T : class
	{
		private ReaderWriterLockSlim sync;
		private Dictionary<K, T> dictionary;

		public ThreadSafeDictionary()
			: this(-1, null)
		{
		}

		public ThreadSafeDictionary(int capacity)
			: this(capacity, null)
		{
		}

		public ThreadSafeDictionary(int capacity, IEqualityComparer<K> comparer)
		{
			sync = new ReaderWriterLockSlim();

			if (capacity > 0)
				dictionary = new Dictionary<K, T>(capacity, comparer);
			else
				dictionary = new Dictionary<K, T>(comparer);
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

		public bool TryAdd(K key, T value)
		{
			try
			{
				sync.EnterWriteLock();

				if (dictionary.ContainsKey(key))
					return false;

				dictionary.Add(key, value);

				return true;
			}
			finally
			{
				sync.ExitWriteLock();
			}
		}

		public T Replace(K key, T value)
		{
			try
			{
				sync.EnterWriteLock();

				T oldValue;
				if (dictionary.TryGetValue(key, out oldValue))
					dictionary.Remove(key);

				dictionary.Add(key, value);

				return oldValue;
			}
			finally
			{
				sync.ExitWriteLock();
			}
		}

		public bool Remove(K key, T value)
		{
			bool result = false;
			try
			{
				sync.EnterWriteLock();

				T dictValue;
				if (dictionary.TryGetValue(key, out dictValue))
				{
					if (dictValue == value)
						result = dictionary.Remove(key);
				}
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

		public bool Contain(Func<T, bool> predicate)
		{
			try
			{
				sync.EnterReadLock();
				foreach (var pair in dictionary)
					if (predicate(pair.Value))
						return true;
			}
			finally
			{
				sync.ExitReadLock();
			}

			return false;
		}

		public void Remove(Predicate<K> match, Action<T> removed)
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
