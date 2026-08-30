using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Per-application step-up exemptions for Advanced Data Protection (plan section 3.3).
	///
	/// A department can release named client apps from the second-factor prompt that guards a
	/// protected reveal. The column defaults to 0 — nothing exempt, every app prompts — and only an
	/// explicit, audited act by the managing member moves it. A department that never opens this
	/// setting keeps the stronger behaviour forever.
	///
	/// It exists because a dispatcher on a live incident cannot stop to read a code off a phone, and
	/// a prompt that lands mid-call is a safety problem rather than a security win. The exemption is
	/// per app so a department can keep the prompt on the web site, where the work is administrative,
	/// and take it off the dispatch console, where seconds count.
	/// </summary>
	[Migration(145)]
	public class M0145_AddAdpStepUpExemptClientsPg : Migration
	{
		private const string Table = "departmentdataprotectionpolicies";

		public override void Up()
		{
			// NOT NULL with a zero default: an existing row must come out of this migration
			// prompting for everything, and a future insert that forgets the column must too.
			if (!Schema.Table(Table).Column("stepupexemptclients").Exists())
				Alter.Table(Table)
					.AddColumn("stepupexemptclients").AsInt32().NotNullable().WithDefaultValue(0);
		}

		public override void Down()
		{
			// Reversible and safe in the strict direction: losing the column restores the prompt for
			// every app rather than removing it. No member data and no key material is involved.
			if (Schema.Table(Table).Column("stepupexemptclients").Exists())
				Delete.Column("stepupexemptclients").FromTable(Table);
		}
	}
}
