using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Every human-readable number stored as text goes to citext, the PostgreSQL string convention, so numbers and the
	/// numbering keys behind them compare case-insensitively as they do under SQL Server's collation: the M0264 call-number
	/// sequences, the Records quality review, prevention and investigation numbers, and the incident, order, request,
	/// contract, registration and serial numbers on business operations rows, plus certification type codes (unique per
	/// department, matched case-insensitively by the service). Calls, Records cores, disclosures and the M0268 document
	/// numbers were citext from the start. Before the unique keys change type, values that differ only by case are made
	/// distinct the way SQL Server's collation always kept them: call-number scopes (a pattern whose letters were recased)
	/// fold into one counter keeping the highest sequence and floor, and a live certification code clashing with an older
	/// one gets the M0213 "-{id}" suffix. varchar and text are binary coercible to citext, so no row is rewritten; indexes
	/// on the columns are rebuilt. A missing table or column is skipped.
	/// </summary>
	[Migration(269)]
	public class M0269_ConvertNumberTextToCitextPg : Migration
	{
		/// <summary>Table, column and the type it had before this migration (Down restores it).</summary>
		public static readonly (string Table, string Column, string PreviousType)[] Columns =
		{
			("callnumbersequences", "scopekey", "character varying(100)"),
			("callnumbersequences", "floorsetbyuserid", "character varying(128)"),
			("rmspreventionsequences", "kind", "character varying(16)"),
			("rmsqualityreviews", "recordnumber", "character varying(64)"),
			("rmsoccupancies", "occupancynumber", "character varying(32)"),
			("rmscodesections", "sectionnumber", "character varying(64)"),
			("rmsinspections", "inspectionnumber", "character varying(32)"),
			("rmshydrants", "hydrantnumber", "character varying(64)"),
			("rmspermits", "permitnumber", "character varying(32)"),
			("rmsinvestigationcases", "casenumber", "character varying(32)"),
			("rmsinvestigationcaseincidents", "recordnumber", "character varying(64)"),
			("rmsinvestigationevidence", "evidencenumber", "character varying(32)"),
			("rmsinvestigationreferrals", "referencenumber", "character varying(100)"),
			("departmentbillingidentities", "taxregistrationnumber", "character varying(100)"),
			("departmentbillingidentities", "secondarytaxregistrationnumber", "character varying(100)"),
			("departmentbillingidentities", "workerscompaccountnumber", "character varying(100)"),
			("unitcertifications", "number", "text"),
			("servicecontracts", "contractnumber", "character varying(100)"),
			("departmentcompliancedocuments", "documentnumber", "text"),
			("bids", "incidentnumber", "character varying(100)"),
			("deployments", "incidentnumber", "character varying(100)"),
			("deployments", "servicerequestnumber", "character varying(100)"),
			("deployments", "resourceordernumber", "character varying(100)"),
			("deployments", "requestnumber", "character varying(100)"),
			("deploymenttimereports", "incidentnumber", "character varying(100)"),
			("deploymenttimereports", "resourceordernumber", "character varying(100)"),
			("deploymenttimereports", "requestnumber", "character varying(100)"),
			("caloesmarsresourceprofiles", "serialnumber", "text"),
			("workforceemployerprofiles", "sosnumber", "text"),
			("workforceaffiliatedentities", "sosnumber", "text"),
			("departmentcertificationtypes", "code", "character varying(50)"),
		};

		public override void Up()
		{
			Execute.Sql("CREATE EXTENSION IF NOT EXISTS citext;");

			if (Schema.Table("callnumbersequences").Column("scopekey").Exists())
			{
				// The survivor of each case-folded scope is the casing written last (the pattern in use); it takes the highest
				// sequence and the highest floor, with who raised that floor and when, then the other casings go.
				Execute.Sql(@"UPDATE callnumbersequences s
					SET lastsequence = f.lastsequence, floorsequence = f.floorsequence, floorseton = f.floorseton,
						floorsetbyuserid = f.floorsetbyuserid, modifiedon = f.modifiedon
					FROM (SELECT DISTINCT departmentid, FIRST_VALUE(scopekey) OVER latest AS keeper,
							FIRST_VALUE(floorseton) OVER raised AS floorseton, FIRST_VALUE(floorsetbyuserid) OVER raised AS floorsetbyuserid,
							MAX(lastsequence) OVER folded AS lastsequence, MAX(floorsequence) OVER folded AS floorsequence,
							MAX(modifiedon) OVER folded AS modifiedon, COUNT(*) OVER folded AS copies
						FROM callnumbersequences
						WINDOW folded AS (PARTITION BY departmentid, lower(scopekey)),
							latest AS (folded ORDER BY modifiedon DESC, scopekey COLLATE ""C""),
							raised AS (folded ORDER BY floorsequence DESC, floorseton DESC NULLS LAST)) f
					WHERE f.copies > 1 AND s.departmentid = f.departmentid AND s.scopekey = f.keeper;");
				Execute.Sql(@"DELETE FROM callnumbersequences s
					USING (SELECT departmentid, scopekey, ROW_NUMBER() OVER (PARTITION BY departmentid, lower(scopekey)
							ORDER BY modifiedon DESC, scopekey COLLATE ""C"") AS place
						FROM callnumbersequences) r
					WHERE r.place > 1 AND s.departmentid = r.departmentid AND s.scopekey = r.scopekey;");
			}

			// ux_departmentcertificationtypes_code covers live rows only; the oldest live spelling keeps its code.
			if (Schema.Table("departmentcertificationtypes").Column("code").Exists())
				Execute.Sql(@"UPDATE departmentcertificationtypes t SET code = LEFT(t.code, 38) || '-' || t.departmentcertificationtypeid::text
					FROM (SELECT departmentcertificationtypeid, ROW_NUMBER() OVER (PARTITION BY departmentid, lower(code)
							ORDER BY departmentcertificationtypeid) AS place
						FROM departmentcertificationtypes WHERE isdeleted = FALSE AND code IS NOT NULL) d
					WHERE d.place > 1 AND t.departmentcertificationtypeid = d.departmentcertificationtypeid;");

			foreach (var (table, column, _) in Columns)
				if (Schema.Table(table).Column(column).Exists())
					Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} TYPE citext;");
		}

		public override void Down()
		{
			// Folded call-number scopes stay folded.
			foreach (var (table, column, previousType) in Columns)
				if (Schema.Table(table).Column(column).Exists())
					Execute.Sql($"ALTER TABLE {table} ALTER COLUMN {column} TYPE {previousType};");
		}
	}
}
