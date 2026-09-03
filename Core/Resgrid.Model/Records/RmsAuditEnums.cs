namespace Resgrid.Model
{
	/// <summary>Action recorded in RmsAccessAudits (RMS plan section 5.8).</summary>
	public enum RmsAccessAuditAction
	{
		Read = 1,
		Search = 2,
		Change = 3,
		Sign = 4,
		Print = 5,
		Export = 6,
		Submit = 7,
		Share = 8,
		Support = 9,
		Denied = 10,
		Admin = 11,
		Activation = 12,
		LegacyWriteDenied = 13
	}

	/// <summary>Source of a row in the records search index / projection (RMS plan section 5.10).</summary>
	public enum RmsSearchSourceType
	{
		Record = 1,
		LegacyLog = 2,
		LegacyUnitLog = 3
	}

	/// <summary>Malware/content scan state of an RmsRecordAttachment.</summary>
	public enum RmsAttachmentScanState
	{
		Pending = 0,
		Clean = 1,
		Rejected = 2,
		Skipped = 3
	}

	/// <summary>Cutover history event kinds written to RmsDepartmentCutoverEvents.</summary>
	public static class RmsDepartmentCutoverEventTypes
	{
		public const string Activated = "Activated";
		public const string PermissionRowsMigrated = "PermissionRowsMigrated";
		public const string Reverted = "Reverted";
		public const string Repaired = "Repaired";
		public const string LegacyWriteDenied = "LegacyWriteDenied";
	}

	/// <summary>Well-known ProducerSubsystem / AggregateType values on DomainEventOutbox rows.</summary>
	public static class DomainEventProducers
	{
		public const string Records = "Records";
		public const string RecordsAggregate = "RmsOperationalRecord";
	}
}
