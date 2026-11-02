using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.GeoLocationProvider.Offline
{
	/// <summary>
	/// The offline pack store MappingConfig.OfflinePackStorage names: "file" (a directory the API and worker share) or "s3"
	/// (an S3-compatible bucket such as the RustFS already in every stack). Anything else leaves offline packs off.
	/// </summary>
	public sealed class OfflineMapPackStore : IOfflineMapPackStore, IDisposable
	{
		private readonly Lazy<IOfflineMapPackStore> _inner = new Lazy<IOfflineMapPackStore>(Create, LazyThreadSafetyMode.ExecutionAndPublication);

		public bool IsConfigured => _inner.Value.IsConfigured;

		private static IOfflineMapPackStore Create()
		{
			switch ((MappingConfig.OfflinePackStorage ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "file":
					return new FileOfflineMapPackStore(MappingConfig.OfflinePackStoragePath);
				case "s3":
					return new S3OfflineMapPackStore();
				default:
					return new FileOfflineMapPackStore(null);
			}
		}

		public Task PutFileAsync(string key, string localPath, string contentType, CancellationToken cancellationToken = default) =>
			_inner.Value.PutFileAsync(key, localPath, contentType, cancellationToken);

		public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default) =>
			_inner.Value.PutAsync(key, content, contentType, cancellationToken);

		public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) => _inner.Value.GetSizeAsync(key, cancellationToken);

		public Task<Stream> OpenReadAsync(string key, long offset, long? length, CancellationToken cancellationToken = default) =>
			_inner.Value.OpenReadAsync(key, offset, length, cancellationToken);

		public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => _inner.Value.DeleteAsync(key, cancellationToken);

		public void Dispose()
		{
			if (_inner.IsValueCreated)
				(_inner.Value as IDisposable)?.Dispose();
		}
	}

	/// <summary>Packs in a directory; keys map to relative paths (no "..", no rooted keys).</summary>
	public sealed class FileOfflineMapPackStore : IOfflineMapPackStore
	{
		private readonly string _root;

		public FileOfflineMapPackStore(string root)
		{
			_root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
		}

		public bool IsConfigured => _root != null;

		private string PathFor(string key)
		{
			if (_root == null)
				throw new InvalidOperationException("Offline map pack storage is not configured (MappingConfig.OfflinePackStorage).");

			if (string.IsNullOrWhiteSpace(key) || key.Contains("..") || Path.IsPathRooted(key))
				throw new ArgumentException("Invalid pack key", nameof(key));

			var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
			if (!full.StartsWith(_root, StringComparison.Ordinal))
				throw new ArgumentException("Invalid pack key", nameof(key));

			return full;
		}

		public async Task PutFileAsync(string key, string localPath, string contentType, CancellationToken cancellationToken = default)
		{
			var target = PathFor(key);
			Directory.CreateDirectory(Path.GetDirectoryName(target));

			// Copy then rename, so a reader never sees a half-written pack.
			var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
			await using (var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
			await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				await source.CopyToAsync(destination, cancellationToken);

			File.Move(temp, target, overwrite: true);
		}

		public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
		{
			var target = PathFor(key);
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
			await File.WriteAllBytesAsync(temp, content, cancellationToken);
			File.Move(temp, target, overwrite: true);
		}

		public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
		{
			var info = new FileInfo(PathFor(key));
			return Task.FromResult(info.Exists ? info.Length : (long?)null);
		}

		public Task<Stream> OpenReadAsync(string key, long offset, long? length, CancellationToken cancellationToken = default)
		{
			var stream = new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
			stream.Seek(offset, SeekOrigin.Begin);
			return Task.FromResult<Stream>(length.HasValue ? new BoundedReadStream(stream, length.Value) : stream);
		}

		public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
		{
			var path = PathFor(key);
			if (File.Exists(path))
				File.Delete(path);
			else if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);

			return Task.CompletedTask;
		}
	}

	/// <summary>Packs in an S3-compatible bucket (path-style addressing, configurable SSL and region).</summary>
	public sealed class S3OfflineMapPackStore : IOfflineMapPackStore, IDisposable
	{
		private readonly Lazy<IAmazonS3> _client;

		public S3OfflineMapPackStore()
		{
			_client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public S3OfflineMapPackStore(IAmazonS3 client)
		{
			_client = new Lazy<IAmazonS3>(() => client);
		}

		public bool IsConfigured => !string.IsNullOrWhiteSpace(MappingConfig.OfflinePackS3Endpoint) && !string.IsNullOrWhiteSpace(MappingConfig.OfflinePackS3Bucket);

		private static string Bucket => MappingConfig.OfflinePackS3Bucket;

		private static string Key(string key)
		{
			var prefix = (MappingConfig.OfflinePackS3Prefix ?? string.Empty).Trim().Trim('/');
			return prefix.Length == 0 ? key : prefix + "/" + key;
		}

		public async Task PutFileAsync(string key, string localPath, string contentType, CancellationToken cancellationToken = default)
		{
			await _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = Bucket,
				Key = Key(key),
				FilePath = localPath,
				ContentType = contentType ?? "application/octet-stream"
			}, cancellationToken);
		}

		public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
		{
			await using var stream = new MemoryStream(content);
			await _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = Bucket,
				Key = Key(key),
				InputStream = stream,
				ContentType = contentType ?? "application/octet-stream"
			}, cancellationToken);
		}

		public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
		{
			try
			{
				var metadata = await _client.Value.GetObjectMetadataAsync(Bucket, Key(key), cancellationToken);
				return metadata.ContentLength;
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}
		}

		public async Task<Stream> OpenReadAsync(string key, long offset, long? length, CancellationToken cancellationToken = default)
		{
			var request = new GetObjectRequest { BucketName = Bucket, Key = Key(key) };
			if (offset > 0 || length.HasValue)
				request.ByteRange = length.HasValue ? new ByteRange(offset, offset + length.Value - 1) : new ByteRange($"bytes={offset}-");

			var response = await _client.Value.GetObjectAsync(request, cancellationToken);
			return response.ResponseStream;
		}

		public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
		{
			await _client.Value.DeleteObjectAsync(Bucket, Key(key), cancellationToken);
		}

		private static IAmazonS3 CreateClient()
		{
			var config = new AmazonS3Config
			{
				ServiceURL = MappingConfig.OfflinePackS3Endpoint,
				ForcePathStyle = true,
				UseHttp = !MappingConfig.OfflinePackS3UseSsl,
				AuthenticationRegion = string.IsNullOrWhiteSpace(MappingConfig.OfflinePackS3Region) ? "us-east-1" : MappingConfig.OfflinePackS3Region
			};

			// Trimmed: a Kubernetes Secret can carry a trailing newline.
			var credentials = new BasicAWSCredentials((MappingConfig.OfflinePackS3AccessKey ?? string.Empty).Trim(), (MappingConfig.OfflinePackS3SecretKey ?? string.Empty).Trim());
			return new AmazonS3Client(credentials, config);
		}

		public void Dispose()
		{
			if (_client.IsValueCreated)
				_client.Value.Dispose();
		}
	}

	/// <summary>Limits reads of an inner stream to a byte count (a file range).</summary>
	internal sealed class BoundedReadStream : Stream
	{
		private readonly Stream _inner;
		private long _remaining;

		public BoundedReadStream(Stream inner, long length)
		{
			_inner = inner;
			_remaining = length;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_remaining <= 0)
				return 0;

			var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
			_remaining -= read;
			return read;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			if (_remaining <= 0)
				return 0;

			var read = await _inner.ReadAsync(buffer.AsMemory(offset, (int)Math.Min(count, _remaining)), cancellationToken);
			_remaining -= read;
			return read;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (_remaining <= 0)
				return 0;

			var read = await _inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken);
			_remaining -= read;
			return read;
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
