using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P4 dispatch decision engine:
	/// <list type="bullet">
	/// <item>CallProtocols: the case recorded on a call — Determinant (in force), OverrideReason, CaseJson (path in words, outcome,
	/// override, source, caller texts) — and an index on (DispatchProtocolId, Determinant) for determinant reports.</item>
	/// <item>DispatchProtocolTriggers and RunCardTriggers: the shared trigger engine's criteria — GeofenceGroupId (inside a station or
	/// group geofence), DaysOfWeek / StartMinute / EndMinute (department-local schedule), Keywords (nature or notes), PoiTypeId (the POI
	/// type at the location) and CallSources (intake classification). No foreign keys: a deleted group or POI type simply never
	/// matches again.</item>
	/// </list>
	/// The decision graph itself is part of a version's ContentJson (hashed and approved with the version), so no graph table is needed.
	/// Guarded for safe retry (a trigger table that does not exist is skipped).
	/// </summary>
	[Migration(399)]
	public class M0399_AddProtocolDecisionEngine : Migration
	{
		private static readonly (string Name, string Type)[] TriggerColumns =
		{
			("GeofenceGroupId", "int NULL"),
			("DaysOfWeek", "int NULL"),
			("StartMinute", "int NULL"),
			("EndMinute", "int NULL"),
			("Keywords", "nvarchar(500) NULL"),
			("PoiTypeId", "int NULL"),
			("CallSources", "varchar(50) NULL")
		};

		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'Determinant') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [Determinant] nvarchar(32) NULL;");
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'OverrideReason') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [OverrideReason] nvarchar(500) NULL;");
			Execute.Sql("IF COL_LENGTH('dbo.CallProtocols', 'CaseJson') IS NULL ALTER TABLE [dbo].[CallProtocols] ADD [CaseJson] nvarchar(max) NULL;");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallProtocols_ProtocolDeterminant' AND object_id = OBJECT_ID('dbo.CallProtocols'))
	EXEC sp_executesql N'CREATE NONCLUSTERED INDEX [IX_CallProtocols_ProtocolDeterminant] ON [dbo].[CallProtocols] ([DispatchProtocolId], [Determinant]) WHERE [Determinant] IS NOT NULL';");

			foreach (var table in new[] { "DispatchProtocolTriggers", "RunCardTriggers" })
			{
				foreach (var (name, type) in TriggerColumns)
					Execute.Sql($"IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL AND COL_LENGTH('dbo.{table}', '{name}') IS NULL ALTER TABLE [dbo].[{table}] ADD [{name}] {type};");
			}
		}

		public override void Down()
		{
			Execute.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CallProtocols_ProtocolDeterminant' AND object_id = OBJECT_ID('dbo.CallProtocols')) DROP INDEX [IX_CallProtocols_ProtocolDeterminant] ON [dbo].[CallProtocols];");

			foreach (var column in new[] { "CaseJson", "OverrideReason", "Determinant" })
				Execute.Sql($"IF COL_LENGTH('dbo.CallProtocols', '{column}') IS NOT NULL ALTER TABLE [dbo].[CallProtocols] DROP COLUMN [{column}];");

			foreach (var table in new[] { "DispatchProtocolTriggers", "RunCardTriggers" })
			{
				foreach (var (name, _) in TriggerColumns)
					Execute.Sql($"IF COL_LENGTH('dbo.{table}', '{name}') IS NOT NULL ALTER TABLE [dbo].[{table}] DROP COLUMN [{name}];");
			}
		}
	}
}
