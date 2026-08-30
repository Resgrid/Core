using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department-scoped member addresses (ADP plan section 5.1). See the SQL Server counterpart:
	/// stored as columns rather than a link to the shared addresses table, because an addresses row
	/// has no owner and encrypting it with one department's key would break every other reader.
	/// EXPAND phase — the legacy profile address links are left intact and backfilled from.
	/// </summary>
	[Migration(133)]
	public class M0133_AddMemberDepartmentAddressesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentmembersensitivedata").Column("homeaddress1").Exists())
				Alter.Table("departmentmembersensitivedata")
					.AddColumn("homeaddress1").AsCustom("citext").Nullable()
					.AddColumn("homecity").AsCustom("citext").Nullable()
					.AddColumn("homestate").AsCustom("citext").Nullable()
					.AddColumn("homepostalcode").AsCustom("citext").Nullable()
					.AddColumn("homecountry").AsCustom("citext").Nullable()
					.AddColumn("mailingaddress1").AsCustom("citext").Nullable()
					.AddColumn("mailingcity").AsCustom("citext").Nullable()
					.AddColumn("mailingstate").AsCustom("citext").Nullable()
					.AddColumn("mailingpostalcode").AsCustom("citext").Nullable()
					.AddColumn("mailingcountry").AsCustom("citext").Nullable();

			Execute.Sql(@"
UPDATE departmentmembersensitivedata s
SET homeaddress1 = ha.address1, homecity = ha.city, homestate = ha.state,
    homepostalcode = ha.postalcode, homecountry = ha.country
FROM userprofiles up
INNER JOIN addresses ha ON ha.addressid = up.homeaddressid
WHERE up.userid = s.userid AND s.homeaddress1 IS NULL AND s.isprotected = false;");

			Execute.Sql(@"
UPDATE departmentmembersensitivedata s
SET mailingaddress1 = ma.address1, mailingcity = ma.city, mailingstate = ma.state,
    mailingpostalcode = ma.postalcode, mailingcountry = ma.country
FROM userprofiles up
INNER JOIN addresses ma ON ma.addressid = up.mailingaddressid
WHERE up.userid = s.userid AND s.mailingaddress1 IS NULL AND s.isprotected = false;");
		}

		public override void Down()
		{
			// Only safe while every department is Disabled: for a protected department these columns
			// hold rgdp ciphertext that exists nowhere else. Refused rather than left to the comment,
			// because the loss is unrecoverable - the legacy profile copy is gone by then (M0141).
			if (Schema.Table("departmentmembersensitivedata").Column("homeaddress1").Exists())
				Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM departmentmembersensitivedata WHERE isprotected = true) THEN
		RAISE EXCEPTION 'M0133 rollback refused: protected member address data exists and these columns hold the only copy. Offboard the affected departments first.';
	END IF;
END $$;");

			if (Schema.Table("departmentmembersensitivedata").Column("homeaddress1").Exists())
				Delete.Column("homeaddress1").Column("homecity").Column("homestate")
					.Column("homepostalcode").Column("homecountry")
					.Column("mailingaddress1").Column("mailingcity").Column("mailingstate")
					.Column("mailingpostalcode").Column("mailingcountry")
					.FromTable("departmentmembersensitivedata");
		}
	}
}
