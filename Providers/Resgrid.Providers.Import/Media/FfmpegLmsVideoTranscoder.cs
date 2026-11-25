using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Import.Media
{
	/// <summary>
	/// Worker 87's transcoder (Training T2, decision 9): ffmpeg (LmsConfig.FfmpegExecutable, separate from the TTS service's) makes one
	/// 720p H.264/AAC MP4 rendition with the moov atom up front (the rendition Omni may cache offline, R-C9), then segments it into a
	/// VOD HLS playlist without re-encoding. ffprobe reads the duration (the watched-percent cap needs it). Arguments are passed as a
	/// list, never through a shell. A failure throws; the worker re-queues or fails the asset.
	/// </summary>
	public sealed class FfmpegLmsVideoTranscoder : ILmsVideoTranscoder
	{
		public async Task<LmsTranscodeOutput> TranscodeAsync(string sourcePath, string outputDirectory, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
				throw new FileNotFoundException("The source video is missing.", sourcePath);

			Directory.CreateDirectory(outputDirectory);
			var mp4 = Path.Combine(outputDirectory, "video.mp4");
			var timeout = TimeSpan.FromMinutes(Math.Max(1, LmsConfig.TranscodeTimeoutMinutes));

			await RunAsync(Executable(LmsConfig.FfmpegExecutable, "ffmpeg"), new[]
			{
				"-y", "-hide_banner", "-loglevel", "error", "-i", sourcePath,
				"-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-pix_fmt", "yuv420p",
				"-c:a", "aac", "-b:a", "128k", "-ac", "2", "-movflags", "+faststart", mp4
			}, timeout, cancellationToken);

			await RunAsync(Executable(LmsConfig.FfmpegExecutable, "ffmpeg"), new[]
			{
				"-y", "-hide_banner", "-loglevel", "error", "-i", mp4, "-c", "copy", "-f", "hls", "-hls_time", "6", "-hls_playlist_type", "vod",
				"-hls_segment_filename", Path.Combine(outputDirectory, "seg%05d.ts"), Path.Combine(outputDirectory, "index.m3u8")
			}, timeout, cancellationToken);

			var duration = 0;
			try
			{
				var probe = await RunAsync(Executable(LmsConfig.FfprobeExecutable, "ffprobe"), new[]
				{
					"-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", mp4
				}, TimeSpan.FromMinutes(2), cancellationToken);
				if (double.TryParse(probe.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
					duration = (int)Math.Ceiling(seconds);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The duration is a nicety (the watched-percent cap); the renditions stand without it.
				Framework.Logging.LogException(ex, "ffprobe could not read an LMS video's duration.");
			}

			return new LmsTranscodeOutput
			{
				Directory = outputDirectory,
				DurationSeconds = duration,
				Files = Directory.GetFiles(outputDirectory).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToList()
			};
		}

		private static string Executable(string configured, string fallback) => string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

		private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
		{
			var start = new ProcessStartInfo(executable)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			foreach (var argument in arguments)
				start.ArgumentList.Add(argument);

			using var process = new Process { StartInfo = start };
			process.Start();
			var output = process.StandardOutput.ReadToEndAsync();
			var errors = process.StandardError.ReadToEndAsync();

			using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			limit.CancelAfter(timeout);
			try
			{
				await process.WaitForExitAsync(limit.Token);
			}
			catch (OperationCanceledException)
			{
				try { process.Kill(true); } catch (InvalidOperationException) { }
				if (cancellationToken.IsCancellationRequested)
					throw;
				throw new TimeoutException(Path.GetFileName(executable) + " did not finish within " + timeout.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " minutes.");
			}

			var stderr = await errors;
			if (process.ExitCode != 0)
				throw new InvalidOperationException(Path.GetFileName(executable) + " exited with " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " +
					(stderr.Length > 500 ? stderr.Substring(0, 500) : stderr));
			return await output;
		}
	}

	/// <summary>
	/// AI captions (Enhanced AI Ai.Voice) are a hook in T2, not built: this stub says they are unavailable, so captions are uploaded
	/// as WebVTT. Ai.Voice registers its own <see cref="ILmsCaptionGenerator"/> when it lands.
	/// </summary>
	public sealed class UnavailableLmsCaptionGenerator : ILmsCaptionGenerator
	{
		public bool IsAvailable(int departmentId) => false;

		public Task<string> GenerateWebVttAsync(int departmentId, int lmsMediaAssetId, string locale, CancellationToken cancellationToken = default) =>
			Task.FromResult<string>(null);
	}
}
