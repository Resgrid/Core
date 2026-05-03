using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace Resgrid.Web.Tts.Services
{
	public sealed class AudioProcessingService : IAudioProcessingService
	{
		private const string MbrolaEnglishVoice = "mb-us1";
		private const int MbrolaEnglishSpeed = 130;
		private const int MbrolaEnglishPitch = 50;
		private const int MbrolaEnglishWordGap = 3;
		private const string TelephoneAudioFilter = "highpass=f=200, lowpass=f=3000, anequalizer=c0 f=2500 w=1000 g=3 t=1";

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

		public (string Voice, int Speed) GetEffectiveSynthesisProfile(string voice, int speed)
		{
			var invocation = GetEspeakInvocation(voice, speed);
			return (invocation.Voice, invocation.Speed);
		}

		private async Task RunEspeakAsync(string text, string voice, int speed, string outputFilePath, CancellationToken cancellationToken)
		{
			var startInfo = CreateEspeakStartInfo(voice, speed, outputFilePath);
			await RunProcessAsync(startInfo, text, "eSpeak NG", cancellationToken);
		}

		private ProcessStartInfo CreateEspeakStartInfo(string voice, int speed, string outputFilePath)
		{
			var invocation = GetEspeakInvocation(voice, speed);
			var startInfo = CreateStartInfo(_options.EspeakExecutable, redirectStandardInput: true);
			startInfo.ArgumentList.Add("--stdin");
			startInfo.ArgumentList.Add("-w");
			startInfo.ArgumentList.Add(outputFilePath);
			startInfo.ArgumentList.Add("-v");
			startInfo.ArgumentList.Add(invocation.Voice);
			startInfo.ArgumentList.Add("-s");
			startInfo.ArgumentList.Add(invocation.Speed.ToString(CultureInfo.InvariantCulture));

			if (invocation.Pitch.HasValue)
			{
				startInfo.ArgumentList.Add("-p");
				startInfo.ArgumentList.Add(invocation.Pitch.Value.ToString(CultureInfo.InvariantCulture));
			}

			if (invocation.WordGap.HasValue)
			{
				startInfo.ArgumentList.Add("-g");
				startInfo.ArgumentList.Add(invocation.WordGap.Value.ToString(CultureInfo.InvariantCulture));
			}

			return startInfo;
		}

		private static EspeakInvocation GetEspeakInvocation(string voice, int speed)
		{
			// English playback uses a fixed MBROLA telephony profile.
			// Other languages continue to use their current eSpeak-NG voice and requested speed.
			return IsEnglishVoice(voice)
				? new EspeakInvocation(MbrolaEnglishVoice, MbrolaEnglishSpeed, MbrolaEnglishPitch, MbrolaEnglishWordGap)
				: new EspeakInvocation(voice, speed, null, null);
		}

		private static bool IsEnglishVoice(string voice)
		{
			if (string.IsNullOrWhiteSpace(voice))
			{
				return false;
			}

			var trimmedVoice = voice.Trim();
			var variantSeparatorIndex = trimmedVoice.IndexOf('+');
			var baseVoice = variantSeparatorIndex <= 0 ? trimmedVoice : trimmedVoice[..variantSeparatorIndex];

			return string.Equals(baseVoice, MbrolaEnglishVoice, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(baseVoice, "en", StringComparison.OrdinalIgnoreCase)
				|| baseVoice.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
		}

		private async Task RunFfmpegAsync(string inputFilePath, string outputFilePath, CancellationToken cancellationToken)
		{
			var startInfo = CreateFfmpegStartInfo(inputFilePath, outputFilePath);
			await RunProcessAsync(startInfo, null, "ffmpeg", cancellationToken);
		}

		private ProcessStartInfo CreateFfmpegStartInfo(string inputFilePath, string outputFilePath)
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
			startInfo.ArgumentList.Add("-af");
			startInfo.ArgumentList.Add(TelephoneAudioFilter);
			startInfo.ArgumentList.Add(outputFilePath);
			return startInfo;
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

		private sealed record EspeakInvocation(string Voice, int Speed, int? Pitch, int? WordGap);
	}
}
