using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(15)]
	public class M0015_AddingMissingWorkshiftValues : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts").AddColumn("Description").AsCustom("citext").Nullable();
			Alter.Table("WorkshiftFills").AddColumn("ReferenceId").AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
