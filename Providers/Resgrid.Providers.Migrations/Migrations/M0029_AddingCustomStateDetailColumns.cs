using FluentMigrator;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(29)]
	public class M0029_AddingCustomStateDetailColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("CustomStateDetails").AddColumn("BaseType").AsInt32().NotNullable().WithDefaultValue(-1);
			Alter.Table("CustomStateDetails").AddColumn("TTL").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{

		}
	}
}
