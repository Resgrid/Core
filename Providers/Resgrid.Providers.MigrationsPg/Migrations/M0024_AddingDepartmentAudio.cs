using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(24)]
	public class M0024_AddingDepartmentAudio : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentAudios")
			   .WithColumn("DepartmentAudioId").AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("DepartmentAudioType").AsInt32().NotNullable()
			   .WithColumn("Name").AsCustom("citext").Nullable()
			   .WithColumn("Data").AsCustom("citext").Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_DepartmentAudios_Department")
				.FromTable("DepartmentAudios").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
