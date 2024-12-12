using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(28)]
	public class M0028_AddingDeletedWorkshiftColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts").AddColumn("DeletedOn").AsDateTime2().Nullable();
			Alter.Table("Workshifts").AddColumn("DeletedById").AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
