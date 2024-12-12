using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(3)]
	public class M0003_AddingUnitActiveRoles : Migration
	{
		public override void Up()
		{
			Create.Table("UnitActiveRoles")
			   .WithColumn("UnitActiveRoleId").AsInt32().NotNullable().PrimaryKey().Identity()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("UnitId").AsInt32().NotNullable()
			   .WithColumn("Role").AsCustom("citext").NotNullable()
			   .WithColumn("UserId").AsCustom("citext").NotNullable()
			   .WithColumn("UpdatedOn").AsDateTime().NotNullable()
			   .WithColumn("UpdatedBy").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_UnitActiveRoles_Departments")
				.FromTable("UnitActiveRoles").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.ForeignKey("FK_UnitActiveRoles_Units")
				.FromTable("UnitActiveRoles").ForeignColumn("UnitId")
				.ToTable("Units").PrimaryColumn("UnitId");
		}

		public override void Down()
		{
			Delete.Table("UnitActiveRoles");
		}
	}
}
