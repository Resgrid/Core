using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>A department's training data with a foreign department's rows mixed into every list (the scoping checks look for "FOREIGN").</summary>
	internal static class LmsDataExitFixtures
	{
		public const int Dept = 501;
		public const int Other = 502;
		public const string Admin = "a1b2c3d4-0000-4000-8000-000000000001";
		public const string Member = "a1b2c3d4-0000-4000-8000-000000000002";
		public const string Former = "a1b2c3d4-0000-4000-8000-000000000003";
		public const string SecretOption = "SECRET-KEY-OPTION";
		public static readonly DateTime Now = new DateTime(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);

		public static LmsDataExportSnapshot Snapshot(LmsDataExportScopes scope = LmsDataExportScopes.Full)
		{
			var question = new LmsQuestion { LmsQuestionId = 1, DepartmentId = Dept, LmsQuestionBankId = 10, Kind = (int)LmsQuestionKinds.SingleChoice, Text = "Max hose pressure?", Points = 1 };
			question.Options = new List<LmsQuestionOption>
			{
				new LmsQuestionOption { Key = "a", Text = "50 psi" }, new LmsQuestionOption { Key = "b", Text = SecretOption, IsCorrect = true }
			};
			var ordering = new LmsQuestion { LmsQuestionId = 2, DepartmentId = Dept, LmsQuestionBankId = 10, Kind = (int)LmsQuestionKinds.Ordering, Text = "Order the steps" };
			ordering.Options = new List<LmsQuestionOption> { new LmsQuestionOption { Key = "x", Text = "First" }, new LmsQuestionOption { Key = "y", Text = "Second" } };
			var foreignQuestion = new LmsQuestion { LmsQuestionId = 3, DepartmentId = Other, LmsQuestionBankId = 11, Text = "FOREIGN question" };

			return new LmsDataExportSnapshot
			{
				DepartmentId = Dept,
				DepartmentName = "Station Nine",
				Scope = scope,
				GeneratedOnUtc = Now,
				ExportId = 77,
				RequestedByUserId = Admin,
				BaseUrl = "https://resgrid.example/",
				Courses = new List<LmsCourse>
				{
					new LmsCourse { LmsCourseId = 1, DepartmentId = Dept, Title = "Hose handling", Code = "FF-101", Status = 1, CurrentVersionId = 11, CreatedOn = Now.AddDays(-30), CreatedByUserId = Admin },
					new LmsCourse { LmsCourseId = 2, DepartmentId = Dept, Title = "Airway quiz course", Status = 1, CurrentVersionId = 21, CreatedOn = Now.AddDays(-20) },
					new LmsCourse { LmsCourseId = 3, DepartmentId = Dept, Title = "Draft only", Status = 0, CreatedOn = Now.AddDays(-5) },
					new LmsCourse { LmsCourseId = 9, DepartmentId = Other, Title = "FOREIGN course", Status = 1, CurrentVersionId = 91, CreatedOn = Now }
				},
				Versions = new List<LmsCourseVersion>
				{
					new LmsCourseVersion { LmsCourseVersionId = 11, LmsCourseId = 1, DepartmentId = Dept, Major = 1, Minor = 0, Status = 2, Title = "Hose handling", Locale = "en", Hours = 2, ContentHtml = "<p>Overview</p>", CreatedOn = Now.AddDays(-29) },
					new LmsCourseVersion { LmsCourseVersionId = 21, LmsCourseId = 2, DepartmentId = Dept, Major = 1, Minor = 0, Status = 2, Title = "Airway quiz course", Locale = "de", CreatedOn = Now.AddDays(-19) },
					new LmsCourseVersion { LmsCourseVersionId = 31, LmsCourseId = 3, DepartmentId = Dept, Status = 0, Title = "Draft only", CreatedOn = Now.AddDays(-4) },
					new LmsCourseVersion { LmsCourseVersionId = 91, LmsCourseId = 9, DepartmentId = Other, Status = 2, Title = "FOREIGN course", CreatedOn = Now }
				},
				Modules = new List<LmsCourseModule>
				{
					new LmsCourseModule { LmsCourseModuleId = 100, LmsCourseVersionId = 11, DepartmentId = Dept, Title = "Basics", SortOrder = 0 },
					new LmsCourseModule { LmsCourseModuleId = 200, LmsCourseVersionId = 21, DepartmentId = Dept, Title = "Quiz", SortOrder = 0 },
					new LmsCourseModule { LmsCourseModuleId = 900, LmsCourseVersionId = 91, DepartmentId = Other, Title = "FOREIGN module" }
				},
				Lessons = new List<LmsLesson>
				{
					new LmsLesson { LmsLessonId = 1000, LmsCourseVersionId = 11, LmsCourseModuleId = 100, DepartmentId = Dept, Kind = (int)LmsLessonKinds.Blocks, Title = "Couplings", ContentHtml = "<p>Hand-tight plus a quarter turn.</p>", ContentJson = "{\"schema\":2,\"blocks\":[]}" },
					new LmsLesson { LmsLessonId = 1001, LmsCourseVersionId = 11, LmsCourseModuleId = 100, DepartmentId = Dept, Kind = (int)LmsLessonKinds.Document, Title = "SOG", DocumentName = "Hose SOG", DocumentUrl = "https://docs.example/sog.pdf", SortOrder = 1 },
					new LmsLesson { LmsLessonId = 1002, LmsCourseVersionId = 11, LmsCourseModuleId = 100, DepartmentId = Dept, Kind = (int)LmsLessonKinds.Video, Title = "Video", VideoSource = (int)LmsVideoSources.Embed, VideoUrl = "https://www.youtube.com/embed/abc", SortOrder = 2 },
					new LmsLesson { LmsLessonId = 2000, LmsCourseVersionId = 21, LmsCourseModuleId = 200, DepartmentId = Dept, Kind = (int)LmsLessonKinds.Assessment, Title = "Airway check", LmsQuestionBankId = 10, PassMark = 80 },
					new LmsLesson { LmsLessonId = 9000, LmsCourseVersionId = 91, LmsCourseModuleId = 900, DepartmentId = Other, Kind = (int)LmsLessonKinds.Blocks, Title = "FOREIGN lesson" }
				},
				Banks = new List<LmsQuestionBank>
				{
					new LmsQuestionBank { LmsQuestionBankId = 10, DepartmentId = Dept, Name = "Pumps", IsShared = true, CreatedOn = Now.AddDays(-40) },
					new LmsQuestionBank { LmsQuestionBankId = 11, DepartmentId = Other, Name = "FOREIGN bank" }
				},
				Questions = new List<LmsQuestion> { question, ordering, foreignQuestion },
				Attempts = new List<LmsAssessmentAttempt>
				{
					new LmsAssessmentAttempt { LmsAssessmentAttemptId = 1, DepartmentId = Dept, LmsCourseVersionId = 21, LmsLessonId = 2000, UserId = Member, AttemptNumber = 1, Status = (int)LmsAttemptStatuses.Scored, StartedOn = Now.AddDays(-3), SubmittedOn = Now.AddDays(-3).AddMinutes(12), Score = 1, MaxScore = 2, Percent = 50, Passed = false, PassMark = 80, SnapshotJson = "{\"Questions\":[{\"Text\":\"" + SecretOption + "\"}]}", AnswersJson = "[]" },
					new LmsAssessmentAttempt { LmsAssessmentAttemptId = 2, DepartmentId = Dept, LmsCourseVersionId = 21, LmsLessonId = 2000, UserId = Member, AttemptNumber = 2, Status = (int)LmsAttemptStatuses.Scored, StartedOn = Now.AddDays(-2), SubmittedOn = Now.AddDays(-2).AddMinutes(8), Score = 2, MaxScore = 2, Percent = 100, Passed = true, PassMark = 80 },
					new LmsAssessmentAttempt { LmsAssessmentAttemptId = 3, DepartmentId = Dept, LmsCourseVersionId = 21, LmsLessonId = 2000, UserId = Former, AttemptNumber = 1, Status = (int)LmsAttemptStatuses.InProgress, StartedOn = Now.AddHours(-1) },
					new LmsAssessmentAttempt { LmsAssessmentAttemptId = 9, DepartmentId = Other, LmsLessonId = 9000, UserId = "FOREIGN-user", Status = 1, StartedOn = Now }
				},
				Enrollments = new List<LmsEnrollment>
				{
					new LmsEnrollment { LmsEnrollmentId = 1, DepartmentId = Dept, LmsCourseId = 2, LmsCourseVersionId = 21, UserId = Member, Status = (int)LmsEnrollmentStatuses.Completed, AssignedOn = Now.AddDays(-5), CompletedOn = Now.AddDays(-2), LearningRecordId = 1 },
					new LmsEnrollment { LmsEnrollmentId = 2, DepartmentId = Dept, LmsCourseId = 1, LmsCourseVersionId = 11, UserId = Former, Status = (int)LmsEnrollmentStatuses.InProgress, AssignedOn = Now.AddDays(-5) },
					new LmsEnrollment { LmsEnrollmentId = 9, DepartmentId = Other, LmsCourseId = 9, UserId = "FOREIGN-user", AssignedOn = Now }
				},
				LearningRecords = new List<LearningRecord>
				{
					new LearningRecord { LearningRecordId = 1, DepartmentId = Dept, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.LmsCourseCompletion, SourceRefId = "enrollment:1", Title = "Airway quiz course", CompletedOn = Now.AddDays(-2), Hours = 1.5m, Status = (int)LearningRecordStatuses.Verified, VerificationCode = "ABCD-2345", CategoryId = 5 },
					new LearningRecord { LearningRecordId = 2, DepartmentId = Dept, UserId = Former, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, SourceRefId = "submission:4", Title = "=HYPERLINK(\"http://evil\")", CompletedOn = Now.AddDays(-50), Hours = 4, Status = (int)LearningRecordStatuses.Verified },
					new LearningRecord { LearningRecordId = 3, DepartmentId = Dept, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.ClassSession, SourceRefId = "calendar:12", Title = "Ladder drill", CompletedOn = Now.AddDays(-10), Hours = 3, Status = (int)LearningRecordStatuses.Voided, VoidReason = "duplicate" },
					new LearningRecord { LearningRecordId = 4, DepartmentId = Dept, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.TrainingRecord, SourceRefId = "log:7", Title = "Pump ops", CompletedOn = Now.AddDays(-9), Hours = 2, Status = (int)LearningRecordStatuses.Pending },
					new LearningRecord { LearningRecordId = 99, DepartmentId = Other, UserId = "FOREIGN-user", Title = "FOREIGN record", CompletedOn = Now, Status = (int)LearningRecordStatuses.Verified }
				},
				Categories = new List<TrainingCategory>
				{
					new TrainingCategory { TrainingCategoryId = 5, DepartmentId = Dept, Name = "Airway", ExternalCode = "NCCP-AIR" },
					new TrainingCategory { TrainingCategoryId = 6, DepartmentId = Other, Name = "FOREIGN category" }
				},
				LessonPlans = new List<LmsLessonPlan> { new LmsLessonPlan { LmsLessonPlanId = 1, DepartmentId = Dept, Title = "Live fire" }, new LmsLessonPlan { LmsLessonPlanId = 9, DepartmentId = Other, Title = "FOREIGN plan" } },
				SkillSheets = new List<LmsSkillSheet> { new LmsSkillSheet { LmsSkillSheetId = 1, DepartmentId = Dept, Title = "SCBA donning" } },
				Requirements = new List<LmsRequirement> { new LmsRequirement { LmsRequirementId = 1, DepartmentId = Dept, Name = "Annual airway" } },
				TrainingPlans = new List<LmsTrainingPlan> { new LmsTrainingPlan { LmsTrainingPlanId = 1, DepartmentId = Dept, Name = "2027 plan", Year = 2027 } },
				Members = new List<LmsExportMember>
				{
					new LmsExportMember { UserId = Admin, FirstName = "Ada", LastName = "Admin", IsCurrent = true, IsActive = true },
					new LmsExportMember { UserId = Member, FirstName = "Mia", LastName = "Member", Email = "mia@example.org", IsCurrent = true, IsActive = true },
					new LmsExportMember { UserId = Former, FirstName = "Fred", LastName = "@Former", IsCurrent = false, IsActive = false }
				}
			};
		}

		public static Dictionary<string, byte[]> Entries(byte[] zip)
		{
			using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
			return archive.Entries.ToDictionary(e => e.FullName, e =>
			{
				using var stream = e.Open();
				using var copy = new MemoryStream();
				stream.CopyTo(copy);
				return copy.ToArray();
			});
		}

		public static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

		/// <summary>A JSON document with its ISO dates kept as the text the file holds.</summary>
		public static Newtonsoft.Json.Linq.JObject ReadJson(string text)
		{
			using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(text)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
			return Newtonsoft.Json.Linq.JObject.Load(reader);
		}

		/// <summary>Data rows of a CSV (header excluded), counting quoted multi-line cells as one row.</summary>
		public static int CsvRows(string csv)
		{
			var rows = 0;
			var quoted = false;
			for (var i = 0; i < csv.Length; i++)
			{
				if (csv[i] == '"')
					quoted = !quoted;
				else if (!quoted && csv[i] == '\n')
					rows++;
			}

			return rows - 1;
		}
	}
}
