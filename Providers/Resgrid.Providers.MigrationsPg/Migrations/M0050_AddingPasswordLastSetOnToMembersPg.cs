using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(50)]
	public class M0050_AddingPasswordLastSetOnToMembersPg : Migration
	{
		public override void Up()
		{
			// Track when a department member last changed their password.
			// Used by the password-expiration policy (DepartmentSecurityPolicy.PasswordExpirationDays).
			// Nullable: NULL means the password was set before this column existed and is treated
			// as never-expired until the user next changes their password.
			Alter.Table("DepartmentMembers".ToLower())
				.AddColumn("PasswordLastSetOn".ToLower()).AsDateTime().Nullable();
		}

		public override void Down()
		{
			Delete.Column("PasswordLastSetOn".ToLower()).FromTable("DepartmentMembers".ToLower());
		}
	}
}

