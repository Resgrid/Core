using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Storage
{
	/// <summary>
	/// Chunked AES-256-GCM for import artifacts (wizard plan section 8.7). Each segment is self-delimiting:
	/// <code>[version:1][final:1][plaintextLength:4 BE][nonce:12][ciphertext][tag:16]</code>
	/// The associated data binds the artifact id, the segment index and the final flag, so a segment moved to another
	/// position or artifact, a flipped final flag, or a truncated object fails authentication. Nonces are random (never
	/// derived from the index), so a re-uploaded part can never reuse a nonce under the artifact's key.
	/// </summary>
	public sealed class ImportArtifactCipher : IImportArtifactCipher
	{
		public const byte FormatVersion = 1;
		public const int HeaderLength = 1 + 1 + 4 + NonceLength;
		public const int NonceLength = 12;
		public const int TagLength = 16;
		public const int MaxSegmentPlaintext = 64 * 1024 * 1024;

		public string NewDataKey()
		{
			var key = new byte[32];
			RandomNumberGenerator.Fill(key);
			return Convert.ToBase64String(key);
		}

		public byte[] EncryptSegment(string dataKey, string artifactId, long segmentIndex, bool isFinal, ReadOnlySpan<byte> plaintext)
		{
			if (plaintext.Length > MaxSegmentPlaintext)
				throw new ArgumentOutOfRangeException(nameof(plaintext), "Segment too large.");

			var key = Convert.FromBase64String(dataKey);
			var output = new byte[HeaderLength + plaintext.Length + TagLength];
			output[0] = FormatVersion;
			output[1] = isFinal ? (byte)1 : (byte)0;
			BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(2, 4), plaintext.Length);
			var nonce = output.AsSpan(6, NonceLength);
			RandomNumberGenerator.Fill(nonce);

			using var aes = new AesGcm(key, TagLength);
			aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderLength, plaintext.Length), output.AsSpan(HeaderLength + plaintext.Length, TagLength),
				AssociatedData(artifactId, segmentIndex, isFinal));

			CryptographicOperations.ZeroMemory(key);
			return output;
		}

		public byte[] EncryptWhole(string dataKey, string artifactId, byte[] plaintext) =>
			EncryptSegment(dataKey, artifactId, 0, true, plaintext ?? Array.Empty<byte>());

		/// <summary>Decrypts one complete segment (header included) read from the store.</summary>
		public static byte[] DecryptSegment(string dataKey, string artifactId, long segmentIndex, ReadOnlySpan<byte> segment, out bool isFinal)
		{
			if (segment.Length < HeaderLength + TagLength || segment[0] != FormatVersion)
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

			isFinal = segment[1] == 1;
			var length = BinaryPrimitives.ReadInt32BigEndian(segment.Slice(2, 4));
			if (length < 0 || length > MaxSegmentPlaintext || segment.Length != HeaderLength + length + TagLength)
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

			var key = Convert.FromBase64String(dataKey);
			var plaintext = new byte[length];
			try
			{
				using var aes = new AesGcm(key, TagLength);
				aes.Decrypt(segment.Slice(6, NonceLength), segment.Slice(HeaderLength, length), segment.Slice(HeaderLength + length, TagLength), plaintext,
					AssociatedData(artifactId, segmentIndex, isFinal));
			}
			catch (CryptographicException)
			{
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-TAMPERED");
			}
			finally
			{
				CryptographicOperations.ZeroMemory(key);
			}

			return plaintext;
		}

		public byte[] DecryptWhole(string dataKey, string artifactId, byte[] content)
		{
			var plaintext = DecryptSegment(dataKey, artifactId, 0, content, out var isFinal);
			if (!isFinal)
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-TRUNCATED");
			return plaintext;
		}

		public async Task<Stream> OpenReadAsync(IImportArtifactStore store, string objectKey, string dataKey, string artifactId, CancellationToken cancellationToken = default)
		{
			var size = await store.GetSizeAsync(objectKey, cancellationToken);
			if (size == null)
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-MISSING");

			// Index the segments by reading only their headers (one ranged read each), then serve reads by decrypting the
			// segment that holds the requested position. Memory stays at one or two segments whatever the file size.
			var segments = new List<SegmentInfo>();
			long offset = 0, plainOffset = 0;
			while (offset < size.Value)
			{
				if (size.Value - offset < HeaderLength + TagLength)
					throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

				var header = await store.ReadRangeAsync(objectKey, offset, HeaderLength, cancellationToken);
				if (header.Length != HeaderLength || header[0] != FormatVersion)
					throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

				var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(2, 4));
				if (length < 0 || length > MaxSegmentPlaintext)
					throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

				var total = HeaderLength + length + TagLength;
				segments.Add(new SegmentInfo { Offset = offset, StoredLength = total, PlainOffset = plainOffset, PlainLength = length, Final = header[1] == 1 });
				offset += total;
				plainOffset += length;
			}

			if (offset != size.Value || segments.Count == 0 || !segments[segments.Count - 1].Final)
				throw new ImportArtifactIntegrityException("IMP-ARTIFACT-TRUNCATED");

			for (var i = 0; i < segments.Count - 1; i++)
				if (segments[i].Final)
					throw new ImportArtifactIntegrityException("IMP-ARTIFACT-CORRUPT");

			return new SeekableArtifactStream(store, objectKey, dataKey, artifactId, segments, plainOffset);
		}

		private static byte[] AssociatedData(string artifactId, long segmentIndex, bool isFinal)
		{
			var id = Encoding.UTF8.GetBytes("resgrid-import-artifact:v1:" + (artifactId ?? string.Empty));
			var data = new byte[id.Length + 9];
			id.CopyTo(data, 0);
			BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(id.Length, 8), segmentIndex);
			data[data.Length - 1] = isFinal ? (byte)1 : (byte)0;
			return data;
		}

		internal sealed class SegmentInfo
		{
			public long Offset { get; set; }
			public int StoredLength { get; set; }
			public long PlainOffset { get; set; }
			public int PlainLength { get; set; }
			public bool Final { get; set; }
		}

		/// <summary>
		/// Read-only seekable plaintext over an encrypted object. Readers that need random access (the XLSX zip central
		/// directory) seek freely; each segment is fetched with one ranged read, authenticated, and cached (two at a time).
		/// </summary>
		private sealed class SeekableArtifactStream : Stream
		{
			private readonly IImportArtifactStore _store;
			private readonly string _objectKey;
			private readonly string _dataKey;
			private readonly string _artifactId;
			private readonly List<SegmentInfo> _segments;
			private readonly long _length;
			private readonly Dictionary<int, byte[]> _cache = new Dictionary<int, byte[]>();
			private readonly Queue<int> _cacheOrder = new Queue<int>();
			private long _position;

			public SeekableArtifactStream(IImportArtifactStore store, string objectKey, string dataKey, string artifactId, List<SegmentInfo> segments, long length)
			{
				_store = store;
				_objectKey = objectKey;
				_dataKey = dataKey;
				_artifactId = artifactId;
				_segments = segments;
				_length = length;
			}

			public override bool CanRead => true;
			public override bool CanSeek => true;
			public override bool CanWrite => false;
			public override long Length => _length;

			public override long Position
			{
				get => _position;
				set => Seek(value, SeekOrigin.Begin);
			}

			public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

			public override int Read(Span<byte> buffer)
			{
				var written = 0;
				while (written < buffer.Length && _position < _length)
				{
					var index = FindSegment(_position);
					var plain = Segment(index);
					var within = (int)(_position - _segments[index].PlainOffset);
					var take = Math.Min(buffer.Length - written, plain.Length - within);
					plain.AsSpan(within, take).CopyTo(buffer.Slice(written));
					written += take;
					_position += take;
				}

				return written;
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				var target = origin switch
				{
					SeekOrigin.Begin => offset,
					SeekOrigin.Current => _position + offset,
					_ => _length + offset
				};

				if (target < 0)
					throw new IOException("Seek before start.");

				_position = Math.Min(target, _length);
				return _position;
			}

			private int FindSegment(long position)
			{
				int lo = 0, hi = _segments.Count - 1;
				while (lo < hi)
				{
					var mid = (lo + hi + 1) / 2;
					if (_segments[mid].PlainOffset <= position) lo = mid; else hi = mid - 1;
				}
				return lo;
			}

			private byte[] Segment(int index)
			{
				if (_cache.TryGetValue(index, out var cached))
					return cached;

				var info = _segments[index];
				// Readers (CsvHelper, OpenXml's zip) are synchronous; the worker thread blocks on the ranged read.
				var stored = _store.ReadRangeAsync(_objectKey, info.Offset, info.StoredLength).GetAwaiter().GetResult();
				var plain = DecryptSegment(_dataKey, _artifactId, index, stored, out var final);
				if (final != info.Final || plain.Length != info.PlainLength)
					throw new ImportArtifactIntegrityException("IMP-ARTIFACT-TAMPERED");

				_cache[index] = plain;
				_cacheOrder.Enqueue(index);
				while (_cacheOrder.Count > 2)
					_cache.Remove(_cacheOrder.Dequeue());

				return plain;
			}

			public override void Flush() { }
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}
	}
}
