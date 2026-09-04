using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) print layouts (RMS plan section 4.10.1, registry M0160): rmsrecordprintlayouts, one versioned
	/// row per (department, scope, definition key). PostgreSQL twin of the SQL Server migration. Existence-guarded.
	/// </summary>
	[Migration(160)]
	public class M0160_AddRmsPrintLayoutsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordprintlayouts").Exists())
			{
				Create.Table("rmsrecordprintlayouts")
					.WithColumn("rmsrecordprintlayoutid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("scope").AsInt32().NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable().WithDefaultValue("")
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("configjson").AsCustom("text").Nullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsrecordprintlayouts_department_scope_definition ON rmsrecordprintlayouts (departmentid, scope, definitionkey);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsrecordprintlayouts").Exists())
				Delete.Table("rmsrecordprintlayouts");
		}
	}
}
