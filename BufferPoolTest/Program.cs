using System;
using System.Threading;
using SocketServers;

namespace BufferPoolTest
{
	class Program
	{
		static BuffersPool<Item> pool;
		static bool[] items;
		static bool run;
		static int count;
		
		const int threads = 4;
		const int actions = 4;
		const int poolSize = 16;

		//static void Test(ref int value)
		//{
		//    value = -value;
		//}

		static void Main(string[] args)
		{
			//int[] arr = new int[] { 1, 2, 3, 4, 5, };

			//Console.WriteLine("{0}, {1}, {2}, {3}, {4}", arr[0], arr[1], arr[2], arr[3], arr[4]);
			//Test(ref arr[0]);
			//Test(ref arr[4]);
			//Console.WriteLine("{0}, {1}, {2}, {3}, {4}", arr[0], arr[1], arr[2], arr[3], arr[4]);

			//return;

			int worker, io;
			ThreadPool.GetMaxThreads(out worker, out io);
			Console.WriteLine("ThreadPool.Max worker {0}, io {1}", worker, io);
			ThreadPool.GetMinThreads(out worker, out io);
			Console.WriteLine("ThreadPool.Min worker {0}, io {1}", worker, io);

			Console.WriteLine(@"ThreadPool Test Starting...");

			Console.WriteLine(@"  All threads: {0}", threads);
			Console.WriteLine(@"  Buffers per thread: {0}", actions);
			Console.WriteLine(@"  Buffer pool size: {0}", poolSize);

			Console.WriteLine();

			pool = new BuffersPool<Item>(poolSize);
			items = new bool[65536];

			run = true;
			for (int i = 0; i < threads; i++)
			{
				var thread = new Thread(TestBufferPool);
				thread.Start();
			}

			Console.WriteLine(@"Started. Press any key to stop...");
			Console.ReadKey();

			run = false;

			while (count > 0)
				Thread.Sleep(25);
		}

		static void TestBufferPool()
		{
			Interlocked.Increment(ref count);
			Thread.Sleep(100);

			Item[] dequeued = new Item[actions];

			while (run)
			{
				for (int i = 0; i < dequeued.Length; i++)
					dequeued[i] = pool.Get();

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
					pool.Put(dequeued[i]);
			}

			Interlocked.Decrement(ref count);
		}

		public static bool Run()
		{
			TestBufferPool();
			return true;
		}
	}

	class Item: IBuffersPoolItem
	{
		public static int count = -1;
		public int Index;

		public Item()
		{
			Index = Interlocked.Increment(ref count);
			Console.WriteLine(@"Buffer created: #{0}", Index + 1);
		}

		void IBuffersPoolItem.Reset()
		{
		}
	}
}
