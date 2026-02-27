using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Ensures the AspNetUserTokens table exists and has an index on UserId for efficient
	/// authenticator key and recovery code lookups used by TOTP 2FA.
	/// The table itself is created by M0001 / EF0001 migrations; this migration is a safe
	/// no-op if those have already run and adds a named index if absent.
	/// </summary>
	[Migration(41)]
	public class M0041_EnsureAspNetUserTokensIndex : Migration
	{
		public override void Up()
		{
			// Create the table if somehow absent (e.g. fresh install that skipped EF migration)
			if (!Schema.Table("AspNetUserTokens").Exists())
			{
				Create.Table("AspNetUserTokens")
					.WithColumn("UserId").AsString(450).NotNullable()
					.WithColumn("LoginProvider").AsString(128).NotNullable()
					.WithColumn("Name").AsString(128).NotNullable()
					.WithColumn("Value").AsString(int.MaxValue).Nullable();

				Create.PrimaryKey("PK_AspNetUserTokens")
					.OnTable("AspNetUserTokens")
					.Columns("UserId", "LoginProvider", "Name");
			}

			// Add a non-clustered index on UserId for fast token lookups
			if (!Schema.Table("AspNetUserTokens").Index("IX_AspNetUserTokens_UserId").Exists())
			{
				Create.Index("IX_AspNetUserTokens_UserId")
					.OnTable("AspNetUserTokens")
					.OnColumn("UserId");
			}
		}

		public override void Down()
		{
			if (Schema.Table("AspNetUserTokens").Index("IX_AspNetUserTokens_UserId").Exists())
				Delete.Index("IX_AspNetUserTokens_UserId").OnTable("AspNetUserTokens");
		}
	}
}

