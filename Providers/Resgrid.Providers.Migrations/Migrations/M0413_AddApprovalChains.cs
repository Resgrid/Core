using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// e-Signature S4: approval chains with thresholds. ApprovalChains (one active chain per department and subject kind),
	/// ApprovalChainSteps (ordered, parallel by equal order, amount threshold, approver kind, escalation), ApprovalRequests (one
	/// submission bound to the subject's content hash and carried by an S3 multi-signer request), ApprovalRequestSteps (worker 97's
	/// announcement and escalation state per step) and ApprovalDelegates. No foreign keys: UnlinkedDepartmentCleanup removes the rows.
	/// Guarded for safe retry; Down drops the tables, children first.
	/// </summary>
	[Migration(413)]
	public class M0413_AddApprovalChains : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.ApprovalChains', 'U') IS NULL
CREATE TABLE [dbo].[ApprovalChains] (
	[ApprovalChainId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApprovalChains] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SubjectKind] int NOT NULL,
	[Name] nvarchar(200) NOT NULL,
	[IsActive] bit NOT NULL CONSTRAINT [DF_ApprovalChains_IsActive] DEFAULT 1,
	[AllowSelfApproval] bit NOT NULL CONSTRAINT [DF_ApprovalChains_AllowSelfApproval] DEFAULT 0,
	[ExpiresInDays] int NOT NULL CONSTRAINT [DF_ApprovalChains_ExpiresInDays] DEFAULT 14,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL,
	[UpdatedByUserId] nvarchar(128) NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalChains_Kind' AND object_id = OBJECT_ID('dbo.ApprovalChains')) " +
				"CREATE INDEX [IX_ApprovalChains_Kind] ON [dbo].[ApprovalChains] ([DepartmentId], [SubjectKind], [IsActive]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ApprovalChainSteps', 'U') IS NULL
CREATE TABLE [dbo].[ApprovalChainSteps] (
	[ApprovalChainStepId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApprovalChainSteps] PRIMARY KEY,
	[ApprovalChainId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[StepOrder] int NOT NULL,
	[ApproverKind] int NOT NULL,
	[PersonnelRoleId] int NULL,
	[UserId] nvarchar(128) NULL,
	[DepartmentGroupId] int NULL,
	[CertificationTypeId] int NULL,
	[MinimumAmount] decimal(18,2) NULL,
	[Label] nvarchar(200) NULL,
	[EscalateAfterHours] int NULL,
	[EscalateTo] int NOT NULL CONSTRAINT [DF_ApprovalChainSteps_EscalateTo] DEFAULT 0,
	[EscalateToRoleId] int NULL,
	[EscalateToUserId] nvarchar(128) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalChainSteps_Chain' AND object_id = OBJECT_ID('dbo.ApprovalChainSteps')) " +
				"CREATE INDEX [IX_ApprovalChainSteps_Chain] ON [dbo].[ApprovalChainSteps] ([DepartmentId], [ApprovalChainId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ApprovalRequests', 'U') IS NULL
CREATE TABLE [dbo].[ApprovalRequests] (
	[ApprovalRequestId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApprovalRequests] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[SubjectKind] int NOT NULL,
	[SubjectId] nvarchar(128) NOT NULL,
	[ApprovalChainId] int NOT NULL,
	[SignatureRequestId] int NULL,
	[Title] nvarchar(256) NULL,
	[Amount] decimal(18,2) NULL,
	[Currency] nvarchar(8) NULL,
	[ContentSha256] nvarchar(64) NOT NULL,
	[RevisionId] nvarchar(128) NULL,
	[Status] int NOT NULL,
	[SubmittedByUserId] nvarchar(128) NULL,
	[SubmittedOn] datetime2 NOT NULL,
	[DecidedOn] datetime2 NULL,
	[DecidedByUserId] nvarchar(128) NULL,
	[RejectReason] nvarchar(1000) NULL,
	[SupersedesApprovalRequestId] int NULL,
	[SealedDocumentId] int NULL,
	[AppliedOn] datetime2 NULL,
	[ApplyError] nvarchar(500) NULL,
	[UpdatedOn] datetime2 NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequests_Subject' AND object_id = OBJECT_ID('dbo.ApprovalRequests')) " +
				"CREATE INDEX [IX_ApprovalRequests_Subject] ON [dbo].[ApprovalRequests] ([DepartmentId], [SubjectKind], [SubjectId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequests_Status' AND object_id = OBJECT_ID('dbo.ApprovalRequests')) " +
				"CREATE INDEX [IX_ApprovalRequests_Status] ON [dbo].[ApprovalRequests] ([Status], [DepartmentId]) INCLUDE ([SignatureRequestId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequests_Signature' AND object_id = OBJECT_ID('dbo.ApprovalRequests')) " +
				"CREATE INDEX [IX_ApprovalRequests_Signature] ON [dbo].[ApprovalRequests] ([SignatureRequestId]) WHERE [SignatureRequestId] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ApprovalRequestSteps', 'U') IS NULL
CREATE TABLE [dbo].[ApprovalRequestSteps] (
	[ApprovalRequestStepId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApprovalRequestSteps] PRIMARY KEY,
	[ApprovalRequestId] int NOT NULL,
	[DepartmentId] int NOT NULL,
	[SignatureRequestSignerId] int NOT NULL,
	[ApprovalChainStepId] int NOT NULL,
	[StepOrder] int NOT NULL,
	[Label] nvarchar(200) NULL,
	[TurnNotifiedOn] datetime2 NULL,
	[EscalateDueOn] datetime2 NULL,
	[EscalatedOn] datetime2 NULL,
	[DelegatedToUserIds] nvarchar(400) NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequestSteps_Request' AND object_id = OBJECT_ID('dbo.ApprovalRequestSteps')) " +
				"CREATE INDEX [IX_ApprovalRequestSteps_Request] ON [dbo].[ApprovalRequestSteps] ([DepartmentId], [ApprovalRequestId]);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequestSteps_Unannounced' AND object_id = OBJECT_ID('dbo.ApprovalRequestSteps')) " +
				"CREATE INDEX [IX_ApprovalRequestSteps_Unannounced] ON [dbo].[ApprovalRequestSteps] ([ApprovalRequestStepId]) INCLUDE ([SignatureRequestSignerId], [ApprovalRequestId]) WHERE [TurnNotifiedOn] IS NULL;");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalRequestSteps_Escalation' AND object_id = OBJECT_ID('dbo.ApprovalRequestSteps')) " +
				"CREATE INDEX [IX_ApprovalRequestSteps_Escalation] ON [dbo].[ApprovalRequestSteps] ([EscalateDueOn]) WHERE [EscalateDueOn] IS NOT NULL AND [EscalatedOn] IS NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.ApprovalDelegates', 'U') IS NULL
CREATE TABLE [dbo].[ApprovalDelegates] (
	[ApprovalDelegateId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ApprovalDelegates] PRIMARY KEY,
	[DepartmentId] int NOT NULL,
	[UserId] nvarchar(128) NOT NULL,
	[DelegateUserId] nvarchar(128) NOT NULL,
	[StartsOn] datetime2 NULL,
	[EndsOn] datetime2 NULL,
	[CreatedByUserId] nvarchar(128) NULL,
	[CreatedOn] datetime2 NOT NULL
);");
			Execute.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApprovalDelegates_Department' AND object_id = OBJECT_ID('dbo.ApprovalDelegates')) " +
				"CREATE INDEX [IX_ApprovalDelegates_Department] ON [dbo].[ApprovalDelegates] ([DepartmentId], [UserId]);");
		}

		public override void Down()
		{
			foreach (var table in new[] { "ApprovalDelegates", "ApprovalRequestSteps", "ApprovalRequests", "ApprovalChainSteps", "ApprovalChains" })
				Execute.Sql($"IF OBJECT_ID('dbo.{table}', 'U') IS NOT NULL DROP TABLE [dbo].[{table}];");
		}
	}
}
