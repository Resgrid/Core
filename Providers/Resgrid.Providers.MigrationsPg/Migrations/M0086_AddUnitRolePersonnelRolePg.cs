using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Adds an optional required personnel qualification to a unit role (PostgreSQL). PersonnelRoleId points
	/// at the PersonnelRole (e.g. "Paramedic") the person filling the seat must hold; PersonnelRoleRequired
	/// marks whether that qualification is a hard requirement (blocks assignment) or a preference (allowed
	/// but reports the unit as degraded). Drives unit staffing computation.
	/// </summary>
	[Migration(86)]
	public class M0086_AddUnitRolePersonnelRolePg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("UnitRoles".ToLower()).Exists() && !Schema.Table("UnitRoles".ToLower()).Column("PersonnelRoleId".ToLower()).Exists())
				Alter.Table("UnitRoles".ToLower()).AddColumn("PersonnelRoleId".ToLower()).AsInt32().Nullable();

			if (Schema.Table("UnitRoles".ToLower()).Exists() && !Schema.Table("UnitRoles".ToLower()).Column("PersonnelRoleRequired".ToLower()).Exists())
				Alter.Table("UnitRoles".ToLower()).AddColumn("PersonnelRoleRequired".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
		}

		public override void Down()
		{
			if (Schema.Table("UnitRoles".ToLower()).Exists() && Schema.Table("UnitRoles".ToLower()).Column("PersonnelRoleRequired".ToLower()).Exists())
				Delete.Column("PersonnelRoleRequired".ToLower()).FromTable("UnitRoles".ToLower());

			if (Schema.Table("UnitRoles".ToLower()).Exists() && Schema.Table("UnitRoles".ToLower()).Column("PersonnelRoleId".ToLower()).Exists())
				Delete.Column("PersonnelRoleId".ToLower()).FromTable("UnitRoles".ToLower());
		}
	}
}
