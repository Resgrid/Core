using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.AuthoringAids;
using Resgrid.Web.Helpers;
using AidsText = Resgrid.Localization.Areas.User.Knowledge.AuthoringAids;

namespace Resgrid.Tests.Knowledge
{
	/// <summary>K2 web endpoints: the free tier gets 404 (the panel never shows), a draft comes back for the panel and nothing is saved.</summary>
	[TestFixture, NonParallelizable]
	public class AuthoringAidsControllerTests
	{
		private const int DepartmentId = 12;
		private const string Author = "author-1";

		private Mock<IAuthoringAidService> _aids;
		private AuthoringAidsController _controller;
		private IHttpContextAccessor _previous;

		[SetUp]
		public void SetUp()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Author), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test"))
			};
			_previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_aids = new Mock<IAuthoringAidService>(MockBehavior.Strict);
			var localizer = new Mock<IStringLocalizer<AidsText>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key == "DroppedNotice" ? "{0} left out" : key));
			_controller = new AuthoringAidsController(_aids.Object, localizer.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previous;

		[Test]
		public async Task The_free_tier_gets_not_found_from_both_endpoints()
		{
			_aids.Setup(a => a.GetStatusAsync(DepartmentId, Author, It.IsAny<CancellationToken>())).ReturnsAsync(new AuthoringAidStatus { Reason = AuthoringAidOutcomes.NotEntitled });
			_aids.Setup(a => a.DraftAsync(It.IsAny<AuthoringAidRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AuthoringAidDraft { Outcome = AuthoringAidOutcomes.NotEntitled });

			(await _controller.Status(CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			(await _controller.Draft(new AuthoringAidInput { Kind = 0, SourceKind = 1, SourceId = 3 }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task A_draft_is_returned_for_review_with_the_left_out_count()
		{
			AuthoringAidRequest asked = null;
			_aids.Setup(a => a.DraftAsync(It.IsAny<AuthoringAidRequest>(), It.IsAny<CancellationToken>()))
				.Callback((AuthoringAidRequest r, CancellationToken _) => asked = r)
				.ReturnsAsync(new AuthoringAidDraft { Outcome = AuthoringAidOutcomes.Drafted, Kind = 0, Dropped = 2, Questions = { new AuthoringAidQuestionDraft { Text = "Q", Options = { "a", "b" } } } });

			var result = await _controller.Draft(new AuthoringAidInput { Kind = 0, SourceKind = 1, SourceId = 0, ContentHtml = "<p>text</p>", ExcludeFromAi = false, QuestionCount = 3 }, CancellationToken.None);

			var view = result.Should().BeOfType<JsonResult>().Subject.Value.Should().BeOfType<AuthoringAidDraftView>().Subject;
			view.Outcome.Should().Be(AuthoringAidOutcomes.Drafted);
			view.Notice.Should().Be("2 left out");
			view.Questions.Should().ContainSingle();
			asked.DepartmentId.Should().Be(DepartmentId);
			asked.UserId.Should().Be(Author);
			asked.ContentHtml.Should().Be("<p>text</p>");
			_aids.VerifyAll();
		}
	}
}
