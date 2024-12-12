using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(19)]
	public class M0019_AddingDeleteReasonToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls").AddColumn("DeletedReason").AsCustom("citext").Nullable();
			Alter.Table("Calls").AddColumn("DeletedByUserId").AsCustom("citext").Nullable();
			Alter.Table("Calls").AddColumn("DeletedOn").AsDateTime2().Nullable();
		}

		public override void Down()
		{

		}
	}
}
