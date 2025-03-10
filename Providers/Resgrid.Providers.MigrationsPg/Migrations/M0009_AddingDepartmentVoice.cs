using FluentMigrator;
using System;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(9)]
	public class M0009_AddingDepartmentVoice : Migration
	{
		public override void Up()
		{
			Create.Table("DepartmentVoices".ToLower())
			   .WithColumn("DepartmentVoiceId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("StartConferenceNumber".ToLower()).AsInt32().NotNullable();

			Create.ForeignKey("FK_DepartmentVoices_Department")
				.FromTable("DepartmentVoices".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.Table("DepartmentVoiceChannels".ToLower())
			   .WithColumn("DepartmentVoiceChannelId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("DepartmentId".ToLower()).AsInt32().NotNullable()
			   .WithColumn("Name".ToLower()).AsCustom("citext")
			   .WithColumn("SystemConferenceId".ToLower()).AsCustom("citext")
			   .WithColumn("SystemCallflowId".ToLower()).AsCustom("citext")
			   .WithColumn("ConferenceNumber".ToLower()).AsInt32()
			   .WithColumn("IsDefault".ToLower()).AsBoolean();

			Create.ForeignKey("FK_DepartmentVoiceChannels_Department")
				.FromTable("DepartmentVoiceChannels".ToLower()).ForeignColumn("DepartmentId".ToLower())
				.ToTable("Departments".ToLower()).PrimaryColumn("DepartmentId".ToLower());

			Create.ForeignKey("FK_DepartmentVoiceChannels_DepartmentVoices")
				.FromTable("DepartmentVoiceChannels".ToLower()).ForeignColumn("DepartmentVoiceId".ToLower())
				.ToTable("DepartmentVoices".ToLower()).PrimaryColumn("DepartmentVoiceId".ToLower());

			Create.Table("DepartmentVoiceUsers".ToLower())
			   .WithColumn("DepartmentVoiceUserId".ToLower()).AsCustom("citext").NotNullable().PrimaryKey()
			   .WithColumn("DepartmentVoiceId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("UserId".ToLower()).AsCustom("citext").NotNullable()
			   .WithColumn("SystemUserId".ToLower()).AsCustom("citext")
			   .WithColumn("SystemDeviceId".ToLower()).AsCustom("citext");

			Create.ForeignKey("FK_DepartmentVoiceUsers_DepartmentVoices")
				.FromTable("DepartmentVoiceUsers".ToLower()).ForeignColumn("DepartmentVoiceId".ToLower())
				.ToTable("DepartmentVoices".ToLower()).PrimaryColumn("DepartmentVoiceId".ToLower());

		}

		public override void Down()
		{

		}
	}
}
