using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(15)]
	public class M0015_AddingMissingWorkshiftValues : Migration
	{
		public override void Up()
		{
			Alter.Table("Workshifts").AddColumn("Description").AsString().Nullable();
			Alter.Table("WorkshiftFills").AddColumn("ReferenceId").AsString(128).Nullable();
		}

		public override void Down()
		{

		}
	}
}
