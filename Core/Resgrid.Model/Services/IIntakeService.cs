using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Intake (call-intake-and-ai-destinations-plan.md, workstream C): turns a message that arrived on a department's intake
	/// channel into a PENDING call a dispatcher reviews and sends. Every message is recorded (IntakeMessages) and claimed once per
	/// channel and provider message id; the call is always created deterministically first (template or raw text, D7) and Enhanced
	/// AI enrichment is queued afterwards. Nothing here dispatches, pages or broadcasts.
	/// </summary>
	public interface IIntakeService
	{
		/// <summary>
		/// Resolves the channel, checks the host switch, the department, the Intake.Channels flag, the channel and the sender,
		/// claims the provider message id, creates the pending call and records the outcome. Never throws for a refusal; the
		/// result says what happened and carries the acknowledgement the transport should send (null = none).
		/// </summary>
		Task<IntakeResult> ReceiveAsync(IntakeEnvelope envelope, CancellationToken cancellationToken = default);

		/// <summary>
		/// Intake messages this calendar month (UTC) that reached the department on the transport (duplicates excluded); for Voice,
		/// the voice intake sessions started this month (each costs carrier minutes whether or not a call came of it).
		/// </summary>
		Task<int> GetMonthlyCountAsync(int departmentId, IntakeChannelTypes channelType, DateTime nowUtc);

		/// <summary>Monthly counts per transport for the admin page (types with no traffic are omitted).</summary>
		Task<Dictionary<IntakeChannelTypes, int>> GetMonthlyCountsAsync(int departmentId, DateTime nowUtc);

		/// <summary>Messages received on the channel this calendar month (UTC), duplicates excluded.</summary>
		Task<Dictionary<string, int>> GetMonthlyChannelCountsAsync(int departmentId, DateTime nowUtc);

		/// <summary>The intake channel each of these calls came from (calls that did not come from intake are omitted).</summary>
		Task<Dictionary<int, IntakeCallSource>> GetCallSourcesAsync(int departmentId, IEnumerable<int> callIds);

		/// <summary>Worker 80: deletes IntakeMessages and VoiceIntakeSessions past their retention.</summary>
		Task<IntakePurgeResult> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Enhanced AI voice intake on a department's dedicated number (plan D2): routes the call (member IVR, guided intake, text-only
	/// message), then runs the guided questions Nature → Location → Caller name → Callback with one session per CallSid so Twilio
	/// retries resume, and hands the answers to <see cref="IIntakeService"/>. Returns what to say; the transport renders TwiML.
	/// </summary>
	public interface IVoiceIntakeService
	{
		/// <summary>Who answers a call from <paramref name="fromNumber"/> to the dedicated <paramref name="number"/>.</summary>
		Task<VoiceIntakeRouting> RouteAsync(DepartmentPhoneNumber number, string fromNumber, DateTime nowUtc);

		/// <summary>Starts (or, for the same CallSid, resumes) the guided questions.</summary>
		Task<VoiceIntakeTurn> StartAsync(DepartmentPhoneNumber number, string providerCallId, string fromNumber, CancellationToken cancellationToken = default);

		/// <summary>Records one answer and returns the next question, a re-ask, or the closing prompt once the pending call exists.</summary>
		Task<VoiceIntakeTurn> AnswerAsync(VoiceIntakeAnswer answer, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Turns one voice intake answer into text. The Twilio implementation reads &lt;Gather input="speech"&gt;'s SpeechResult; a
	/// self-hosted Whisper implementation can replace it later without touching the step machine (plan D2).
	/// </summary>
	public interface IVoiceIntakeTranscriber
	{
		Task<VoiceTranscript> TranscribeAsync(VoiceIntakeAnswer answer, CancellationToken cancellationToken = default);
	}
}
