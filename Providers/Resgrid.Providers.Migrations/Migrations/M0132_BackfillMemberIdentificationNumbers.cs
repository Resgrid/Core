using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Moves member identification numbers from the GLOBAL UserProfiles row to the department-scoped
	/// DepartmentMemberSensitiveData row (ADP plan section 5.1). A profile is shared across every
	/// department a user belongs to, so it can neither be encrypted with one department's key nor
	/// hold the different badge/ID numbers different departments issue the same person.
	///
	/// EXPAND phase of expand/contract: this backfills a row per (department, member) that actually
	/// has a number and leaves UserProfiles.IdentificationNumber in place. The application reads and
	/// writes the department-scoped value from here on; the column is dropped in a later migration
	/// once this is deployed and verified, so a rollback does not lose data.
	/// </summary>
	[Migration(132)]
	public class M0132_BackfillMemberIdentificationNumbers : Migration
	{
		public override void Up()
		{
			// Only members with a number, and only where no row exists yet — the migration is
			// re-runnable and never overwrites a department-specific value someone already set.
			Execute.Sql(@"
INSERT INTO [DepartmentMemberSensitiveData] ([DepartmentId], [UserId], [ProtectionId], [IdentificationNumber], [IsProtected], [CreatedOn])
SELECT dm.[DepartmentId], dm.[UserId], LOWER(REPLACE(CONVERT(NVARCHAR(64), NEWID()), '-', '')), up.[IdentificationNumber], 0, GETUTCDATE()
FROM [DepartmentMembers] dm
INNER JOIN [UserProfiles] up ON up.[UserId] = dm.[UserId]
WHERE dm.[IsDeleted] = 0
  AND up.[IdentificationNumber] IS NOT NULL
  AND LTRIM(RTRIM(up.[IdentificationNumber])) <> ''
  AND NOT EXISTS (
      SELECT 1 FROM [DepartmentMemberSensitiveData] s
      WHERE s.[DepartmentId] = dm.[DepartmentId] AND s.[UserId] = dm.[UserId]);");
		}

		public override void Down()
		{
			// The rows may since have been edited per department, or encrypted for a protected
			// department — copying them back into the shared profile would be wrong in both cases.
			// The source column was never cleared, so there is nothing to restore.
		}
	}
}
