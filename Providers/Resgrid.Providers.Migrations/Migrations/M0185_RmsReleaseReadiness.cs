using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS-4 release readiness (RMS plan section 6, RMS-4; registry M0185, taken physically under the no-gaps rule in
	/// place of the M0174 reservation): the covering indexes for the hot Records read paths (queue by state and
	/// modified time, call lookups, finalized-since windows, idempotency replay, group-scope recompute, due-state
	/// lookups, per-department outbox and audit counts for the release telemetry, scan-state and submission-state
	/// counts), plus the optional post-finalization quality review tables (plan section 4.7): a department rubric
	/// and one non-mutating review per sampled revision. Guarded for safe retry.
	/// </summary>
	[Migration(185)]
	public class M0185_RmsReleaseReadiness : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_State_Modified").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_State_Modified").OnTable("RmsOperationalRecords").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("ModifiedOn").Ascending();
			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Call").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Call").OnTable("RmsOperationalRecords").OnColumn("DepartmentId").Ascending().OnColumn("CallId").Ascending();
			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Finalized").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Finalized").OnTable("RmsOperationalRecords").OnColumn("DepartmentId").Ascending().OnColumn("FinalizedOn").Ascending();
			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Idempotency").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Idempotency").OnTable("RmsOperationalRecords").OnColumn("DepartmentId").Ascending().OnColumn("IdempotencyKey").Ascending();
			if (Schema.Table("RmsIncidentReports").Exists() && !Schema.Table("RmsIncidentReports").Index("IX_RmsIncidentReports_Department_Modified").Exists())
				Create.Index("IX_RmsIncidentReports_Department_Modified").OnTable("RmsIncidentReports").OnColumn("DepartmentId").Ascending().OnColumn("ModifiedOn").Ascending();
			if (Schema.Table("RmsRecordGroupScopes").Exists() && !Schema.Table("RmsRecordGroupScopes").Index("IX_RmsRecordGroupScopes_Department_Record").Exists())
				Create.Index("IX_RmsRecordGroupScopes_Department_Record").OnTable("RmsRecordGroupScopes").OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			if (Schema.Table("RmsRecordDueStates").Exists() && !Schema.Table("RmsRecordDueStates").Index("IX_RmsRecordDueStates_Department_Record").Exists())
				Create.Index("IX_RmsRecordDueStates_Department_Record").OnTable("RmsRecordDueStates").OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
			if (Schema.Table("DomainEventOutbox").Exists() && !Schema.Table("DomainEventOutbox").Index("IX_DomainEventOutbox_Department_State_Created").Exists())
				Create.Index("IX_DomainEventOutbox_Department_State_Created").OnTable("DomainEventOutbox").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending().OnColumn("CreatedOn").Ascending();
			if (Schema.Table("RmsAccessAudits").Exists() && !Schema.Table("RmsAccessAudits").Index("IX_RmsAccessAudits_Department_Action_Occurred").Exists())
				Create.Index("IX_RmsAccessAudits_Department_Action_Occurred").OnTable("RmsAccessAudits").OnColumn("DepartmentId").Ascending().OnColumn("Action").Ascending().OnColumn("OccurredOn").Ascending();
			if (Schema.Table("RmsRecordAttachments").Exists() && !Schema.Table("RmsRecordAttachments").Index("IX_RmsRecordAttachments_Department_ScanState").Exists())
				Create.Index("IX_RmsRecordAttachments_Department_ScanState").OnTable("RmsRecordAttachments").OnColumn("DepartmentId").Ascending().OnColumn("ScanState").Ascending();
			if (Schema.Table("RmsSubmissions").Exists() && !Schema.Table("RmsSubmissions").Index("IX_RmsSubmissions_Department_State").Exists())
				Create.Index("IX_RmsSubmissions_Department_State").OnTable("RmsSubmissions").OnColumn("DepartmentId").Ascending().OnColumn("State").Ascending();

			if (!Schema.Table("RmsQualityRubrics").Exists())
			{
				Create.Table("RmsQualityRubrics")
					.WithColumn("RmsQualityRubricId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("DefinitionKey").AsString(100).Nullable()
					.WithColumn("CriteriaJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("SampleSize").AsInt32().NotNullable().WithDefaultValue(10)
					.WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();

				Create.Index("IX_RmsQualityRubrics_Department").OnTable("RmsQualityRubrics").OnColumn("DepartmentId").Ascending().OnColumn("IsActive").Ascending();
			}
			if (!Schema.Table("RmsQualityReviews").Exists())
			{
				Create.Table("RmsQualityReviews")
					.WithColumn("RmsQualityReviewId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("RmsQualityRubricId").AsString(36).NotNullable()
					.WithColumn("RecordId").AsString(36).NotNullable()
					.WithColumn("RecordKind").AsInt32().NotNullable()
					.WithColumn("RevisionId").AsString(36).Nullable()
					.WithColumn("DefinitionKey").AsString(100).Nullable()
					.WithColumn("RecordNumber").AsString(64).Nullable()
					.WithColumn("AuthorUserId").AsString(128).Nullable()
					.WithColumn("UnitId").AsInt32().Nullable()
					.WithColumn("ReviewerUserId").AsString(128).Nullable()
					.WithColumn("SampledOn").AsDateTime2().NotNullable()
					.WithColumn("ScoredOn").AsDateTime2().Nullable()
					.WithColumn("Score").AsInt32().Nullable()
					.WithColumn("CriteriaJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("FindingsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("Note").AsString(int.MaxValue).Nullable()
					.WithColumn("AmendmentRecommended").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsQualityReviews_Department_Record").OnTable("RmsQualityReviews").OnColumn("DepartmentId").Ascending().OnColumn("RecordId").Ascending();
				Create.Index("IX_RmsQualityReviews_Department_Scored").OnTable("RmsQualityReviews").OnColumn("DepartmentId").Ascending().OnColumn("ScoredOn").Ascending();
				Create.Index("IX_RmsQualityReviews_Department_Rubric_Sampled").OnTable("RmsQualityReviews").OnColumn("DepartmentId").Ascending().OnColumn("RmsQualityRubricId").Ascending().OnColumn("SampledOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsQualityReviews").Exists())
				Delete.Table("RmsQualityReviews");
			if (Schema.Table("RmsQualityRubrics").Exists())
				Delete.Table("RmsQualityRubrics");
			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_State_Modified").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_State_Modified").OnTable("RmsOperationalRecords");
			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Call").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Call").OnTable("RmsOperationalRecords");
			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Finalized").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Finalized").OnTable("RmsOperationalRecords");
			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Index("IX_RmsOperationalRecords_Department_Idempotency").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Idempotency").OnTable("RmsOperationalRecords");
			if (Schema.Table("RmsIncidentReports").Exists() && Schema.Table("RmsIncidentReports").Index("IX_RmsIncidentReports_Department_Modified").Exists())
				Delete.Index("IX_RmsIncidentReports_Department_Modified").OnTable("RmsIncidentReports");
			if (Schema.Table("RmsRecordGroupScopes").Exists() && Schema.Table("RmsRecordGroupScopes").Index("IX_RmsRecordGroupScopes_Department_Record").Exists())
				Delete.Index("IX_RmsRecordGroupScopes_Department_Record").OnTable("RmsRecordGroupScopes");
			if (Schema.Table("RmsRecordDueStates").Exists() && Schema.Table("RmsRecordDueStates").Index("IX_RmsRecordDueStates_Department_Record").Exists())
				Delete.Index("IX_RmsRecordDueStates_Department_Record").OnTable("RmsRecordDueStates");
			if (Schema.Table("DomainEventOutbox").Exists() && Schema.Table("DomainEventOutbox").Index("IX_DomainEventOutbox_Department_State_Created").Exists())
				Delete.Index("IX_DomainEventOutbox_Department_State_Created").OnTable("DomainEventOutbox");
			if (Schema.Table("RmsAccessAudits").Exists() && Schema.Table("RmsAccessAudits").Index("IX_RmsAccessAudits_Department_Action_Occurred").Exists())
				Delete.Index("IX_RmsAccessAudits_Department_Action_Occurred").OnTable("RmsAccessAudits");
			if (Schema.Table("RmsRecordAttachments").Exists() && Schema.Table("RmsRecordAttachments").Index("IX_RmsRecordAttachments_Department_ScanState").Exists())
				Delete.Index("IX_RmsRecordAttachments_Department_ScanState").OnTable("RmsRecordAttachments");
			if (Schema.Table("RmsSubmissions").Exists() && Schema.Table("RmsSubmissions").Index("IX_RmsSubmissions_Department_State").Exists())
				Delete.Index("IX_RmsSubmissions_Department_State").OnTable("RmsSubmissions");
		}
	}
}
