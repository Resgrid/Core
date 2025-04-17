using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(34)]
	public class M0034_UpdatingContactNotesTable : Migration
	{
		public override void Up()
		{
			Alter.Table("ContactNotes").AlterColumn("ExpiresOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{

		}
	}
}
