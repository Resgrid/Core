using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// CONTRACT phase of the expand/relocate/contract move of member profile data (ADP plan
	/// section 5.1). M0132/M0133 expanded (department-scoped rows beside the global profile),
	/// M0134 relocated every member, and the application has read and written the department copy
	/// ever since. This removes the originals.
	///
	/// A userprofile is GLOBAL to a person across every department they belong to, so it can never
	/// be encrypted under one department's key — that is the whole reason the data moved. Leaving
	/// the plaintext originals behind would mean an enrolled department encrypts a member's
	/// identification number and address while an identical plaintext copy sits one table over.
	///
	/// Three things happen, in this order and only in this order:
	///   1. REFUSE if any member still has legacy data that relocation has not stamped as moved.
	///      A contract that runs early destroys the only copy, so this is a hard stop rather than
	///      a comment.
	///   2. Delete the addresses rows that ONLY a user profile referenced. A member's home address
	///      is now held department-scoped (and encrypted for an enrolled department); an orphaned
	///      plaintext row in addresses would defeat that. Rows any contact, department, station or
	///      department profile still points at are left completely alone.
	///   3. Clear the profile's address links and drop identificationnumber.
	///
	/// The address links are CLEARED rather than dropped: they are ordinary nullable columns that
	/// several code paths still read defensively, and a null reads the same as an absent column.
	///
	/// OUT OF SCOPE, deliberately: a userprofile with no active department membership. There is no
	/// department to scope its identification number or address to, so there is nowhere for
	/// relocation to move them and the guard in step 1 cannot cover them. ADP's whole premise is
	/// that this data cannot remain on a global profile, so for these accounts the contraction is
	/// the intended end state rather than an oversight: the values are removed with the columns.
	/// A member who rejoins a department re-enters their details on the department-scoped row.
	/// </summary>
	[Migration(141)]
	public class M0141_ContractLegacyMemberProfileDataPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (
		SELECT 1
		FROM userprofiles up
		INNER JOIN departmentmembers dm ON dm.userid = up.userid AND dm.isdeleted = false
		LEFT JOIN departmentmembersensitivedata s
			ON s.departmentid = dm.departmentid AND s.userid = up.userid
		WHERE (
				(up.identificationnumber IS NOT NULL AND btrim(up.identificationnumber::text) <> '')
				OR up.homeaddressid IS NOT NULL
				OR up.mailingaddressid IS NOT NULL
			)
		  AND (s.departmentmembersensitivedataid IS NULL OR s.legacyprofilerelocatedon IS NULL)) THEN
		RAISE EXCEPTION 'M0141 refused: members still hold legacy profile data that relocation has not stamped as moved. Run the member profile relocation to completion first - this migration destroys the originals.';
	END IF;
END $$;");

			// Only addresses nothing else references. A shared row (a contact's, a station's) is
			// left exactly as it is.
			Execute.Sql(@"
DELETE FROM addresses a
WHERE EXISTS (SELECT 1 FROM userprofiles up
              WHERE up.homeaddressid = a.addressid OR up.mailingaddressid = a.addressid)
  AND NOT EXISTS (SELECT 1 FROM contacts c
                  WHERE c.physicaladdressid = a.addressid OR c.mailingaddressid = a.addressid)
  AND NOT EXISTS (SELECT 1 FROM departments d WHERE d.addressid = a.addressid)
  AND NOT EXISTS (SELECT 1 FROM departmentgroups g WHERE g.addressid = a.addressid)
  AND NOT EXISTS (SELECT 1 FROM departmentprofiles p WHERE p.addressid = a.addressid);");

			Execute.Sql(@"
UPDATE userprofiles
SET homeaddressid = NULL, mailingaddressid = NULL
WHERE homeaddressid IS NOT NULL OR mailingaddressid IS NOT NULL;");

			if (Schema.Table("userprofiles").Column("identificationnumber").Exists())
				Delete.Column("identificationnumber").FromTable("userprofiles");
		}

		public override void Down()
		{
			// The column comes back empty. Its values moved to departmentmembersensitivedata long
			// before this ran and are encrypted there for an enrolled department, so there is
			// nothing to restore and nothing that could restore it — a rollback recreates the shape,
			// not the data. The deleted addresses rows do not come back either.
			if (!Schema.Table("userprofiles").Column("identificationnumber").Exists())
				Alter.Table("userprofiles").AddColumn("identificationnumber").AsCustom("citext").Nullable();
		}
	}
}
