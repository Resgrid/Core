using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// RMS Advanced Data Protection catalog v11 (RMS plan section 5.9.4 (e), registry M0178): typed values of
	/// department definitions. A row whose field is Protected-classified is flagged protectionrequired at write time;
	/// the ADP seam seals its typed columns into the existing protectedenvelope column and the migration sweep only
	/// visits flagged rows. Guarded for safe retry.
	/// </summary>
	[Migration(178)]
	public class M0178_RmsProtectedDataCatalogV11Pg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("rmsrecordvalues").Exists())
				return;

			if (!Schema.Table("rmsrecordvalues").Column("protectionrequired").Exists())
				Alter.Table("rmsrecordvalues").AddColumn("protectionrequired").AsBoolean().NotNullable().WithDefaultValue(false);

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_rmsrecordvalues_department_protectionrequired ON rmsrecordvalues (departmentid, rmsrecordvalueid) WHERE protectionrequired = TRUE;");
		}

		public override void Down()
		{
			if (!Schema.Table("rmsrecordvalues").Exists())
				return;
			Execute.Sql("DROP INDEX IF EXISTS ix_rmsrecordvalues_department_protectionrequired;");
			if (Schema.Table("rmsrecordvalues").Column("protectionrequired").Exists())
				Delete.Column("protectionrequired").FromTable("rmsrecordvalues");
		}
	}
}
