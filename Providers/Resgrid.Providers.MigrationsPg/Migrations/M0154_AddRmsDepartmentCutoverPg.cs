using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS) department cutover (RMS plan section 4.1, registry M0154): rmsdepartmentcutovers
	/// (one append-only activation row per department) and rmsdepartmentcutoverevents (its audited
	/// history). PostgreSQL twin of the SQL Server migration. Existence-guarded for safe retry.
	/// </summary>
	[Migration(154)]
	public class M0154_AddRmsDepartmentCutoverPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsdepartmentcutovers").Exists())
			{
				Create.Table("rmsdepartmentcutovers")
					.WithColumn("rmsdepartmentcutoverid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("activatedon").AsDateTime2().NotNullable()
					.WithColumn("activatedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("sourcelegacylogcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcelegacyunitlogcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcechecksum").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("revertedon").AsDateTime2().Nullable()
					.WithColumn("revertedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("permissionmappingjson").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsdepartmentcutovers_departmentid ON rmsdepartmentcutovers (departmentid);");
			}

			if (!Schema.Table("rmsdepartmentcutoverevents").Exists())
			{
				Create.Table("rmsdepartmentcutoverevents")
					.WithColumn("rmsdepartmentcutovereventid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("rmsdepartmentcutoverid").AsInt32().NotNullable()
					.WithColumn("eventtype").AsCustom("citext").NotNullable()
					.WithColumn("actoruserid").AsCustom("citext").Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsdepartmentcutoverevents_department_cutover ON rmsdepartmentcutoverevents (departmentid, rmsdepartmentcutoverid);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("rmsdepartmentcutoverevents").Exists())
				Delete.Table("rmsdepartmentcutoverevents");

			if (Schema.Table("rmsdepartmentcutovers").Exists())
				Delete.Table("rmsdepartmentcutovers");
		}
	}
}
