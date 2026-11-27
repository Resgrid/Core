using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	// In-memory repositories of the T5 standards tables, with the semantics of the Dapper ones (unique registration per enrollment and
	// lesson, single-use fetch and launch hints, statement id and assertion jti uniqueness, row-version claims, the outbox claim).

	public class FakeLmsContentPackages : MemRepo<LmsContentPackage>, ILmsContentPackagesRepository
	{
		public Task<LmsContentPackage> GetByIdForDepartmentAsync(int departmentId, int lmsContentPackageId) =>
			Task.FromResult(One(p => p.DepartmentId == departmentId && p.LmsContentPackageId == lmsContentPackageId));
		public Task<List<LmsContentPackage>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			Task.FromResult(Out(Rows.Where(p => p.DepartmentId == departmentId && (includeDeleted || !p.IsDeleted))));
		public Task<LmsContentPackage> ClaimNextAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, CancellationToken cancellationToken = default)
		{
			var next = Rows.Where(p => !p.IsDeleted && (p.Status == (int)LmsContentPackageStatuses.Queued || (p.Status == (int)LmsContentPackageStatuses.Processing && p.ClaimedOn < staleClaimBeforeUtc)))
				.OrderBy(p => p.CreatedOn).FirstOrDefault();
			if (next == null)
				return Task.FromResult<LmsContentPackage>(null);
			next.Status = (int)LmsContentPackageStatuses.Processing;
			next.ClaimedOn = utcNow;
			next.Attempts++;
			return Task.FromResult(Copy(next));
		}
		public Task<bool> TryMoveAsync(LmsContentPackage package, LmsContentPackageStatuses from, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(p => p.LmsContentPackageId == package.LmsContentPackageId && p.Status == (int)from);
			if (row == null)
				return Task.FromResult(false);
			row.Status = package.Status;
			row.FileCount = package.FileCount;
			row.ContentBytes = package.ContentBytes;
			row.LastError = package.LastError;
			row.ReadyOn = package.ReadyOn;
			return Task.FromResult(true);
		}
	}

	public class FakeLmsRuntimeRegistrations : MemRepo<LmsRuntimeRegistration>, ILmsRuntimeRegistrationsRepository
	{
		public Task<LmsRuntimeRegistration> GetByIdForDepartmentAsync(int departmentId, int id) => Task.FromResult(One(r => r.DepartmentId == departmentId && r.LmsRuntimeRegistrationId == id));
		public Task<LmsRuntimeRegistration> GetAsync(int departmentId, int lmsEnrollmentId, int lmsLessonId) =>
			Task.FromResult(One(r => r.DepartmentId == departmentId && r.LmsEnrollmentId == lmsEnrollmentId && r.LmsLessonId == lmsLessonId));
		public Task<LmsRuntimeRegistration> GetForLessonSubjectAsync(int departmentId, int lmsLessonId, string registrationKey) =>
			Task.FromResult(Copy(Rows.Where(r => r.DepartmentId == departmentId && r.LmsLessonId == lmsLessonId && r.RegistrationKey == registrationKey && r.Kind == (int)LmsRuntimeKinds.Lti)
				.OrderByDescending(r => r.LmsRuntimeRegistrationId).FirstOrDefault()));
		public async Task<(LmsRuntimeRegistration Registration, bool Inserted)> InsertOrGetAsync(LmsRuntimeRegistration registration, CancellationToken cancellationToken = default)
		{
			var existing = await GetAsync(registration.DepartmentId, registration.LmsEnrollmentId, registration.LmsLessonId);
			if (existing != null)
				return (existing, false);
			await InsertAsync(registration, cancellationToken);
			return (registration, true);
		}
		public Task<bool> TryUpdateAsync(LmsRuntimeRegistration registration, long expectedRowVersion, CancellationToken cancellationToken = default) =>
			TryVersioned(registration, expectedRowVersion, v => registration.RowVersion = v);
	}

	public class FakeLmsRuntimeSessions : MemRepo<LmsRuntimeSession>, ILmsRuntimeSessionsRepository
	{
		public Task<LmsRuntimeSession> GetByTokenHashAsync(string tokenHash) => Task.FromResult(tokenHash == null ? null : One(s => s.TokenHash == tokenHash));
		public Task<LmsRuntimeSession> GetByFetchTokenHashAsync(string fetchTokenHash) => Task.FromResult(fetchTokenHash == null ? null : One(s => s.FetchTokenHash == fetchTokenHash));
		public Task<LmsRuntimeSession> GetByAuthTokenHashAsync(string authTokenHash) => Task.FromResult(authTokenHash == null ? null : One(s => s.AuthTokenHash == authTokenHash));
		public Task<bool> TryMarkFetchedAsync(int id, string authTokenHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(s => s.LmsRuntimeSessionId == id && !s.FetchedOn.HasValue);
			if (row == null) return Task.FromResult(false);
			row.FetchedOn = utcNow;
			row.AuthTokenHash = authTokenHash;
			return Task.FromResult(true);
		}
		public Task<bool> TryMarkUsedAsync(int id, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(s => s.LmsRuntimeSessionId == id && !s.UsedOn.HasValue);
			if (row == null) return Task.FromResult(false);
			row.UsedOn = utcNow;
			return Task.FromResult(true);
		}
		public Task MarkEndedAsync(int id, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(s => s.LmsRuntimeSessionId == id && !s.EndedOn.HasValue);
			if (row != null) row.EndedOn = utcNow;
			return Task.CompletedTask;
		}
		public Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(Rows.RemoveAll(s => s.ExpiresOn < beforeUtc));
		public Task<LmsRuntimeSession> GetLatestForAppSessionAsync(int departmentId, int appSessionId) =>
			Task.FromResult(Copy(Rows.Where(s => s.DepartmentId == departmentId && s.AppSessionId == appSessionId).OrderByDescending(s => s.LmsRuntimeSessionId).FirstOrDefault()));
	}

	public class FakeLmsCmi5Statements : MemRepo<LmsCmi5Statement>, ILmsCmi5StatementsRepository
	{
		public async Task<bool> TryInsertAsync(LmsCmi5Statement statement, CancellationToken cancellationToken = default)
		{
			if (Rows.Any(s => s.LmsRuntimeRegistrationId == statement.LmsRuntimeRegistrationId && s.StatementId == statement.StatementId))
				return false;
			await InsertAsync(statement, cancellationToken);
			return true;
		}
		public Task<LmsCmi5Statement> GetByStatementIdAsync(int departmentId, int registrationId, string statementId) =>
			Task.FromResult(One(s => s.DepartmentId == departmentId && s.LmsRuntimeRegistrationId == registrationId && s.StatementId == statementId));
		public Task<int> CountForRegistrationAsync(int departmentId, int registrationId) => Task.FromResult(Rows.Count(s => s.DepartmentId == departmentId && s.LmsRuntimeRegistrationId == registrationId));
		public Task<List<LmsCmi5Statement>> GetForRegistrationAsync(int departmentId, int registrationId, int take) =>
			Task.FromResult(Out(Rows.Where(s => s.DepartmentId == departmentId && s.LmsRuntimeRegistrationId == registrationId).OrderByDescending(s => s.LmsCmi5StatementId).Take(take)));
	}

	public class FakeLmsCmi5StateDocuments : MemRepo<LmsCmi5StateDocument>, ILmsCmi5StateDocumentsRepository
	{
		public Task<LmsCmi5StateDocument> GetAsync(int departmentId, int registrationId, string activityId, string stateId) =>
			Task.FromResult(One(d => d.DepartmentId == departmentId && d.LmsRuntimeRegistrationId == registrationId && d.ActivityId == activityId && d.StateId == stateId));
		public Task<int> DeleteAsync(int departmentId, int registrationId, string activityId, string stateId, CancellationToken cancellationToken = default) =>
			Task.FromResult(Rows.RemoveAll(d => d.DepartmentId == departmentId && d.LmsRuntimeRegistrationId == registrationId && d.ActivityId == activityId && (stateId == null || d.StateId == stateId)));
	}

	public class FakeLmsLtiTools : MemRepo<LmsLtiTool>, ILmsLtiToolsRepository
	{
		public Task<List<LmsLtiTool>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false) =>
			Task.FromResult(Out(Rows.Where(t => t.DepartmentId == departmentId && (includeDeleted || !t.IsDeleted))));
		public Task<LmsLtiTool> GetByIdForDepartmentAsync(int departmentId, int id) => Task.FromResult(One(t => t.DepartmentId == departmentId && t.LmsLtiToolId == id));
		public Task<LmsLtiTool> GetByClientIdAsync(int departmentId, string clientId) => Task.FromResult(One(t => t.DepartmentId == departmentId && t.ClientId == clientId));
	}

	public class FakeLmsLtiPlatformKeys : MemRepo<LmsLtiPlatformKey>, ILmsLtiPlatformKeysRepository
	{
		public Task<List<LmsLtiPlatformKey>> GetForDepartmentAsync(int departmentId) =>
			Task.FromResult(Out(Rows.Where(k => k.DepartmentId == departmentId).OrderByDescending(k => k.LmsLtiPlatformKeyId)));
		public Task<int> RetireOthersAsync(int departmentId, int keepKeyId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(k => k.DepartmentId == departmentId && k.Status == (int)LmsLtiKeyStatuses.Active && k.LmsLtiPlatformKeyId != keepKeyId).ToList();
			foreach (var row in rows)
			{
				row.Status = (int)LmsLtiKeyStatuses.Retired;
				row.RetiredOn = utcNow;
			}
			return Task.FromResult(rows.Count);
		}
	}

	public class FakeLmsLtiAccessTokens : MemRepo<LmsLtiAccessToken>, ILmsLtiAccessTokensRepository
	{
		public async Task<bool> TryInsertAsync(LmsLtiAccessToken token, CancellationToken cancellationToken = default)
		{
			if (Rows.Any(t => t.LmsLtiToolId == token.LmsLtiToolId && t.AssertionJti == token.AssertionJti))
				return false;
			await InsertAsync(token, cancellationToken);
			return true;
		}
		public Task<LmsLtiAccessToken> GetByHashAsync(int departmentId, string tokenHash) => Task.FromResult(One(t => t.DepartmentId == departmentId && t.TokenHash == tokenHash));
		public Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(Rows.RemoveAll(t => t.ExpiresOn < beforeUtc));
	}

	public class FakeLmsXapiOutbox : MemRepo<LmsXapiOutboxItem>, ILmsXapiOutboxRepository
	{
		public Task<List<LmsXapiOutboxItem>> ClaimDueAsync(DateTime utcNow, DateTime staleClaimBeforeUtc, int take, CancellationToken cancellationToken = default)
		{
			var due = Rows.Where(i => (i.Status == (int)LmsXapiOutboxStatuses.Pending && i.NextAttemptOn <= utcNow) || (i.Status == (int)LmsXapiOutboxStatuses.Sending && i.ClaimedOn < staleClaimBeforeUtc))
				.OrderBy(i => i.CreatedOn).ThenBy(i => i.LmsXapiOutboxItemId).Take(take).ToList();
			foreach (var item in due)
			{
				item.Status = (int)LmsXapiOutboxStatuses.Sending;
				item.ClaimedOn = utcNow;
				item.Attempts++;
			}
			return Task.FromResult(Out(due));
		}
		public Task<bool> TryFinishAsync(LmsXapiOutboxItem item, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(i => i.LmsXapiOutboxItemId == item.LmsXapiOutboxItemId && i.Status == (int)LmsXapiOutboxStatuses.Sending);
			if (row == null) return Task.FromResult(false);
			row.Status = item.Status;
			row.NextAttemptOn = item.NextAttemptOn;
			row.LastError = item.LastError;
			row.DeliveredOn = item.DeliveredOn;
			return Task.FromResult(true);
		}
		public Task<(int Pending, int Failed, int Delivered)> GetCountsAsync(int departmentId)
		{
			var rows = Rows.Where(i => i.DepartmentId == departmentId).ToList();
			return Task.FromResult((rows.Count(i => i.Status <= 1), rows.Count(i => i.Status == 3), rows.Count(i => i.Status == 2)));
		}
		public Task<List<LmsXapiOutboxItem>> GetRecentFailedAsync(int departmentId, int take) =>
			Task.FromResult(Out(Rows.Where(i => i.DepartmentId == departmentId && i.Status == (int)LmsXapiOutboxStatuses.Failed).Take(take)));
		public Task<int> RequeueFailedAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(i => i.DepartmentId == departmentId && i.Status == (int)LmsXapiOutboxStatuses.Failed).ToList();
			foreach (var row in rows)
			{
				row.Status = (int)LmsXapiOutboxStatuses.Pending;
				row.Attempts = 0;
				row.NextAttemptOn = utcNow;
			}
			return Task.FromResult(rows.Count);
		}
		public Task<int> PurgeDeliveredAsync(DateTime beforeUtc, CancellationToken cancellationToken = default) =>
			Task.FromResult(Rows.RemoveAll(i => i.Status == (int)LmsXapiOutboxStatuses.Delivered && i.DeliveredOn < beforeUtc));
	}

	/// <summary>
	/// The T5 standards services over the T2 LMS world (the real learner service, its gate and lapse rules, the learning-record writer), an
	/// in-memory media store, a reversible fake of the department encryption and a scripted outbound transport (no network).
	/// </summary>
	public sealed class LmsStandardsWorld
	{
		public const int DepartmentId = LmsWorld.DepartmentId;

		public readonly LmsWorld Lms = new LmsWorld();
		public readonly FakeLmsContentPackages Packages = new FakeLmsContentPackages();
		public readonly FakeLmsRuntimeRegistrations Registrations = new FakeLmsRuntimeRegistrations();
		public readonly FakeLmsRuntimeSessions Sessions = new FakeLmsRuntimeSessions();
		public readonly FakeLmsCmi5Statements Statements = new FakeLmsCmi5Statements();
		public readonly FakeLmsCmi5StateDocuments StateDocuments = new FakeLmsCmi5StateDocuments();
		public readonly FakeLmsLtiTools Tools = new FakeLmsLtiTools();
		public readonly FakeLmsLtiPlatformKeys Keys = new FakeLmsLtiPlatformKeys();
		public readonly FakeLmsLtiAccessTokens Tokens = new FakeLmsLtiAccessTokens();
		public readonly FakeLmsXapiOutbox Outbox = new FakeLmsXapiOutbox();

		public readonly Dictionary<string, byte[]> Objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		public readonly Mock<IEncryptionService> Encryption = new Mock<IEncryptionService>();
		public readonly Mock<IDepartmentSettingsService> Settings = new Mock<IDepartmentSettingsService>();
		public readonly Mock<IUserProfileService> Profiles = new Mock<IUserProfileService>();
		public readonly Mock<ILmsStandardsTransport> Transport = new Mock<ILmsStandardsTransport>();
		public readonly Mock<IEventAggregator> Events = new Mock<IEventAggregator>();
		public DepartmentSetting StandardsSetting;

		public readonly LmsContentPackageService PackageService;
		public readonly LmsStandardsRuntimeService Runtime;
		public readonly LmsLtiService Lti;
		public readonly LmsXapiService Xapi;
		public readonly LmsStandardsLessonValidator Validator;

		public LmsStandardsWorld()
		{
			LmsStandardsRuntimeService.ClearCache();
			LmsLtiService.ClearCache();

			Lms.Store.Setup(s => s.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((string key, Stream content, string type, CancellationToken ct) =>
				{
					using var buffer = new MemoryStream();
					content.CopyTo(buffer);
					Objects[key] = buffer.ToArray();
					return Task.CompletedTask;
				});
			Lms.Store.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((string key, CancellationToken ct) => Objects.TryGetValue(key, out var bytes)
					? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
					: Task.FromException<Stream>(new FileNotFoundException(key)));

			Encryption.Setup(e => e.EncryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string plain, int d, string code) => "enc:" + d + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain)));
			Encryption.Setup(e => e.DecryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string cipher, int d, string code) => cipher.StartsWith("enc:" + d + ":", StringComparison.Ordinal)
					? Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(("enc:" + d + ":").Length)))
					: throw new InvalidOperationException("wrong department key"));

			Settings.Setup(s => s.GetSettingByTypeAsync(DepartmentId, DepartmentSettingTypes.LmsStandardsSettings)).ReturnsAsync(() => StandardsSetting);
			Settings.Setup(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), DepartmentSettingTypes.LmsStandardsSettings, It.IsAny<CancellationToken>()))
				.Returns((int d, string value, DepartmentSettingTypes type, CancellationToken ct) =>
				{
					StandardsSetting = new DepartmentSetting { DepartmentId = d, SettingType = (int)type, Setting = value };
					return Task.FromResult(StandardsSetting);
				});

			Profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string userId, bool bypass) => new UserProfile { UserId = userId, FirstName = "Pat", LastName = "Member", MembershipEmail = userId + "@example.test" });

			Xapi = new LmsXapiService(Outbox, Settings.Object, Lms.Departments.Object, Encryption.Object, Profiles.Object, Lms.Courses, Lms.Versions, Lms.Lessons,
				Transport.Object, Events.Object);
			PackageService = new LmsContentPackageService(Lms.Store.Object, Packages, Lms.Owners, Events.Object);
			Runtime = new LmsStandardsRuntimeService(Packages, Registrations, Sessions, Statements, StateDocuments, Lms.Learner, Xapi, Profiles.Object, Lms.Store.Object);
			Lti = new LmsLtiService(Tools, Keys, Tokens, Sessions, Registrations, Lms.Lessons, Lms.Versions, Lms.Courses, Lms.Learner, Xapi, Encryption.Object,
				Lms.Departments.Object, Profiles.Object, Transport.Object, Events.Object);
			Validator = new LmsStandardsLessonValidator(Packages, Tools);
		}

		/// <summary>Turns xAPI on for the department (an LRS endpoint, a key and a stored secret).</summary>
		public void EnableXapi(bool shareEmail = false)
		{
			StandardsSetting = new DepartmentSetting
			{
				DepartmentId = DepartmentId,
				SettingType = (int)DepartmentSettingTypes.LmsStandardsSettings,
				Setting = ObjectSerialization.Serialize(new LmsStandardsSettings
				{
					XapiEnabled = true, LrsEndpoint = "https://lrs.example.test/xapi/", LrsKey = "key", ShareEmailInActor = shareEmail,
					LrsSecretEncrypted = Encryption.Object.EncryptForDepartment("s3cret", DepartmentId, null)
				})
			};
		}

		/// <summary>A zip with the given entries (names as given: a test may write an unsafe one).</summary>
		public static MemoryStream Zip(params (string Name, string Content)[] entries)
		{
			var stream = new MemoryStream();
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
				foreach (var (name, content) in entries)
				{
					using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
					writer.Write(content);
				}
			stream.Position = 0;
			return stream;
		}

		public const string Scorm12Manifest = @"<?xml version=""1.0""?>
<manifest identifier=""com.example.scba"" version=""1"" xmlns=""http://www.imsproject.org/xsd/imscp_rootv1p1p2"" xmlns:adlcp=""http://www.adlnet.org/xsd/adlcp_rootv1p2"">
  <metadata><schema>ADL SCORM</schema><schemaversion>1.2</schemaversion></metadata>
  <organizations default=""org1"">
    <organization identifier=""org1"">
      <title>SCBA Basics</title>
      <item identifier=""item1"" identifierref=""res1""><title>Donning</title><adlcp:masteryscore>80</adlcp:masteryscore><adlcp:datafromlms>start=1</adlcp:datafromlms></item>
    </organization>
  </organizations>
  <resources>
    <resource identifier=""res1"" type=""webcontent"" adlcp:scormtype=""sco"" href=""index.html""><file href=""index.html""/></resource>
    <resource identifier=""res2"" type=""webcontent"" adlcp:scormtype=""asset"" href=""shared/style.css""/>
  </resources>
</manifest>";

		public const string Scorm2004Manifest = @"<?xml version=""1.0""?>
<manifest identifier=""com.example.hazmat"" xmlns=""http://www.imsglobal.org/xsd/imscp_v1p1"" xmlns:adlcp=""http://www.adlnet.org/xsd/adlcp_v1p3"" xmlns:imsss=""http://www.imsglobal.org/xsd/imsss"">
  <metadata><schema>ADL SCORM</schema><schemaversion>2004 4th Edition</schemaversion></metadata>
  <organizations default=""o"">
    <organization identifier=""o"">
      <title>Hazmat Awareness</title>
      <item identifier=""i1"" identifierref=""r1""><title>Part 1</title>
        <imsss:sequencing><imsss:objectives><imsss:primaryObjective objectiveID=""p"" satisfiedByMeasure=""true""><imsss:minNormalizedMeasure>0.7</imsss:minNormalizedMeasure></imsss:primaryObjective></imsss:objectives></imsss:sequencing>
      </item>
      <item identifier=""i2"" identifierref=""r2"" parameters=""?part=2""><title>Part 2</title></item>
    </organization>
  </organizations>
  <resources>
    <resource identifier=""r1"" type=""webcontent"" adlcp:scormType=""sco"" href=""part1/index.html""/>
    <resource identifier=""r2"" type=""webcontent"" adlcp:scormType=""sco"" href=""part2/index.html""/>
  </resources>
</manifest>";

		public const string Cmi5Manifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<courseStructure xmlns=""https://w3id.org/xapi/profiles/cmi5/v1/CourseStructure.xsd"">
  <course id=""https://example.test/course/ems-1""><title><langstring lang=""en-US"">EMS Refresher</langstring></title><description><langstring lang=""en-US"">d</langstring></description></course>
  <au id=""https://example.test/au/1"" moveOn=""CompletedAndPassed"" masteryScore=""0.8"" launchMethod=""AnyWindow"">
    <title><langstring lang=""en-US"">Airway</langstring></title><description><langstring lang=""en-US"">x</langstring></description>
    <url>au1/index.html</url>
  </au>
</courseStructure>";

		public static MemoryStream Scorm12Zip() => Zip(("imsmanifest.xml", Scorm12Manifest), ("index.html", "<html><head><title>SCO</title></head><body>hi</body></html>"), ("shared/style.css", "body{}"));

		public static MemoryStream Scorm2004Zip() => Zip(("imsmanifest.xml", Scorm2004Manifest), ("part1/index.html", "<html><head></head><body>1</body></html>"),
			("part2/index.html", "<html><head></head><body>2</body></html>"));

		public static MemoryStream Cmi5Zip() => Zip(("cmi5.xml", Cmi5Manifest), ("au1/index.html", "<html><head></head><body>au</body></html>"));

		/// <summary>Uploads a package as the LMS administrator and unpacks it (worker 98); returns the ready package.</summary>
		public async Task<LmsContentPackage> ReadyPackageAsync(MemoryStream zip, string fileName = "package.zip")
		{
			var result = await PackageService.UploadAsync(Lms.AdminActor, fileName, zip, zip.Length);
			if (!result.Succeeded)
				throw new InvalidOperationException("upload refused: " + result.Error + " " + (result.Value as LmsPackageInspection)?.Error);
			await PackageService.ProcessQueueAsync(5);
			return Packages.Rows.Single(p => p.LmsContentPackageId == ((LmsContentPackage)result.Value).LmsContentPackageId);
		}

		/// <summary>A published course with a reading, then a standards lesson of <paramref name="kind"/> on <paramref name="packageId"/> or <paramref name="toolId"/>; the member enrolled.</summary>
		public async Task<(LmsEnrollment Enrollment, LmsLesson Lesson, LmsCourse Course)> CourseWithAsync(LmsLessonKinds kind, int? packageId = null, int? toolId = null, int passMark = 0,
			bool readingFirst = false)
		{
			var lessons = new List<LmsLesson>();
			if (readingFirst)
				lessons.Add(LmsWorld.Reading("Intro"));
			lessons.Add(new LmsLesson { Kind = (int)kind, Title = "Standards lesson", IsRequired = true, LmsContentPackageId = packageId, LmsLtiToolId = toolId, PassMark = passMark });
			var (course, _, created) = await Lms.PublishedCourseAsync("Standards course", lessons.ToArray());
			var enrolled = await Lms.Learner.EnrollSelfAsync(Lms.Actor(LmsWorld.Member), course.LmsCourseId);
			if (!enrolled.Succeeded)
				throw new InvalidOperationException("enrol refused: " + enrolled.Error);
			return (enrolled.Enrollment, created.Last(), course);
		}

		/// <summary>The token in a /lms-content/{token}/... URL.</summary>
		public static string TokenOf(string url) => url.Split('/')[2];
	}
}
