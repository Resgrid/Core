using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.ContentSets;
using Resgrid.Services.Records;

namespace Resgrid.Tests.ContentSets
{
	/// <summary>
	/// Round 10 A3 follow-up: learning paths in course sets, stored lesson files (hosted video, SCORM/cmi5 packages, LTI tools) in course copies,
	/// training quizzes that flow down by version, notices to configurable roles, attribution of course and path copies, sponsored path assignment,
	/// and the linked partner's acceptance of Records deployment fills.
	/// </summary>
	[TestFixture]
	public class ContentSetFollowUpTests
	{
		private const int Publisher = 40;
		private const int Subscriber = 1;

		private static ContentSetCopyContext Context(int sourceItemId, int localItemId = 0) => new ContentSetCopyContext
		{
			SubscriberDepartmentId = Subscriber,
			UserId = "admin-1",
			Set = new ContentSet { ContentSetId = 3, DepartmentId = Publisher, Name = "County academy" },
			Source = new ContentSetSourceItem { ItemId = sourceItemId, VersionLabel = "2.0" },
			Copy = localItemId > 0 ? new ContentSetCopy { LocalItemId = localItemId } : null,
			PublisherName = "Dept 40"
		};

		#region Settings, notices, gate, order

		[Test]
		public void Notice_roles_parse_positive_distinct_and_capped_and_admins_stay_on_by_default()
		{
			var parsed = ContentSetSettings.Parse(new ContentSetSettings { NotifyRoleIds = new List<int> { 5, 5, -1, 0, 9 } }.ToJson());
			parsed.NotifyRoleIds.Should().Equal(5, 9);
			parsed.NotifyAdmins.Should().BeTrue();
			ContentSetSettings.Parse(null).NotifyAdmins.Should().BeTrue();
			ContentSetSettings.Parse(new ContentSetSettings { NotifyRoleIds = Enumerable.Range(1, 40).ToList() }.ToJson()).NotifyRoleIds.Should().HaveCount(ContentSetSettings.MaxNotifyRoles);
		}

		private static ContentSetNotifier Notifier(ContentSetSettings settings)
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(Subscriber)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { Id = "admin-1" } });
			var settingsService = new Mock<IDepartmentSettingsService>();
			settingsService.Setup(s => s.GetSettingByTypeAsync(Subscriber, DepartmentSettingTypes.ContentSetSettings)).ReturnsAsync(new DepartmentSetting { Setting = settings.ToJson() });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForDepartmentAsync(Subscriber)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5, DepartmentId = Subscriber } });
			roles.Setup(r => r.GetAllMembersOfRoleAsync(5)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "trainer" }, new PersonnelRoleUser { UserId = "ADMIN-1" } });
			roles.Setup(r => r.GetAllMembersOfRoleAsync(9)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "other-department" } });
			return new ContentSetNotifier(new Lazy<ICommunicationService>(() => Mock.Of<ICommunicationService>()), departments.Object, Mock.Of<IUserProfileService>(), settingsService.Object, roles.Object);
		}

		[Test]
		public async Task Notices_reach_the_admins_and_the_named_roles_members_and_a_must_reach_notice_falls_back_to_the_admins()
		{
			(await Notifier(new ContentSetSettings { NotifyRoleIds = new List<int> { 5, 9 } }).RecipientsAsync(Subscriber, false))
				.Should().BeEquivalentTo(new[] { "admin-1", "trainer" }, "a role of another department is never used, and nobody hears it twice");
			(await Notifier(new ContentSetSettings { NotifyAdmins = false, NotifyRoleIds = new List<int> { 5 } }).RecipientsAsync(Subscriber, false))
				.Should().BeEquivalentTo(new[] { "trainer", "ADMIN-1" });
			(await Notifier(new ContentSetSettings { NotifyAdmins = false }).RecipientsAsync(Subscriber, false)).Should().BeEmpty();
			(await Notifier(new ContentSetSettings { NotifyAdmins = false }).RecipientsAsync(Subscriber, true)).Should().Equal("admin-1");
		}

		[Test]
		public void A_sponsored_path_copy_is_assigned_under_the_sponsors_add_on_and_nothing_else_is()
		{
			var gate = new LmsDepartmentGate { DepartmentId = Subscriber, ModuleOn = true, FlagOn = true, AddonActive = false, SponsoredPaths = new Dictionary<int, DateTime?> { [7] = null } };
			LmsLapseRules.CanAssignNew(gate.ForPath(7), false).Should().BeTrue();
			LmsLapseRules.CanAssignNew(gate.ForPath(8), false).Should().BeFalse();
			LmsLapseRules.CanAssignNew(gate.ForCourse(7), false).Should().BeFalse("a path id is not a course id");
		}

		[Test]
		public void Paths_are_copied_after_the_courses_and_trainings_they_name()
		{
			var order = ContentSetService.CopyOrder(new[]
			{
				new ContentSetItem { ContentSetItemId = 1, ItemKind = (int)ContentSetItemKinds.LmsPath, SortOrder = 1 },
				new ContentSetItem { ContentSetItemId = 2, ItemKind = (int)ContentSetItemKinds.LmsCourse, SortOrder = 2 },
				new ContentSetItem { ContentSetItemId = 3, ItemKind = (int)ContentSetItemKinds.Training, SortOrder = 3 }
			});
			order.Select(i => i.ContentSetItemId).Should().Equal(2, 3, 1);
		}

		#endregion Settings, notices, gate, order

		#region Training quizzes

		private static Training Quiz(int id, int department, string question, params (string Answer, bool Correct)[] answers)
		{
			var training = new Training { TrainingId = id, DepartmentId = department, Name = "Hose", TrainingText = "<p>Lay a line</p>", MinimumScore = 80, Questions = new Collection<TrainingQuestion>() };
			training.Questions.Add(new TrainingQuestion
			{
				TrainingQuestionId = id * 10, Question = question,
				Answers = answers.Select((a, i) => new TrainingQuestionAnswer { TrainingQuestionAnswerId = id * 100 + i, Answer = a.Answer, Correct = a.Correct }).ToList()
			});
			return training;
		}

		[Test]
		public async Task A_publishers_quiz_change_is_a_new_version_and_replaces_the_copys_quiz_while_an_unchanged_quiz_keeps_its_rows()
		{
			var source = Quiz(10, Publisher, "How long?", ("50 ft", true), ("100 ft", false));
			var local = Quiz(20, Subscriber, "How long?", ("50 ft", true), ("100 ft", false));
			var trainings = new Mock<ITrainingService>();
			trainings.Setup(t => t.GetTrainingByIdAsync(10)).ReturnsAsync(() => source);
			trainings.Setup(t => t.GetTrainingByIdAsync(20)).ReturnsAsync(() => local);
			trainings.Setup(t => t.SaveAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>())).ReturnsAsync((Training t, CancellationToken _) => t);
			var attributions = new Mock<IContentAttributionsRepository>();
			var item = new TrainingSetItemSource(trainings.Object, attributions.Object);

			var before = TrainingSetItemSource.ContentVersion(source);
			(await item.ApplyVersionAsync(Context(10, 20))).Succeeded.Should().BeTrue();
			local.Questions.Single().TrainingQuestionId.Should().Be(200, "an unchanged quiz keeps its rows and ids");

			source.Questions.Single().Answers.First().Correct = false;
			source.Questions.Single().Answers.Last().Correct = true;
			TrainingSetItemSource.ContentVersion(source).Should().NotBe(before, "the answer key is part of the version");
			(await item.ApplyVersionAsync(Context(10, 20))).Succeeded.Should().BeTrue();
			local.Questions.Single().TrainingQuestionId.Should().Be(0, "a changed quiz is replaced by new rows");
			local.Questions.Single().Answers.Select(a => a.Correct).Should().Equal(false, true);
			TrainingQuizScorer.RevisionHash(local).Should().Be(TrainingQuizScorer.RevisionHash(source));
			(await item.GetLocalHtmlAsync(Subscriber, 20)).Should().Contain("How long?").And.Contain("&#10003; 100 ft");

			attributions.Verify(a => a.SaveForSubjectAsync(It.Is<ContentAttribution>(c => c.DepartmentId == Subscriber && c.SubjectKind == (int)ContentAttributionSubjects.Training &&
				c.SubjectId == 20 && c.SourceKind == (int)ContentAttributionSources.ContentSet && c.SourceRef == "3"), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		#endregion Training quizzes

		#region Stored lesson files

		private sealed class Files
		{
			public readonly List<LmsMediaAsset> Media = new List<LmsMediaAsset>();
			public readonly List<LmsContentPackage> Packages = new List<LmsContentPackage>();
			public readonly List<LmsLtiTool> Tools = new List<LmsLtiTool>();
			public readonly LmsSetFiles Service;

			public Files()
			{
				var media = new Mock<ILmsMediaAssetsRepository>();
				media.Setup(m => m.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Media.FirstOrDefault(a => a.DepartmentId == d && a.LmsMediaAssetId == id));
				media.Setup(m => m.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) => Media.Where(a => a.DepartmentId == d).ToList());
				media.Setup(m => m.InsertAsync(It.IsAny<LmsMediaAsset>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsMediaAsset a, CancellationToken _, bool __) => { a.LmsMediaAssetId = 500 + Media.Count; Media.Add(a); return a; });
				var packages = new Mock<ILmsContentPackagesRepository>();
				packages.Setup(m => m.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Packages.FirstOrDefault(a => a.DepartmentId == d && a.LmsContentPackageId == id));
				packages.Setup(m => m.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int d, bool _) => Packages.Where(a => a.DepartmentId == d).ToList());
				packages.Setup(m => m.InsertAsync(It.IsAny<LmsContentPackage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsContentPackage a, CancellationToken _, bool __) => { a.LmsContentPackageId = 600 + Packages.Count; Packages.Add(a); return a; });
				var tools = new Mock<ILmsLtiToolsRepository>();
				tools.Setup(m => m.GetByIdForDepartmentAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Tools.FirstOrDefault(a => a.DepartmentId == d && a.LmsLtiToolId == id));
				tools.Setup(m => m.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int d, bool _) => Tools.Where(a => a.DepartmentId == d).ToList());
				tools.Setup(m => m.InsertAsync(It.IsAny<LmsLtiTool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsLtiTool a, CancellationToken _, bool __) => { a.LmsLtiToolId = 700 + Tools.Count; Tools.Add(a); return a; });
				Service = new LmsSetFiles(media.Object, packages.Object, tools.Object);
			}
		}

		[Test]
		public async Task Hosted_video_and_packages_are_shared_as_the_subscribers_own_rows_over_the_publishers_objects_and_reused()
		{
			var files = new Files();
			files.Media.Add(new LmsMediaAsset { LmsMediaAssetId = 1, DepartmentId = Publisher, Status = (int)LmsMediaStatuses.Ready, SourceKey = "lms/40/1/source.mp4", HlsPrefix = "lms/40/1/hls/", Mp4Key = "lms/40/1/video.mp4", DurationSeconds = 300 });
			files.Packages.Add(new LmsContentPackage { LmsContentPackageId = 2, DepartmentId = Publisher, Status = (int)LmsContentPackageStatuses.Ready, Sha256 = "abc", ContentPrefix = "lms/40/shared/packages/x/content/", Standard = (int)LmsContentStandards.Cmi5 });

			var video = await files.Service.ShareVideoAsync(Publisher, Subscriber, 1, "admin-1");
			var shared = files.Media.Single(m => m.LmsMediaAssetId == video);
			shared.DepartmentId.Should().Be(Subscriber);
			shared.SharedFromDepartmentId.Should().Be(Publisher);
			shared.HlsPrefix.Should().Be("lms/40/1/hls/");
			shared.Status.Should().Be((int)LmsMediaStatuses.Ready);
			shared.DurationSeconds.Should().Be(300, "the watched-percent cap needs the duration");
			(await files.Service.ShareVideoAsync(Publisher, Subscriber, 1, "admin-1")).Should().Be(video, "an update reuses the shared row");

			var package = await files.Service.SharePackageAsync(Publisher, Subscriber, 2, "admin-1");
			files.Packages.Single(p => p.LmsContentPackageId == package).Should().Match<LmsContentPackage>(p => p.DepartmentId == Subscriber && p.SharedFromDepartmentId == Publisher &&
				p.ContentPrefix == "lms/40/shared/packages/x/content/" && p.Standard == (int)LmsContentStandards.Cmi5);
			(await files.Service.SharePackageAsync(Publisher, Subscriber, 2, "admin-1")).Should().Be(package);
			(await files.Service.ShareVideoAsync(Subscriber, Subscriber, video.Value, "admin-1")).Should().BeNull("nothing is shared with the department itself");
		}

		[Test]
		public async Task An_lti_lesson_gets_the_subscribers_own_inactive_registration_or_reuses_one_with_the_same_endpoints()
		{
			var files = new Files();
			files.Tools.Add(new LmsLtiTool { LmsLtiToolId = 3, DepartmentId = Publisher, Name = "Vendor", ClientId = "pub-client", OidcLoginUrl = "https://tool/login", LaunchUrl = "https://tool/launch", AllowGradePassback = true, ShareEmail = true, IsActive = true });

			var tool = await files.Service.PrepareToolAsync(Publisher, Subscriber, 3, "admin-1");
			tool.Created.Should().BeTrue();
			tool.IsActive.Should().BeFalse("the vendor must trust the subscriber's own issuer and keys first");
			var row = files.Tools.Single(t => t.LmsLtiToolId == tool.LmsLtiToolId);
			row.DepartmentId.Should().Be(Subscriber);
			row.ClientId.Should().NotBe("pub-client");
			row.ShareEmail.Should().BeFalse("sharing learner data is the subscriber's own choice");

			row.IsActive = true;
			var again = await files.Service.PrepareToolAsync(Publisher, Subscriber, 3, "admin-1");
			again.LmsLtiToolId.Should().Be(tool.LmsLtiToolId);
			again.IsActive.Should().BeTrue();
			again.Created.Should().BeFalse();
		}

		[Test]
		public async Task A_course_whose_video_or_package_is_not_ready_or_whose_tool_was_removed_cannot_travel_yet()
		{
			var files = new Files();
			files.Media.Add(new LmsMediaAsset { LmsMediaAssetId = 1, DepartmentId = Publisher, Status = (int)LmsMediaStatuses.Transcoding });
			files.Packages.Add(new LmsContentPackage { LmsContentPackageId = 2, DepartmentId = Publisher, Status = (int)LmsContentPackageStatuses.Ready, IsDeleted = true });
			(await files.Service.GetProblemAsync(Publisher, new[] { new LmsLesson { LmsMediaAssetId = 1 } })).Should().Be(LmsSetFileProblems.MediaNotReady);
			(await files.Service.GetProblemAsync(Publisher, new[] { new LmsLesson { LmsContentPackageId = 2 } })).Should().Be(LmsSetFileProblems.PackageNotReady);
			(await files.Service.GetProblemAsync(Publisher, new[] { new LmsLesson { LmsLtiToolId = 9 } })).Should().Be(LmsSetFileProblems.ToolMissing);
			(await files.Service.GetProblemAsync(Publisher, new[] { new LmsLesson { Kind = (int)LmsLessonKinds.Blocks } })).Should().BeNull();
			Resgrid.Services.LmsCourseService.SetCopyProblem(new LmsCourse { LmsCourseId = 1, DepartmentId = Publisher },
				new LmsCourseVersion { LmsCourseId = 1, DepartmentId = Publisher, Status = (int)ProtocolVersionStatuses.Published },
				new[] { new LmsLesson { Kind = (int)LmsLessonKinds.Scorm, LmsContentPackageId = 2 }, new LmsLesson { Kind = (int)LmsLessonKinds.LtiLink, LmsLtiToolId = 3 } })
				.Should().BeNull("SCORM, cmi5, LTI and hosted video lessons travel now");
			Resgrid.Services.LmsCourseService.SetCopyProblem(new LmsCourse { LmsCourseId = 1, DepartmentId = Publisher },
				new LmsCourseVersion { LmsCourseId = 1, DepartmentId = Publisher, Status = (int)ProtocolVersionStatuses.Published },
				new[] { new LmsLesson { Kind = (int)LmsLessonKinds.LiveSession } }).Should().Be(Resgrid.Services.LmsCourseService.SetProblemLessonKinds);
		}

		#endregion Stored lesson files

		#region Learning paths

		private sealed class Paths
		{
			public readonly List<LmsPath> Rows = new List<LmsPath>();
			public readonly List<LmsPathStep> Steps = new List<LmsPathStep>();
			public readonly FakeCopies Copies = new FakeCopies();
			public readonly Mock<IContentAttributionsRepository> Attributions = new Mock<IContentAttributionsRepository>();
			public readonly LmsPathSetItemSource Source;

			public Paths()
			{
				var paths = new Mock<ILmsPathsRepository>();
				paths.Setup(p => p.GetVisibleByIdAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int d, int id) => Rows.FirstOrDefault(r => r.LmsPathId == id && (r.DepartmentId == d || r.DepartmentId == 0)));
				paths.Setup(p => p.GetVisibleAsync(It.IsAny<int>())).ReturnsAsync((int d) => Rows.Where(r => r.DepartmentId == d || r.DepartmentId == 0).ToList());
				paths.Setup(p => p.InsertAsync(It.IsAny<LmsPath>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((LmsPath p, CancellationToken _, bool __) => { p.LmsPathId = 900 + Rows.Count; Rows.Add(p); return p; });
				paths.Setup(p => p.SaveOrUpdateAsync(It.IsAny<LmsPath>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((LmsPath p, CancellationToken _, bool __) => p);
				var steps = new Mock<ILmsPathStepsRepository>();
				steps.Setup(s => s.GetForPathsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync((IEnumerable<int> ids) => Steps.Where(s => ids.Contains(s.LmsPathId)).ToList());
				steps.Setup(s => s.ReplaceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<LmsPathStep>>(), It.IsAny<CancellationToken>()))
					.Callback((int d, int id, IEnumerable<LmsPathStep> list, CancellationToken _) => { Steps.RemoveAll(s => s.LmsPathId == id); Steps.AddRange(list); })
					.Returns(Task.CompletedTask);
				var courses = new Mock<ILmsCoursesRepository>();
				courses.Setup(c => c.GetVisibleByIdsAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsCourse>());
				Source = new LmsPathSetItemSource(paths.Object, steps.Object, courses.Object, Mock.Of<ITrainingService>(), Copies, Attributions.Object);

				Rows.Add(new LmsPath { LmsPathId = 7, DepartmentId = Publisher, Name = "Driver path", CreatedOn = DateTime.UtcNow });
				Steps.Add(new LmsPathStep { LmsPathId = 7, DepartmentId = Publisher, SortOrder = 1, Kind = (int)LmsPathStepKinds.Course, TargetRefId = 11 });
				Steps.Add(new LmsPathStep { LmsPathId = 7, DepartmentId = Publisher, SortOrder = 2, Kind = (int)LmsPathStepKinds.Training, TargetRefId = 12, PrerequisiteOrders = "1" });
				Steps.Add(new LmsPathStep { LmsPathId = 7, DepartmentId = Publisher, SortOrder = 3, Kind = (int)LmsPathStepKinds.Course, TargetRefId = 3, TargetSystemKey = "academy:course:x" });
			}

			public void Copied(ContentSetItemKinds kind, int source, int local) =>
				Copies.Rows.Add(new ContentSetCopy { ContentSetCopyId = Copies.Rows.Count + 1, DepartmentId = Subscriber, PublisherDepartmentId = Publisher, ItemKind = (int)kind, SourceItemId = source, LocalItemId = local });
		}

		[Test]
		public async Task A_path_names_its_department_courses_and_trainings_as_set_dependencies_but_not_academy_steps()
		{
			var paths = new Paths();
			(await paths.Source.GetDependenciesAsync(Publisher, 7)).Should().BeEquivalentTo(new[]
			{
				new KeyValuePair<ContentSetItemKinds, int>(ContentSetItemKinds.LmsCourse, 11), new KeyValuePair<ContentSetItemKinds, int>(ContentSetItemKinds.Training, 12)
			});
			paths.Source.NeedsLms.Should().BeTrue();
		}

		[Test]
		public async Task A_path_copy_points_at_the_subscribers_copies_and_waits_until_they_exist()
		{
			var paths = new Paths();
			var described = await paths.Source.DescribeAsync(Publisher, 7);
			var context = Context(7);
			context.Source = described;

			(await paths.Source.CreateCopyAsync(context)).Problem.Should().Be(LmsPathSetItemSource.ProblemStepMissing);
			paths.Rows.Should().ContainSingle("nothing is written until every step has a copy");

			paths.Copied(ContentSetItemKinds.LmsCourse, 11, 111);
			paths.Copied(ContentSetItemKinds.Training, 12, 112);
			var outcome = await paths.Source.CreateCopyAsync(context);
			outcome.Succeeded.Should().BeTrue();
			var local = paths.Rows.Single(r => r.LmsPathId == outcome.LocalItemId);
			local.DepartmentId.Should().Be(Subscriber);
			local.IssuesCertificationTypeId.Should().BeNull("a certification type is the publisher's own id");
			paths.Steps.Where(s => s.LmsPathId == local.LmsPathId).OrderBy(s => s.SortOrder).Select(s => (s.Kind, s.TargetRefId, s.TargetSystemKey)).Should().Equal(
				((int)LmsPathStepKinds.Course, 111, (string)null), ((int)LmsPathStepKinds.Training, 112, (string)null), ((int)LmsPathStepKinds.Course, 3, "academy:course:x"));
			paths.Steps.Single(s => s.LmsPathId == local.LmsPathId && s.SortOrder == 2).PrerequisiteOrders.Should().Be("1");
			paths.Attributions.Verify(a => a.SaveForSubjectAsync(It.Is<ContentAttribution>(c => c.SubjectKind == (int)ContentAttributionSubjects.LmsPath && c.SubjectId == local.LmsPathId),
				It.IsAny<CancellationToken>()), Times.Once);

			// A publisher's edit (a renamed path, a step moved) is a new version.
			paths.Rows.Single(r => r.LmsPathId == 7).Name = "Driver path 2027";
			(await paths.Source.DescribeAsync(Publisher, 7)).VersionId.Should().NotBe(described.VersionId);
		}

		#endregion Learning paths

		#region Records deployment partner

		private sealed class Deployments
		{
			public readonly RmsExternalOrder Order = new RmsExternalOrder { RmsExternalOrderId = "order-1", DepartmentId = Subscriber, RecordId = "rec-1", OrderNumber = "CA-1", SourceSystem = "IROC", RowVersion = 3 };
			public readonly RmsExternalOrderFill Fill = new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-1", DepartmentId = Subscriber, RmsExternalOrderId = "order-1", RequestNumber = "O-12", ResourceType = "Engine", Status = (int)RmsDeploymentFillStatus.Accepted, FilledOn = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc) };
			public readonly DepartmentLink Link = new DepartmentLink { DepartmentLinkId = 5, DepartmentId = Subscriber, LinkedDepartmentId = Publisher, LinkEnabled = true };
			public readonly Mock<ISignatureRequestService> Requests = new Mock<ISignatureRequestService>();
			public readonly Mock<IRmsExternalOrderFillsRepository> Fills = new Mock<IRmsExternalOrderFillsRepository>();
			public MultiSignerRequestInput Requested;
			public readonly RecordDeploymentPartnerService Service;
			public readonly RecordDeploymentPartnerAcceptanceSource Source;

			public Deployments()
			{
				var orders = new Mock<IRmsExternalOrdersRepository>();
				orders.Setup(o => o.GetByIdForDepartmentAsync(Subscriber, "order-1", false)).ReturnsAsync(() => Order);
				orders.Setup(o => o.TrySetPartnerAsync(Subscriber, "order-1", It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>(), 3, It.IsAny<CancellationToken>()))
					.ReturnsAsync((int d, string id, int? link, int? partner, string u, long v, CancellationToken _) => { Order.PartnerDepartmentLinkId = link; Order.PartnerDepartmentId = partner; return true; });
				Fills.Setup(f => f.GetByIdForDepartmentAsync(Subscriber, "fill-1")).ReturnsAsync(() => Fill);
				Fills.Setup(f => f.GetForOrderAsync(Subscriber, "order-1")).ReturnsAsync(() => new[] { Fill });
				Fills.Setup(f => f.GetAllPartnerSignRequestedAsync(It.IsAny<int>())).ReturnsAsync(() => Fill.PartnerSignStatus == (int)RmsDeploymentPartnerSignStatuses.Requested ? new[] { Fill } : new RmsExternalOrderFill[0]);
				Fills.Setup(f => f.UpdatePartnerSignAsync(It.IsAny<RmsExternalOrderFill>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var links = new Mock<IDepartmentLinksService>();
				links.Setup(l => l.GetLinkByIdAsync(5)).ReturnsAsync(() => Link);
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Name = "Dept " + id });
				var authorization = new Mock<IRecordsAuthorizationService>();
				authorization.Setup(a => a.CanUserViewRecordAsync("officer", "rec-1", Subscriber)).ReturnsAsync(true);
				authorization.Setup(a => a.HasPermissionAsync("officer", Subscriber, PermissionTypes.CreateRecord)).ReturnsAsync(true);
				Requests.Setup(r => r.CreateMultiSignerAsync(It.IsAny<MultiSignerRequestInput>(), It.IsAny<CancellationToken>()))
					.Callback((MultiSignerRequestInput input, CancellationToken _) => Requested = input)
					.ReturnsAsync(new SignatureRequestResult { Request = new SignatureRequestRecord { SignatureRequestId = 88, DepartmentId = Publisher } });
				Service = new RecordDeploymentPartnerService(orders.Object, Fills.Object, links.Object, departments.Object, authorization.Object,
					new Lazy<ISignatureRequestService>(() => Requests.Object), Mock.Of<IEventAggregator>());
				Source = new RecordDeploymentPartnerAcceptanceSource(orders.Object, Fills.Object, links.Object, departments.Object, new Lazy<IRecordDeploymentPartnerService>(() => Service));
			}
		}

		[Test]
		public async Task Naming_a_linked_partner_asks_its_admins_to_accept_each_accepted_fill_at_L2_over_the_link()
		{
			var world = new Deployments();
			(await world.Service.SetPartnerAsync(Subscriber, "intruder", "order-1", 5)).Should().Be(RecordDeploymentPartnerErrors.NotAuthorized);
			(await world.Service.SetPartnerAsync(Subscriber, "officer", "order-1", 5)).Should().Be(RecordDeploymentPartnerErrors.None);

			world.Order.PartnerDepartmentId.Should().Be(Publisher);
			world.Requested.Should().NotBeNull();
			world.Requested.DepartmentId.Should().Be(Publisher, "the request lives in the partner department");
			world.Requested.SubjectKind.Should().Be(SignatureSubjectKinds.RecordDeploymentPartnerAcceptance);
			world.Requested.SubjectId.Should().Be("1:fill-1");
			world.Requested.Steps.Single().Level.Should().Be(SignatureLevels.StepUp);
			world.Requested.Steps.Single().Eligibility.DepartmentAdmins.Should().BeTrue();
			world.Fill.PartnerSignStatus.Should().Be((int)RmsDeploymentPartnerSignStatuses.Requested);
			world.Fill.PartnerSignRequestId.Should().Be(88);

			var info = await world.Source.DescribeAsync(Publisher, "1:fill-1", "partner-admin");
			info.Should().NotBeNull();
			info.RevisionSha256.Should().Be(SignaturePolicy.HashArtifact(RecordDeploymentPartnerSubject.Canonical(world.Order, world.Fill, "Dept 1")));
			info.DocumentHtml.Should().NotContain("Roster");
			(await world.Source.DescribeAsync(77, "1:fill-1", "outsider")).Should().BeNull("only the named partner reads it");

			await world.Source.OnSignedAsync(new SignatureRequestRecord { SignatureRequestId = 88, DepartmentId = Publisher, SubjectId = "1:fill-1" },
				new SignatureRequestSigner { UserId = "partner-admin", SignedOn = DateTime.UtcNow }, "partner-admin");
			world.Fill.PartnerSignStatus.Should().Be((int)RmsDeploymentPartnerSignStatuses.Signed);
			world.Fill.PartnerSignedByUserId.Should().Be("partner-admin");
		}

		[Test]
		public async Task A_fill_not_yet_accepted_or_a_disabled_link_is_never_routed_and_a_disabled_link_voids_the_open_request()
		{
			var world = new Deployments();
			world.Fill.Status = (int)RmsDeploymentFillStatus.Requested;
			(await world.Service.SetPartnerAsync(Subscriber, "officer", "order-1", 5)).Should().Be(RecordDeploymentPartnerErrors.None);
			world.Requested.Should().BeNull("only an accepted fill asks the partner");

			world.Fill.Status = (int)RmsDeploymentFillStatus.Accepted;
			await world.Service.RequestAsync(Subscriber, "fill-1", "officer");
			world.Fill.PartnerSignStatus.Should().Be((int)RmsDeploymentPartnerSignStatuses.Requested);

			world.Link.LinkEnabled = false;
			(await world.Source.DescribeAsync(Publisher, "1:fill-1", "partner-admin")).Should().BeNull("revoking the link stops the read at once");
			(await world.Service.LapseForLinkAsync(5)).Should().Be(1);
			world.Fill.PartnerSignStatus.Should().Be((int)RmsDeploymentPartnerSignStatuses.Lapsed);
			world.Requests.Verify(r => r.VoidAsync(Publisher, 88, null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

			(await world.Service.SetPartnerAsync(Subscriber, "officer", "order-1", 5)).Should().Be(RecordDeploymentPartnerErrors.NotLinked);
		}

		[Test]
		public void The_partner_subject_parses_only_its_own_shape()
		{
			RecordDeploymentPartnerSubject.TryParse("1:fill-1", out var origin, out var fill).Should().BeTrue();
			origin.Should().Be(1);
			fill.Should().Be("fill-1");
			RecordDeploymentPartnerSubject.TryParse(":fill", out _, out _).Should().BeFalse();
			RecordDeploymentPartnerSubject.TryParse("x:fill", out _, out _).Should().BeFalse();
			RecordDeploymentPartnerSubject.TryParse("1:", out _, out _).Should().BeFalse();
		}

		#endregion Records deployment partner
	}
}
