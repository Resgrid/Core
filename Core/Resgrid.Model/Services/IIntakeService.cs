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
		/// Intake messages this calendar month (UTC) that reached the department on the transport (duplicates and blocked senders
		/// excluded); for Voice, the voice intake sessions started this month (each costs carrier minutes whether or not a call came of
		/// it). A caller who never pressed 1 at the gate (M0326) started no session, so robocalls do not use up the limit.
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
	/// Spam handling for intake (call-intake-and-ai-destinations-plan.md §9, M0326): the department's blocked-sender list, a
	/// dispatcher's "dismiss as spam" on a pending intake call, and the screening labels that keep flagged calls out of the dispatcher
	/// desk's offers. Nothing here deletes a call or a message; a dismissed call is cancelled and its sender optionally blocked.
	/// </summary>
	public interface IIntakeScreeningService
	{
		/// <summary>
		/// Cancels the department's pending intake call <paramref name="callId"/>, closes its SMS conversation without texting the
		/// sender, marks it <see cref="IntakeScreenings.DismissedAsSpam"/> and, when <paramref name="blockSender"/>, blocks its sender for
		/// IntakeConfig.SpamBlockDays (0 = until lifted). <paramref name="userId"/> must already be allowed to close the call.
		/// </summary>
		Task<IntakeDismissResult> DismissAsSpamAsync(int departmentId, int callId, string userId, bool blockSender, CancellationToken cancellationToken = default);

		/// <summary>The department's block that applies to <paramref name="sender"/> now, else null (never throws for a blank sender).</summary>
		Task<IntakeBlockedSender> GetBlockAsync(int departmentId, string sender);

		/// <summary>The department's standing blocks (expired ones included until purged), newest first.</summary>
		Task<List<IntakeBlockedSender>> GetBlockedSendersAsync(int departmentId);

		/// <summary>Lifts a block early. False when there is no standing block with that id in the department.</summary>
		Task<bool> UnblockAsync(int departmentId, string intakeBlockedSenderId, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records Enhanced AI's reading of an intake call (M0326): <paramref name="likelyNotIncident"/> sets
		/// <see cref="IntakeScreenings.LikelyNotIncident"/>, false clears it. Never overrides a repeat-sender or dismissal screening.
		/// True when the screening changed.
		/// </summary>
		Task<bool> SetAiScreeningAsync(int departmentId, int callId, bool likelyNotIncident, CancellationToken cancellationToken = default);

		/// <summary>The screening of each of these intake calls (unscreened and non-intake calls are omitted).</summary>
		Task<Dictionary<int, IntakeScreenings>> GetScreeningsAsync(int departmentId, IEnumerable<int> callIds);
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
