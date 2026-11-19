using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>Pages owed to resources added to live calls; see the SQL Server M0321_AddCallBroadcastOutbox.</summary>
	[Migration(321)]
	public class M0321_AddCallBroadcastOutboxPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("callbroadcastoutbox").Exists())
			{
				Create.Table("callbroadcastoutbox")
					.WithColumn("callbroadcastoutboxid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("userids").AsCustom("citext").Nullable()
					.WithColumn("groupids").AsCustom("citext").Nullable()
					.WithColumn("unitids").AsCustom("citext").Nullable()
					.WithColumn("roleids").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("attempts").AsInt32().NotNullable()
					.WithColumn("nextattempton").AsDateTime2().NotNullable()
					.WithColumn("claimtoken").AsCustom("citext").Nullable()
					.WithColumn("leaseexpireson").AsDateTime2().Nullable()
					.WithColumn("lasterror").AsCustom("citext").Nullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("completedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callbroadcastoutbox_due ON callbroadcastoutbox (state, nextattempton) WHERE state < 2;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callbroadcastoutbox_call ON callbroadcastoutbox (callid, state);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callbroadcastoutbox_department ON callbroadcastoutbox (departmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_callbroadcastoutbox_completed ON callbroadcastoutbox (completedon) WHERE completedon IS NOT NULL;");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS callbroadcastoutbox;");
		}
	}
}
