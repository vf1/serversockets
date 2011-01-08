using System;
using System.Threading;
using SocketServers;

namespace BufferPoolTest
{
	enum TestObject
	{
		Pool,
		FastPool,
		StackQueue,
	}

	class Program
	{
		static TestObject testObject = TestObject.FastPool;
		static bool testPool;
		static LockFreeItem<Item>[] array;
		static LockFreeQueue<Item> queue;
		static LockFreeStack<Item> stack;
		static ILockFreePool<Item> pool;
		static bool[] items;
		static bool run;
		static int count;

		const int threads = 4;
		const int actions = 4;
		const int poolSize = 16;

		static void Main(string[] args)
		{
			Console.Write(@"Test Lock-free ");
			if (testObject == TestObject.Pool)
			{
				Console.WriteLine("Pool");
				testPool = true;
			}
			else if (testObject == TestObject.FastPool)
			{
				Console.WriteLine("Fast Pool");
				testPool = true;
			}
			else if (testObject == TestObject.StackQueue)
			{
				Console.WriteLine("Stack & Queue (Not Pool)");
				testPool = false;
			}
			else
				throw new NotImplementedException();

			Console.WriteLine(@"ThreadPool Test Starting...");

			Console.WriteLine(@"  All threads: {0}", threads);
			Console.WriteLine(@"  Buffers per thread: {0}", actions);
			Console.WriteLine(@"  Buffer pool size: {0}", poolSize);

			Console.WriteLine();

			if (testPool)
			{
				if (testObject == TestObject.Pool)
					pool = new LockFreePool<Item>(poolSize);
				if (testObject == TestObject.FastPool)
					pool = new LockFreeFastPool<Item>(poolSize);
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

			int start = Environment.TickCount;
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
					if (items[dequeued[i].TestIndex])
						Console.WriteLine(@"Error");
					else
						items[dequeued[i].TestIndex] = true;
				}

				for (int i = 0; i < dequeued.Length; i++)
					items[dequeued[i].TestIndex] = false;

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
						int ms = Environment.TickCount - start;
						double speed = (double)Thread.VolatileRead(ref actionCount) / (double)ms;
						Console.WriteLine("Reach 2 ^ {0}: {1} ms, {2:0.00} per/ms", actionPower, ms, speed);
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

	class Item : ILockFreePoolItem, ILockFreePoolItemIndex, IDisposable
	{
		public static int count = -1;
		public int TestIndex;

		public Item()
		{
			TestIndex = Interlocked.Increment(ref count);
			Console.WriteLine(@"Buffer created: #{0}", TestIndex + 1);
		}

		void ILockFreePoolItem.SetDefaultValue()
		{
		}

		bool ILockFreePoolItem.IsPooled
		{
			set { }
		}

		int ILockFreePoolItemIndex.Index
		{
			get;
			set;
		}

		void IDisposable.Dispose()
		{
		}
	}
}
