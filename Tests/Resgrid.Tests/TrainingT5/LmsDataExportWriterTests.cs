using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Services;
using static Resgrid.Tests.TrainingT5.LmsDataExitFixtures;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Training T5 data exit (R-C12): the zip's completeness (every entity's count equals the department's rows, every file's hash is right),
	/// department scoping, answer keys only in the full export, formula-safe CSV, xAPI statements and SCORM packages.
	/// </summary>
	[TestFixture]
	public class LmsDataExportWriterTests
	{
		private static async Task<(LmsDataExportManifest Manifest, System.Collections.Generic.Dictionary<string, byte[]> Files)> WriteAsync(LmsDataExportSnapshot snapshot)
		{
			using var output = new MemoryStream();
			var manifest = await LmsDataExportWriter.WriteAsync(output, snapshot, null);
			return (manifest, Entries(output.ToArray()));
		}

		[Test]
		public async Task Every_entity_count_equals_the_departments_rows_and_the_files_rows()
		{
			var snapshot = Snapshot();
			var (manifest, files) = await WriteAsync(snapshot);

			int Mine<T>(System.Collections.Generic.IEnumerable<T> rows, Func<T, int> department) => rows.Count(r => department(r) == Dept);
			manifest.Counts["learningRecords"].Should().Be(Mine(snapshot.LearningRecords, r => r.DepartmentId)).And.Be(4, "voided and pending records are evidence too");
			manifest.Counts["assessmentAttempts"].Should().Be(Mine(snapshot.Attempts, r => r.DepartmentId));
			manifest.Counts["enrollments"].Should().Be(Mine(snapshot.Enrollments, r => r.DepartmentId));
			manifest.Counts["certificates"].Should().Be(1);
			manifest.Counts["courses"].Should().Be(3);
			manifest.Counts["courseVersions"].Should().Be(3);
			manifest.Counts["courseModules"].Should().Be(2);
			manifest.Counts["lessons"].Should().Be(4);
			manifest.Counts["questionBanks"].Should().Be(1);
			manifest.Counts["questions"].Should().Be(2);
			manifest.Counts["members"].Should().Be(3);
			manifest.Counts["lessonPlans"].Should().Be(1);
			manifest.Counts["skillSheets"].Should().Be(1);
			manifest.Counts["requirements"].Should().Be(1);
			manifest.Counts["trainingPlans"].Should().Be(1);

			CsvRows(Text(files["records/learning-records.csv"])).Should().Be(manifest.Counts["learningRecords"]);
			CsvRows(Text(files["records/assessment-attempts.csv"])).Should().Be(manifest.Counts["assessmentAttempts"]);
			CsvRows(Text(files["records/enrollments.csv"])).Should().Be(manifest.Counts["enrollments"]);
			CsvRows(Text(files["records/certificates.csv"])).Should().Be(manifest.Counts["certificates"]);
			CsvRows(Text(files["records/members.csv"])).Should().Be(manifest.Counts["members"]);
			((JArray)ReadJson(Text(files["courses/courses.json"]))["courses"]).Should().HaveCount(manifest.Counts["courses"]);

			// Statements: the verified records (1, 2) and the submitted attempts (1, 2); the in-progress attempt and the pending and voided
			// records have none.
			var lines = Text(files["records/xapi-statements.jsonl"]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
			lines.Should().HaveCount(4).And.HaveCount(manifest.Counts["xapiStatements"]);
		}

		[Test]
		public async Task The_manifest_lists_every_file_with_its_real_hash()
		{
			var (manifest, files) = await WriteAsync(Snapshot());

			var listed = manifest.Files.ToDictionary(f => f.Path);
			files.Keys.Where(k => k != "manifest.json").Should().BeEquivalentTo(listed.Keys);
			foreach (var (path, bytes) in files.Where(f => f.Key != "manifest.json"))
			{
				listed[path].Bytes.Should().Be(bytes.LongLength, path);
				listed[path].Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), path);
			}

			var json = ReadJson(Text(files["manifest.json"]));
			((string)json["format"]).Should().Be("resgrid-training-export");
			((int)json["formatVersion"]).Should().Be(1);
			((string)json["generatedOnUtc"]).Should().Be("2026-11-25T15:00:00Z");
		}

		[Test]
		public async Task No_other_departments_row_reaches_the_zip()
		{
			foreach (var scope in new[] { LmsDataExportScopes.Full, LmsDataExportScopes.RecordsOnly })
			{
				var (_, files) = await WriteAsync(Snapshot(scope));
				foreach (var (path, bytes) in files.Where(f => !f.Key.EndsWith(".zip")))
					Text(bytes).Should().NotContain("FOREIGN", path + " in the " + scope + " export");
			}
		}

		[Test]
		public async Task Answer_keys_are_only_in_the_full_export()
		{
			var (_, full) = await WriteAsync(Snapshot(LmsDataExportScopes.Full));
			var banks = ReadJson(Text(full["question-banks/question-banks.json"]));
			var options = banks["banks"][0]["questions"][0]["options"];
			options.Single(o => (bool)o["isCorrect"])["text"].Value<string>().Should().Be(SecretOption);
			banks["banks"][0]["questions"][1]["correctOrder"].Values<string>().Should().Equal("x", "y");
			Text(full["records/assessment-attempts.json"]).Should().Contain(SecretOption, "the full export carries the attempt snapshot with its key");

			var (manifest, records) = await WriteAsync(Snapshot(LmsDataExportScopes.RecordsOnly));
			records.Keys.Should().NotContain(new[] { "question-banks/question-banks.json", "records/assessment-attempts.json", "courses/courses.json" });
			records.Keys.Should().NotContain(k => k.StartsWith("courses/") || k.StartsWith("programs/"));
			foreach (var (path, bytes) in records)
			{
				Text(bytes).Should().NotContain(SecretOption, path);
				Text(bytes).Should().NotContain("isCorrect", path);
			}

			manifest.Omitted.Should().Contain(new[] { "question-banks", "assessment-answers", "courses" });
			manifest.Counts.Keys.Should().NotContain("questionBanks");
		}

		[Test]
		public async Task Csv_cells_a_spreadsheet_would_run_are_written_as_text()
		{
			var (_, files) = await WriteAsync(Snapshot());
			var records = Text(files["records/learning-records.csv"]);
			records.Should().Contain("\"'=HYPERLINK(\"\"http://evil\"\")\"", "a formula title is written as quoted text with a leading apostrophe");
			records.Should().NotContain(",=HYPERLINK");
			Text(files["records/members.csv"]).Should().Contain(",'@Former,", "a leading @ is a formula prefix too");

			LmsExportCsv.Cell("-12.5").Should().Be("-12.5", "a plain negative number stays a number");
			LmsExportCsv.Cell("+1 555 0100").Should().Be("'+1 555 0100");
			LmsExportCsv.Cell("\tcmd").Should().Be("'\tcmd");
		}

		[Test]
		public async Task Xapi_statements_follow_the_specification_and_keep_their_ids()
		{
			var (_, first) = await WriteAsync(Snapshot());
			var (_, second) = await WriteAsync(Snapshot());
			var lines = Text(first["records/xapi-statements.jsonl"]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
			lines.Should().Equal(Text(second["records/xapi-statements.jsonl"]).Split('\n', StringSplitOptions.RemoveEmptyEntries), "statement ids are deterministic");

			var statements = lines.Select(ReadJson).ToList();
			foreach (var statement in statements)
			{
				Guid.TryParse((string)statement["id"], out var id).Should().BeTrue();
				((string)statement["id"]).Substring(14, 1).Should().Be("5", "a name-based version 5 UUID");
				((string)statement["version"]).Should().Be("1.0.3");
				((string)statement["actor"]["account"]["homePage"]).Should().Be("https://resgrid.example");
				((string)statement["timestamp"]).Should().EndWith("Z");
				statement.ToString().Should().NotContain(SecretOption, "answers never go into a statement");
			}

			var completion = statements.Single(s => (string)s["context"]["extensions"]["https://resgrid.com/xapi/extensions/learning-record-id"] == "1");
			((string)completion["verb"]["id"]).Should().Be(LmsXapiStatements.VerbCompleted);
			((string)completion["object"]["id"]).Should().Be("https://resgrid.example/xapi/activities/department/501/course/2", "a course completion names the course");
			((string)completion["result"]["duration"]).Should().Be("PT1H30M");
			((string)completion["actor"]["name"]).Should().Be("Mia Member");

			var failed = statements.Single(s => (string)s["context"]["extensions"]["https://resgrid.com/xapi/extensions/attempt-id"] == "1");
			((string)failed["verb"]["id"]).Should().Be(LmsXapiStatements.VerbFailed);
			((decimal)failed["result"]["score"]["scaled"]).Should().Be(0.5m);
			((bool)failed["result"]["success"]).Should().BeFalse();
			statements.Count(s => (string)s["verb"]["id"] == LmsXapiStatements.VerbPassed).Should().Be(1);

			LmsXapiStatements.StatementId("learning-record:501:1").Should().Be(LmsXapiStatements.StatementId("learning-record:501:1"));
			LmsXapiStatements.StatementId("learning-record:501:1").Should().NotBe(LmsXapiStatements.StatementId("learning-record:502:1"));
			LmsXapiStatements.Duration(0.25m).Should().Be("PT15M");
			LmsXapiStatements.Duration(2m).Should().Be("PT2H");
		}

		[Test]
		public async Task A_scorm_package_is_written_where_the_lesson_kinds_allow()
		{
			var (manifest, files) = await WriteAsync(Snapshot());

			manifest.Scorm.Single(s => s.LmsCourseId == 2).SkippedReason.Should().Be("lesson-kind:Assessment");
			manifest.Scorm.Single(s => s.LmsCourseId == 3).SkippedReason.Should().Be("no-published-version");
			var packaged = manifest.Scorm.Single(s => s.LmsCourseId == 1);
			packaged.Path.Should().Be("courses/scorm/1-hose-handling-scorm12.zip");
			manifest.Counts["scormPackages"].Should().Be(1);

			var package = Entries(files[packaged.Path]);
			package.Keys.Should().BeEquivalentTo(new[] { "imsmanifest.xml", "index.html", "scorm.js", "course.css" });
			var imsmanifest = Text(package["imsmanifest.xml"]);
			imsmanifest.Should().Contain("<schemaversion>1.2</schemaversion>").And.Contain("adlcp:scormtype=\"sco\"").And.Contain("href=\"index.html\"");
			System.Xml.Linq.XDocument.Parse(imsmanifest).Root.Name.LocalName.Should().Be("manifest");
			var page = Text(package["index.html"]);
			page.Should().Contain("Hand-tight plus a quarter turn.").And.Contain("https://docs.example/sog.pdf").And.Contain("https://www.youtube.com/embed/abc");
			page.Should().Contain("Mark this course complete");
			Text(package["scorm.js"]).Should().Contain("LMSInitialize").And.Contain("cmi.core.lesson_status").And.Contain("LMSFinish");
		}

		[Test]
		public void The_scorm_rule_accepts_blocks_videos_and_documents_only()
		{
			LmsLesson Lesson(LmsLessonKinds kind) => new LmsLesson { Kind = (int)kind };
			LmsDataExitRules.ScormRefusal(new[] { Lesson(LmsLessonKinds.Blocks), Lesson(LmsLessonKinds.Video), Lesson(LmsLessonKinds.Document) }).Should().BeNull();
			LmsDataExitRules.ScormRefusal(new[] { Lesson(LmsLessonKinds.Blocks), Lesson(LmsLessonKinds.Survey) }).Should().Be("lesson-kind:Survey");
			LmsDataExitRules.ScormRefusal(new[] { Lesson(LmsLessonKinds.ProtocolVersion) }).Should().Be("lesson-kind:ProtocolVersion");
			LmsDataExitRules.ScormRefusal(Array.Empty<LmsLesson>()).Should().Be("no-lessons");
		}

		[Test]
		public async Task Course_json_documents_versions_modules_lessons_and_blocks()
		{
			var (_, files) = await WriteAsync(Snapshot());
			var course = ReadJson(Text(files["courses/courses.json"]))["courses"].First(c => (int)c["id"] == 1);
			((string)course["createdOn"]).Should().EndWith("Z");
			var version = course["versions"].Single();
			((string)version["label"]).Should().Be("1.0");
			((string)version["status"]).Should().Be(((ProtocolVersionStatuses)2).ToString());
			var lessons = version["modules"].Single()["lessons"];
			lessons.Select(l => (string)l["kind"]).Should().Equal("Blocks", "Document", "Video");
			((int)lessons[0]["content"]["schema"]).Should().Be(2, "the block document is emitted parsed");
			((string)lessons[1]["document"]["url"]).Should().Be("https://docs.example/sog.pdf");
			((string)lessons[2]["video"]["source"]).Should().Be("Embed");
		}
	}
}
