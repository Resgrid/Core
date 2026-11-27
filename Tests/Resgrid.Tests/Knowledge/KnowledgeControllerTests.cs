using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Knowledge;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using KnowledgeText = Resgrid.Localization.Areas.User.Knowledge.Knowledge;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>K1 web endpoints: Ask maps the answer for the panel (camelCase, links to the exact section), the gaps report is hidden from departments without Ai.Knowledge, and a gap starts a training from its question without putting it in the URL.</summary>
	[TestFixture, NonParallelizable]
	public class KnowledgeControllerTests
	{
		private const int DepartmentId = 7;
		private const string Member = "member-1";

		private Mock<IAiKnowledgeService> _knowledge;
		private Mock<IKnowledgeGapsService> _gaps;
		private Mock<IAuthorizationService> _authorization;
		private KnowledgeController _controller;
		private IHttpContextAccessor _previous;

		[SetUp]
		public void SetUp()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.View)
				}, "Test"))
			};
			_previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_knowledge = new Mock<IAiKnowledgeService>();
			_gaps = new Mock<IKnowledgeGapsService>();
			_authorization = new Mock<IAuthorizationService>();
			var localizer = new Mock<IStringLocalizer<KnowledgeText>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key == "VersionLabel" ? "version {0}" : key == "EffectiveLabel" ? "in effect since {0}" : key));
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });

			_controller = new KnowledgeController(_knowledge.Object, _gaps.Object, Mock.Of<IKnowledgeIndexService>(), Mock.Of<IProtocolAccessService>(), Mock.Of<IProtocolLibraryService>(),
				_authorization.Object, departments.Object, Mock.Of<IUserProfileService>(), localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previous;

		[Test]
		public async Task Ask_returns_the_answer_for_the_panel_with_links_to_the_exact_section()
		{
			KnowledgeAskRequest asked = null;
			_knowledge.Setup(k => k.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>()))
				.Callback((KnowledgeAskRequest r, CancellationToken _) => asked = r)
				.ReturnsAsync(new KnowledgeAnswer
				{
					Outcome = KnowledgeOutcomes.Answered,
					Parts = new List<KnowledgeAnswerPart> { new KnowledgeAnswerPart { Text = "Stage uphill.", Refs = new List<string> { "S1" } } },
					Citations = new List<KnowledgeCitation> { new KnowledgeCitation { Ref = "S1", SourceKind = 0, ItemId = "4", VersionId = "40", VersionLabel = "2.1", Anchor = "p-scene-safety", Title = "SOG 4" } },
					EmbeddingsUnavailable = true,
					ExchangeId = "e1"
				});

			var result = await _controller.Ask(new KnowledgeAskInput { Question = "Where do we stage?", Surface = 1, ScopeKind = 0, ScopeItemId = " 4 " }, CancellationToken.None) as JsonResult;

			asked.Reader.UserId.Should().Be(Member);
			asked.Reader.CanViewProtocols.Should().BeTrue();
			asked.Reader.CanViewTrainings.Should().BeFalse();
			asked.ScopeItemId.Should().Be("4");
			var json = JsonSerializer.Serialize(result.Value, (JsonSerializerOptions)result.SerializerSettings);
			json.Should().Contain("\"outcome\":\"Answered\"").And.Contain("\"url\":\"/User/Protocols/View/4?versionId=40#p-scene-safety\"")
				.And.Contain("\"version\":\"version 2.1\"").And.Contain("KeywordOnlyNotice").And.Contain("\"exchangeId\":\"e1\"");
		}

		[Test]
		public async Task Ask_is_not_found_for_a_department_without_knowledge()
		{
			_knowledge.Setup(k => k.AskAsync(It.IsAny<KnowledgeAskRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeAnswer { Outcome = KnowledgeOutcomes.NotEntitled });

			(await _controller.Ask(new KnowledgeAskInput { Question = "x" }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task The_gaps_report_is_hidden_without_knowledge_or_without_the_right_to_fix_content()
		{
			_knowledge.Setup(k => k.IsEntitledAsync(DepartmentId)).ReturnsAsync(false);
			(await _controller.Gaps()).Should().BeOfType<NotFoundResult>();

			_knowledge.Setup(k => k.IsEntitledAsync(DepartmentId)).ReturnsAsync(true);
			_gaps.Setup(g => g.CanSeeGapsAsync(DepartmentId, Member)).ReturnsAsync(false);
			(await _controller.Gaps()).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task A_gap_starts_a_new_training_with_its_question_kept_out_of_the_url()
		{
			_knowledge.Setup(k => k.IsEntitledAsync(DepartmentId)).ReturnsAsync(true);
			_gaps.Setup(g => g.CanSeeGapsAsync(DepartmentId, Member)).ReturnsAsync(true);
			_gaps.Setup(g => g.GetGapAsync(DepartmentId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeGap { KnowledgeGapId = 3, DepartmentId = DepartmentId, Question = "How do we handle lithium battery fires?" });
			var scope = TrainingManagementScope.None(Member, DepartmentId);
			scope.ManagesOwn = true;
			_authorization.Setup(a => a.GetTrainingManagementScopeAsync(Member, DepartmentId)).ReturnsAsync(scope);

			var result = await _controller.NewTraining(3, CancellationToken.None) as RedirectToActionResult;

			result.ActionName.Should().Be("New");
			result.ControllerName.Should().Be("Trainings");
			result.RouteValues.Should().NotContainKey("question");
			_controller.TempData[KnowledgeController.TrainingPrefillKey].Should().Be("How do we handle lithium battery fires?");
		}

		[Test]
		public void A_gap_points_at_the_protocol_it_was_asked_from_else_its_closest_protocol()
		{
			KnowledgeController.ProtocolOf(new KnowledgeGap { ContextKind = 0, ContextItemId = "12" }).Should().Be(12);
			KnowledgeController.ProtocolOf(new KnowledgeGap
			{
				NearestSourcesJson = "[{\"SourceKind\":2,\"ItemId\":\"5\"},{\"SourceKind\":0,\"ItemId\":\"9\"}]"
			}).Should().Be(9);
			KnowledgeController.ProtocolOf(new KnowledgeGap { NearestSourcesJson = "[{\"SourceKind\":2,\"ItemId\":\"5\"}]" }).Should().BeNull();
		}
	}
}
