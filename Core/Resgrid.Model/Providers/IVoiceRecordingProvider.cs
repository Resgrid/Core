using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>How a recording download ended.</summary>
	public enum VoiceRecordingDownloadStatuses
	{
		Downloaded = 0,

		/// <summary>The carrier has no such recording (deleted, or it never existed): nothing will ever be fetched.</summary>
		NotFound = 1,

		/// <summary>Larger than the limit asked for; not downloaded.</summary>
		TooLarge = 2,

		/// <summary>Not available now (still being processed, a carrier or network error): try again later.</summary>
		Unavailable = 3
	}

	public class VoiceRecordingDownload
	{
		public VoiceRecordingDownloadStatuses Status { get; set; }

		public byte[] Data { get; set; }

		/// <summary>File extension with the dot (".mp3").</summary>
		public string Extension { get; set; }
	}

	/// <summary>
	/// Carrier call recording for voice intake (M0332). Twilio: the recording is started with the REST API on the live call, ends with
	/// the call, and its status callback (completed / absent) is served by Web.Services <c>TwilioController.VoiceIntakeRecordingCallback</c>.
	/// </summary>
	public interface IVoiceRecordingProvider
	{
		/// <summary>
		/// Starts recording the live call (both directions, one channel) and returns the recording's sid, or null when the carrier refused.
		/// Cancelling stops the wait only; the carrier may still start it (the status callback carries the call's sid).
		/// </summary>
		Task<string> StartCallRecordingAsync(string callSid, string statusCallbackUrl, CancellationToken cancellationToken = default);

		/// <summary>The recording's audio (MP3), unless it is larger than <paramref name="maxBytes"/>.</summary>
		Task<VoiceRecordingDownload> DownloadRecordingAsync(string recordingSid, int maxBytes, CancellationToken cancellationToken = default);

		/// <summary>Deletes the carrier's copy. True when it is gone (deleted now, or already gone).</summary>
		Task<bool> DeleteRecordingAsync(string recordingSid, CancellationToken cancellationToken = default);
	}
}
