using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-A Foundation (large-incident-logistics-finance-plan.md rev 4.1, §9.0–§9.2, §9.5 enrollment,
	/// §9.17 intelligence, §11; registry §4C): activations with complexes and exercise mode, authority profiles and snapshots,
	/// standing team rosters, operational periods, plan assembly manifests and weather blocks, objectives, the position
	/// catalog, organization nodes and position assignments, participants with protected detail (ADP catalog 34) and
	/// qualification snapshots, the snapshot / metric / freshness / projection-cursor read models and the position-routing log.
	/// No column on any IC table. PostgreSQL twin: lower-case identifiers and every string column citext (plan §11.2),
	/// existence-guarded.
	/// </summary>
	[Migration(290)]
	public class M0290_AddIncidentBackOfficeFoundationPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentactivations").Exists())
			{
				Create.Table("incidentactivations")
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("parentactivationid").AsCustom("citext").Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("incidentcommandid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("shortcode").AsCustom("citext").NotNullable()
					.WithColumn("complexitytype").AsInt32().Nullable()
					.WithColumn("timezoneid").AsCustom("citext").NotNullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("measurementsystem").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isexercise").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("authorityprofilesnapshotid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("openedon").AsDateTime2().Nullable()
					.WithColumn("openedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("sealedon").AsDateTime2().Nullable()
					.WithColumn("sealedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reopenedon").AsDateTime2().Nullable()
					.WithColumn("reopenedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("reopenreason").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentactivations_department ON incidentactivations (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentactivations_status ON incidentactivations (departmentid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentactivations_call ON incidentactivations (departmentid, callid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentactivations_parent ON incidentactivations (departmentid, parentactivationid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentactivations_shortcode ON incidentactivations (departmentid, shortcode) WHERE isdeleted = FALSE;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentactivations_board ON incidentactivations (departmentid, incidentcommandid) WHERE isdeleted = FALSE AND incidentcommandid IS NOT NULL;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentactivations_shortcode') THEN ALTER TABLE incidentactivations ADD CONSTRAINT ck_incidentactivations_shortcode CHECK (char_length(shortcode) <= 32); END IF; END $$;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentactivations_currency') THEN ALTER TABLE incidentactivations ADD CONSTRAINT ck_incidentactivations_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("departmentincidentauthorityprofiles").Exists())
			{
				Create.Table("departmentincidentauthorityprofiles")
					.WithColumn("departmentincidentauthorityprofileid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("isdefault").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentauthorityprofiles_department ON departmentincidentauthorityprofiles (departmentid);");
			}

			if (!Schema.Table("incidentdomainauthorities").Exists())
			{
				Create.Table("incidentdomainauthorities")
					.WithColumn("incidentdomainauthorityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("ownerkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ownerid").AsCustom("citext").NotNullable()
					.WithColumn("domain").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("mode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcesystem").AsCustom("citext").Nullable()
					.WithColumn("connectorstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("precedence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdomainauthorities_department ON incidentdomainauthorities (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdomainauthorities_owner ON incidentdomainauthorities (departmentid, ownerkind, ownerid);");
			}

			if (!Schema.Table("incidentauthorityprofilesnapshots").Exists())
			{
				Create.Table("incidentauthorityprofilesnapshots")
					.WithColumn("incidentauthorityprofilesnapshotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("sourceprofileid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("takenon").AsDateTime2().NotNullable()
					.WithColumn("takenbyuserid").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentauthorityprofilesnapshots_activation ON incidentauthorityprofilesnapshots (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentreconciliationitems").Exists())
			{
				Create.Table("incidentreconciliationitems")
					.WithColumn("incidentreconciliationitemid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("domain").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjecttype").AsCustom("citext").NotNullable()
					.WithColumn("subjectid").AsCustom("citext").Nullable()
					.WithColumn("sourcesystem").AsCustom("citext").Nullable()
					.WithColumn("externalidentifier").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable()
					.WithColumn("resolvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("resolution").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreconciliationitems_activation ON incidentreconciliationitems (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentreconciliationitems_status ON incidentreconciliationitems (departmentid, incidentactivationid, status);");
			}

			if (!Schema.Table("departmentincidentteams").Exists())
			{
				Create.Table("departmentincidentteams")
					.WithColumn("departmentincidentteamid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("teamtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("owninggroupid").AsInt32().Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteams_department ON departmentincidentteams (departmentid);");
			}

			if (!Schema.Table("departmentincidentteamrotations").Exists())
			{
				Create.Table("departmentincidentteamrotations")
					.WithColumn("departmentincidentteamrotationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentincidentteamid").AsCustom("citext").NotNullable()
					.WithColumn("windowstart").AsDateTime2().NotNullable()
					.WithColumn("windowend").AsDateTime2().NotNullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamrotations_department ON departmentincidentteamrotations (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamrotations_team ON departmentincidentteamrotations (departmentid, departmentincidentteamid, windowstart);");
			}

			if (!Schema.Table("departmentincidentteamslots").Exists())
			{
				Create.Table("departmentincidentteamslots")
					.WithColumn("departmentincidentteamslotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentincidentteamid").AsCustom("citext").NotNullable()
					.WithColumn("incidentpositiondefinitionid").AsCustom("citext").NotNullable()
					.WithColumn("slotorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("label").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamslots_department ON departmentincidentteamslots (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamslots_team ON departmentincidentteamslots (departmentid, departmentincidentteamid, slotorder);");
			}

			if (!Schema.Table("departmentincidentteamcandidates").Exists())
			{
				Create.Table("departmentincidentteamcandidates")
					.WithColumn("departmentincidentteamcandidateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentincidentteamid").AsCustom("citext").NotNullable()
					.WithColumn("departmentincidentteamslotid").AsCustom("citext").NotNullable()
					.WithColumn("candidatekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("contactid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamcandidates_department ON departmentincidentteamcandidates (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentincidentteamcandidates_slot ON departmentincidentteamcandidates (departmentid, departmentincidentteamslotid, priority);");
			}

			if (!Schema.Table("incidentoperationalperiods").Exists())
			{
				Create.Table("incidentoperationalperiods")
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("number").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().NotNullable()
					.WithColumn("endon").AsDateTime2().NotNullable()
					.WithColumn("timezoneid").AsCustom("citext").NotNullable()
					.WithColumn("briefingon").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("planneruserid").AsCustom("citext").Nullable()
					.WithColumn("isfrozen").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("frozenon").AsDateTime2().Nullable()
					.WithColumn("frozenbyuserid").AsCustom("citext").Nullable()
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("copiedfromperiodid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentoperationalperiods_activation ON incidentoperationalperiods (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentoperationalperiods_window ON incidentoperationalperiods (departmentid, incidentactivationid, starton);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentoperationalperiods_number ON incidentoperationalperiods (departmentid, incidentactivationid, number) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("incidentplanversions").Exists())
			{
				Create.Table("incidentplanversions")
					.WithColumn("incidentplanversionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").NotNullable()
					.WithColumn("versionnumber").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("manifestjson").AsCustom("citext").Nullable()
					.WithColumn("distributionjson").AsCustom("citext").Nullable()
					.WithColumn("printcopies").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("distributedon").AsDateTime2().Nullable()
					.WithColumn("supersededbyplanversionid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentplanversions_activation ON incidentplanversions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentplanversions_period ON incidentplanversions (departmentid, incidentactivationid, incidentoperationalperiodid);");
			}

			if (!Schema.Table("incidentplanweatherblocks").Exists())
			{
				Create.Table("incidentplanweatherblocks")
					.WithColumn("incidentplanweatherblockid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentplanversionid").AsCustom("citext").Nullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("forecastsource").AsCustom("citext").Nullable()
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("forecastperiod").AsCustom("citext").Nullable()
					.WithColumn("temperaturesummary").AsCustom("citext").Nullable()
					.WithColumn("humiditysummary").AsCustom("citext").Nullable()
					.WithColumn("windsummary").AsCustom("citext").Nullable()
					.WithColumn("precipitationsummary").AsCustom("citext").Nullable()
					.WithColumn("spotforecastreference").AsCustom("citext").Nullable()
					.WithColumn("activealertidsjson").AsCustom("citext").Nullable()
					.WithColumn("narrative").AsCustom("citext").Nullable()
					.WithColumn("snapshottedon").AsDateTime2().Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentplanweatherblocks_activation ON incidentplanweatherblocks (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentplanweatherblocks_plan ON incidentplanweatherblocks (departmentid, incidentplanversionid);");
			}

			if (!Schema.Table("incidentobjectives").Exists())
			{
				Create.Table("incidentobjectives")
					.WithColumn("incidentobjectiveid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("number").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("text").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("carriedfromobjectiveid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentobjectives_activation ON incidentobjectives (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentobjectives_period ON incidentobjectives (departmentid, incidentactivationid, incidentoperationalperiodid);");
			}

			if (!Schema.Table("incidentpositiondefinitions").Exists())
			{
				Create.Table("incidentpositiondefinitions")
					.WithColumn("incidentpositiondefinitionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").Nullable()
					.WithColumn("code").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("branch").AsCustom("citext").Nullable()
					.WithColumn("unitname").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("sourcecatalog").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceversion").AsCustom("citext").Nullable()
					.WithColumn("requirementsjson").AsCustom("citext").Nullable()
					.WithColumn("defaultworkspace").AsCustom("citext").Nullable()
					.WithColumn("defaultincidentroletype").AsInt32().Nullable()
					.WithColumn("supervisorcode").AsCustom("citext").Nullable()
					.WithColumn("isstandard").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositiondefinitions_department ON incidentpositiondefinitions (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositiondefinitions_section ON incidentpositiondefinitions (departmentid, section, sortorder);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentpositiondefinitions_code ON incidentpositiondefinitions (departmentid, code) WHERE isdeleted = FALSE AND incidentactivationid IS NULL;");
			}

			if (!Schema.Table("incidentpositioncapabilities").Exists())
			{
				Create.Table("incidentpositioncapabilities")
					.WithColumn("incidentpositioncapabilityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentpositiondefinitionid").AsCustom("citext").NotNullable()
					.WithColumn("capabilitykey").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositioncapabilities_department ON incidentpositioncapabilities (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositioncapabilities_position ON incidentpositioncapabilities (departmentid, incidentpositiondefinitionid);");
			}

			if (!Schema.Table("incidentorganizationnodes").Exists())
			{
				Create.Table("incidentorganizationnodes")
					.WithColumn("incidentorganizationnodeid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("parentnodeid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("section").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentorganizationnodes_activation ON incidentorganizationnodes (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentorganizationnodes_period ON incidentorganizationnodes (departmentid, incidentactivationid, incidentoperationalperiodid);");
			}

			if (!Schema.Table("incidentpositionassignments").Exists())
			{
				Create.Table("incidentpositionassignments")
					.WithColumn("incidentpositionassignmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("incidentpositiondefinitionid").AsCustom("citext").NotNullable()
					.WithColumn("incidentorganizationnodeid").AsCustom("citext").Nullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("placeholderlabel").AsCustom("citext").Nullable()
					.WithColumn("assignmentkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("shift").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("starton").AsDateTime2().NotNullable()
					.WithColumn("endon").AsDateTime2().Nullable()
					.WithColumn("supervisorassignmentid").AsCustom("citext").Nullable()
					.WithColumn("incidentroleassignmentid").AsCustom("citext").Nullable()
					.WithColumn("istrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("taskbookreference").AsCustom("citext").Nullable()
					.WithColumn("taskbookexpireson").AsDate().Nullable()
					.WithColumn("trainingspecialistassignmentid").AsCustom("citext").Nullable()
					.WithColumn("traineepriority").AsInt32().Nullable()
					.WithColumn("qualificationstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("qualificationnote").AsCustom("citext").Nullable()
					.WithColumn("teamactivationbatchid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositionassignments_activation ON incidentpositionassignments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositionassignments_position ON incidentpositionassignments (departmentid, incidentactivationid, incidentpositiondefinitionid, starton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositionassignments_user ON incidentpositionassignments (departmentid, userid, starton);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentpositionassignments_period ON incidentpositionassignments (departmentid, incidentactivationid, incidentoperationalperiodid);");
			}

			if (!Schema.Table("incidentparticipants").Exists())
			{
				Create.Table("incidentparticipants")
					.WithColumn("incidentparticipantid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("contactid").AsCustom("citext").Nullable()
					.WithColumn("incidentadhocpersonnelid").AsCustom("citext").Nullable()
					.WithColumn("displayname").AsCustom("citext").NotNullable()
					.WithColumn("agency").AsCustom("citext").Nullable()
					.WithColumn("homeunit").AsCustom("citext").Nullable()
					.WithColumn("positiontitle").AsCustom("citext").Nullable()
					.WithColumn("ordernumber").AsCustom("citext").Nullable()
					.WithColumn("requestnumber").AsCustom("citext").Nullable()
					.WithColumn("paymentclass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("supportstatus").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("enrollmentsource").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdraft").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("expectedarrivalon").AsDateTime2().Nullable()
					.WithColumn("arrivedon").AsDateTime2().Nullable()
					.WithColumn("checkedinon").AsDateTime2().Nullable()
					.WithColumn("assignmentstarton").AsDate().Nullable()
					.WithColumn("maxassignmentdays").AsInt32().Nullable()
					.WithColumn("timeouton").AsDate().Nullable()
					.WithColumn("tentativereleaseon").AsDateTime2().Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("departedon").AsDateTime2().Nullable()
					.WithColumn("travelmode").AsCustom("citext").Nullable()
					.WithColumn("isselfsufficient").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("needslodging").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("needsmeals").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("checkincode").AsCustom("citext").Nullable()
					.WithColumn("personalphone").AsCustom("citext").Nullable()
					.WithColumn("personalemail").AsCustom("citext").Nullable()
					.WithColumn("homeaddress").AsCustom("citext").Nullable()
					.WithColumn("dateofbirth").AsCustom("citext").Nullable()
					.WithColumn("externalidentifier").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipants_activation ON incidentparticipants (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipants_status ON incidentparticipants (departmentid, incidentactivationid, supportstatus);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipants_user ON incidentparticipants (departmentid, userid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentparticipants_adhoc ON incidentparticipants (departmentid, incidentactivationid, incidentadhocpersonnelid) WHERE isdeleted = FALSE AND incidentadhocpersonnelid IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentparticipants_member ON incidentparticipants (departmentid, incidentactivationid, userid) WHERE isdeleted = FALSE AND userid IS NOT NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentparticipants_checkincode ON incidentparticipants (departmentid, checkincode) WHERE isdeleted = FALSE AND checkincode IS NOT NULL;");
			}

			if (!Schema.Table("incidentparticipantsensitive").Exists())
			{
				Create.Table("incidentparticipantsensitive")
					.WithColumn("incidentparticipantsensitiveid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").NotNullable()
					.WithColumn("emergencycontactname").AsCustom("citext").Nullable()
					.WithColumn("emergencycontactphone").AsCustom("citext").Nullable()
					.WithColumn("emergencycontactrelationship").AsCustom("citext").Nullable()
					.WithColumn("medicalaccommodation").AsCustom("citext").Nullable()
					.WithColumn("accessibilityneed").AsCustom("citext").Nullable()
					.WithColumn("allergendetail").AsCustom("citext").Nullable()
					.WithColumn("dietaryrestriction").AsCustom("citext").Nullable()
					.WithColumn("dietarycategorycode").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipantsensitive_activation ON incidentparticipantsensitive (departmentid, incidentactivationid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentparticipantsensitive_participant ON incidentparticipantsensitive (departmentid, incidentparticipantid);");
			}

			if (!Schema.Table("incidentparticipantqualifications").Exists())
			{
				Create.Table("incidentparticipantqualifications")
					.WithColumn("incidentparticipantqualificationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentparticipantid").AsCustom("citext").NotNullable()
					.WithColumn("code").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").Nullable()
					.WithColumn("source").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourcename").AsCustom("citext").Nullable()
					.WithColumn("verificationstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("effectiveon").AsDate().Nullable()
					.WithColumn("expireson").AsDate().Nullable()
					.WithColumn("snapshoton").AsDateTime2().NotNullable()
					.WithColumn("credentialnumber").AsCustom("citext").Nullable()
					.WithColumn("verificationnotes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipantqualifications_activation ON incidentparticipantqualifications (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentparticipantqualifications_participant ON incidentparticipantqualifications (departmentid, incidentparticipantid);");
			}

			if (!Schema.Table("incidentexternalorganizationrefs").Exists())
			{
				Create.Table("incidentexternalorganizationrefs")
					.WithColumn("incidentexternalorganizationrefid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("contactid").AsCustom("citext").NotNullable()
					.WithColumn("role").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentexternalorganizationrefs_activation ON incidentexternalorganizationrefs (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentoperationalsnapshots").Exists())
			{
				Create.Table("incidentoperationalsnapshots")
					.WithColumn("incidentoperationalsnapshotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("cursor").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("takenon").AsDateTime2().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("oldestsourceon").AsDateTime2().Nullable()
					.WithColumn("lagseconds").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("driftdetected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("summaryjson").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentoperationalsnapshots_activation ON incidentoperationalsnapshots (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentoperationalsnapshots_cursor ON incidentoperationalsnapshots (departmentid, incidentactivationid, cursor);");
			}

			if (!Schema.Table("incidentmetricvalues").Exists())
			{
				Create.Table("incidentmetricvalues")
					.WithColumn("incidentmetricvalueid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalsnapshotid").AsCustom("citext").NotNullable()
					.WithColumn("metrickey").AsCustom("citext").NotNullable()
					.WithColumn("metricversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bucket").AsCustom("citext").Nullable()
					.WithColumn("value").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("sourcecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("asof").AsDateTime2().NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmetricvalues_activation ON incidentmetricvalues (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmetricvalues_snapshot ON incidentmetricvalues (departmentid, incidentoperationalsnapshotid, metrickey);");
			}

			if (!Schema.Table("incidentdatafreshness").Exists())
			{
				Create.Table("incidentdatafreshness")
					.WithColumn("incidentdatafreshnessid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("sourcekey").AsCustom("citext").NotNullable()
					.WithColumn("lastobservedon").AsDateTime2().Nullable()
					.WithColumn("lastchangeon").AsDateTime2().Nullable()
					.WithColumn("stalenessseconds").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isstale").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdatafreshness_activation ON incidentdatafreshness (departmentid, incidentactivationid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentdatafreshness_source ON incidentdatafreshness (departmentid, incidentactivationid, sourcekey);");
			}

			if (!Schema.Table("incidentsaveddashboardviews").Exists())
			{
				Create.Table("incidentsaveddashboardviews")
					.WithColumn("incidentsaveddashboardviewid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").Nullable()
					.WithColumn("userid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("deskkey").AsCustom("citext").NotNullable()
					.WithColumn("filtersjson").AsCustom("citext").Nullable()
					.WithColumn("isshared").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentsaveddashboardviews_department ON incidentsaveddashboardviews (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentsaveddashboardviews_user ON incidentsaveddashboardviews (departmentid, userid);");
			}

			if (!Schema.Table("incidentprojectioncursors").Exists())
			{
				Create.Table("incidentprojectioncursors")
					.WithColumn("incidentprojectioncursorid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("contractkey").AsCustom("citext").NotNullable()
					.WithColumn("contractversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("cursor").AsInt64().NotNullable().WithDefaultValue(0)
					.WithColumn("oldestsourceon").AsDateTime2().Nullable()
					.WithColumn("updatedon").AsDateTime2().NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentprojectioncursors_activation ON incidentprojectioncursors (departmentid, incidentactivationid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentprojectioncursors_contract ON incidentprojectioncursors (departmentid, incidentactivationid, contractkey);");
			}

			if (!Schema.Table("incidentnotificationlogs").Exists())
			{
				Create.Table("incidentnotificationlogs")
					.WithColumn("incidentnotificationlogid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("eventtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("positioncode").AsCustom("citext").Nullable()
					.WithColumn("holdersjson").AsCustom("citext").Nullable()
					.WithColumn("fallback").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recipientcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("summary").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentnotificationlogs_activation ON incidentnotificationlogs (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentnotificationlogs_sent ON incidentnotificationlogs (departmentid, incidentactivationid, addedon);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentnotificationlogs").Exists())
				Delete.Table("incidentnotificationlogs");
			if (Schema.Table("incidentprojectioncursors").Exists())
				Delete.Table("incidentprojectioncursors");
			if (Schema.Table("incidentsaveddashboardviews").Exists())
				Delete.Table("incidentsaveddashboardviews");
			if (Schema.Table("incidentdatafreshness").Exists())
				Delete.Table("incidentdatafreshness");
			if (Schema.Table("incidentmetricvalues").Exists())
				Delete.Table("incidentmetricvalues");
			if (Schema.Table("incidentoperationalsnapshots").Exists())
				Delete.Table("incidentoperationalsnapshots");
			if (Schema.Table("incidentexternalorganizationrefs").Exists())
				Delete.Table("incidentexternalorganizationrefs");
			if (Schema.Table("incidentparticipantqualifications").Exists())
				Delete.Table("incidentparticipantqualifications");
			if (Schema.Table("incidentparticipantsensitive").Exists())
				Delete.Table("incidentparticipantsensitive");
			if (Schema.Table("incidentparticipants").Exists())
				Delete.Table("incidentparticipants");
			if (Schema.Table("incidentpositionassignments").Exists())
				Delete.Table("incidentpositionassignments");
			if (Schema.Table("incidentorganizationnodes").Exists())
				Delete.Table("incidentorganizationnodes");
			if (Schema.Table("incidentpositioncapabilities").Exists())
				Delete.Table("incidentpositioncapabilities");
			if (Schema.Table("incidentpositiondefinitions").Exists())
				Delete.Table("incidentpositiondefinitions");
			if (Schema.Table("incidentobjectives").Exists())
				Delete.Table("incidentobjectives");
			if (Schema.Table("incidentplanweatherblocks").Exists())
				Delete.Table("incidentplanweatherblocks");
			if (Schema.Table("incidentplanversions").Exists())
				Delete.Table("incidentplanversions");
			if (Schema.Table("incidentoperationalperiods").Exists())
				Delete.Table("incidentoperationalperiods");
			if (Schema.Table("departmentincidentteamcandidates").Exists())
				Delete.Table("departmentincidentteamcandidates");
			if (Schema.Table("departmentincidentteamslots").Exists())
				Delete.Table("departmentincidentteamslots");
			if (Schema.Table("departmentincidentteamrotations").Exists())
				Delete.Table("departmentincidentteamrotations");
			if (Schema.Table("departmentincidentteams").Exists())
				Delete.Table("departmentincidentteams");
			if (Schema.Table("incidentreconciliationitems").Exists())
				Delete.Table("incidentreconciliationitems");
			if (Schema.Table("incidentauthorityprofilesnapshots").Exists())
				Delete.Table("incidentauthorityprofilesnapshots");
			if (Schema.Table("incidentdomainauthorities").Exists())
				Delete.Table("incidentdomainauthorities");
			if (Schema.Table("departmentincidentauthorityprofiles").Exists())
				Delete.Table("departmentincidentauthorityprofiles");
			if (Schema.Table("incidentactivations").Exists())
				Delete.Table("incidentactivations");
		}
	}
}
