using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(79)]
	public class M0079_AddIncidentAdHocResourceTablesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("IncidentAdHocUnits".ToLower()).Exists())
			{
				Create.Table("IncidentAdHocUnits".ToLower())
					.WithColumn("IncidentAdHocUnitId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("UnitTypeId".ToLower()).AsInt32().Nullable()
					.WithColumn("Type".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ExternalAgencyName".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentAdHocUnits_Department_Call".ToLower())
					.OnTable("IncidentAdHocUnits".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}

			if (!Schema.Table("IncidentAdHocPersonnel".ToLower()).Exists())
			{
				Create.Table("IncidentAdHocPersonnel".ToLower())
					.WithColumn("IncidentAdHocPersonnelId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
					.WithColumn("CallId".ToLower()).AsInt32().NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Role".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("ExternalAgencyName".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("Contact".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("RidingResourceKind".ToLower()).AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("RidingResourceId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedByUserId".ToLower()).AsCustom("citext").Nullable()
					.WithColumn("CreatedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("ReleasedOn".ToLower()).AsDateTime2().Nullable();

				Create.Index("IX_IncidentAdHocPersonnel_Department_Call".ToLower())
					.OnTable("IncidentAdHocPersonnel".ToLower())
					.OnColumn("DepartmentId".ToLower()).Ascending()
					.OnColumn("CallId".ToLower()).Ascending();
			}
		}

		public override void Down()
		{
			Delete.Table("IncidentAdHocPersonnel".ToLower());
			Delete.Table("IncidentAdHocUnits".ToLower());
		}
	}
}
