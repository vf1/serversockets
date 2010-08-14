//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// Author: Michael J. Primeaux
// 

using System;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;

namespace SocketServers
{
	/// <summary>
	/// Represents a spin lock.
	/// </summary>
	/// <remarks>
	/// <para>
	/// In software engineering, a spinlock is a lock where the thread simply waits in a loop ("spins") repeatedly 
	/// checking until the lock becomes available. As the thread remains active but isn't performing a useful task, 
	/// the use of such a lock is a kind of busy waiting. Once acquired, spinlocks will usually be held until they 
	/// are explicitly released, although in some implementations they may be automatically released if the thread blocks 
	/// (aka "goes to sleep").
	/// </para>
	/// <para>
	/// Spinlocks are efficient if threads are only likely to be blocked for a short period of time, as they avoid the 
	/// overhead of operating system process re-scheduling. For this reason, spinlocks are often used within operating 
	/// system kernels. They are wasteful if the lock is held for a long period of time because they do not allow other 
	/// threads to run while a thread is waiting for the lock to be released. They are always inefficient if used on 
	/// systems with only one processor, as no other thread would be able to make progress while the waiting thread spun.
	/// </para>
	/// <para>	
	/// <b>Note</b>: This is a value type so it works very efficiently when used as a field in a class. Avoid boxing this 
	/// or you will lose thread safety!
	/// </para>
	/// <para>
	/// On a single CPU system, the kernel function <b>SwitchToThread</b> is called, which causes the calling thread to yield 
	/// execution to another thread that is ready to run on the current processor.  The operating system selects the next thread 
	/// to be executed. The yield of execution is in effect for up to one thread-scheduling time slice. After that, the 
	/// operating system reschedules execution for the yielding thread. The rescheduling is determined by the priority of 
	/// the yielding thread and the status of other threads that are available to run. 
	/// </para>
	/// <para>
	/// Unfortunately, the yield of execution using <b>SwitchToThread</b> is limited to the processor of the calling thread. 
	/// The operating system will not switch execution to another processor, even if that processor is idle or is running 
	/// a thread of lower  priority. Therefore, on a multiple-CPU system <see cref="Thread.SpinWait(int)"/> is used.
	/// </para>
	/// </remarks>
	public class SpinLock
	{
		private int _lockState; // Defaults to 0 = LockStateFree
		private static readonly bool _isSingleCpuMachine = (Environment.ProcessorCount == 1);

		private const int LockStateFree = 0;
		private const int LockStateOwned = 1;

		private static void StallThread()
		{
			//
			// On a single-CPU system, spinning does no good.
			//
			if (_isSingleCpuMachine)
			{
				//SwitchToThread();
				Thread.Sleep(0);
			}
			else
			{
				//
				// Multi-CPU system might be hyper-threaded so let another thread run.
				//
				Thread.SpinWait(1);
			}
		}

		//
		// This is what Thread.SpinWait calls.
		//
		//[DllImport("kernel32", ExactSpelling = true)]
		//[MethodImpl(MethodImplOptions.InternalCall), ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success), HostProtection(SecurityAction.LinkDemand, Synchronization = true, ExternalThreading = true)]
		//private static extern void SpinWaitInternal(int iterations);

		/// <summary>
		/// Causes the calling thread to yield execution to another thread that is ready to run on the current processor. 
		/// The operating system selects the next thread to be executed.
		/// </summary>
		//[DllImport("kernel32.dll"), HostProtection(SecurityAction.LinkDemand, ExternalThreading = true)]
		//private static extern bool SwitchToThread();

		/// <summary>
		/// Gets a value indicating whether the current thread holds a lock.
		/// </summary>
		public bool IsLockHeld
		{
			get
			{
				return _lockState == LockStateOwned;
			}
		}

		/// <summary>
		/// Acquires an exclusive lock.
		/// </summary>
		public void Enter()
		{
			//
			// Notify the host that execution is about to enter a region of code in which the effects of a thread abort 
			// or unhandled exception might jeopardize other tasks in the application domain. 
			//
			Thread.BeginCriticalRegion();

			while (true)
			{
				//
				// If resource available, set it to in-use and return
				//
				if (Interlocked.Exchange(ref _lockState, LockStateOwned) == LockStateFree)
				{
					return;
				}

				//
				// Efficiently spin, until the resource looks like it might 
				// be free. 
				//

				// NOTE: Just reading here (as compared to repeatedly 
				// calling Exchange) improves performance because writing 
				// forces all CPUs to update this value.
				//
				while (Thread.VolatileRead(ref _lockState) == LockStateOwned)
				{
					StallThread();
				}
			}
		}

		/// <summary>
		/// Releases an exclusive lock.
		/// </summary>
		public void Exit()
		{
			//
			// Mark the resource as available.
			//
			Interlocked.Exchange(ref _lockState, LockStateFree);

			//
			// Notify the host that execution is about to leave a region of code in which the effects of a thread abort 
			// or unhandled exception might jeopardize other tasks in the application domain. 
			//
			Thread.EndCriticalRegion();
		}
	}
}
