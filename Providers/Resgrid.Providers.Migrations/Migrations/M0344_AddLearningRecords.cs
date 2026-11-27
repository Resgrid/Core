using System.Data;
using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T1, one transcript (protocols-training-knowledge-plan §12.2). Baseline (free) tables:
	/// - TrainingCategories: custom categories and the ones applied from the vertical templates (ISO FSRS, NREMT NCCP, OSHA)
	///   with their targets; soft deleted.
	/// - LearningRecords: the ledger. One record per person per event component, idempotent on (DepartmentId, SourceKind,
	///   SourceRefId, UserId, ComponentKey) among the records that are not voided (filtered unique index, R-T1). No foreign key
	///   to its source or its category: records survive the deletion of the training, log, record or calendar item that wrote
	///   them, and department deletion purges them by DepartmentId (UnlinkedDepartmentCleanup). The certificate verification
	///   code (LMS, dark) has its own filtered unique index.
	/// - LearningRecordFiles: evidence uploaded with an external submission (bytes kept out of the ledger).
	/// - CategoryCreditMappings: certification type → category (+ component), cascading from TrainingCategories.
	/// - LearningRecordBackfills: the resumable per-department history backfill jobs worker 85 runs (R-T3).
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(344)]
	public class M0344_AddLearningRecords : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("TrainingCategories").Exists())
			{
				Create.Table("TrainingCategories")
					.WithColumn("TrainingCategoryId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(150).NotNullable()
					.WithColumn("Code").AsString(32).Nullable()
					.WithColumn("Kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("TargetHours").AsDecimal(9, 2).Nullable()
					.WithColumn("TargetPeriod").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TargetAppliesTo").AsString(200).Nullable()
					.WithColumn("ExternalCode").AsString(64).Nullable()
					.WithColumn("RetentionYears").AsInt32().Nullable()
					.WithColumn("TemplateKey").AsString(64).Nullable()
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();

				Create.Index("IX_TrainingCategories_Department").OnTable("TrainingCategories").OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("LearningRecords").Exists())
			{
				Create.Table("LearningRecords")
					.WithColumn("LearningRecordId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("SourceKind").AsInt32().NotNullable()
					.WithColumn("SourceRefId").AsString(128).NotNullable()
					.WithColumn("ComponentKey").AsString(64).NotNullable().WithDefaultValue("")
					.WithColumn("SessionKey").AsString(128).Nullable()
					.WithColumn("Title").AsString(250).NotNullable()
					.WithColumn("CourseCode").AsString(100).Nullable()
					.WithColumn("CompletedOn").AsDateTime2().NotNullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("EndedOn").AsDateTime2().Nullable()
					.WithColumn("Hours").AsDecimal(9, 2).NotNullable().WithDefaultValue(0)
					.WithColumn("CategoryId").AsInt32().Nullable()
					.WithColumn("MethodOfTraining").AsInt32().Nullable()
					.WithColumn("Objectives").AsString(2000).Nullable()
					.WithColumn("Location").AsString(250).Nullable()
					.WithColumn("CeProvider").AsString(200).Nullable()
					.WithColumn("CeCourseNumber").AsString(100).Nullable()
					.WithColumn("CeHours").AsDecimal(9, 2).Nullable()
					.WithColumn("InstructorUserIds").AsString(1000).Nullable()
					.WithColumn("InstructorNames").AsString(1000).Nullable()
					.WithColumn("Status").AsInt32().NotNullable()
					.WithColumn("SupersedesId").AsInt32().Nullable()
					.WithColumn("VoidReason").AsString(500).Nullable()
					.WithColumn("VoidedOn").AsDateTime2().Nullable()
					.WithColumn("VoidedByUserId").AsString(128).Nullable()
					.WithColumn("VerifiedByUserId").AsString(128).Nullable()
					.WithColumn("VerifiedOn").AsDateTime2().Nullable()
					.WithColumn("ReviewNote").AsString(1000).Nullable()
					.WithColumn("EvidenceFileId").AsInt32().Nullable()
					.WithColumn("IsBackfill").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ExternalId").AsString(100).Nullable()
					.WithColumn("Notes").AsString(4000).Nullable()
					.WithColumn("Tags").AsString(500).Nullable()
					.WithColumn("PossibleDuplicateOfId").AsInt32().Nullable()
					.WithColumn("DuplicateResolvedOn").AsDateTime2().Nullable()
					.WithColumn("DuplicateResolution").AsInt32().Nullable()
					.WithColumn("VerificationCode").AsString(32).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable();

				Create.Index("IX_LearningRecords_Department_User_Completed").OnTable("LearningRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("UserId").Ascending().OnColumn("CompletedOn").Ascending();
				Create.Index("IX_LearningRecords_Department_Completed").OnTable("LearningRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("CompletedOn").Ascending();
				Create.Index("IX_LearningRecords_Department_Session").OnTable("LearningRecords")
					.OnColumn("DepartmentId").Ascending().OnColumn("SessionKey").Ascending();
			}

			// The idempotency key (R-T1): unique among records that are not voided, so a superseding record (R-T4) can reuse it.
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LearningRecords_Key' AND object_id = OBJECT_ID('dbo.LearningRecords')) " +
				"CREATE UNIQUE INDEX [UX_LearningRecords_Key] ON [dbo].[LearningRecords] ([DepartmentId], [SourceKind], [SourceRefId], [UserId], [ComponentKey]) WHERE [Status] <> 3;");
			Execute.Sql(
				"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LearningRecords_VerificationCode' AND object_id = OBJECT_ID('dbo.LearningRecords')) " +
				"CREATE UNIQUE INDEX [UX_LearningRecords_VerificationCode] ON [dbo].[LearningRecords] ([VerificationCode]) WHERE [VerificationCode] IS NOT NULL;");

			if (!Schema.Table("LearningRecordFiles").Exists())
			{
				Create.Table("LearningRecordFiles")
					.WithColumn("LearningRecordFileId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("FileName").AsString(255).NotNullable()
					.WithColumn("ContentType").AsString(100).Nullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("UploadedOn").AsDateTime2().NotNullable()
					.WithColumn("UploadedByUserId").AsString(128).Nullable();

				Create.Index("IX_LearningRecordFiles_Department").OnTable("LearningRecordFiles").OnColumn("DepartmentId").Ascending();
			}

			if (!Schema.Table("CategoryCreditMappings").Exists())
			{
				Create.Table("CategoryCreditMappings")
					.WithColumn("CategoryCreditMappingId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("DepartmentCertificationTypeId").AsInt32().NotNullable()
					.WithColumn("CategoryId").AsInt32().NotNullable()
					.WithColumn("ComponentKey").AsString(64).Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("EnabledOn").AsDateTime2().Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();

				Create.ForeignKey("FK_CategoryCreditMappings_TrainingCategories").FromTable("CategoryCreditMappings").ForeignColumn("CategoryId")
					.ToTable("TrainingCategories").PrimaryColumn("TrainingCategoryId").OnDelete(Rule.Cascade);

				Create.Index("IX_CategoryCreditMappings_Department_Type").OnTable("CategoryCreditMappings")
					.OnColumn("DepartmentId").Ascending().OnColumn("DepartmentCertificationTypeId").Ascending();
			}

			if (!Schema.Table("LearningRecordBackfills").Exists())
			{
				Create.Table("LearningRecordBackfills")
					.WithColumn("LearningRecordBackfillId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("State").AsInt32().NotNullable()
					.WithColumn("DryRun").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RequestedOn").AsDateTime2().NotNullable()
					.WithColumn("RequestedByUserId").AsString(128).Nullable()
					.WithColumn("StartedOn").AsDateTime2().Nullable()
					.WithColumn("CompletedOn").AsDateTime2().Nullable()
					.WithColumn("ResumeAfter").AsString(200).Nullable()
					.WithColumn("CompletionsFound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("TrainingRecordsFound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordsWritten").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RecordsSkipped").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Attempts").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("Error").AsString(1000).Nullable();

				Create.Index("IX_LearningRecordBackfills_State").OnTable("LearningRecordBackfills").OnColumn("State").Ascending();
				Create.Index("IX_LearningRecordBackfills_Department").OnTable("LearningRecordBackfills").OnColumn("DepartmentId").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("LearningRecordBackfills").Exists())
				Delete.Table("LearningRecordBackfills");
			if (Schema.Table("CategoryCreditMappings").Exists())
				Delete.Table("CategoryCreditMappings");
			if (Schema.Table("LearningRecordFiles").Exists())
				Delete.Table("LearningRecordFiles");
			if (Schema.Table("LearningRecords").Exists())
				Delete.Table("LearningRecords");
			if (Schema.Table("TrainingCategories").Exists())
				Delete.Table("TrainingCategories");
		}
	}
}
