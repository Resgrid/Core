using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;

namespace Resgrid.Services
{
	/// <summary>
	/// Writes a department's data exit zip (R-C12) from a loaded snapshot: documented JSON for courses (versions, modules, lessons and their
	/// blocks), question banks with keys and programs; CSV for members, categories, learning records, enrollments, attempts and certificates;
	/// xAPI statements as JSON lines; hosted media and SCORM 1.2 packages; and manifest.json with the count of every entity and the SHA-256
	/// of every file. Every list is filtered to the snapshot's department again (defence in depth over the department-scoped reads). The
	/// format is documented in docs/training-data-export.md.
	/// </summary>
	public static class LmsDataExportWriter
	{
		private static readonly JsonSerializer Camel = JsonSerializer.Create(new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			NullValueHandling = NullValueHandling.Include,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc
		});

		private static readonly HashSet<string> EntityPlumbing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "idValue", "tableName", "idName", "idType", "ignoredProperties" };

		private static string EnumName<TEnum>(int value) where TEnum : struct, Enum =>
			Enum.IsDefined(typeof(TEnum), value) ? ((TEnum)(object)value).ToString() : value.ToString(CultureInfo.InvariantCulture);

		private static JToken ParseJson(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return JValue.CreateNull();
			try
			{
				return JToken.Parse(json);
			}
			catch (JsonException)
			{
				return new JValue(json);
			}
		}

		/// <summary>An entity as camel-case JSON without the ORM's plumbing; stored JSON text (a property ending in "Json") is emitted parsed.</summary>
		public static JObject EntityJson(object entity)
		{
			var json = JObject.FromObject(entity, Camel);
			foreach (var property in json.Properties().ToList())
			{
				if (EntityPlumbing.Contains(property.Name))
					property.Remove();
				else if (property.Name.EndsWith("Json", StringComparison.Ordinal) && property.Value.Type == JTokenType.String)
					property.Value = ParseJson((string)property.Value);
			}

			return json;
		}

		/// <summary>Every date in the document as ISO 8601 UTC text ("2026-11-25T14:30:00Z"); stored times are UTC.</summary>
		public static JToken NormalizeDates(JToken token)
		{
			var values = token is JContainer container ? container.DescendantsAndSelf().OfType<JValue>() : new[] { token }.OfType<JValue>();
			foreach (var value in values.Where(v => v.Type == JTokenType.Date).ToList())
			{
				var text = LmsExportCsv.Date(value.Value is DateTimeOffset offset ? offset.UtcDateTime : (DateTime)value.Value);
				if (value.Parent == null)
					return new JValue(text);
				value.Replace(new JValue(text));
			}

			return token;
		}

		private static string Csv(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows) => LmsExportCsv.Write(header, rows);

		private sealed class Writer
		{
			public ZipArchive Archive { get; set; }
			public LmsDataExportManifest Manifest { get; set; }

			public void Text(string path, string text, int? rows, bool listed = true)
			{
				var bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
				var entry = Archive.CreateEntry(path, CompressionLevel.Optimal);
				using (var stream = entry.Open())
					stream.Write(bytes, 0, bytes.Length);
				if (listed)
					Manifest.Files.Add(new LmsDataExportFileEntry { Path = path, Rows = rows, Bytes = bytes.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
			}

			/// <summary>manifest.json itself: written last and not listed in its own file list.</summary>
			public void WriteManifest(JsonSerializer serializer) => Text("manifest.json", NormalizeDates(JObject.FromObject(Manifest, serializer)).ToString(Formatting.Indented) + "\n", null, false);

			public void Json(string path, JToken token, int? rows) => Text(path, NormalizeDates(token).ToString(Formatting.Indented) + "\n", rows);

			public async Task<bool> CopyAsync(string path, Func<Task<Stream>> open, CancellationToken cancellationToken)
			{
				await using var source = await open();
				if (source == null)
					return false;
				var entry = Archive.CreateEntry(path, CompressionLevel.NoCompression);
				using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
				long length = 0;
				await using (var target = entry.Open())
				{
					var buffer = new byte[81920];
					int read;
					while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
					{
						await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
						hash.AppendData(buffer, 0, read);
						length += read;
					}
				}

				Manifest.Files.Add(new LmsDataExportFileEntry { Path = path, Bytes = length, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() });
				return true;
			}
		}

		/// <summary>
		/// Writes the zip into <paramref name="output"/> and returns its manifest. <paramref name="openMedia"/> opens a store object (hosted
		/// media marked for copying in <see cref="LmsDataExportSnapshot.Media"/>, caption tracks); null when the install has no store.
		/// </summary>
		public static async Task<LmsDataExportManifest> WriteAsync(Stream output, LmsDataExportSnapshot snapshot, Func<string, CancellationToken, Task<Stream>> openMedia,
			CancellationToken cancellationToken = default)
		{
			var department = snapshot.DepartmentId;
			var full = snapshot.Scope == LmsDataExportScopes.Full;
			var manifest = new LmsDataExportManifest
			{
				ExportId = snapshot.ExportId,
				DepartmentId = department,
				DepartmentName = snapshot.DepartmentName,
				Scope = snapshot.Scope.ToString(),
				GeneratedOnUtc = snapshot.GeneratedOnUtc,
				RequestedByUserId = snapshot.RequestedByUserId
			};

			// Department scoping, again: whatever the reads returned, only this department's rows reach the zip.
			List<T> Mine<T>(IEnumerable<T> rows, Func<T, int> departmentOf) => (rows ?? Enumerable.Empty<T>()).Where(r => r != null && departmentOf(r) == department).ToList();

			var records = Mine(snapshot.LearningRecords, r => r.DepartmentId);
			var attempts = Mine(snapshot.Attempts, r => r.DepartmentId);
			var enrollments = Mine(snapshot.Enrollments, r => r.DepartmentId);
			var categories = Mine(snapshot.Categories, r => r.DepartmentId);
			var courses = Mine(snapshot.Courses, r => r.DepartmentId).Where(c => c.PublisherKind == (int)LmsPublisherKinds.Department).ToList();
			var versions = Mine(snapshot.Versions, r => r.DepartmentId);
			var modules = Mine(snapshot.Modules, r => r.DepartmentId);
			var lessons = Mine(snapshot.Lessons, r => r.DepartmentId);
			var audiences = Mine(snapshot.CourseAudiences, r => r.DepartmentId);
			var banks = Mine(snapshot.Banks, r => r.DepartmentId);
			var questions = Mine(snapshot.Questions, r => r.DepartmentId);
			var members = (snapshot.Members ?? new List<LmsExportMember>()).Where(m => m?.UserId != null)
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(m => m.LastName).ThenBy(m => m.FirstName).ThenBy(m => m.UserId).ToList();
			var names = members.ToDictionary(m => m.UserId, m => m.Name, StringComparer.OrdinalIgnoreCase);
			string NameOf(string userId) => userId != null && names.TryGetValue(userId, out var name) ? name : string.Empty;
			var categoryNames = categories.ToDictionary(c => c.TrainingCategoryId, c => c.Name);
			string CategoryOf(int? id) => id.HasValue && categoryNames.TryGetValue(id.Value, out var name) ? name : string.Empty;
			var lessonTitles = lessons.ToDictionary(l => l.LmsLessonId, l => l.Title);
			// Academy courses (DepartmentId 0) are not exported as content, but the department's enrollments on them are named.
			var courseTitles = (snapshot.Courses ?? new List<LmsCourse>()).Where(c => c != null && (c.DepartmentId == department || c.DepartmentId == 0))
				.GroupBy(c => c.LmsCourseId).ToDictionary(g => g.Key, g => g.First().Title);

			using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
			var writer = new Writer { Archive = archive, Manifest = manifest };

			writer.Text("README.txt", Readme(snapshot, manifest), null);

			// ---- Records (both scopes) ----------------------------------------------------------------------------------------------
			writer.Text("records/members.csv", Csv(new[] { "user_id", "first_name", "last_name", "email", "identification_number", "is_current_member", "is_active_member" },
				members.Select(m => new[] { m.UserId, m.FirstName, m.LastName, m.Email, m.IdentificationNumber, LmsExportCsv.Flag(m.IsCurrent), LmsExportCsv.Flag(m.IsActive) })), members.Count);
			manifest.Counts["members"] = members.Count;

			writer.Text("records/categories.csv", Csv(new[] { "category_id", "name", "code", "kind", "external_code", "target_hours", "target_period", "retention_years", "is_deleted" },
				categories.Select(c => new[]
				{
					LmsExportCsv.Number(c.TrainingCategoryId), c.Name, c.Code, EnumName<TrainingCategoryKinds>(c.Kind), c.ExternalCode, LmsExportCsv.Number(c.TargetHours),
					EnumName<TrainingTargetPeriods>(c.TargetPeriod), LmsExportCsv.Number(c.RetentionYears), LmsExportCsv.Flag(c.IsDeleted)
				})), categories.Count);
			manifest.Counts["categories"] = categories.Count;

			writer.Text("records/learning-records.csv", Csv(new[]
			{
				"learning_record_id", "user_id", "member_name", "source_kind", "source_ref_id", "component_key", "session_key", "title", "course_code", "completed_on", "started_on",
				"ended_on", "hours", "category_id", "category_name", "method_of_training", "objectives", "location", "ce_provider", "ce_course_number", "ce_hours",
				"instructor_user_ids", "instructor_names", "status", "supersedes_id", "void_reason", "voided_on", "voided_by_user_id", "verified_by_user_id", "verified_on",
				"review_note", "is_backfill", "external_id", "notes", "tags", "verification_code", "created_on", "created_by_user_id",
				// Round 7B: a completion recorded for the member by an instructor (the recording id points into recorded-completions)
				"completion_method", "recorded_by_user_id", "recorded_on", "recorded_completion_id"
			}, records.Select(r => new[]
			{
				LmsExportCsv.Number(r.LearningRecordId), r.UserId, NameOf(r.UserId), EnumName<LearningRecordSourceKinds>(r.SourceKind), r.SourceRefId, r.ComponentKey, r.SessionKey,
				r.Title, r.CourseCode, LmsExportCsv.Date(r.CompletedOn), LmsExportCsv.Date(r.StartedOn), LmsExportCsv.Date(r.EndedOn), LmsExportCsv.Number(r.Hours),
				LmsExportCsv.Number(r.CategoryId), CategoryOf(r.CategoryId), r.MethodOfTraining.HasValue ? EnumName<MethodsOfTraining>(r.MethodOfTraining.Value) : string.Empty,
				r.Objectives, r.Location, r.CeProvider, r.CeCourseNumber, LmsExportCsv.Number(r.CeHours), r.InstructorUserIds, r.InstructorNames,
				EnumName<LearningRecordStatuses>(r.Status), LmsExportCsv.Number(r.SupersedesId), r.VoidReason, LmsExportCsv.Date(r.VoidedOn), r.VoidedByUserId, r.VerifiedByUserId,
				LmsExportCsv.Date(r.VerifiedOn), r.ReviewNote, LmsExportCsv.Flag(r.IsBackfill), r.ExternalId, r.Notes, r.Tags, r.VerificationCode, LmsExportCsv.Date(r.CreatedOn),
				r.CreatedByUserId, EnumName<LearningCompletionMethods>(r.CompletionMethod), r.RecordedByUserId, LmsExportCsv.Date(r.RecordedOn), LmsExportCsv.Number(r.RecordedCompletionId)
			})), records.Count);
			manifest.Counts["learningRecords"] = records.Count;

			writer.Text("records/enrollments.csv", Csv(new[]
			{
				"enrollment_id", "course_id", "course_version_id", "course_title", "user_id", "member_name", "source", "role_path_key", "assigned_by_user_id", "assigned_on", "due_on",
				"status", "started_on", "completed_on", "tested_out", "progress_percent", "seat_seconds", "learning_record_id", "personnel_certification_id", "withdrawn_on",
				"withdrawn_by_user_id"
			}, enrollments.Select(e => new[]
			{
				LmsExportCsv.Number(e.LmsEnrollmentId), LmsExportCsv.Number(e.LmsCourseId), LmsExportCsv.Number(e.LmsCourseVersionId),
				courseTitles.TryGetValue(e.LmsCourseId, out var title) ? title : string.Empty, e.UserId, NameOf(e.UserId), EnumName<LmsEnrollmentSources>(e.Source), e.RolePathKey,
				e.AssignedByUserId, LmsExportCsv.Date(e.AssignedOn), LmsExportCsv.Date(e.DueOn), EnumName<LmsEnrollmentStatuses>(e.Status), LmsExportCsv.Date(e.StartedOn),
				LmsExportCsv.Date(e.CompletedOn), LmsExportCsv.Flag(e.TestedOut), LmsExportCsv.Number(e.ProgressPercent), LmsExportCsv.Number(e.SeatSeconds),
				LmsExportCsv.Number(e.LearningRecordId), LmsExportCsv.Number(e.PersonnelCertificationId), LmsExportCsv.Date(e.WithdrawnOn), e.WithdrawnByUserId
			})), enrollments.Count);
			manifest.Counts["enrollments"] = enrollments.Count;

			// Scores only in the CSV: the answers and the drawn questions with their keys are in the full scope's JSON.
			writer.Text("records/assessment-attempts.csv", Csv(new[]
			{
				"attempt_id", "course_version_id", "lesson_id", "lesson_title", "enrollment_id", "user_id", "member_name", "attempt_number", "status", "started_on", "deadline_on",
				"submitted_on", "score", "max_score", "percent", "passed", "is_pre_test", "pass_mark", "revision_hash", "graded_by_user_id", "graded_on", "attested"
			}, attempts.Select(a => new[]
			{
				LmsExportCsv.Number(a.LmsAssessmentAttemptId), LmsExportCsv.Number(a.LmsCourseVersionId), LmsExportCsv.Number(a.LmsLessonId),
				lessonTitles.TryGetValue(a.LmsLessonId, out var lessonTitle) ? lessonTitle : string.Empty, LmsExportCsv.Number(a.LmsEnrollmentId), a.UserId, NameOf(a.UserId),
				LmsExportCsv.Number(a.AttemptNumber), EnumName<LmsAttemptStatuses>(a.Status), LmsExportCsv.Date(a.StartedOn), LmsExportCsv.Date(a.DeadlineOn),
				LmsExportCsv.Date(a.SubmittedOn), LmsExportCsv.Number(a.Score), LmsExportCsv.Number(a.MaxScore), LmsExportCsv.Number(a.Percent),
				a.Passed.HasValue ? LmsExportCsv.Flag(a.Passed.Value) : string.Empty, LmsExportCsv.Flag(a.IsPreTest), LmsExportCsv.Number(a.PassMark), a.RevisionHash,
				a.GradedByUserId, LmsExportCsv.Date(a.GradedOn), LmsExportCsv.Flag(a.Attested)
			})), attempts.Count);
			manifest.Counts["assessmentAttempts"] = attempts.Count;

			var certificates = records.Where(r => !string.IsNullOrWhiteSpace(r.VerificationCode)).ToList();
			var home = LmsXapiStatements.HomePage(snapshot.BaseUrl);
			writer.Text("records/certificates.csv", Csv(new[] { "learning_record_id", "user_id", "member_name", "title", "completed_on", "hours", "status", "verification_code", "verification_url" },
				certificates.Select(r => new[]
				{
					LmsExportCsv.Number(r.LearningRecordId), r.UserId, NameOf(r.UserId), r.Title, LmsExportCsv.Date(r.CompletedOn), LmsExportCsv.Number(r.Hours),
					EnumName<LearningRecordStatuses>(r.Status), r.VerificationCode, home + "/verify/certificate/" + Uri.EscapeDataString(r.VerificationCode.Trim())
				})), certificates.Count);
			manifest.Counts["certificates"] = certificates.Count;

			// xAPI statements: completions of verified records and every submitted attempt (never answers).
			var courseOfRecord = enrollments.Where(e => e.LearningRecordId.HasValue).GroupBy(e => e.LearningRecordId.Value).ToDictionary(g => g.Key, g => g.First().LmsCourseId);
			var statements = new StringBuilder();
			var statementCount = 0;
			foreach (var record in records)
			{
				string activityId, activityType = LmsXapiStatements.ActivityCourse;
				if (courseOfRecord.TryGetValue(record.LearningRecordId, out var courseId))
					activityId = LmsXapiStatements.ActivityId(home, department, "course", courseId.ToString(CultureInfo.InvariantCulture));
				else
				{
					if (record.SourceKind == (int)LearningRecordSourceKinds.ClassSession || record.SourceKind == (int)LearningRecordSourceKinds.CalendarAttendance)
						activityType = LmsXapiStatements.ActivityMeeting;
					activityId = LmsXapiStatements.ActivityId(home, department, LmsDataExitRules.Slug(EnumName<LearningRecordSourceKinds>(record.SourceKind), "record"),
						string.IsNullOrWhiteSpace(record.SourceRefId) ? record.LearningRecordId.ToString(CultureInfo.InvariantCulture) : record.SourceRefId);
				}

				var line = LmsXapiStatements.ToJsonLine(LmsXapiStatements.ForLearningRecord(record, NameOf(record.UserId), home, activityId, activityType, CategoryOf(record.CategoryId)));
				if (line == null)
					continue;
				statements.Append(line).Append('\n');
				statementCount++;
			}

			foreach (var attempt in attempts)
			{
				var line = LmsXapiStatements.ToJsonLine(LmsXapiStatements.ForAttempt(attempt, NameOf(attempt.UserId), home,
					lessonTitles.TryGetValue(attempt.LmsLessonId, out var title) ? title : null));
				if (line == null)
					continue;
				statements.Append(line).Append('\n');
				statementCount++;
			}

			writer.Text("records/xapi-statements.jsonl", statements.ToString(), statementCount);
			manifest.Counts["xapiStatements"] = statementCount;

			if (!full)
			{
				manifest.Omitted.AddRange(new[] { "courses", "question-banks", "assessment-answers", "programs", "media", "scorm" });
				writer.WriteManifest(Camel);
				return manifest;
			}

			// ---- Full scope: attempts with answers and keys, courses, banks, programs, media, SCORM ------------------------------------
			var attemptDetails = new JArray(attempts.Select(a => new JObject
			{
				["attemptId"] = a.LmsAssessmentAttemptId,
				["userId"] = a.UserId,
				["snapshot"] = ParseJson(a.SnapshotJson),
				["answers"] = ParseJson(a.AnswersJson),
				["results"] = ParseJson(a.ResultsJson),
				["gradeNotes"] = a.GradeNotes,
				["ipAddress"] = a.IpAddress,
				["userAgent"] = a.UserAgent
			}));
			writer.Json("records/assessment-attempts.json", new JObject { ["attempts"] = attemptDetails }, attempts.Count);

			var media = snapshot.Media ?? new Dictionary<int, LmsExportMediaFile>();
			foreach (var file in media.Values.Where(m => m.ZipPath != null && openMedia != null).OrderBy(m => m.LmsMediaAssetId))
			{
				var copied = false;
				try
				{
					copied = await writer.CopyAsync(file.ZipPath, () => openMedia(file.ObjectKey, cancellationToken), cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Data export " + snapshot.ExportId + ": media " + file.ObjectKey + " could not be copied; it is listed as a link.");
				}

				if (copied)
					manifest.MediaFilesIncluded++;
				else
					file.ZipPath = null;
			}

			manifest.MediaLinksOnly = media.Values.Count(m => m.ZipPath == null);
			manifest.Counts["mediaFiles"] = manifest.MediaFilesIncluded;
			var assets = Mine(snapshot.MediaAssets, r => r.DepartmentId);
			writer.Json("courses/media.json", new JObject
			{
				["assets"] = new JArray(assets.Select(asset =>
				{
					media.TryGetValue(asset.LmsMediaAssetId, out var file);
					var json = new JObject
					{
						["id"] = asset.LmsMediaAssetId,
						["kind"] = EnumName<LmsMediaKinds>(asset.Kind),
						["fileName"] = asset.FileName,
						["contentType"] = asset.ContentType,
						["status"] = EnumName<LmsMediaStatuses>(asset.Status),
						["durationSeconds"] = asset.DurationSeconds,
						["file"] = file?.ZipPath,
						["link"] = file?.ZipPath == null ? file?.SignedUrl : null
					};
					return json;
				}))
			}, assets.Count);
			manifest.Counts["mediaAssets"] = assets.Count;

			var categoryById = categories.ToDictionary(c => c.TrainingCategoryId);
			var courseArray = new JArray();
			foreach (var course in courses.OrderBy(c => c.LmsCourseId))
			{
				var courseVersions = versions.Where(v => v.LmsCourseId == course.LmsCourseId).OrderBy(v => v.Major).ThenBy(v => v.Minor).ThenBy(v => v.LmsCourseVersionId).ToList();
				courseArray.Add(new JObject
				{
					["id"] = course.LmsCourseId,
					["code"] = course.Code,
					["title"] = course.Title,
					["summary"] = course.Summary,
					["tags"] = course.Tags,
					["status"] = EnumName<ProtocolStatuses>(course.Status),
					["isSelfEnrollable"] = course.IsSelfEnrollable,
					["isDeleted"] = course.IsDeleted,
					["currentVersionId"] = course.CurrentVersionId,
					["createdOn"] = course.CreatedOn,
					["createdByUserId"] = course.CreatedByUserId,
					["audience"] = new JArray(audiences.Where(a => a.LmsCourseId == course.LmsCourseId)
						.Select(a => new JObject { ["kind"] = EnumName<ContentAudienceKinds>(a.Kind), ["refId"] = a.RefId })),
					["versions"] = new JArray(courseVersions.Select(v => VersionJson(v, modules, lessons, categoryById, media)))
				});
			}

			writer.Json("courses/courses.json", new JObject { ["format"] = LmsDataExportManifest.FormatName + "/courses", ["formatVersion"] = 1, ["courses"] = courseArray }, courses.Count);
			manifest.Counts["courses"] = courses.Count;
			var courseIds = new HashSet<int>(courses.Select(c => c.LmsCourseId));
			var exportedVersions = versions.Where(v => courseIds.Contains(v.LmsCourseId)).ToList();
			var versionIds = new HashSet<int>(exportedVersions.Select(v => v.LmsCourseVersionId));
			manifest.Counts["courseVersions"] = exportedVersions.Count;
			manifest.Counts["courseModules"] = modules.Count(m => versionIds.Contains(m.LmsCourseVersionId));
			manifest.Counts["lessons"] = lessons.Count(l => versionIds.Contains(l.LmsCourseVersionId));

			var bankArray = new JArray(banks.Where(b => string.IsNullOrEmpty(b.SystemKey)).OrderBy(b => b.LmsQuestionBankId).Select(bank => new JObject
			{
				["id"] = bank.LmsQuestionBankId,
				["name"] = bank.Name,
				["description"] = bank.Description,
				["isShared"] = bank.IsShared,
				["ownerUserId"] = bank.OwnerUserId,
				["tags"] = bank.Tags,
				["isDeleted"] = bank.IsDeleted,
				["createdOn"] = bank.CreatedOn,
				["questions"] = new JArray(questions.Where(q => q.LmsQuestionBankId == bank.LmsQuestionBankId).OrderBy(q => q.SortOrder).ThenBy(q => q.LmsQuestionId).Select(QuestionJson))
			}));
			writer.Json("question-banks/question-banks.json", new JObject { ["format"] = LmsDataExportManifest.FormatName + "/question-banks", ["formatVersion"] = 1, ["banks"] = bankArray }, bankArray.Count);
			manifest.Counts["questionBanks"] = bankArray.Count;
			var bankIds = new HashSet<int>(bankArray.Select(b => (int)b["id"]));
			manifest.Counts["questions"] = questions.Count(q => bankIds.Contains(q.LmsQuestionBankId));

			JArray Entities<T>(IEnumerable<T> rows) => new JArray((rows ?? Enumerable.Empty<T>()).Select(r => (JToken)EntityJson(r)));
			var lessonPlans = Mine(snapshot.LessonPlans, r => r.DepartmentId);
			var lessonPlanVersions = Mine(snapshot.LessonPlanVersions, r => r.DepartmentId);
			writer.Json("programs/lesson-plans.json", new JObject { ["lessonPlans"] = Entities(lessonPlans), ["lessonPlanVersions"] = Entities(lessonPlanVersions) }, lessonPlans.Count);
			manifest.Counts["lessonPlans"] = lessonPlans.Count;
			manifest.Counts["lessonPlanVersions"] = lessonPlanVersions.Count;

			var sheets = Mine(snapshot.SkillSheets, r => r.DepartmentId);
			var sheetVersions = Mine(snapshot.SkillSheetVersions, r => r.DepartmentId);
			var taskBooks = Mine(snapshot.TaskBookDefinitions, r => r.DepartmentId);
			writer.Json("programs/skill-sheets.json", new JObject
			{
				["skillSheets"] = Entities(sheets), ["skillSheetVersions"] = Entities(sheetVersions), ["taskBookDefinitions"] = Entities(taskBooks)
			}, sheets.Count);
			manifest.Counts["skillSheets"] = sheets.Count;
			manifest.Counts["skillSheetVersions"] = sheetVersions.Count;
			manifest.Counts["taskBookDefinitions"] = taskBooks.Count;

			var requirements = Mine(snapshot.Requirements, r => r.DepartmentId);
			var requirementAudiences = Mine(snapshot.RequirementAudiences, r => r.DepartmentId);
			writer.Json("programs/requirements.json", new JObject { ["requirements"] = Entities(requirements), ["requirementAudiences"] = Entities(requirementAudiences) }, requirements.Count);
			manifest.Counts["requirements"] = requirements.Count;

			var plans = Mine(snapshot.TrainingPlans, r => r.DepartmentId);
			var planItems = Mine(snapshot.TrainingPlanItems, r => r.DepartmentId);
			writer.Json("programs/training-plans.json", new JObject { ["trainingPlans"] = Entities(plans), ["trainingPlanItems"] = Entities(planItems) }, plans.Count);
			manifest.Counts["trainingPlans"] = plans.Count;
			manifest.Counts["trainingPlanItems"] = planItems.Count;

			var paths = Mine(snapshot.Paths, r => r.DepartmentId);
			var pathSteps = Mine(snapshot.PathSteps, r => r.DepartmentId);
			writer.Json("programs/learning-paths.json", new JObject { ["paths"] = Entities(paths), ["pathSteps"] = Entities(pathSteps) }, paths.Count);
			manifest.Counts["learningPaths"] = paths.Count;

			// SCORM 1.2: the version in force of each course whose lessons all have a single-SCO form.
			foreach (var course in courses.OrderBy(c => c.LmsCourseId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var version = course.CurrentVersionId.HasValue ? exportedVersions.FirstOrDefault(v => v.LmsCourseVersionId == course.CurrentVersionId.Value) : null;
				if (version == null)
				{
					manifest.Scorm.Add(new LmsDataExportScormEntry { LmsCourseId = course.LmsCourseId, SkippedReason = "no-published-version" });
					continue;
				}

				var versionLessons = lessons.Where(l => l.LmsCourseVersionId == version.LmsCourseVersionId).ToList();
				var refusal = LmsDataExitRules.ScormRefusal(versionLessons);
				if (refusal != null)
				{
					manifest.Scorm.Add(new LmsDataExportScormEntry { LmsCourseId = course.LmsCourseId, LmsCourseVersionId = version.LmsCourseVersionId, SkippedReason = refusal });
					continue;
				}

				var videos = new Dictionary<int, LmsScormPackageWriter.PackageMedia>();
				var captions = new Dictionary<int, List<(LmsScormPackageWriter.PackageMedia, LmsCaptionTrack)>>();
				foreach (var lesson in versionLessons.Where(l => l.Kind == (int)LmsLessonKinds.Video && l.VideoSource == (int)LmsVideoSources.Hosted && l.LmsMediaAssetId.HasValue))
				{
					if (media.TryGetValue(lesson.LmsMediaAssetId.Value, out var file) && file.ZipPath != null)
						videos[lesson.LmsMediaAssetId.Value] = new LmsScormPackageWriter.PackageMedia
						{
							PackagePath = "media/video-" + lesson.LmsMediaAssetId.Value.ToString(CultureInfo.InvariantCulture) + ".mp4", ObjectKey = file.ObjectKey
						};
					var tracks = LmsCaptionTrack.Parse(lesson.CaptionTracksJson).Where(t => !string.IsNullOrWhiteSpace(t.ObjectKey)).ToList();
					if (tracks.Count > 0 && videos.ContainsKey(lesson.LmsMediaAssetId.Value))
						captions[lesson.LmsLessonId] = tracks.Select((t, i) => (new LmsScormPackageWriter.PackageMedia
						{
							PackagePath = "media/captions-" + lesson.LmsLessonId.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture) + ".vtt",
							ObjectKey = t.ObjectKey
						}, t)).ToList();
				}

				var path = "courses/scorm/" + course.LmsCourseId.ToString(CultureInfo.InvariantCulture) + "-" + LmsDataExitRules.Slug(course.Title, "course") + "-scorm12.zip";
				var packageModules = modules.Where(m => m.LmsCourseVersionId == version.LmsCourseVersionId).ToList();
				var temp = Path.Combine(Path.GetTempPath(), "rg-scorm-" + Guid.NewGuid().ToString("N") + ".zip");
				try
				{
					await using (var package = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
						await LmsScormPackageWriter.WriteAsync(package, course, version, packageModules, versionLessons, videos, captions, openMedia, home, cancellationToken);
					await writer.CopyAsync(path, () => Task.FromResult<Stream>(new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous)), cancellationToken);
				}
				finally
				{
					try
					{
						System.IO.File.Delete(temp);
					}
					catch (IOException)
					{
					}
				}

				manifest.Scorm.Add(new LmsDataExportScormEntry { LmsCourseId = course.LmsCourseId, LmsCourseVersionId = version.LmsCourseVersionId, Path = path });
			}

			manifest.Counts["scormPackages"] = manifest.Scorm.Count(s => s.Path != null);
			manifest.Omitted.Add("learner-uploads-and-evidence-files");
			writer.WriteManifest(Camel);
			return manifest;
		}

		private static JObject VersionJson(LmsCourseVersion v, List<LmsCourseModule> modules, List<LmsLesson> lessons, IReadOnlyDictionary<int, TrainingCategory> categories,
			IReadOnlyDictionary<int, LmsExportMediaFile> media)
		{
			return new JObject
			{
				["id"] = v.LmsCourseVersionId,
				["label"] = v.Label,
				["major"] = v.Major,
				["minor"] = v.Minor,
				["status"] = EnumName<ProtocolVersionStatuses>(v.Status),
				["title"] = v.Title,
				["locale"] = v.Locale,
				["hours"] = v.Hours,
				["categoryId"] = v.CategoryId,
				["categoryName"] = v.CategoryId.HasValue && categories.TryGetValue(v.CategoryId.Value, out var category) ? category.Name : null,
				["ceProvider"] = v.CeProvider,
				["ceCourseNumber"] = v.CeCourseNumber,
				["contentHash"] = v.ContentHash,
				["changeSummary"] = v.ChangeSummary,
				["overviewHtml"] = v.ContentHtml,
				["overview"] = ParseJson(v.ContentJson),
				["certificateTemplateId"] = v.CertificateTemplateId,
				["issuesCertificationTypeId"] = v.IssuesCertificationTypeId,
				["majorChangePolicy"] = EnumName<LmsMajorChangePolicies>(v.MajorChangePolicy),
				["effectiveOn"] = v.EffectiveOn,
				["createdOn"] = v.CreatedOn,
				["createdByUserId"] = v.CreatedByUserId,
				["approvedOn"] = v.ApprovedOn,
				["approvedByUserId"] = v.ApprovedByUserId,
				["publishedOn"] = v.PublishedOn,
				["publishedByUserId"] = v.PublishedByUserId,
				["supersededOn"] = v.SupersededOn,
				["retiredOn"] = v.RetiredOn,
				["modules"] = new JArray(modules.Where(m => m.LmsCourseVersionId == v.LmsCourseVersionId).OrderBy(m => m.SortOrder).ThenBy(m => m.LmsCourseModuleId)
					.Select(m => new JObject
					{
						["id"] = m.LmsCourseModuleId,
						["title"] = m.Title,
						["sortOrder"] = m.SortOrder,
						["lessons"] = new JArray(lessons.Where(l => l.LmsCourseModuleId == m.LmsCourseModuleId).OrderBy(l => l.SortOrder).ThenBy(l => l.LmsLessonId)
							.Select(l => LessonJson(l, media)))
					}))
			};
		}

		private static JObject LessonJson(LmsLesson l, IReadOnlyDictionary<int, LmsExportMediaFile> media)
		{
			var lesson = new JObject
			{
				["id"] = l.LmsLessonId,
				["kind"] = EnumName<LmsLessonKinds>(l.Kind),
				["title"] = l.Title,
				["sortOrder"] = l.SortOrder,
				["isRequired"] = l.IsRequired,
				["minimumSeatSeconds"] = l.MinimumSeatSeconds
			};

			switch ((LmsLessonKinds)l.Kind)
			{
				case LmsLessonKinds.Blocks:
					lesson["contentHtml"] = l.ContentHtml;
					lesson["content"] = ParseJson(l.ContentJson);
					lesson["freeResponsePrompt"] = l.FreeResponsePrompt;
					lesson["fileRequestPrompt"] = l.FileRequestPrompt;
					break;
				case LmsLessonKinds.Video:
					LmsExportMediaFile file = null;
					if (l.LmsMediaAssetId.HasValue)
						media?.TryGetValue(l.LmsMediaAssetId.Value, out file);
					lesson["video"] = new JObject
					{
						["source"] = EnumName<LmsVideoSources>(l.VideoSource),
						["url"] = l.VideoUrl,
						["mediaAssetId"] = l.LmsMediaAssetId,
						["file"] = file?.ZipPath,
						["link"] = file?.ZipPath == null ? file?.SignedUrl : null,
						["requiredWatchPercent"] = l.RequiredWatchPercent,
						["captions"] = new JArray(LmsCaptionTrack.Parse(l.CaptionTracksJson).Select(t => new JObject { ["locale"] = t.Locale, ["label"] = t.Label, ["url"] = t.Url, ["objectKey"] = t.ObjectKey })),
						["transcript"] = l.TranscriptText,
						["availableOffline"] = l.AvailableOffline
					};
					break;
				case LmsLessonKinds.Document:
					lesson["document"] = new JObject { ["name"] = l.DocumentName, ["url"] = l.DocumentUrl };
					break;
				case LmsLessonKinds.ProtocolVersion:
					lesson["protocolVersionId"] = l.ProtocolVersionId;
					break;
				case LmsLessonKinds.Assessment:
					lesson["assessment"] = new JObject
					{
						["questionBankId"] = l.LmsQuestionBankId,
						["drawCount"] = l.DrawCount,
						["drawTags"] = l.DrawTags,
						["passMark"] = l.PassMark,
						["attemptLimit"] = l.AttemptLimit,
						["retakeCooldownHours"] = l.RetakeCooldownHours,
						["timeLimitMinutes"] = l.TimeLimitMinutes,
						["showAnswersPolicy"] = EnumName<LmsShowAnswersPolicies>(l.ShowAnswersPolicy),
						["isPreTest"] = l.IsPreTest,
						["randomizeAnswers"] = l.RandomizeAnswers,
						["requireAttestation"] = l.RequireAttestation
					};
					break;
				case LmsLessonKinds.Survey:
					lesson["survey"] = new JArray(LmsSurveyQuestion.Parse(l.SurveyJson).Select(q => new JObject
					{
						["key"] = q.Key, ["kind"] = EnumName<LmsSurveyQuestionKinds>(q.Kind), ["text"] = q.Text, ["options"] = new JArray(q.Options ?? new List<string>())
					}));
					break;
			}

			return lesson;
		}

		private static JObject QuestionJson(LmsQuestion q)
		{
			var options = q.Options;
			var question = new JObject
			{
				["id"] = q.LmsQuestionId,
				["kind"] = EnumName<LmsQuestionKinds>(q.Kind),
				["text"] = q.Text,
				["imageUrl"] = q.ImageUrl,
				["options"] = new JArray(options.Select(o => new JObject { ["key"] = o.Key, ["text"] = o.Text, ["isCorrect"] = o.IsCorrect })),
				["explanation"] = q.Explanation,
				["tags"] = q.Tags,
				["points"] = q.Points,
				["translations"] = ParseJson(q.TranslationsJson),
				["isRetired"] = q.IsRetired,
				["sortOrder"] = q.SortOrder
			};
			if (q.Kind == (int)LmsQuestionKinds.Ordering)
				question["correctOrder"] = new JArray(options.Select(o => o.Key));
			return question;
		}

		private static string Readme(LmsDataExportSnapshot snapshot, LmsDataExportManifest manifest)
		{
			var builder = new StringBuilder();
			builder.Append("Resgrid training data export\r\n\r\n");
			builder.Append("Department: ").Append(snapshot.DepartmentName).Append(" (").Append(snapshot.DepartmentId.ToString(CultureInfo.InvariantCulture)).Append(")\r\n");
			builder.Append("Scope: ").Append(manifest.Scope).Append("\r\n");
			builder.Append("Generated (UTC): ").Append(snapshot.GeneratedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\r\n\r\n");
			builder.Append("manifest.json lists every file with its SHA-256 and the number of each entity this export holds.\r\n");
			builder.Append("The format is documented in docs/training-data-export.md of the Resgrid Core repository.\r\n");
			builder.Append("CSV files are UTF-8 without a byte order mark; times are UTC (ISO 8601). A cell starting with an apostrophe was text\r\n");
			builder.Append("that a spreadsheet would otherwise run as a formula.\r\n");
			if (snapshot.Scope == LmsDataExportScopes.Full)
				builder.Append("\r\nThis export contains question banks WITH THEIR ANSWER KEYS and learners' assessment answers. Keep it private.\r\n");
			return builder.ToString();
		}
	}
}
