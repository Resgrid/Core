using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("CallProtocols")]
	public class CallProtocol : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallProtocolId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ForeignKey("Protocol"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(3)]
		public int DispatchProtocolId { get; set; }

		public virtual DispatchProtocol Protocol { get; set; }

		[ProtoMember(4)]
		public int Score { get; set; }

		[ProtoMember(5)]
		public int Trigger { get; set; }

		[ProtoMember(6)]
		public string Data { get; set; }

		/// <summary>
		/// What the dispatcher was asked and answered, in words (<see cref="CallProtocolAnswers"/> as JSON): each question's text,
		/// the answer chosen, its weight, and the protocol's minimum weight at the time. Ids alone stop meaning anything the first
		/// time the protocol is edited. Null when the protocol was attached without answers (no questions, or an older client).
		/// </summary>
		[ProtoMember(7)]
		public string AnswersJson { get; set; }

		/// <summary>How the protocol came onto the call (<see cref="CallProtocolSources"/>). Null for rows recorded before M0336.</summary>
		[ProtoMember(8)]
		public int? Source { get; set; }

		/// <summary>
		/// SHA-256 (hex) of the protocol text as dispatched (<see cref="ProtocolContentHash"/>); the text itself is kept once per
		/// protocol and hash in DispatchProtocolSnapshots. Null for rows recorded before M0336.
		/// </summary>
		[ProtoMember(9)]
		public string ContentHash { get; set; }

		[ProtoMember(10)]
		public DateTime? AttachedOn { get; set; }

		[ProtoMember(11)]
		public string AttachedByUserId { get; set; }

		/// <summary>Set when the protocol was taken off the call. The row stays, with who removed it and why.</summary>
		[ProtoMember(12)]
		public DateTime? RemovedOn { get; set; }

		[ProtoMember(13)]
		public string RemovedByUserId { get; set; }

		[ProtoMember(14)]
		public string RemovedReason { get; set; }

		/// <summary>
		/// The protocol version the call cites (P1 §4 "calls cite versions, never live rows"), set at attach from the protocol's
		/// version in force. M0343 filled it for rows recorded before P1: from the P0 snapshot hash where there was one (the
		/// matching version, or a Legacy version for text that differs), else version 1.0. Null only for a protocol without
		/// any version.
		/// </summary>
		[ProtoMember(15)]
		public int? ProtocolVersionId { get; set; }

		/// <summary>
		/// P3 step-through: the protocol run on the call like a checklist (<see cref="ProtocolStepLog"/> as JSON), each step timestamped.
		/// Read with the row; written only by <see cref="Repositories.ICallProtocolsRepository.TryUpdateStepLogAsync"/> (never by the call's
		/// save cascade, which would write back a copy loaded before the last step), hence not mapped for the ORM's writes.
		/// </summary>
		[NotMapped]
		[ProtoMember(16)]
		public string StepLogJson { get; set; }

		/// <summary>Bumped on every step-log write; a write against an older value is retried on the fresh log.</summary>
		[NotMapped]
		[ProtoMember(17)]
		public int StepLogRevision { get; set; }

		/// <summary>
		/// P4: the determinant in force for the call (the case flow's, or the dispatcher's override); null when the protocol has no case
		/// flow or the case did not reach an outcome. Run card ProtocolOutcome conditions and the dispatch texts read it.
		/// </summary>
		[ProtoMember(18)]
		public string Determinant { get; set; }

		/// <summary>P4: why the dispatcher overrode the computed determinant (required for an override); null when not overridden.</summary>
		[ProtoMember(19)]
		public string OverrideReason { get; set; }

		/// <summary>P4: the case record (<see cref="CallProtocolCase"/> as JSON): the path in words, the outcome, the override, the source, caller texts.</summary>
		[ProtoMember(20)]
		public string CaseJson { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsRemoved => RemovedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallProtocolId; }
			set { CallProtocolId = (int)value; }
		}

		
		[NotMapped]
		public string TableName => "CallProtocols";

		[NotMapped]
		public string IdName => "CallProtocolId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call", "Protocol", "IsRemoved", "StepLogJson", "StepLogRevision" };
	}
}
