using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of the Addresses widening. PG string columns are typically citext/unbounded (so the SQL-Server
	/// 8152 truncation does not occur here), but ensure the free-text street address is unbounded for parity.
	/// See M0085_WidenAddressColumns (SQL Server).
	/// </summary>
	[Migration(85)]
	public class M0085_WidenAddressColumnsPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("addresses").Exists() && Schema.Table("addresses").Column("address1").Exists())
			{
				Alter.Table("addresses").AlterColumn("address1").AsCustom("text").NotNullable();
			}
		}

		public override void Down()
		{
			// No-op: narrowing could truncate existing data.
		}
	}
}
