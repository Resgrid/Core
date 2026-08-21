using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	// PostgreSQL has no concurrent ADD COLUMN operation. TransactionBehavior.None keeps each
	// metadata-only ALTER self-contained so its brief ACCESS EXCLUSIVE lock is released before
	// the next statement. Every change is existence-guarded for retry after a partial apply.
	[Migration(120, TransactionBehavior.None)]
	public class M0120_AddUserAuthenticationStatePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("aspnetusers").Column("authenticationgeneration").Exists())
				Alter.Table("aspnetusers").AddColumn("authenticationgeneration").AsInt64().NotNullable().WithDefaultValue(0L);

			if (!Schema.Table("aspnetusers").Column("credentialsvalidafterutc").Exists())
				Alter.Table("aspnetusers").AddColumn("credentialsvalidafterutc").AsDateTime2().Nullable();

			if (!Schema.Table("aspnetusers").Column("authenticationstatechangedon").Exists())
				Alter.Table("aspnetusers").AddColumn("authenticationstatechangedon").AsDateTime2().Nullable();
		}

		public override void Down()
		{
			// DROP COLUMN also requires ACCESS EXCLUSIVE. PostgreSQL has no online alternative;
			// schedule rollback during a maintenance window when lock acquisition cannot be tolerated.
			if (Schema.Table("aspnetusers").Column("authenticationstatechangedon").Exists())
				Delete.Column("authenticationstatechangedon").FromTable("aspnetusers");
			if (Schema.Table("aspnetusers").Column("credentialsvalidafterutc").Exists())
				Delete.Column("credentialsvalidafterutc").FromTable("aspnetusers");
			if (Schema.Table("aspnetusers").Column("authenticationgeneration").Exists())
				Delete.Column("authenticationgeneration").FromTable("aspnetusers");
		}
	}
}
