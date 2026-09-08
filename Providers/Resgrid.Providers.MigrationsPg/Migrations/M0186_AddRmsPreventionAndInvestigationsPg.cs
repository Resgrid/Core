using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RMS-5 prevention and investigations (RMS plan sections 4.3, 4.4 and 6; registry M0186): the occupancy/property
	/// master with role-separated Contact links, hazards, the ContactPreplan/Contact/POI crosswalk, field provenance and
	/// the per-department structure-write ownership row; inspection programs, adopted code sets, inspections and
	/// violations; hydrants with flow tests and maintenance; permit types, permits and plan reviews; community risk
	/// reduction activities; prevention attachments and number sequences; investigation cases with members, linked
	/// incidents, notes, evidence, an append-only custody chain and referrals. Seeds the seven module feature flags off.
	/// Every table carries DepartmentId and ProtectionId; the cataloged text columns (ADP catalog v13) carry the
	/// IsProtected marker. Guarded for safe retry.
	/// </summary>
	[Migration(186)]
	public class M0186_AddRmsPreventionAndInvestigationsPg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsoccupancies").Exists())
			{
				Create.Table("rmsoccupancies")
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("occupancynumber").AsString(32).Nullable()
					.WithColumn("name").AsString(250).NotNullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("mergedintooccupancyid").AsString(36).Nullable()
					.WithColumn("addresstext").AsString(500).Nullable()
					.WithColumn("city").AsString(120).Nullable()
					.WithColumn("stateprovince").AsString(120).Nullable()
					.WithColumn("postalcode").AsString(20).Nullable()
					.WithColumn("country").AsString(80).Nullable()
					.WithColumn("normalizedaddress").AsString(500).Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("parcelid").AsString(100).Nullable()
					.WithColumn("constructiontype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rooftype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("occupancytype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("stories").AsInt32().Nullable()
					.WithColumn("yearbuilt").AsInt32().Nullable()
					.WithColumn("squarefeet").AsInt32().Nullable()
					.WithColumn("occupantload").AsInt32().Nullable()
					.WithColumn("occupancyhours").AsString(250).Nullable()
					.WithColumn("hasoccupantsneedingassistance").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("occupantsneedingassistancenotes").AsCustom("text").Nullable()
					.WithColumn("sprinklertype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("hasstandpipe").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("hasfirealarm").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("fdclocation").AsString(250).Nullable()
					.WithColumn("gasshutofflocation").AsString(500).Nullable()
					.WithColumn("electricshutofflocation").AsString(500).Nullable()
					.WithColumn("watershutofflocation").AsString(500).Nullable()
					.WithColumn("utilitynotes").AsCustom("text").Nullable()
					.WithColumn("knoxboxlocation").AsCustom("text").Nullable()
					.WithColumn("gatecode").AsCustom("text").Nullable()
					.WithColumn("alarmpanellocation").AsCustom("text").Nullable()
					.WithColumn("alarmcompany").AsCustom("text").Nullable()
					.WithColumn("alarmcompanyphone").AsCustom("text").Nullable()
					.WithColumn("accessnotes").AsCustom("text").Nullable()
					.WithColumn("nearesthydrantid").AsString(36).Nullable()
					.WithColumn("requiredfireflowgpm").AsInt32().Nullable()
					.WithColumn("watersupplynotes").AsCustom("text").Nullable()
					.WithColumn("emergencycontactname").AsCustom("text").Nullable()
					.WithColumn("emergencycontactphone").AsCustom("text").Nullable()
					.WithColumn("hazmatonsite").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("generalhazardnotes").AsCustom("text").Nullable()
					.WithColumn("tacticalsummary").AsCustom("text").Nullable()
					.WithColumn("poiid").AsInt32().Nullable()
					.WithColumn("lastreviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable()
					.WithColumn("nextreviewdue").AsDateTime2().Nullable()
					.WithColumn("lastinspectedon").AsDateTime2().Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("modifiedbyuserid").AsString(128).Nullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancies_Department_Status").OnTable("rmsoccupancies").OnColumn("departmentid").Ascending().OnColumn("status").Ascending().OnColumn("deletedon").Ascending();
				Create.Index("IX_RmsOccupancies_Department_Address").OnTable("rmsoccupancies").OnColumn("departmentid").Ascending().OnColumn("normalizedaddress").Ascending();
				Create.Index("IX_RmsOccupancies_Department_Review").OnTable("rmsoccupancies").OnColumn("departmentid").Ascending().OnColumn("nextreviewdue").Ascending();
			}
			if (!Schema.Table("rmsoccupancycontactlinks").Exists())
			{
				Create.Table("rmsoccupancycontactlinks")
					.WithColumn("rmsoccupancycontactlinkid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable()
					.WithColumn("contactid").AsString(128).NotNullable()
					.WithColumn("role").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprimary").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancyContactLinks_Occupancy").OnTable("rmsoccupancycontactlinks").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
				Create.Index("IX_RmsOccupancyContactLinks_Contact").OnTable("rmsoccupancycontactlinks").OnColumn("departmentid").Ascending().OnColumn("contactid").Ascending();
			}
			if (!Schema.Table("rmsoccupancyhazards").Exists())
			{
				Create.Table("rmsoccupancyhazards")
					.WithColumn("rmsoccupancyhazardid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable()
					.WithColumn("hazardtype").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("severity").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("title").AsString(200).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("locationdescription").AsCustom("text").Nullable()
					.WithColumn("gpscoordinates").AsCustom("text").Nullable()
					.WithColumn("shouldalert").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("sourcecontactpreplanhazardid").AsString(128).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsOccupancyHazards_Occupancy").OnTable("rmsoccupancyhazards").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
			}
			if (!Schema.Table("rmsoccupancycrosswalks").Exists())
			{
				Create.Table("rmsoccupancycrosswalks")
					.WithColumn("rmsoccupancycrosswalkid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).Nullable()
					.WithColumn("sourcekind").AsInt32().NotNullable()
					.WithColumn("sourceid").AsString(128).NotNullable()
					.WithColumn("contactid").AsString(128).Nullable()
					.WithColumn("sourcedisplayname").AsString(250).Nullable()
					.WithColumn("normalizedaddress").AsString(500).Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("matchconfidence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("matchreason").AsString(250).Nullable()
					.WithColumn("suggestedoccupancyid").AsString(36).Nullable()
					.WithColumn("groupkey").AsString(64).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("decidedon").AsDateTime2().Nullable()
					.WithColumn("decidedbyuserid").AsString(128).Nullable()
					.WithColumn("inventoriedon").AsDateTime2().NotNullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsOccupancyCrosswalks_Department_State").OnTable("rmsoccupancycrosswalks").OnColumn("departmentid").Ascending().OnColumn("state").Ascending();
				Create.Index("IX_RmsOccupancyCrosswalks_Occupancy").OnTable("rmsoccupancycrosswalks").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
				Create.Index("IX_RmsOccupancyCrosswalks_Contact").OnTable("rmsoccupancycrosswalks").OnColumn("departmentid").Ascending().OnColumn("contactid").Ascending();
				Create.Index("UX_RmsOccupancyCrosswalks_Source").OnTable("rmsoccupancycrosswalks").OnColumn("departmentid").Ascending().OnColumn("sourcekind").Ascending().OnColumn("sourceid").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("rmsoccupancyfieldprovenances").Exists())
			{
				Create.Table("rmsoccupancyfieldprovenances")
					.WithColumn("rmsoccupancyfieldprovenanceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable()
					.WithColumn("fieldkey").AsString(100).NotNullable()
					.WithColumn("sourcekind").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("sourceid").AsString(128).Nullable()
					.WithColumn("capturedon").AsDateTime2().NotNullable()
					.WithColumn("capturedbyuserid").AsString(128).Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable();

				Create.Index("IX_RmsOccupancyFieldProvenances_Occupancy").OnTable("rmsoccupancyfieldprovenances").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
			}
			if (!Schema.Table("rmsoccupancyownerships").Exists())
			{
				Create.Table("rmsoccupancyownerships")
					.WithColumn("rmsoccupancyownershipid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("inventoriedon").AsDateTime2().Nullable()
					.WithColumn("switchedon").AsDateTime2().Nullable()
					.WithColumn("switchedbyuserid").AsString(128).Nullable()
					.WithColumn("reason").AsString(1000).Nullable()
					.WithColumn("candidatecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("linkedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("rejectedcount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("UX_RmsOccupancyOwnerships_Department").OnTable("rmsoccupancyownerships").OnColumn("departmentid").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("rmscodesets").Exists())
			{
				Create.Table("rmscodesets")
					.WithColumn("rmscodesetid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("edition").AsString(100).Nullable()
					.WithColumn("jurisdiction").AsString(200).Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsCodeSets_Department").OnTable("rmscodesets").OnColumn("departmentid").Ascending().OnColumn("isactive").Ascending();
			}
			if (!Schema.Table("rmscodesections").Exists())
			{
				Create.Table("rmscodesections")
					.WithColumn("rmscodesectionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmscodesetid").AsString(36).NotNullable()
					.WithColumn("sectionnumber").AsString(64).NotNullable()
					.WithColumn("title").AsString(300).Nullable()
					.WithColumn("text").AsCustom("text").Nullable()
					.WithColumn("defaultseverity").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("defaultcorrectiondays").AsInt32().NotNullable().WithDefaultValue(30)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsCodeSections_CodeSet").OnTable("rmscodesections").OnColumn("departmentid").Ascending().OnColumn("rmscodesetid").Ascending();
			}
			if (!Schema.Table("rmsinspectionprograms").Exists())
			{
				Create.Table("rmsinspectionprograms")
					.WithColumn("rmsinspectionprogramid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("occupancytypescsv").AsString(500).Nullable()
					.WithColumn("frequencymonths").AsInt32().NotNullable().WithDefaultValue(12)
					.WithColumn("rmscodesetid").AsString(36).Nullable()
					.WithColumn("checklistjson").AsCustom("text").Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsInspectionPrograms_Department").OnTable("rmsinspectionprograms").OnColumn("departmentid").Ascending().OnColumn("isactive").Ascending();
			}
			if (!Schema.Table("rmsinspections").Exists())
			{
				Create.Table("rmsinspections")
					.WithColumn("rmsinspectionid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable()
					.WithColumn("rmsinspectionprogramid").AsString(36).Nullable()
					.WithColumn("inspectionnumber").AsString(32).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("result").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("scheduledon").AsDateTime2().Nullable()
					.WithColumn("startedon").AsDateTime2().Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("inspectoruserid").AsString(128).Nullable()
					.WithColumn("itemsjson").AsCustom("text").Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("signaturename").AsCustom("text").Nullable()
					.WithColumn("signedon").AsDateTime2().Nullable()
					.WithColumn("parentinspectionid").AsString(36).Nullable()
					.WithColumn("noticeissuedon").AsDateTime2().Nullable()
					.WithColumn("noticereference").AsString(100).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsInspections_Department_State").OnTable("rmsinspections").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("scheduledon").Ascending();
				Create.Index("IX_RmsInspections_Occupancy").OnTable("rmsinspections").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending().OnColumn("completedon").Ascending();
				Create.Index("IX_RmsInspections_Program").OnTable("rmsinspections").OnColumn("departmentid").Ascending().OnColumn("rmsinspectionprogramid").Ascending().OnColumn("state").Ascending();
			}
			if (!Schema.Table("rmsviolations").Exists())
			{
				Create.Table("rmsviolations")
					.WithColumn("rmsviolationid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinspectionid").AsString(36).NotNullable()
					.WithColumn("rmsoccupancyid").AsString(36).NotNullable()
					.WithColumn("rmscodesetid").AsString(36).Nullable()
					.WithColumn("rmscodesectionid").AsString(36).Nullable()
					.WithColumn("checklistitemkey").AsString(100).Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("severity").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("correctiveaction").AsCustom("text").Nullable()
					.WithColumn("dueon").AsDateTime2().Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("correctedon").AsDateTime2().Nullable()
					.WithColumn("verifiedon").AsDateTime2().Nullable()
					.WithColumn("verifiedbyuserid").AsString(128).Nullable()
					.WithColumn("reinspectionid").AsString(36).Nullable()
					.WithColumn("overdueemittedon").AsDateTime2().Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsViolations_Department_State_Due").OnTable("rmsviolations").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("dueon").Ascending();
				Create.Index("IX_RmsViolations_Inspection").OnTable("rmsviolations").OnColumn("departmentid").Ascending().OnColumn("rmsinspectionid").Ascending();
				Create.Index("IX_RmsViolations_Occupancy").OnTable("rmsviolations").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending().OnColumn("state").Ascending();
			}
			if (!Schema.Table("rmshydrants").Exists())
			{
				Create.Table("rmshydrants")
					.WithColumn("rmshydrantid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("hydrantnumber").AsString(64).NotNullable()
					.WithColumn("type").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("latitude").AsDecimal(12, 8).NotNullable()
					.WithColumn("longitude").AsDecimal(12, 8).NotNullable()
					.WithColumn("addresstext").AsString(500).Nullable()
					.WithColumn("ownerkind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("ownername").AsString(200).Nullable()
					.WithColumn("mainsizeinches").AsDecimal(6,2).Nullable()
					.WithColumn("staticpressurepsi").AsInt32().Nullable()
					.WithColumn("residualpressurepsi").AsInt32().Nullable()
					.WithColumn("flowgpm").AsInt32().Nullable()
					.WithColumn("flowclass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("inservice").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("outofservicereason").AsString(500).Nullable()
					.WithColumn("outofservicesince").AsDateTime2().Nullable()
					.WithColumn("lasttestedon").AsDateTime2().Nullable()
					.WithColumn("lastmaintainedon").AsDateTime2().Nullable()
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("poiid").AsInt32().Nullable()
					.WithColumn("source").AsString(32).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsHydrants_Department_Service").OnTable("rmshydrants").OnColumn("departmentid").Ascending().OnColumn("inservice").Ascending().OnColumn("deletedon").Ascending();
				Create.Index("IX_RmsHydrants_Department_Position").OnTable("rmshydrants").OnColumn("departmentid").Ascending().OnColumn("latitude").Ascending().OnColumn("longitude").Ascending();
			}
			if (!Schema.Table("rmshydrantflowtests").Exists())
			{
				Create.Table("rmshydrantflowtests")
					.WithColumn("rmshydrantflowtestid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmshydrantid").AsString(36).NotNullable()
					.WithColumn("testedon").AsDateTime2().NotNullable()
					.WithColumn("testedbyuserid").AsString(128).Nullable()
					.WithColumn("staticpressurepsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("residualpressurepsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("pitotpressurepsi").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("outletdiameterinches").AsDecimal(6,2).NotNullable().WithDefaultValue(0)
					.WithColumn("coefficient").AsDecimal(6,2).NotNullable().WithDefaultValue(0)
					.WithColumn("flowgpm").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("flowclass").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Create.Index("IX_RmsHydrantFlowTests_Hydrant").OnTable("rmshydrantflowtests").OnColumn("departmentid").Ascending().OnColumn("rmshydrantid").Ascending().OnColumn("testedon").Descending();
			}
			if (!Schema.Table("rmshydrantmaintenances").Exists())
			{
				Create.Table("rmshydrantmaintenances")
					.WithColumn("rmshydrantmaintenanceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmshydrantid").AsString(36).NotNullable()
					.WithColumn("performedon").AsDateTime2().NotNullable()
					.WithColumn("performedbyuserid").AsString(128).Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("notes").AsCustom("text").Nullable()
					.WithColumn("returnedtoservice").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("createdon").AsDateTime2().NotNullable();

				Create.Index("IX_RmsHydrantMaintenances_Hydrant").OnTable("rmshydrantmaintenances").OnColumn("departmentid").Ascending().OnColumn("rmshydrantid").Ascending().OnColumn("performedon").Descending();
			}
			if (!Schema.Table("rmspermittypes").Exists())
			{
				Create.Table("rmspermittypes")
					.WithColumn("rmspermittypeid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("name").AsString(200).NotNullable()
					.WithColumn("code").AsString(32).Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("defaultvaliditydays").AsInt32().NotNullable().WithDefaultValue(365)
					.WithColumn("requiresplanreview").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("feeamount").AsDecimal(18, 2).Nullable()
					.WithColumn("conditionstemplate").AsCustom("text").Nullable()
					.WithColumn("isactive").AsBoolean().NotNullable().WithDefaultValue(true)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsPermitTypes_Department").OnTable("rmspermittypes").OnColumn("departmentid").Ascending().OnColumn("isactive").Ascending();
			}
			if (!Schema.Table("rmspermits").Exists())
			{
				Create.Table("rmspermits")
					.WithColumn("rmspermitid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmspermittypeid").AsString(36).NotNullable()
					.WithColumn("rmsoccupancyid").AsString(36).Nullable()
					.WithColumn("permitnumber").AsString(32).Nullable()
					.WithColumn("applicantcontactid").AsString(128).Nullable()
					.WithColumn("applicantname").AsCustom("text").Nullable()
					.WithColumn("applicantphone").AsCustom("text").Nullable()
					.WithColumn("applicantemail").AsCustom("text").Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("appliedon").AsDateTime2().NotNullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("reviewedbyuserid").AsString(128).Nullable()
					.WithColumn("issuedon").AsDateTime2().Nullable()
					.WithColumn("issuedbyuserid").AsString(128).Nullable()
					.WithColumn("effectiveon").AsDateTime2().Nullable()
					.WithColumn("expireson").AsDateTime2().Nullable()
					.WithColumn("conditions").AsCustom("text").Nullable()
					.WithColumn("reviewnotes").AsCustom("text").Nullable()
					.WithColumn("feeamount").AsDecimal(18, 2).Nullable()
					.WithColumn("feepaidon").AsDateTime2().Nullable()
					.WithColumn("invoicereference").AsString(100).Nullable()
					.WithColumn("decisionreason").AsString(1000).Nullable()
					.WithColumn("expiringemittedon").AsDateTime2().Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsPermits_Department_State_Expires").OnTable("rmspermits").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("expireson").Ascending();
				Create.Index("IX_RmsPermits_Occupancy").OnTable("rmspermits").OnColumn("departmentid").Ascending().OnColumn("rmsoccupancyid").Ascending();
			}
			if (!Schema.Table("rmsplanreviews").Exists())
			{
				Create.Table("rmsplanreviews")
					.WithColumn("rmsplanreviewid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmspermitid").AsString(36).NotNullable()
					.WithColumn("cyclenumber").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("submittedon").AsDateTime2().NotNullable()
					.WithColumn("revieweruserid").AsString(128).Nullable()
					.WithColumn("reviewedon").AsDateTime2().Nullable()
					.WithColumn("outcome").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("comments").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsPlanReviews_Permit").OnTable("rmsplanreviews").OnColumn("departmentid").Ascending().OnColumn("rmspermitid").Ascending().OnColumn("cyclenumber").Ascending();
			}
			if (!Schema.Table("rmscrractivities").Exists())
			{
				Create.Table("rmscrractivities")
					.WithColumn("rmscrractivityid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("title").AsString(250).NotNullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).Nullable()
					.WithColumn("locationtext").AsString(500).Nullable()
					.WithColumn("latitude").AsDecimal(12, 8).Nullable()
					.WithColumn("longitude").AsDecimal(12, 8).Nullable()
					.WithColumn("audiencecount").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("smokealarmsinstalled").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("hoursspent").AsDecimal(8,2).NotNullable().WithDefaultValue(0)
					.WithColumn("staffuseridscsv").AsCustom("text").Nullable()
					.WithColumn("outcome").AsCustom("text").Nullable()
					.WithColumn("nerissecondaryjson").AsCustom("text").Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("createdbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsCrrActivities_Department_Occurred").OnTable("rmscrractivities").OnColumn("departmentid").Ascending().OnColumn("occurredon").Ascending();
			}
			if (!Schema.Table("rmspreventionattachments").Exists())
			{
				Create.Table("rmspreventionattachments")
					.WithColumn("rmspreventionattachmentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("parentkind").AsInt32().NotNullable()
					.WithColumn("parentid").AsString(36).NotNullable()
					.WithColumn("filename").AsCustom("text").Nullable()
					.WithColumn("contenttype").AsString(200).Nullable()
					.WithColumn("bytesize").AsInt64().NotNullable().WithDefaultValue(0L)
					.WithColumn("checksum").AsString(128).Nullable()
					.WithColumn("data").AsCustom("bytea").Nullable()
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("uploadedbyuserid").AsString(128).Nullable()
					.WithColumn("uploadedon").AsDateTime2().NotNullable()
					.WithColumn("scanstate").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("metadatastripped").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("classification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsPreventionAttachments_Parent").OnTable("rmspreventionattachments").OnColumn("departmentid").Ascending().OnColumn("parentkind").Ascending().OnColumn("parentid").Ascending();
			}
			if (!Schema.Table("rmspreventionsequences").Exists())
			{
				Create.Table("rmspreventionsequences")
					.WithColumn("rmspreventionsequenceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("kind").AsString(16).NotNullable()
					.WithColumn("year").AsInt32().NotNullable()
					.WithColumn("lastvalue").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("modifiedon").AsDateTime2().NotNullable();

				Create.Index("UX_RmsPreventionSequences_Key").OnTable("rmspreventionsequences").OnColumn("departmentid").Ascending().OnColumn("kind").Ascending().OnColumn("year").Ascending().WithOptions().Unique();
			}
			if (!Schema.Table("rmsinvestigationcases").Exists())
			{
				Create.Table("rmsinvestigationcases")
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("casenumber").AsString(32).Nullable()
					.WithColumn("title").AsString(250).NotNullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("openedon").AsDateTime2().NotNullable()
					.WithColumn("openedbyuserid").AsString(128).Nullable()
					.WithColumn("leadinvestigatoruserid").AsString(128).Nullable()
					.WithColumn("rmsoccupancyid").AsString(36).Nullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("incidentsummary").AsCustom("text").Nullable()
					.WithColumn("causeclassification").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("causedetail").AsCustom("text").Nullable()
					.WithColumn("origindescription").AsCustom("text").Nullable()
					.WithColumn("findings").AsCustom("text").Nullable()
					.WithColumn("findingsauthoruserid").AsString(128).Nullable()
					.WithColumn("findingsrecordedon").AsDateTime2().Nullable()
					.WithColumn("findingsapprovedon").AsDateTime2().Nullable()
					.WithColumn("findingsapprovedbyuserid").AsString(128).Nullable()
					.WithColumn("recommendsincidentamendment").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("closedon").AsDateTime2().Nullable()
					.WithColumn("closedbyuserid").AsString(128).Nullable()
					.WithColumn("closurereason").AsCustom("text").Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationCases_Department_State").OnTable("rmsinvestigationcases").OnColumn("departmentid").Ascending().OnColumn("state").Ascending().OnColumn("openedon").Ascending();
			}
			if (!Schema.Table("rmsinvestigationcaseincidents").Exists())
			{
				Create.Table("rmsinvestigationcaseincidents")
					.WithColumn("rmsinvestigationcaseincidentid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable()
					.WithColumn("recordid").AsString(36).NotNullable()
					.WithColumn("pinnedrevisionid").AsString(36).Nullable()
					.WithColumn("recordnumber").AsString(64).Nullable()
					.WithColumn("linkedon").AsDateTime2().NotNullable()
					.WithColumn("linkedbyuserid").AsString(128).Nullable();

				Create.Index("IX_RmsInvestigationCaseIncidents_Case").OnTable("rmsinvestigationcaseincidents").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationcaseid").Ascending();
				Create.Index("IX_RmsInvestigationCaseIncidents_Record").OnTable("rmsinvestigationcaseincidents").OnColumn("departmentid").Ascending().OnColumn("recordid").Ascending();
			}
			if (!Schema.Table("rmsinvestigationcasemembers").Exists())
			{
				Create.Table("rmsinvestigationcasemembers")
					.WithColumn("rmsinvestigationcasememberid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable()
					.WithColumn("userid").AsString(128).NotNullable()
					.WithColumn("role").AsInt32().NotNullable().WithDefaultValue(2)
					.WithColumn("addedon").AsDateTime2().NotNullable()
					.WithColumn("addedbyuserid").AsString(128).Nullable()
					.WithColumn("removedon").AsDateTime2().Nullable()
					.WithColumn("removedbyuserid").AsString(128).Nullable();

				Create.Index("IX_RmsInvestigationCaseMembers_Case").OnTable("rmsinvestigationcasemembers").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationcaseid").Ascending();
				Create.Index("IX_RmsInvestigationCaseMembers_User").OnTable("rmsinvestigationcasemembers").OnColumn("departmentid").Ascending().OnColumn("userid").Ascending().OnColumn("removedon").Ascending();
			}
			if (!Schema.Table("rmsinvestigationnotes").Exists())
			{
				Create.Table("rmsinvestigationnotes")
					.WithColumn("rmsinvestigationnoteid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("occurredon").AsDateTime2().NotNullable()
					.WithColumn("authoruserid").AsString(128).Nullable()
					.WithColumn("subject").AsCustom("text").Nullable()
					.WithColumn("body").AsCustom("text").Nullable()
					.WithColumn("islocked").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationNotes_Case").OnTable("rmsinvestigationnotes").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationcaseid").Ascending().OnColumn("occurredon").Ascending();
			}
			if (!Schema.Table("rmsinvestigationevidence").Exists())
			{
				Create.Table("rmsinvestigationevidence")
					.WithColumn("rmsinvestigationevidenceid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable()
					.WithColumn("evidencenumber").AsString(32).Nullable()
					.WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("description").AsCustom("text").Nullable()
					.WithColumn("collectedon").AsDateTime2().NotNullable()
					.WithColumn("collectedbyuserid").AsString(128).Nullable()
					.WithColumn("collectedfrom").AsCustom("text").Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("currentcustodianuserid").AsString(128).Nullable()
					.WithColumn("currentcustodianexternal").AsCustom("text").Nullable()
					.WithColumn("storagelocation").AsString(250).Nullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L)
					.WithColumn("deletedon").AsDateTime2().Nullable();

				Create.Index("IX_RmsInvestigationEvidence_Case").OnTable("rmsinvestigationevidence").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationcaseid").Ascending();
			}
			if (!Schema.Table("rmsinvestigationcustody").Exists())
			{
				Create.Table("rmsinvestigationcustody")
					.WithColumn("rmsinvestigationcustodyid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationevidenceid").AsString(36).NotNullable()
					.WithColumn("sequence").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("transferredon").AsDateTime2().NotNullable()
					.WithColumn("fromuserid").AsString(128).Nullable()
					.WithColumn("fromexternal").AsCustom("text").Nullable()
					.WithColumn("touserid").AsString(128).Nullable()
					.WithColumn("toexternal").AsCustom("text").Nullable()
					.WithColumn("reason").AsCustom("text").Nullable()
					.WithColumn("resultingstate").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("recordedbyuserid").AsString(128).Nullable()
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0);

				Create.Index("IX_RmsInvestigationCustody_Evidence").OnTable("rmsinvestigationcustody").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationevidenceid").Ascending().OnColumn("sequence").Ascending();
			}
			if (!Schema.Table("rmsinvestigationreferrals").Exists())
			{
				Create.Table("rmsinvestigationreferrals")
					.WithColumn("rmsinvestigationreferralid").AsString(36).NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("protectionid").AsString(36).Nullable()
					.WithColumn("rmsinvestigationcaseid").AsString(36).NotNullable()
					.WithColumn("agency").AsString(250).NotNullable()
					.WithColumn("referredon").AsDateTime2().NotNullable()
					.WithColumn("referredbyuserid").AsString(128).Nullable()
					.WithColumn("reason").AsCustom("text").Nullable()
					.WithColumn("referencenumber").AsString(100).Nullable()
					.WithColumn("state").AsInt32().NotNullable().WithDefaultValue(1)
					.WithColumn("isprotected").AsBoolean().NotNullable().WithDefaultValue(false)
					.WithColumn("protectedcatalogversion").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable()
					.WithColumn("rowversion").AsInt64().NotNullable().WithDefaultValue(1L);

				Create.Index("IX_RmsInvestigationReferrals_Case").OnTable("rmsinvestigationreferrals").OnColumn("departmentid").Ascending().OnColumn("rmsinvestigationcaseid").Ascending();
			}

			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Prevention.Occupancy', 'Records Prevention - Occupancies', 'RMS-5 occupancy/property master and the Contacts pre-plan crosswalk. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Prevention.Occupancy');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Prevention.Inspections', 'Records Prevention - Inspections', 'RMS-5 inspection programs, code sets, inspections and violations. Requires Records.Prevention.Occupancy. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Prevention.Inspections');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Prevention.Hydrants', 'Records Prevention - Hydrants', 'RMS-5 hydrants and water sources with flow tests and the response-map layer. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Prevention.Hydrants');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Prevention.Permits', 'Records Prevention - Permits', 'RMS-5 permits, plan review and expiration. Requires Records.Prevention.Occupancy. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Prevention.Permits');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Prevention.Crr', 'Records Prevention - Community Risk Reduction', 'RMS-5 community risk reduction activity tracking. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Prevention.Crr');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.Investigations', 'Records Investigations', 'RMS-5 investigation cases with case-level authorization, evidence chain of custody and referrals. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.Investigations');");
			Execute.Sql("INSERT INTO featureflags (flagkey, name, description, category, isenabledglobally) SELECT 'Records.QualityReview', 'Records Quality Review', 'RMS-4 optional post-finalization quality review: rubric-scored sampling of finalized records. Requires Records.System. Seeded off.', 'Records', false WHERE NOT EXISTS (SELECT 1 FROM featureflags WHERE flagkey = 'Records.QualityReview');");
		}

		public override void Down()
		{
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Prevention.Occupancy" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Prevention.Inspections" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Prevention.Hydrants" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Prevention.Permits" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Prevention.Crr" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.Investigations" });
			Delete.FromTable("featureflags").Row(new { flagkey = "Records.QualityReview" });
			if (Schema.Table("rmsinvestigationreferrals").Exists())
				Delete.Table("rmsinvestigationreferrals");
			if (Schema.Table("rmsinvestigationcustody").Exists())
				Delete.Table("rmsinvestigationcustody");
			if (Schema.Table("rmsinvestigationevidence").Exists())
				Delete.Table("rmsinvestigationevidence");
			if (Schema.Table("rmsinvestigationnotes").Exists())
				Delete.Table("rmsinvestigationnotes");
			if (Schema.Table("rmsinvestigationcasemembers").Exists())
				Delete.Table("rmsinvestigationcasemembers");
			if (Schema.Table("rmsinvestigationcaseincidents").Exists())
				Delete.Table("rmsinvestigationcaseincidents");
			if (Schema.Table("rmsinvestigationcases").Exists())
				Delete.Table("rmsinvestigationcases");
			if (Schema.Table("rmspreventionsequences").Exists())
				Delete.Table("rmspreventionsequences");
			if (Schema.Table("rmspreventionattachments").Exists())
				Delete.Table("rmspreventionattachments");
			if (Schema.Table("rmscrractivities").Exists())
				Delete.Table("rmscrractivities");
			if (Schema.Table("rmsplanreviews").Exists())
				Delete.Table("rmsplanreviews");
			if (Schema.Table("rmspermits").Exists())
				Delete.Table("rmspermits");
			if (Schema.Table("rmspermittypes").Exists())
				Delete.Table("rmspermittypes");
			if (Schema.Table("rmshydrantmaintenances").Exists())
				Delete.Table("rmshydrantmaintenances");
			if (Schema.Table("rmshydrantflowtests").Exists())
				Delete.Table("rmshydrantflowtests");
			if (Schema.Table("rmshydrants").Exists())
				Delete.Table("rmshydrants");
			if (Schema.Table("rmsviolations").Exists())
				Delete.Table("rmsviolations");
			if (Schema.Table("rmsinspections").Exists())
				Delete.Table("rmsinspections");
			if (Schema.Table("rmsinspectionprograms").Exists())
				Delete.Table("rmsinspectionprograms");
			if (Schema.Table("rmscodesections").Exists())
				Delete.Table("rmscodesections");
			if (Schema.Table("rmscodesets").Exists())
				Delete.Table("rmscodesets");
			if (Schema.Table("rmsoccupancyownerships").Exists())
				Delete.Table("rmsoccupancyownerships");
			if (Schema.Table("rmsoccupancyfieldprovenances").Exists())
				Delete.Table("rmsoccupancyfieldprovenances");
			if (Schema.Table("rmsoccupancycrosswalks").Exists())
				Delete.Table("rmsoccupancycrosswalks");
			if (Schema.Table("rmsoccupancyhazards").Exists())
				Delete.Table("rmsoccupancyhazards");
			if (Schema.Table("rmsoccupancycontactlinks").Exists())
				Delete.Table("rmsoccupancycontactlinks");
			if (Schema.Table("rmsoccupancies").Exists())
				Delete.Table("rmsoccupancies");
		}
	}
}
