using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(15)]
	public class M0015_AddingMissingWorkshiftValues : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts".ToLower()).AddColumn("Description".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("WorkshiftFills".ToLower()).AddColumn("ReferenceId".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
