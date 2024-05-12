using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(24)]
	public class M0024_AddingDepartmentAudio : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentAudios")
			   .WithColumn("DepartmentAudioId").AsString(128).NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId").AsInt32().NotNullable()
			   .WithColumn("DepartmentAudioType").AsInt32().NotNullable()
			   .WithColumn("Name").AsString().Nullable()
			   .WithColumn("Data").AsString().Nullable()
			   .WithColumn("AddedOn").AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId").AsString(128).NotNullable();

			Create.ForeignKey("FK_DepartmentAudios_Department")
				.FromTable("DepartmentAudios").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");
		}

		public override void Down()
		{

		}
	}
}
