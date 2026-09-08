using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Per-definition record cardinality (RMS plan section 5.2.1, registry M0182). Two parts: the rule a definition
	/// version declares, and the key that enforces it.
	///
	/// The partial unique index is the enforcement, not the service check in front of it. Two engine companies on
	/// the same fire produce one official incident report and two company-level records, and which of those a
	/// definition is must be a property of the definition rather than a global rule. A Record that is voided or
	/// cancelled has its key cleared, so an abandoned Record never blocks the one that replaces it.
	/// </summary>
	[Migration(182)]
	public class M0182_AddRmsRecordCardinalityPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("rmsrecorddefinitionversions").Exists() && !Schema.Table("rmsrecorddefinitionversions").Column("cardinality").Exists())
				Alter.Table("rmsrecorddefinitionversions").AddColumn("cardinality").AsInt32().NotNullable().WithDefaultValue(2);

			if (Schema.Table("rmsoperationalrecords").Exists() && !Schema.Table("rmsoperationalrecords").Column("cardinalitykey").Exists())
			{
				Alter.Table("rmsoperationalrecords").AddColumn("cardinalitykey").AsString(400).Nullable();
				Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_rmsoperationalrecords_department_cardinalitykey ON rmsoperationalrecords (departmentid, cardinalitykey) WHERE cardinalitykey IS NOT NULL;");
			}
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ux_rmsoperationalrecords_department_cardinalitykey;");

			if (Schema.Table("rmsoperationalrecords").Exists() && Schema.Table("rmsoperationalrecords").Column("cardinalitykey").Exists())
				Delete.Column("cardinalitykey").FromTable("rmsoperationalrecords");

			if (Schema.Table("rmsrecorddefinitionversions").Exists() && Schema.Table("rmsrecorddefinitionversions").Column("cardinality").Exists())
				Delete.Column("cardinality").FromTable("rmsrecorddefinitionversions");
		}
	}
}
