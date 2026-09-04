using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0164 (registry M0164, RMS-2): rmsincidentreports, rmssourcefacts, rmsunitresponses,
	/// rmsincidenttypes, rmsactiontactics, rmsaids, rmslocations, rmsnarratives, rmsvalidationissues. Lowercase
	/// unquoted identifiers, citext for text, partial unique indexes for the SingleAuthoritative and idempotency rules.
	/// </summary>
	[Migration(164)]
	public class M0164_AddRmsIncidentReportCorePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsincidentreports").Exists())
			{
				Create.Table("rmsincidentreports")
					.WithColumn("rmsincidentreportid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("callid").AsInt32().NotNullable()
					.WithColumn("reportingentityid").AsCustom("citext").NotNullable()
					.WithColumn("definitionkey").AsCustom("citext").NotNullable()
					.WithColumn("definitionversion").AsInt32().NotNullable()
					.WithColumn("profileversion").AsCustom("citext").Nullable()
					.WithColumn("lifecyclepreset").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("recordnumber").AsCustom("citext").Nullable()
					.WithColumn("draftreference").AsCustom("citext").Nullable()
					.WithColumn("incidentnumber").AsCustom("citext").Nullable()
					.WithColumn("displaysummary").AsCustom("citext").Nullable()
					.WithColumn("stationgroupid").AsInt32().Nullable()
					.WithColumn("authoruserid").AsString(128).NotNullable()
					.WithColumn("owneruserid").AsString(128).Nullable()
					.WithColumn("revieweruserid").AsString(128).Nullable()
					.WithColumn("approveruserid").AsString(128).Nullable()
					.WithColumn("reviewdueon").AsDateTime2().Nullable()
					.WithColumn("submittedforreviewon").AsDateTime2().Nullable()
					.WithColumn("returnedon").AsDateTime2().Nullable()
					.WithColumn("returnreasoncode").AsCustom("citext").Nullable()
					.WithColumn("returnreasontext").AsCustom("citext").Nullable()
					.WithColumn("returncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("approvedon").AsDateTime2().Nullable()
					.WithColumn("finalizedon").AsDateTime2().Nullable()
					.WithColumn("finalizedbyuserid").AsString(128).Nullable()
					.WithColumn("currentrevisionid").AsString(36).Nullable()
					.WithColumn("revisioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("amendsrevisionid").AsString(36).Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsString(128).Nullable()
					.WithColumn("voidreasoncode").AsCustom("citext").Nullable()
					.WithColumn("voidreasontext").AsCustom("citext").Nullable()
					.WithColumn("cancelledon").AsDateTime2().Nullable()
					.WithColumn("cancelledbyuserid").AsString(128).Nullable()
					.WithColumn("nerisincidentid").AsCustom("citext").Nullable()
					.WithColumn("lastsubmissionid").AsString(36).Nullable()
					.WithColumn("lastsubmissionstate").AsInt32().Nullable()
					.WithColumn("lastsubmittedon").AsDateTime2().Nullable()
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("rejectedon").AsDateTime2().Nullable()
					.WithColumn("rejectionsummary").AsCustom("citext").Nullable()
					.WithColumn("callcreatedon").AsDateTime2().Nullable()
					.WithColumn("callansweredon").AsDateTime2().Nullable()
					.WithColumn("callarrivalon").AsDateTime2().Nullable()
					.WithColumn("incidentclearedon").AsDateTime2().Nullable()
					.WithColumn("dispatchcenterid").AsCustom("citext").Nullable()
					.WithColumn("determinantcode").AsCustom("citext").Nullable()
					.WithColumn("dispatchincidentcode").AsCustom("citext").Nullable()
					.WithColumn("disposition").AsCustom("citext").Nullable()
					.WithColumn("peoplepresent").AsBoolean().Nullable()
					.WithColumn("displacementcount").AsInt32().Nullable()
					.WithColumn("animalsrescued").AsInt32().Nullable()
					.WithColumn("specialmodifierscsv").AsCustom("citext").Nullable()
					.WithColumn("idempotencykey").AsCustom("citext").Nullable()
					.WithColumn("originclient").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentreports_department_state_created ON rmsincidentreports (departmentid, state, createdon DESC);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentreports_department_call ON rmsincidentreports (departmentid, callid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentreports_department_owner ON rmsincidentreports (departmentid, owneruserid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsincidentreports_call_entity ON rmsincidentreports (departmentid, callid, reportingentityid, definitionkey) WHERE deletedon IS NULL;");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsincidentreports_idempotency ON rmsincidentreports (departmentid, idempotencykey) WHERE idempotencykey IS NOT NULL;");
			}

			if (!Schema.Table("rmssourcefacts").Exists())
			{
				Create.Table("rmssourcefacts")
					.WithColumn("rmssourcefactid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("factkey").AsCustom("citext").NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable()
					.WithColumn("sourcesystem").AsCustom("citext").Nullable()
					.WithColumn("sourceentitytype").AsCustom("citext").Nullable()
					.WithColumn("sourceentityid").AsCustom("citext").Nullable()
					.WithColumn("sourcevalue").AsCustom("citext").Nullable()
					.WithColumn("currentvalue").AsCustom("citext").Nullable()
					.WithColumn("sourcetime").AsDateTime2().Nullable()
					.WithColumn("importedon").AsDateTime2().NotNullable()
					.WithColumn("correctedon").AsDateTime2().Nullable()
					.WithColumn("correctedbyuserid").AsString(128).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmssourcefacts_department_record_revision ON rmssourcefacts (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsunitresponses").Exists())
			{
				Create.Table("rmsunitresponses")
					.WithColumn("rmsunitresponseid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("unitid").AsInt32().Nullable()
					.WithColumn("unitnamesnapshot").AsCustom("citext").Nullable()
					.WithColumn("unittypesnapshot").AsCustom("citext").Nullable()
					.WithColumn("unitnerisid").AsCustom("citext").Nullable()
					.WithColumn("stationgroupidsnapshot").AsInt32().Nullable()
					.WithColumn("staffing").AsInt32().Nullable()
					.WithColumn("unabletodispatch").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("dispatchedon").AsDateTime2().Nullable()
					.WithColumn("enrouteon").AsDateTime2().Nullable()
					.WithColumn("onsceneon").AsDateTime2().Nullable()
					.WithColumn("canceledenrouteon").AsDateTime2().Nullable()
					.WithColumn("stagingon").AsDateTime2().Nullable()
					.WithColumn("clearedon").AsDateTime2().Nullable()
					.WithColumn("responsemode").AsCustom("citext").Nullable()
					.WithColumn("transportmode").AsCustom("citext").Nullable()
					.WithColumn("timessourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("disposition").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsunitresponses_department_record_revision ON rmsunitresponses (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsincidenttypes").Exists())
			{
				Create.Table("rmsincidenttypes")
					.WithColumn("rmsincidenttypeid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("typecode").AsCustom("citext").NotNullable()
					.WithColumn("isprimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("localcode").AsCustom("citext").Nullable()
					.WithColumn("valuesetversion").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidenttypes_department_record_revision ON rmsincidenttypes (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsactiontactics").Exists())
			{
				Create.Table("rmsactiontactics")
					.WithColumn("rmsactiontacticid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("tacticcode").AsCustom("citext").NotNullable()
					.WithColumn("actorunitid").AsInt32().Nullable()
					.WithColumn("occurredon").AsDateTime2().Nullable()
					.WithColumn("sourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("outcome").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsactiontactics_department_record_revision ON rmsactiontactics (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsaids").Exists())
			{
				Create.Table("rmsaids")
					.WithColumn("rmsaidid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("direction").AsCustom("citext").NotNullable()
					.WithColumn("aidtype").AsCustom("citext").NotNullable()
					.WithColumn("counterpartnerisid").AsCustom("citext").Nullable()
					.WithColumn("counterpartname").AsCustom("citext").Nullable()
					.WithColumn("isnonfiredepartment").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("nonfdtype").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsaids_department_record_revision ON rmsaids (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmslocations").Exists())
			{
				Create.Table("rmslocations")
					.WithColumn("rmslocationid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("addresstext").AsCustom("citext").Nullable()
					.WithColumn("number").AsCustom("citext").Nullable()
					.WithColumn("numberprefix").AsCustom("citext").Nullable()
					.WithColumn("numbersuffix").AsCustom("citext").Nullable()
					.WithColumn("street").AsCustom("citext").Nullable()
					.WithColumn("unitvalue").AsCustom("citext").Nullable()
					.WithColumn("municipality").AsCustom("citext").Nullable()
					.WithColumn("county").AsCustom("citext").Nullable()
					.WithColumn("state").AsCustom("citext").Nullable()
					.WithColumn("postalcode").AsCustom("citext").Nullable()
					.WithColumn("country").AsCustom("citext").Nullable()
					.WithColumn("placetype").AsCustom("citext").Nullable()
					.WithColumn("locationuse").AsCustom("citext").Nullable()
					.WithColumn("crossstreet1").AsCustom("citext").Nullable()
					.WithColumn("crossstreet2").AsCustom("citext").Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("jurisdiction").AsCustom("citext").Nullable()
					.WithColumn("sourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmslocations_department_record_revision ON rmslocations (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsnarratives").Exists())
			{
				Create.Table("rmsnarratives")
					.WithColumn("rmsnarrativeid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("narrative").AsCustom("citext").Nullable()
					.WithColumn("impedimentnarrative").AsCustom("citext").Nullable()
					.WithColumn("outcomenarrative").AsCustom("citext").Nullable()
					.WithColumn("supplementaljson").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedenvelope").AsString(int.MaxValue).Nullable()
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsnarratives_department_record_revision ON rmsnarratives (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsvalidationissues").Exists())
			{
				Create.Table("rmsvalidationissues")
					.WithColumn("rmsvalidationissueid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("profileversion").AsCustom("citext").Nullable()
					.WithColumn("rulekey").AsCustom("citext").NotNullable()
					.WithColumn("severity").AsInt32().NotNullable()
					.WithColumn("fieldpath").AsCustom("citext").Nullable()
					.WithColumn("message").AsCustom("citext").Nullable()
					.WithColumn("source").AsInt32().NotNullable()
					.WithColumn("resolvedon").AsDateTime2().Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsvalidationissues_department_record ON rmsvalidationissues (departmentid, recordid);");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsvalidationissues", "rmsnarratives", "rmslocations", "rmsaids", "rmsactiontactics", "rmsincidenttypes", "rmsunitresponses", "rmssourcefacts", "rmsincidentreports" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
