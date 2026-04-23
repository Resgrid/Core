using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace Resgrid.Web.Tts.Services
{
	public sealed class AudioProcessingService : IAudioProcessingService
	{
		private readonly TtsOptions _options;
		private readonly ILogger<AudioProcessingService> _logger;

		public AudioProcessingService(
			IOptions<TtsOptions> options,
			ILogger<AudioProcessingService> logger)
		{
			_options = options.Value;
			_logger = logger;
		}

		public async Task<byte[]> GenerateNormalizedWavAsync(string text, string voice, int speed, CancellationToken cancellationToken)
		{
			var tempRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.TempDirectory) ? Path.GetTempPath() : _options.TempDirectory);
			var workingDirectory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
			var rawFilePath = Path.Combine(workingDirectory, "raw.wav");
			var normalizedFilePath = Path.Combine(workingDirectory, "normalized.wav");

			Directory.CreateDirectory(workingDirectory);

			try
			{
				await RunEspeakAsync(text, voice, speed, rawFilePath, cancellationToken);
				await RunFfmpegAsync(rawFilePath, normalizedFilePath, cancellationToken);

				return await File.ReadAllBytesAsync(normalizedFilePath, cancellationToken);
			}
			finally
			{
				TryDeleteDirectory(workingDirectory);
			}
		}

		private async Task RunEspeakAsync(string text, string voice, int speed, string outputFilePath, CancellationToken cancellationToken)
		{
			var startInfo = CreateStartInfo(_options.EspeakExecutable, redirectStandardInput: true);
			startInfo.ArgumentList.Add("--stdin");
			startInfo.ArgumentList.Add("-w");
			startInfo.ArgumentList.Add(outputFilePath);
			startInfo.ArgumentList.Add("-v");
			startInfo.ArgumentList.Add(voice);
			startInfo.ArgumentList.Add("-s");
			startInfo.ArgumentList.Add(speed.ToString(CultureInfo.InvariantCulture));

			await RunProcessAsync(startInfo, text, "eSpeak NG", cancellationToken);
		}

		private async Task RunFfmpegAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
		{
			var startInfo = CreateStartInfo(_options.FfmpegExecutable);
			startInfo.ArgumentList.Add("-nostdin");
			startInfo.ArgumentList.Add("-loglevel");
			startInfo.ArgumentList.Add("error");
			startInfo.ArgumentList.Add("-y");
			startInfo.ArgumentList.Add("-i");
			startInfo.ArgumentList.Add(inputFilePath);
			startInfo.ArgumentList.Add("-ar");
			startInfo.ArgumentList.Add(_options.NormalizedSampleRate.ToString(CultureInfo.InvariantCulture));
			startInfo.ArgumentList.Add("-ac");
			startInfo.ArgumentList.Add(_options.NormalizedChannels.ToString(CultureInfo.InvariantCulture));
			startInfo.ArgumentList.Add("-acodec");
			startInfo.ArgumentList.Add("pcm_s16le");
			startInfo.ArgumentList.Add(outputFilePath);

			await RunProcessAsync(startInfo, null, "ffmpeg", cancellationToken);
		}

		private static ProcessStartInfo CreateStartInfo(string fileName, bool redirectStandardInput = false)
		{
			return new ProcessStartInfo
			{
				FileName = fileName,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				RedirectStandardInput = redirectStandardInput
			};
		}

		private async Task RunProcessAsync(ProcessStartInfo startInfo, string? standardInput, string processName, CancellationToken cancellationToken)
		{
			using var process = new Process
			{
				StartInfo = startInfo
			};

			if (!process.Start())
			{
				throw new InvalidOperationException($"{processName} failed to start.");
			}

			using var cancellationRegistration = cancellationToken.Register(() => TryKillProcess(process));

			var standardErrorTask = process.StandardError.ReadToEndAsync();
			var standardOutputTask = process.StandardOutput.ReadToEndAsync();

			if (standardInput is not null)
			{
				await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
				await process.StandardInput.FlushAsync();
				process.StandardInput.Close();
			}

			await process.WaitForExitAsync(cancellationToken);

			var standardError = await standardErrorTask;
			var standardOutput = await standardOutputTask;

			if (process.ExitCode != 0)
			{
				var output = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
				throw new InvalidOperationException($"{processName} exited with code {process.ExitCode}: {output}");
			}
		}

		private void TryDeleteDirectory(string workingDirectory)
		{
			if (!Directory.Exists(workingDirectory))
			{
				return;
			}

			try
			{
				Directory.Delete(workingDirectory, recursive: true);
			}
			catch (IOException ex)
			{
				_logger.LogWarning(ex, "Failed to delete temporary TTS directory {WorkingDirectory}", workingDirectory);
			}
			catch (UnauthorizedAccessException ex)
			{
				_logger.LogWarning(ex, "Failed to delete temporary TTS directory {WorkingDirectory}", workingDirectory);
			}
		}

		private static void TryKillProcess(Process process)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch (InvalidOperationException)
			{
			}
			catch (NotSupportedException)
			{
			}
		}
	}
}
