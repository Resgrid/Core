using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Moves member identification numbers from the GLOBAL userprofiles row to the department-scoped
	/// departmentmembersensitivedata row (ADP plan section 5.1). See the SQL Server counterpart for
	/// the reasoning; this is the EXPAND phase and leaves userprofiles.identificationnumber in place.
	/// </summary>
	[Migration(132)]
	public class M0132_BackfillMemberIdentificationNumbersPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
INSERT INTO departmentmembersensitivedata (departmentid, userid, protectionid, identificationnumber, isprotected, createdon)
SELECT dm.departmentid, dm.userid, md5(random()::text || clock_timestamp()::text), up.identificationnumber, false, (NOW() AT TIME ZONE 'utc')
FROM departmentmembers dm
INNER JOIN userprofiles up ON up.userid = dm.userid
WHERE dm.isdeleted = false
  AND up.identificationnumber IS NOT NULL
  AND btrim(up.identificationnumber) <> ''
  AND NOT EXISTS (
      SELECT 1 FROM departmentmembersensitivedata s
      WHERE s.departmentid = dm.departmentid AND s.userid = dm.userid);");
		}

		public override void Down()
		{
			// Nothing to restore: the source column was never cleared, and the copies may since have
			// diverged per department or been encrypted.
		}
	}
}
