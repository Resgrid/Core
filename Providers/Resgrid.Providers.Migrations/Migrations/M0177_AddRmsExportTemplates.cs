using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department-authored report exports (RMS plan section 5.6): the export template a department designs and
	/// the stored runs a Workflow step or the schedule sweep (worker 45) renders from it.
	/// </summary>
	[Migration(177)]
	public class M0177_AddRmsExportTemplates : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsExportTemplates").Exists())
			{
				Create.Table("RmsExportTemplates")
					.WithColumn("RmsExportTemplateId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("TemplateKey").AsString(64).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("Format").AsInt32().NotNullable()
					.WithColumn("Scope").AsInt32().NotNullable()
					.WithColumn("DefinitionKeysCsv").AsString(1000).Nullable()
					.WithColumn("ColumnsJson").AsString(int.MaxValue).NotNullable()
					.WithColumn("IncludeNarrative").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("IncludeRestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("EgressAcknowledgedOn").AsDateTime2().Nullable()
					.WithColumn("EgressAcknowledgedByUserId").AsString(128).Nullable()
					.WithColumn("FileNameTemplate").AsString(200).Nullable()
					.WithColumn("IncludeHeader").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("Delimiter").AsString(4).Nullable()
					.WithColumn("ScheduleKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ScheduleHourLocal").AsInt32().NotNullable().WithDefaultValue(6)
					.WithColumn("ScheduleDayOfWeek").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ScheduleDayOfMonth").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("WindowDays").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("NextRunOn").AsDateTime2().Nullable()
					.WithColumn("LastRunOn").AsDateTime2().Nullable()
					.WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Create.Index("UX_RmsExportTemplates_Key").OnTable("RmsExportTemplates")
					.OnColumn("DepartmentId").Ascending().OnColumn("TemplateKey").Ascending().WithOptions().Unique();
				Create.Index("IX_RmsExportTemplates_Due").OnTable("RmsExportTemplates")
					.OnColumn("IsEnabled").Ascending().OnColumn("NextRunOn").Ascending();
			}

			if (!Schema.Table("RmsExportRuns").Exists())
			{
				Create.Table("RmsExportRuns")
					.WithColumn("RmsExportRunId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).Nullable()
					.WithColumn("TemplateId").AsString(36).NotNullable()
					.WithColumn("TemplateKey").AsString(64).NotNullable()
					.WithColumn("Trigger").AsInt32().NotNullable()
					.WithColumn("RecordId").AsString(36).Nullable()
					.WithColumn("WindowStart").AsDateTime2().Nullable()
					.WithColumn("WindowEnd").AsDateTime2().Nullable()
					.WithColumn("RecordCount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FileName").AsString(260).NotNullable()
					.WithColumn("ContentType").AsString(100).NotNullable()
					.WithColumn("ByteSize").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("Checksum").AsString(80).NotNullable()
					.WithColumn("Data").AsBinary(int.MaxValue).Nullable()
					.WithColumn("Redacted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("RedactedFieldsJson").AsString(int.MaxValue).Nullable()
					.WithColumn("GeneratedOn").AsDateTime2().NotNullable()
					.WithColumn("GeneratedByUserId").AsString(128).Nullable()
					.WithColumn("WorkflowRunId").AsString(36).Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().NotNullable()
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("ProtectedCatalogVersion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Create.Index("IX_RmsExportRuns_Template").OnTable("RmsExportRuns")
					.OnColumn("DepartmentId").Ascending().OnColumn("TemplateId").Ascending().OnColumn("GeneratedOn").Descending();
				Create.Index("IX_RmsExportRuns_Expires").OnTable("RmsExportRuns")
					.OnColumn("DepartmentId").Ascending().OnColumn("ExpiresOn").Ascending();
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsExportRuns").Exists())
				Delete.Table("RmsExportRuns");
			if (Schema.Table("RmsExportTemplates").Exists())
				Delete.Table("RmsExportTemplates");
		}
	}
}
