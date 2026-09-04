using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0167 (registry M0167, RMS-3): rmsincidentmodules, rmsincidentresources,
	/// rmsincidentanalyses, rmsincidentproperties, rmsincidentvehicles. Lowercase unquoted identifiers, citext for
	/// text, partial unique index for the one-analysis-per-report rule.
	/// </summary>
	[Migration(167)]
	public class M0167_AddRmsConditionalIncidentModulesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsincidentmodules").Exists())
			{
				Create.Table("rmsincidentmodules")
					.WithColumn("rmsincidentmoduleid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("recordkind").AsInt32().NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("modulekind").AsInt32().NotNullable()
					.WithColumn("schemaname").AsCustom("citext").Nullable()
					.WithColumn("profileversion").AsCustom("citext").Nullable()
					.WithColumn("primarycode").AsCustom("citext").Nullable()
					.WithColumn("secondarycode").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("quantityunit").AsCustom("citext").Nullable()
					.WithColumn("occurredon").AsDateTime2().Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentmodules_department_record_revision ON rmsincidentmodules (departmentid, recordid, revisionid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentmodules_department_kind ON rmsincidentmodules (departmentid, modulekind, primarycode);");
			}

			if (!Schema.Table("rmsincidentresources").Exists())
			{
				Create.Table("rmsincidentresources")
					.WithColumn("rmsincidentresourceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("resourcecode").AsCustom("citext").NotNullable()
					.WithColumn("quantity").AsInt32().Nullable()
					.WithColumn("detail").AsCustom("citext").Nullable()
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentresources_department_record_revision ON rmsincidentresources (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsincidentanalyses").Exists())
			{
				Create.Table("rmsincidentanalyses")
					.WithColumn("rmsincidentanalysisid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("incidentreportid").AsString(36).NotNullable()
					.WithColumn("reportingentityid").AsCustom("citext").Nullable()
					.WithColumn("profileversion").AsCustom("citext").Nullable()
					.WithColumn("state").AsInt32().NotNullable()
					.WithColumn("generalcause").AsCustom("citext").Nullable()
					.WithColumn("investigationtypescsv").AsCustom("citext").Nullable()
					.WithColumn("estimatedlosstotal").AsDecimal(18, 2).Nullable()
					.WithColumn("estimatedvaluetotal").AsDecimal(18, 2).Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("authoruserid").AsString(128).NotNullable()
					.WithColumn("owneruserid").AsString(128).Nullable()
					.WithColumn("finalizedon").AsDateTime2().Nullable()
					.WithColumn("finalizedbyuserid").AsString(128).Nullable()
					.WithColumn("currentrevisionid").AsString(36).Nullable()
					.WithColumn("revisioncount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("nerisanalysisid").AsCustom("citext").Nullable()
					.WithColumn("lastsubmissionid").AsString(36).Nullable()
					.WithColumn("lastsubmissionstate").AsInt32().Nullable()
					.WithColumn("lastsubmittedon").AsDateTime2().Nullable()
					.WithColumn("acceptedon").AsDateTime2().Nullable()
					.WithColumn("rejectedon").AsDateTime2().Nullable()
					.WithColumn("rejectionsummary").AsCustom("citext").Nullable()
					.WithColumn("voidedon").AsDateTime2().Nullable()
					.WithColumn("voidedbyuserid").AsString(128).Nullable()
					.WithColumn("voidreasoncode").AsCustom("citext").Nullable()
					.WithColumn("voidreasontext").AsCustom("citext").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentanalyses_department_state ON rmsincidentanalyses (departmentid, state);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsincidentanalyses_report ON rmsincidentanalyses (departmentid, incidentreportid) WHERE deletedon IS NULL;");
			}

			if (!Schema.Table("rmsincidentproperties").Exists())
			{
				Create.Table("rmsincidentproperties")
					.WithColumn("rmsincidentpropertyid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("locationuse").AsCustom("citext").Nullable()
					.WithColumn("constructiontype").AsCustom("citext").Nullable()
					.WithColumn("foundation").AsCustom("citext").Nullable()
					.WithColumn("exteriorfinish").AsCustom("citext").Nullable()
					.WithColumn("roofmaterial").AsCustom("citext").Nullable()
					.WithColumn("storiesabovegrade").AsInt32().Nullable()
					.WithColumn("storiesbelowgrade").AsInt32().Nullable()
					.WithColumn("yearbuilt").AsInt32().Nullable()
					.WithColumn("vacancy").AsCustom("citext").Nullable()
					.WithColumn("damagetype").AsCustom("citext").Nullable()
					.WithColumn("firespread").AsCustom("citext").Nullable()
					.WithColumn("estimatedvalue").AsDecimal(18, 2).Nullable()
					.WithColumn("estimatedloss").AsDecimal(18, 2).Nullable()
					.WithColumn("contentsvalue").AsDecimal(18, 2).Nullable()
					.WithColumn("contentsloss").AsDecimal(18, 2).Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentproperties_department_record_revision ON rmsincidentproperties (departmentid, recordid, revisionid);");
			}

			if (!Schema.Table("rmsincidentvehicles").Exists())
			{
				Create.Table("rmsincidentvehicles")
					.WithColumn("rmsincidentvehicleid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("revisionid").AsString(36).Nullable()
					.WithColumn("vehiclekind").AsCustom("citext").NotNullable()
					.WithColumn("make").AsCustom("citext").Nullable()
					.WithColumn("model").AsCustom("citext").Nullable()
					.WithColumn("modelyear").AsInt32().Nullable()
					.WithColumn("bodystyle").AsCustom("citext").Nullable()
					.WithColumn("powertrain").AsCustom("citext").Nullable()
					.WithColumn("damagetype").AsCustom("citext").Nullable()
					.WithColumn("vin").AsCustom("citext").Nullable()
					.WithColumn("licenseplate").AsCustom("citext").Nullable()
					.WithColumn("licensestate").AsCustom("citext").Nullable()
					.WithColumn("wasoccupied").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("estimatedvalue").AsDecimal(18, 2).Nullable()
					.WithColumn("estimatedloss").AsDecimal(18, 2).Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("detailjson").AsCustom("citext").Nullable()
					.WithColumn("protectedenvelope").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsincidentvehicles_department_record_revision ON rmsincidentvehicles (departmentid, recordid, revisionid);");
			}
		}

		public override void Down()
		{
			foreach (var table in new[] { "rmsincidentvehicles", "rmsincidentproperties", "rmsincidentanalyses", "rmsincidentresources", "rmsincidentmodules" })
			{
				if (Schema.Table(table).Exists())
					Delete.Table(table);
			}
		}
	}
}
