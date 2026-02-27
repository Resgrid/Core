using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL counterpart for M0044. The verification-code columns were already
	/// defined as <c>citext</c> (unbounded) in M0040, so no DDL change is required.
	/// This migration exists solely to keep the SQL Server and PostgreSQL version
	/// numbers in sync.
	/// </summary>
	[Migration(44)]
	public class M0044_WidenVerificationCodeColumnsPg : Migration
	{
		public override void Up()
		{
			// citext has no length limit — nothing to alter.
		}

		public override void Down()
		{
			// Nothing to undo.
		}
	}
}

