using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Records (RMS-1C) external orders and fills (plan section 4.1 external-order fill contract, registry M0163): one manually entered, checksummed order snapshot per deployment Record with both home and host profiles, and one row per request/fill the department answers with its lifecycle times captured with local offset. No connector, no write-back.
	/// PostgreSQL twin of the SQL Server migration; lower-case identifiers, citext keys, existence-guarded.
	/// </summary>
	[Migration(163)]
	public class M0163_AddRmsExternalOrderReferencesPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsexternalorders").Exists())
			{
				Create.Table("rmsexternalorders")
					.WithColumn("rmsexternalorderid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("profilekey").AsCustom("citext").NotNullable()
					.WithColumn("profileversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("homeprofilekey").AsCustom("citext").Nullable()
					.WithColumn("hostprofilekey").AsCustom("citext").Nullable()
					.WithColumn("sourcescheme").AsCustom("citext").Nullable()
					.WithColumn("sourcesystem").AsCustom("citext").Nullable()
					.WithColumn("ordernumber").AsCustom("citext").NotNullable()
					.WithColumn("incidentname").AsCustom("citext").NotNullable()
					.WithColumn("incidentnumber").AsCustom("citext").Nullable()
					.WithColumn("incidentcountry").AsCustom("citext").Nullable()
					.WithColumn("incidentsubdivision").AsCustom("citext").Nullable()
					.WithColumn("orderingoffice").AsCustom("citext").Nullable()
					.WithColumn("dispatchoffice").AsCustom("citext").Nullable()
					.WithColumn("requestingagency").AsCustom("citext").Nullable()
					.WithColumn("receivingagency").AsCustom("citext").Nullable()
					.WithColumn("sendingagency").AsCustom("citext").Nullable()
					.WithColumn("departmentrole").AsCustom("citext").Nullable()
					.WithColumn("costcode").AsCustom("citext").Nullable()
					.WithColumn("agreementreference").AsCustom("citext").Nullable()
					.WithColumn("currencycode").AsCustom("citext").Nullable()
					.WithColumn("measurementsystem").AsCustom("citext").Nullable()
					.WithColumn("timezoneid").AsCustom("citext").Nullable()
					.WithColumn("capturedoffsetminutes").AsInt32().Nullable()
					.WithColumn("sourcecapturedon").AsDateTime2().Nullable()
					.WithColumn("sourceversion").AsCustom("citext").Nullable()
					.WithColumn("artifactfilename").AsCustom("citext").Nullable()
					.WithColumn("artifactcontenttype").AsCustom("citext").Nullable()
					.WithColumn("artifactchecksum").AsCustom("citext").Nullable()
					.WithColumn("artifactdata").AsCustom("bytea").Nullable()
					.WithColumn("artifactsafeurl").AsCustom("text").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("mobilizedon").AsDateTime2().Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("closedouton").AsDateTime2().Nullable()
					.WithColumn("closedoutbyuserid").AsCustom("citext").Nullable()
					.WithColumn("closeoutnotes").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsexternalorders_department_record ON rmsexternalorders (departmentid, recordid) WHERE deletedon IS NULL;");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalorders_department_order ON rmsexternalorders (departmentid, ordernumber);");
			}

			if (!Schema.Table("rmsexternalorderfills").Exists())
			{
				Create.Table("rmsexternalorderfills")
					.WithColumn("rmsexternalorderfillid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsCustom("citext").NotNullable()
					.WithColumn("rmsexternalorderid").AsCustom("citext").NotNullable()
					.WithColumn("recordid").AsCustom("citext").NotNullable()
					.WithColumn("requestnumber").AsCustom("citext").NotNullable()
					.WithColumn("parentrequestnumber").AsCustom("citext").Nullable()
					.WithColumn("requestcategory").AsCustom("citext").Nullable()
					.WithColumn("fillnumber").AsCustom("citext").Nullable()
					.WithColumn("resourcekind").AsCustom("citext").Nullable()
					.WithColumn("resourcetype").AsCustom("citext").Nullable()
					.WithColumn("resourcetypescheme").AsCustom("citext").Nullable()
					.WithColumn("position").AsCustom("citext").Nullable()
					.WithColumn("positionscheme").AsCustom("citext").Nullable()
					.WithColumn("istrainee").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("homeunit").AsCustom("citext").Nullable()
					.WithColumn("hostagency").AsCustom("citext").Nullable()
					.WithColumn("agencyunitid").AsCustom("citext").Nullable()
					.WithColumn("pointofhire").AsCustom("citext").Nullable()
					.WithColumn("costcode").AsCustom("citext").Nullable()
					.WithColumn("agreementreference").AsCustom("citext").Nullable()
					.WithColumn("assigneduserid").AsCustom("citext").Nullable()
					.WithColumn("assignedunitid").AsInt32().Nullable()
					.WithColumn("qualificationsjson").AsCustom("text").Nullable()
					.WithColumn("rosterjson").AsCustom("text").Nullable()
					.WithColumn("traveljson").AsCustom("text").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("declinereason").AsCustom("text").Nullable()
					.WithColumn("requestedon").AsDateTime2().Nullable()
					.WithColumn("neededon").AsDateTime2().Nullable()
					.WithColumn("filledon").AsDateTime2().Nullable()
					.WithColumn("mobilizedon").AsDateTime2().Nullable()
					.WithColumn("checkedinon").AsDateTime2().Nullable()
					.WithColumn("assignedon").AsDateTime2().Nullable()
					.WithColumn("releasedon").AsDateTime2().Nullable()
					.WithColumn("demobilizedon").AsDateTime2().Nullable()
					.WithColumn("returnedon").AsDateTime2().Nullable()
					.WithColumn("capturedoffsetminutes").AsInt32().Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsCustom("citext").Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsexternalorderfills_department_order ON rmsexternalorderfills (departmentid, rmsexternalorderid);");
			}

		}

		public override void Down()
		{
			if (Schema.Table("rmsexternalorderfills").Exists())
				Delete.Table("rmsexternalorderfills");
			if (Schema.Table("rmsexternalorders").Exists())
				Delete.Table("rmsexternalorders");
		}
	}
}
