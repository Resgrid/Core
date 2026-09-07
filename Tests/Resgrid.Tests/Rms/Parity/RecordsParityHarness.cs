using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms.Parity
{
	/// <summary>
	/// Replays a golden fixture against the real Records aggregate (RMS plan section 6, RMS-0: "the assertion
	/// harness that replays them against Records"). The legacy row is mapped through <see cref="LegacyFieldMap"/>
	/// into a draft, attachments are added, the Quick Entry finalize runs, and the four projections the plan
	/// names — list, detail, print and export — are captured for the test to assert against.
	/// </summary>
	public sealed class RecordsParityHarness
	{
		public const int Dept = 9;
		public const string Author = "author";
		public const string Clerk = "clerk";

		private static readonly JsonSerializerSettings FixtureSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
		private static readonly JsonSerializerSettings ActualSettings = new JsonSerializerSettings
		{
			DateFormatHandling = DateFormatHandling.IsoDateFormat, DateTimeZoneHandling = DateTimeZoneHandling.Utc, NullValueHandling = NullValueHandling.Include,
			Culture = CultureInfo.InvariantCulture, ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		};

		public FakeIncidentStore IncidentStore { get; }
		public FakeRmsStore Store { get; }
		public RecordsService Records { get; }
		public RecordsDocumentService Documents { get; }
		public Mock<IRecordsAuthorizationService> Authorization { get; }

		public RecordsParityHarness()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			IncidentStore = new FakeIncidentStore();
			Store = IncidentStore.Shared;

			Authorization = new Mock<IRecordsAuthorizationService>();
			Authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			Authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			Authorization.Setup(a => a.HasPermissionAsync(Clerk, Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			Authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			Authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync(true);
			Authorization.Setup(a => a.IsDepartmentAdminAsync(It.IsAny<string>(), Dept)).ReturnsAsync(false);

			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsNumberingConfig());
			settings.Setup(s => s.GetRecordsReviewDueHoursAsync(Dept, It.IsAny<bool>())).ReturnsAsync(72);
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, Name = "Station 1" });
			groups.Setup(g => g.GetGroupForUserAsync("p3", Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 12, Name = "Station 2" });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "Member", LastName = id });
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5", Type = "Engine", StationGroupId = 11 });
			units.Setup(u => u.GetUnitByIdAsync(6)).ReturnsAsync(new Unit { UnitId = 6, DepartmentId = Dept, Name = "Ladder 6", Type = "Ladder", StationGroupId = 12 });
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 77, DepartmentId = Dept, Number = "C2026-0009", Name = "Structure fire", Type = "Fire", Priority = 3, LoggedOn = new DateTime(2026, 3, 4, 17, 42, 0, DateTimeKind.Utc), Address = "1 Main St", NatureOfCall = "Smoke showing" });
			var adp = new Mock<IDepartmentDataProtectionService>();
			adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(0);
			var outbox = new DomainEventOutboxService(Store.OutboxRepo.Object, Mock.Of<IEventAggregator>());
			var queue = new Mock<IOutboundQueueProvider>();
			queue.Setup(q => q.EnqueueNotification(It.IsAny<Resgrid.Model.Queue.NotificationItem>())).ReturnsAsync(true);
			var evidence = new Mock<IRecordsEvidenceService>();
			evidence.Setup(e => e.BindToRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
			evidence.Setup(e => e.GetForRecordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(new List<RmsEvidenceArtifact>());
			var udf = new RecordsUdfService(Mock.Of<IRmsUdfDefinitionsRepository>(), Mock.Of<IUdfFieldRepository>(), Mock.Of<IUdfFieldValueRepository>(), Authorization.Object, groups.Object, Mock.Of<IUnitOfWork>(), adp.Object);

			Records = new RecordsService(Store.RecordsRepo.Object, new RmsRecordValueService(Store.DetailsRepo.Object), Store.ParticipantsRepo.Object, Store.UnitsRepo.Object,
				Store.AttachmentsRepo.Object, Store.RevisionsRepo.Object, evidence.Object, Store.ScopesRepo.Object, Store.SharesRepo.Object, Store.ProjectionsRepo.Object,
				Store.AuditsRepo.Object, outbox, cutover.Object, settings.Object, groups.Object, profiles.Object, units.Object, calls.Object, adp.Object,
				Store.UnitOfWork.Object, queue.Object, new NullRecordAttachmentScanner(), Authorization.Object, udf, new PassthroughRecordsProtection(), Mock.Of<IRecordDefinitionsService>(), Mock.Of<IRecordTypedValuesService>(), Mock.Of<IPersonnelRolesService>());

			var branding = new Mock<IDepartmentProfileMediaService>();
			branding.Setup(b => b.GetBrandingAsync(Dept)).ReturnsAsync(new DepartmentBranding { DisplayName = "Parity Fire Department", ShortName = "PFD", AddressText = "100 Station Road" });
			var layouts = new Mock<IRecordsPrintLayoutService>();
			layouts.Setup(l => l.GetDepartmentDefaultAsync(Dept)).ReturnsAsync(new RmsRecordPrintLayout { Version = 1, Scope = 1, Config = RecordsPrintLayoutConfig.Default() });
			Documents = new RecordsDocumentService(Authorization.Object, Store.RecordsRepo.Object, IncidentStore.ReportsRepo.Object, IncidentStore.AnalysesRepo.Object, Store.RevisionsRepo.Object,
				Mock.Of<IIncidentReportsService>(), branding.Object, layouts.Object, Mock.Of<IPdfProvider>(), evidence.Object, udf, new PassthroughRecordsProtection(), Mock.Of<IRecordDefinitionsService>());
		}

		#region Fixtures

		public static string FixtureDirectory()
		{
			var local = Path.Combine(TestContext.CurrentContext.TestDirectory, "Rms", "Parity");
			if (Directory.Exists(local) && Directory.EnumerateFiles(local, "*.json").Any())
				return local;

			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, "Tests", "Resgrid.Tests", "Rms", "Parity");
				if (Directory.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}
			throw new DirectoryNotFoundException("The Records parity fixtures were not found beside the test assembly or in the repository.");
		}

		public static IEnumerable<RecordsParityFixture> LoadFixtures()
		{
			foreach (var file in Directory.EnumerateFiles(FixtureDirectory(), "*.json").OrderBy(f => f, StringComparer.Ordinal))
			{
				var fixture = JsonConvert.DeserializeObject<RecordsParityFixture>(System.IO.File.ReadAllText(file), FixtureSettings);
				fixture.FileName = Path.GetFileName(file);
				yield return fixture;
			}
		}

		#endregion

		#region Legacy mapping

		/// <summary>The legacy row, mapped field by field per <see cref="LegacyFieldMap"/>, as the draft Records would author.</summary>
		public static RecordDraftInput MapLegacy(RecordsParityFixture fixture)
		{
			var legacy = fixture.Legacy ?? throw new ArgumentException("The fixture has no legacy row.");
			var input = new RecordDraftInput { DefinitionKey = fixture.DefinitionKey, Details = new RmsOperationalRecordDetail() };

			if (string.Equals(legacy.Source, "UnitLogs", StringComparison.Ordinal))
			{
				var unitLog = legacy.UnitLog ?? throw new ArgumentException("A UnitLogs fixture needs a unitLog object.");
				var unitId = Int(unitLog, "UnitId") ?? throw new ArgumentException("UnitLog.UnitId is required.");
				input.Details.UnitId = unitId;
				input.Details.ActivityOn = Date(unitLog, "Timestamp");
				input.Details.Narrative = Text(unitLog, "Narrative");
				input.Units.Add(new RecordUnitResponseInput { UnitId = unitId });
				return input;
			}

			var log = legacy.Log ?? throw new ArgumentException("A Logs fixture needs a log object.");
			input.ExternalId = Text(log, "ExternalId");
			input.StationGroupId = Int(log, "StationGroupId");
			input.CallId = Int(log, "CallId");
			input.StartedOn = Date(log, "StartedOn");
			input.EndedOn = Date(log, "EndedOn");
			var d = input.Details;
			d.Narrative = Text(log, "Narrative");
			d.InitialReport = Text(log, "InitialReport");
			d.Type = Text(log, "Type");
			d.Course = Text(log, "Course");
			d.CourseCode = Text(log, "CourseCode");
			d.Instructors = Text(log, "Instructors");
			d.Cause = Text(log, "Cause");
			d.InvestigatedByUserId = Text(log, "InvestigatedByUserId");
			d.ContactName = Text(log, "ContactName");
			d.ContactNumber = Text(log, "ContactNumber");
			d.OtherPersonnel = Text(log, "OtherPersonnel");
			d.Location = Text(log, "Location");
			d.OtherAgencies = Text(log, "OtherAgencies");
			d.OtherUnits = Text(log, "OtherUnits");
			d.BodyLocation = Text(log, "BodyLocation");
			d.PronouncedDeceasedBy = Text(log, "PronouncedDeceasedBy");

			var officer = Text(log, "OfficerUserId");
			if (officer != null)
				input.Participants.Add(new RecordParticipantInput { UserId = officer, Role = "Officer" });
			foreach (var user in legacy.Users)
			{
				var userId = Text(user, "UserId");
				if (userId == null || input.Participants.Any(p => p.UserId == userId))
					continue;
				input.Participants.Add(new RecordParticipantInput { UserId = userId, UnitId = Int(user, "UnitId"), Role = Text(user, "Role") });
			}
			foreach (var unit in legacy.Units)
			{
				input.Units.Add(new RecordUnitResponseInput
				{
					UnitId = Int(unit, "UnitId") ?? throw new ArgumentException("LogUnit.UnitId is required."),
					Dispatched = Date(unit, "Dispatched"), Enroute = Date(unit, "Enroute"), OnScene = Date(unit, "OnScene"), Released = Date(unit, "Released"), InQuarters = Date(unit, "InQuarters")
				});
			}
			return input;
		}

		public static string LegacyAuthor(RecordsParityFixture fixture)
		{
			return Text(fixture.Legacy?.Log, "LoggedByUserId") ?? Author;
		}

		private static string Text(JObject o, string key)
		{
			var token = o?[key];
			return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
		}

		private static int? Int(JObject o, string key)
		{
			var token = o?[key];
			return token == null || token.Type == JTokenType.Null ? (int?)null : token.Value<int>();
		}

		public static DateTime? Date(JObject o, string key) => Date(Text(o, key));

		public static DateTime? Date(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			return DateTime.TryParseExact(value, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz" }, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
				? parsed : (DateTime?)null;
		}

		#endregion

		#region Replay

		public async Task<RecordsParityReplay> ReplayAsync(RecordsParityFixture fixture)
		{
			var author = LegacyAuthor(fixture);
			var input = MapLegacy(fixture);
			var draft = await Records.CreateDraftAsync(Dept, author, input);
			var id = draft.Record.RmsOperationalRecordId;

			var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			foreach (var attachment in fixture.Legacy.Attachments)
			{
				var data = Convert.FromBase64String(attachment.DataBase64 ?? string.Empty);
				bytes[attachment.FileName] = data;
				await Records.AddAttachmentAsync(Dept, attachment.UserId ?? author, id, attachment.FileName, attachment.Type, data, attachment.Description);
			}

			var current = await Records.GetAsync(Dept, id);
			var finalized = await Records.FinalizeAsync(Dept, author, id, current.Record.RowVersion, "1", null, null);
			var aggregate = await Records.GetAsync(Dept, id, includeRevisions: true);
			var revision = Store.Revisions.Single(r => r.RecordId == id);
			var snapshot = RecordSnapshotSerializer.Deserialize(revision.SnapshotJson);

			var officerDocument = await Documents.GetAsync(Dept, Author, id, RmsRecordKind.Operational);
			var clerkDocument = await Documents.GetAsync(Dept, Clerk, id, RmsRecordKind.Operational);

			return new RecordsParityReplay
			{
				Fixture = fixture,
				RecordId = id,
				Aggregate = aggregate,
				Finalized = finalized,
				Projection = Store.Projections.Single(p => p.RmsRecordSearchProjectionId == id),
				Revision = revision,
				Snapshot = snapshot,
				ExportJson = RecordSnapshotSerializer.Serialize(snapshot),
				PrintHtml = await Documents.RenderHtmlAsync(Dept, Author, officerDocument),
				PrintHtmlWithoutRestrictedAccess = await Documents.RenderHtmlAsync(Dept, Clerk, clerkDocument),
				ClerkDocument = clerkDocument,
				AttachmentBytes = bytes
			};
		}

		#endregion

		#region Comparison

		/// <summary>Serialize the actual object the way the fixture is written: ISO UTC dates, nulls kept, no reference loops.</summary>
		public static JObject Actual(object value)
		{
			return JObject.Parse(JsonConvert.SerializeObject(value, ActualSettings), new JsonLoadSettings());
		}

		/// <summary>Every property named in <paramref name="expected"/> must equal the actual value, compared as JSON tokens with dates as ISO strings.</summary>
		public static List<string> Mismatches(JObject expected, object actualObject)
		{
			var actualJson = JsonConvert.SerializeObject(actualObject, ActualSettings);
			var actual = JsonConvert.DeserializeObject<JObject>(actualJson, FixtureSettings);
			var mismatches = new List<string>();
			foreach (var property in expected.Properties())
			{
				var actualToken = actual[property.Name];
				if (actualToken == null)
				{
					mismatches.Add($"{property.Name}: not present on the Records side");
					continue;
				}
				if (!JToken.DeepEquals(Normalize(property.Value), Normalize(actualToken)))
					mismatches.Add($"{property.Name}: expected {property.Value.ToString(Formatting.None)} but Records holds {actualToken.ToString(Formatting.None)}");
			}
			return mismatches;
		}

		private static JToken Normalize(JToken token)
		{
			if (token.Type == JTokenType.Integer)
				return new JValue(token.Value<long>());
			if (token.Type == JTokenType.Float)
				return new JValue(token.Value<double>());
			if (token.Type == JTokenType.String)
			{
				var text = token.Value<string>();
				var date = Date(text);
				if (date.HasValue && text.Contains("T"))
					return new JValue(date.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
				return new JValue(text);
			}
			return token;
		}

		#endregion
	}

	public sealed class RecordsParityReplay
	{
		public RecordsParityFixture Fixture { get; set; }
		public string RecordId { get; set; }
		public RecordAggregate Aggregate { get; set; }
		public RecordAggregate Finalized { get; set; }
		public RmsRecordSearchProjection Projection { get; set; }
		public RmsRevision Revision { get; set; }
		public RecordSnapshot Snapshot { get; set; }
		public string ExportJson { get; set; }
		public string PrintHtml { get; set; }
		public string PrintHtmlWithoutRestrictedAccess { get; set; }
		public RecordDocument ClerkDocument { get; set; }
		public Dictionary<string, byte[]> AttachmentBytes { get; set; }
	}
}
