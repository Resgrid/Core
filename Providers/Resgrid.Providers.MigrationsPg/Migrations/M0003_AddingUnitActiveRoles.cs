using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(3)]
	public class M0003_AddingUnitActiveRoles : Migration
	{
		public override void Up()
		{
			Create.Table("UnitActiveRoles".ToLower())
			   .WithColumn("UnitActiveRoleId".ToLower()).AsInt32().NotNullable().PrimaryKey().Identity()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("UnitId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Role".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("UpdatedOn".ToLower()).AsDateTime().NotNullable()
			   .WithColumn("UpdatedBy".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_UnitActiveRoles_Departments")
				.FromTable("UnitActiveRoles".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_UnitActiveRoles_Units")
				.FromTable("UnitActiveRoles".ToLower()).ForeignColumn("UnitId".ToLower())
				.ToTable("Units".ToLower()).PrimaryColumn("UnitId".ToLower());
		}

		public override void Down()
		{
			Delete.Table("UnitActiveRoles");
		}
	}
}
