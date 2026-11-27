using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0360_AddResponderExposures (member wellness WL0, §13b): rmsresponderexposures, rmscallexposureflags and
	/// rmsresponderscreeningflags. Member ids are citext so the case-insensitive lookups use their index; protected columns are text.
	/// No foreign keys; guarded for a safe retry; Down drops the three tables.
	/// </summary>
	[Migration(360)]
	public class M0360_AddResponderExposuresPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsresponderexposures").Exists())
			{
				Create.Table("rmsresponderexposures")
					.WithColumn("rmsresponderexposureid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("category").AsInt32().NotNullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("durationminutes").AsInt32().Nullable()
					.WithColumn("status").AsInt32().NotNullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("callexposureflagid").AsString(36).Nullable()
					.WithColumn("suggestionbasis").AsString(200).Nullable()
					.WithColumn("substance").AsCustom("text").Nullable()
					.WithColumn("narrative").AsCustom("text").Nullable()
					.WithColumn("ppeworn").AsCustom("text").Nullable()
					.WithColumn("decontamination").AsCustom("text").Nullable()
					.WithColumn("healthdetails").AsCustom("text").Nullable()
					.WithColumn("remindmember").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("remindpeerteam").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("nextanniversaryon").AsDateTime2().Nullable()
					.WithColumn("lastremindersenton").AsDateTime2().Nullable()
					.WithColumn("confirmedon").AsDateTime2().Nullable()
					.WithColumn("confirmedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("dismissedon").AsDateTime2().Nullable()
					.WithColumn("dismissedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsresponderexposures_user ON rmsresponderexposures (departmentid, userid, occurredon);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsresponderexposures_call ON rmsresponderexposures (departmentid, callid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsresponderexposures_anniversary ON rmsresponderexposures (nextanniversaryon, departmentid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsresponderexposures_occurred ON rmsresponderexposures (occurredon, departmentid);");

			if (!Schema.Table("rmscallexposureflags").Exists())
			{
				Create.Table("rmscallexposureflags")
					.WithColumn("rmscallexposureflagid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("category").AsInt32().NotNullable()
					.WithColumn("flaggedon").AsDateTime2().NotNullable()
					.WithColumn("flaggedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("suggestionsgeneratedon").AsDateTime2().Nullable()
					.WithColumn("suggestioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("clearedon").AsDateTime2().Nullable()
					.WithColumn("clearedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmscallexposureflags_callcategory ON rmscallexposureflags (departmentid, callid, category);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmscallexposureflags_pending ON rmscallexposureflags (suggestionsgeneratedon, flaggedon);");

			if (!Schema.Table("rmsresponderscreeningflags").Exists())
			{
				Create.Table("rmsresponderscreeningflags")
					.WithColumn("rmsresponderscreeningflagid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("userid").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().Nullable()
					.WithColumn("reviewon").AsDateTime2().Nullable()
					.WithColumn("plannotes").AsCustom("text").Nullable()
					.WithColumn("flaggedon").AsDateTime2().NotNullable()
					.WithColumn("flaggedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("clearedon").AsDateTime2().Nullable()
					.WithColumn("clearedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1);
			}

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsresponderscreeningflags_user ON rmsresponderscreeningflags (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS rmsresponderscreeningflags;");
			Execute.Sql("DROP TABLE IF EXISTS rmscallexposureflags;");
			Execute.Sql("DROP TABLE IF EXISTS rmsresponderexposures;");
		}
	}
}
