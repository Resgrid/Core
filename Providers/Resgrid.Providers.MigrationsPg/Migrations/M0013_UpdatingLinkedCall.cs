using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(13)]
	public class M0013_UpdatingLinkedCall : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls".ToLower()).AddColumn("LinkedCallId".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
