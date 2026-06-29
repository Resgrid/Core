IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[Quidjibo].[Schedule]') AND type in (N'U'))
BEGIN
CREATE TABLE [Quidjibo].[Schedule](
	[Id] [uniqueidentifier] NOT NULL,
	[Sequence] [bigint] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](250) NOT NULL,
	[Queue] [nvarchar](250) NOT NULL,
	[CronExpression] [nvarchar](50) NOT NULL,
	[CreatedOn] [datetime] NULL,
	[EnqueueOn] [datetime] NULL,
	[EnqueuedOn] [datetime] NULL,
	[VisibleOn] [datetime] NULL,
	[Payload] [varbinary](max) NOT NULL,
 CONSTRAINT [PK_Schedule] PRIMARY KEY NONCLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY],
 CONSTRAINT [IX_Schedule_Sequence] UNIQUE CLUSTERED 
(
	[Sequence] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END