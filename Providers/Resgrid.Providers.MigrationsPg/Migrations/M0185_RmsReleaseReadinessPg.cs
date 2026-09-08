using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
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
	public class M0185_RmsReleaseReadinessPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_State_Modified").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_State_Modified").OnTable("rmsoperationalrecords").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("modifiedon").Ascending();
			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Call").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Call").OnTable("rmsoperationalrecords").OnColumn("departmentid").Ascending().OnColumn("callid").Ascending();
			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Finalized").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Finalized").OnTable("rmsoperationalrecords").OnColumn("departmentid").Ascending().OnColumn("finalizedon").Ascending();
			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Idempotency").Exists())
				Create.Index("IX_RmsOperationalRecords_Department_Idempotency").OnTable("rmsoperationalrecords").OnColumn("departmentid").Ascending().OnColumn("idempotencykey").Ascending();
			if (Schema.Table("rmsincidentreports").Exists() && !Schema.Table("rmsincidentreports").Index("IX_RmsIncidentReports_Department_Modified").Exists())
				Create.Index("IX_RmsIncidentReports_Department_Modified").OnTable("rmsincidentreports").OnColumn("departmentid").Ascending().OnColumn("modifiedon").Ascending();
			if (Schema.Table("rmsrecordgroupscopes").Exists() && !Schema.Table("rmsrecordgroupscopes").Index("IX_RmsRecordGroupScopes_Department_Record").Exists())
				Create.Index("IX_RmsRecordGroupScopes_Department_Record").OnTable("rmsrecordgroupscopes").OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending();
			if (Schema.Table("rmsrecordduestates").Exists() && !Schema.Table("rmsrecordduestates").Index("IX_RmsRecordDueStates_Department_Record").Exists())
				Create.Index("IX_RmsRecordDueStates_Department_Record").OnTable("rmsrecordduestates").OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending();
			if (Schema.Table("domaineventoutbox").Exists() && !Schema.Table("domaineventoutbox").Index("IX_DomainEventOutbox_Department_State_Created").Exists())
				Create.Index("IX_DomainEventOutbox_Department_State_Created").OnTable("domaineventoutbox").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("createdon").Ascending();
			if (Schema.Table("rmsaccessaudits").Exists() && !Schema.Table("rmsaccessaudits").Index("IX_RmsAccessAudits_Department_Action_Occurred").Exists())
				Create.Index("IX_RmsAccessAudits_Department_Action_Occurred").OnTable("rmsaccessaudits").OnColumn("departmentid").Ascending().OnColumn("action").Ascending().OnColumn("occurredon").Ascending();
			if (Schema.Table("rmsrecordattachments").Exists() && !Schema.Table("rmsrecordattachments").Index("IX_RmsRecordAttachments_Department_ScanState").Exists())
				Create.Index("IX_RmsRecordAttachments_Department_ScanState").OnTable("rmsrecordattachments").OnColumn("departmentid").Ascending().OnColumn("scanstate").Ascending();
			if (Schema.Table("rmssubmissions").Exists() && !Schema.Table("rmssubmissions").Index("IX_RmsSubmissions_Department_State").Exists())
				Create.Index("IX_RmsSubmissions_Department_State").OnTable("rmssubmissions").OnColumn("departmentid").Ascending().OnColumn("state").Ascending();

			if (!Schema.Table("rmsqualityrubrics").Exists())
			{
				Create.Table("rmsqualityrubrics")
					.WithColumn("rmsqualityrubricid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("definitionkey").AsString(100).Nullable()
					.WithColumn("criteriajson").AsCustom("text").NotNullable()
					.WithColumn("samplesize").AsInt32().NotNullable().WithDefaultValue(10)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsQualityRubrics_Department").OnTable("rmsqualityrubrics").OnColumn("departmentid").Ascending().OnColumn("isactive").Ascending();
			}
			if (!Schema.Table("rmsqualityreviews").Exists())
			{
				Create.Table("rmsqualityreviews")
					.WithColumn("rmsqualityreviewid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsqualityrubricid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("definitionkey").AsString(100).Nullable()
					.WithColumn("recordnumber").AsString(64).Nullable()
					.WithColumn("authoruserid").AsString(128).Nullable()
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("revieweruserid").AsString(128).Nullable()
					.WithColumn("sampledon").AsDateTime2().NotNullable()
					.WithColumn("scoredon").AsDateTime2().Nullable()
					.WithColumn("score").AsInt32().Nullable()
					.WithColumn("criteriajson").AsCustom("text").NotNullable()
					.WithColumn("findingsjson").AsCustom("text").Nullable()
					.WithColumn("note").AsCustom("text").Nullable()
					.WithColumn("amendmentrecommended").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsQualityReviews_Department_Record").OnTable("rmsqualityreviews").OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending();
				Create.Index("IX_RmsQualityReviews_Department_Scored").OnTable("rmsqualityreviews").OnColumn("departmentid").Ascending().OnColumn("scoredon").Ascending();
				Create.Index("IX_RmsQualityReviews_Department_Rubric_Sampled").OnTable("rmsqualityreviews").OnColumn("departmentid").Ascending().OnColumn("rmsqualityrubricid").Ascending().OnColumn("sampledon").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsqualityreviews").Exists())
				Delete.Table("rmsqualityreviews");
			if (Schema.Table("rmsqualityrubrics").Exists())
				Delete.Table("rmsqualityrubrics");
			if (Schema.Table("rmsoperationalrecords").Exists() && Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_State_Modified").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_State_Modified").OnTable("rmsoperationalrecords");
			if (Schema.Table("rmsoperationalrecords").Exists() && Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Call").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Call").OnTable("rmsoperationalrecords");
			if (Schema.Table("rmsoperationalrecords").Exists() && Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Finalized").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Finalized").OnTable("rmsoperationalrecords");
			if (Schema.Table("rmsoperationalrecords").Exists() && Schema.Table("rmsoperationalrecords").Index("IX_RmsOperationalRecords_Department_Idempotency").Exists())
				Delete.Index("IX_RmsOperationalRecords_Department_Idempotency").OnTable("rmsoperationalrecords");
			if (Schema.Table("rmsincidentreports").Exists() && Schema.Table("rmsincidentreports").Index("IX_RmsIncidentReports_Department_Modified").Exists())
				Delete.Index("IX_RmsIncidentReports_Department_Modified").OnTable("rmsincidentreports");
			if (Schema.Table("rmsrecordgroupscopes").Exists() && Schema.Table("rmsrecordgroupscopes").Index("IX_RmsRecordGroupScopes_Department_Record").Exists())
				Delete.Index("IX_RmsRecordGroupScopes_Department_Record").OnTable("rmsrecordgroupscopes");
			if (Schema.Table("rmsrecordduestates").Exists() && Schema.Table("rmsrecordduestates").Index("IX_RmsRecordDueStates_Department_Record").Exists())
				Delete.Index("IX_RmsRecordDueStates_Department_Record").OnTable("rmsrecordduestates");
			if (Schema.Table("domaineventoutbox").Exists() && Schema.Table("domaineventoutbox").Index("IX_DomainEventOutbox_Department_State_Created").Exists())
				Delete.Index("IX_DomainEventOutbox_Department_State_Created").OnTable("domaineventoutbox");
			if (Schema.Table("rmsaccessaudits").Exists() && Schema.Table("rmsaccessaudits").Index("IX_RmsAccessAudits_Department_Action_Occurred").Exists())
				Delete.Index("IX_RmsAccessAudits_Department_Action_Occurred").OnTable("rmsaccessaudits");
			if (Schema.Table("rmsrecordattachments").Exists() && Schema.Table("rmsrecordattachments").Index("IX_RmsRecordAttachments_Department_ScanState").Exists())
				Delete.Index("IX_RmsRecordAttachments_Department_ScanState").OnTable("rmsrecordattachments");
			if (Schema.Table("rmssubmissions").Exists() && Schema.Table("rmssubmissions").Index("IX_RmsSubmissions_Department_State").Exists())
				Delete.Index("IX_RmsSubmissions_Department_State").OnTable("rmssubmissions");
		}
	}
}
