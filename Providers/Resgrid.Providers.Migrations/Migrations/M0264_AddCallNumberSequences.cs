using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Department call numbering: one counter row per call-number sequence (a department and the text its pattern writes
	/// around {SEQ} for one period, e.g. "26-#"). New calls take their sequence from the row in one atomic statement, so
	/// two calls logged together no longer share a number. The row also holds the department's raised starting point.
	/// A scope with no row yet is seeded from the highest number already on the department's calls, so the legacy
	/// "26-153" numbers carry on unbroken. No FK; the SQL Server department purge deletes the rows explicitly.
	/// </summary>
	[Migration(264)]
	public class M0264_AddCallNumberSequences : Migration
	{
		private const string Table = "CallNumberSequences";

		public override void Up()
		{
			if (!Schema.Table(Table).Exists())
			{
				Create.Table(Table)
					.WithColumn("DepartmentId").AsInt32().NotNullable()
					.WithColumn("ScopeKey").AsString(100).NotNullable()
					.WithColumn("LastSequence").AsInt32().NotNullable()
					.WithColumn("FloorSequence").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("FloorSetOn").AsDateTime2().Nullable()
					.WithColumn("FloorSetByUserId").AsString(128).Nullable()
					.WithColumn("ModifiedOn").AsDateTime2().NotNullable();

				Create.PrimaryKey("PK_CallNumberSequences").OnTable(Table).Columns("DepartmentId", "ScopeKey");
			}
		}

		public override void Down()
		{
			if (Schema.Table(Table).Exists())
				Delete.Table(Table);
		}
	}
}
