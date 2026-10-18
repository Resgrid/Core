using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Providers.Import.Storage;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Client-side chunked AEAD for import artifacts (wizard plan sections 8.7 and 24.1 "RustFS store"): round trip through a
	/// multipart upload, random access, and detection of tampering, segment reordering, truncation and cross-artifact swaps.
	/// </summary>
	[TestFixture]
	public class ImportArtifactCipherTests
	{
		private const int Segment = 64 * 1024;
		private ImportArtifactCipher _cipher;
		private InMemoryImportArtifactStore _store;
		private string _key;

		[SetUp]
		public void SetUp()
		{
			_cipher = new ImportArtifactCipher();
			_store = new InMemoryImportArtifactStore();
			_key = _cipher.NewDataKey();
		}

		private async Task<byte[]> UploadAsync(string objectKey, string artifactId, byte[] plaintext)
		{
			var uploadId = await _store.StartUploadAsync(objectKey);
			var parts = new System.Collections.Generic.List<ImportArtifactPart>();
			for (int offset = 0, part = 1; offset < plaintext.Length; offset += Segment, part++)
			{
				var chunk = plaintext.Skip(offset).Take(Segment).ToArray();
				var isFinal = offset + chunk.Length == plaintext.Length;
				var etag = await _store.UploadPartAsync(objectKey, uploadId, part, _cipher.EncryptSegment(_key, artifactId, part - 1, isFinal, chunk));
				parts.Add(new ImportArtifactPart { PartNumber = part, ETag = etag, Size = chunk.Length });
			}
			await _store.CompleteUploadAsync(objectKey, uploadId, parts);
			return plaintext;
		}

		private static byte[] Random(int length)
		{
			var data = new byte[length];
			RandomNumberGenerator.Fill(data);
			return data;
		}

		[Test]
		public async Task Multipart_upload_round_trips_and_reads_are_seekable()
		{
			var plaintext = await UploadAsync("k/1", "a1", Random(Segment * 3 + 1234));

			await using var stream = await _cipher.OpenReadAsync(_store, "k/1", _key, "a1");
			stream.Length.Should().Be(plaintext.Length);
			using (var copy = new MemoryStream())
			{
				await stream.CopyToAsync(copy);
				copy.ToArray().Should().Equal(plaintext);
			}

			// Random access across a segment boundary, the way the zip reader seeks to the central directory.
			stream.Position = Segment - 10;
			var window = new byte[20];
			stream.Read(window, 0, window.Length).Should().Be(20);
			window.Should().Equal(plaintext.Skip(Segment - 10).Take(20));
		}

		[Test]
		public async Task A_tampered_segment_is_refused()
		{
			await UploadAsync("k/2", "a2", Random(Segment * 2));
			_store.Corrupt("k/2", ImportArtifactCipher.HeaderLength + 100);

			var stream = await _cipher.OpenReadAsync(_store, "k/2", _key, "a2");
			Action read = () => stream.CopyTo(new MemoryStream());
			read.Should().Throw<ImportArtifactIntegrityException>().Which.Code.Should().Be("IMP-ARTIFACT-TAMPERED");
		}

		[Test]
		public async Task A_truncated_object_is_refused_before_any_read()
		{
			await UploadAsync("k/3", "a3", Random(Segment * 2 + 10));
			var segmentBytes = ImportArtifactCipher.HeaderLength + Segment + ImportArtifactCipher.TagLength;
			_store.Truncate("k/3", segmentBytes * 2);

			Func<Task> open = () => _cipher.OpenReadAsync(_store, "k/3", _key, "a3");
			(await open.Should().ThrowAsync<ImportArtifactIntegrityException>()).Which.Code.Should().Be("IMP-ARTIFACT-TRUNCATED");
		}

		[Test]
		public async Task Reordered_segments_fail_authentication()
		{
			await UploadAsync("k/4", "a4", Random(Segment * 2 + 10));
			var data = _store.Objects["k/4"];
			var segmentBytes = ImportArtifactCipher.HeaderLength + Segment + ImportArtifactCipher.TagLength;
			var swapped = data.Skip(segmentBytes).Take(segmentBytes).Concat(data.Take(segmentBytes)).Concat(data.Skip(segmentBytes * 2)).ToArray();
			await _store.PutAsync("k/4", swapped);

			var stream = await _cipher.OpenReadAsync(_store, "k/4", _key, "a4");
			Action read = () => stream.CopyTo(new MemoryStream());
			read.Should().Throw<ImportArtifactIntegrityException>();
		}

		[Test]
		public async Task A_segment_cannot_be_moved_to_another_artifact()
		{
			await UploadAsync("k/5", "a5", Random(1000));

			var stream = await _cipher.OpenReadAsync(_store, "k/5", _key, "another-artifact");
			Action read = () => stream.CopyTo(new MemoryStream());
			read.Should().Throw<ImportArtifactIntegrityException>().Which.Code.Should().Be("IMP-ARTIFACT-TAMPERED");
		}

		[Test]
		public void Whole_payloads_round_trip_and_bind_to_their_label()
		{
			var sealedPayload = _cipher.EncryptWhole(_key, "row:d:5:source", new byte[] { 1, 2, 3 });
			_cipher.DecryptWhole(_key, "row:d:5:source", sealedPayload).Should().Equal(1, 2, 3);

			Action moved = () => _cipher.DecryptWhole(_key, "row:d:6:source", sealedPayload);
			moved.Should().Throw<ImportArtifactIntegrityException>();
		}

		[Test]
		public void Nonces_are_never_reused_for_the_same_segment()
		{
			var first = _cipher.EncryptSegment(_key, "a", 0, true, new byte[] { 9, 9, 9 });
			var second = _cipher.EncryptSegment(_key, "a", 0, true, new byte[] { 9, 9, 9 });
			first.Skip(6).Take(ImportArtifactCipher.NonceLength).Should().NotEqual(second.Skip(6).Take(ImportArtifactCipher.NonceLength));
		}
	}
}
