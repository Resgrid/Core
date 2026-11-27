using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.TrainingT1;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.TrainingSubmissions
{
	/// <summary>
	/// Round 9B: training hours submissions with evidence and the optional review. Real LearningRecordService and LearningRecordWriter over the
	/// in-memory ledger, with the review service on top: evidence types, cleaning, caps and the scanner; review off / on / by category / above a
	/// threshold; department-wide, group-scoped and own-group role reviewers; approve / deny / more information / resubmit; the inbox, counts and
	/// bulk approve; member and reviewer notices once each (digest and none), workflow payloads without notes or file names, and the GDPR
	/// section.
	/// </summary>
	[TestFixture]
	public class TrainingSubmissionTests
	{
		internal const int Dept = 9100;
		internal const string Member = "member-m";
		internal const string Member2 = "member-n";
		internal const string Captain = "captain-c";
		internal const string GroupAdmin = "group-admin-g";
		internal const string Admin = "admin-a";
		internal const string Outsider = "member-o";
		internal const int Hazmat = 5;
		internal const int Fire = 6;

		#region Harness

		internal sealed class World
		{
			public readonly FakeLearningRecordsRepository Records = new FakeLearningRecordsRepository();
			public readonly List<LearningRecordFile> Files = new();
			public readonly List<LearningRecordReviewEvent> History = new();
			public readonly List<TrainingSubmissionWorkflowEvent> Workflow = new();
			public readonly List<AuditEvent> Audits = new();
			public readonly HashSet<(int, DateTime)> Digests = new();
			public readonly Mock<ITrainingRecordNoticeService> Notices = new();
			public readonly Mock<IRecordAttachmentScanner> Scanner = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IAuthorizationService> Authorization = new();
			public TrainingRecordSettings Settings = new TrainingRecordSettings();
			public readonly LearningRecordService Ledger;
			public readonly TrainingSubmissionService Service;
			public readonly Mock<ILearningRecordFilesRepository> FileRepository = new();
			public readonly Mock<ILearningRecordReviewEventsRepository> HistoryRepository = new();

			public World()
			{
				FileRepository.Setup(f => f.InsertAsync(It.IsAny<LearningRecordFile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LearningRecordFile file, CancellationToken _, bool __) => { file.LearningRecordFileId = 300 + Files.Count + 1; Files.Add(file); return file; });
				FileRepository.Setup(f => f.GetForRecordsAsync(Dept, It.IsAny<IEnumerable<int>>()))
					.ReturnsAsync((int d, IEnumerable<int> ids) => Files.Where(f => f.LearningRecordId.HasValue && ids.Contains(f.LearningRecordId.Value)).Select(Meta).ToList());
				FileRepository.Setup(f => f.GetForUserAsync(Dept, It.IsAny<string>()))
					.ReturnsAsync((int d, string user) => Files.Where(f => string.Equals(f.UserId, user, StringComparison.OrdinalIgnoreCase)).Select(Meta).ToList());
				FileRepository.Setup(f => f.GetWithDataAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => Files.FirstOrDefault(f => f.LearningRecordFileId == id));
				FileRepository.Setup(f => f.LinkToRecordAsync(Dept, It.IsAny<IEnumerable<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, IEnumerable<int> ids, int recordId, CancellationToken _) =>
					{
						var linked = 0;
						foreach (var file in Files.Where(f => ids.Contains(f.LearningRecordFileId) && !f.LearningRecordId.HasValue))
						{
							file.LearningRecordId = recordId;
							linked++;
						}
						return linked;
					});

				Scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((string name, string type, byte[] data, CancellationToken _) => new RecordAttachmentScanResult
					{
						State = name.Contains("eicar", StringComparison.OrdinalIgnoreCase) ? RmsAttachmentScanState.Rejected : RmsAttachmentScanState.Clean
					});

				var categories = new Mock<ITrainingCategoriesRepository>();
				categories.Setup(c => c.GetForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory>
				{
					new TrainingCategory { TrainingCategoryId = Hazmat, DepartmentId = Dept, Name = "Hazardous materials" },
					new TrainingCategory { TrainingCategoryId = Fire, DepartmentId = Dept, Name = "Fire" }
				});
				var mappings = new Mock<ICategoryCreditMappingsRepository>();
				mappings.Setup(m => m.GetForDepartmentAsync(Dept)).ReturnsAsync(new List<CategoryCreditMapping>());
				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(s => s.GetTrainingRecordSettingsAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => Settings);

				var events = new Mock<IEventAggregator>();
				events.Setup(e => e.SendMessage(It.IsAny<TrainingSubmissionWorkflowEvent>())).Callback((TrainingSubmissionWorkflowEvent e) => Workflow.Add(e));
				events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback((AuditEvent e) => Audits.Add(e));

				var writer = new LearningRecordWriter(Records, mappings.Object, categories.Object, Mock.Of<IPersonnelCertificationRepository>(),
					Mock.Of<IPersonnelCertificationCreditsRepository>(), new Lazy<ICertificationService>(() => Mock.Of<ICertificationService>()), Mock.Of<ITrainingRepository>(),
					Mock.Of<ITrainingUserRepository>(), Mock.Of<ITrainingCompletionHistoriesRepository>(), Mock.Of<ILogUsersRepository>(),
					Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordParticipantsRepository>(), Mock.Of<ICalendarItemsRepository>(),
					Mock.Of<ICalendarItemTypeRepository>(), settings.Object, events.Object,
					new Lazy<IEnumerable<ILearningRecordObserver>>(() => Array.Empty<ILearningRecordObserver>()));
				Ledger = new LearningRecordService(Records, FileRepository.Object, categories.Object, mappings.Object, Mock.Of<ILearningRecordBackfillsRepository>(), writer,
					Mock.Of<ICertificationService>(), Mock.Of<IPersonnelCertificationRepository>(), Mock.Of<IPersonnelCertificationCreditsRepository>(), settings.Object,
					Departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IShiftsService>(), events.Object, Scanner.Object);

				HistoryRepository.Setup(h => h.InsertAsync(It.IsAny<LearningRecordReviewEvent>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LearningRecordReviewEvent e, CancellationToken _, bool __) => { e.LearningRecordReviewEventId = History.Count + 1; History.Add(e); return e; });
				HistoryRepository.Setup(h => h.GetForRecordAsync(Dept, It.IsAny<int>())).ReturnsAsync((int d, int id) => History.Where(e => e.LearningRecordId == id).ToList());
				HistoryRepository.Setup(h => h.GetForUserAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string user) => History.Where(e => e.UserId == user).ToList());

				var digests = new Mock<ITrainingReviewDigestsRepository>();
				digests.Setup(d => d.TryClaimAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, DateTime day, DateTime now, CancellationToken _) => Digests.Add((d, day.Date)));
				digests.Setup(d => d.SaveCountsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);

				Departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = Admin, TimeZone = "UTC", Name = "Test" });
				Departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept))
					.ReturnsAsync(() => new HashSet<string>(new[] { Member, Member2, Captain, GroupAdmin, Admin, Outsider }, StringComparer.OrdinalIgnoreCase));
				Departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = Admin } });
				Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
				{
					new PersonName { UserId = Member, FirstName = "Morgan" }, new PersonName { UserId = Member2, FirstName = "Noor" }
				});

				var groupA = new DepartmentGroup
				{
					DepartmentGroupId = 1, DepartmentId = Dept,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Member }, new DepartmentGroupMember { UserId = GroupAdmin, IsAdmin = true } }
				};
				var groupB = new DepartmentGroup
				{
					DepartmentGroupId = 2, DepartmentId = Dept,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = Member2 }, new DepartmentGroupMember { UserId = Captain } }
				};
				var groups = new Mock<IDepartmentGroupsService>();
				groups.Setup(g => g.GetGroupForUserAsync(It.IsAny<string>(), Dept))
					.ReturnsAsync((string user, int d) => user == Member || user == GroupAdmin ? groupA : user == Member2 || user == Captain ? groupB : null);

				var permissions = new Mock<IPermissionsService>();
				permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(Dept, PermissionTypes.RecordTrainingCompletions)).ReturnsAsync(new Permission
				{
					DepartmentId = Dept, PermissionType = (int)PermissionTypes.RecordTrainingCompletions, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = "77"
				});
				var roles = new Mock<IPersonnelRolesService>();
				roles.Setup(r => r.GetAllMembersOfRoleAsync(77)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { DepartmentId = Dept, PersonnelRoleId = 77, UserId = Captain } });

				Authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), Dept)).ReturnsAsync((string user, int d) => Scope(user));

				Notices.Setup(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), It.IsAny<TrainingRecordNoticeKinds>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Notices.Setup(n => n.NotifyReviewersAsync(It.IsAny<LearningRecord>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((LearningRecord r, IEnumerable<string> reviewers, bool _, CancellationToken __) => reviewers.Count());
				Notices.Setup(n => n.NotifyReviewerDigestAsync(Dept, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

				Service = new TrainingSubmissionService(Ledger, Records, HistoryRepository.Object, digests.Object, Authorization.Object, Departments.Object, groups.Object,
					roles.Object, permissions.Object, Notices.Object, events.Object);
			}

			private static LearningRecordFile Meta(LearningRecordFile f) => new LearningRecordFile
			{
				LearningRecordFileId = f.LearningRecordFileId, DepartmentId = f.DepartmentId, UserId = f.UserId, FileName = f.FileName, ContentType = f.ContentType,
				UploadedOn = f.UploadedOn, UploadedByUserId = f.UploadedByUserId, LearningRecordId = f.LearningRecordId, SizeBytes = f.SizeBytes, Sha256 = f.Sha256,
				EvidenceKind = f.EvidenceKind, ScanState = f.ScanState
			};

			public static TrainingManagementScope Scope(string user)
			{
				var scope = TrainingManagementScope.None(user, Dept);
				scope.Assign = new TrainingRightScope();
				scope.Delete = new TrainingRightScope();
				switch (user)
				{
					case Admin:
						scope.Author = new TrainingRightScope { ManagesAll = true, ManagesOwn = true };
						scope.Assign = new TrainingRightScope { ManagesAll = true, ManagesOwn = true };
						scope.Record = new TrainingRightScope { ManagesAll = true, ManagesOwn = true };
						break;
					case Captain:
						// Record Training Completions (135) through the training captain role: every member.
						scope.Record = new TrainingRightScope { ManagesAll = true, ManagesOwn = true };
						break;
					case GroupAdmin:
						// Record Training Completions as a group admin: their own group's members.
						scope.Record = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = new HashSet<string>(new[] { Member, GroupAdmin }, StringComparer.OrdinalIgnoreCase) };
						break;
				}
				return scope;
			}

			public LearningRecordDraft Draft(string title = "Hazardous materials refresher", decimal hours = 8m, int? categoryId = Hazmat) => new LearningRecordDraft
			{
				Title = title,
				CompletedOn = DateTime.UtcNow.Date.AddDays(-2),
				Hours = hours,
				CategoryId = categoryId,
				CeProvider = "State academy",
				CeCourseNumber = "HM-8",
				Notes = "Private note: my supervisor said this counts"
			};

			public async Task<LearningRecord> SubmitAsync(string user = Member, LearningRecordDraft draft = null, params TrainingEvidenceUpload[] files)
			{
				var outcome = await Service.SubmitAsync(Dept, user, draft ?? Draft(), files);
				outcome.Refusal.Should().BeNull();
				return outcome.Record;
			}

			public LearningRecord Stored(int id) => Records.Rows.Single(r => r.LearningRecordId == id);
		}

		internal static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF");
		internal static byte[] Heic() => new byte[] { 0, 0, 0, 0x18 }.Concat(Encoding.ASCII.GetBytes("ftypheic")).Concat(new byte[16]).ToArray();
		internal static byte[] Docx() => new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(new byte[32]).ToArray();
		internal static byte[] Doc() => new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.Concat(new byte[32]).ToArray();

		internal static byte[] ImageBytes(string format, bool withGps = false)
		{
			using var image = new Image<Rgba32>(8, 8);
			if (withGps)
			{
				image.Metadata.ExifProfile = new ExifProfile();
				image.Metadata.ExifProfile.SetValue(ExifTag.Software, "Phone camera");
				image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
			}
			using var stream = new MemoryStream();
			switch (format)
			{
				case "jpg": image.SaveAsJpeg(stream); break;
				case "gif": image.SaveAsGif(stream); break;
				case "webp": image.SaveAsWebp(stream); break;
				default: image.SaveAsPng(stream); break;
			}
			return stream.ToArray();
		}

		internal static TrainingEvidenceUpload File(string name, byte[] data, TrainingEvidenceKinds kind = TrainingEvidenceKinds.Other) =>
			new TrainingEvidenceUpload { FileName = name, Data = data, Kind = (int)kind };

		private World _world;

		[SetUp]
		public void SetUp() => _world = new World();

		#endregion Harness

		#region Evidence

		[Test]
		public async Task Every_evidence_type_is_accepted_linked_hashed_and_photos_lose_their_metadata()
		{
			var record = await _world.SubmitAsync(Member, null,
				File("certificate.pdf", Pdf(), TrainingEvidenceKinds.Certificate),
				File("IMG_0042.jpg", ImageBytes("jpg", withGps: true)),
				File("scan.png", ImageBytes("png"), TrainingEvidenceKinds.Document),
				File("receipt.docx", Docx(), TrainingEvidenceKinds.Receipt));

			var stored = _world.Files.Where(f => f.LearningRecordId == record.LearningRecordId).ToList();
			stored.Should().HaveCount(4, "every file is linked to the submission");
			stored.Select(f => f.ContentType).Should().Equal("application/pdf", "image/jpeg", "image/png",
				"application/vnd.openxmlformats-officedocument.wordprocessingml.document");
			stored.Select(f => f.EvidenceKind).Should().Equal((int)TrainingEvidenceKinds.Certificate, (int)TrainingEvidenceKinds.Photo, (int)TrainingEvidenceKinds.Document,
				(int)TrainingEvidenceKinds.Receipt);
			stored.Should().OnlyContain(f => f.SizeBytes == f.Data.LongLength && f.Sha256 == TrainingEvidencePolicy.Sha256Of(f.Data) && f.UserId == Member);
			stored.Should().OnlyContain(f => f.ScanState == (int)RmsAttachmentScanState.Clean);
			_world.Stored(record.LearningRecordId).EvidenceFileId.Should().Be(stored[0].LearningRecordFileId);

			using var photo = SixLabors.ImageSharp.Image.Load(stored[1].Data);
			photo.Metadata.ExifProfile.Should().BeNull("location, device and author never reach storage");
		}

		[TestCase("notes.doc")]
		[TestCase("notes.odt")]
		[TestCase("photo.gif")]
		[TestCase("photo.webp")]
		public void The_other_accepted_types_pass_the_policy(string name)
		{
			var extension = Path.GetExtension(name).TrimStart('.');
			var data = extension switch { "doc" => Doc(), "odt" => Docx(), "gif" => ImageBytes("gif"), _ => ImageBytes("webp") };

			var check = TrainingEvidencePolicy.Check(File(name, data));

			check.Refusal.Should().BeNull();
			check.File.ContentType.Should().Be(TrainingEvidencePolicy.Types[extension]);
		}

		[TestCase("IMG_0043.HEIC")]
		[TestCase("photo.heif")]
		public void A_heic_photo_is_refused_because_its_location_metadata_cannot_be_stripped(string name)
		{
			TrainingEvidencePolicy.Check(File(name, Heic())).Refusal.Should().Be(TrainingEvidencePolicy.TypeRefused);
			TrainingEvidencePolicy.ContentTypeOf(name).Should().BeNull("the browser picker and the API refuse it before upload");
			TrainingEvidencePolicy.AcceptAttribute.Should().NotContain("heic").And.NotContain("heif");
		}

		[Test]
		public async Task Caps_types_contents_and_the_scanner_refuse_before_anything_is_stored()
		{
			async Task<string> Refusal(params TrainingEvidenceUpload[] files) => (await _world.Service.SubmitAsync(Dept, Member, _world.Draft(), files)).Refusal;

			(await Refusal(Enumerable.Range(0, TrainingSubmissionRules.MaxFilesPerUpload + 1).Select(i => File("p" + i + ".pdf", Pdf())).ToArray()))
				.Should().Be(TrainingEvidencePolicy.TooMany);
			(await Refusal(File("big.pdf", Pdf().Concat(new byte[LearningRecordFile.MaxBytes]).ToArray()))).Should().Be(TrainingEvidencePolicy.TooLarge);
			(await Refusal(File("a.pdf", Pdf().Concat(new byte[9 * 1024 * 1024]).ToArray()), File("b.pdf", Pdf().Concat(new byte[9 * 1024 * 1024]).ToArray()),
				File("c.pdf", Pdf().Concat(new byte[9 * 1024 * 1024]).ToArray()), File("d.pdf", Pdf().Concat(new byte[9 * 1024 * 1024]).ToArray())))
				.Should().Be(TrainingEvidencePolicy.TooLarge, "30 MB per upload together");
			(await Refusal(File("setup.exe", new byte[] { 0x4D, 0x5A, 0, 0 }))).Should().Be(TrainingEvidencePolicy.TypeRefused);
			(await Refusal(File("drawing.svg", Encoding.ASCII.GetBytes("<svg/>")))).Should().Be(TrainingEvidencePolicy.TypeRefused);
			(await Refusal(File("certificate.pdf", new byte[] { 0x4D, 0x5A, 0, 0, 1, 2 }))).Should().Be(TrainingEvidencePolicy.ContentMismatch, "a renamed program is not a PDF");
			(await Refusal(File("photo.jpg", Pdf()))).Should().Be(TrainingEvidencePolicy.ContentMismatch);
			(await Refusal(File("eicar.pdf", Pdf()))).Should().Be(TrainingEvidencePolicy.ScannerRejected);

			_world.Files.Should().BeEmpty();
			_world.Records.Rows.Should().BeEmpty();
		}

		#endregion Evidence

		#region Review settings

		[Test]
		public async Task Review_off_accepts_on_submit_as_self_reported_and_tells_only_the_member()
		{
			_world.Settings = new TrainingRecordSettings { SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Off };

			var outcome = await _world.Service.SubmitAsync(Dept, Member, _world.Draft(), Array.Empty<TrainingEvidenceUpload>());

			outcome.ReviewRequired.Should().BeFalse();
			var record = _world.Stored(outcome.Record.LearningRecordId);
			record.Status.Should().Be((int)LearningRecordStatuses.Verified);
			record.ReviewState.Should().Be((int)TrainingSubmissionReviewStates.SelfReported);
			record.VerifiedByUserId.Should().BeNull();
			TrainingSubmissionRules.ProvenanceOf(record).Should().Be(LearningRecordProvenances.SelfReported);
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.Is<LearningRecord>(r => r.LearningRecordId == record.LearningRecordId), TrainingRecordNoticeKinds.SubmissionAccepted,
				null, It.IsAny<CancellationToken>()), Times.Once);
			_world.Notices.Verify(n => n.NotifyReviewersAsync(It.IsAny<LearningRecord>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.History.Single().Kind.Should().Be((int)TrainingSubmissionEventKinds.SelfReported);
			_world.Workflow.Single().Should().Match<TrainingSubmissionWorkflowEvent>(e => e.Change == TrainingSubmissionWorkflowChanges.Received && !e.ReviewRequired);
		}

		[Test]
		public async Task Review_on_waits_for_a_reviewer_and_tells_the_member_and_each_reviewer_once()
		{
			var record = await _world.SubmitAsync();

			var stored = _world.Stored(record.LearningRecordId);
			stored.Status.Should().Be((int)LearningRecordStatuses.Pending);
			stored.ReviewState.Should().Be((int)TrainingSubmissionReviewStates.AwaitingReview);
			stored.SubmittedOn.Should().NotBeNull();
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), TrainingRecordNoticeKinds.SubmissionReceived, null, It.IsAny<CancellationToken>()), Times.Once);
			_world.Notices.Verify(n => n.NotifyReviewersAsync(It.Is<LearningRecord>(r => r.LearningRecordId == record.LearningRecordId),
				It.Is<IEnumerable<string>>(reviewers => reviewers.OrderBy(x => x).SequenceEqual(new[] { Admin, Captain, GroupAdmin })), false, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void Review_by_category_or_above_a_threshold_and_selected_with_nothing_reviews_everything()
		{
			var selected = new TrainingRecordSettings { SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Selected, ReviewCategoryIds = new List<int> { Hazmat }, ReviewAboveHours = 6m };

			TrainingSubmissionRules.RequiresReview(selected, Hazmat, 1m).Should().BeTrue("the category needs review");
			TrainingSubmissionRules.RequiresReview(selected, Fire, 4m).Should().BeFalse("another category under the threshold is self-reported");
			TrainingSubmissionRules.RequiresReview(selected, Fire, 6.5m).Should().BeTrue("above the threshold");
			TrainingSubmissionRules.RequiresReview(selected, null, 6m).Should().BeFalse("the threshold is \"more than\"");
			TrainingSubmissionRules.RequiresReview(new TrainingRecordSettings { SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Selected }, Fire, 1m)
				.Should().BeTrue("selected with nothing selected fails safe");
			TrainingSubmissionRules.RequiresReview(new TrainingRecordSettings(), Fire, 1m).Should().BeTrue("always is the default (T1's behaviour)");
			TrainingSubmissionRules.RequiresReview(new TrainingRecordSettings { SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Off }, Hazmat, 100m).Should().BeFalse();
		}

		[Test]
		public async Task Selected_review_applies_per_submission()
		{
			_world.Settings = new TrainingRecordSettings { SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Selected, ReviewCategoryIds = new List<int> { Hazmat }, ReviewAboveHours = 6m };

			(await _world.Service.SubmitAsync(Dept, Member, _world.Draft("Hazmat ops", 2m, Hazmat), null)).ReviewRequired.Should().BeTrue();
			(await _world.Service.SubmitAsync(Dept, Member, _world.Draft("Pump ops", 4m, Fire), null)).ReviewRequired.Should().BeFalse();
			(await _world.Service.SubmitAsync(Dept, Member, _world.Draft("Pump academy", 8m, Fire), null)).ReviewRequired.Should().BeTrue();
		}

		[Test]
		public void The_review_settings_survive_a_protobuf_round_trip()
		{
			var settings = new TrainingRecordSettings
			{
				MemberSubmissionsDisabled = false,
				SubmissionReviewMode = (int)TrainingSubmissionReviewModes.Selected,
				ReviewCategoryIds = new List<int> { Fire, Hazmat },
				ReviewAboveHours = 6.5m,
				RoleReviewersOwnGroupOnly = true,
				ReviewerNotices = (int)TrainingReviewerNoticeModes.DailyDigest
			};

			var copy = ObjectSerialization.Deserialize<TrainingRecordSettings>(ObjectSerialization.Serialize(settings)).Normalized();

			copy.SubmissionReviewMode.Should().Be((int)TrainingSubmissionReviewModes.Selected);
			copy.ReviewCategoryIds.Should().Equal(Hazmat, Fire);
			copy.ReviewAboveHours.Should().Be(6.5m);
			copy.RoleReviewersOwnGroupOnly.Should().BeTrue();
			copy.ReviewerNotices.Should().Be((int)TrainingReviewerNoticeModes.DailyDigest);
			new TrainingRecordSettings { SubmissionReviewMode = 42, ReviewerNotices = 9, ReviewAboveHours = 5000m }.Normalized()
				.Should().Match<TrainingRecordSettings>(s => s.SubmissionReviewMode == 0 && s.ReviewerNotices == 0 && s.ReviewAboveHours == TrainingRecordSettings.MaxReviewAboveHours);
		}

		#endregion Review settings

		#region Reviewers

		[Test]
		public async Task Department_wide_group_scoped_and_role_reviewers_and_nobody_for_themself()
		{
			(await _world.Service.GetReviewerScopeAsync(Dept, Admin)).Covers(Member2).Should().BeTrue();
			(await _world.Service.GetReviewerScopeAsync(Dept, Captain)).Covers(Member).Should().BeTrue("the training captain role reviews everyone");
			var group = await _world.Service.GetReviewerScopeAsync(Dept, GroupAdmin);
			group.Covers(Member).Should().BeTrue();
			group.Covers(Member2).Should().BeFalse("a group admin reviews only their group's members");
			group.Covers(GroupAdmin).Should().BeFalse("never their own");
			(await _world.Service.GetReviewerScopeAsync(Dept, Outsider)).IsReviewer.Should().BeFalse();

			_world.Settings = new TrainingRecordSettings { RoleReviewersOwnGroupOnly = true };
			var captain = await _world.Service.GetReviewerScopeAsync(Dept, Captain);
			captain.Covers(Member2).Should().BeTrue("the captain's own group");
			captain.Covers(Member).Should().BeFalse("role reviewers keep to their own group when the department says so");
			(await _world.Service.GetReviewerScopeAsync(Dept, Admin)).Covers(Member).Should().BeTrue("department admins still review everyone");
			(await _world.Service.GetReviewerUserIdsAsync(Dept, Member)).Should().BeEquivalentTo(Admin, GroupAdmin);
		}

		[Test]
		public async Task A_group_reviewer_sees_and_decides_only_their_groups_submissions()
		{
			var mine = await _world.SubmitAsync(Member);
			var other = await _world.SubmitAsync(Member2);

			var inbox = await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = GroupAdmin });
			inbox.Items.Select(r => r.LearningRecordId).Should().Equal(mine.LearningRecordId);

			(await _world.Service.ApproveAsync(Dept, other.LearningRecordId, GroupAdmin, null, null)).Refusal.Should().Be("learning_record_not_reviewer");
			(await _world.Service.ApproveAsync(Dept, mine.LearningRecordId, GroupAdmin, "Looks right", null)).Refusal.Should().BeNull();
			_world.Stored(other.LearningRecordId).Status.Should().Be((int)LearningRecordStatuses.Pending);
		}

		#endregion Reviewers

		#region State machine

		[Test]
		public async Task More_information_resubmission_denial_and_decided_states_follow_the_rules()
		{
			var record = await _world.SubmitAsync(Member, null, File("certificate.pdf", Pdf()));
			var id = record.LearningRecordId;

			(await _world.Service.RequestInfoAsync(Dept, id, Captain, " ")).Refusal.Should().Be("learning_record_note_required");
			(await _world.Service.RequestInfoAsync(Dept, id, Member, "x")).Refusal.Should().Be("learning_record_self_review");
			(await _world.Service.RequestInfoAsync(Dept, id, Captain, "The certificate is cut off; add page 2.")).Refusal.Should().BeNull();
			_world.Stored(id).ReviewState.Should().Be((int)TrainingSubmissionReviewStates.InfoRequested);
			_world.Stored(id).Status.Should().Be((int)LearningRecordStatuses.Pending, "it still does not count");
			(await _world.Service.RequestInfoAsync(Dept, id, Admin, "again")).Refusal.Should().Be("learning_record_not_awaiting_review", "the member has not answered yet");
			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain })).Items.Should().BeEmpty("it waits for the member");

			(await _world.Service.ResubmitAsync(Dept, id, Member2, "not mine", null, new[] { File("page2.pdf", Pdf()) })).Refusal.Should().Be("learning_record_not_found");
			(await _world.Service.ResubmitAsync(Dept, id, Member, " ", null, null)).Refusal.Should().Be("learning_record_resubmit_empty");
			var resubmitted = await _world.Service.ResubmitAsync(Dept, id, Member, "Page 2 added; it was 6 hours.", new TrainingSubmissionCorrection { Hours = 6m },
				new[] { File("page2.pdf", Pdf(), TrainingEvidenceKinds.Certificate) });
			resubmitted.Refusal.Should().BeNull();
			_world.Stored(id).Should().Match<LearningRecord>(r => r.ReviewState == (int)TrainingSubmissionReviewStates.AwaitingReview && r.Hours == 6m);
			_world.Files.Count(f => f.LearningRecordId == id).Should().Be(2);
			_world.Notices.Verify(n => n.NotifyReviewersAsync(It.IsAny<LearningRecord>(), It.IsAny<IEnumerable<string>>(), true, It.IsAny<CancellationToken>()), Times.Once);

			(await _world.Service.DenyAsync(Dept, id, Captain, "")).Refusal.Should().Be("learning_record_reason_required");
			(await _world.Service.DenyAsync(Dept, id, Captain, "Not an accredited provider.")).Refusal.Should().BeNull();
			_world.Stored(id).Should().Match<LearningRecord>(r => r.Status == (int)LearningRecordStatuses.Rejected && r.ReviewState == (int)TrainingSubmissionReviewStates.Denied &&
				r.ReviewNote == "Not an accredited provider.");
			(await _world.Service.ApproveAsync(Dept, id, Admin, null, null)).Refusal.Should().Be("learning_record_not_pending");

			_world.History.Select(e => (TrainingSubmissionEventKinds)e.Kind).Should().Equal(TrainingSubmissionEventKinds.Submitted, TrainingSubmissionEventKinds.InfoRequested,
				TrainingSubmissionEventKinds.Resubmitted, TrainingSubmissionEventKinds.Denied);
			_world.History[2].FileCount.Should().Be(1);
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), TrainingRecordNoticeKinds.SubmissionInfoRequested, "The certificate is cut off; add page 2.",
				It.IsAny<CancellationToken>()), Times.Once);
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), TrainingRecordNoticeKinds.SubmissionDenied, "Not an accredited provider.", It.IsAny<CancellationToken>()),
				Times.Once);
			_world.Audits.Select(a => a.Type).Should().Contain(new[] { AuditLogTypes.ExternalTrainingSubmitted, AuditLogTypes.TrainingSubmissionInfoRequested,
				AuditLogTypes.TrainingSubmissionResubmitted, AuditLogTypes.LearningRecordRejected });
		}

		[Test]
		public async Task An_approval_counts_as_reviewed_with_corrected_hours_and_tells_the_member_once()
		{
			var record = await _world.SubmitAsync();

			var approved = await _world.Service.ApproveAsync(Dept, record.LearningRecordId, Captain, "Checked the certificate", 7.5m);
			await _world.Service.ApproveAsync(Dept, record.LearningRecordId, Captain, "twice", null);

			approved.Refusal.Should().BeNull();
			var stored = _world.Stored(record.LearningRecordId);
			stored.Should().Match<LearningRecord>(r => r.Status == (int)LearningRecordStatuses.Verified && r.ReviewState == (int)TrainingSubmissionReviewStates.Approved &&
				r.Hours == 7.5m && r.VerifiedByUserId == Captain);
			TrainingSubmissionRules.ProvenanceOf(stored).Should().Be(LearningRecordProvenances.Reviewed);
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), TrainingRecordNoticeKinds.SubmissionApproved, "Checked the certificate", It.IsAny<CancellationToken>()),
				Times.Once, "a second approval is refused, so the member hears once");
			_world.Workflow.Count(e => e.Change == TrainingSubmissionWorkflowChanges.Approved).Should().Be(1);
		}

		#endregion State machine

		#region Inbox

		[Test]
		public async Task The_inbox_counts_lists_oldest_first_filters_and_bulk_approves_what_the_reviewer_covers()
		{
			var first = await _world.SubmitAsync(Member, _world.Draft("Confined space awareness", 4m, Fire));
			var second = await _world.SubmitAsync(Member2, _world.Draft("Hazmat technician", 40m, Hazmat));
			var third = await _world.SubmitAsync(Member, _world.Draft("Rope rescue", 16m, Hazmat));
			_world.Records.Rows.Single(r => r.LearningRecordId == first.LearningRecordId).SubmittedOn = DateTime.UtcNow.AddDays(-3);
			_world.Records.Rows.Single(r => r.LearningRecordId == second.LearningRecordId).SubmittedOn = DateTime.UtcNow.AddDays(-2);
			_world.Records.Rows.Single(r => r.LearningRecordId == third.LearningRecordId).SubmittedOn = DateTime.UtcNow.AddDays(-1);
			await _world.Service.RequestInfoAsync(Dept, third.LearningRecordId, Admin, "Which instructor?");

			var inbox = await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain });
			inbox.IsReviewer.Should().BeTrue();
			inbox.AwaitingCount.Should().Be(2);
			inbox.InfoRequestedCount.Should().Be(1);
			inbox.Items.Select(r => r.LearningRecordId).Should().Equal(first.LearningRecordId, second.LearningRecordId);
			inbox.OldestWaitingSince.Should().BeCloseTo(DateTime.UtcNow.AddDays(-3), TimeSpan.FromMinutes(1));
			(await _world.Service.CountAwaitingAsync(Dept, Captain)).Should().Be(2);
			(await _world.Service.CountAwaitingAsync(Dept, GroupAdmin)).Should().Be(1, "only their group's member");
			(await _world.Service.CountAwaitingAsync(Dept, Outsider)).Should().Be(0);

			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain, CategoryId = Hazmat })).Items
				.Select(r => r.LearningRecordId).Should().Equal(second.LearningRecordId);
			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain, MemberUserId = Member.ToUpperInvariant() })).Items
				.Select(r => r.LearningRecordId).Should().Equal(first.LearningRecordId);
			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain, Search = "hm-8" })).Items.Should().HaveCount(2);
			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Captain, InfoRequested = true })).Items
				.Select(r => r.LearningRecordId).Should().Equal(third.LearningRecordId);
			(await _world.Service.GetInboxAsync(new TrainingReviewInboxQuery { DepartmentId = Dept, ReviewerUserId = Outsider })).IsReviewer.Should().BeFalse();

			var bulk = await _world.Service.BulkApproveAsync(Dept, new[] { first.LearningRecordId, second.LearningRecordId }, GroupAdmin, null);
			bulk.Approved.Should().Equal(first.LearningRecordId);
			bulk.Refused[second.LearningRecordId].Should().Be("learning_record_not_reviewer");
			(await _world.Service.BulkApproveAsync(Dept, new[] { second.LearningRecordId, third.LearningRecordId }, Captain, "Bulk")).Approved
				.Should().BeEquivalentTo(new[] { second.LearningRecordId, third.LearningRecordId }, "a submission waiting for its member can still be approved");
		}

		[Test]
		public async Task The_member_and_a_reviewer_read_the_submission_with_its_evidence_and_others_do_not()
		{
			var record = await _world.SubmitAsync(Member, null, File("certificate.pdf", Pdf()));
			var fileId = _world.Files.Single().LearningRecordFileId;

			var own = await _world.Service.GetSubmissionAsync(Dept, record.LearningRecordId, Member);
			own.IsOwner.Should().BeTrue();
			own.CanReview.Should().BeFalse();
			own.Files.Should().ContainSingle().Which.Data.Should().BeNull("lists never carry the bytes");
			(await _world.Service.GetSubmissionAsync(Dept, record.LearningRecordId, GroupAdmin)).CanReview.Should().BeTrue();
			(await _world.Service.GetSubmissionAsync(Dept, record.LearningRecordId, Outsider)).Should().BeNull();
			(await _world.Service.GetEvidenceFileAsync(Dept, fileId, Captain)).Data.Should().Equal(Pdf());
			(await _world.Service.GetEvidenceFileAsync(Dept, fileId, Member)).Should().NotBeNull();
			(await _world.Service.GetEvidenceFileAsync(Dept, fileId, Member2)).Should().BeNull();
		}

		#endregion Inbox

		#region Notices, digest, workflow and GDPR

		[TestCase(TrainingReviewerNoticeModes.DailyDigest)]
		[TestCase(TrainingReviewerNoticeModes.None)]
		public async Task Reviewers_hear_nothing_per_submission_with_the_digest_or_no_notices(TrainingReviewerNoticeModes mode)
		{
			_world.Settings = new TrainingRecordSettings { ReviewerNotices = (int)mode };

			await _world.SubmitAsync();

			_world.Notices.Verify(n => n.NotifyReviewersAsync(It.IsAny<LearningRecord>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_world.Notices.Verify(n => n.NotifyMemberAsync(It.IsAny<LearningRecord>(), TrainingRecordNoticeKinds.SubmissionReceived, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_daily_digest_goes_once_a_day_after_the_hour_with_each_reviewers_count()
		{
			_world.Settings = new TrainingRecordSettings { ReviewerNotices = (int)TrainingReviewerNoticeModes.DailyDigest };
			await _world.SubmitAsync(Member);
			await _world.SubmitAsync(Member2);
			var day = DateTime.UtcNow.Date;

			(await _world.Service.SendReviewerDigestsAsync(day.AddHours(5))).Should().Be(0, "before the digest hour");
			(await _world.Service.SendReviewerDigestsAsync(day.AddHours(9))).Should().Be(3);
			(await _world.Service.SendReviewerDigestsAsync(day.AddHours(10))).Should().Be(0, "claimed for the day");

			_world.Notices.Verify(n => n.NotifyReviewerDigestAsync(Dept, Admin, 2, It.IsAny<CancellationToken>()), Times.Once);
			_world.Notices.Verify(n => n.NotifyReviewerDigestAsync(Dept, Captain, 2, It.IsAny<CancellationToken>()), Times.Once);
			_world.Notices.Verify(n => n.NotifyReviewerDigestAsync(Dept, GroupAdmin, 1, It.IsAny<CancellationToken>()), Times.Once);

			_world.Settings = new TrainingRecordSettings();
			(await _world.Service.SendReviewerDigestsAsync(day.AddDays(1).AddHours(9))).Should().Be(0, "immediate departments get no digest");
		}

		[Test]
		public async Task Workflow_payloads_carry_ids_hours_and_states_never_notes_reasons_or_file_names()
		{
			var record = await _world.SubmitAsync(Member, null, File("passport-scan-jane-doe.pdf", Pdf()));
			await _world.Service.RequestInfoAsync(Dept, record.LearningRecordId, Captain, "Secret reviewer remark");
			await _world.Service.ResubmitAsync(Dept, record.LearningRecordId, Member, "Member private answer", null, null);
			await _world.Service.DenyAsync(Dept, record.LearningRecordId, Captain, "Confidential denial reason");

			_world.Workflow.Select(e => e.Trigger).Should().Equal(WorkflowTriggerEventType.TrainingSubmissionReceived, WorkflowTriggerEventType.TrainingSubmissionInfoRequested,
				WorkflowTriggerEventType.TrainingSubmissionResubmitted, WorkflowTriggerEventType.TrainingSubmissionDenied);
			_world.Workflow.Should().OnlyContain(e => e.DepartmentId == Dept && e.LearningRecordId == record.LearningRecordId && e.EvidenceFiles == 1);
			var json = JsonConvert.SerializeObject(_world.Workflow);
			foreach (var secret in new[] { "Private note", "Secret reviewer remark", "Member private answer", "Confidential denial reason", "passport-scan", "jane" })
				json.Should().NotContain(secret);
			_world.Workflow.Last().ReviewerUserId.Should().Be(Captain);
		}

		[Test]
		public async Task The_gdpr_section_holds_the_members_submissions_history_and_evidence_metadata_only()
		{
			var record = await _world.SubmitAsync(Member, null, File("certificate.pdf", Pdf(), TrainingEvidenceKinds.Certificate));
			await _world.SubmitAsync(Member2);
			await _world.Service.ApproveAsync(Dept, record.LearningRecordId, Captain, "ok", null);

			var section = new TrainingSubmissionsGdprSection(_world.Records, _world.FileRepository.Object, _world.HistoryRepository.Object);
			var json = JsonConvert.SerializeObject(await section.BuildAsync(Member, Dept));

			section.EntryName.Should().Be("training_submissions.json");
			json.Should().Contain("Hazardous materials refresher").And.Contain("certificate.pdf").And.Contain("Certificate").And.Contain("Approved").And.Contain("Reviewed");
			json.Should().NotContain(Member2);
			json.Should().NotContain("\"Data\"", "the bytes are downloaded from the product, not exported");
		}

		#endregion Notices, digest, workflow and GDPR
	}

	/// <summary>Round 9B: the member and reviewer notices (EventTypes 79-81) through the standard notification path.</summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingRecordNoticeTests
	{
		private const int Dept = 9200;
		private Mock<ICommunicationService> _communication;
		private Mock<IUserProfileService> _profiles;
		private TrainingRecordNoticeService _notices;
		private bool _doNotBroadcast;

		[SetUp]
		public void SetUp()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			_communication = new Mock<ICommunicationService>();
			_communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>())).ReturnsAsync(true);
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync("member-de", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "member-de", Language = "de" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("reviewer-en", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "reviewer-en", Language = "en" });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(Dept)).ReturnsAsync(new HashSet<string>(new[] { "member-de", "reviewer-en" }, StringComparer.OrdinalIgnoreCase));
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, TimeZone = "UTC" });
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = "member-de", FirstName = "Mia" }, new PersonName { UserId = "reviewer-en", FirstName = "Rae", LastName = "Smith" }
			});
			_notices = new TrainingRecordNoticeService(new Lazy<ICommunicationService>(() => _communication.Object), departments.Object, _profiles.Object,
				Mock.Of<IDepartmentSettingsService>());
		}

		[TearDown]
		public void TearDown() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private static LearningRecord Record(string user = "member-de") => new LearningRecord
		{
			LearningRecordId = 41, DepartmentId = Dept, UserId = user, Title = "Atemschutz Auffrischung", Hours = 4m, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission
		};

		[Test]
		public async Task A_member_notice_goes_through_the_standard_path_in_the_members_language_with_its_event_code()
		{
			(await _notices.NotifyMemberAsync(Record(), TrainingRecordNoticeKinds.SubmissionDenied, "Kein Nachweis")).Should().BeTrue();

			// The profile goes with it: CommunicationService applies the member's own push / e-mail / text choices.
			_communication.Verify(c => c.SendNotificationAsync("member-de", Dept, It.Is<string>(m => m.Contains("Atemschutz Auffrischung") && m.Contains("Kein Nachweis") &&
					m.Contains("/User/TrainingReviews/Submission/41")), It.IsAny<string>(), It.IsAny<Department>(), "Ausbildungseinreichung nicht angenommen",
				It.Is<UserProfile>(p => p.Language == "de"), false, "LS:41"), Times.Once);
			TrainingRecordNoticeService.EventTypeOf(TrainingRecordNoticeKinds.SubmissionDenied).Should().Be(EventTypes.TrainingSubmissionNotice);
			TrainingRecordNoticeService.EventTypeOf(TrainingRecordNoticeKinds.ReviewDigest).Should().Be(EventTypes.TrainingReviewNotice);
			TrainingRecordNoticeService.EventTypeOf(TrainingRecordNoticeKinds.CompletionVoided).Should().Be(EventTypes.TrainingCompletionNotice);
		}

		[Test]
		public async Task Inactive_members_the_member_themself_and_a_quiet_department_are_never_notified()
		{
			(await _notices.NotifyMemberAsync(Record("member-hidden"), TrainingRecordNoticeKinds.SubmissionApproved, null)).Should().BeFalse();
			(await _notices.NotifyReviewersAsync(Record(), new[] { "member-de", "member-hidden", "reviewer-en", "REVIEWER-EN" }, false)).Should().Be(1);
			_communication.Verify(c => c.SendNotificationAsync("reviewer-en", Dept, It.Is<string>(m => m.Contains("Mia") && m.Contains("/User/TrainingReviews/Review/41")),
				It.IsAny<string>(), It.IsAny<Department>(), "Training submission to review", It.IsAny<UserProfile>(), false, "LV:41"), Times.Once);

			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;
			(await _notices.NotifyMemberAsync(Record(), TrainingRecordNoticeKinds.SubmissionApproved, null)).Should().BeFalse();
			_communication.Verify(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(),
				It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task A_recorded_completion_and_its_void_tell_the_member_never_an_external_learner()
		{
			var recording = new TrainingCompletionRecording
			{
				TrainingCompletionRecordingId = 7, DepartmentId = Dept, UserId = "member-de", RecordedByUserId = "reviewer-en", TargetName = "CPR",
				CompletedOn = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc), LearningRecordId = 55
			};

			(await _notices.NotifyCompletionAsync(recording, false)).Should().BeTrue();
			recording.VoidReason = "Falsche Person";
			(await _notices.NotifyCompletionAsync(recording, true)).Should().BeTrue();
			(await _notices.NotifyCompletionAsync(new TrainingCompletionRecording { DepartmentId = Dept, UserId = "xl-abc", TargetName = "CPR" }, false)).Should().BeFalse();

			_communication.Verify(c => c.SendNotificationAsync("member-de", Dept, It.Is<string>(m => m.Contains("Rae Smith") && m.Contains("CPR")), It.IsAny<string>(),
				It.IsAny<Department>(), "Ausbildungsabschluss erfasst", It.IsAny<UserProfile>(), false, "LC:55"), Times.Once);
			_communication.Verify(c => c.SendNotificationAsync("member-de", Dept, It.Is<string>(m => m.Contains("Falsche Person")), It.IsAny<string>(),
				It.IsAny<Department>(), "Erfasster Abschluss storniert", It.IsAny<UserProfile>(), false, "LC:55"), Times.Once);
		}

		[Test]
		public void Every_notice_kind_has_its_text_in_every_language_with_its_placeholders()
		{
			foreach (var culture in Resgrid.Localization.SupportedLocales.SupportedLanguagesMap.Keys)
				foreach (var kind in Enum.GetValues<TrainingRecordNoticeKinds>())
				{
					var (title, message) = TrainingRecordNoticeService.Compose(kind, CultureInfo.GetCultureInfo(culture), "Course", 2.5m, "Reason", "Rae", 3, DateTime.UtcNow, "https://x");
					title.Should().NotBeNullOrWhiteSpace($"{culture} {kind}");
					message.Should().NotContain("{", $"{culture} {kind} fills every placeholder");
				}
		}
	}

	/// <summary>Round 9B: how every record shows on the transcript and its exports.</summary>
	[TestFixture]
	public class LearningRecordProvenanceTests
	{
		[Test]
		public void Self_reported_reviewed_recorded_by_an_instructor_imported_and_system_records_are_told_apart()
		{
			LearningRecord R(LearningRecordSourceKinds kind, LearningRecordStatuses status, string verifiedBy = null) =>
				new LearningRecord { SourceKind = (int)kind, Status = (int)status, VerifiedByUserId = verifiedBy };

			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.ExternalSubmission, LearningRecordStatuses.Verified)).Should().Be(LearningRecordProvenances.SelfReported);
			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.ExternalSubmission, LearningRecordStatuses.Pending)).Should().Be(LearningRecordProvenances.SelfReported);
			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.ExternalSubmission, LearningRecordStatuses.Verified, "officer")).Should().Be(LearningRecordProvenances.Reviewed);
			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.CalendarAttendance, LearningRecordStatuses.Verified, "officer")).Should().Be(LearningRecordProvenances.Reviewed);
			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.CourseCompletion, LearningRecordStatuses.Verified)).Should().Be(LearningRecordProvenances.System);
			TrainingSubmissionRules.ProvenanceOf(R(LearningRecordSourceKinds.Import, LearningRecordStatuses.Verified)).Should().Be(LearningRecordProvenances.Imported);
			var recorded = R(LearningRecordSourceKinds.CourseCompletion, LearningRecordStatuses.Verified);
			recorded.CompletionMethod = (int)LearningCompletionMethods.RecordedByOther;
			TrainingSubmissionRules.ProvenanceOf(recorded).Should().Be(LearningRecordProvenances.RecordedByOther);

			// A T1 submission written before M0408 reads its state from its status.
			TrainingSubmissionRules.StateOf(R(LearningRecordSourceKinds.ExternalSubmission, LearningRecordStatuses.Pending)).Should().Be(TrainingSubmissionReviewStates.AwaitingReview);
			TrainingSubmissionRules.StateOf(R(LearningRecordSourceKinds.ExternalSubmission, LearningRecordStatuses.Rejected)).Should().Be(TrainingSubmissionReviewStates.Denied);
			TrainingSubmissionRules.StateOf(R(LearningRecordSourceKinds.CourseCompletion, LearningRecordStatuses.Verified)).Should().BeNull();
		}

		[Test]
		public void The_transcript_export_names_how_each_record_was_recorded()
		{
			var document = new TranscriptDocument { MemberName = "Morgan" };
			document.Lines.Add(new TranscriptLine { CompletedOn = new DateTime(2026, 11, 1), Title = "Hazmat", Hours = 8m, Counts = true, Provenance = "Self-reported" });

			TrainingDocuments.TranscriptCsv(document).Should().Contain("How recorded").And.Contain("Self-reported");
			TrainingDocuments.TranscriptHtml(document).Should().Contain("Self-reported");
		}
	}
}
