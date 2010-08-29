// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using Microsoft.Win32.Ssp;

namespace SocketServers
{
	class SspiContext
		: IDisposable
	{
		public SspiContext()
		{
			Handle = new SafeCtxtHandle();

			SecBufferDesc5 = new SecBufferDescEx(new SecBufferEx[5]);

			SecBufferDesc2 = new SecBufferDescEx[]
			{
				new SecBufferDescEx(new SecBufferEx[2]),
				new SecBufferDescEx(new SecBufferEx[2]),
			};

			Buffer = new StreamBuffer();
		}

		public void Dispose()
		{
			Handle.Dispose();
			Buffer.Dispose();
		}

		public bool Connected;
		public SafeCtxtHandle Handle;
		public SecBufferDescEx SecBufferDesc5;
		public SecBufferDescEx[] SecBufferDesc2;
		public SecPkgContext_StreamSizes StreamSizes;
		public readonly StreamBuffer Buffer;
	}
}
