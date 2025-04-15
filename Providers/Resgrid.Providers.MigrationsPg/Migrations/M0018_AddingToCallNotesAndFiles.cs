using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(18)]
	public class M0018_AddingToCallNotesAndFiles : Migration
	{
		public override void Up()
		{
			Alter.Table("CallAttachments".ToLower()).AddColumn("IsDeleted".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallAttachments".ToLower()).AddColumn("IsFlagged".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallAttachments".ToLower()).AddColumn("FlaggedReason".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallAttachments".ToLower()).AddColumn("FlaggedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallAttachments".ToLower()).AddColumn("DeletedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallAttachments".ToLower()).AddColumn("FlaggedOn".ToLower()).AsDateTime2().Nullable();
			Alter.Table("CallAttachments".ToLower()).AddColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();

			Alter.Table("CallNotes".ToLower()).AddColumn("IsDeleted".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallNotes".ToLower()).AddColumn("IsFlagged".ToLower()).AsBoolean().NotNullable().WithDefaultValue(false);
			Alter.Table("CallNotes".ToLower()).AddColumn("FlaggedReason".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallNotes".ToLower()).AddColumn("FlaggedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallNotes".ToLower()).AddColumn("DeletedByUserId".ToLower()).AsCustom("citext").Nullable();
			Alter.Table("CallNotes".ToLower()).AddColumn("FlaggedOn".ToLower()).AsDateTime2().Nullable();
			Alter.Table("CallNotes".ToLower()).AddColumn("DeletedOn".ToLower()).AsDateTime2().Nullable();

			Alter.Table("CallDispatches".ToLower()).AddColumn("DispatchedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
			Alter.Table("CallDispatchGroups".ToLower()).AddColumn("DispatchedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
			Alter.Table("CallDispatchRoles".ToLower()).AddColumn("DispatchedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
			Alter.Table("CallDispatchUnits".ToLower()).AddColumn("DispatchedOn".ToLower()).AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
		}

		public override void Down()
		{

		}
	}
}
