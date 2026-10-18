using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Managed activation and invitation sends (wizard plan section 17.8): ImportRuns.Request holds what a staff operator asked
	/// for (who to invite, when, which administrators to confirm, whether to sign off) while the run waits for a second staff
	/// member, the department's notice period and, when the grant asks for it, the department's own approval.
	/// </summary>
	[Migration(281)]
	public class M0281_AddImportRunRequest : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("ImportRuns").Column("Request").Exists())
				Alter.Table("ImportRuns").AddColumn("Request").AsString(int.MaxValue).Nullable();
		}

		public override void Down()
		{
			if (Schema.Table("ImportRuns").Column("Request").Exists())
				Delete.Column("Request").FromTable("ImportRuns");
		}
	}
}
