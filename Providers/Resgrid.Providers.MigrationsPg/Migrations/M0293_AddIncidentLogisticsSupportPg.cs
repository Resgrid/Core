using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Incident Back Office LI-B Logistics, support (plan §9.6–§9.11, §9.15, §11.5): facilities with capacity, agreements and
	/// services; lodging properties, blocks, units, beds, the per-night inventory that enforces one occupant per bed-night,
	/// assignments and rooming lists; meal and potable-water planning; support-service templates, tickets and assets;
	/// transport, travel and fuel; the communications assignment ledger (ADP catalog 35). PostgreSQL twin: lower-case
	/// identifiers and every string column citext (plan §11.2), existence-guarded.
	/// </summary>
	[Migration(293)]
	public class M0293_AddIncidentLogisticsSupportPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("incidentfacilities").Exists())
			{
				Create.Table("incidentfacilities")
					.WithColumn("incidentfacilityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("facilitytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("poiid").AsInt32().Nullable()
					.WithColumn("maplayerid").AsCustom("citext").Nullable()
					.WithColumn("indoormapid").AsCustom("citext").Nullable()
					.WithColumn("address").AsCustom("citext").Nullable()
					.WithColumn("operatinghours").AsCustom("citext").Nullable()
					.WithColumn("owneroperator").AsCustom("citext").Nullable()
					.WithColumn("contactid").AsCustom("citext").Nullable()
					.WithColumn("contactphone").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("safetynotes").AsCustom("citext").Nullable()
					.WithColumn("accessnotes").AsCustom("citext").Nullable()
					.WithColumn("openedon").AsDateTime2().Nullable()
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilities_activation ON incidentfacilities (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilities_type ON incidentfacilities (departmentid, incidentactivationid, facilitytype);");
			}

			if (!Schema.Table("incidentfacilitycapacities").Exists())
			{
				Create.Table("incidentfacilitycapacities")
					.WithColumn("incidentfacilitycapacityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").NotNullable()
					.WithColumn("servicekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("maxcapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("usablecapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("currentuse").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("reserved").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("outofservice").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("thresholdpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilitycapacities_activation ON incidentfacilitycapacities (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilitycapacities_facility ON incidentfacilitycapacities (departmentid, incidentfacilityid);");
			}

			if (!Schema.Table("incidentfacilityagreements").Exists())
			{
				Create.Table("incidentfacilityagreements")
					.WithColumn("incidentfacilityagreementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").NotNullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("agreementkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("startson").AsDate().Nullable()
					.WithColumn("endson").AsDate().Nullable()
					.WithColumn("terms").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilityagreements_activation ON incidentfacilityagreements (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilityagreements_facility ON incidentfacilityagreements (departmentid, incidentfacilityid);");
			}

			if (!Schema.Table("incidentfacilityservices").Exists())
			{
				Create.Table("incidentfacilityservices")
					.WithColumn("incidentfacilityserviceid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").NotNullable()
					.WithColumn("servicekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("schedule").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilityservices_activation ON incidentfacilityservices (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfacilityservices_facility ON incidentfacilityservices (departmentid, incidentfacilityid);");
			}

			if (!Schema.Table("incidentlodgingproperties").Exists())
			{
				Create.Table("incidentlodgingproperties")
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("vendorid").AsCustom("citext").Nullable()
					.WithColumn("contactid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("address").AsCustom("citext").Nullable()
					.WithColumn("distanceminutes").AsInt32().Nullable()
					.WithColumn("checkinhours").AsCustom("citext").Nullable()
					.WithColumn("amenities").AsCustom("citext").Nullable()
					.WithColumn("cancellationterms").AsCustom("citext").Nullable()
					.WithColumn("directbillmethod").AsCustom("citext").Nullable()
					.WithColumn("billingcontact").AsCustom("citext").Nullable()
					.WithColumn("cutoffon").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("iscamp").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingproperties_activation ON incidentlodgingproperties (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentlodgingblocks").Exists())
			{
				Create.Table("incidentlodgingblocks")
					.WithColumn("incidentlodgingblockid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable()
					.WithColumn("roomtype").AsCustom("citext").Nullable()
					.WithColumn("firstnight").AsDate().NotNullable()
					.WithColumn("lastnight").AsDate().NotNullable()
					.WithColumn("roomsblocked").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("roomsconfirmed").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("nightlyrate").AsDecimal(18, 2).Nullable()
					.WithColumn("taxpercent").AsDecimal(9, 4).Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("cutoffon").AsDateTime2().Nullable()
					.WithColumn("attritionterms").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("vendoragreementid").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingblocks_activation ON incidentlodgingblocks (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingblocks_property ON incidentlodgingblocks (departmentid, incidentlodgingpropertyid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentlodgingblocks_currency') THEN ALTER TABLE incidentlodgingblocks ADD CONSTRAINT ck_incidentlodgingblocks_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentlodgingunits").Exists())
			{
				Create.Table("incidentlodgingunits")
					.WithColumn("incidentlodgingunitid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingblockid").AsCustom("citext").Nullable()
					.WithColumn("unittype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isaccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isoutofservice").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("unitlabel").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingunits_activation ON incidentlodgingunits (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingunits_property ON incidentlodgingunits (departmentid, incidentlodgingpropertyid);");
			}

			if (!Schema.Table("incidentlodgingbeds").Exists())
			{
				Create.Table("incidentlodgingbeds")
					.WithColumn("incidentlodgingbedid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingunitid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable()
					.WithColumn("bedlabel").AsCustom("citext").Nullable()
					.WithColumn("isaccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isoutofservice").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingbeds_activation ON incidentlodgingbeds (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingbeds_unit ON incidentlodgingbeds (departmentid, incidentlodgingunitid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingbeds_property ON incidentlodgingbeds (departmentid, incidentlodgingpropertyid);");
			}

			if (!Schema.Table("incidentlodgingnightinventory").Exists())
			{
				Create.Table("incidentlodgingnightinventory")
					.WithColumn("incidentlodgingnightinventoryid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingbedid").AsCustom("citext").NotNullable()
					.WithColumn("night").AsDate().NotNullable()
					.WithColumn("incidentlodgingassignmentid").AsCustom("citext").NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingnightinventory_activation ON incidentlodgingnightinventory (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingnightinventory_assignment ON incidentlodgingnightinventory (departmentid, incidentlodgingassignmentid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingnightinventory_night ON incidentlodgingnightinventory (departmentid, incidentactivationid, night);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentlodgingnightinventory_bednight ON incidentlodgingnightinventory (incidentlodgingbedid, night);");
			}

			if (!Schema.Table("incidentlodgingassignments").Exists())
			{
				Create.Table("incidentlodgingassignments")
					.WithColumn("incidentlodgingassignmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingbedid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentparticipantid").AsCustom("citext").Nullable()
					.WithColumn("incidentresourceid").AsCustom("citext").Nullable()
					.WithColumn("reservationlabel").AsCustom("citext").Nullable()
					.WithColumn("firstnight").AsDate().NotNullable()
					.WithColumn("lastnight").AsDate().NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isaccessible").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("requiressingleoccupancy").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("daynightpattern").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isrestperiod").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("incidentbudgetlineid").AsCustom("citext").Nullable()
					.WithColumn("ratesnapshotid").AsCustom("citext").Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("confirmationnumber").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingassignments_activation ON incidentlodgingassignments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingassignments_nights ON incidentlodgingassignments (departmentid, incidentactivationid, firstnight);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingassignments_bed ON incidentlodgingassignments (incidentlodgingbedid, firstnight, lastnight);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentlodgingassignments_participant ON incidentlodgingassignments (incidentparticipantid, firstnight);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentlodgingassignments_currency') THEN ALTER TABLE incidentlodgingassignments ADD CONSTRAINT ck_incidentlodgingassignments_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentroominglistversions").Exists())
			{
				Create.Table("incidentroominglistversions")
					.WithColumn("incidentroominglistversionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentlodgingpropertyid").AsCustom("citext").NotNullable()
					.WithColumn("versionnumber").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("senton").AsDateTime2().Nullable()
					.WithColumn("checksum").AsCustom("citext").Nullable()
					.WithColumn("linecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("contentjson").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentroominglistversions_activation ON incidentroominglistversions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentroominglistversions_property ON incidentroominglistversions (departmentid, incidentlodgingpropertyid, versionnumber);");
			}

			if (!Schema.Table("incidentmealplans").Exists())
			{
				Create.Table("incidentmealplans")
					.WithColumn("incidentmealplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("sitename").AsCustom("citext").NotNullable()
					.WithColumn("mealtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("servicedate").AsDate().NotNullable()
					.WithColumn("windowstart").AsDateTime2().NotNullable()
					.WithColumn("windowend").AsDateTime2().NotNullable()
					.WithColumn("cutoffon").AsDateTime2().Nullable()
					.WithColumn("deliverymode").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("isoffsite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("voucherterms").AsCustom("citext").Nullable()
					.WithColumn("authorizedheadcount").AsInt32().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealplans_activation ON incidentmealplans (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealplans_window ON incidentmealplans (departmentid, incidentactivationid, windowstart);");
			}

			if (!Schema.Table("incidentmealdemands").Exists())
			{
				Create.Table("incidentmealdemands")
					.WithColumn("incidentmealdemandid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentmealplanid").AsCustom("citext").NotNullable()
					.WithColumn("version").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("metricversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("population").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("inbound").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("departures").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("remote").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("guests").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("exclusions").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("contingencypercent").AsDecimal(9, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unregisteredband").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("predictedtimeoutband").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("forecastcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("manualoverride").AsInt32().Nullable()
					.WithColumn("overridereason").AsCustom("citext").Nullable()
					.WithColumn("dietaryjson").AsCustom("citext").Nullable()
					.WithColumn("inputsjson").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealdemands_activation ON incidentmealdemands (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealdemands_plan ON incidentmealdemands (departmentid, incidentmealplanid, version);");
			}

			if (!Schema.Table("incidentmealorders").Exists())
			{
				Create.Table("incidentmealorders")
					.WithColumn("incidentmealorderid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentmealplanid").AsCustom("citext").NotNullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("orderedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("frozenforecastversion").AsInt32().Nullable()
					.WithColumn("unitprice").AsDecimal(18, 4).Nullable()
					.WithColumn("currency").AsCustom("citext").NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealorders_activation ON incidentmealorders (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealorders_plan ON incidentmealorders (departmentid, incidentmealplanid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentmealorders_currency') THEN ALTER TABLE incidentmealorders ADD CONSTRAINT ck_incidentmealorders_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentmealactuals").Exists())
			{
				Create.Table("incidentmealactuals")
					.WithColumn("incidentmealactualid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentmealplanid").AsCustom("citext").NotNullable()
					.WithColumn("delivered").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("served").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("returned").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("waste").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("shortage").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("receivingtemperature").AsCustom("citext").Nullable()
					.WithColumn("inspectionnotes").AsCustom("citext").Nullable()
					.WithColumn("complaint").AsCustom("citext").Nullable()
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealactuals_activation ON incidentmealactuals (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentmealactuals_plan ON incidentmealactuals (departmentid, incidentmealplanid);");
			}

			if (!Schema.Table("incidentwaterplans").Exists())
			{
				Create.Table("incidentwaterplans")
					.WithColumn("incidentwaterplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("sitename").AsCustom("citext").NotNullable()
					.WithColumn("storagecapacity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").NotNullable()
					.WithColumn("reorderthreshold").AsDecimal(18, 4).Nullable()
					.WithColumn("daysofsupplythreshold").AsDecimal(18, 4).Nullable()
					.WithColumn("source").AsCustom("citext").Nullable()
					.WithColumn("coefficientprofile").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentwaterplans_activation ON incidentwaterplans (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentwaterstock").Exists())
			{
				Create.Table("incidentwaterstock")
					.WithColumn("incidentwaterstockid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentwaterplanid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitofmeasure").AsCustom("citext").NotNullable()
					.WithColumn("canonicalquantity").AsDecimal(18, 4).Nullable()
					.WithColumn("canonicalunit").AsCustom("citext").Nullable()
					.WithColumn("measuredon").AsDateTime2().NotNullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentwaterstock_activation ON incidentwaterstock (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentwaterstock_plan ON incidentwaterstock (departmentid, incidentwaterplanid, measuredon);");
			}

			if (!Schema.Table("incidentwaterdeliveries").Exists())
			{
				Create.Table("incidentwaterdeliveries")
					.WithColumn("incidentwaterdeliveryid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentwaterplanid").AsCustom("citext").NotNullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitofmeasure").AsCustom("citext").NotNullable()
					.WithColumn("canonicalquantity").AsDecimal(18, 4).Nullable()
					.WithColumn("canonicalunit").AsCustom("citext").Nullable()
					.WithColumn("scheduledon").AsDateTime2().Nullable()
					.WithColumn("deliveredon").AsDateTime2().Nullable()
					.WithColumn("testresult").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentwaterdeliveries_activation ON incidentwaterdeliveries (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentwaterdeliveries_plan ON incidentwaterdeliveries (departmentid, incidentwaterplanid);");
			}

			if (!Schema.Table("incidentdemandcoefficients").Exists())
			{
				Create.Table("incidentdemandcoefficients")
					.WithColumn("incidentdemandcoefficientid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("populationclass").AsCustom("citext").Nullable()
					.WithColumn("value").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("climate").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentdemandcoefficients_department ON incidentdemandcoefficients (departmentid);");
			}

			if (!Schema.Table("incidentservicetemplates").Exists())
			{
				Create.Table("incidentservicetemplates")
					.WithColumn("incidentservicetemplateid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("code").AsCustom("citext").NotNullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("category").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("defaultslaminutes").AsInt32().Nullable()
					.WithColumn("isselfservice").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentservicetemplates_department ON incidentservicetemplates (departmentid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentservicetemplates_code ON incidentservicetemplates (departmentid, code) WHERE isdeleted = FALSE;");
			}

			if (!Schema.Table("incidentservicerequests").Exists())
			{
				Create.Table("incidentservicerequests")
					.WithColumn("incidentservicerequestid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("incidentservicetemplateid").AsCustom("citext").NotNullable()
					.WithColumn("ticketnumber").AsCustom("citext").NotNullable()
					.WithColumn("requesteruserid").AsCustom("citext").Nullable()
					.WithColumn("requesterparticipantid").AsCustom("citext").Nullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("location").AsCustom("citext").Nullable()
					.WithColumn("priority").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("neededby").AsDateTime2().Nullable()
					.WithColumn("title").AsCustom("citext").NotNullable()
					.WithColumn("quantity").AsDecimal(18, 4).Nullable()
					.WithColumn("unit").AsCustom("citext").Nullable()
					.WithColumn("assetreference").AsCustom("citext").Nullable()
					.WithColumn("assignedposition").AsCustom("citext").Nullable()
					.WithColumn("assigneduserid").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sladueon").AsDateTime2().Nullable()
					.WithColumn("slabreached").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("customerconfirmedon").AsDateTime2().Nullable()
					.WithColumn("costamount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").Nullable()
					.WithColumn("workorderid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("completionnotes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentservicerequests_activation ON incidentservicerequests (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentservicerequests_status ON incidentservicerequests (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentservicerequests_template ON incidentservicerequests (departmentid, incidentservicetemplateid);");
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_incidentservicerequests_ticket ON incidentservicerequests (departmentid, incidentactivationid, ticketnumber) WHERE isdeleted = FALSE;");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentservicerequests_currency') THEN ALTER TABLE incidentservicerequests ADD CONSTRAINT ck_incidentservicerequests_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentserviceactivities").Exists())
			{
				Create.Table("incidentserviceactivities")
					.WithColumn("incidentserviceactivityid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentservicerequestid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("laborminutes").AsInt32().Nullable()
					.WithColumn("amount").AsDecimal(18, 2).Nullable()
					.WithColumn("currency").AsCustom("citext").Nullable()
					.WithColumn("inventorytransactionid").AsCustom("citext").Nullable()
					.WithColumn("description").AsCustom("citext").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentserviceactivities_activation ON incidentserviceactivities (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentserviceactivities_ticket ON incidentserviceactivities (departmentid, incidentservicerequestid);");
				Execute.Sql("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_incidentserviceactivities_currency') THEN ALTER TABLE incidentserviceactivities ADD CONSTRAINT ck_incidentserviceactivities_currency CHECK (char_length(currency) <= 3); END IF; END $$;");
			}

			if (!Schema.Table("incidentserviceassets").Exists())
			{
				Create.Table("incidentserviceassets")
					.WithColumn("incidentserviceassetid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentservicetemplateid").AsCustom("citext").Nullable()
					.WithColumn("name").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("serialnumber").AsCustom("citext").Nullable()
					.WithColumn("inventoryassetid").AsCustom("citext").Nullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentserviceassets_activation ON incidentserviceassets (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidenttransportrequests").Exists())
			{
				Create.Table("incidenttransportrequests")
					.WithColumn("incidenttransportrequestid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("requestedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("pickuplocation").AsCustom("citext").Nullable()
					.WithColumn("dropofflocation").AsCustom("citext").Nullable()
					.WithColumn("neededon").AsDateTime2().Nullable()
					.WithColumn("passengers").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("cargo").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("assignedvehicle").AsCustom("citext").Nullable()
					.WithColumn("assigneddriver").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttransportrequests_activation ON incidenttransportrequests (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttransportrequests_status ON incidenttransportrequests (departmentid, incidentactivationid, status);");
			}

			if (!Schema.Table("incidenttransportmovements").Exists())
			{
				Create.Table("incidenttransportmovements")
					.WithColumn("incidenttransportmovementid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidenttransportrequestid").AsCustom("citext").Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("route").AsCustom("citext").Nullable()
					.WithColumn("vehiclelabel").AsCustom("citext").Nullable()
					.WithColumn("drivername").AsCustom("citext").Nullable()
					.WithColumn("departedon").AsDateTime2().Nullable()
					.WithColumn("arrivedon").AsDateTime2().Nullable()
					.WithColumn("etaon").AsDateTime2().Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("passengercount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("passengernames").AsCustom("citext").Nullable()
					.WithColumn("mileage").AsDecimal(18, 4).Nullable()
					.WithColumn("exceptionkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("pickupaddresses").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttransportmovements_activation ON incidenttransportmovements (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttransportmovements_day ON incidenttransportmovements (departmentid, incidentactivationid, departedon);");
			}

			if (!Schema.Table("incidenttravelitineraries").Exists())
			{
				Create.Table("incidenttravelitineraries")
					.WithColumn("incidenttravelitineraryid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("subjecttype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("subjectid").AsCustom("citext").NotNullable()
					.WithColumn("direction").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("travelmode").AsCustom("citext").Nullable()
					.WithColumn("departureon").AsDate().Nullable()
					.WithColumn("destination").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("bookinglocator").AsCustom("citext").Nullable()
					.WithColumn("carrierdetail").AsCustom("citext").Nullable()
					.WithColumn("pickupaddress").AsCustom("citext").Nullable()
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttravelitineraries_activation ON incidenttravelitineraries (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidenttravelitineraries_subject ON incidenttravelitineraries (departmentid, subjectid);");
			}

			if (!Schema.Table("incidentfueltransactions").Exists())
			{
				Create.Table("incidentfueltransactions")
					.WithColumn("incidentfueltransactionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("fueltype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sitename").AsCustom("citext").Nullable()
					.WithColumn("incidentfacilityid").AsCustom("citext").Nullable()
					.WithColumn("incidentresourceid").AsCustom("citext").Nullable()
					.WithColumn("quantity").AsDecimal(18, 4).NotNullable().WithDefaultValue(0)
					.WithColumn("unitofmeasure").AsCustom("citext").NotNullable()
					.WithColumn("canonicalquantity").AsDecimal(18, 4).Nullable()
					.WithColumn("canonicalunit").AsCustom("citext").Nullable()
					.WithColumn("cardlastfour").AsCustom("citext").Nullable()
					.WithColumn("incidentvendorengagementid").AsCustom("citext").Nullable()
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("exceptionkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfueltransactions_activation ON incidentfueltransactions (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentfueltransactions_day ON incidentfueltransactions (departmentid, incidentactivationid, occurredon);");
			}

			if (!Schema.Table("incidentcommunicationsplans").Exists())
			{
				Create.Table("incidentcommunicationsplans")
					.WithColumn("incidentcommunicationsplanid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentoperationalperiodid").AsCustom("citext").Nullable()
					.WithColumn("recordid").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommunicationsplans_activation ON incidentcommunicationsplans (departmentid, incidentactivationid);");
			}

			if (!Schema.Table("incidentcommunicationsresourceassignments").Exists())
			{
				Create.Table("incidentcommunicationsresourceassignments")
					.WithColumn("incidentcommunicationsresourceassignmentid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("incidentactivationid").AsCustom("citext").NotNullable()
					.WithColumn("incidentcommunicationsplanid").AsCustom("citext").Nullable()
					.WithColumn("itemkind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("itemlabel").AsCustom("citext").NotNullable()
					.WithColumn("serialnumber").AsCustom("citext").Nullable()
					.WithColumn("inventoryassetid").AsCustom("citext").Nullable()
					.WithColumn("holdertype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("holderid").AsCustom("citext").Nullable()
					.WithColumn("holdername").AsCustom("citext").Nullable()
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("expectedreturnon").AsDateTime2().Nullable()
					.WithColumn("returnedon").AsDateTime2().Nullable()
					.WithColumn("programmingprofile").AsCustom("citext").Nullable()
					.WithColumn("condition").AsCustom("citext").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("citext").Nullable()
					.WithColumn("restrictedchannelids").AsCustom("citext").Nullable()
					.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("rowversion").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().Nullable()
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("editedon").AsDateTime2().Nullable()
					.WithColumn("editedbyuserid").AsCustom("citext").Nullable();
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommunicationsresourceassignments_activation ON incidentcommunicationsresourceassignments (departmentid, incidentactivationid);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommunicationsresourceassignments_status ON incidentcommunicationsresourceassignments (departmentid, incidentactivationid, status);");
				Execute.Sql("CREATE INDEX IF NOT EXISTS ix_incidentcommunicationsresourceassignments_holder ON incidentcommunicationsresourceassignments (departmentid, holderid);");
			}

			// Plan §11.5: defense in depth over the per-night rows (the primary mechanism in both dialects). The citext key is cast
			// to text because btree_gist has no citext operator class; a role that may not create btree_gist skips it with a notice.
			Execute.Sql(@"DO $$
			BEGIN
			  BEGIN
			    CREATE EXTENSION IF NOT EXISTS btree_gist;
			  EXCEPTION WHEN insufficient_privilege OR undefined_file THEN
			    RAISE NOTICE 'btree_gist unavailable; lodging exclusion constraint skipped (night rows still enforce it)';
			  END;
			  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')
			     AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_incidentlodgingassignments_bednight') THEN
			    ALTER TABLE incidentlodgingassignments ADD CONSTRAINT ex_incidentlodgingassignments_bednight
			      EXCLUDE USING gist ((incidentlodgingbedid::text) WITH =, daterange(firstnight, lastnight, '[]') WITH &&)
			      WHERE (isdeleted = false AND status NOT IN (3, 5));
			  END IF;
			END $$;");
		}

		public override void Down()
		{
			if (Schema.Table("incidentcommunicationsresourceassignments").Exists())
				Delete.Table("incidentcommunicationsresourceassignments");
			if (Schema.Table("incidentcommunicationsplans").Exists())
				Delete.Table("incidentcommunicationsplans");
			if (Schema.Table("incidentfueltransactions").Exists())
				Delete.Table("incidentfueltransactions");
			if (Schema.Table("incidenttravelitineraries").Exists())
				Delete.Table("incidenttravelitineraries");
			if (Schema.Table("incidenttransportmovements").Exists())
				Delete.Table("incidenttransportmovements");
			if (Schema.Table("incidenttransportrequests").Exists())
				Delete.Table("incidenttransportrequests");
			if (Schema.Table("incidentserviceassets").Exists())
				Delete.Table("incidentserviceassets");
			if (Schema.Table("incidentserviceactivities").Exists())
				Delete.Table("incidentserviceactivities");
			if (Schema.Table("incidentservicerequests").Exists())
				Delete.Table("incidentservicerequests");
			if (Schema.Table("incidentservicetemplates").Exists())
				Delete.Table("incidentservicetemplates");
			if (Schema.Table("incidentdemandcoefficients").Exists())
				Delete.Table("incidentdemandcoefficients");
			if (Schema.Table("incidentwaterdeliveries").Exists())
				Delete.Table("incidentwaterdeliveries");
			if (Schema.Table("incidentwaterstock").Exists())
				Delete.Table("incidentwaterstock");
			if (Schema.Table("incidentwaterplans").Exists())
				Delete.Table("incidentwaterplans");
			if (Schema.Table("incidentmealactuals").Exists())
				Delete.Table("incidentmealactuals");
			if (Schema.Table("incidentmealorders").Exists())
				Delete.Table("incidentmealorders");
			if (Schema.Table("incidentmealdemands").Exists())
				Delete.Table("incidentmealdemands");
			if (Schema.Table("incidentmealplans").Exists())
				Delete.Table("incidentmealplans");
			if (Schema.Table("incidentroominglistversions").Exists())
				Delete.Table("incidentroominglistversions");
			if (Schema.Table("incidentlodgingassignments").Exists())
				Delete.Table("incidentlodgingassignments");
			if (Schema.Table("incidentlodgingnightinventory").Exists())
				Delete.Table("incidentlodgingnightinventory");
			if (Schema.Table("incidentlodgingbeds").Exists())
				Delete.Table("incidentlodgingbeds");
			if (Schema.Table("incidentlodgingunits").Exists())
				Delete.Table("incidentlodgingunits");
			if (Schema.Table("incidentlodgingblocks").Exists())
				Delete.Table("incidentlodgingblocks");
			if (Schema.Table("incidentlodgingproperties").Exists())
				Delete.Table("incidentlodgingproperties");
			if (Schema.Table("incidentfacilityservices").Exists())
				Delete.Table("incidentfacilityservices");
			if (Schema.Table("incidentfacilityagreements").Exists())
				Delete.Table("incidentfacilityagreements");
			if (Schema.Table("incidentfacilitycapacities").Exists())
				Delete.Table("incidentfacilitycapacities");
			if (Schema.Table("incidentfacilities").Exists())
				Delete.Table("incidentfacilities");
		}
	}
}
