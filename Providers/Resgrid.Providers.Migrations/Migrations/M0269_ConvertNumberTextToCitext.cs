using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Version-parity no-op for the PostgreSQL M0269, which moves every human-readable number stored as text (call-number
	/// sequence keys, Records prevention and investigation numbers, business operations reference numbers, certification
	/// type codes) to citext.
	/// SQL Server's nvarchar columns already compare case-insensitively under the database collation, so nothing changes here.
	/// </summary>
	[Migration(269)]
	public class M0269_ConvertNumberTextToCitext : Migration
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
