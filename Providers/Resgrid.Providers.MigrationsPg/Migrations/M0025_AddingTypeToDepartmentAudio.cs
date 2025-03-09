using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(25)]
	public class M0025_AddingTypeToDepartmentAudio : Migration
	{
		public override void Up()
		{
			Alter.Table("DepartmentAudios".ToLower()).AddColumn("Type".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
