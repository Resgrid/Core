using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(25)]
	public class M0025_AddingTypeToDepartmentAudio : Migration
	{
		public override void Up()
		{
			Alter.Table("DepartmentAudios").AddColumn("Type").AsString(256).Nullable();
		}

		public override void Down()
		{

		}
	}
}
