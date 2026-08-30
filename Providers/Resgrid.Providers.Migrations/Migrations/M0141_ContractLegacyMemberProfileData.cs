using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// CONTRACT phase of the expand/relocate/contract move of member profile data (ADP plan
	/// section 5.1). M0132/M0133 expanded (department-scoped rows beside the global profile),
	/// M0134 relocated every member, and the application has read and written the department copy
	/// ever since. This removes the originals.
	///
	/// A UserProfile is GLOBAL to a person across every department they belong to, so it can never
	/// be encrypted under one department's key — that is the whole reason the data moved. Leaving
	/// the plaintext originals behind would mean an enrolled department encrypts a member's
	/// identification number and address while an identical plaintext copy sits one table over.
	///
	/// Three things happen, in this order and only in this order:
	///   1. REFUSE if any member still has legacy data that relocation has not stamped as moved.
	///      A contract that runs early destroys the only copy, so this is a hard stop rather than
	///      a comment.
	///   2. Delete the Address rows that ONLY a user profile referenced. A member's home address is
	///      now held department-scoped (and encrypted for an enrolled department); an orphaned
	///      plaintext row in Addresses would defeat that. Rows any contact, department, station or
	///      department profile still points at are left completely alone.
	///   3. Clear the profile's address links and drop IdentificationNumber.
	///
	/// The address links are CLEARED rather than dropped: they are ordinary nullable columns that
	/// several code paths still read defensively, and a null reads the same as an absent column.
	///
	/// OUT OF SCOPE, deliberately: a UserProfile with no active department membership. There is no
	/// department to scope its identification number or address to, so there is nowhere for
	/// relocation to move them and the guard in step 1 cannot cover them. ADP's whole premise is
	/// that this data cannot remain on a global profile, so for these accounts the contraction is
	/// the intended end state rather than an oversight: the values are removed with the columns.
	/// A member who rejoins a department re-enters their details on the department-scoped row.
	/// </summary>
	[Migration(141)]
	public class M0141_ContractLegacyMemberProfileData : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF EXISTS (
	SELECT 1
	FROM [UserProfiles] up
	INNER JOIN [DepartmentMembers] dm ON dm.[UserId] = up.[UserId] AND dm.[IsDeleted] = 0
	LEFT JOIN [DepartmentMemberSensitiveData] s
		ON s.[DepartmentId] = dm.[DepartmentId] AND s.[UserId] = up.[UserId]
	WHERE (
			(up.[IdentificationNumber] IS NOT NULL AND LTRIM(RTRIM(up.[IdentificationNumber])) <> '')
			OR up.[HomeAddressId] IS NOT NULL
			OR up.[MailingAddressId] IS NOT NULL
		)
	  AND (s.[DepartmentMemberSensitiveDataId] IS NULL OR s.[LegacyProfileRelocatedOn] IS NULL))
	THROW 51000, 'M0141 refused: members still hold legacy profile data that relocation has not stamped as moved. Run the member profile relocation to completion first — this migration destroys the originals.', 1;");

			// Only addresses nothing else references. A shared row (a contact's, a station's) is
			// left exactly as it is.
			Execute.Sql(@"
DELETE a
FROM [Addresses] a
WHERE EXISTS (SELECT 1 FROM [UserProfiles] up
              WHERE up.[HomeAddressId] = a.[AddressId] OR up.[MailingAddressId] = a.[AddressId])
  AND NOT EXISTS (SELECT 1 FROM [Contacts] c
                  WHERE c.[PhysicalAddressId] = a.[AddressId] OR c.[MailingAddressId] = a.[AddressId])
  AND NOT EXISTS (SELECT 1 FROM [Departments] d WHERE d.[AddressId] = a.[AddressId])
  AND NOT EXISTS (SELECT 1 FROM [DepartmentGroups] g WHERE g.[AddressId] = a.[AddressId])
  AND NOT EXISTS (SELECT 1 FROM [DepartmentProfiles] p WHERE p.[AddressId] = a.[AddressId]);");

			Execute.Sql(@"
UPDATE [UserProfiles]
SET [HomeAddressId] = NULL, [MailingAddressId] = NULL
WHERE [HomeAddressId] IS NOT NULL OR [MailingAddressId] IS NOT NULL;");

			if (Schema.Table("UserProfiles").Column("IdentificationNumber").Exists())
				Delete.Column("IdentificationNumber").FromTable("UserProfiles");
		}

		public override void Down()
		{
			// The column comes back empty. Its values moved to DepartmentMemberSensitiveData long
			// before this ran and are encrypted there for an enrolled department, so there is
			// nothing to restore and nothing that could restore it — a rollback recreates the shape,
			// not the data. The deleted Address rows do not come back either.
			if (!Schema.Table("UserProfiles").Column("IdentificationNumber").Exists())
				Alter.Table("UserProfiles").AddColumn("IdentificationNumber").AsString(int.MaxValue).Nullable();
		}
	}
}
