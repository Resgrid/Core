using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>Who a work assignment names (RMS plan section 5.2 RmsRecordWorkAssignment).</summary>
	public enum RmsWorkAssigneeKind
	{
		Person = 1,
		Unit = 2,
		Group = 3,
		/// <summary>A role on the active command structure of the Record's Call (IC app).</summary>
		CommandRole = 4,
		/// <summary>A dispatch-console role (Dispatch app).</summary>
		DispatchRole = 5
	}

	public enum RmsWorkAssignmentState
	{
		Open = 1,
		Acknowledged = 2,
		Completed = 3,
		Cancelled = 4
	}

	/// <summary>Why the work exists; a closed set so queues and dashboards can count it.</summary>
	public static class RmsWorkAssignmentPurposes
	{
		public const string Complete = "complete";
		public const string Review = "review";
		public const string Correct = "correct";
		public const string Attach = "attach";
		public const string Acknowledge = "acknowledge";
		public static readonly IReadOnlyList<string> All = new[] { Complete, Review, Correct, Attach, Acknowledge };
		public static bool IsKnown(string purpose) => purpose != null && Array.IndexOf((string[])All, purpose.Trim().ToLowerInvariant()) >= 0;
	}

	/// <summary>
	/// An optional person, unit/team, command-role or dispatch-role assignment on a Record with purpose, due /
	/// acknowledged / completed state, source context and audit (RMS plan section 5.2, RMS-1D). It narrows a
	/// field work queue; it never replaces live authorization, so a queue row the assignee may no longer read
	/// is withheld at read time rather than trusted because it was assigned.
	/// </summary>
	public class RmsRecordWorkAssignment : IEntity
	{
		public string RmsRecordWorkAssignmentId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public string RecordId { get; set; }

		/// <summary><see cref="RmsWorkAssigneeKind"/>.</summary>
		public int AssigneeKind { get; set; }

		public string AssigneeUserId { get; set; }

		public int? AssigneeUnitId { get; set; }

		public int? AssigneeGroupId { get; set; }

		/// <summary>Command or dispatch role name for the role kinds; null otherwise.</summary>
		public string AssigneeRole { get; set; }

		/// <summary><see cref="RmsWorkAssignmentPurposes"/>.</summary>
		public string Purpose { get; set; }

		public string Note { get; set; }

		/// <summary>Safe source context (call / unit / group / command identifiers only) the assignment was made in.</summary>
		public string SourceContextJson { get; set; }

		public DateTime? DueOn { get; set; }

		/// <summary><see cref="RmsWorkAssignmentState"/>.</summary>
		public int State { get; set; }

		public DateTime? AcknowledgedOn { get; set; }

		public string AcknowledgedByUserId { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string CompletedByUserId { get; set; }

		public DateTime? CancelledOn { get; set; }

		public string CancelledByUserId { get; set; }

		public string CancelReason { get; set; }

		/// <summary><see cref="RmsOriginClient"/> of the client that created the assignment (safe audit metadata).</summary>
		public int OriginClient { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		[Key]
		[Required]
		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		public bool IsOpen => State == (int)RmsWorkAssignmentState.Open || State == (int)RmsWorkAssignmentState.Acknowledged;

		[NotMapped]
		public object IdValue
		{
			get { return RmsRecordWorkAssignmentId; }
			set { RmsRecordWorkAssignmentId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsRecordWorkAssignments";

		[NotMapped]
		public string IdName => "RmsRecordWorkAssignmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>Input for creating a work assignment.</summary>
	public class RecordWorkAssignmentInput
	{
		public string RecordId { get; set; }
		public RmsWorkAssigneeKind AssigneeKind { get; set; } = RmsWorkAssigneeKind.Person;
		public string AssigneeUserId { get; set; }
		public int? AssigneeUnitId { get; set; }
		public int? AssigneeGroupId { get; set; }
		public string AssigneeRole { get; set; }
		public string Purpose { get; set; } = RmsWorkAssignmentPurposes.Complete;
		public string Note { get; set; }
		public DateTime? DueOn { get; set; }
		public FieldRecordContext SourceContext { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
	}
}
