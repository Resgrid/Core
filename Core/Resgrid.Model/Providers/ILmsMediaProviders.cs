using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// The LMS media store (decision 9): RustFS, S3-compatible, its own bucket (LmsConfig.S3Bucket). Objects live under
	/// {prefix}/{departmentId}/{assetId}/... Fails closed: with no endpoint configured every write refuses and nothing streams.
	/// </summary>
	public interface ILmsMediaStore
	{
		bool IsConfigured { get; }

		Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default);

		Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);

		Task<byte[]> ReadAllAsync(string objectKey, CancellationToken cancellationToken = default);

		Task<long?> GetSizeAsync(string objectKey, CancellationToken cancellationToken = default);

		/// <summary>A time-limited GET URL for one object (the browser fetches segments straight from the store).</summary>
		string GetSignedUrl(string objectKey, DateTime expiresOnUtc);

		Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);

		/// <summary>The object key a department's asset file lives at.</summary>
		string KeyFor(int departmentId, int lmsMediaAssetId, string relativePath);
	}

	/// <summary>The renditions a transcode produced, in a local folder (worker 87 uploads them).</summary>
	public sealed class LmsTranscodeOutput
	{
		/// <summary>The folder holding index.m3u8, its segments and video.mp4.</summary>
		public string Directory { get; set; }

		/// <summary>Files relative to <see cref="Directory"/> (forward slashes).</summary>
		public List<string> Files { get; set; } = new List<string>();

		public int DurationSeconds { get; set; }
	}

	/// <summary>Transcodes a video to HLS plus one progressive MP4 rendition (ffmpeg, LmsConfig.FfmpegExecutable). Worker 87 only.</summary>
	public interface ILmsVideoTranscoder
	{
		/// <summary>Transcodes <paramref name="sourcePath"/> into <paramref name="outputDirectory"/>. Throws on failure (the worker retries).</summary>
		Task<LmsTranscodeOutput> TranscodeAsync(string sourcePath, string outputDirectory, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// AI captions (Enhanced AI Ai.Voice, §17.2): the hook only. T2 does not build transcription; the stub says captions must be
	/// uploaded. Ai.Voice implements this when it lands.
	/// </summary>
	public interface ILmsCaptionGenerator
	{
		bool IsAvailable(int departmentId);

		/// <summary>A WebVTT track for the asset, or null when AI captions are not available.</summary>
		Task<string> GenerateWebVttAsync(int departmentId, int lmsMediaAssetId, string locale, CancellationToken cancellationToken = default);
	}
}
