using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>The guided questions of Enhanced AI voice intake, in order (persisted as the session step).</summary>
	public enum VoiceIntakeSteps
	{
		Greeting = 0,

		/// <summary>"What is the emergency or request?"</summary>
		Nature = 1,

		/// <summary>"What is the address or location?"</summary>
		Location = 2,

		/// <summary>"What is your name?"</summary>
		CallerName = 3,

		/// <summary>"What number can we call you back on?" (the caller id is offered).</summary>
		Callback = 4,

		/// <summary>The pending call was created and the caller was told.</summary>
		Completed = 5,

		/// <summary>The caller hung up or nothing usable was said.</summary>
		Abandoned = 6
	}

	/// <summary>Where a voice intake call recording is (M0332; persisted on VoiceIntakeSessions.RecordingState, append only).</summary>
	public enum VoiceIntakeRecordingStates
	{
		/// <summary>Not recorded (recording off for the number, or the caller never accepted it).</summary>
		None = 0,

		/// <summary>Recording was started at the carrier after the caller accepted; it ends when the call does.</summary>
		Requested = 1,

		/// <summary>The carrier finished the recording; it is waiting to be attached to the call.</summary>
		Completed = 2,

		/// <summary>The recording is a file on the call (<see cref="VoiceIntakeSession.RecordingCallAttachmentId"/>).</summary>
		Attached = 3,

		/// <summary>Nothing to keep: no call was created (the caller hung up first), the recording was empty, or it never started.</summary>
		Discarded = 4,

		/// <summary>It could not be attached after IntakeConfig.VoiceRecordingMaxAttempts; the carrier copy is kept until the session purge.</summary>
		Failed = 5
	}

	/// <summary>
	/// One phone call taken by Enhanced AI voice intake (M0323), keyed by the carrier's call id so Twilio's webhook retries
	/// resume the same session. Answers are transcripts (protected content): kept only until the pending call is created and
	/// purged after <c>IntakeConfig.VoiceSessionRetentionHours</c> by worker 80.
	/// </summary>
	[Table("VoiceIntakeSessions")]
	public class VoiceIntakeSession : IEntity
	{
		[Key]
		[Required]
		public string VoiceIntakeSessionId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Twilio CallSid.</summary>
		[MaxLength(64)]
		public string ProviderCallId { get; set; }

		[MaxLength(32)]
		public string FromNumber { get; set; }

		[MaxLength(32)]
		public string ToNumber { get; set; }

		/// <summary>A <see cref="VoiceIntakeSteps"/> value.</summary>
		public int Step { get; set; }

		/// <summary>Answers by step name as JSON; cleared when the call is created.</summary>
		public string AnswersJson { get; set; }

		/// <summary>Re-asks of the current step (no speech heard / not understood).</summary>
		public int Retries { get; set; }

		public int? CallId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>
		/// Call recording (M0332): a <see cref="VoiceIntakeRecordingStates"/> value. Recording starts only after the caller pressed 1 at a gate
		/// that said the call is recorded, on a number with <see cref="DepartmentPhoneNumber.VoiceRecordingEnabled"/>.
		/// </summary>
		public int RecordingState { get; set; }

		/// <summary>Twilio RecordingSid ("RE…"); null until the carrier confirmed the recording (start answer or status callback).</summary>
		[MaxLength(64)]
		public string RecordingSid { get; set; }

		public int? RecordingDurationSeconds { get; set; }

		/// <summary>Attach attempts made (the callback's and worker 80's); the recording is Failed after IntakeConfig.VoiceRecordingMaxAttempts.</summary>
		public int RecordingAttempts { get; set; }

		/// <summary>Lease of the attempt in progress, so the callback and worker 80 never attach the same recording at once.</summary>
		public DateTime? RecordingClaimedOn { get; set; }

		/// <summary>The call file the recording became.</summary>
		public int? RecordingCallAttachmentId { get; set; }

		/// <summary>When the carrier's copy was deleted (after it was attached or discarded); null while Twilio still holds it.</summary>
		public DateTime? RecordingCarrierDeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return VoiceIntakeSessionId; }
			set { VoiceIntakeSessionId = (string)value; }
		}

		[NotMapped]
		public string TableName => "VoiceIntakeSessions";

		[NotMapped]
		public string IdName => "VoiceIntakeSessionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
