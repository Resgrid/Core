using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// The append-only Records activation fact for one department (RMS plan section 4.1). Created by the
	/// activation command inside the same transaction as its audit; nothing else writes
	/// <see cref="ActivatedOn"/>. The legacy Log/UnitLog write guard and the rollback runbook key off
	/// this row, never off a mutable setting or the feature flag alone.
	/// </summary>
	[Table("RmsDepartmentCutovers")]
	public class RmsDepartmentCutover : IEntity
	{
		public int RmsDepartmentCutoverId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		public DateTime ActivatedOn { get; set; }

		public string ActivatedByUserId { get; set; }

		public string Reason { get; set; }

		/// <summary>Legacy Log row count at activation, for the rollback decision frame and reconciliation.</summary>
		public int SourceLegacyLogCount { get; set; }

		public int SourceLegacyUnitLogCount { get; set; }

		public string SourceChecksum { get; set; }

		/// <summary><see cref="RmsDepartmentCutoverState"/>.</summary>
		public int State { get; set; }

		public DateTime? RevertedOn { get; set; }

		public string RevertedByUserId { get; set; }

		/// <summary>The before/after Permission-row table the administrator confirmed (registry section 4.6).</summary>
		public string PermissionMappingJson { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public bool IsActive => State == (int)RmsDepartmentCutoverState.Active;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsDepartmentCutoverId; }
			set { RmsDepartmentCutoverId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RmsDepartmentCutovers";

		[NotMapped]
		public string IdName => "RmsDepartmentCutoverId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>Audited history of a department's cutover row.</summary>
	[Table("RmsDepartmentCutoverEvents")]
	public class RmsDepartmentCutoverEvent : IEntity
	{
		public int RmsDepartmentCutoverEventId { get; set; }

		public int DepartmentId { get; set; }

		public int RmsDepartmentCutoverId { get; set; }

		/// <summary>One of <see cref="RmsDepartmentCutoverEventTypes"/>.</summary>
		public string EventType { get; set; }

		public string ActorUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public string DetailJson { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsDepartmentCutoverEventId; }
			set { RmsDepartmentCutoverEventId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RmsDepartmentCutoverEvents";

		[NotMapped]
		public string IdName => "RmsDepartmentCutoverEventId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
