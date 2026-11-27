using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P0 (D4, D9, D16, D17, D22, D23):
	/// <list type="bullet">
	/// <item>DispatchProtocols.Code grows from 4 to 16 characters (determinant codes, SOG numbers). Description becomes
	/// nvarchar(max): its 500 characters are now counted on the text, not the markup, so the stored markup can be longer.</item>
	/// <item>DispatchProtocolTriggers.CallTypeId: triggers match the call type by id, so a rename no longer breaks them. Filled
	/// from the stored name where the department has a type of that name. The English "No Type" label the old form stored
	/// becomes a null name ("no type" is a null id with no name).</item>
	/// <item>Trigger windows were saved as the department's wall-clock time and compared with UTC; they are now stored in UTC.
	/// Existing windows are converted once, in the same batch that adds CallTypeId (so a retry never converts twice), for
	/// departments whose time zone SQL Server knows.</item>
	/// <item>CallProtocols records the questionnaire (AnswersJson), how the protocol came onto the call (Source), the hash of
	/// the text as dispatched (ContentHash), who attached it and when, and a soft removal with its reason.</item>
	/// </list>
	/// Guarded for safe retry.
	/// </summary>
	[Migration(336)]
	public class M0336_ProtocolFoundation : Migration
	{
		public override void Up()
		{
			// COL_LENGTH is in bytes: nvarchar(16) is 32, nvarchar(max) is -1.
			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'Code') IS NOT NULL AND COL_LENGTH('DispatchProtocols', 'Code') BETWEEN 1 AND 31 ALTER TABLE [DispatchProtocols] ALTER COLUMN [Code] nvarchar(16) NOT NULL;");
			Execute.Sql("IF COL_LENGTH('DispatchProtocols', 'Description') IS NOT NULL AND COL_LENGTH('DispatchProtocols', 'Description') <> -1 ALTER TABLE [DispatchProtocols] ALTER COLUMN [Description] nvarchar(max) NULL;");

			Execute.Sql(@"
IF COL_LENGTH('DispatchProtocolTriggers', 'CallTypeId') IS NULL
BEGIN
	UPDATE t
		SET t.[StartsOn] = CASE WHEN t.[StartsOn] IS NULL THEN NULL ELSE CONVERT(datetime, (t.[StartsOn] AT TIME ZONE d.[TimeZone]) AT TIME ZONE 'UTC') END,
			t.[EndsOn] = CASE WHEN t.[EndsOn] IS NULL THEN NULL ELSE CONVERT(datetime, (t.[EndsOn] AT TIME ZONE d.[TimeZone]) AT TIME ZONE 'UTC') END
		FROM [dbo].[DispatchProtocolTriggers] t
		INNER JOIN [dbo].[DispatchProtocols] p ON p.[DispatchProtocolId] = t.[DispatchProtocolId]
		INNER JOIN [dbo].[Departments] d ON d.[DepartmentId] = p.[DepartmentId]
		WHERE (t.[StartsOn] IS NOT NULL OR t.[EndsOn] IS NOT NULL)
			AND d.[TimeZone] IN (SELECT [name] FROM sys.time_zone_info);

	ALTER TABLE [dbo].[DispatchProtocolTriggers] ADD [CallTypeId] int NULL;
END");

			Execute.Sql("UPDATE [dbo].[DispatchProtocolTriggers] SET [CallType] = NULL WHERE [CallTypeId] IS NULL AND LTRIM(RTRIM([CallType])) = N'No Type';");

			Execute.Sql(@"
UPDATE t
	SET t.[CallTypeId] = ct.[CallTypeId]
	FROM [dbo].[DispatchProtocolTriggers] t
	INNER JOIN [dbo].[DispatchProtocols] p ON p.[DispatchProtocolId] = t.[DispatchProtocolId]
	CROSS APPLY (SELECT TOP 1 c.[CallTypeId] FROM [dbo].[CallTypes] c
		WHERE c.[DepartmentId] = p.[DepartmentId] AND LTRIM(RTRIM(c.[Type])) = LTRIM(RTRIM(t.[CallType]))
		ORDER BY c.[CallTypeId]) ct
	WHERE t.[CallTypeId] IS NULL AND t.[CallType] IS NOT NULL;");

			Execute.Sql("IF COL_LENGTH('CallProtocols', 'AnswersJson') IS NULL ALTER TABLE [CallProtocols] ADD [AnswersJson] nvarchar(max) NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'Source') IS NULL ALTER TABLE [CallProtocols] ADD [Source] int NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'ContentHash') IS NULL ALTER TABLE [CallProtocols] ADD [ContentHash] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'AttachedOn') IS NULL ALTER TABLE [CallProtocols] ADD [AttachedOn] datetime2 NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'AttachedByUserId') IS NULL ALTER TABLE [CallProtocols] ADD [AttachedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'RemovedOn') IS NULL ALTER TABLE [CallProtocols] ADD [RemovedOn] datetime2 NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'RemovedByUserId') IS NULL ALTER TABLE [CallProtocols] ADD [RemovedByUserId] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('CallProtocols', 'RemovedReason') IS NULL ALTER TABLE [CallProtocols] ADD [RemovedReason] nvarchar(500) NULL;");
		}

		public override void Down()
		{
			foreach (var column in new[] { "RemovedReason", "RemovedByUserId", "RemovedOn", "AttachedByUserId", "AttachedOn", "ContentHash", "Source", "AnswersJson" })
				Execute.Sql($"IF COL_LENGTH('CallProtocols', '{column}') IS NOT NULL ALTER TABLE [CallProtocols] DROP COLUMN [{column}];");

			// The windows stay in UTC: converting back would need each department's offset at each window's date, and the code
			// before M0336 compared them with UTC anyway.
			Execute.Sql("IF COL_LENGTH('DispatchProtocolTriggers', 'CallTypeId') IS NOT NULL ALTER TABLE [DispatchProtocolTriggers] DROP COLUMN [CallTypeId];");

			// Only narrowed back when every stored value still fits; otherwise the wider column is kept rather than failing.
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [DispatchProtocols] WHERE LEN([Description]) > 500) ALTER TABLE [DispatchProtocols] ALTER COLUMN [Description] nvarchar(500) NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM [DispatchProtocols] WHERE LEN([Code]) > 4) ALTER TABLE [DispatchProtocols] ALTER COLUMN [Code] nvarchar(4) NOT NULL;");
		}
	}
}
