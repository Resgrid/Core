using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(28)]
	public class M0028_AddingDeletedWorkshiftColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts".ToLower()).AddColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();
			Alter.Table("Workshifts".ToLower()).AddColumn("DeletedById".ToLower()).AsCustom("citext").Nullable();
		}

		public override void Down()
		{

		}
	}
}
