using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(29)]
	public class M0029_AddingCustomStateDetailColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("CustomStateDetails".ToLower()).AddColumn("BaseType".ToLower()).AsInt32().NotNullable().WithDefaultValue(-1);
			Alter.Table("CustomStateDetails".ToLower()).AddColumn("TTL".ToLower()).AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{

		}
	}
}
