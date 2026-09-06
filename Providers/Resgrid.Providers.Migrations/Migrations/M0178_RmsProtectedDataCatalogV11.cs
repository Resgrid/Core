using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// RMS Advanced Data Protection catalog v11 (RMS plan section 5.9.4 (e), registry M0178): typed values of
	/// department definitions. A row whose field is Protected-classified is flagged ProtectionRequired at write time;
	/// the ADP seam seals its typed columns into the existing ProtectedEnvelope column and the migration sweep only
	/// visits flagged rows. Guarded for safe retry.
	/// </summary>
	[Migration(178)]
	public class M0178_RmsProtectedDataCatalogV11 : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("RmsRecordValues").Exists())
				return;

			if (!Schema.Table("RmsRecordValues").Column("ProtectionRequired").Exists())
				Alter.Table("RmsRecordValues").AddColumn("ProtectionRequired").AsBoolean().NotNullable().WithDefaultValue(false);

			if (!Schema.Table("RmsRecordValues").Index("IX_RmsRecordValues_Department_ProtectionRequired").Exists())
				Execute.Sql("CREATE NONCLUSTERED INDEX IX_RmsRecordValues_Department_ProtectionRequired ON RmsRecordValues (DepartmentId, RmsRecordValueId) WHERE ProtectionRequired = 1;");
		}

		public override void Down()
		{
			if (!Schema.Table("RmsRecordValues").Exists())
				return;
			if (Schema.Table("RmsRecordValues").Index("IX_RmsRecordValues_Department_ProtectionRequired").Exists())
				Delete.Index("IX_RmsRecordValues_Department_ProtectionRequired").OnTable("RmsRecordValues");
			if (Schema.Table("RmsRecordValues").Column("ProtectionRequired").Exists())
				Delete.Column("ProtectionRequired").FromTable("RmsRecordValues");
		}
	}
}
