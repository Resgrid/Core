using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department-scoped emergency contacts for a member (ADP plan section 5.1). A member can have
	/// MORE THAN ONE, and the set is per department: UserProfile is global to the user and shared
	/// across every department they belong to, so it can neither be encrypted with one department's
	/// key nor hold values that legitimately differ between departments.
	///
	/// Also drops the single-value EmergencyContactName/Phone columns M0124 speculatively added to
	/// DepartmentMemberSensitiveData. Those were never populated, never surfaced in any UI and never
	/// read by any service — this table supersedes them.
	/// </summary>
	[Migration(131)]
	public class M0131_AddDepartmentMemberEmergencyContacts : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("DepartmentMemberEmergencyContacts").Exists())
			{
				Create.Table("DepartmentMemberEmergencyContacts")
					.WithColumn("DepartmentMemberEmergencyContactId").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("UserId").AsString(128).NotNullable()
					.WithColumn("Name").AsString(int.MaxValue).Nullable()
					.WithColumn("Relationship").AsString(int.MaxValue).Nullable()
					.WithColumn("PhoneNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("AlternatePhoneNumber").AsString(int.MaxValue).Nullable()
					.WithColumn("Email").AsString(int.MaxValue).Nullable()
					.WithColumn("Notes").AsString(int.MaxValue).Nullable()
					.WithColumn("IsPrimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					// ADP row marker: set once the row's cataloged columns carry rgdp envelopes.
					.WithColumn("IsProtected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("CreatedOn").AsDateTime().NotNullable()
					.WithColumn("CreatedByUserId").AsString(128).Nullable()
					.WithColumn("UpdatedOn").AsDateTime().Nullable()
					.WithColumn("UpdatedByUserId").AsString(128).Nullable();

				Create.Index("IX_DepartmentMemberEmergencyContacts_Department_User")
					.OnTable("DepartmentMemberEmergencyContacts")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("UserId").Ascending();
			}

			// Superseded by the table above; never populated or read.
			if (Schema.Table("DepartmentMemberSensitiveData").Column("EmergencyContactName").Exists())
				Delete.Column("EmergencyContactName").FromTable("DepartmentMemberSensitiveData");

			if (Schema.Table("DepartmentMemberSensitiveData").Column("EmergencyContactPhone").Exists())
				Delete.Column("EmergencyContactPhone").FromTable("DepartmentMemberSensitiveData");
		}

		/// <summary>
		/// Refuses the rollback when any row is already protected. These columns hold the ONLY copy
		/// of an enrolled department's emergency contacts — the plaintext was never kept elsewhere —
		/// so dropping the table would destroy them with no way back. A comment saying "only safe
		/// while every department is Disabled" is not a control; this is.
		/// </summary>
		private const string RefuseIfProtected = @"
IF EXISTS (SELECT 1 FROM [DepartmentMemberEmergencyContacts] WHERE [IsProtected] = 1)
	THROW 51000, 'M0131 rollback refused: protected emergency-contact data exists and this table holds the only copy. Offboard the affected departments first.', 1;";

		public override void Down()
		{
			// Refuses rather than relying on the operator having read the comment: dropping this
			// table on a protected department destroys emergency-contact ciphertext for good.
			if (Schema.Table("DepartmentMemberEmergencyContacts").Exists())
				Execute.Sql(RefuseIfProtected);

			if (Schema.Table("DepartmentMemberEmergencyContacts").Exists())
				Delete.Table("DepartmentMemberEmergencyContacts");

			if (!Schema.Table("DepartmentMemberSensitiveData").Column("EmergencyContactName").Exists())
				Alter.Table("DepartmentMemberSensitiveData")
					.AddColumn("EmergencyContactName").AsString(int.MaxValue).Nullable()
					.AddColumn("EmergencyContactPhone").AsString(int.MaxValue).Nullable();
		}
	}
}
