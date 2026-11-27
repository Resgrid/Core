using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.Academy;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Search;
using Resgrid.Search.Knowledge;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Knowledge;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Academy
{
	/// <summary>
	/// A3 in-product guidance: tours by role, remembered per member (one member's progress never shows for another), "What's new" until
	/// read and filtered by entitlement, the free Help search over lessons the department can see and the Admin Assist reference, and
	/// "How do I…?" routed to knowledge answers over the Academy corpus only when the department has them.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AcademyGuidanceTests
	{
		private const int DepartmentId = 44;
		private const string Alice = "alice";
		private const string Bob = "bob";
		private static readonly DateTime Now = new DateTime(2026, 12, 2, 12, 0, 0, DateTimeKind.Utc);

		private static readonly ConfigurationCatalog Catalog = new ConfigurationCatalog();
		private FakeAcademyGuidanceStates _states;
		private AcademyEntitlements _entitlements;
		private List<List<string>> _requestedKeys;
		private Mock<IDispatchAccessService> _dispatch;
		private AdminAssistReferenceSearch _reference;
		private AcademyGuidanceService _service;

		[SetUp]
		public void SetUp()
		{
			_states = new FakeAcademyGuidanceStates();
			_entitlements = new AcademyEntitlements();
			_requestedKeys = new List<List<string>>();
			var entitlements = new Mock<ILmsEntitlementService>();
			// Like the real service: add-ons are only known when a key asks for them (each costs a billing read).
			entitlements.Setup(e => e.GetEntitlementsAsync(DepartmentId, It.IsAny<IEnumerable<string>>()))
				.Callback((int d, IEnumerable<string> keys) => _requestedKeys.Add(keys.ToList()))
				.ReturnsAsync((int d, IEnumerable<string> keys) => new AcademyEntitlements
				{
					Modules = _entitlements.Modules,
					Flags = _entitlements.Flags,
					Addons = new HashSet<string>(_entitlements.Addons.Where(a => keys.Any(k => string.Equals(k, "addon:" + a, StringComparison.OrdinalIgnoreCase))), StringComparer.OrdinalIgnoreCase)
				});
			_dispatch = new Mock<IDispatchAccessService>();
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.GetTrainingManagementScopeAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync((string u, int d) => TrainingManagementScope.None(u, d));
			_reference = new AdminAssistReferenceSearch(Catalog);
			_service = new AcademyGuidanceService(_states, entitlements.Object, _dispatch.Object, authorization.Object, _reference, Catalog);
		}

		[TearDown]
		public void TearDown() => _reference.Dispose();

		private static AcademyGuidanceActor Actor(string user, bool admin = false, string locale = "en") =>
			new AcademyGuidanceActor { DepartmentId = DepartmentId, UserId = user, Locale = locale, IsDepartmentAdmin = admin };

		#region Tours

		[Test]
		public async Task Each_member_gets_the_tour_for_their_role()
		{
			(await _service.GetDefaultTourKeyAsync(Actor(Alice, admin: true))).Should().Be(AcademyTourManifest.Administrator);
			_dispatch.Setup(d => d.CanUseDispatchAsync(DepartmentId, Bob)).ReturnsAsync(true);
			(await _service.GetDefaultTourKeyAsync(Actor(Bob))).Should().Be(AcademyTourManifest.Dispatcher);
			(await _service.GetDefaultTourKeyAsync(new AcademyGuidanceActor { DepartmentId = DepartmentId, UserId = "carol", MayAssignTraining = true }))
				.Should().Be(AcademyTourManifest.TrainingOfficer);
			(await _service.GetDefaultTourKeyAsync(Actor("dave"))).Should().Be(AcademyTourManifest.Member);
		}

		[Test]
		public async Task Tour_progress_and_dismissal_are_remembered_per_member()
		{
			(await _service.SetTaskDoneAsync(Actor(Alice), "member", "profile", true)).Should().BeTrue();
			(await _service.SetTaskDoneAsync(Actor("ALICE"), "member", "apps", true)).Should().BeTrue("member ids compare without case");

			var alice = (await _service.GetDashboardAsync(Actor(Alice), Now)).Tour;
			var bob = (await _service.GetDashboardAsync(Actor(Bob), Now)).Tour;
			alice.Tasks.Where(t => t.Done).Select(t => t.Key).Should().BeEquivalentTo(new[] { "profile", "apps" });
			bob.Tasks.Should().OnlyContain(t => !t.Done, "one member's progress is never another's");
			_states.Rows.Should().ContainSingle(s => s.GuidanceKey == "tour:member");

			(await _service.DismissAsync(Actor(Alice), "tour:member")).Should().BeTrue();
			(await _service.GetDashboardAsync(Actor(Alice), Now)).Tour.Should().BeNull("a dismissed tour stays closed");
			(await _service.GetDashboardAsync(Actor(Bob), Now)).Tour.Should().NotBeNull();
			(await _service.GetToursAsync(Actor(Alice))).Single(t => t.Key == "member").Should().Match<AcademyTourView>(t => t.Dismissed && t.DoneCount == 2);

			(await _service.ReopenTourAsync(Actor(Alice), "member")).Should().BeTrue();
			(await _service.GetDashboardAsync(Actor(Alice), Now)).Tour.DoneCount.Should().Be(2, "reopening keeps what was done");
		}

		[Test]
		public async Task Finishing_every_task_completes_the_tour_and_a_newer_revision_shows_it_again()
		{
			foreach (var task in AcademyManifests.FindTour("member").Tasks)
				await _service.SetTaskDoneAsync(Actor(Alice), "member", task.Key, true);

			_states.Rows.Single().State.Should().Be((int)AcademyGuidanceStatuses.Completed);
			(await _service.GetDashboardAsync(Actor(Alice), Now)).Tour.Should().BeNull();

			// The member finished an earlier revision of the tour: the new revision's tasks show again.
			var row = _states.Rows.Single();
			row.Revision = 0;
			(await _service.GetDashboardAsync(Actor(Alice), Now)).Tour.Should().NotBeNull();
		}

		[Test]
		public async Task Tasks_the_department_cannot_use_are_left_out_and_unknown_keys_are_refused()
		{
			_dispatch.Setup(d => d.CanUseDispatchAsync(DepartmentId, Bob)).ReturnsAsync(true);
			(await _service.GetDashboardAsync(Actor(Bob), Now)).Tour.Tasks.Select(t => t.Key).Should().NotContain("map", "Mapping is off");
			_entitlements.Modules.Add("Mapping");
			(await _service.GetDashboardAsync(Actor(Bob), Now)).Tour.Tasks.Select(t => t.Key).Should().Contain("map");

			(await _service.SetTaskDoneAsync(Actor(Bob), "dispatcher", "no-such-task", true)).Should().BeFalse();
			(await _service.DismissAsync(Actor(Bob), "tour:no-such-tour")).Should().BeFalse();
			(await _service.DismissAsync(Actor(Bob), "anything-else")).Should().BeFalse();
			_states.Rows.Should().BeEmpty();
		}

		#endregion Tours

		#region What's new

		[Test]
		public async Task Whats_new_shows_until_read_and_follows_entitlement()
		{
			var release = AcademyManifests.WhatsNew.First();
			var view = (await _service.GetDashboardAsync(Actor(Alice), release.PublishedOn.AddDays(1))).WhatsNew;
			view.Should().NotBeNull();
			view.Items.Select(i => i.Key).Should().NotContain("drafting-aids", "the drafting aids need Enhanced AI");
			_entitlements.Addons.Add("EnhancedAi");
			(await _service.GetDashboardAsync(Actor(Alice), release.PublishedOn.AddDays(1))).WhatsNew.Items.Select(i => i.Key).Should().NotContain("drafting-aids",
				"the dashboard never pays for a billing read, so an add-on item waits for the page");
			_requestedKeys.SelectMany(k => k).Should().NotContain(k => k.StartsWith("addon:"));
			(await _service.GetWhatsNewAsync(Actor(Alice), release.Release)).Items.Select(i => i.Key).Should().Contain("drafting-aids", "the page checks the add-on");

			(await _service.GetDashboardAsync(Actor(Alice), release.PublishedOn.AddDays(AcademyGuidanceService.WhatsNewDays + 1))).WhatsNew.Should().BeNull("an old release leaves the dashboard");

			(await _service.DismissAsync(Actor(Alice), release.GuidanceKey)).Should().BeTrue();
			(await _service.GetDashboardAsync(Actor(Alice), release.PublishedOn.AddDays(1))).WhatsNew.Should().BeNull();
			(await _service.GetDashboardAsync(Actor(Bob), release.PublishedOn.AddDays(1))).WhatsNew.Should().NotBeNull();
			(await _service.GetWhatsNewAsync(Actor(Alice), release.Release)).Dismissed.Should().BeTrue("the page still opens, marked read");
		}

		[Test]
		public async Task Whats_new_is_shown_in_the_members_language()
		{
			var release = AcademyManifests.WhatsNew.First();
			var view = await _service.GetWhatsNewAsync(Actor(Alice, locale: "de"), release.Release);

			view.Title.Should().Be(release.Title["de"]);
			view.Items.First().Title.Should().Be(release.Items.First().Title["de"]);
		}

		#endregion What's new

		#region Help search

		[Test]
		public async Task Help_search_finds_lessons_and_reference_without_ai()
		{
			var result = await _service.SearchAsync(Actor(Alice), "dispatch settings");

			result.Lessons.Select(l => l.Id).Should().Contain("06-dispatch-settings");
			result.Lessons.First(l => l.Id == "06-dispatch-settings").Url.Should().StartWith("https://academy.resgrid.com/deep-dive/lessons/06-dispatch-settings");
			result.Docs.Should().Contain(d => d.Kind == AcademyHelpHitKinds.Feature && d.Id == "dispatch-settings" && d.Url.StartsWith("https://docs.resgrid.com/"));
			result.Docs.Should().Contain(d => d.Kind == AcademyHelpHitKinds.Reference);
		}

		[Test]
		public async Task Help_search_hides_lessons_the_department_cannot_use_and_works_in_the_members_language()
		{
			(await _service.SearchAsync(Actor(Alice), "inventory")).Lessons.Select(l => l.Id).Should().NotContain("13-inventory");
			_entitlements.Modules.Add("Inventory");
			(await _service.SearchAsync(Actor(Alice), "inventory")).Lessons.Select(l => l.Id).Should().Contain("13-inventory");

			var german = AcademyManifests.FindLesson("05-calls").Title["de"];
			var word = german.Split(' ', ':', ',').Where(w => w.Length >= 6).OrderByDescending(w => w.Length).First();
			(await _service.SearchAsync(Actor(Alice, locale: "de"), word)).Lessons.Select(l => l.Id).Should().Contain("05-calls");
		}

		[Test]
		public async Task Help_search_gives_members_documentation_and_administrators_the_setting_page()
		{
			var member = await _service.SearchAsync(Actor(Bob), "Mapbox token");
			var admin = await _service.SearchAsync(Actor(Alice, admin: true), "Mapbox token");

			member.Docs.Where(d => d.Kind == AcademyHelpHitKinds.Reference).Should().NotBeEmpty().And.OnlyContain(d => d.PageUrl == null);
			admin.Docs.Where(d => d.Kind == AcademyHelpHitKinds.Reference).Should().Contain(d => d.PageUrl != null && d.PageUrl.StartsWith("/User/"));
			(await _service.SearchAsync(Actor(Bob), "  ")).HasResults.Should().BeFalse();
			(await _service.SearchAsync(Actor(Bob), new string('a', 600))).Query.Length.Should().Be(AcademyGuidanceService.MaxQueryLength);
		}

		#endregion Help search

		#region "How do I…?" routing

		[Test]
		public void How_do_I_retrieves_from_the_academy_corpus_only()
		{
			var academy = new KnowledgeChunk { SourceKind = (int)KnowledgeSourceKinds.Academy, ItemId = "05-calls" };
			var protocol = new KnowledgeChunk { SourceKind = (int)KnowledgeSourceKinds.Protocol, ItemId = "4" };

			KnowledgeRetriever.InScope(academy, (int)KnowledgeSourceKinds.Academy, null).Should().BeTrue();
			KnowledgeRetriever.InScope(protocol, (int)KnowledgeSourceKinds.Academy, null).Should().BeFalse("a Help question never reads department content");
			KnowledgeRetriever.InScope(protocol, (int)KnowledgeSourceKinds.Protocol, null).Should().BeFalse("other kinds still need an item");
			KnowledgeRetriever.InScope(protocol, (int)KnowledgeSourceKinds.Protocol, "4").Should().BeTrue();
		}

		[Test]
		public async Task How_do_I_reaches_knowledge_answers_only_with_the_entitlement()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Alice), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test"))
			};
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			try
			{
				KnowledgeAskRequest asked = null;
				var knowledge = new Mock<IAiKnowledgeService>();
				knowledge.Setup(k => k.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>()))
					.Callback((KnowledgeAskRequest r, CancellationToken _) => asked = r)
					.ReturnsAsync(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.NotEntitled });
				var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.Knowledge>>();
				localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
				var controller = new KnowledgeController(knowledge.Object, Mock.Of<IKnowledgeGapsService>(), Mock.Of<IKnowledgeIndexService>(), Mock.Of<IProtocolAccessService>(),
					Mock.Of<IProtocolLibraryService>(), Mock.Of<IAuthorizationService>(), departments.Object, Mock.Of<IUserProfileService>(), localizer.Object)
				{
					ControllerContext = new ControllerContext { HttpContext = http },
					TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
				};

				var input = new KnowledgeAskInput { Question = "How do I add a unit?", Surface = (int)KnowledgeAskSurfaces.Help, ScopeKind = (int)KnowledgeSourceKinds.Academy };
				(await controller.Ask(input, CancellationToken.None)).Should().BeOfType<NotFoundResult>("a department without knowledge answers gets nothing");
				asked.ScopeKind.Should().Be((int)KnowledgeSourceKinds.Academy, "the Academy corpus is scoped without an item");
				asked.ScopeItemId.Should().BeNull();
				asked.Surface.Should().Be((int)KnowledgeAskSurfaces.Help);

				knowledge.Setup(k => k.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>()))
					.Callback((KnowledgeAskRequest r, CancellationToken _) => asked = r)
					.ReturnsAsync(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.NotCovered, Nearest = new List<KnowledgeCitation>() });
				(await controller.Ask(input, CancellationToken.None)).Should().BeOfType<JsonResult>();

				// Any other kind still needs an item: a protocol scope without one asks the whole department corpus.
				await controller.Ask(new KnowledgeAskInput { Question = "x", ScopeKind = (int)KnowledgeSourceKinds.Protocol }, CancellationToken.None);
				asked.ScopeKind.Should().BeNull();
			}
			finally
			{
				ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}

		#endregion "How do I…?" routing
	}
}
