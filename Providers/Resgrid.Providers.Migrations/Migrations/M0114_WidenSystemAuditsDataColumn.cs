using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// SystemAudits.Data was created as the FluentMigrator default nvarchar(255). Audit writers
	/// (SCIM operations, email receive, auth failures) build free-form payloads that routinely
	/// exceed that, and SQL Server then fails the whole insert with "String or binary data would
	/// be truncated" — losing the audit row entirely. Data is never indexed or filtered, only
	/// displayed, so nvarchar(max) is safe.
	/// </summary>
	[Migration(114)]
	public class M0114_WidenSystemAuditsDataColumn : Migration
	{
		public override void Up()
		{
			Alter.Table("SystemAudits").AlterColumn("Data").AsCustom("nvarchar(max)").Nullable();
		}

		public override void Down()
		{
			// Shrinking back to nvarchar(255) would truncate stored audit payloads; one-way.
		}
	}
}
