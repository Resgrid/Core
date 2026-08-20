using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Resgrid.Web.Tts.Services
{
	/// <summary>
	/// Piper synthesis tuning shared by the persistent workers and the one-shot
	/// process fallback in <see cref="AudioProcessingService"/>. Keep both in sync —
	/// the audio must be identical regardless of which path produced it.
	/// </summary>
	internal static class PiperTuning
	{
		// 0.35s of silence between sentences — dispatch messages are strings of
		// short sentences and need audible boundaries to stay intelligible.
		public const string SentenceSilence = "0.35";

		// Lower generation noise than the Piper defaults (0.667/0.8): reduces
		// prosody jitter that makes digits and short words sound mumbled.
		public const string NoiseScale = "0.333";
		public const string NoiseW = "0.4";

		public static void AppendCommonArguments(ProcessStartInfo startInfo, string lengthScale)
		{
			startInfo.ArgumentList.Add("--length-scale");
			startInfo.ArgumentList.Add(lengthScale);
			startInfo.ArgumentList.Add("--sentence-silence");
			startInfo.ArgumentList.Add(SentenceSilence);
			startInfo.ArgumentList.Add("--noise-scale");
			startInfo.ArgumentList.Add(NoiseScale);
			startInfo.ArgumentList.Add("--noise-w");
			startInfo.ArgumentList.Add(NoiseW);
		}
	}

	public sealed class PiperWorkerFactory : IPiperWorkerFactory
	{
		// Reserved directory under the TTS temp root for worker output. Excluded from
		// TempDirectorySweepHostedService (live worker directories must not be swept);
		// stale content from a previous process is removed wholesale in the constructor.
		public const string WorkerRootDirectoryName = "piper-workers";

		private readonly TtsOptions _options;
		private readonly ILoggerFactory _loggerFactory;
		private readonly ILogger<PiperWorkerFactory> _logger;
		private readonly string _workerRoot;

		public PiperWorkerFactory(IOptions<TtsOptions> options, ILoggerFactory loggerFactory)
		{
			_options = options.Value;
			_loggerFactory = loggerFactory;
			_logger = loggerFactory.CreateLogger<PiperWorkerFactory>();

			var tempRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.TempDirectory)
				? Path.GetTempPath()
				: _options.TempDirectory);
			_workerRoot = Path.Combine(tempRoot, WorkerRootDirectoryName);

			try
			{
				if (Directory.Exists(_workerRoot))
				{
					Directory.Delete(_workerRoot, recursive: true);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Not fatal — synthesis still works and the stale entries only take
				// space until the next start. Logged because the temp volume is a
				// fixed-size emptyDir: repeated failures here are the leading
				// indicator of the disk filling up, and the sweep service
				// deliberately skips this directory.
				_logger.LogWarning(
					ex,
					"Failed to delete the stale Piper worker root {WorkerRoot} during startup; orphaned worker directories will remain.",
					_workerRoot);
			}

			Directory.CreateDirectory(_workerRoot);
		}

		public IPiperWorker Create(PiperSynthesisProfile profile)
		{
			var workerDirectory = Path.Combine(_workerRoot, Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(workerDirectory);

			var startInfo = new ProcessStartInfo
			{
				FileName = _options.PiperExecutable,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			startInfo.ArgumentList.Add("--model");
			startInfo.ArgumentList.Add(profile.ModelPath);
			// --json-input + --output_dir: each stdin line is {"text": ...}; Piper
			// writes one WAV per line into the directory and prints its path to stdout,
			// which is the per-request completion signal the worker waits on.
			startInfo.ArgumentList.Add("--json-input");
			startInfo.ArgumentList.Add("--output_dir");
			startInfo.ArgumentList.Add(workerDirectory);
			PiperTuning.AppendCommonArguments(startInfo, profile.LengthScale);

			return new PiperWorker(startInfo, workerDirectory, _loggerFactory.CreateLogger<PiperWorker>());
		}
	}

	/// <summary>
	/// See <see cref="IPiperWorker"/>. Protocol per request: write one JSON line to
	/// stdin, await one stdout line naming the WAV Piper wrote, move that file to the
	/// caller's path. Any deviation throws, and the pool replaces the worker.
	/// </summary>
	public sealed class PiperWorker : IPiperWorker
	{
		private const int StderrTailLength = 5;

		private readonly Process _process;
		private readonly string _workerDirectory;
		private readonly ILogger<PiperWorker> _logger;
		private readonly ConcurrentQueue<string> _stderrTail = new();
		private bool _disposed;

		public PiperWorker(ProcessStartInfo startInfo, string workerDirectory, ILogger<PiperWorker> logger)
		{
			_workerDirectory = workerDirectory;
			_logger = logger;
			_process = new Process { StartInfo = startInfo };

			if (!_process.Start())
			{
				throw new InvalidOperationException("The Piper worker process failed to start.");
			}

			// Drain stderr continuously — an undrained pipe buffer eventually blocks
			// Piper mid-synthesis. The tail is kept for failure diagnostics.
			// Fire-and-forget, so the delegate must never let an exception escape
			// unobserved: expected stream teardown is handled inside the loop, and
			// anything else is logged here.
			_ = Task.Run(async () =>
			{
				try
				{
					await DrainStandardErrorAsync();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed draining Piper worker stderr.");
				}
			});
		}

		public async Task SynthesizeAsync(string text, string outputFilePath, CancellationToken cancellationToken)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_process.HasExited)
			{
				throw new InvalidOperationException($"The Piper worker exited with code {_process.ExitCode}.{StderrSuffix()}");
			}

			var inputLine = JsonSerializer.Serialize(new { text });
			await _process.StandardInput.WriteLineAsync(inputLine.AsMemory(), cancellationToken);
			await _process.StandardInput.FlushAsync(cancellationToken);

			var reportedPath = await _process.StandardOutput.ReadLineAsync(cancellationToken);

			if (string.IsNullOrWhiteSpace(reportedPath))
			{
				throw new InvalidOperationException($"The Piper worker closed its output stream mid-request.{StderrSuffix()}");
			}

			var producedFilePath = reportedPath.Trim();

			if (!File.Exists(producedFilePath))
			{
				throw new InvalidOperationException($"The Piper worker reported \"{producedFilePath}\" but the file does not exist.{StderrSuffix()}");
			}

			File.Move(producedFilePath, outputFilePath, overwrite: true);
		}

		private async Task DrainStandardErrorAsync()
		{
			try
			{
				while (await _process.StandardError.ReadLineAsync() is { } line)
				{
					_stderrTail.Enqueue(line);

					while (_stderrTail.Count > StderrTailLength)
					{
						_stderrTail.TryDequeue(out _);
					}

					_logger.LogDebug("piper: {StderrLine}", line);
				}
			}
			catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
			{
				// Expected: the stream closes when the process dies or is disposed.
				// Anything outside this set is a real fault and reaches the call-site
				// handler in the constructor rather than being swallowed here.
			}
		}

		private string StderrSuffix()
		{
			var tail = string.Join(" | ", _stderrTail);
			return string.IsNullOrWhiteSpace(tail) ? string.Empty : $" Last stderr: {tail}";
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			try
			{
				if (!_process.HasExited)
				{
					_process.Kill(entireProcessTree: true);
				}
			}
			catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
			{
				// The process exited between the HasExited check and the Kill call, so
				// there is nothing left to kill. Expected on normal worker recycling —
				// logged at debug so it stays diagnosable without adding routine noise.
				_logger.LogDebug(ex, "Piper worker process had already exited when disposal tried to kill it.");
			}

			_process.Dispose();

			try
			{
				if (Directory.Exists(_workerDirectory))
				{
					Directory.Delete(_workerDirectory, recursive: true);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.LogWarning(ex, "Failed to delete Piper worker directory {WorkerDirectory}.", _workerDirectory);
			}
		}
	}
}
