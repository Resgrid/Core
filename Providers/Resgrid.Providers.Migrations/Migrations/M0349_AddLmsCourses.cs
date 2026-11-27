using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T2 (LMS courses on the shared content core, plan §12.1/§12.3): LmsCourses and LmsCourseVersions (the content core's
	/// version shape: status, number, ContentJson/ContentHtml, ContentHash, change summary, plus hours, category, CE, certificate and
	/// the certification a completion issues), LmsCourseVersionApprovals (L2 signature per step), LmsCourseAudiences, LmsCourseOwners
	/// (per-course rights), LmsCourseModules, LmsMediaAssets (hosted video on RustFS, worker 87), LmsQuestionBanks, LmsQuestions and
	/// LmsLessons. System content (the Academy, A1) has DepartmentId 0 and a unique SystemKey. LMS tables reference baseline tables
	/// (a lesson's ProtocolVersionId has no key, so P1's retention purge never depends on an LMS row), never the reverse (§17.3); LmsDepartmentCleanup removes them children first. Guarded for safe retry.
	/// </summary>
	[Migration(349)]
	public class M0349_AddLmsCourses : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourses', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourses] (
		[LmsCourseId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[PublisherKind] int NOT NULL CONSTRAINT [DF_LmsCourses_PublisherKind] DEFAULT (0),
		[SystemKey] nvarchar(100) NULL,
		[ManifestVersion] int NOT NULL CONSTRAINT [DF_LmsCourses_ManifestVersion] DEFAULT (0),
		[Title] nvarchar(250) NOT NULL,
		[Code] nvarchar(50) NULL,
		[Summary] nvarchar(2000) NULL,
		[Tags] nvarchar(500) NULL,
		[Status] int NOT NULL CONSTRAINT [DF_LmsCourses_Status] DEFAULT (0),
		[CurrentVersionId] int NULL,
		[IsSelfEnrollable] bit NOT NULL CONSTRAINT [DF_LmsCourses_IsSelfEnrollable] DEFAULT (0),
		[VisibilityKeys] nvarchar(500) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsCourses_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsCourses_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsCourses] PRIMARY KEY CLUSTERED ([LmsCourseId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourses_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCourses'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourses_DepartmentId] ON [dbo].[LmsCourses] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCourses_SystemKey' AND object_id = OBJECT_ID('dbo.LmsCourses'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCourses_SystemKey] ON [dbo].[LmsCourses] ([SystemKey]) WHERE [SystemKey] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourseVersions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourseVersions] (
		[LmsCourseVersionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[Major] int NOT NULL CONSTRAINT [DF_LmsCourseVersions_Major] DEFAULT (0),
		[Minor] int NOT NULL CONSTRAINT [DF_LmsCourseVersions_Minor] DEFAULT (0),
		[Status] int NOT NULL,
		[Title] nvarchar(250) NULL,
		[ContentJson] nvarchar(max) NULL,
		[ContentHtml] nvarchar(max) NULL,
		[ContentHash] nvarchar(64) NULL,
		[ChangeSummary] nvarchar(2000) NULL,
		[Locale] nvarchar(16) NULL,
		[Hours] decimal(9,2) NOT NULL CONSTRAINT [DF_LmsCourseVersions_Hours] DEFAULT (0),
		[CategoryId] int NULL,
		[CeProvider] nvarchar(200) NULL,
		[CeCourseNumber] nvarchar(100) NULL,
		[CertificateTemplateId] int NULL,
		[IssuesCertificationTypeId] int NULL,
		[MajorChangePolicy] int NOT NULL CONSTRAINT [DF_LmsCourseVersions_MajorChangePolicy] DEFAULT (0),
		[ReassignDueDays] int NULL,
		[ManifestVersion] int NOT NULL CONSTRAINT [DF_LmsCourseVersions_ManifestVersion] DEFAULT (0),
		[EffectiveOn] datetime2 NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		[UpdatedByUserId] nvarchar(128) NULL,
		[SubmittedOn] datetime2 NULL,
		[SubmittedByUserId] nvarchar(128) NULL,
		[ApprovedOn] datetime2 NULL,
		[ApprovedByUserId] nvarchar(128) NULL,
		[PublishedOn] datetime2 NULL,
		[PublishedByUserId] nvarchar(128) NULL,
		[SupersededOn] datetime2 NULL,
		[RetiredOn] datetime2 NULL,
		[RowVersion] bigint NOT NULL CONSTRAINT [DF_LmsCourseVersions_RowVersion] DEFAULT (0),
		CONSTRAINT [PK_LmsCourseVersions] PRIMARY KEY CLUSTERED ([LmsCourseVersionId] ASC),
		CONSTRAINT [FK_LmsCourseVersions_LmsCourseId] FOREIGN KEY ([LmsCourseId]) REFERENCES [dbo].[LmsCourses] ([LmsCourseId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseVersions_Course_Status' AND object_id = OBJECT_ID('dbo.LmsCourseVersions'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseVersions_Course_Status] ON [dbo].[LmsCourseVersions] ([LmsCourseId], [Status]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseVersions_DepartmentId_Status' AND object_id = OBJECT_ID('dbo.LmsCourseVersions'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseVersions_DepartmentId_Status] ON [dbo].[LmsCourseVersions] ([DepartmentId], [Status]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourseVersionApprovals', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourseVersionApprovals] (
		[LmsCourseVersionApprovalId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[Round] int NOT NULL CONSTRAINT [DF_LmsCourseVersionApprovals_Round] DEFAULT (1),
		[StepOrder] int NOT NULL,
		[ApproverUserId] nvarchar(128) NULL,
		[Decision] int NOT NULL CONSTRAINT [DF_LmsCourseVersionApprovals_Decision] DEFAULT (0),
		[DecidedByUserId] nvarchar(128) NULL,
		[Comment] nvarchar(2000) NULL,
		[DecidedOn] datetime2 NULL,
		[SignatureRecordId] int NULL,
		[CreatedOn] datetime2 NOT NULL,
		CONSTRAINT [PK_LmsCourseVersionApprovals] PRIMARY KEY CLUSTERED ([LmsCourseVersionApprovalId] ASC),
		CONSTRAINT [FK_LmsCourseVersionApprovals_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseVersionApprovals_Version' AND object_id = OBJECT_ID('dbo.LmsCourseVersionApprovals'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseVersionApprovals_Version] ON [dbo].[LmsCourseVersionApprovals] ([LmsCourseVersionId], [Round]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseVersionApprovals_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCourseVersionApprovals'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseVersionApprovals_DepartmentId] ON [dbo].[LmsCourseVersionApprovals] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourseAudiences', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourseAudiences] (
		[LmsCourseAudienceId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[Kind] int NOT NULL,
		[RefId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCourseAudiences] PRIMARY KEY CLUSTERED ([LmsCourseAudienceId] ASC),
		CONSTRAINT [FK_LmsCourseAudiences_LmsCourseId] FOREIGN KEY ([LmsCourseId]) REFERENCES [dbo].[LmsCourses] ([LmsCourseId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseAudiences_Course' AND object_id = OBJECT_ID('dbo.LmsCourseAudiences'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseAudiences_Course] ON [dbo].[LmsCourseAudiences] ([LmsCourseId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseAudiences_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCourseAudiences'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseAudiences_DepartmentId] ON [dbo].[LmsCourseAudiences] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourseOwners', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourseOwners] (
		[LmsCourseOwnerId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseId] int NOT NULL,
		[UserId] nvarchar(128) NOT NULL,
		[CanEdit] bit NOT NULL CONSTRAINT [DF_LmsCourseOwners_CanEdit] DEFAULT (0),
		[CanAssign] bit NOT NULL CONSTRAINT [DF_LmsCourseOwners_CanAssign] DEFAULT (0),
		[CanReport] bit NOT NULL CONSTRAINT [DF_LmsCourseOwners_CanReport] DEFAULT (0),
		[AddedOn] datetime2 NOT NULL,
		[AddedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsCourseOwners] PRIMARY KEY CLUSTERED ([LmsCourseOwnerId] ASC),
		CONSTRAINT [FK_LmsCourseOwners_LmsCourseId] FOREIGN KEY ([LmsCourseId]) REFERENCES [dbo].[LmsCourses] ([LmsCourseId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsCourseOwners_Course_User' AND object_id = OBJECT_ID('dbo.LmsCourseOwners'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsCourseOwners_Course_User] ON [dbo].[LmsCourseOwners] ([LmsCourseId], [UserId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseOwners_Department_User' AND object_id = OBJECT_ID('dbo.LmsCourseOwners'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseOwners_Department_User] ON [dbo].[LmsCourseOwners] ([DepartmentId], [UserId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsCourseModules', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsCourseModules] (
		[LmsCourseModuleId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[Title] nvarchar(250) NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsCourseModules_SortOrder] DEFAULT (0),
		CONSTRAINT [PK_LmsCourseModules] PRIMARY KEY CLUSTERED ([LmsCourseModuleId] ASC),
		CONSTRAINT [FK_LmsCourseModules_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseModules_Version' AND object_id = OBJECT_ID('dbo.LmsCourseModules'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseModules_Version] ON [dbo].[LmsCourseModules] ([LmsCourseVersionId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsCourseModules_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsCourseModules'))
	CREATE NONCLUSTERED INDEX [IX_LmsCourseModules_DepartmentId] ON [dbo].[LmsCourseModules] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsMediaAssets', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsMediaAssets] (
		[LmsMediaAssetId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsMediaAssets_Kind] DEFAULT (0),
		[FileName] nvarchar(260) NULL,
		[ContentType] nvarchar(128) NULL,
		[SourceKey] nvarchar(500) NULL,
		[SourceBytes] bigint NOT NULL CONSTRAINT [DF_LmsMediaAssets_SourceBytes] DEFAULT (0),
		[Status] int NOT NULL CONSTRAINT [DF_LmsMediaAssets_Status] DEFAULT (0),
		[HlsPrefix] nvarchar(500) NULL,
		[Mp4Key] nvarchar(500) NULL,
		[OutputBytes] bigint NOT NULL CONSTRAINT [DF_LmsMediaAssets_OutputBytes] DEFAULT (0),
		[DurationSeconds] int NOT NULL CONSTRAINT [DF_LmsMediaAssets_DurationSeconds] DEFAULT (0),
		[Attempts] int NOT NULL CONSTRAINT [DF_LmsMediaAssets_Attempts] DEFAULT (0),
		[LastError] nvarchar(2000) NULL,
		[ClaimedOn] datetime2 NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[ReadyOn] datetime2 NULL,
		[ArchivedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsMediaAssets] PRIMARY KEY CLUSTERED ([LmsMediaAssetId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsMediaAssets_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsMediaAssets'))
	CREATE NONCLUSTERED INDEX [IX_LmsMediaAssets_DepartmentId] ON [dbo].[LmsMediaAssets] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsMediaAssets_Status_CreatedOn' AND object_id = OBJECT_ID('dbo.LmsMediaAssets'))
	CREATE NONCLUSTERED INDEX [IX_LmsMediaAssets_Status_CreatedOn] ON [dbo].[LmsMediaAssets] ([Status], [CreatedOn]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsQuestionBanks', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsQuestionBanks] (
		[LmsQuestionBankId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Name] nvarchar(250) NOT NULL,
		[Description] nvarchar(2000) NULL,
		[IsShared] bit NOT NULL CONSTRAINT [DF_LmsQuestionBanks_IsShared] DEFAULT (1),
		[OwnerUserId] nvarchar(128) NULL,
		[Tags] nvarchar(500) NULL,
		[SystemKey] nvarchar(100) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsQuestionBanks_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsQuestionBanks] PRIMARY KEY CLUSTERED ([LmsQuestionBankId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsQuestionBanks_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsQuestionBanks'))
	CREATE NONCLUSTERED INDEX [IX_LmsQuestionBanks_DepartmentId] ON [dbo].[LmsQuestionBanks] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsQuestionBanks_SystemKey' AND object_id = OBJECT_ID('dbo.LmsQuestionBanks'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsQuestionBanks_SystemKey] ON [dbo].[LmsQuestionBanks] ([SystemKey]) WHERE [SystemKey] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsQuestions', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsQuestions] (
		[LmsQuestionId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsQuestionBankId] int NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsQuestions_Kind] DEFAULT (0),
		[Text] nvarchar(max) NULL,
		[ImageUrl] nvarchar(1000) NULL,
		[OptionsJson] nvarchar(max) NULL,
		[Explanation] nvarchar(max) NULL,
		[Tags] nvarchar(500) NULL,
		[Points] decimal(9,2) NOT NULL CONSTRAINT [DF_LmsQuestions_Points] DEFAULT (1),
		[TranslationsJson] nvarchar(max) NULL,
		[IsRetired] bit NOT NULL CONSTRAINT [DF_LmsQuestions_IsRetired] DEFAULT (0),
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsQuestions_SortOrder] DEFAULT (0),
		[SystemKey] nvarchar(100) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsQuestions] PRIMARY KEY CLUSTERED ([LmsQuestionId] ASC),
		CONSTRAINT [FK_LmsQuestions_LmsQuestionBankId] FOREIGN KEY ([LmsQuestionBankId]) REFERENCES [dbo].[LmsQuestionBanks] ([LmsQuestionBankId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsQuestions_Bank' AND object_id = OBJECT_ID('dbo.LmsQuestions'))
	CREATE NONCLUSTERED INDEX [IX_LmsQuestions_Bank] ON [dbo].[LmsQuestions] ([LmsQuestionBankId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsQuestions_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsQuestions'))
	CREATE NONCLUSTERED INDEX [IX_LmsQuestions_DepartmentId] ON [dbo].[LmsQuestions] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LmsQuestions_Bank_SystemKey' AND object_id = OBJECT_ID('dbo.LmsQuestions'))
	CREATE UNIQUE NONCLUSTERED INDEX [UX_LmsQuestions_Bank_SystemKey] ON [dbo].[LmsQuestions] ([LmsQuestionBankId], [SystemKey]) WHERE [SystemKey] IS NOT NULL;");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsLessons', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsLessons] (
		[LmsLessonId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsCourseVersionId] int NOT NULL,
		[LmsCourseModuleId] int NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsLessons_Kind] DEFAULT (0),
		[Title] nvarchar(250) NULL,
		[SortOrder] int NOT NULL CONSTRAINT [DF_LmsLessons_SortOrder] DEFAULT (0),
		[IsRequired] bit NOT NULL CONSTRAINT [DF_LmsLessons_IsRequired] DEFAULT (1),
		[ContentJson] nvarchar(max) NULL,
		[ContentHtml] nvarchar(max) NULL,
		[VideoSource] int NOT NULL CONSTRAINT [DF_LmsLessons_VideoSource] DEFAULT (0),
		[VideoUrl] nvarchar(1000) NULL,
		[LmsMediaAssetId] int NULL,
		[CaptionTracksJson] nvarchar(max) NULL,
		[TranscriptText] nvarchar(max) NULL,
		[DocumentUrl] nvarchar(1000) NULL,
		[DocumentName] nvarchar(250) NULL,
		[ProtocolVersionId] int NULL,
		[MinimumSeatSeconds] int NOT NULL CONSTRAINT [DF_LmsLessons_MinimumSeatSeconds] DEFAULT (0),
		[RequiredWatchPercent] int NOT NULL CONSTRAINT [DF_LmsLessons_RequiredWatchPercent] DEFAULT (0),
		[AvailableOffline] bit NOT NULL CONSTRAINT [DF_LmsLessons_AvailableOffline] DEFAULT (0),
		[OfflineMaxBytes] bigint NULL,
		[OfflineExpiryDays] int NULL,
		[FreeResponsePrompt] nvarchar(2000) NULL,
		[FileRequestPrompt] nvarchar(2000) NULL,
		[LmsQuestionBankId] int NULL,
		[DrawCount] int NOT NULL CONSTRAINT [DF_LmsLessons_DrawCount] DEFAULT (0),
		[DrawTags] nvarchar(500) NULL,
		[PassMark] int NOT NULL CONSTRAINT [DF_LmsLessons_PassMark] DEFAULT (80),
		[AttemptLimit] int NOT NULL CONSTRAINT [DF_LmsLessons_AttemptLimit] DEFAULT (0),
		[RetakeCooldownHours] int NOT NULL CONSTRAINT [DF_LmsLessons_RetakeCooldownHours] DEFAULT (0),
		[TimeLimitMinutes] int NOT NULL CONSTRAINT [DF_LmsLessons_TimeLimitMinutes] DEFAULT (0),
		[ShowAnswersPolicy] int NOT NULL CONSTRAINT [DF_LmsLessons_ShowAnswersPolicy] DEFAULT (2),
		[IsPreTest] bit NOT NULL CONSTRAINT [DF_LmsLessons_IsPreTest] DEFAULT (0),
		[RandomizeAnswers] bit NOT NULL CONSTRAINT [DF_LmsLessons_RandomizeAnswers] DEFAULT (1),
		[RequireAttestation] bit NOT NULL CONSTRAINT [DF_LmsLessons_RequireAttestation] DEFAULT (0),
		[SurveyJson] nvarchar(max) NULL,
		CONSTRAINT [PK_LmsLessons] PRIMARY KEY CLUSTERED ([LmsLessonId] ASC),
		CONSTRAINT [FK_LmsLessons_LmsCourseVersionId] FOREIGN KEY ([LmsCourseVersionId]) REFERENCES [dbo].[LmsCourseVersions] ([LmsCourseVersionId]),
		CONSTRAINT [FK_LmsLessons_LmsCourseModuleId] FOREIGN KEY ([LmsCourseModuleId]) REFERENCES [dbo].[LmsCourseModules] ([LmsCourseModuleId]),
		CONSTRAINT [FK_LmsLessons_LmsMediaAssetId] FOREIGN KEY ([LmsMediaAssetId]) REFERENCES [dbo].[LmsMediaAssets] ([LmsMediaAssetId]),
		CONSTRAINT [FK_LmsLessons_LmsQuestionBankId] FOREIGN KEY ([LmsQuestionBankId]) REFERENCES [dbo].[LmsQuestionBanks] ([LmsQuestionBankId])
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_Version' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_Version] ON [dbo].[LmsLessons] ([LmsCourseVersionId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_Module' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_Module] ON [dbo].[LmsLessons] ([LmsCourseModuleId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_DepartmentId] ON [dbo].[LmsLessons] ([DepartmentId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_Media' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_Media] ON [dbo].[LmsLessons] ([LmsMediaAssetId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_Bank' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_Bank] ON [dbo].[LmsLessons] ([LmsQuestionBankId]);");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsLessons_ProtocolVersion' AND object_id = OBJECT_ID('dbo.LmsLessons'))
	CREATE NONCLUSTERED INDEX [IX_LmsLessons_ProtocolVersion] ON [dbo].[LmsLessons] ([ProtocolVersionId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsLessons', 'U') IS NOT NULL DROP TABLE [dbo].[LmsLessons];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsQuestions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsQuestions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsQuestionBanks', 'U') IS NOT NULL DROP TABLE [dbo].[LmsQuestionBanks];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsMediaAssets', 'U') IS NOT NULL DROP TABLE [dbo].[LmsMediaAssets];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourseModules', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourseModules];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourseOwners', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourseOwners];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourseAudiences', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourseAudiences];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourseVersionApprovals', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourseVersionApprovals];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourseVersions', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourseVersions];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsCourses', 'U') IS NOT NULL DROP TABLE [dbo].[LmsCourses];");
		}
	}
}
