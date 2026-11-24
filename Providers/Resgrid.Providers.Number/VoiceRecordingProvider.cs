using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Exceptions;
using CallRecordingResource = Twilio.Rest.Api.V2010.Account.Call.RecordingResource;
using AccountRecordingResource = Twilio.Rest.Api.V2010.Account.RecordingResource;

namespace Resgrid.Providers.NumberProvider
{
	/// <summary>
	/// Twilio call recording for voice intake (M0332). The recording is started with the REST API on the live call (both tracks, one
	/// channel, silence trimmed) and its status callback reports "completed" or "absent"; the MP3 is fetched from the account's
	/// Recordings resource with basic auth and deleted once Resgrid holds it.
	/// </summary>
	public class VoiceRecordingProvider : IVoiceRecordingProvider
	{
		private const string ApiBaseUrl = "https://api.twilio.com/2010-04-01";

		// One client for the process (no socket exhaustion); the Authorization header is per request, so a redirect to the media store
		// (which HttpClient follows without it) never carries the account credentials.
		private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(90) };

		public async Task<string> StartCallRecordingAsync(string callSid, string statusCallbackUrl, CancellationToken cancellationToken = default)
		{
			if (!IsSid(callSid, "CA") || !Uri.TryCreate(statusCallbackUrl, UriKind.Absolute, out var callback) || !Configured())
				return null;

			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			var start = CallRecordingResource.CreateAsync(
				pathCallSid: callSid.Trim(),
				recordingStatusCallbackEvent: new List<string> { "completed", "absent" },
				recordingStatusCallback: callback,
				recordingStatusCallbackMethod: Twilio.Http.HttpMethod.Post,
				trim: "trim-silence",
				recordingChannels: "mono",
				recordingTrack: "both");

			try
			{
				// The SDK call takes no token: stop waiting on cancel (the gate must answer the caller) and let the callback bind it.
				var recording = await start.WaitAsync(cancellationToken);
				return recording?.Sid;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// The start keeps running unobserved: observe its failure so it never surfaces as an unobserved task exception.
				_ = start.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
				throw;
			}
			catch (ApiException ex)
			{
				// Most often the caller hung up while the gate answered: the call is no longer live.
				Resgrid.Framework.Logging.LogError($"Voice intake recording could not start on call {callSid}: Twilio {ex.Code} {ex.Message}");
				return null;
			}
		}

		public async Task<VoiceRecordingDownload> DownloadRecordingAsync(string recordingSid, int maxBytes, CancellationToken cancellationToken = default)
		{
			if (!IsSid(recordingSid, "RE"))
				return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.NotFound };
			if (!Configured())
				return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable };

			using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/Accounts/{Config.NumberProviderConfig.TwilioAccountSid}/Recordings/{recordingSid.Trim()}.mp3");
			request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
				Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Config.NumberProviderConfig.TwilioAccountSid}:{Config.NumberProviderConfig.TwilioAuthToken}")));

			try
			{
				using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
					return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.NotFound };
				if (!response.IsSuccessStatusCode)
				{
					Resgrid.Framework.Logging.LogError($"Voice intake recording {recordingSid} download answered {(int)response.StatusCode}.");
					return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable };
				}

				var limit = Math.Max(1, maxBytes);
				if (response.Content.Headers.ContentLength > limit)
					return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.TooLarge };

				await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
				var body = await ReadBodyAsync(stream, limit, BodyTimeout, cancellationToken);
				if (body.Status == VoiceRecordingDownloadStatuses.Unavailable)
					Resgrid.Framework.Logging.LogError($"Voice intake recording {recordingSid} download stalled or was empty.");
				return body;
			}
			catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
			{
				Resgrid.Framework.Logging.LogError($"Voice intake recording {recordingSid} could not be downloaded: {ex.Message}");
				return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable };
			}
		}

		/// <summary>
		/// How long the MP3 body may take after the headers. HttpClient.Timeout stops at the headers (ResponseHeadersRead), so a media server
		/// that sends them and then stalls would otherwise hold worker 80's whole pass (the rest of the batch, the purge, the number lifecycle).
		/// </summary>
		public static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(120);

		/// <summary>
		/// Reads at most <paramref name="limit"/> + 1 bytes of the recording (a missing or wrong Content-Length never buffers more than that),
		/// within <paramref name="timeout"/>: a body that stalls is Unavailable (worker 80 retries it). The caller's own cancellation still throws.
		/// </summary>
		public static async Task<VoiceRecordingDownload> ReadBodyAsync(Stream stream, int limit, TimeSpan timeout, CancellationToken cancellationToken)
		{
			using var body = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			body.CancelAfter(timeout);
			try
			{
				using var buffer = new MemoryStream();
				var chunk = new byte[81920];
				int read;
				while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), body.Token)) > 0)
				{
					buffer.Write(chunk, 0, read);
					if (buffer.Length > limit)
						return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.TooLarge };
				}

				if (buffer.Length == 0)
					return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable };

				return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Downloaded, Data = buffer.ToArray(), Extension = ".mp3" };
			}
			catch (Exception ex) when ((ex is OperationCanceledException || ex is IOException) && !cancellationToken.IsCancellationRequested)
			{
				// The body timeout (or the connection dropping mid-body), not the caller.
				return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable };
			}
		}

		public async Task<bool> DeleteRecordingAsync(string recordingSid, CancellationToken cancellationToken = default)
		{
			if (!IsSid(recordingSid, "RE"))
				return true;
			if (!Configured() || cancellationToken.IsCancellationRequested)
				return false;

			try
			{
				TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
				return await AccountRecordingResource.DeleteAsync(recordingSid.Trim());
			}
			catch (ApiException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
			{
				return true;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, $"Voice intake recording {recordingSid} could not be deleted at Twilio.");
				return false;
			}
		}

		private static bool Configured() =>
			!string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioAccountSid) && !string.IsNullOrWhiteSpace(Config.NumberProviderConfig.TwilioAuthToken);

		/// <summary>A Twilio sid: the two-letter prefix and 32 hex digits (never a path an attacker could steer).</summary>
		public static bool IsSid(string value, string prefix)
		{
			if (string.IsNullOrWhiteSpace(value))
				return false;

			var sid = value.Trim();
			if (sid.Length != 34 || !sid.StartsWith(prefix, StringComparison.Ordinal))
				return false;

			for (var i = 2; i < sid.Length; i++)
			{
				if (!Uri.IsHexDigit(sid[i]))
					return false;
			}

			return true;
		}
	}
}
