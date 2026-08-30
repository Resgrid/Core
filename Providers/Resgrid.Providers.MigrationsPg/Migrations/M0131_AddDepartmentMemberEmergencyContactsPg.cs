using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department-scoped emergency contacts for a member (ADP plan section 5.1). A member can have
	/// MORE THAN ONE, and the set is per department: userprofiles is global to the user and shared
	/// across every department they belong to, so it can neither be encrypted with one department's
	/// key nor hold values that legitimately differ between departments.
	///
	/// Also drops the single-value emergencycontactname/phone columns M0124 speculatively added to
	/// departmentmembersensitivedata — never populated, never surfaced, superseded by this table.
	/// </summary>
	[Migration(131)]
	public class M0131_AddDepartmentMemberEmergencyContactsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentmemberemergencycontacts").Exists())
			{
				Create.Table("departmentmemberemergencycontacts")
					.WithColumn("departmentmemberemergencycontactid").AsInt32().NotNullable().PrimaryKey().Identity()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("name").AsCustom("citext").Nullable()
					.WithColumn("relationship").AsCustom("citext").Nullable()
					.WithColumn("phonenumber").AsCustom("citext").Nullable()
					.WithColumn("alternatephonenumber").AsCustom("citext").Nullable()
					.WithColumn("email").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isprimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					// ADP row marker: set once the row's cataloged columns carry rgdp envelopes.
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("updatedon").AsDateTime().Nullable()
					.WithColumn("updatedbyuserid").AsString(128).Nullable();

				Create.Index("ix_departmentmemberemergencycontacts_department_user")
					.OnTable("departmentmemberemergencycontacts")
					.OnColumn("departmentid").Ascending()
					.OnColumn("userid").Ascending();
			}

			if (Schema.Table("departmentmembersensitivedata").Column("emergencycontactname").Exists())
				Delete.Column("emergencycontactname").FromTable("departmentmembersensitivedata");

			if (Schema.Table("departmentmembersensitivedata").Column("emergencycontactphone").Exists())
				Delete.Column("emergencycontactphone").FromTable("departmentmembersensitivedata");
		}

		public override void Down()
		{
			// Only safe while every department is Disabled: dropping this table on a protected
			// department destroys emergency-contact ciphertext that cannot be recovered.
			if (Schema.Table("departmentmemberemergencycontacts").Exists())
				Delete.Table("departmentmemberemergencycontacts");

			if (!Schema.Table("departmentmembersensitivedata").Column("emergencycontactname").Exists())
				Alter.Table("departmentmembersensitivedata")
					.AddColumn("emergencycontactname").AsCustom("citext").Nullable()
					.AddColumn("emergencycontactphone").AsCustom("citext").Nullable();
		}
	}
}
