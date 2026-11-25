using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Protocols P4 AI suggestion (Enhanced AI, the intake desk seam): ProtocolAiSuggestions keeps each suggestion the model made for a
	/// call (the protocol, its version, the prefilled case answers, confidence and rationale) until a dispatcher confirms it, so a call
	/// recorded with Source = AI points at a suggestion that really existed. No foreign keys (department deletion clears it in
	/// ProtocolDepartmentCleanup on both databases); indexed by call. Guarded for safe retry.
	/// </summary>
	[Migration(400)]
	public class M0400_AddProtocolAiSuggestions : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ProtocolAiSuggestions', 'U') IS NULL
CREATE TABLE [dbo].[ProtocolAiSuggestions] (
	[ProtocolAiSuggestionId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProtocolAiSuggestions] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[CallId] int NOT NULL,
	[DispatchProtocolId] int NOT NULL,
	[ProtocolVersionId] int NULL,
	[AnswersJson] nvarchar(max) NULL,
	[Confidence] decimal(5,4) NOT NULL CONSTRAINT [DF_ProtocolAiSuggestions_Confidence] DEFAULT (0),
	[Rationale] nvarchar(1000) NULL,
	[InputKind] int NOT NULL CONSTRAINT [DF_ProtocolAiSuggestions_InputKind] DEFAULT (0),
	[ModelRevision] nvarchar(100) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[RequestedByUserId] nvarchar(128) NULL,
	[ConfirmedOn] datetime2 NULL,
	[ConfirmedByUserId] nvarchar(128) NULL
);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProtocolAiSuggestions_Call' AND object_id = OBJECT_ID('dbo.ProtocolAiSuggestions'))
	CREATE NONCLUSTERED INDEX [IX_ProtocolAiSuggestions_Call] ON [dbo].[ProtocolAiSuggestions] ([DepartmentId], [CallId], [CreatedOn]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.ProtocolAiSuggestions', 'U') IS NOT NULL DROP TABLE [dbo].[ProtocolAiSuggestions];");
		}
	}
}
