using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Adds an optional required personnel qualification to a unit role. PersonnelRoleId points at the
	/// PersonnelRole (e.g. "Paramedic") the person filling the seat must hold; PersonnelRoleRequired marks
	/// whether that qualification is a hard requirement (blocks assignment) or a preference (allowed but
	/// reports the unit as degraded). Drives unit staffing computation.
	/// </summary>
	[Migration(86)]
	public class M0086_AddUnitRolePersonnelRole : Migration
	{
		public override void Up()
		{
			if (Schema.Table("UnitRoles").Exists() && !Schema.Table("UnitRoles").Column("PersonnelRoleId").Exists())
				Alter.Table("UnitRoles").AddColumn("PersonnelRoleId").AsInt32().Nullable();

			if (Schema.Table("UnitRoles").Exists() && !Schema.Table("UnitRoles").Column("PersonnelRoleRequired").Exists())
				Alter.Table("UnitRoles").AddColumn("PersonnelRoleRequired").AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			if (Schema.Table("UnitRoles").Exists() && Schema.Table("UnitRoles").Column("PersonnelRoleRequired").Exists())
				Delete.Column("PersonnelRoleRequired").FromTable("UnitRoles");

			if (Schema.Table("UnitRoles").Exists() && Schema.Table("UnitRoles").Column("PersonnelRoleId").Exists())
				Delete.Column("PersonnelRoleId").FromTable("UnitRoles");
		}
	}
}
