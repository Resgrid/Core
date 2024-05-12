using FluentMigrator;
using FluentMigrator.SqlServer;
using System;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(27)]
	public class M0027_AddingFreeEntityLimits : Migration
	{
		public override void Up()
		{
			Insert.IntoTable("PlanLimits").Row(new { PlanId = 1, LimitType = 6, LimitValue = 10 });
		}

		public override void Down()
		{

		}
	}
}
