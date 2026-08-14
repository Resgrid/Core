using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Number-parity no-op for the SQL Server M0114. Postgres created systemaudits.data as
	/// citext, which is unbounded, so there is nothing to widen.
	/// </summary>
	[Migration(114)]
	public class M0114_WidenSystemAuditsDataColumnPg : Migration
	{
		public override void Up()
		{
		}

		public override void Down()
		{
		}
	}
}
