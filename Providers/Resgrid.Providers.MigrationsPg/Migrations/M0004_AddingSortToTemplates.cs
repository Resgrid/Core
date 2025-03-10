using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(4)]
	public class M0004_AddingSortToTemplates : Migration
	{
		public override void Up()
		{
			Alter.Table("CallQuickTemplates".ToLower()).AddColumn("Sort".ToLower()).AsInt32().Nullable();
		}

		public override void Down()
		{
			
		}
	}
}
