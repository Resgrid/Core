using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department-scoped member addresses (ADP plan section 5.1). Stored as columns on
	/// DepartmentMemberSensitiveData rather than as a foreign key to the shared Addresses table:
	/// an Addresses row has no owner, is reachable from profiles, contacts, departments and
	/// stations alike, and encrypting one with a single department's key would break every other
	/// reader. That shared-ownership problem is exactly what the plan defers — sidestepped here by
	/// giving the member's address its own department-scoped storage, which also lets a member hold
	/// a different address per department.
	///
	/// EXPAND phase: the legacy UserProfiles.HomeAddressId/MailingAddressId links are left intact
	/// and backfilled from; the contract migration clears them once this is deployed and verified.
	/// </summary>
	[Migration(133)]
	public class M0133_AddMemberDepartmentAddresses : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentMemberSensitiveData").Column("HomeAddress1").Exists())
				Alter.Table("DepartmentMemberSensitiveData")
					.AddColumn("HomeAddress1").AsString(int.MaxValue).Nullable()
					.AddColumn("HomeCity").AsString(int.MaxValue).Nullable()
					.AddColumn("HomeState").AsString(int.MaxValue).Nullable()
					.AddColumn("HomePostalCode").AsString(int.MaxValue).Nullable()
					.AddColumn("HomeCountry").AsString(int.MaxValue).Nullable()
					.AddColumn("MailingAddress1").AsString(int.MaxValue).Nullable()
					.AddColumn("MailingCity").AsString(int.MaxValue).Nullable()
					.AddColumn("MailingState").AsString(int.MaxValue).Nullable()
					.AddColumn("MailingPostalCode").AsString(int.MaxValue).Nullable()
					.AddColumn("MailingCountry").AsString(int.MaxValue).Nullable();

			// Backfill from the member's existing profile addresses, once per department they belong
			// to. Re-runnable: only fills rows whose home address is still empty, so a
			// department-specific address someone has already entered is never overwritten.
			Execute.Sql(@"
UPDATE s
SET s.[HomeAddress1] = ha.[Address1], s.[HomeCity] = ha.[City], s.[HomeState] = ha.[State],
    s.[HomePostalCode] = ha.[PostalCode], s.[HomeCountry] = ha.[Country]
FROM [DepartmentMemberSensitiveData] s
INNER JOIN [UserProfiles] up ON up.[UserId] = s.[UserId]
INNER JOIN [Addresses] ha ON ha.[AddressId] = up.[HomeAddressId]
WHERE s.[HomeAddress1] IS NULL AND s.[IsProtected] = 0;");

			Execute.Sql(@"
UPDATE s
SET s.[MailingAddress1] = ma.[Address1], s.[MailingCity] = ma.[City], s.[MailingState] = ma.[State],
    s.[MailingPostalCode] = ma.[PostalCode], s.[MailingCountry] = ma.[Country]
FROM [DepartmentMemberSensitiveData] s
INNER JOIN [UserProfiles] up ON up.[UserId] = s.[UserId]
INNER JOIN [Addresses] ma ON ma.[AddressId] = up.[MailingAddressId]
WHERE s.[MailingAddress1] IS NULL AND s.[IsProtected] = 0;");
		}

		/// <summary>
		/// Refuses the rollback when any row is already protected — these columns hold the only copy
		/// of an enrolled department's addresses.
		/// </summary>
		private const string RefuseIfProtected = @"
IF EXISTS (SELECT 1 FROM [DepartmentMemberSensitiveData] WHERE [IsProtected] = 1)
	THROW 51000, 'M0133 rollback refused: protected member address data exists and these columns hold the only copy. Offboard the affected departments first.', 1;";

		public override void Down()
		{
			// Refuses rather than relying on the operator having read the comment: for a protected
			// department these columns hold rgdp ciphertext that exists nowhere else.
			if (Schema.Table("DepartmentMemberSensitiveData").Column("HomeAddress1").Exists())
				Execute.Sql(RefuseIfProtected);

			if (Schema.Table("DepartmentMemberSensitiveData").Column("HomeAddress1").Exists())
				Delete.Column("HomeAddress1").Column("HomeCity").Column("HomeState")
					.Column("HomePostalCode").Column("HomeCountry")
					.Column("MailingAddress1").Column("MailingCity").Column("MailingState")
					.Column("MailingPostalCode").Column("MailingCountry")
					.FromTable("DepartmentMemberSensitiveData");
		}
	}
}
