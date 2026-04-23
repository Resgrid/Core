using System.IO;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Web.Tts.Services
{
	public sealed class CacheService : ICacheService
	{
		private static readonly Regex HashPattern = new("^[a-f0-9]{64}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private const byte CachePayloadVersion = 1;

		private readonly IStorageService _storageService;
		private readonly IDistributedCache _distributedCache;
		private readonly TtsOptions _options;

		public CacheService(IStorageService storageService, IDistributedCache distributedCache, IOptions<TtsOptions> options)
		{
			_storageService = storageService;
			_distributedCache = distributedCache;
			_options = options.Value;
		}

		public TtsCacheKey CreateCacheKey(string text, string voice, int speed)
		{
			using var sha256 = SHA256.Create();

			var payload = $"{voice}\u001F{speed}\u001F{text}";
			var hash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
			return CreateCacheKeyFromHash(hash);
		}

		public async Task<Uri?> TryGetCachedUrlAsync(TtsCacheKey cacheKey, CancellationToken cancellationToken)
		{
			if (await TryGetCachedAudioAsync(cacheKey.Hash, cancellationToken) is not null)
			{
				return await _storageService.GetObjectUrlAsync(cacheKey.ObjectKey, cancellationToken);
			}

			if (!await _storageService.ExistsAsync(cacheKey.ObjectKey, cancellationToken))
			{
				return null;
			}

			return await _storageService.GetObjectUrlAsync(cacheKey.ObjectKey, cancellationToken);
		}

		public async Task<TtsAudioContent?> TryGetAudioAsync(string hash, CancellationToken cancellationToken)
		{
			if (!TryCreateCacheKeyFromHash(hash, out var cacheKey))
			{
				return null;
			}

			var cachedAudio = await TryGetCachedAudioAsync(cacheKey.Hash, cancellationToken);

			if (cachedAudio is not null)
			{
				return cachedAudio;
			}

			var audio = await _storageService.GetObjectAsync(cacheKey.ObjectKey, cancellationToken);

			if (audio is null)
			{
				return null;
			}

			await SetCachedAudioAsync(cacheKey.Hash, audio, cancellationToken);
			return audio;
		}

		public async Task<Uri> StoreAsync(TtsCacheKey cacheKey, byte[] audioBytes, CancellationToken cancellationToken)
		{
			using var stream = new MemoryStream(audioBytes, writable: false);

			await _storageService.UploadAsync(cacheKey.ObjectKey, stream, "audio/wav", cancellationToken);
			await SetCachedAudioAsync(cacheKey.Hash, CreateAudioContent(audioBytes), cancellationToken);

			return await _storageService.GetObjectUrlAsync(cacheKey.ObjectKey, cancellationToken);
		}

		private TtsCacheKey CreateCacheKeyFromHash(string hash)
		{
			var normalizedHash = hash.Trim().ToLowerInvariant();
			var prefix = string.IsNullOrWhiteSpace(_options.CachePrefix)
				? string.Empty
				: _options.CachePrefix.Trim().Trim('/');
			var objectKey = string.IsNullOrWhiteSpace(prefix)
				? $"{normalizedHash}.wav"
				: $"{prefix}/{normalizedHash}.wav";

			return new TtsCacheKey(normalizedHash, objectKey);
		}

		private bool TryCreateCacheKeyFromHash(string hash, out TtsCacheKey cacheKey)
		{
			if (string.IsNullOrWhiteSpace(hash) || !HashPattern.IsMatch(hash))
			{
				cacheKey = default;
				return false;
			}

			cacheKey = CreateCacheKeyFromHash(hash);
			return true;
		}

		private TtsAudioContent CreateAudioContent(byte[] audioBytes)
		{
			return new TtsAudioContent(
				audioBytes,
				"audio/wav",
				CreateEntityTag(audioBytes),
				DateTimeOffset.UtcNow);
		}

		private async Task<TtsAudioContent?> TryGetCachedAudioAsync(string hash, CancellationToken cancellationToken)
		{
			var payload = await _distributedCache.GetAsync(GetAudioCacheEntryKey(hash), cancellationToken);

			if (payload is null || payload.Length == 0)
			{
				return null;
			}

			try
			{
				return DeserializeAudioContent(payload);
			}
			catch (InvalidDataException)
			{
				await _distributedCache.RemoveAsync(GetAudioCacheEntryKey(hash), cancellationToken);
				return null;
			}
		}

		private Task SetCachedAudioAsync(string hash, TtsAudioContent audio, CancellationToken cancellationToken)
		{
			var options = new DistributedCacheEntryOptions
			{
				AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.PlaybackMemoryCacheMinutes)
			};

			return _distributedCache.SetAsync(
				GetAudioCacheEntryKey(hash),
				SerializeAudioContent(audio),
				options,
				cancellationToken);
		}

		private static string GetAudioCacheEntryKey(string hash) => $"tts-audio::{hash}";

		private static string CreateEntityTag(byte[] audioBytes)
		{
			using var sha256 = SHA256.Create();
			return $"\"{Convert.ToHexString(sha256.ComputeHash(audioBytes)).ToLowerInvariant()}\"";
		}

		private static byte[] SerializeAudioContent(TtsAudioContent audio)
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

			writer.Write(CachePayloadVersion);
			writer.Write(audio.ContentType);
			writer.Write(audio.EntityTag);
			writer.Write(audio.LastModified.UtcDateTime.Ticks);
			writer.Write(audio.AudioBytes.Length);
			writer.Write(audio.AudioBytes);
			writer.Flush();

			return stream.ToArray();
		}

		private static TtsAudioContent DeserializeAudioContent(byte[] payload)
		{
			using var stream = new MemoryStream(payload, writable: false);
			using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

			if (reader.ReadByte() != CachePayloadVersion)
			{
				throw new InvalidDataException("Unsupported TTS audio cache payload version.");
			}

			var contentType = reader.ReadString();
			var entityTag = reader.ReadString();
			var lastModified = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
			var audioLength = reader.ReadInt32();

			if (audioLength < 0 || audioLength > stream.Length - stream.Position)
			{
				throw new InvalidDataException("The TTS audio cache payload length is invalid.");
			}

			var audioBytes = reader.ReadBytes(audioLength);

			if (audioBytes.Length != audioLength)
			{
				throw new InvalidDataException("The TTS audio cache payload is truncated.");
			}

			return new TtsAudioContent(audioBytes, contentType, entityTag, lastModified);
		}
	}
}
