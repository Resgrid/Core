using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Training T4 training time and cost, facility and prop booking (protocols-training-knowledge-plan §13 T4 item 6, R-C10; §13a Acadis row;
	/// LMS add-on): LmsClassCostLines (instructor, materials, outside fees, backfill overtime per session), LmsTrainingResources (the
	/// department's bookable facilities, props, apparatus and equipment, optionally a POI) and LmsClassResourceBookings (a resource held for a
	/// session's time). No foreign keys (§17.3). Existence-guarded for safe retry.
	/// </summary>
	[Migration(380)]
	public class M0380_AddLmsClassCostsAndResources : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassCostLines', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassCostLines] (
		[LmsClassCostLineId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsClassCostLines_Kind] DEFAULT (0),
		[Description] nvarchar(250) NULL,
		[Amount] decimal(14,2) NOT NULL CONSTRAINT [DF_LmsClassCostLines_Amount] DEFAULT (0),
		[Currency] nvarchar(3) NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsClassCostLines] PRIMARY KEY CLUSTERED ([LmsClassCostLineId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassCostLines_Session' AND object_id = OBJECT_ID('dbo.LmsClassCostLines'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassCostLines_Session] ON [dbo].[LmsClassCostLines] ([DepartmentId], [LmsClassSessionId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsTrainingResources', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsTrainingResources] (
		[LmsTrainingResourceId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[Name] nvarchar(150) NOT NULL,
		[Kind] int NOT NULL CONSTRAINT [DF_LmsTrainingResources_Kind] DEFAULT (0),
		[PoiId] int NULL,
		[Capacity] int NULL,
		[Notes] nvarchar(1000) NULL,
		[IsDeleted] bit NOT NULL CONSTRAINT [DF_LmsTrainingResources_IsDeleted] DEFAULT (0),
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		[UpdatedOn] datetime2 NULL,
		CONSTRAINT [PK_LmsTrainingResources] PRIMARY KEY CLUSTERED ([LmsTrainingResourceId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsTrainingResources_DepartmentId' AND object_id = OBJECT_ID('dbo.LmsTrainingResources'))
	CREATE NONCLUSTERED INDEX [IX_LmsTrainingResources_DepartmentId] ON [dbo].[LmsTrainingResources] ([DepartmentId]);");

			Execute.Sql(@"
IF OBJECT_ID('dbo.LmsClassResourceBookings', 'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LmsClassResourceBookings] (
		[LmsClassResourceBookingId] int IDENTITY(1,1) NOT NULL,
		[DepartmentId] int NOT NULL,
		[LmsTrainingResourceId] int NOT NULL,
		[LmsClassSessionId] int NOT NULL,
		[StartsOn] datetime2 NOT NULL,
		[EndsOn] datetime2 NOT NULL,
		[CreatedOn] datetime2 NOT NULL,
		[CreatedByUserId] nvarchar(128) NULL,
		CONSTRAINT [PK_LmsClassResourceBookings] PRIMARY KEY CLUSTERED ([LmsClassResourceBookingId] ASC)
	);
END");

			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassResourceBookings_Resource' AND object_id = OBJECT_ID('dbo.LmsClassResourceBookings'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassResourceBookings_Resource] ON [dbo].[LmsClassResourceBookings] ([DepartmentId], [LmsTrainingResourceId], [StartsOn]);");
			Execute.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LmsClassResourceBookings_Session' AND object_id = OBJECT_ID('dbo.LmsClassResourceBookings'))
	CREATE NONCLUSTERED INDEX [IX_LmsClassResourceBookings_Session] ON [dbo].[LmsClassResourceBookings] ([DepartmentId], [LmsClassSessionId]);");
		}

		public override void Down()
		{
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassResourceBookings', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassResourceBookings];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsTrainingResources', 'U') IS NOT NULL DROP TABLE [dbo].[LmsTrainingResources];");
			Execute.Sql("IF OBJECT_ID('dbo.LmsClassCostLines', 'U') IS NOT NULL DROP TABLE [dbo].[LmsClassCostLines];");
		}
	}
}
