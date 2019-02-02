namespace Resgrid.Repositories.DataRepository.Migrations
{
	using System.Data.Entity.Migrations;

	public partial class AddedDefaultCustomStates : DbMigration
	{
		public override void Up()
		{
			Sql(Config.DataConfig.TestDepartmentCustomStatesSql);
		}

		public override void Down()
		{
		}
	}
}
