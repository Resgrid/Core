using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Lets a status option name the options the apps offer next while it is the current status ("Departed" offers
	/// only "On Scene"), as a comma-separated list of CustomStateDetailIds in the same set. Null means no restriction,
	/// which is every row written before this migration. A display hint only: the server still accepts any status.
	/// </summary>
	[Migration(263)]
	public class M0263_AddCustomStateDetailNextStates : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('CustomStateDetails', 'NextStateDetailIds') IS NULL ALTER TABLE [CustomStateDetails] ADD [NextStateDetailIds] nvarchar(1000) NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('CustomStateDetails', 'NextStateDetailIds') IS NOT NULL ALTER TABLE [CustomStateDetails] DROP COLUMN [NextStateDetailIds];");
		}
	}
}
