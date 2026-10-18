using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// In-memory stand-in for the RustFS artifact store (wizard plan section 8.7 "Tests"): multipart uploads, ranged reads and
	/// prefix deletes with the same contract as RustFsImportArtifactStore. Integration tests use a RustFS/MinIO container.
	/// </summary>
	public sealed class InMemoryImportArtifactStore : IImportArtifactStore
	{
		private readonly ConcurrentDictionary<string, byte[]> _objects = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte[]>> _uploads = new ConcurrentDictionary<string, ConcurrentDictionary<int, byte[]>>();

		public bool IsConfigured { get; set; } = true;
		public int AbortCount { get; private set; }
		public IReadOnlyDictionary<string, byte[]> Objects => _objects;

		public Task<ImportStoreHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(new ImportStoreHealth { Configured = IsConfigured, Healthy = IsConfigured, CheckedOn = DateTime.UtcNow, Reason = IsConfigured ? null : "not-configured" });

		public Task<string> StartUploadAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			var id = Guid.NewGuid().ToString("N");
			_uploads[id] = new ConcurrentDictionary<int, byte[]>();
			return Task.FromResult(id);
		}

		public Task<string> UploadPartAsync(string objectKey, string uploadId, int partNumber, byte[] content, CancellationToken cancellationToken = default)
		{
			_uploads[uploadId][partNumber] = content.ToArray();
			return Task.FromResult("etag-" + partNumber);
		}

		public Task CompleteUploadAsync(string objectKey, string uploadId, IReadOnlyList<ImportArtifactPart> parts, CancellationToken cancellationToken = default)
		{
			var upload = _uploads[uploadId];
			_objects[objectKey] = parts.OrderBy(p => p.PartNumber).SelectMany(p => upload[p.PartNumber]).ToArray();
			_uploads.TryRemove(uploadId, out _);
			return Task.CompletedTask;
		}

		public Task AbortUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken = default)
		{
			AbortCount++;
			if (uploadId != null)
				_uploads.TryRemove(uploadId, out _);
			return Task.CompletedTask;
		}

		public Task PutAsync(string objectKey, byte[] content, CancellationToken cancellationToken = default)
		{
			_objects[objectKey] = content.ToArray();
			return Task.CompletedTask;
		}

		public Task<long?> GetSizeAsync(string objectKey, CancellationToken cancellationToken = default) =>
			Task.FromResult(_objects.TryGetValue(objectKey, out var data) ? data.LongLength : (long?)null);

		public Task<byte[]> ReadRangeAsync(string objectKey, long offset, int length, CancellationToken cancellationToken = default)
		{
			var data = _objects[objectKey];
			var take = (int)Math.Max(0, Math.Min(length, data.LongLength - offset));
			return Task.FromResult(data.Skip((int)offset).Take(take).ToArray());
		}

		public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			_objects.TryRemove(objectKey, out _);
			return Task.CompletedTask;
		}

		public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
		{
			var keys = _objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
			foreach (var key in keys)
				_objects.TryRemove(key, out _);
			return Task.FromResult(keys.Count);
		}

		public Task<IReadOnlyList<int>> ListDepartmentIdsAsync(CancellationToken cancellationToken = default)
		{
			var root = ImportArtifactKeys.Root;
			IReadOnlyList<int> ids = _objects.Keys.Where(k => k.StartsWith(root, StringComparison.Ordinal))
				.Select(k => k.Substring(root.Length).Split('/')[0]).Select(s => int.TryParse(s, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
			return Task.FromResult(ids);
		}

		/// <summary>Test hook: replace one byte of a stored object (tamper detection).</summary>
		public void Corrupt(string objectKey, long offset)
		{
			_objects[objectKey][offset] ^= 0x5A;
		}

		/// <summary>Test hook: drop the tail of a stored object (truncation detection).</summary>
		public void Truncate(string objectKey, int keepBytes)
		{
			_objects[objectKey] = _objects[objectKey].Take(keepBytes).ToArray();
		}
	}
}
