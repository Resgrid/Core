using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(28)]
	public class M0028_AddingDeletedWorkshiftColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts").AddColumn("DeletedOn").AsDateTime2().Nullable();
			Alter.Table("Workshifts").AddColumn("DeletedById").AsString(128).Nullable();
		}

		public override void Down()
		{

		}
	}
}
