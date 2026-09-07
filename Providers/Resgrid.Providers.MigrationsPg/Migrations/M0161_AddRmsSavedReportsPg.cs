using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS-1B) saved reports (plan section 4.1 reporting, registry M0161): department-owned allowlisted columns, bounded filters, one group-by and count/sum/avg/min/max, with explicit cross-version field mappings in SpecJson.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys, existence-guarded.
	/// </summary>
	[Migration(161)]
	public class M0161_AddRmsSavedReportsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmssavedreportdefinitions").Exists())
			{
				Create.Table("rmssavedreportdefinitions")
					.WithColumn("rmssavedreportdefinitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().Nullable()
					.WithColumn("specjson").AsCustom("text").Nullable()
					.WithColumn("maxrowsperrun").AsInt32().NotNullable().WithDefaultValue(5000)
					.WithColumn("includerestricted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastrunon").AsDateTime2().Nullable()
					.WithColumn("lastrunbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssavedreportdefinitions_department ON rmssavedreportdefinitions (departmentid, definitionkey);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("rmssavedreportdefinitions").Exists())
				Delete.Table("rmssavedreportdefinitions");
		}
	}
}
