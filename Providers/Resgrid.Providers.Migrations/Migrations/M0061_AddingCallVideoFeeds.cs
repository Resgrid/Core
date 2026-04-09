using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(61)]
	public class M0061_AddingCallVideoFeeds : Migration
	{
		public override void Up()
		{
			Create.Table("CallVideoFeeds")
				.WithColumn("CallVideoFeedId").AsString(128).NotNullable().PrimaryKey()
				.WithColumn("CallId").AsInt32().NotNullable()
				.WithColumn("DepartmentId").AsInt32().NotNullable()
				.WithColumn("Name").AsString(500).NotNullable()
				.WithColumn("Url").AsString(2000).NotNullable()
				.WithColumn("FeedType").AsInt32().Nullable()
				.WithColumn("FeedFormat").AsInt32().Nullable()
				.WithColumn("Description").AsString(4000).Nullable()
				.WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("Latitude").AsDecimal(10, 7).Nullable()
				.WithColumn("Longitude").AsDecimal(10, 7).Nullable()
				.WithColumn("AddedByUserId").AsString(128).NotNullable()
				.WithColumn("AddedOn").AsDateTime2().NotNullable()
				.WithColumn("UpdatedOn").AsDateTime2().Nullable()
				.WithColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("DeletedByUserId").AsString(128).Nullable()
				.WithColumn("DeletedOn").AsDateTime2().Nullable()
				.WithColumn("IsFlagged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("FlaggedReason").AsString(4000).Nullable()
				.WithColumn("FlaggedByUserId").AsString(128).Nullable()
				.WithColumn("FlaggedOn").AsDateTime2().Nullable();

			Create.ForeignKey("FK_CallVideoFeeds_Calls")
				.FromTable("CallVideoFeeds").ForeignColumn("CallId")
				.ToTable("Calls").PrimaryColumn("CallId");

			Create.ForeignKey("FK_CallVideoFeeds_Departments")
				.FromTable("CallVideoFeeds").ForeignColumn("DepartmentId")
				.ToTable("Departments").PrimaryColumn("DepartmentId");

			Create.Index("IX_CallVideoFeeds_CallId")
				.OnTable("CallVideoFeeds")
				.OnColumn("CallId");

			Create.Index("IX_CallVideoFeeds_DepartmentId")
				.OnTable("CallVideoFeeds")
				.OnColumn("DepartmentId");
		}

		public override void Down()
		{
			Delete.ForeignKey("FK_CallVideoFeeds_Calls").OnTable("CallVideoFeeds");
			Delete.ForeignKey("FK_CallVideoFeeds_Departments").OnTable("CallVideoFeeds");
			Delete.Table("CallVideoFeeds");
		}
	}
}
