using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(27)]
	public class M0027_AddingFreeEntityLimits : Migration
	{
		public override void Up()
		{
			Insert.IntoTable("PlanLimits".ToLower()).Row(new { planlimitid = 8, planid = 1, limittype = 6, limitvalue = 10 });
		}

		public override void Down()
		{

		}
	}
}
