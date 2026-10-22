using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Saved map visualizations (Mapping → Visualizations, registry M0288): a member's own or department-shared definition of
	/// data overlays (call heat maps, hex grids, response footprints, tracks) as JSON. Only the definition is stored; the data
	/// is read under the viewer's permissions each time. No foreign keys; the department purge removes the rows.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys and user ids, existence-guarded.
	/// </summary>
	[Migration(288)]
	public class M0288_AddMapVisualizationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("mapvisualizations").Exists())
			{
				Create.Table("mapvisualizations")
					.WithColumn("mapvisualizationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("definitionjson").AsCustom("text").Nullable()
					.WithColumn("templatekey").AsCustom("citext").Nullable()
					.WithColumn("isshared").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("updatedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_mapvisualizations_department ON mapvisualizations (departmentid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("mapvisualizations").Exists())
				Delete.Table("mapvisualizations");
		}
	}
}
