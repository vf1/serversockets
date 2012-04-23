// Copyright (C) 2010 OfficeSIP Communications
// This source is subject to the GNU General Public License.
// Please see Notice.txt for details.

using System;
using System.Collections.Generic;

namespace SocketServers
{
	public abstract class HeaderContentConnection
		: BaseConnection
	{
		public const int MaximumHeadersSize = 8192;
		private const int MinimumBuffer1Size = 4096;

		private ServerAsyncEventArgs e1;
		private StreamBuffer buffer1;
		private StreamBuffer buffer2;
		private StreamState state;
		private int expectedContentLength;
		private int receivedContentLength;
		private int buffer1UnusedCount;
		private ArraySegment<byte> headerData;
		private ArraySegment<byte> contentData;
		private int bytesProccessed;
		private bool ready;

		private Storage readerStorage;
		private Storage contentStorage;

		private int keepAliveRecived;

		#region enum StreamState {...}

		enum StreamState
		{
			WaitingHeaders,
			WaitingHeadersContinue,
			WaitingMicroBody,
			WaitingSmallBody,
			WaitingBigBody,
		}

		#endregion

		public HeaderContentConnection()
		{
			state = StreamState.WaitingHeaders;
		}

		public new void Dispose()
		{
			base.Dispose();

			if (buffer1 != null)
				buffer1.Dispose();

			if (buffer2 != null)
				buffer2.Dispose();

			if (e1 != null)
			{
				e1.Dispose();
				e1 = null;
			}
		}

		private StreamBuffer Buffer1
		{
			get
			{
				if (buffer1 == null)
					buffer1 = new StreamBuffer();
				return buffer1;
			}
		}

		private StreamBuffer Buffer2
		{
			get
			{
				if (buffer2 == null)
					buffer2 = new StreamBuffer();
				return buffer2;
			}
		}

		public ArraySegment<byte> Header
		{
			get { return headerData; }
		}

		public bool IsMessageReady
		{
			get { return ready; }
		}

		public ArraySegment<byte> Content
		{
			get { return contentData; }
		}

		public void ResetState()
		{
			ready = false;
			headerData = new ArraySegment<byte>();

			expectedContentLength = 0;
			receivedContentLength = 0;
			contentData = new ArraySegment<byte>();

			readerStorage = Storage.None;
			contentStorage = Storage.None;

			ResetParser();

			if (buffer1 != null)
			{
				buffer1UnusedCount = (buffer1.Count <= 0) ? buffer1UnusedCount + 1 : 0;

				if (buffer1.Capacity <= MaximumHeadersSize && buffer1UnusedCount < 8)
					buffer1.Clear();
				else
					buffer1.Free();
			}

			if (buffer2 != null)
				buffer2.Free();

			if (e1 != null)
			{
				e1.Dispose();
				e1 = null;
			}

			keepAliveRecived = 0;

			state = StreamState.WaitingHeaders;
		}

		enum Storage
		{
			None,
			E,
			E1,
			Buffer1,
			Buffer2,
		}

		public bool Proccess(ref ServerAsyncEventArgs e, out bool closeConnection)
		{
			//  ----------------------------------------------------------
			//  |           | 0  | 1  | 2  | 3  | 4  | 5  | 6  | 7  | 8  |
			//  ----------------------------------------------------------
			//  |         e | H  | HC |    |    |    |    |    |    |    |
			//  |        e1 |    |    | H  | HC | H  | H  |    |    |    |
			//  |   Buffer1 |    |    |    |    |  C |    | H  | HC | H  |
			//  |   Buffer2 |    |    |    |    |    |  C |    |    |  C |
			//  ----------------------------------------------------------

			closeConnection = false;

			switch (state)
			{
				case StreamState.WaitingHeaders:
					{
						int oldBytesProccessed = bytesProccessed;

						var data = new ArraySegment<byte>(e.Buffer, e.Offset + bytesProccessed, e.BytesTransferred - bytesProccessed);

						PreProcessRaw(data);
						var result = Parse(data);

						switch (result.ParseCode)
						{
							case ParseCode.NotEnoughData:
								{
									bytesProccessed += data.Count;

									ResetParser();

									Buffer1.Resize(MaximumHeadersSize);
									Buffer1.CopyTransferredFrom(e, oldBytesProccessed);
									state = StreamState.WaitingHeadersContinue;
								}
								break;

							case ParseCode.Error:
								{
									closeConnection = true;
								}
								break;

							case ParseCode.Skip:
								{
									bytesProccessed += result.Count;
								}
								break;

							case ParseCode.HeaderDone:
								{
									bytesProccessed += result.HeaderLength;

									SetReaderStorage(Storage.E, e.Buffer, e.Offset + oldBytesProccessed, result.HeaderLength);

									expectedContentLength = result.ContentLength;

									if (expectedContentLength <= 0)
									{
										SetReady();
									}
									else
									{
										int bytesLeft = e.BytesTransferred - bytesProccessed;

										if (bytesLeft >= expectedContentLength)
										{
											SetReady(Storage.E, e.Buffer, e.Offset + bytesProccessed, expectedContentLength);
											bytesProccessed += expectedContentLength;
										}
										else
										{
											if (expectedContentLength <= e.Count - e.BytesTransferred)
											{
												state = StreamState.WaitingMicroBody;
											}
											else if (expectedContentLength < MaximumHeadersSize)
											{
												if (Buffer1.IsInvalid || Buffer1.Capacity < expectedContentLength)
													if (Buffer1.Resize(Math.Max(expectedContentLength, MinimumBuffer1Size)) == false)
														closeConnection = true;

												if (closeConnection == false)
												{
													Buffer1.CopyTransferredFrom(e, bytesProccessed);
													state = StreamState.WaitingSmallBody;
												}
											}
											else
											{
												if (Buffer2.Resize(expectedContentLength) == false)
													closeConnection = true;
												else
												{
													Buffer2.CopyTransferredFrom(e, bytesProccessed);
													state = StreamState.WaitingBigBody;
												}
											}

											if (closeConnection == false)
											{
												e1 = e;
												e = null;
												readerStorage = Storage.E1;
											}

											bytesProccessed += bytesLeft;
											receivedContentLength += bytesLeft;
										}
									}
								}
								break;
						}
					}
					break;

				case StreamState.WaitingHeadersContinue:
					{
						int count = Math.Min(e.BytesTransferred - bytesProccessed, Buffer1.FreeSize);

						PreProcessRaw(new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred - bytesProccessed));

						System.Buffer.BlockCopy(e.Buffer, e.Offset, Buffer1.Array, Buffer1.Offset + Buffer1.Count, count);

						var data = new ArraySegment<byte>(Buffer1.Array, Buffer1.Offset, Buffer1.Count + count);
						var result = Parse(data);

						switch (result.ParseCode)
						{
							case ParseCode.NotEnoughData:
								{
									ResetParser();

									if (data.Count < Buffer1.Capacity)
									{
										Buffer1.AddCount(count);
										bytesProccessed += count;
									}
									else
									{
										closeConnection = true;
									}
								}
								break;

							case ParseCode.Error:
								{
									closeConnection = true;
								}
								break;

							case ParseCode.Skip:
								throw new NotImplementedException();

							case ParseCode.HeaderDone:
								{
									int delta = result.HeaderLength - Buffer1.Count;
									Buffer1.AddCount(delta);
									bytesProccessed += delta;

									SetReaderStorage(Storage.Buffer1, Buffer1.Array, Buffer1.Offset, result.HeaderLength);

									expectedContentLength = result.ContentLength;

									if (expectedContentLength <= 0)
									{
										SetReady();
									}
									else
									{
										int bytesLeft = e.BytesTransferred - bytesProccessed;

										if (bytesLeft >= expectedContentLength)
										{
											SetReady(Storage.E, e.Buffer, e.Offset + bytesProccessed, expectedContentLength);
											bytesProccessed += expectedContentLength;
										}
										else
										{
											if (expectedContentLength < Buffer1.FreeSize)
											{
												Buffer1.AddCount(bytesLeft);
												state = StreamState.WaitingSmallBody;
											}
											else
											{
												if (Buffer2.Resize(expectedContentLength) == false)
													closeConnection = true;
												Buffer2.CopyTransferredFrom(e, bytesProccessed);
												state = StreamState.WaitingBigBody;
											}

											bytesProccessed += bytesLeft;
											receivedContentLength += bytesLeft;
										}
									}
								}
								break;
						}
					}
					break;

				case StreamState.WaitingMicroBody:
					{
						int count = Math.Min(e.BytesTransferred - bytesProccessed,
							expectedContentLength - receivedContentLength);

						var data = new ArraySegment<byte>(e.Buffer, e.Offset + bytesProccessed, count);

						PreProcessRaw(data);
						System.Buffer.BlockCopy(data.Array, data.Offset, e1.Buffer, e1.Offset + e1.BytesTransferred, data.Count);

						//System.Buffer.BlockCopy(e.Buffer, e.Offset + bytesProccessed,
						//    e1.Buffer, e1.Offset + e1.BytesTransferred, count);

						e1.BytesTransferred += count;

						receivedContentLength += count;
						bytesProccessed += count;

						if (receivedContentLength == expectedContentLength)
							SetReady(Storage.E1, e1.Buffer, e1.Offset + e1.BytesTransferred - receivedContentLength, receivedContentLength);
					}
					break;

				case StreamState.WaitingSmallBody:
					{
						int count = Math.Min(e.BytesTransferred - bytesProccessed,
							expectedContentLength - receivedContentLength);

						var data = new ArraySegment<byte>(e.Buffer, e.Offset + bytesProccessed, count);

						PreProcessRaw(data);
						Buffer1.CopyFrom(data);
						//Buffer1.CopyFrom(e.Buffer, e.Offset + bytesProccessed, count);

						receivedContentLength += count;
						bytesProccessed += count;

						if (receivedContentLength == expectedContentLength)
							SetReady(Storage.Buffer1, Buffer1.Array, Buffer1.Offset + Buffer1.Count - receivedContentLength, receivedContentLength);
					}
					break;

				case StreamState.WaitingBigBody:
					{
						int count = Math.Min(e.BytesTransferred - bytesProccessed,
							expectedContentLength - receivedContentLength);

						var data = new ArraySegment<byte>(e.Buffer, e.Offset + bytesProccessed, count);

						PreProcessRaw(data);
						Buffer2.CopyFrom(data);
						//Buffer2.CopyFrom(e.Buffer, e.Offset + bytesProccessed, count);

						receivedContentLength += count;
						bytesProccessed += count;

						if (receivedContentLength == expectedContentLength)
							SetReady(Storage.Buffer2, Buffer2.Array, Buffer2.Offset + Buffer2.Count - receivedContentLength, receivedContentLength);
					}
					break;
			}

			bool continueProccessing = closeConnection == false &&
				e != null && bytesProccessed < e.BytesTransferred;

			if (continueProccessing == false)
				bytesProccessed = 0;

			return continueProccessing;
		}

		public void Dettach(ref ServerAsyncEventArgs e, out ArraySegment<byte> segment1, out ArraySegment<byte> segment2)
		{
			if (readerStorage == Storage.E)
			{
				int size = headerData.Count;
				if (contentStorage == readerStorage)
					size += contentData.Count;

				segment1 = Detach(ref e, size);
				segment2 = (contentStorage != readerStorage) ? Dettach(contentStorage) : new ArraySegment<byte>();
			}
			else
			{
				segment1 = Dettach(readerStorage);

				if (contentStorage != readerStorage)
				{
					if (contentStorage == Storage.E)
						segment2 = Detach(ref e, contentData.Count);
					else
						segment2 = Dettach(contentStorage);
				}
				else
				{
					segment2 = new ArraySegment<byte>();
				}
			}
		}

		private ArraySegment<byte> Detach(ref ServerAsyncEventArgs e, int size)
		{
			ServerAsyncEventArgs copy = null;
			if (e.BytesTransferred > size)
				copy = e.CreateDeepCopy();

			var result = e.DetachBuffer();

			EventArgsManager.Put(ref e);
			if (copy != null)
				e = copy;

			return result;
		}

		private ArraySegment<byte> Dettach(Storage storage)
		{
			switch (storage)
			{
				case Storage.E1:
					return e1.DetachBuffer();
				case Storage.Buffer1:
					return buffer1.Detach();
				case Storage.Buffer2:
					return buffer2.Detach();
				case Storage.None:
					return new ArraySegment<byte>();
				default:
					throw new ArgumentException();
			}
		}

		protected enum ParseCode
		{
			NotEnoughData,
			HeaderDone,
			Error,
			Skip,
		}

		protected struct ParseResult
		{
			public ParseResult(ParseCode parseCode, int headerLength, int contentLength)
			{
				ParseCode = parseCode;
				HeaderLength = headerLength;
				ContentLength = contentLength;
			}

			public static ParseResult HeaderDone(int headerLength, int contentLength)
			{
				return new ParseResult(ParseCode.HeaderDone, headerLength, contentLength);
			}

			public static ParseResult Skip(int count)
			{
				return new ParseResult(ParseCode.Skip, count, 0);
			}

			public static ParseResult Error()
			{
				return new ParseResult() { ParseCode = ParseCode.Error, };
			}

			public static ParseResult NotEnoughData()
			{
				return new ParseResult() { ParseCode = ParseCode.NotEnoughData, };
			}

			public ParseCode ParseCode;
			public int HeaderLength;
			public int ContentLength;

			public int Count
			{
				get { return HeaderLength; }
			}
		}

		protected abstract void ResetParser();
		protected abstract void MessageReady();
		protected abstract ParseResult Parse(ArraySegment<byte> data);
		protected abstract void PreProcessRaw(ArraySegment<byte> data);

		private void SetReaderStorage(Storage readerStorage1, byte[] buffer, int offset, int count)
		{
			readerStorage = readerStorage1;
			headerData = new ArraySegment<byte>(buffer, offset, count);
		}

		private void SetReady(Storage contentStorage1, byte[] buffer, int offset, int count)
		{
			contentStorage = contentStorage1;
			contentData = new ArraySegment<byte>(buffer, offset, count);
			ready = true;

			MessageReady();
		}

		private void SetReady()
		{
			contentStorage = Storage.None;
			ready = true;

			MessageReady();
		}
	}
}
