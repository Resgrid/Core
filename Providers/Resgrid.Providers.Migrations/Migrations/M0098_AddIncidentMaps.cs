using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Named tactical maps per incident (own framing, description, optional expiry, full audit), markup
	/// linkage (IncidentMapAnnotations.IncidentMapId), and lane → map attachment
	/// (CommandStructureNodes.LinkedMapId).
	/// </summary>
	[Migration(98)]
	public class M0098_AddIncidentMaps : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentMaps").Exists())
			{
				Create.Table("IncidentMaps")
					.WithColumn("IncidentMapId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("IncidentCommandId").AsString(128).NotNullable()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(500).NotNullable()
					.WithColumn("Description").AsString(2000).Nullable()
					.WithColumn("CenterLatitude").AsString(int.MaxValue).Nullable()
					.WithColumn("CenterLongitude").AsString(int.MaxValue).Nullable()
					.WithColumn("ZoomLevel").AsString(int.MaxValue).Nullable()
					.WithColumn("ExpiresOn").AsDateTime2().Nullable()
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("UpdatedByUserId").AsString(450).Nullable()
					.WithColumn("UpdatedOn").AsDateTime2().Nullable()
					.WithColumn("DeletedOn").AsDateTime2().Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentMaps_Department_Call")
					.OnTable("IncidentMaps")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (Schema.Table("IncidentMapAnnotations").Exists() && !Schema.Table("IncidentMapAnnotations").Column("IncidentMapId").Exists())
				Alter.Table("IncidentMapAnnotations").AddColumn("IncidentMapId").AsString(128).Nullable();

			if (Schema.Table("CommandStructureNodes").Exists() && !Schema.Table("CommandStructureNodes").Column("LinkedMapId").Exists())
				Alter.Table("CommandStructureNodes").AddColumn("LinkedMapId").AsString(128).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("CommandStructureNodes").Exists() && Schema.Table("CommandStructureNodes").Column("LinkedMapId").Exists())
				Delete.Column("LinkedMapId").FromTable("CommandStructureNodes");

			if (Schema.Table("IncidentMapAnnotations").Exists() && Schema.Table("IncidentMapAnnotations").Column("IncidentMapId").Exists())
				Delete.Column("IncidentMapId").FromTable("IncidentMapAnnotations");

			if (Schema.Table("IncidentMaps").Exists())
				Delete.Table("IncidentMaps");
		}
	}
}
