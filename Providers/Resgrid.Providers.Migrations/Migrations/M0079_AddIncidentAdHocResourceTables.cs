using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(79)]
	public class M0079_AddIncidentAdHocResourceTables : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentAdHocUnits").Exists())
			{
				Create.Table("IncidentAdHocUnits")
					.WithColumn("IncidentAdHocUnitId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("UnitTypeId").AsInt32().Nullable()
					.WithColumn("Type").AsString(int.MaxValue).Nullable()
					.WithColumn("ExternalAgencyName").AsString(int.MaxValue).Nullable()
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentAdHocUnits_Department_Call")
					.OnTable("IncidentAdHocUnits")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}

			if (!Schema.Table("IncidentAdHocPersonnel").Exists())
			{
				Create.Table("IncidentAdHocPersonnel")
					.WithColumn("IncidentAdHocPersonnelId").AsString(128).NotNullable().PrimaryKey()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("CallId").AsInt32().NotNullable()
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("Role").AsString(int.MaxValue).Nullable()
					.WithColumn("ExternalAgencyName").AsString(int.MaxValue).Nullable()
					.WithColumn("Contact").AsString(int.MaxValue).Nullable()
					.WithColumn("RidingResourceKind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RidingResourceId").AsString(450).Nullable()
					.WithColumn("CreatedByUserId").AsString(450).Nullable()
					.WithColumn("CreatedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn").AsDateTime2().Nullable();

				Create.Index("IX_IncidentAdHocPersonnel_Department_Call")
					.OnTable("IncidentAdHocPersonnel")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("CallId").Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("IncidentAdHocPersonnel");
			Delete.Table("IncidentAdHocUnits");
		}
	}
}
