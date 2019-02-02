namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class FixingPlanAndLimitsIssue : DbMigration
	{
		public override void Up()
		{
			Sql(Config.DataConfig.PlansSql);
		}

		public override void Down()
		{
		}
	}
}
