using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Postgres twin of M0134_CompleteMemberProfileRelocation. See that migration for why the move
	/// needs a marker column rather than inferring "already relocated" from an empty target, and why
	/// rows that already carry ciphertext are left for MemberProfileRelocationService.
	/// </summary>
	[Migration(134)]
	public class M0134_CompleteMemberProfileRelocationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentmembersensitivedata").Column("legacyprofilerelocatedon").Exists())
				Alter.Table("departmentmembersensitivedata")
					.AddColumn("legacyprofilerelocatedon").AsDateTime2().Nullable();

			// 1) Rows for members whose legacy profile holds an address but no identification number
			//    (M0132 skipped them) or who joined a department after M0132 ran. md5(random()) keeps
			//    ProtectionId's 32-hex shape without depending on the pgcrypto extension.
			Execute.Sql(@"
INSERT INTO departmentmembersensitivedata (departmentid, userid, protectionid, isprotected, createdon)
SELECT dm.departmentid, dm.userid, md5(random()::text || clock_timestamp()::text), false, (now() at time zone 'utc')
FROM departmentmembers dm
INNER JOIN userprofiles up ON up.userid = dm.userid
WHERE dm.isdeleted = false
  AND (up.homeaddressid IS NOT NULL OR up.mailingaddressid IS NOT NULL
       OR (up.identificationnumber IS NOT NULL AND btrim(up.identificationnumber) <> ''))
  AND NOT EXISTS (
      SELECT 1 FROM departmentmembersensitivedata s
      WHERE s.departmentid = dm.departmentid AND s.userid = dm.userid);");

			// 2) Fill the three families into any still-empty, still-plaintext target.
			Execute.Sql(@"
UPDATE departmentmembersensitivedata s
SET identificationnumber = up.identificationnumber
FROM userprofiles up
WHERE up.userid = s.userid
  AND s.identificationnumber IS NULL AND s.isprotected = false AND s.legacyprofilerelocatedon IS NULL
  AND up.identificationnumber IS NOT NULL AND btrim(up.identificationnumber) <> '';");

			Execute.Sql(@"
UPDATE departmentmembersensitivedata s
SET homeaddress1 = ha.address1, homecity = ha.city, homestate = ha.state,
    homepostalcode = ha.postalcode, homecountry = ha.country
FROM userprofiles up
INNER JOIN addresses ha ON ha.addressid = up.homeaddressid
WHERE up.userid = s.userid
  AND s.homeaddress1 IS NULL AND s.isprotected = false AND s.legacyprofilerelocatedon IS NULL;");

			Execute.Sql(@"
UPDATE departmentmembersensitivedata s
SET mailingaddress1 = ma.address1, mailingcity = ma.city, mailingstate = ma.state,
    mailingpostalcode = ma.postalcode, mailingcountry = ma.country
FROM userprofiles up
INNER JOIN addresses ma ON ma.addressid = up.mailingaddressid
WHERE up.userid = s.userid
  AND s.mailingaddress1 IS NULL AND s.isprotected = false AND s.legacyprofilerelocatedon IS NULL;");

			// 3) Stamp what moved, including rows whose member had nothing to move.
			Execute.Sql(@"
UPDATE departmentmembersensitivedata
SET legacyprofilerelocatedon = (now() at time zone 'utc')
WHERE legacyprofilerelocatedon IS NULL AND isprotected = false;");
		}

		public override void Down()
		{
			if (Schema.Table("departmentmembersensitivedata").Column("legacyprofilerelocatedon").Exists())
				Delete.Column("legacyprofilerelocatedon").FromTable("departmentmembersensitivedata");
		}
	}
}
