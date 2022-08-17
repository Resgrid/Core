using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	[Migration(18)]
	public class M0018_AddingToCallNotesAndFiles : Migration
	{
		public override void Up()
		{
			Alter.Table("CallAttachments").AddColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallAttachments").AddColumn("IsFlagged").AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallAttachments").AddColumn("FlaggedReason").AsString().Nullable();
			Alter.Table("CallAttachments").AddColumn("FlaggedByUserId").AsString(128).Nullable();
			Alter.Table("CallAttachments").AddColumn("DeletedByUserId").AsString(128).Nullable();
			Alter.Table("CallAttachments").AddColumn("FlaggedOn").AsDateTime2().Nullable();
			Alter.Table("CallAttachments").AddColumn("DeletedOn").AsDateTime2().Nullable();

			Alter.Table("CallNotes").AddColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallNotes").AddColumn("IsFlagged").AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallNotes").AddColumn("FlaggedReason").AsString().Nullable();
			Alter.Table("CallNotes").AddColumn("FlaggedByUserId").AsString(128).Nullable();
			Alter.Table("CallNotes").AddColumn("DeletedByUserId").AsString(128).Nullable();
			Alter.Table("CallNotes").AddColumn("FlaggedOn").AsDateTime2().Nullable();
			Alter.Table("CallNotes").AddColumn("DeletedOn").AsDateTime2().Nullable();

			Alter.Table("CallDispatches").AddColumn("DispatchedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime); ;
			Alter.Table("CallDispatchGroups").AddColumn("DispatchedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime); ;
			Alter.Table("CallDispatchRoles").AddColumn("DispatchedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime); ;
			Alter.Table("CallDispatchUnits").AddColumn("DispatchedOn").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime); ;
		}

		public override void Down()
		{
			
		}
	}
}
