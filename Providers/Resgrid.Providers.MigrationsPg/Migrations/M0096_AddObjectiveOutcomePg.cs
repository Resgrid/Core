using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Close-out outcome (Successful/Partial/Unsuccessful) and an optional completion note on tactical
	/// objectives, recorded when an objective is completed.
	/// </summary>
	[Migration(96)]
	public class M0096_AddObjectiveOutcomePg : Migration
	{
		public override void Up()
		{
			if (!Schema.Table("tacticalobjectives").Exists())
				return;

			if (!Schema.Table("tacticalobjectives").Column("outcome").Exists())
				Alter.Table("tacticalobjectives").AddColumn("outcome").AsInt32().NotNullable().WithDefaultValue(0);

			if (!Schema.Table("tacticalobjectives").Column("completionnote").Exists())
				Alter.Table("tacticalobjectives").AddColumn("completionnote").AsCustom("citext").Nullable();
		}

		public override void Down()
		{
			if (!Schema.Table("tacticalobjectives").Exists())
				return;

			foreach (var column in new[] { "outcome", "completionnote" })
			{
				if (Schema.Table("tacticalobjectives").Column(column).Exists())
					Delete.Column(column).FromTable("tacticalobjectives");
			}
		}
	}
}
