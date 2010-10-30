using System;
using System.Threading;
using SocketServers;

namespace BufferPoolTest
{
	class Program
	{
		static bool testPool = false;
		static LockFreeItem<Item>[] array;
		static LockFreeQueue<Item> queue;
		static LockFreeStack<Item> stack;
		static LockFreePool<Item> pool;
		static bool[] items;
		static bool run;
		static int count;
		
		const int threads = 4;
		const int actions = 4;
		const int poolSize = 16;

		static void Main(string[] args)
		{
			Console.WriteLine(@"ThreadPool Test Starting...");

			Console.WriteLine(@"  All threads: {0}", threads);
			Console.WriteLine(@"  Buffers per thread: {0}", actions);
			Console.WriteLine(@"  Buffer pool size: {0}", poolSize);

			Console.WriteLine();

			if (testPool)
			{
				pool = new LockFreePool<Item>(poolSize);
			}
			else
			{
				array = new LockFreeItem<Item>[poolSize + 1];
				for (int i = 1; i < array.Length; i++)
					array[i].Value = new Item();

				queue = new LockFreeQueue<Item>(array, 0, poolSize + 1);
				stack = new LockFreeStack<Item>(array, -1, -1);
			}

			items = new bool[65536];

			run = true;
			for (int i = 0; i < threads; i++)
			{
				var thread = new Thread(TestBufferPool);
				thread.Start();
			}

			Console.WriteLine(@"Started. Press any key to stop...");
			Console.ReadKey(true);

			run = false;

			while (count > 0)
				Thread.Sleep(25);
		}

		static Int64 actionCount = 0;

		static void TestBufferPool()
		{
			bool console = Interlocked.Increment(ref count) == 1;
			Thread.Sleep(100);

			Item[] dequeued = new Item[actions];

			int actionPower = 24;
			Int64 actionPowered = 1 << actionPower;
			while (run)
			{
				for (int i = 0; i < dequeued.Length; i++)
				{
					if (testPool)
						dequeued[i] = pool.Get();
					else
					{
						int index = queue.Dequeue();
						dequeued[i] = array[index].Value;
						array[index].Value = null;
						stack.Push(index);
					}
				}

				for (int i = 0; i < dequeued.Length; i++)
				{
					if (items[dequeued[i].Index])
						Console.WriteLine(@"Error");
					else
						items[dequeued[i].Index] = true;
				}

				for (int i = 0; i < dequeued.Length; i++)
					items[dequeued[i].Index] = false;

				for (int i = 0; i < dequeued.Length; i++)
				{
					if (testPool)
						pool.Put(dequeued[i]);
					else
					{
						int index = stack.Pop();
						array[index].Value = dequeued[i];
						queue.Enqueue(index);
					}

					Interlocked.Increment(ref actionCount);
				}

				if (console)
				{
					if (actionPowered < actionCount)
					{
						Console.WriteLine("Reach 2 ^ {0}", actionPower);
						actionPower++;
						actionPowered <<= 1;
					}
				}
			}

			Interlocked.Decrement(ref count);
		}

		public static bool Run()
		{
			TestBufferPool();
			return true;
		}
	}

	class Item : ILockFreePoolItem, IDisposable
	{
		public static int count = -1;
		public int Index;

		public Item()
		{
			Index = Interlocked.Increment(ref count);
			Console.WriteLine(@"Buffer created: #{0}", Index + 1);
		}

		void ILockFreePoolItem.SetDefaultValue()
		{
		}

		bool ILockFreePoolItem.IsPooled
		{
			set { }
		}

		void IDisposable.Dispose()
		{
		}
	}
}
