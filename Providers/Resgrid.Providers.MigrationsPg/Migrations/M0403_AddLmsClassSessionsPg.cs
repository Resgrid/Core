using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0403_AddLmsClassSessions: the same tables and indexes in lower case, text columns citext (member ids compare
	/// case-insensitively; the unique roster index folds case). Guarded for safe retry.
	/// </summary>
	[Migration(403)]
	public class M0403_AddLmsClassSessionsPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclasssessions (
	lmsclasssessionid serial NOT NULL,
	departmentid integer NOT NULL,
	calendaritemid integer NULL,
	serieskey citext NULL,
	seriesindex integer NOT NULL DEFAULT 0,
	title citext NOT NULL,
	description citext NULL,
	contentsummary citext NULL,
	coursekind integer NOT NULL DEFAULT 0,
	lmscourseid integer NULL,
	trainingid integer NULL,
	lmslessonplanid integer NULL,
	lmslessonplanversionid integer NULL,
	categoryid integer NULL,
	coursecode citext NULL,
	startson timestamp without time zone NOT NULL,
	endson timestamp without time zone NOT NULL,
	location citext NULL,
	meetingurl citext NULL,
	capacity integer NOT NULL DEFAULT 0,
	waitlistenabled boolean NOT NULL DEFAULT false,
	enrollmentopen boolean NOT NULL DEFAULT false,
	requiresapproval boolean NOT NULL DEFAULT false,
	cutoffdays integer NOT NULL DEFAULT 0,
	allowselfunenroll boolean NOT NULL DEFAULT false,
	restrictgroupids citext NULL,
	visiblegroupids citext NULL,
	tags citext NULL,
	reminderhours integer NOT NULL DEFAULT 0,
	qrenabled boolean NOT NULL DEFAULT false,
	qrsecret citext NULL,
	gpsrequired boolean NOT NULL DEFAULT false,
	gpslatitude numeric(9,6) NULL,
	gpslongitude numeric(9,6) NULL,
	gpsradiusmeters integer NOT NULL DEFAULT 0,
	postclassevaluationkind integer NOT NULL DEFAULT 0,
	postclassevaluationref citext NULL,
	status integer NOT NULL DEFAULT 0,
	cancelledon timestamp without time zone NULL,
	cancelreason citext NULL,
	signedoffon timestamp without time zone NULL,
	signedoffbyuserid citext NULL,
	retentionyears integer NULL,
	retainuntil timestamp without time zone NULL,
	currency citext NULL,
	lmstrainingplanitemid integer NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	deletedon timestamp without time zone NULL,
	deletedbyuserid citext NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	CONSTRAINT pk_lmsclasssessions PRIMARY KEY (lmsclasssessionid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclasssessions_department_starts ON public.lmsclasssessions (departmentid, startson);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclasssessions_status_starts ON public.lmsclasssessions (status, isdeleted, startson);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclasssessions_calendaritem ON public.lmsclasssessions (departmentid, calendaritemid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclasssessions_series ON public.lmsclasssessions (departmentid, serieskey);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclassenrollments (
	lmsclassenrollmentid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	userid citext NOT NULL,
	status integer NOT NULL DEFAULT 0,
	waitlistposition integer NULL,
	attendance integer NOT NULL DEFAULT 0,
	iswalkin boolean NOT NULL DEFAULT false,
	checkinmethod integer NOT NULL DEFAULT 0,
	checkinon timestamp without time zone NULL,
	checkouton timestamp without time zone NULL,
	checkinlatitude numeric(9,6) NULL,
	checkinlongitude numeric(9,6) NULL,
	importedminutes integer NULL,
	hours numeric(9,2) NULL,
	hoursoverride numeric(9,2) NULL,
	membername citext NULL,
	jobtitle citext NULL,
	learningrecordid integer NULL,
	timecardofferedon timestamp without time zone NULL,
	remindersenton timestamp without time zone NULL,
	enrolledon timestamp without time zone NOT NULL,
	enrolledbyuserid citext NULL,
	decidedon timestamp without time zone NULL,
	decidedbyuserid citext NULL,
	promotedon timestamp without time zone NULL,
	withdrawnon timestamp without time zone NULL,
	note citext NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsclassenrollments PRIMARY KEY (lmsclassenrollmentid)
);");

			Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsclassenrollments_session_user ON public.lmsclassenrollments (lmsclasssessionid, userid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassenrollments_department_user ON public.lmsclassenrollments (departmentid, userid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsclassinstructors (
	lmsclassinstructorid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsclasssessionid integer NOT NULL,
	userid citext NULL,
	name citext NULL,
	qualifications citext NULL,
	role integer NOT NULL DEFAULT 0,
	hourstaught numeric(9,2) NULL,
	sortorder integer NOT NULL DEFAULT 0,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmsclassinstructors PRIMARY KEY (lmsclassinstructorid)
);");

			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassinstructors_session ON public.lmsclassinstructors (departmentid, lmsclasssessionid);");
			Execute.Sql("CREATE INDEX IF NOT EXISTS ix_lmsclassinstructors_user ON public.lmsclassinstructors (departmentid, userid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclassinstructors;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclassenrollments;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsclasssessions;");
		}
	}
}
