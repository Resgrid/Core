using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// The lease on a waiting call's dispatch (see the SQL Server M0266).
	/// </summary>
	[Migration(266)]
	public class M0266_AddCallDispatchClaimPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE calls ADD COLUMN IF NOT EXISTS dispatchclaimedon timestamp without time zone NULL;");
		}

		public override void Down()
		{
			Execute.Sql("ALTER TABLE calls DROP COLUMN IF EXISTS dispatchclaimedon;");
		}
	}
}
