using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-D Coordination (plan §9.25, §9.25.3a): boards, lanes and interagency share grants; lifeline
	/// status history, open actions, decisions in flight and situational features. No protected column: boards are shared with
	/// partner agencies. PostgreSQL twin: lower-case identifiers and every string column citext (plan §11.2),
	/// existence-guarded.
	/// </summary>
	[Migration(297)]
	public class M0297_AddIncidentCoordinationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("coordinationboards").Exists())
			{
				Create.Table("coordinationboards")
					.WithColumn("coordinationboardid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("scope").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("defaultstalenessminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isarchived").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isfrozen").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("frozenon").AsDateTime2().Nullable()
					.WithColumn("snapshotartifactid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationboards_department ON coordinationboards (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationboards_activation ON coordinationboards (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("coordinationboardlanes").Exists())
			{
				Create.Table("coordinationboardlanes")
					.WithColumn("coordinationboardlaneid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("coordinationboardid").AsCustom("citext").NotNullable()
					.WithColumn("lanetype").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("filterjson").AsCustom("citext").Nullable()
					.WithColumn("displayrule").AsCustom("citext").Nullable()
					.WithColumn("stalenessminutes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("alarmonstale").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("lastsourceon").AsDateTime2().Nullable()
					.WithColumn("stalesince").AsDateTime2().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationboardlanes_department ON coordinationboardlanes (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationboardlanes_board ON coordinationboardlanes (departmentid, coordinationboardid, sortorder);");
			}

			if (!Schema.Table("coordinationsharegrants").Exists())
			{
				Create.Table("coordinationsharegrants")
					.WithColumn("coordinationsharegrantid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("coordinationboardid").AsCustom("citext").NotNullable()
					.WithColumn("coordinationboardlaneid").AsCustom("citext").Nullable()
					.WithColumn("partnercontactid").AsCustom("citext").Nullable()
					.WithColumn("partnername").AsCustom("citext").NotNullable()
					.WithColumn("scope").AsCustom("citext").Nullable()
					.WithColumn("tokenhash").AsCustom("citext").NotNullable()
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("lastaccessedon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationsharegrants_department ON coordinationsharegrants (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_coordinationsharegrants_board ON coordinationsharegrants (departmentid, coordinationboardid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_coordinationsharegrants_token ON coordinationsharegrants (tokenhash);");
			}

			if (!Schema.Table("incidentlifelinestatuses").Exists())
			{
				Create.Table("incidentlifelinestatuses")
					.WithColumn("incidentlifelinestatusid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("lifeline").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("component").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("asof").AsDateTime2().NotNullable()
					.WithColumn("source").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlifelinestatuses_activation ON incidentlifelinestatuses (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlifelinestatuses_lifeline ON incidentlifelinestatuses (departmentid, incidentactivationid, lifeline, asof);");
			}

			if (!Schema.Table("incidentopenactions").Exists())
			{
				Create.Table("incidentopenactions")
					.WithColumn("incidentopenactionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("title").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("ownername").AsCustom("citext").Nullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notifiedon").AsDateTime2().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentopenactions_activation ON incidentopenactions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentopenactions_due ON incidentopenactions (departmentid, incidentactivationid, dueon);");
			}

			if (!Schema.Table("incidentdecisions").Exists())
			{
				Create.Table("incidentdecisions")
					.WithColumn("incidentdecisionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("question").AsCustom("citext").NotNullable()
					.WithColumn("optionsjson").AsCustom("citext").Nullable()
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("ownername").AsCustom("citext").Nullable()
					.WithColumn("deadlineon").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("decision").AsCustom("citext").Nullable()
					.WithColumn("decidedon").AsDateTime2().Nullable()
					.WithColumn("closedtorecordid").AsCustom("citext").Nullable()
					.WithColumn("notifiedon").AsDateTime2().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdecisions_activation ON incidentdecisions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdecisions_deadline ON incidentdecisions (departmentid, incidentactivationid, deadlineon);");
			}

			if (!Schema.Table("incidentsituationalfeatures").Exists())
			{
				Create.Table("incidentsituationalfeatures")
					.WithColumn("incidentsituationalfeatureid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("featuretype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("geometryjson").AsCustom("citext").Nullable()
					.WithColumn("capacitytotal").AsInt32().Nullable()
					.WithColumn("capacityavailable").AsInt32().Nullable()
					.WithColumn("thresholdpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("asof").AsDateTime2().NotNullable()
					.WithColumn("source").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentsituationalfeatures_activation ON incidentsituationalfeatures (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentsituationalfeatures_type ON incidentsituationalfeatures (departmentid, incidentactivationid, featuretype);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentsituationalfeatures").Exists())
				Delete.Table("incidentsituationalfeatures");
			if (Schema.Table("incidentdecisions").Exists())
				Delete.Table("incidentdecisions");
			if (Schema.Table("incidentopenactions").Exists())
				Delete.Table("incidentopenactions");
			if (Schema.Table("incidentlifelinestatuses").Exists())
				Delete.Table("incidentlifelinestatuses");
			if (Schema.Table("coordinationsharegrants").Exists())
				Delete.Table("coordinationsharegrants");
			if (Schema.Table("coordinationboardlanes").Exists())
				Delete.Table("coordinationboardlanes");
			if (Schema.Table("coordinationboards").Exists())
				Delete.Table("coordinationboards");
		}
	}
}
