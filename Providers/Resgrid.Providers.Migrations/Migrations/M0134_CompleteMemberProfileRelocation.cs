using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Completes the EXPAND half of moving member identification numbers and addresses off the
	/// global UserProfiles row onto the department-scoped DepartmentMemberSensitiveData row
	/// (ADP plan section 5.1), and adds the marker that makes the move verifiable.
	///
	/// M0132 inserted rows only for members who had an identification number, and M0133 backfilled
	/// addresses only into rows that already existed — so a member with an address but no
	/// identification number was moved by neither. This migration closes that gap and then stamps
	/// every relocated row, so "has this member's legacy data been moved?" is a column lookup rather
	/// than an inference from which target fields happen to be empty. Inferring it from emptiness is
	/// wrong: a member who deliberately CLEARS their department identification number would look
	/// un-relocated forever and have the legacy value pushed back on top of them.
	///
	/// Rows already carrying ciphertext (IsProtected = 1) are deliberately left alone here. Writing
	/// plaintext into an enrolled department's row would poison it, so those departments are
	/// relocated by MemberProfileRelocationService instead, which goes through the ADP write path
	/// and encrypts the moved value as it lands. They stay unstamped until it does.
	///
	/// The legacy source columns are NOT cleared. That is the contract migration's job, once this is
	/// deployed, the relocation backlog reads zero, and a rollback can no longer lose data.
	/// </summary>
	[Migration(134)]
	public class M0134_CompleteMemberProfileRelocation : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentMemberSensitiveData").Column("LegacyProfileRelocatedOn").Exists())
				Alter.Table("DepartmentMemberSensitiveData")
					.AddColumn("LegacyProfileRelocatedOn").AsDateTime2().Nullable();

			// 1) Rows for members whose legacy profile holds an address but no identification number
			//    (M0132 skipped them) or who joined a department after M0132 ran.
			Execute.Sql(@"
INSERT INTO [DepartmentMemberSensitiveData] ([DepartmentId], [UserId], [ProtectionId], [IsProtected], [CreatedOn])
SELECT dm.[DepartmentId], dm.[UserId], LOWER(REPLACE(CONVERT(NVARCHAR(64), NEWID()), '-', '')), 0, GETUTCDATE()
FROM [DepartmentMembers] dm
INNER JOIN [UserProfiles] up ON up.[UserId] = dm.[UserId]
WHERE dm.[IsDeleted] = 0
  AND (up.[HomeAddressId] IS NOT NULL OR up.[MailingAddressId] IS NOT NULL
       OR (up.[IdentificationNumber] IS NOT NULL AND LTRIM(RTRIM(up.[IdentificationNumber])) <> ''))
  AND NOT EXISTS (
      SELECT 1 FROM [DepartmentMemberSensitiveData] s
      WHERE s.[DepartmentId] = dm.[DepartmentId] AND s.[UserId] = dm.[UserId]);");

			// 2) Fill the three families into any still-empty, still-plaintext target. Re-runnable:
			//    a department-specific value someone has already entered is never overwritten.
			Execute.Sql(@"
UPDATE s
SET s.[IdentificationNumber] = up.[IdentificationNumber]
FROM [DepartmentMemberSensitiveData] s
INNER JOIN [UserProfiles] up ON up.[UserId] = s.[UserId]
WHERE s.[IdentificationNumber] IS NULL AND s.[IsProtected] = 0 AND s.[LegacyProfileRelocatedOn] IS NULL
  AND up.[IdentificationNumber] IS NOT NULL AND LTRIM(RTRIM(up.[IdentificationNumber])) <> '';");

			Execute.Sql(@"
UPDATE s
SET s.[HomeAddress1] = ha.[Address1], s.[HomeCity] = ha.[City], s.[HomeState] = ha.[State],
    s.[HomePostalCode] = ha.[PostalCode], s.[HomeCountry] = ha.[Country]
FROM [DepartmentMemberSensitiveData] s
INNER JOIN [UserProfiles] up ON up.[UserId] = s.[UserId]
INNER JOIN [Addresses] ha ON ha.[AddressId] = up.[HomeAddressId]
WHERE s.[HomeAddress1] IS NULL AND s.[IsProtected] = 0 AND s.[LegacyProfileRelocatedOn] IS NULL;");

			Execute.Sql(@"
UPDATE s
SET s.[MailingAddress1] = ma.[Address1], s.[MailingCity] = ma.[City], s.[MailingState] = ma.[State],
    s.[MailingPostalCode] = ma.[PostalCode], s.[MailingCountry] = ma.[Country]
FROM [DepartmentMemberSensitiveData] s
INNER JOIN [UserProfiles] up ON up.[UserId] = s.[UserId]
INNER JOIN [Addresses] ma ON ma.[AddressId] = up.[MailingAddressId]
WHERE s.[MailingAddress1] IS NULL AND s.[IsProtected] = 0 AND s.[LegacyProfileRelocatedOn] IS NULL;");

			// 3) Stamp what moved. Every plaintext row is now relocated — including rows whose member
			//    simply had nothing to move, which are relocated by definition and must not be
			//    revisited by the worker on every pass.
			Execute.Sql(@"
UPDATE [DepartmentMemberSensitiveData]
SET [LegacyProfileRelocatedOn] = GETUTCDATE()
WHERE [LegacyProfileRelocatedOn] IS NULL AND [IsProtected] = 0;");
		}

		public override void Down()
		{
			// The relocated copies stay: they may have been edited per department or encrypted since,
			// and the source was never cleared, so there is nothing to restore. Dropping only the
			// marker returns every row to the backlog, which is safe — relocation is idempotent and
			// refuses to overwrite a populated target.
			if (Schema.Table("DepartmentMemberSensitiveData").Column("LegacyProfileRelocatedOn").Exists())
				Delete.Column("LegacyProfileRelocatedOn").FromTable("DepartmentMemberSensitiveData");
		}
	}
}
