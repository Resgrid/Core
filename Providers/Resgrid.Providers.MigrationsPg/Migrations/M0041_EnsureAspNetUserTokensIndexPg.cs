using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL counterpart of M0041 — ensures the AspNetUserTokens table exists
	/// and has an index on UserId for TOTP 2FA authenticator key and recovery code lookups.
	/// </summary>
	[Migration(41)]
	public class M0041_EnsureAspNetUserTokensIndexPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("AspNetUserTokens".ToLower()).Exists())
			{
				Create.Table("AspNetUserTokens".ToLower())
					.WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("LoginProvider".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("Name".ToLower()).AsCustom("citext").NotNullable()
					.WithColumn("Value".ToLower()).AsCustom("citext").Nullable();

				Create.PrimaryKey("PK_AspNetUserTokens".ToLower())
					.OnTable("AspNetUserTokens".ToLower())
					.Columns("UserId".ToLower(), "LoginProvider".ToLower(), "Name".ToLower());
			}

			if (!Schema.Table("AspNetUserTokens".ToLower()).Index("IX_AspNetUserTokens_UserId".ToLower()).Exists())
			{
				Create.Index("IX_AspNetUserTokens_UserId".ToLower())
					.OnTable("AspNetUserTokens".ToLower())
					.OnColumn("UserId".ToLower());
			}
		}

		public override void Down()
		{
			if (Schema.Table("AspNetUserTokens".ToLower()).Index("IX_AspNetUserTokens_UserId".ToLower()).Exists())
				Delete.Index("IX_AspNetUserTokens_UserId".ToLower()).OnTable("AspNetUserTokens".ToLower());
		}
	}
}

