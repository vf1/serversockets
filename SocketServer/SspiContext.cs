// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using Microsoft.Win32.Ssp;

namespace SocketServers
{
	//enum TlsStates
	//{
	//    Handshake,
	//    Connected,
	//}

	class SspiContext
		: IDisposable
	{
		public SspiContext()
		{
			Handle = new SafeCtxtHandle();

			SecBufferDesc5 = new SecBufferDescEx(
				new SecBufferEx[]
					{ 
						new SecBufferEx(),
						new SecBufferEx(),
						new SecBufferEx(),
						new SecBufferEx(),
						new SecBufferEx(),
					});
		}

		public void Dispose()
		{
			Handle.Dispose();
			BufferManager.Free(ref Buffer);
		}

		public bool Connected;
		public SafeCtxtHandle Handle;
		public SecBufferDescEx SecBufferDesc5;
		public SecPkgContext_StreamSizes StreamSizes;

		public ArraySegment<byte> Buffer;
		public int BufferCount;

		public void CopyToBuffer(ServerAsyncEventArgs e, int processed)
		{
			CreateBuffer();

			Buffer.CopyArrayFrom(BufferCount,
				e.Buffer, e.Offset + processed, e.BytesTransferred - processed);

			BufferCount += e.BytesTransferred - processed;
		}

		public void CopyToBuffer(SecBufferEx secBuffer)
		{
			CreateBuffer();

			Buffer.CopyArrayFrom(secBuffer.Buffer as byte[],
				secBuffer.Offset, secBuffer.Size);

			BufferCount += secBuffer.Size;
		}

		public ArraySegment<byte> DetachBuffer()
		{
			var buffer = Buffer;

			Buffer = new ArraySegment<byte>();

			return buffer;
		}

		private void CreateBuffer()
		{
			if (Buffer.IsInvalid())
			{
				Buffer = BufferManager.Allocate(32768);
				BufferCount = 0;
			}
		}
	}
}
