using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Department call numbering: one counter row per call-number sequence (a department and the text its pattern writes
	/// around {SEQ} for one period, e.g. "26-#"). New calls take their sequence from the row in one atomic statement, so
	/// two calls logged together no longer share a number. The row also holds the department's raised starting point.
	/// A scope with no row yet is seeded from the highest number already on the department's calls, so the legacy
	/// "26-153" numbers carry on unbroken. No FK.
	/// </summary>
	[Migration(264)]
	public class M0264_AddCallNumberSequencesPg : Migration
	{
		private const string Table = "callnumbersequences";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("scopekey").AsString(100).NotNullable()
					.WithColumn("lastsequence").AsInt32().NotNullable()
					.WithColumn("floorsequence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("floorseton").AsDateTime2().Nullable()
					.WithColumn("floorsetbyuserid").AsString(128).Nullable()
					.WithColumn("modifiedon").AsDateTime2().NotNullable();

				Create.PrimaryKey("pk_callnumbersequences").OnTable(Table).Columns("departmentid", "scopekey");
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
