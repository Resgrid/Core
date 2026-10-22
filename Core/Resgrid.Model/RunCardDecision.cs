using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// The pinned decision of a conditional run card level for one call (append-only; the latest row per call, card and
	/// level is the active pin). Re-evaluating a call in progress must not re-walk the graph with the dispatched units
	/// missing from the pool, so add-resources requests use the pinned package snapshot instead. Separate from
	/// RunCardActivations, which audits auto-dispatch and escalation: a pin records which branch the proposal took, not
	/// what the dispatcher accepted.
	/// </summary>
	[Table("RunCardDecisions")]
	public class RunCardDecision : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardDecisionId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int CallId { get; set; }

		[Required]
		public int RunCardId { get; set; }

		public int AlarmLevel { get; set; }

		[MaxLength(36)]
		public string LeafNodeKey { get; set; }

		[MaxLength(200)]
		public string LeafLabel { get; set; }

		public int? ResponsePackageId { get; set; }

		/// <summary>The outcome's rows as resolved at decision time (JSON list of ResolvedRequirementRow).</summary>
		public string PackageSnapshotJson { get; set; }

		/// <summary>The decision path (JSON RunCardDecisionExplanation). Holds node labels, results and seconds; never intake answers.</summary>
		public string PathJson { get; set; }

		/// <summary>A <see cref="RunCardDecisionSources"/> value.</summary>
		public int Source { get; set; }

		public DateTime CreatedOn { get; set; }

		[MaxLength(450)]
		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardDecisionId; }
			set { RunCardDecisionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardDecisions";

		[NotMapped]
		public string IdName => "RunCardDecisionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
