using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class FileHelperTests
	{
		[TestCase("EngineeringManager.Exercise.docx.pdf", "pdf")]
		[TestCase("incident.photo.JPG", "jpg")]
		[TestCase("archive.tar.gz", "gz")]
		[TestCase("no-extension", "")]
		[TestCase("", "")]
		[TestCase(null, "")]
		public void should_return_last_file_extension_without_dot(string fileName, string expectedExtension)
		{
			var extension = FileHelper.GetFileExtensionWithoutDot(fileName);

			extension.Should().Be(expectedExtension);
		}

		[TestCase(@"C:\fakepath\certificate.pdf", "certificate.pdf")]
		[TestCase("/tmp/operations plan.docx", "operations plan.docx")]
		[TestCase("document.pdf", "document.pdf")]
		[TestCase(null, "")]
		public void GetSafeFileName_WithClientPath_ReturnsFileNameOnly(string fileName, string expectedFileName)
		{
			// Act
			var result = FileHelper.GetSafeFileName(fileName);

			// Assert
			result.Should().Be(expectedFileName);
		}

		[Test]
		public async Task ReadAllBytesAsync_WithPartialNonSeekableReads_ReturnsCompleteFile()
		{
			// Arrange
			var expected = new byte[32_769];
			new Random(42).NextBytes(expected);
			using var stream = new PartialReadStream(expected, 7);

			// Act
			var result = await FileHelper.ReadAllBytesAsync(stream, CancellationToken.None);

			// Assert
			result.Should().Equal(expected);
		}

		private sealed class PartialReadStream : Stream
		{
			private readonly MemoryStream _inner;
			private readonly int _maximumReadSize;

			public PartialReadStream(byte[] data, int maximumReadSize)
			{
				_inner = new MemoryStream(data);
				_maximumReadSize = maximumReadSize;
			}

			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				return _inner.Read(buffer, offset, Math.Min(count, _maximumReadSize));
			}

			public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			{
				return _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maximumReadSize)], cancellationToken);
			}

			public override void Flush()
			{
			}

			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

			protected override void Dispose(bool disposing)
			{
				if (disposing)
					_inner.Dispose();

				base.Dispose(disposing);
			}
		}
	}
}
