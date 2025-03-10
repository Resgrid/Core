using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(19)]
	public class M0019_AddingDeleteReasonToCalls : Migration
	{
		public override void Up()
		{
			Alter.Table("Calls".ToLower()).AddColumn("DeletedReason".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("Calls".ToLower()).AddColumn("DeletedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("Calls".ToLower()).AddColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();
		}

		public override void Down()
		{

		}
	}
}
