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
