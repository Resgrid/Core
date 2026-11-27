using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// PostgreSQL twin of M0349_AddLmsCourses: lmscourses, lmscourseversions, lmscourseversionapprovals, lmscourseaudiences,
	/// lmscourseowners, lmscoursemodules, lmsmediaassets, lmsquestionbanks, lmsquestions and lmslessons. Text columns are citext;
	/// content and revision hashes stay exact varchar(64). Guarded for safe retry.
	/// </summary>
	[Migration(349)]
	public class M0349_AddLmsCoursesPg : Migration
	{
		public override void Up()
		{
			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscourses (
	lmscourseid serial NOT NULL,
	departmentid integer NOT NULL,
	publisherkind integer NOT NULL DEFAULT 0,
	systemkey citext NULL,
	manifestversion integer NOT NULL DEFAULT 0,
	title citext NOT NULL,
	code citext NULL,
	summary citext NULL,
	tags citext NULL,
	status integer NOT NULL DEFAULT 0,
	currentversionid integer NULL,
	isselfenrollable boolean NOT NULL DEFAULT false,
	visibilitykeys citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmscourses PRIMARY KEY (lmscourseid)
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourses_departmentid ON public.lmscourses (departmentid);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscourses_systemkey ON public.lmscourses (systemkey) WHERE systemkey IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscourseversions (
	lmscourseversionid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	major integer NOT NULL DEFAULT 0,
	minor integer NOT NULL DEFAULT 0,
	status integer NOT NULL,
	title citext NULL,
	contentjson text NULL,
	contenthtml text NULL,
	contenthash varchar(64) NULL,
	changesummary citext NULL,
	locale citext NULL,
	hours numeric(9,2) NOT NULL DEFAULT 0,
	categoryid integer NULL,
	ceprovider citext NULL,
	cecoursenumber citext NULL,
	certificatetemplateid integer NULL,
	issuescertificationtypeid integer NULL,
	majorchangepolicy integer NOT NULL DEFAULT 0,
	reassignduedays integer NULL,
	manifestversion integer NOT NULL DEFAULT 0,
	effectiveon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	updatedbyuserid citext NULL,
	submittedon timestamp without time zone NULL,
	submittedbyuserid citext NULL,
	approvedon timestamp without time zone NULL,
	approvedbyuserid citext NULL,
	publishedon timestamp without time zone NULL,
	publishedbyuserid citext NULL,
	supersededon timestamp without time zone NULL,
	retiredon timestamp without time zone NULL,
	rowversion bigint NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmscourseversions PRIMARY KEY (lmscourseversionid),
	CONSTRAINT fk_lmscourseversions_lmscourseid FOREIGN KEY (lmscourseid) REFERENCES public.lmscourses (lmscourseid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseversions_course_status ON public.lmscourseversions (lmscourseid, status);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseversions_departmentid_status ON public.lmscourseversions (departmentid, status);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscourseversionapprovals (
	lmscourseversionapprovalid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	round integer NOT NULL DEFAULT 1,
	steporder integer NOT NULL,
	approveruserid citext NULL,
	decision integer NOT NULL DEFAULT 0,
	decidedbyuserid citext NULL,
	comment citext NULL,
	decidedon timestamp without time zone NULL,
	signaturerecordid integer NULL,
	createdon timestamp without time zone NOT NULL,
	CONSTRAINT pk_lmscourseversionapprovals PRIMARY KEY (lmscourseversionapprovalid),
	CONSTRAINT fk_lmscourseversionapprovals_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseversionapprovals_version ON public.lmscourseversionapprovals (lmscourseversionid, round);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseversionapprovals_departmentid ON public.lmscourseversionapprovals (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscourseaudiences (
	lmscourseaudienceid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	kind integer NOT NULL,
	refid citext NULL,
	CONSTRAINT pk_lmscourseaudiences PRIMARY KEY (lmscourseaudienceid),
	CONSTRAINT fk_lmscourseaudiences_lmscourseid FOREIGN KEY (lmscourseid) REFERENCES public.lmscourses (lmscourseid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseaudiences_course ON public.lmscourseaudiences (lmscourseid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseaudiences_departmentid ON public.lmscourseaudiences (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscourseowners (
	lmscourseownerid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseid integer NOT NULL,
	userid citext NOT NULL,
	canedit boolean NOT NULL DEFAULT false,
	canassign boolean NOT NULL DEFAULT false,
	canreport boolean NOT NULL DEFAULT false,
	addedon timestamp without time zone NOT NULL,
	addedbyuserid citext NULL,
	CONSTRAINT pk_lmscourseowners PRIMARY KEY (lmscourseownerid),
	CONSTRAINT fk_lmscourseowners_lmscourseid FOREIGN KEY (lmscourseid) REFERENCES public.lmscourses (lmscourseid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmscourseowners_course_user ON public.lmscourseowners (lmscourseid, userid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscourseowners_department_user ON public.lmscourseowners (departmentid, userid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmscoursemodules (
	lmscoursemoduleid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	title citext NULL,
	sortorder integer NOT NULL DEFAULT 0,
	CONSTRAINT pk_lmscoursemodules PRIMARY KEY (lmscoursemoduleid),
	CONSTRAINT fk_lmscoursemodules_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscoursemodules_version ON public.lmscoursemodules (lmscourseversionid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmscoursemodules_departmentid ON public.lmscoursemodules (departmentid);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsmediaassets (
	lmsmediaassetid serial NOT NULL,
	departmentid integer NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	filename citext NULL,
	contenttype citext NULL,
	sourcekey citext NULL,
	sourcebytes bigint NOT NULL DEFAULT 0,
	status integer NOT NULL DEFAULT 0,
	hlsprefix citext NULL,
	mp4key citext NULL,
	outputbytes bigint NOT NULL DEFAULT 0,
	durationseconds integer NOT NULL DEFAULT 0,
	attempts integer NOT NULL DEFAULT 0,
	lasterror citext NULL,
	claimedon timestamp without time zone NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	readyon timestamp without time zone NULL,
	archivedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsmediaassets PRIMARY KEY (lmsmediaassetid)
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsmediaassets_departmentid ON public.lmsmediaassets (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsmediaassets_status_createdon ON public.lmsmediaassets (status, createdon);");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsquestionbanks (
	lmsquestionbankid serial NOT NULL,
	departmentid integer NOT NULL,
	name citext NOT NULL,
	description citext NULL,
	isshared boolean NOT NULL DEFAULT true,
	owneruserid citext NULL,
	tags citext NULL,
	systemkey citext NULL,
	isdeleted boolean NOT NULL DEFAULT false,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	CONSTRAINT pk_lmsquestionbanks PRIMARY KEY (lmsquestionbankid)
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsquestionbanks_departmentid ON public.lmsquestionbanks (departmentid);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsquestionbanks_systemkey ON public.lmsquestionbanks (systemkey) WHERE systemkey IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmsquestions (
	lmsquestionid serial NOT NULL,
	departmentid integer NOT NULL,
	lmsquestionbankid integer NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	text text NULL,
	imageurl citext NULL,
	optionsjson text NULL,
	explanation text NULL,
	tags citext NULL,
	points numeric(9,2) NOT NULL DEFAULT 1,
	translationsjson text NULL,
	isretired boolean NOT NULL DEFAULT false,
	sortorder integer NOT NULL DEFAULT 0,
	systemkey citext NULL,
	createdon timestamp without time zone NOT NULL,
	createdbyuserid citext NULL,
	updatedon timestamp without time zone NULL,
	CONSTRAINT pk_lmsquestions PRIMARY KEY (lmsquestionid),
	CONSTRAINT fk_lmsquestions_lmsquestionbankid FOREIGN KEY (lmsquestionbankid) REFERENCES public.lmsquestionbanks (lmsquestionbankid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsquestions_bank ON public.lmsquestions (lmsquestionbankid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmsquestions_departmentid ON public.lmsquestions (departmentid);");

			Execute.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_lmsquestions_bank_systemkey ON public.lmsquestions (lmsquestionbankid, systemkey) WHERE systemkey IS NOT NULL;");

			Execute.Sql(@"
CREATE TABLE IF NOT EXISTS public.lmslessons (
	lmslessonid serial NOT NULL,
	departmentid integer NOT NULL,
	lmscourseversionid integer NOT NULL,
	lmscoursemoduleid integer NOT NULL,
	kind integer NOT NULL DEFAULT 0,
	title citext NULL,
	sortorder integer NOT NULL DEFAULT 0,
	isrequired boolean NOT NULL DEFAULT true,
	contentjson text NULL,
	contenthtml text NULL,
	videosource integer NOT NULL DEFAULT 0,
	videourl citext NULL,
	lmsmediaassetid integer NULL,
	captiontracksjson text NULL,
	transcripttext text NULL,
	documenturl citext NULL,
	documentname citext NULL,
	protocolversionid integer NULL,
	minimumseatseconds integer NOT NULL DEFAULT 0,
	requiredwatchpercent integer NOT NULL DEFAULT 0,
	availableoffline boolean NOT NULL DEFAULT false,
	offlinemaxbytes bigint NULL,
	offlineexpirydays integer NULL,
	freeresponseprompt citext NULL,
	filerequestprompt citext NULL,
	lmsquestionbankid integer NULL,
	drawcount integer NOT NULL DEFAULT 0,
	drawtags citext NULL,
	passmark integer NOT NULL DEFAULT 80,
	attemptlimit integer NOT NULL DEFAULT 0,
	retakecooldownhours integer NOT NULL DEFAULT 0,
	timelimitminutes integer NOT NULL DEFAULT 0,
	showanswerspolicy integer NOT NULL DEFAULT 2,
	ispretest boolean NOT NULL DEFAULT false,
	randomizeanswers boolean NOT NULL DEFAULT true,
	requireattestation boolean NOT NULL DEFAULT false,
	surveyjson text NULL,
	CONSTRAINT pk_lmslessons PRIMARY KEY (lmslessonid),
	CONSTRAINT fk_lmslessons_lmscourseversionid FOREIGN KEY (lmscourseversionid) REFERENCES public.lmscourseversions (lmscourseversionid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessons_lmscoursemoduleid FOREIGN KEY (lmscoursemoduleid) REFERENCES public.lmscoursemodules (lmscoursemoduleid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessons_lmsmediaassetid FOREIGN KEY (lmsmediaassetid) REFERENCES public.lmsmediaassets (lmsmediaassetid) ON DELETE NO ACTION,
	CONSTRAINT fk_lmslessons_lmsquestionbankid FOREIGN KEY (lmsquestionbankid) REFERENCES public.lmsquestionbanks (lmsquestionbankid) ON DELETE NO ACTION
);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_version ON public.lmslessons (lmscourseversionid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_module ON public.lmslessons (lmscoursemoduleid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_departmentid ON public.lmslessons (departmentid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_media ON public.lmslessons (lmsmediaassetid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_bank ON public.lmslessons (lmsquestionbankid);");

			Execute.Sql(@"
CREATE INDEX IF NOT EXISTS ix_lmslessons_protocolversion ON public.lmslessons (protocolversionid);");
		}

		public override void Down()
		{
			Execute.Sql("DROP TABLE IF EXISTS public.lmslessons;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsquestions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsquestionbanks;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmsmediaassets;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscoursemodules;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscourseowners;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscourseaudiences;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscourseversionapprovals;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscourseversions;");
			Execute.Sql("DROP TABLE IF EXISTS public.lmscourses;");
		}
	}
}
