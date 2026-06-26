using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of the Autofills.Data widening: ensure the call-note / autofill template body is unbounded
	/// (text) so long templates don't truncate. See M0084_WidenAutofillDataColumn (SQL Server).
	/// </summary>
	[Migration(84)]
	public class M0084_WidenAutofillDataColumnPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("autofills").Exists() && Schema.Table("autofills").Column("data").Exists())
			{
				Alter.Table("autofills").AlterColumn("data").AsCustom("text").Nullable();
			}
		}

		public override void Down()
		{
			// No-op: narrowing could truncate existing data.
		}
	}
}
