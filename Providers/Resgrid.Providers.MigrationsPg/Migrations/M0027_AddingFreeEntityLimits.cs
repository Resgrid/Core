using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(27)]
	public class M0027_AddingFreeEntityLimits : Migration
	{
		public override void Up()
		{
			Insert.IntoTable("PlanLimits".ToLower()).Row(new { PlanId = 1, LimitType = 6, LimitValue = 10 });
		}

		public override void Down()
		{

		}
	}
}
