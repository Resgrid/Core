using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Per-definition record cardinality (RMS plan section 5.2.1, registry M0182). Two parts: the rule a definition
	/// version declares, and the key that enforces it.
	///
	/// The filtered unique index is the enforcement, not the service check in front of it. Two engine companies on
	/// the same fire produce one official incident report and two company-level records, and which of those a
	/// definition is must be a property of the definition rather than a global rule. A Record that is voided or
	/// cancelled has its key cleared, so an abandoned Record never blocks the one that replaces it.
	/// </summary>
	[Migration(182)]
	public class M0182_AddRmsRecordCardinality : Migration
	{
		public override void Up()
		{
			if (Schema.Table("RmsRecordDefinitionVersions").Exists() && !Schema.Table("RmsRecordDefinitionVersions").Column("Cardinality").Exists())
				Alter.Table("RmsRecordDefinitionVersions").AddColumn("Cardinality").AsInt32().NotNullable().WithDefaultValue(2);

			if (Schema.Table("RmsOperationalRecords").Exists() && !Schema.Table("RmsOperationalRecords").Column("CardinalityKey").Exists())
			{
				Alter.Table("RmsOperationalRecords").AddColumn("CardinalityKey").AsString(400).Nullable();
				Execute.Sql("CREATE UNIQUE NONCLUSTERED INDEX UX_RmsOperationalRecords_Department_CardinalityKey ON RmsOperationalRecords (DepartmentId, CardinalityKey) WHERE CardinalityKey IS NOT NULL;");
			}
		}

		public override void Down()
		{
			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Index("UX_RmsOperationalRecords_Department_CardinalityKey").Exists())
				Delete.Index("UX_RmsOperationalRecords_Department_CardinalityKey").OnTable("RmsOperationalRecords");

			if (Schema.Table("RmsOperationalRecords").Exists() && Schema.Table("RmsOperationalRecords").Column("CardinalityKey").Exists())
				Delete.Column("CardinalityKey").FromTable("RmsOperationalRecords");

			if (Schema.Table("RmsRecordDefinitionVersions").Exists() && Schema.Table("RmsRecordDefinitionVersions").Column("Cardinality").Exists())
				Delete.Column("Cardinality").FromTable("RmsRecordDefinitionVersions");
		}
	}
}
