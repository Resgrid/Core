using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0360_AddLmsLearnerTables: lmsenrollments, lmslessonfiles, lmscrewviewings, lmslessonprogress,
	/// lmsassessmentattempts, lmssurveyresponses, lmscrewviewingattendees, lmspathassignments and lmsdepartmentstates. Guarded for safe
	/// retry.
	/// </summary>
	[Migration(360)]
	public class M0360_AddLmsLearnerTablesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsenrollments (
	lmsenrollmentid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	userid citext NOT NULL,
	source integer NOT NULL DEFAULT 0,
	rolepathkey citext NULL,
	assignedbyuserid citext NULL,
	assignedon timestamp without time zone NOT NULL,
	dueon timestamp without time zone NULL,
	status integer NOT NULL DEFAULT 0,
	isopen boolean NOT NULL DEFAULT true,
	startedon timestamp without time zone NULL,
	completedon timestamp without time zone NULL,
	testedout boolean NOT NULL DEFAULT false,
	currentlessonid integer NULL,
	progresspercent integer NOT NULL DEFAULT 0,
	seatseconds integer NOT NULL DEFAULT 0,
	lastactivityon timestamp without time zone NULL,
	learningrecordid integer NULL,
	personnelcertificationid integer NULL,
	remindersenton timestamp without time zone NULL,
	withdrawnon timestamp without time zone NULL,
	withdrawnbyuserid citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsenrollments PRIMARY KEY (lmsenrollmentid),
	CONSTRAINT fk_lmsenrollments_lmscourseid FOREIGN KEY (lmscourseid) REFERENCES public.lmscourses (lmscourseid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmsenrollments_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsenrollments_open ON public.lmsenrollments (departmentid, userid, lmscourseid) WHERE isopen = true;");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsenrollments_department_user ON public.lmsenrollments (departmentid, userid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsenrollments_course ON public.lmsenrollments (lmscourseid, status);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsenrollments_version_user ON public.lmsenrollments (lmscourseversionid, userid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsenrollments_status_lastactivityon ON public.lmsenrollments (status, lastactivityon);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslessonfiles (
	lmslessonfileid serial NOT NULL,
	departmentid integer NOT NULL,
	lmslessonid integer NOT NULL,
	userid citext NOT NULL,
	filename citext NULL,
	contenttype citext NULL,
	sizebytes bigint NOT NULL DEFAULT 0,
	data bytea NULL,
	uploadedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmslessonfiles PRIMARY KEY (lmslessonfileid),
	CONSTRAINT fk_lmslessonfiles_lmslessonid FOREIGN KEY (lmslessonid) REFERENCES public.lmslessons (lmslessonid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonfiles_departmentid ON public.lmslessonfiles (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonfiles_lesson ON public.lmslessonfiles (lmslessonid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscrewviewings (
	lmscrewviewingid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	lmslessonid integer NOT NULL,
	startedbyuserid citext NULL,
	startedon timestamp without time zone NOT NULL,
	expireson timestamp without time zone NOT NULL,
	endedon timestamp without time zone NULL,
	secret citext NOT NULL,
	CONSTRAINT pk_lmscrewviewings PRIMARY KEY (lmscrewviewingid),
	CONSTRAINT fk_lmscrewviewings_lmscourseid FOREIGN KEY (lmscourseid) REFERENCES public.lmscourses (lmscourseid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmscrewviewings_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmscrewviewings_lmslessonid FOREIGN KEY (lmslessonid) REFERENCES public.lmslessons (lmslessonid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscrewviewings_departmentid ON public.lmscrewviewings (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscrewviewings_lesson ON public.lmscrewviewings (lmslessonid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscrewviewings_version ON public.lmscrewviewings (lmscourseversionid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscrewviewings_course ON public.lmscrewviewings (lmscourseid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslessonprogress (
	lmslessonprogressid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsenrollmentid integer NOT NULL,
	lmslessonid integer NOT NULL,
	userid citext NOT NULL,
	status integer NOT NULL DEFAULT 0,
	startedon timestamp without time zone NULL,
	completedon timestamp without time zone NULL,
	seatseconds integer NOT NULL DEFAULT 0,
	videopositionseconds integer NOT NULL DEFAULT 0,
	watchedpercent integer NOT NULL DEFAULT 0,
	lastseenon timestamp without time zone NULL,
	responsetext text NULL,
	lmslessonfileid integer NULL,
	lmscrewviewingid integer NULL,
	CONSTRAINT pk_lmslessonprogress PRIMARY KEY (lmslessonprogressid),
	CONSTRAINT fk_lmslessonprogress_lmsenrollmentid FOREIGN KEY (lmsenrollmentid) REFERENCES public.lmsenrollments (lmsenrollmentid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessonprogress_lmslessonid FOREIGN KEY (lmslessonid) REFERENCES public.lmslessons (lmslessonid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessonprogress_lmslessonfileid FOREIGN KEY (lmslessonfileid) REFERENCES public.lmslessonfiles (lmslessonfileid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessonprogress_lmscrewviewingid FOREIGN KEY (lmscrewviewingid) REFERENCES public.lmscrewviewings (lmscrewviewingid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmslessonprogress_enrollment_lesson ON public.lmslessonprogress (lmsenrollmentid, lmslessonid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonprogress_departmentid ON public.lmslessonprogress (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonprogress_lesson ON public.lmslessonprogress (lmslessonid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonprogress_file ON public.lmslessonprogress (lmslessonfileid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessonprogress_viewing ON public.lmslessonprogress (lmscrewviewingid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsassessmentattempts (
	lmsassessmentattemptid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	lmslessonid integer NOT NULL,
	lmsenrollmentid integer NOT NULL,
	userid citext NOT NULL,
	attemptnumber integer NOT NULL DEFAULT 1,
	status integer NOT NULL DEFAULT 0,
	startedon timestamp without time zone NOT NULL,
	deadlineon timestamp without time zone NULL,
	submittedon timestamp without time zone NULL,
	score numeric(9,2) NOT NULL DEFAULT 0,
	maxscore numeric(9,2) NOT NULL DEFAULT 0,
	percent numeric(9,2) NOT NULL DEFAULT 0,
	passed boolean NULL,
	ispretest boolean NOT NULL DEFAULT false,
	passmark integer NOT NULL DEFAULT 0,
	revisionhash varchar(64) NULL,
	snapshotjson text NULL,
	answersjson text NULL,
	resultsjson text NULL,
	gradedbyuserid citext NULL,
	gradedon timestamp without time zone NULL,
	gradenotes citext NULL,
	attested boolean NOT NULL DEFAULT false,
	ipaddress citext NULL,
	useragent citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmsassessmentattempts PRIMARY KEY (lmsassessmentattemptid),
	CONSTRAINT fk_lmsassessmentattempts_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmsassessmentattempts_lmslessonid FOREIGN KEY (lmslessonid) REFERENCES public.lmslessons (lmslessonid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmsassessmentattempts_lmsenrollmentid FOREIGN KEY (lmsenrollmentid) REFERENCES public.lmsenrollments (lmsenrollmentid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsassessmentattempts_enrollment_lesson ON public.lmsassessmentattempts (lmsenrollmentid, lmslessonid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsassessmentattempts_lesson_status ON public.lmsassessmentattempts (lmslessonid, status);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsassessmentattempts_department_status ON public.lmsassessmentattempts (departmentid, status);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsassessmentattempts_version ON public.lmsassessmentattempts (lmscourseversionid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmssurveyresponses (
	lmssurveyresponseid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	lmslessonid integer NOT NULL,
	answersjson text NULL,
	submittedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmssurveyresponses PRIMARY KEY (lmssurveyresponseid),
	CONSTRAINT fk_lmssurveyresponses_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmssurveyresponses_lmslessonid FOREIGN KEY (lmslessonid) REFERENCES public.lmslessons (lmslessonid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmssurveyresponses_lesson ON public.lmssurveyresponses (lmslessonid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmssurveyresponses_departmentid ON public.lmssurveyresponses (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmssurveyresponses_version ON public.lmssurveyresponses (lmscourseversionid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscrewviewingattendees (
	lmscrewviewingattendeeid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscrewviewingid integer NOT NULL,
	userid citext NOT NULL,
	markedon timestamp without time zone NOT NULL,
	ipaddress citext NULL,
	CONSTRAINT pk_lmscrewviewingattendees PRIMARY KEY (lmscrewviewingattendeeid),
	CONSTRAINT fk_lmscrewviewingattendees_lmscrewviewingid FOREIGN KEY (lmscrewviewingid) REFERENCES public.lmscrewviewings (lmscrewviewingid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscrewviewingattendees_viewing_user ON public.lmscrewviewingattendees (lmscrewviewingid, userid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscrewviewingattendees_departmentid ON public.lmscrewviewingattendees (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmspathassignments (
	lmspathassignmentid serial NOT NULL,
	departmentid integer NOT NULL,
	pathkey citext NOT NULL,
	userid citext NOT NULL,
	assignedbyuserid citext NULL,
	assignedon timestamp without time zone NOT NULL,
	dueon timestamp without time zone NULL,
	removedon timestamp without time zone NULL,
	CONSTRAINT pk_lmspathassignments PRIMARY KEY (lmspathassignmentid)
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmspathassignments_department_path ON public.lmspathassignments (departmentid, pathkey);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmspathassignments_department_user ON public.lmspathassignments (departmentid, userid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsdepartmentstates (
	lmsdepartmentstateid serial NOT NULL,
	departmentid integer NOT NULL,
	lastentitledon timestamp without time zone NULL,
	updatedon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmsdepartmentstates PRIMARY KEY (lmsdepartmentstateid)
);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsdepartmentstates_departmentid ON public.lmsdepartmentstates (departmentid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmsdepartmentstates;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmspathassignments;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscrewviewingattendees;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmssurveyresponses;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsassessmentattempts;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmslessonprogress;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscrewviewings;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmslessonfiles;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsenrollments;");
		}
	}
}
