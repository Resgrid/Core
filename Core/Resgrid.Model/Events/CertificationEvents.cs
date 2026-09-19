using System;

namespace Resgrid.Model.Events
{
	// Workforce & Business Operations plan, Phase D7 (decision 22). Published through IEventAggregator by the
	// certification service (87, 88, 91) and the expiry worker 34 (23, 89, 90, 92, 93). The PersonnelCertification
	// payload keeps its ADP catalog 6 envelopes; the workflow context builder renders them through SafeDisplay.

	/// <summary>Trigger 87: a personnel certification record was created.</summary>
	public class CertificationAddedEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
	}

	/// <summary>Trigger 88: a record was renewed (a new record replaced it or its expiry was extended).</summary>
	public class CertificationRenewedEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public DateTime? PreviousExpiresOn { get; set; }
	}

	/// <summary>Trigger 89: the worker flipped an Active record to Expired.</summary>
	public class CertificationExpiredEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
	}

	/// <summary>Trigger 90: the enforcement pass removed a member from a role after the grace period.</summary>
	public class CertificationRoleRemovedEvent
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public int PersonnelRoleId { get; set; }
		public string RoleName { get; set; }
		public int DepartmentCertificationTypeId { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public DateTime? GraceDeadline { get; set; }
	}

	/// <summary>Trigger 91: a record's status changed (suspend / revoke / reinstate / verify / trainee).</summary>
	public class CertificationStatusChangedEvent
	{
		public int DepartmentId { get; set; }
		public PersonnelCertification Certification { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public int OldStatus { get; set; }
		public int NewStatus { get; set; }
		public string Reason { get; set; }
		public string ChangedByUserId { get; set; }
	}

	/// <summary>Trigger 92 / notification 28: a unit certification reaches a lead day.</summary>
	public class UnitCertificationExpiringEvent
	{
		public int DepartmentId { get; set; }
		public UnitCertification Certification { get; set; }
		public string UnitName { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
		public int DaysUntilExpiry { get; set; }
	}

	/// <summary>Trigger 93 / notification 29: the worker flipped a unit certification to Expired.</summary>
	public class UnitCertificationExpiredEvent
	{
		public int DepartmentId { get; set; }
		public UnitCertification Certification { get; set; }
		public string UnitName { get; set; }
		public string TypeCode { get; set; }
		public string TypeName { get; set; }
	}
}
