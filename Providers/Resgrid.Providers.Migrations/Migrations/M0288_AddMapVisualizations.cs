using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Saved map visualizations (Mapping → Visualizations, registry M0288): a member's own or department-shared definition of
	/// data overlays (call heat maps, hex grids, response footprints, tracks) as JSON. Only the definition is stored; the data
	/// is read under the viewer's permissions each time. No foreign keys; the department purge removes the rows.
	/// Existence-guarded for safe retry.
	/// </summary>
	[Migration(288)]
	public class M0288_AddMapVisualizations : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("MapVisualizations").Exists())
			{
				Create.Table("MapVisualizations")
					.WithColumn("MapVisualizationId").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(200).NotNullable()
					.WithColumn("Description").AsString(1000).Nullable()
					.WithColumn("DefinitionJson").AsString(int.MaxValue).Nullable()
					.WithColumn("TemplateKey").AsString(100).Nullable()
					.WithColumn("IsShared").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().NotNullable();
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_MapVisualizations_Department ON MapVisualizations (DepartmentId);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("MapVisualizations").Exists())
				Delete.Table("MapVisualizations");
		}
	}
}
