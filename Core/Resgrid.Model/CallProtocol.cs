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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call", "Protocol", "IsRemoved" };
	}
}
