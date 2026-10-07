using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Unified Search call history backfill. Rebuilds used to project only the last three calendar years of closed calls and
	/// retired every older call, so departments indexed before rebuilds walked every year are missing their older calls. The
	/// search worker now walks each department's calls newest first in small batches, projecting only the calls search does
	/// not hold, without changing the index generation (which would blank every department's results until its rebuild).
	/// CallBackfillCursor is the lowest CallId already walked (null: start from the newest); CallBackfillCompletedOn is set
	/// when the walk reaches the oldest call or a full rebuild has projected every call. Null on existing rows, so every
	/// activated department is backfilled once.
	/// </summary>
	[Migration(261)]
	public class M0261_AddSearchCallBackfill : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('SearchIndexStates', 'CallBackfillCursor') IS NULL ALTER TABLE [SearchIndexStates] ADD [CallBackfillCursor] int NULL;");
			Execute.Sql("IF COL_LENGTH('SearchIndexStates', 'CallBackfillCompletedOn') IS NULL ALTER TABLE [SearchIndexStates] ADD [CallBackfillCompletedOn] datetime2 NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('SearchIndexStates', 'CallBackfillCompletedOn') IS NOT NULL ALTER TABLE [SearchIndexStates] DROP COLUMN [CallBackfillCompletedOn];");
			Execute.Sql("IF COL_LENGTH('SearchIndexStates', 'CallBackfillCursor') IS NOT NULL ALTER TABLE [SearchIndexStates] DROP COLUMN [CallBackfillCursor];");
		}
	}
}
