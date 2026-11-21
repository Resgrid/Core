using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0326_AddIntakeSpamScreening (blocked intake senders + intake call screening).</summary>
	[Migration(326)]
	public class M0326_AddIntakeSpamScreeningPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("intakeblockedsenders").Exists())
			{
				Create.Table("intakeblockedsenders")
					.WithColumn("intakeblockedsenderid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("sender").AsCustom("citext").NotNullable()
					.WithColumn("channeltype").AsInt32().NotNullable()
					.WithColumn("reason").AsCustom("citext").Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("blockedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("blockedon").AsDateTime2().NotNullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("removedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("removedon").AsDateTime2().Nullable();
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_intakeblockedsenders_standing ON intakeblockedsenders (departmentid, sender) WHERE removedon IS NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakeblockedsenders_ended ON intakeblockedsenders (removedon, expireson);");

			Execute.Sql("ALTER TABLE intakemessages ADD COLUMN IF NOT EXISTS screening integer NULL;");
			Execute.Sql("ALTER TABLE intakemessages ADD COLUMN IF NOT EXISTS screenedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE intakemessages ADD COLUMN IF NOT EXISTS screenedbyuserid citext NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_intakemessages_sender ON intakemessages (departmentid, sender, receivedon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS intakeblockedsenders;");
			Execute.Sql("DROP INDEX IF EXISTS ix_intakemessages_sender;");
			Execute.Sql("ALTER TABLE intakemessages DROP COLUMN IF EXISTS screenedbyuserid;");
			Execute.Sql("ALTER TABLE intakemessages DROP COLUMN IF EXISTS screenedon;");
			Execute.Sql("ALTER TABLE intakemessages DROP COLUMN IF EXISTS screening;");
		}
	}
}
