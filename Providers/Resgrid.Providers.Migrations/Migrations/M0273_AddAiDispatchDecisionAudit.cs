using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// AI dispatch decision-model provenance (Cloudflare Clef / Clef-Flash on SGLang, ai-inference-vm runbook): when the decision
	/// model classified a call, its prompt version, model name, revision and runtime digest, input tokens and latency go on the
	/// same metadata-only AiDispatchAudits row as the generative model's. Still no message, prompt, answer or probability is
	/// stored. Next physical number at authoring (disk was at M0272). Guarded for safe retry.
	/// </summary>
	[Migration(273)]
	public class M0273_AddAiDispatchDecisionAudit : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionPromptVersion') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionPromptVersion] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionModelName') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionModelName] nvarchar(128) NULL;");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionModelRevision') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionModelRevision] nvarchar(64) NULL;");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionRuntimeDigest') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionRuntimeDigest] nvarchar(80) NULL;");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionInputTokens') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionInputTokens] int NOT NULL CONSTRAINT [DF_AiDispatchAudits_DecisionInputTokens] DEFAULT 0;");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionLatencyMs') IS NULL ALTER TABLE [AiDispatchAudits] ADD [DecisionLatencyMs] int NOT NULL CONSTRAINT [DF_AiDispatchAudits_DecisionLatencyMs] DEFAULT 0;");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('DF_AiDispatchAudits_DecisionLatencyMs', 'D') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP CONSTRAINT [DF_AiDispatchAudits_DecisionLatencyMs];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionLatencyMs') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionLatencyMs];");
			Execute.Sql("IF OBJECT_ID('DF_AiDispatchAudits_DecisionInputTokens', 'D') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP CONSTRAINT [DF_AiDispatchAudits_DecisionInputTokens];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionInputTokens') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionInputTokens];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionRuntimeDigest') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionRuntimeDigest];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionModelRevision') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionModelRevision];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionModelName') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionModelName];");
			Execute.Sql("IF COL_LENGTH('AiDispatchAudits', 'DecisionPromptVersion') IS NOT NULL ALTER TABLE [AiDispatchAudits] DROP COLUMN [DecisionPromptVersion];");
		}
	}
}
