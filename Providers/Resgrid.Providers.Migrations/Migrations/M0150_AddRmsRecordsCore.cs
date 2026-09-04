using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS) core aggregate (RMS plan sections 5.2/5.3, registry M0150):
	/// RmsOperationalRecords (header: definition/version pin, lifecycle preset and state, numbering,
	/// ownership, review/approval/finalize/void/cancel facts, ETag RowVersion, idempotency key),
	/// RmsOperationalRecordDetails (first-class typed Logs-parity fields for Run/Training/Work/Meeting/
	/// Coroner/Callback/Unit Activity plus the Call snapshot; one working-draft row per Record with
	/// RevisionId NULL, and one immutable row per finalized revision) and RmsExternalReferences
	/// (versioned correlation to a source subsystem/entity with provenance and checksum).
	/// Every table carries DepartmentId and an immutable random ProtectionId so a later Protected Data
	/// enrollment is an in-place row update, never a schema rewrite (plan section 5.9.1). Protected-
	/// candidate text columns are nvarchar(max) so an rgdp: envelope fits; IsProtected/
	/// ProtectedCatalogVersion ship inert (0). Primary keys are client-compatible GUID strings so
	/// field apps can create drafts offline. Existence-guarded for safe retry.
	/// </summary>
	[Migration(150)]
	public class M0150_AddRmsRecordsCore : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsOperationalRecords").Exists())
			{
				Create.Table("RmsOperationalRecords")
					.WithColumn("RmsOperationalRecordId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("DefinitionKey").AsString(100).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RecordType").AsInt32().Nullable()
					.WithColumn("LifecyclePreset").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("State").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("RecordNumber").AsString(50).Nullable()
					.WithColumn("DraftReference").AsString(20).NotNullable()
					.WithColumn("DisplaySummary").AsString(400).Nullable()
					.WithColumn("StationGroupId").AsInt32().Nullable()
					.WithColumn("CallId").AsInt32().Nullable()
					.WithColumn("ExternalId").AsString(200).Nullable()
					.WithColumn("AuthorUserId").AsString(128).NotNullable()
					.WithColumn("AuthorGroupIdSnapshot").AsInt32().Nullable()
					.WithColumn("OwnerUserId").AsString(128).NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("EndedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewDueOn").AsDateTime2().Nullable()
					.WithColumn("SubmittedForReviewOn").AsDateTime2().Nullable()
					.WithColumn("ReturnedOn").AsDateTime2().Nullable()
					.WithColumn("ReturnReasonCode").AsString(50).Nullable()
					.WithColumn("ReturnReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("ReturnCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("ApprovedOn").AsDateTime2().Nullable()
					.WithColumn("ApproverUserId").AsString(128).Nullable()
					.WithColumn("FinalizedOn").AsDateTime2().Nullable()
					.WithColumn("FinalizedByUserId").AsString(128).Nullable()
					.WithColumn("CurrentRevisionId").AsString(36).Nullable()
					.WithColumn("RevisionCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("AmendsRevisionId").AsString(36).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("VoidReasonCode").AsString(50).Nullable()
					.WithColumn("VoidReasonText").AsString(int.MaxValue).Nullable()
					.WithColumn("CancelledOn").AsDateTime2().Nullable()
					.WithColumn("CancelledByUserId").AsString(128).Nullable()
					.WithColumn("IdempotencyKey").AsString(100).Nullable()
					.WithColumn("OriginClient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsOperationalRecords_Department_State").OnTable("RmsOperationalRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();
				Create.Index("IX_RmsOperationalRecords_Department_Definition_Created").OnTable("RmsOperationalRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("DefinitionKey").Ascending().OnColumn("CreatedOn").Descending();
				Create.Index("IX_RmsOperationalRecords_Department_Author").OnTable("RmsOperationalRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("AuthorUserId").Ascending();
				Create.Index("IX_RmsOperationalRecords_Department_Owner").OnTable("RmsOperationalRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("OwnerUserId").Ascending();

				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsOperationalRecords_Department_Call ON RmsOperationalRecords (DepartmentId, CallId) WHERE CallId IS NOT NULL;");
				// Idempotent create: a replayed create returns the existing Record instead of a second one.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecords_Department_IdempotencyKey ON RmsOperationalRecords (DepartmentId, IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;");
				// A record number is never reused within a department, under either numbering policy.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecords_Department_RecordNumber ON RmsOperationalRecords (DepartmentId, RecordNumber) WHERE RecordNumber IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecords_Department_DraftReference ON RmsOperationalRecords (DepartmentId, DraftReference);");
			}

			if (!Schema.Table("RmsOperationalRecordDetails").Exists())
			{
				Create.Table("RmsOperationalRecordDetails")
					.WithColumn("RmsOperationalRecordDetailId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("Narrative").AsString(int.MaxValue).Nullable()
					.WithColumn("InitialReport").AsString(int.MaxValue).Nullable()
					.WithColumn("Type").AsString(200).Nullable()
					.WithColumn("Course").AsString(400).Nullable()
					.WithColumn("CourseCode").AsString(100).Nullable()
					.WithColumn("Instructors").AsString(int.MaxValue).Nullable()
					.WithColumn("Cause").AsString(int.MaxValue).Nullable()
					.WithColumn("InvestigatedByUserId").AsString(128).Nullable()
					.WithColumn("ContactName").AsString(int.MaxValue).Nullable()
					.WithColumn("ContactNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("OtherPersonnel").AsString(int.MaxValue).Nullable()
					.WithColumn("Location").AsString(int.MaxValue).Nullable()
					.WithColumn("OtherAgencies").AsString(int.MaxValue).Nullable()
					.WithColumn("OtherUnits").AsString(int.MaxValue).Nullable()
					.WithColumn("BodyLocation").AsString(int.MaxValue).Nullable()
					.WithColumn("PronouncedDeceasedBy").AsString(int.MaxValue).Nullable()
					.WithColumn("CaseNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Destination").AsString(int.MaxValue).Nullable()
					.WithColumn("Facilitator").AsString(400).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("ActivityOn").AsDateTime2().Nullable()
					.WithColumn("CallNumber").AsString(50).Nullable()
					.WithColumn("CallName").AsString(400).Nullable()
					.WithColumn("CallType").AsString(200).Nullable()
					.WithColumn("CallPriority").AsInt32().Nullable()
					.WithColumn("CallLoggedOn").AsDateTime2().Nullable()
					.WithColumn("CallAddress").AsString(int.MaxValue).Nullable()
					.WithColumn("CallNature").AsString(int.MaxValue).Nullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedEnvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsOperationalRecordDetails_Department_Record").OnTable("RmsOperationalRecordDetails")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				// Exactly one working draft (RevisionId NULL) per Record; one detail row per revision.
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecordDetails_Record_Draft ON RmsOperationalRecordDetails (DepartmentId, RecordId) WHERE RevisionId IS NULL;");
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecordDetails_Record_Revision ON RmsOperationalRecordDetails (DepartmentId, RecordId, RevisionId) WHERE RevisionId IS NOT NULL;");
			}

			if (!Schema.Table("RmsExternalReferences").Exists())
			{
				Create.Table("RmsExternalReferences")
					.WithColumn("RmsExternalReferenceId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("SourceSubsystem").AsString(50).NotNullable()
					.WithColumn("SourceEntityType").AsString(100).NotNullable()
					.WithColumn("SourceEntityId").AsString(100).NotNullable()
					.WithColumn("IdentifierScheme").AsString(100).Nullable()
					.WithColumn("SourceVersion").AsString(100).Nullable()
					.WithColumn("SourceEventId").AsString(36).Nullable()
					.WithColumn("SemanticRole").AsString(50).NotNullable()
					.WithColumn("CapturedOn").AsDateTime2().NotNullable()
					.WithColumn("CapturedByUserId").AsString(128).Nullable()
					.WithColumn("Checksum").AsString(128).Nullable()
					.WithColumn("SafeUrl").AsString(1000).Nullable()
					.WithColumn("SnapshotJson").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsExternalReferences_Department_Record").OnTable("RmsExternalReferences")
					.OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsExternalReferences_Department_Source").OnTable("RmsExternalReferences")
					.OnColumn("DepartmentId").Ascending().OnColumn("SourceSubsystem").Ascending()
					.OnColumn("SourceEntityType").Ascending().OnColumn("SourceEntityId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsExternalReferences").Exists())
				Delete.Table("RmsExternalReferences");

			if (Schema.Table("RmsOperationalRecordDetails").Exists())
				Delete.Table("RmsOperationalRecordDetails");

			if (Schema.Table("RmsOperationalRecords").Exists())
				Delete.Table("RmsOperationalRecords");
		}
	}
}
