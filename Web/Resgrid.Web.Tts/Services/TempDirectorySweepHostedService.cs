using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// Deletes orphaned synthesis working directories under the TTS temp directory.
	/// <see cref="AudioProcessingService"/> removes its own per-job directory in a finally
	/// block, so leftovers only appear when the process is hard-killed mid-synthesis
	/// (OOMKill, SIGKILL) or when the delete itself failed and was swallowed as a warning.
	/// Nothing else reclaims those, and the temp volume is a fixed-size emptyDir, so they
	/// are swept at startup and then on a schedule.
	/// </summary>
	public sealed class TempDirectorySweepHostedService : BackgroundService
	{
		private readonly TtsOptions _options;
		private readonly ILogger<TempDirectorySweepHostedService> _logger;

		public TempDirectorySweepHostedService(
			IOptions<TtsOptions> options,
			ILogger<TempDirectorySweepHostedService> logger)
		{
			_options = options.Value;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			// Yield before the first sweep so host startup isn't blocked on directory IO.
			await Task.Yield();

			SweepOnce();

			using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.TempDirectorySweepHours));

			try
			{
				while (await timer.WaitForNextTickAsync(stoppingToken))
				{
					SweepOnce();
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				_logger.LogInformation("TTS temp directory sweep stopped.");
			}
		}

		/// <summary>
		/// Removes every entry directly under the temp root last written more than
		/// TempDirectorySweepHours ago, and returns how many were deleted. Individual
		/// failures are logged and skipped so one undeletable entry can't stop the sweep.
		/// </summary>
		public int SweepOnce()
		{
			var tempRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.TempDirectory)
				? Path.GetTempPath()
				: _options.TempDirectory);

			if (!Directory.Exists(tempRoot))
			{
				return 0;
			}

			var cutoff = DateTime.UtcNow - TimeSpan.FromHours(_options.TempDirectorySweepHours);
			FileSystemInfo[] entries;

			try
			{
				// Materialized rather than streamed: the loop below deletes as it goes.
				entries = new DirectoryInfo(tempRoot).GetFileSystemInfos();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.LogWarning(ex, "Could not enumerate the TTS temp directory {TempRoot} for sweeping.", tempRoot);
				return 0;
			}

			var removed = 0;

			foreach (var entry in entries)
			{
				if (entry.LastWriteTimeUtc >= cutoff)
				{
					continue;
				}

				try
				{
					if (entry is DirectoryInfo directory)
					{
						directory.Delete(recursive: true);
					}
					else
					{
						entry.Delete();
					}

					removed++;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					_logger.LogWarning(ex, "Failed to sweep orphaned TTS temp entry {Entry}.", entry.FullName);
				}
			}

			if (removed > 0)
			{
				_logger.LogInformation(
					"Swept {RemovedCount} orphaned TTS temp entries older than {MaxAgeHours}h from {TempRoot}.",
					removed,
					_options.TempDirectorySweepHours,
					tempRoot);
			}

			return removed;
		}
	}
}
