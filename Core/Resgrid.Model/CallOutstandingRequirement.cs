using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// A required resource that could not be sent as configured (a substitute went in its place, or nothing could be
	/// sent), kept on the call so dispatchers see it is still awaited. It clears only when a matching resource is
	/// dispatched to the call, a person dismisses it, or a person closes the call, never on a timer, and automatic call
	/// close skips a call that still holds one.
	/// </summary>
	[Table("CallOutstandingRequirements")]
	[ProtoContract]
	public class CallOutstandingRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallOutstandingRequirementId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(3)]
		public int CallId { get; set; }

		/// <summary>What is awaited, as dispatchers read it ("MUG", "Ladder or Quint").</summary>
		[MaxLength(200)]
		[ProtoMember(4)]
		public string Label { get; set; }

		/// <summary>Snapshot of the row's targets (JSON list of RunCardTarget), so a later package edit changes nothing here.</summary>
		[ProtoMember(5)]
		public string TargetsJson { get; set; }

		[ProtoMember(6)]
		public int RequiredCount { get; set; }

		[ProtoMember(7)]
		public int ResolvedCount { get; set; }

		[ProtoMember(8)]
		public int StatusFilter { get; set; }

		[MaxLength(200)]
		[ProtoMember(9)]
		public string StatusBaseTypes { get; set; }

		[ProtoMember(10)]
		public int? ResponseCodeId { get; set; }

		[MaxLength(16)]
		[ProtoMember(11)]
		public string ResponseCode { get; set; }

		[ProtoMember(12)]
		public int? RunCardId { get; set; }

		[ProtoMember(13)]
		public int AlarmLevel { get; set; }

		[ProtoMember(14)]
		public int? ResponsePackageRowId { get; set; }

		/// <summary>What was sent instead, for the waiting list ("PIT P-21"); null when nothing could be sent.</summary>
		[MaxLength(200)]
		[ProtoMember(15)]
		public string SentInstead { get; set; }

		[ProtoMember(16)]
		public DateTime CreatedOn { get; set; }

		[MaxLength(450)]
		[ProtoMember(17)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(18)]
		public DateTime? ResolvedOn { get; set; }

		[MaxLength(450)]
		[ProtoMember(19)]
		public string ResolvedByUserId { get; set; }

		/// <summary>An <see cref="OutstandingRequirementResolutions"/> value (0 = still open).</summary>
		[ProtoMember(20)]
		public int Resolution { get; set; }

		[MaxLength(500)]
		[ProtoMember(21)]
		public string ResolutionNote { get; set; }

		[ProtoMember(22)]
		public int? ResolvedByUnitId { get; set; }

		/// <summary>Set by the automatic dispatch claim so two freed units cannot both be sent (and one unit not to two calls).</summary>
		[ProtoMember(23)]
		public DateTime? ClaimedOn { get; set; }

		[ProtoMember(24)]
		public int? ClaimedByUnitId { get; set; }

		[NotMapped]
		public bool IsOpen => !ResolvedOn.HasValue && Resolution == (int)OutstandingRequirementResolutions.Open;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallOutstandingRequirementId; }
			set { CallOutstandingRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallOutstandingRequirements";

		[NotMapped]
		public string IdName => "CallOutstandingRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}
}
