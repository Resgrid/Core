using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B vendors (plan §9.12, §9.28): the department support plan, areas and vendor preferences,
	/// children of the shipped InventoryVendors row (locations, capabilities, availability windows, compliance documents,
	/// protected tax / remittance detail), incident vendor engagements, capacity holds and commitments, quotes, and participant
	/// / vendor self-service links (ADP catalog 35). PostgreSQL twin: lower-case identifiers and every string column citext
	/// (plan §11.2), existence-guarded.
	/// </summary>
	[Migration(294)]
	public class M0294_AddIncidentVendorsAndSelfServicePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("departmentemergencysupportplans").Exists())
			{
				Create.Table("departmentemergencysupportplans")
					.WithColumn("departmentemergencysupportplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("nextreviewon").AsDate().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_departmentemergencysupportplans_department ON departmentemergencysupportplans (departmentid);");
			}

			if (!Schema.Table("supportplanareas").Exists())
			{
				Create.Table("supportplanareas")
					.WithColumn("supportplanareaid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentemergencysupportplanid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("geography").AsCustom("citext").Nullable()
					.WithColumn("incidenttypes").AsCustom("citext").Nullable()
					.WithColumn("activationthreshold").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_supportplanareas_department ON supportplanareas (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_supportplanareas_plan ON supportplanareas (departmentid, departmentemergencysupportplanid);");
			}

			if (!Schema.Table("supportplanvendorpreferences").Exists())
			{
				Create.Table("supportplanvendorpreferences")
					.WithColumn("supportplanvendorpreferenceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("departmentemergencysupportplanid").AsCustom("citext").NotNullable()
					.WithColumn("supportplanareaid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rank").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_supportplanvendorpreferences_department ON supportplanvendorpreferences (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_supportplanvendorpreferences_plan ON supportplanvendorpreferences (departmentid, departmentemergencysupportplanid, category);");
			}

			if (!Schema.Table("incidentsupportplansnapshots").Exists())
			{
				Create.Table("incidentsupportplansnapshots")
					.WithColumn("incidentsupportplansnapshotid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("departmentemergencysupportplanid").AsCustom("citext").NotNullable()
					.WithColumn("contentjson").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentsupportplansnapshots_activation ON incidentsupportplansnapshots (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("vendorlocations").Exists())
			{
				Create.Table("vendorlocations")
					.WithColumn("vendorlocationid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("address").AsCustom("citext").Nullable()
					.WithColumn("serviceradiuskm").AsDecimal(18, 4).Nullable()
					.WithColumn("dispatchphone").AsCustom("citext").Nullable()
					.WithColumn("afterhoursphone").AsCustom("citext").Nullable()
					.WithColumn("orderingchannel").AsCustom("citext").Nullable()
					.WithColumn("hours").AsCustom("citext").Nullable()
					.WithColumn("is24x7").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("languages").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorlocations_department ON vendorlocations (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorlocations_vendor ON vendorlocations (departmentid, vendorid);");
			}

			if (!Schema.Table("vendorcapabilities").Exists())
			{
				Create.Table("vendorcapabilities")
					.WithColumn("vendorcapabilityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("vendorlocationid").AsCustom("citext").Nullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("unit").AsCustom("citext").NotNullable()
					.WithColumn("normalcapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("maxcapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("leadtimehours").AsInt32().Nullable()
					.WithColumn("minimumorder").AsCustom("citext").Nullable()
					.WithColumn("cutofftime").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("lastverifiedon").AsDateTime2().Nullable()
					.WithColumn("verifiedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcapabilities_department ON vendorcapabilities (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcapabilities_vendor ON vendorcapabilities (departmentid, vendorid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcapabilities_category ON vendorcapabilities (departmentid, category);");
			}

			if (!Schema.Table("vendoravailabilitywindows").Exists())
			{
				Create.Table("vendoravailabilitywindows")
					.WithColumn("vendoravailabilitywindowid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorcapabilityid").AsCustom("citext").NotNullable()
					.WithColumn("startson").AsDateTime2().NotNullable()
					.WithColumn("endson").AsDateTime2().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("capacity").AsDecimal(18, 4).Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendoravailabilitywindows_department ON vendoravailabilitywindows (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendoravailabilitywindows_capability ON vendoravailabilitywindows (departmentid, vendorcapabilityid, startson);");
			}

			if (!Schema.Table("vendorcompliancedocuments").Exists())
			{
				Create.Table("vendorcompliancedocuments")
					.WithColumn("vendorcompliancedocumentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("documentnumber").AsCustom("citext").Nullable()
					.WithColumn("issuedon").AsDate().Nullable()
					.WithColumn("expireson").AsDate().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcompliancedocuments_department ON vendorcompliancedocuments (departmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcompliancedocuments_vendor ON vendorcompliancedocuments (departmentid, vendorid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorcompliancedocuments_expiry ON vendorcompliancedocuments (departmentid, expireson);");
			}

			if (!Schema.Table("vendorprotecteddetails").Exists())
			{
				Create.Table("vendorprotecteddetails")
					.WithColumn("vendorprotecteddetailid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("taxidentifier").AsCustom("citext").Nullable()
					.WithColumn("remittancedetails").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_vendorprotecteddetails_department ON vendorprotecteddetails (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_vendorprotecteddetails_vendor ON vendorprotecteddetails (departmentid, vendorid);");
			}

			if (!Schema.Table("incidentvendorengagements").Exists())
			{
				Create.Table("incidentvendorengagements")
					.WithColumn("incidentvendorengagementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("vendorid").AsCustom("citext").NotNullable()
					.WithColumn("vendorcapabilityid").AsCustom("citext").Nullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("servicedescription").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("purchaseruserid").AsCustom("citext").Nullable()
					.WithColumn("owneruserid").AsCustom("citext").Nullable()
					.WithColumn("windowstart").AsDateTime2().Nullable()
					.WithColumn("windowend").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorengagements_activation ON incidentvendorengagements (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorengagements_status ON incidentvendorengagements (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorengagements_vendor ON incidentvendorengagements (departmentid, vendorid);");
			}

			if (!Schema.Table("incidentvendorcapacitycommitments").Exists())
			{
				Create.Table("incidentvendorcapacitycommitments")
					.WithColumn("incidentvendorcapacitycommitmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").NotNullable()
					.WithColumn("vendorcapabilityid").AsCustom("citext").NotNullable()
					.WithColumn("bucketstart").AsDateTime2().NotNullable()
					.WithColumn("bucketend").AsDateTime2().NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("isoverride").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("overridereason").AsCustom("citext").Nullable()
					.WithColumn("approvedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorcapacitycommitments_activation ON incidentvendorcapacitycommitments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorcapacitycommitments_capability ON incidentvendorcapacitycommitments (vendorcapabilityid, bucketstart, bucketend);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorcapacitycommitments_engagement ON incidentvendorcapacitycommitments (departmentid, incidentvendorengagementid);");
			}

			if (!Schema.Table("incidentvendorquotes").Exists())
			{
				Create.Table("incidentvendorquotes")
					.WithColumn("incidentvendorquoteid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").NotNullable()
					.WithColumn("requestedon").AsDateTime2().NotNullable()
					.WithColumn("receivedon").AsDateTime2().Nullable()
					.WithColumn("amount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("terms").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorquotes_activation ON incidentvendorquotes (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentvendorquotes_engagement ON incidentvendorquotes (departmentid, incidentvendorengagementid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentvendorquotes_currency') THEN ALTER TABLE incidentvendorquotes ADD CONSTRAINT ck_incidentvendorquotes_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentselfservicelinks").Exists())
			{
				Create.Table("incidentselfservicelinks")
					.WithColumn("incidentselfservicelinkid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("tokenhash").AsCustom("citext").NotNullable()
					.WithColumn("purposes").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("expireson").AsDateTime2().NotNullable()
					.WithColumn("revokedon").AsDateTime2().Nullable()
					.WithColumn("revokedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("lastusedon").AsDateTime2().Nullable()
					.WithColumn("usecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("onetimecodehash").AsCustom("citext").Nullable()
					.WithColumn("onetimecodeexpireson").AsDateTime2().Nullable()
					.WithColumn("verifiedon").AsDateTime2().Nullable()
					.WithColumn("deliverytarget").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentselfservicelinks_activation ON incidentselfservicelinks (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentselfservicelinks_subject ON incidentselfservicelinks (departmentid, subjectid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentselfservicelinks_token ON incidentselfservicelinks (tokenhash);");
			}
		}

		public override void Down()
		{
			if (Schema.Table("incidentselfservicelinks").Exists())
				Delete.Table("incidentselfservicelinks");
			if (Schema.Table("incidentvendorquotes").Exists())
				Delete.Table("incidentvendorquotes");
			if (Schema.Table("incidentvendorcapacitycommitments").Exists())
				Delete.Table("incidentvendorcapacitycommitments");
			if (Schema.Table("incidentvendorengagements").Exists())
				Delete.Table("incidentvendorengagements");
			if (Schema.Table("vendorprotecteddetails").Exists())
				Delete.Table("vendorprotecteddetails");
			if (Schema.Table("vendorcompliancedocuments").Exists())
				Delete.Table("vendorcompliancedocuments");
			if (Schema.Table("vendoravailabilitywindows").Exists())
				Delete.Table("vendoravailabilitywindows");
			if (Schema.Table("vendorcapabilities").Exists())
				Delete.Table("vendorcapabilities");
			if (Schema.Table("vendorlocations").Exists())
				Delete.Table("vendorlocations");
			if (Schema.Table("incidentsupportplansnapshots").Exists())
				Delete.Table("incidentsupportplansnapshots");
			if (Schema.Table("supportplanvendorpreferences").Exists())
				Delete.Table("supportplanvendorpreferences");
			if (Schema.Table("supportplanareas").Exists())
				Delete.Table("supportplanareas");
			if (Schema.Table("departmentemergencysupportplans").Exists())
				Delete.Table("departmentemergencysupportplans");
		}
	}
}
