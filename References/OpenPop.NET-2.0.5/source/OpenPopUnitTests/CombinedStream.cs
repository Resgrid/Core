using System;
using System.IO;

namespace OpenPopUnitTests
{
	/// <summary>
	/// Only used to testing purposes
	/// </summary>
	internal class CombinedStream : Stream
	{
		private readonly Stream ReadStream;
		private readonly Stream WriteStream;

		public CombinedStream(Stream readStream, Stream writeStream)
		{
			if(readStream == null)
				throw new ArgumentNullException("readStream");

			if(writeStream == null)
				throw new ArgumentNullException("writeStream");

			ReadStream = readStream;
			WriteStream = writeStream;
		}

		public override void Flush()
		{
			WriteStream.Flush();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotImplementedException();
		}

		public override void SetLength(long value)
		{
			throw new NotImplementedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return ReadStream.Read(buffer, offset, count);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			WriteStream.Write(buffer, offset, count);
		}

		public override bool CanRead
		{
			get { return ReadStream.CanRead; }
		}

		public override bool CanSeek
		{
			get { return false; }
		}

		public override bool CanWrite
		{
			get { return WriteStream.CanWrite; }
		}

		public override long Length
		{
			get { return ReadStream.Length; }
		}

		public override long Position
		{
			get { return ReadStream.Position; }
			set { ReadStream.Position = value; }
		}
	}
}