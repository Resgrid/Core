using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(24)]
	public class M0024_AddingDepartmentAudio : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentAudios".ToLower())
			   .WithColumn("DepartmentAudioId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("DepartmentAudioType".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("Data".ToLower()).AsCustom("citext").Nullable()
			   .WithColumn("AddedOn".ToLower()).AsDateTime2().NotNullable()
			   .WithColumn("AddedByUserId".ToLower()).AsCustom("citext").NotNullable();

			Create.ForeignKey("FK_DepartmentAudios_Department")
				.FromTable("DepartmentAudios".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());
		}

		public override void Down()
		{

		}
	}
}
