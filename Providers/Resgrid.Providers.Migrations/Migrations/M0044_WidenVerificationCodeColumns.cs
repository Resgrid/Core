using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Widens the three verification-code columns on UserProfiles from nvarchar(16) to
	/// nvarchar(512) so they can hold AES-256/Base64-encoded ciphertext produced by
	/// IEncryptionService (raw Base64 of a 16-byte IV + 16-byte cipher block is ~44 chars;
	/// using 512 gives ample headroom for future algorithm changes).
	/// </summary>
	[Migration(44)]
	public class M0044_WidenVerificationCodeColumns : Migration
	{
		public override void Up()
		{
			Alter.Table("UserProfiles")
				.AlterColumn("EmailVerificationCode").AsString(512).Nullable()
				.AlterColumn("MobileVerificationCode").AsString(512).Nullable()
				.AlterColumn("HomeVerificationCode").AsString(512).Nullable();
		}

		public override void Down()
		{
			Alter.Table("UserProfiles")
				.AlterColumn("EmailVerificationCode").AsString(16).Nullable()
				.AlterColumn("MobileVerificationCode").AsString(16).Nullable()
				.AlterColumn("HomeVerificationCode").AsString(16).Nullable();
		}
	}
}

