namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System;
	using System.Data.Entity.Migrations;

	public partial class AddedMissingOpenPreviewPlanLimits : DbMigration
	{
		public override void Up()
		{
			Sql(Config.DataConfig.PlanFixSql);
		}

		public override void Down()
		{
		}
	}
}
