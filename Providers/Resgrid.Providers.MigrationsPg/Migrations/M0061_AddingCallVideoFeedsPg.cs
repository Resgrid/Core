using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(61)]
	public class M0061_AddingCallVideoFeedsPg : Migration
	{
		public override void Up()
		{
			Create.Table("callvideofeeds")
				.WithColumn("callvideofeedid").AsCustom("citext").NotNullable().PrimaryKey()
				.WithColumn("callid").AsInt32().NotNullable()
				.WithColumn("departmentid").AsInt32().NotNullable()
				.WithColumn("name").AsCustom("citext").NotNullable()
				.WithColumn("url").AsCustom("text").NotNullable()
				.WithColumn("feedtype").AsInt32().Nullable()
				.WithColumn("feedformat").AsInt32().Nullable()
				.WithColumn("description").AsCustom("text").Nullable()
				.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("latitude").AsDecimal(10, 7).Nullable()
				.WithColumn("longitude").AsDecimal(10, 7).Nullable()
				.WithColumn("addedbyuserid").AsCustom("citext").NotNullable()
				.WithColumn("addedon").AsDateTime().NotNullable()
				.WithColumn("updatedon").AsDateTime().Nullable()
				.WithColumn("sortorder").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("isdeleted").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("deletedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("deletedon").AsDateTime().Nullable()
				.WithColumn("isflagged").AsBoolean().NotNullable().WithDefaultValue(false)
				.WithColumn("flaggedreason").AsCustom("text").Nullable()
				.WithColumn("flaggedbyuserid").AsCustom("citext").Nullable()
				.WithColumn("flaggedon").AsDateTime().Nullable();

			Create.ForeignKey("fk_callvideofeeds_calls")
				.FromTable("callvideofeeds").ForeignColumn("callid")
				.ToTable("calls").PrimaryColumn("callid");

			Create.ForeignKey("fk_callvideofeeds_departments")
				.FromTable("callvideofeeds").ForeignColumn("departmentid")
				.ToTable("departments").PrimaryColumn("departmentid");

			Create.Index("ix_callvideofeeds_callid")
				.OnTable("callvideofeeds")
				.OnColumn("callid");

			Create.Index("ix_callvideofeeds_departmentid")
				.OnTable("callvideofeeds")
				.OnColumn("departmentid");
		}

		public override void Down()
		{
			Delete.ForeignKey("fk_callvideofeeds_calls").OnTable("callvideofeeds");
			Delete.ForeignKey("fk_callvideofeeds_departments").OnTable("callvideofeeds");
			Delete.Table("callvideofeeds");
		}
	}
}
