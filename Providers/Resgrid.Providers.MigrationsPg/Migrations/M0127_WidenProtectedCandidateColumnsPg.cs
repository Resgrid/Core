using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Version-parity no-op for the SQL Server M0127 capacity migration (ADP plan section 22.2).
	/// PostgreSQL stores every audited string column as unbounded citext, so no resizes are required
	/// on this engine — the rgdp envelope (roughly 1.4 × plaintext + 70 characters) always fits.
	/// </summary>
	[Migration(127)]
	public class M0127_WidenProtectedCandidateColumnsPg : Migration
	{
		public override void Up()
		{
			// Intentionally empty; see summary.
		}

		public override void Down()
		{
			// Intentionally empty; see summary.
		}
	}
}
