using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(13)]
	public class M0013_UpdatingLinkedCall : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AddColumn("LinkedCallId").AsInt32().Nullable();
		}

		public override void Down()
		{

		}
	}
}
