// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.IO;
using System.Threading;
using Pcap;

namespace SocketServers
{
	public class Logger
		: IDisposable
	{
		private object sync;
		private PcapWriter writer;

		public Logger()
		{
			sync = new object();
		}

		internal void Dispose()
		{
			(this as IDisposable).Dispose();
		}

		void IDisposable.Dispose()
		{
			if (writer != null)
				writer.Dispose();
		}

		public void Enable(string filename)
		{
			Enable(File.Create(filename));
		}

		public void Enable(Stream stream)
		{
			lock (sync)
			{
				if (IsEnabled)
					Disable();

				writer = new PcapWriter(stream);

				IsEnabled = true;
			}
		}

		public void Disable()
		{
			lock (sync)
			{
				IsEnabled = false;

				if (writer != null)
					writer.Dispose();

				writer = null;
			}
		}

		public bool IsEnabled
		{
			get;
			private set;
		}

		public void Flush()
		{
			try
			{
				var localWriter = writer;
				if (localWriter != null)
					localWriter.Flush();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		public void WriteComment(string comment)
		{
			try
			{
				var localWriter = writer;
				if (localWriter != null)
					localWriter.WriteComment(comment);
			}
			catch (ObjectDisposedException)
			{
			}
		}

		internal void Write(ServerAsyncEventArgs e, bool incomingOutgoing)
		{
			try
			{
				var localWriter = writer;
				if (localWriter != null)
				{
					localWriter.Write(
						e.Buffer,
						e.Offset,
						incomingOutgoing ? e.BytesTransferred : e.Count,
						Convert(e.LocalEndPoint.Protocol),
						incomingOutgoing ? e.RemoteEndPoint : e.LocalEndPoint,
						incomingOutgoing ? e.LocalEndPoint : e.RemoteEndPoint);
				}
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private Protocol Convert(ServerProtocol source)
		{
			if (source == ServerProtocol.Udp)
				return Protocol.Udp;
			if (source == ServerProtocol.Tls)
				return Protocol.Tls;
			return Protocol.Tcp;
		}
	}
}
