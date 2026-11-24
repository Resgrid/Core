using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>PostgreSQL twin of M0332_AddVoiceIntakeRecording (voice intake call recording).</summary>
	[Migration(332)]
	public class M0332_AddVoiceIntakeRecordingPg : Migration
	{
		public override void Up()
		{
			Execute.Sql("ALTER TABLE departmentphonenumbers ADD COLUMN IF NOT EXISTS voicerecordingenabled boolean NOT NULL DEFAULT false;");

			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingstate integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingsid citext NULL;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingdurationseconds integer NULL;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingattempts integer NOT NULL DEFAULT 0;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingclaimedon timestamp without time zone NULL;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingcallattachmentid integer NULL;");
			Execute.Sql("ALTER TABLE voiceintakesessions ADD COLUMN IF NOT EXISTS recordingcarrierdeletedon timestamp without time zone NULL;");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_voiceintakesessions_recording ON voiceintakesessions (recordingstate, startedon);");
		}

		public override void Down()
		{
			Execute.Sql("DROP INDEX IF EXISTS ix_voiceintakesessions_recording;");
			foreach (var column in new[] { "recordingcarrierdeletedon", "recordingcallattachmentid", "recordingclaimedon", "recordingattempts",
				"recordingdurationseconds", "recordingsid", "recordingstate" })
				Execute.Sql($"ALTER TABLE voiceintakesessions DROP COLUMN IF EXISTS {column};");
			Execute.Sql("ALTER TABLE departmentphonenumbers DROP COLUMN IF EXISTS voicerecordingenabled;");
		}
	}
}
