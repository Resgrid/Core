using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Diagnostics;
using System.Globalization;

namespace Resgrid.Web.Tts.Services
{
	public sealed class AudioProcessingService : IAudioProcessingService
	{
		// Speed reference point: 175 wpm (eSpeak scale) ≈ length-scale 1.0 (Piper).
		private const float SpeedReferenceWpm = 175f;
		private const float MinLengthScale = 0.25f;
		private const float MaxLengthScale = 3.0f;
		private const string DefaultEnglishModel = "en_US-norman-medium.onnx";
		private const string TelephoneAudioFilter = "highpass=f=200, lowpass=f=3000, anequalizer=c0 f=2500 w=1000 g=3 t=1";

		/// <summary>
		/// Maps the eSpeak-style voice identifier (base language code) to a Piper
		/// model filename.  Languages without a matching Piper model fall back to
		/// the default en-US model.
		/// </summary>
		private static readonly Dictionary<string, string> VoiceModelMap = new(StringComparer.OrdinalIgnoreCase)
		{
			// English variants
			{ "en-us", "en_US-norman-medium.onnx" },
			{ "en", "en_GB-alan-medium.onnx" },
			{ "en-gb", "en_GB-alan-medium.onnx" },
			{ "en-gb-x-rp", "en_GB-semaine-medium.onnx" },
			{ "en-gb-scotland", "en_GB-alan-medium.onnx" },
			{ "en-gb-x-gbclan", "en_GB-northern_english_male-medium.onnx" },
			{ "en-gb-x-gbcwmd", "en_GB-alan-medium.onnx" },
			{ "en-029", "en_US-norman-medium.onnx" },
			{ "mb-us1", "en_US-norman-medium.onnx" },

			// Western European
			{ "fr", "fr_FR-siwis-medium.onnx" },
			{ "fr-be", "fr_FR-siwis-medium.onnx" },
			{ "fr-ch", "fr_FR-siwis-medium.onnx" },
			{ "de", "de_DE-thorsten-medium.onnx" },
			{ "es", "es_ES-sharvard-medium.onnx" },
			{ "es-419", "es_MX-claude-high.onnx" },
			{ "it", "it_IT-paola-medium.onnx" },
			{ "nl", "nl_NL-mls-medium.onnx" },
			{ "pt", "pt_BR-faber-medium.onnx" },
			{ "pt-br", "pt_BR-faber-medium.onnx" },

			// Nordic
			{ "da", "da_DK-talesyntese-medium.onnx" },
			{ "sv", "sv_SE-nst-medium.onnx" },
			{ "nb", "no_NO-talesyntese-medium.onnx" },
			{ "fi", "fi_FI-harri-medium.onnx" },
			{ "is", "is_IS-ugla-medium.onnx" },

			// Slavic
			{ "pl", "pl_PL-gosia-medium.onnx" },
			{ "uk", "uk_UA-ukrainian_tts-medium.onnx" },
			{ "ru", "ru_RU-ruslan-medium.onnx" },
			{ "cs", "cs_CZ-jirka-medium.onnx" },
			{ "sk", "sk_SK-lili-medium.onnx" },
			{ "sl", "sl_SI-artur-medium.onnx" },
			{ "sr", "sr_RS-serbski_institut-medium.onnx" },

			// Other European
			{ "hu", "hu_HU-anna-medium.onnx" },
			{ "el", "el_GR-rapunzelina-medium.onnx" },
			{ "ro", "ro_RO-mihai-medium.onnx" },
			{ "tr", "tr_TR-dfki-medium.onnx" },
			{ "sq", "sq_AL-edon-medium.onnx" },
			{ "eu", "eu_ES-antton-medium.onnx" },
			{ "bg", "bg_BG-dimitar-medium.onnx" },
			{ "cy", "cy_GB-gwryw_gogleddol-medium.onnx" },
			{ "lb", "lb_LU-marylux-medium.onnx" },
			{ "lv", "lv_LV-aivars-medium.onnx" },
			{ "ca", "ca_ES-upc_ona-medium.onnx" },

			// Baltic/Uralic
			{ "et", "fi_FI-harri-medium.onnx" },

			// Middle Eastern / Central Asian
			{ "ar", "ar_JO-kareem-medium.onnx" },
			{ "fa", "fa_IR-amir-medium.onnx" },
			{ "fa-latn", "fa_IR-amir-medium.onnx" },
			{ "ka", "ka_GE-natia-medium.onnx" },
			{ "kk", "kk_KZ-issai-high.onnx" },
			{ "ku", "ku_TR-berfin_renas-medium.onnx" },
			{ "ur", "ur_PK-fasih-medium.onnx" },

			// South Asian
			{ "hi", "hi_IN-pratham-medium.onnx" },
			{ "ml", "ml_IN-arjun-medium.onnx" },
			{ "te", "te_IN-maya-medium.onnx" },
			{ "ne", "ne_NP-google-medium.onnx" },

			// Southeast Asian
			{ "vi", "vi_VN-vais1000-medium.onnx" },
			{ "vi-vn-x-central", "vi_VN-vais1000-medium.onnx" },
			{ "vi-vn-x-south", "vi_VN-vais1000-medium.onnx" },
			{ "id", "id_ID-news_tts-medium.onnx" },

			// East Asian
			{ "zh", "zh_CN-huayan-medium.onnx" },
			{ "cmn", "zh_CN-huayan-medium.onnx" },
			{ "yue", "zh_CN-huayan-medium.onnx" },
			{ "hak", "zh_CN-huayan-medium.onnx" },

			// African
			{ "sw", "sw_CD-lanfrica-medium.onnx" },
		};

		private readonly TtsOptions _options;
		private readonly ILogger<AudioProcessingService> _logger;
		private readonly ITextPreprocessor _textPreprocessor;

		public AudioProcessingService(
			IOptions<TtsOptions> options,
			ILogger<AudioProcessingService> logger,
			ITextPreprocessor textPreprocessor)
		{
			_options = options.Value;
			_logger = logger;
			_textPreprocessor = textPreprocessor;
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
				var preprocessedText = _textPreprocessor.Preprocess(text, voice);
				await RunPiperAsync(preprocessedText, voice, speed, rawFilePath, cancellationToken);
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
			var invocation = GetPiperInvocation(voice, speed);
			return (invocation.ModelName, speed);
		}

		/// <summary>
		/// Resolves a voice identifier plus speed into a Piper model filename and length-scale,
		/// with the effective model name used for cache-key derivation.
		/// </summary>
		private static PiperInvocation GetPiperInvocation(string voice, int speed)
		{
			var modelName = ResolveModelName(voice);
			var lengthScale = ComputeLengthScale(speed);
			return new PiperInvocation(modelName, lengthScale);
		}

		private static string ResolveModelName(string voice)
		{
			if (string.IsNullOrWhiteSpace(voice))
			{
				return DefaultEnglishModel;
			}

			var trimmed = voice.Trim();
			var variantSeparatorIndex = trimmed.IndexOf('+');
			var baseVoice = variantSeparatorIndex <= 0 ? trimmed : trimmed[..variantSeparatorIndex];

			if (VoiceModelMap.TryGetValue(baseVoice, out var modelName))
			{
				return modelName;
			}

			// For unknown languages, fall back to the default en-US model rather
			// than failing outright — the caller can still receive intelligible audio
			// even if the accent isn't ideal.
			return DefaultEnglishModel;
		}

		/// <summary>
		/// Converts the caller-supplied wpm speed value to a Piper length-scale.
		/// A higher wpm (faster speech) maps to a lower length-scale.
		/// </summary>
		private static float ComputeLengthScale(int speed)
		{
			if (speed <= 0)
			{
				return 1.0f;
			}

			var lengthScale = SpeedReferenceWpm / speed;
			return Math.Clamp(lengthScale, MinLengthScale, MaxLengthScale);
		}

		private async Task RunPiperAsync(string text, string voice, int speed, string outputFilePath, CancellationToken cancellationToken)
		{
			var startInfo = CreatePiperStartInfo(voice, speed, outputFilePath);
			await RunProcessAsync(startInfo, text, "Piper TTS", cancellationToken);
		}

		private ProcessStartInfo CreatePiperStartInfo(string voice, int speed, string outputFilePath)
		{
			var invocation = GetPiperInvocation(voice, speed);
			var modelPath = Path.Combine(_options.PiperModelDirectory, invocation.ModelName);

			var startInfo = CreateStartInfo(_options.PiperExecutable, redirectStandardInput: true);
			startInfo.ArgumentList.Add("--model");
			startInfo.ArgumentList.Add(modelPath);
			startInfo.ArgumentList.Add("--output_file");
			startInfo.ArgumentList.Add(outputFilePath);
			startInfo.ArgumentList.Add("--length-scale");
			startInfo.ArgumentList.Add(invocation.LengthScale.ToString("0.00", CultureInfo.InvariantCulture));

			return startInfo;
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

		private sealed record PiperInvocation(string ModelName, float LengthScale);
	}
}