using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Records (RMS-1B) saved reports (plan section 4.1 reporting, registry M0161): department-owned allowlisted columns, bounded filters, one group-by and count/sum/avg/min/max, with explicit cross-version field mappings in SpecJson.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(161)]
	public class M0161_AddRmsSavedReports : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsSavedReportDefinitions").Exists())
			{
				Create.Table("RmsSavedReportDefinitions")
					.WithColumn("RmsSavedReportDefinitionId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ProtectionId").AsString(36).NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(int.MaxValue).Nullable()
					.WithColumn("DefinitionKey").AsString(200).NotNullable()
					.WithColumn("DefinitionVersion").AsInt32().Nullable()
					.WithColumn("SpecJson").AsString(int.MaxValue).Nullable()
					.WithColumn("MaxRowsPerRun").AsInt32().NotNullable().WithDefaultValue(5000)
					.WithColumn("IncludeRestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("LastRunOn").AsDateTime2().Nullable()
					.WithColumn("LastRunByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable()
					.WithColumn("ModifiedByUserId").AsString(128).Nullable()
					.WithColumn("RowVersion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("DeletedOn").AsDateTime2().Nullable();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsSavedReportDefinitions_Department ON RmsSavedReportDefinitions (DepartmentId, DefinitionKey);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("RmsSavedReportDefinitions").Exists())
				Delete.Table("RmsSavedReportDefinitions");
		}
	}
}
