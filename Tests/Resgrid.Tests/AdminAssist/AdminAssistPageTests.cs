using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>
	/// Admin Assist is one page with Setup Wizard, Setup Report, Explore and Admin AI tabs. The setup tabs follow Admin.Setup;
	/// Admin AI needs Admin.Assist and Ai.AdminAssist, and until then the page runs in setup mode with that tab disabled.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AdminAssistPageTests
	{
		private IHttpContextAccessor _previousAccessor;
		private Mock<IAdminAssistAccessService> _access;
		private Mock<IFeatureToggleService> _flags;
		private AdminAssistController _controller;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{ new Claim(ClaimTypes.PrimarySid, "admin"), new Claim(ClaimTypes.PrimaryGroupSid, "7") }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_access = new Mock<IAdminAssistAccessService>();
			_flags = new Mock<IFeatureToggleService>();
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.IsProtectionEnforcedAsync(7)).ReturnsAsync(false);
			_controller = new AdminAssistController(_access.Object, Mock.Of<IAdminAssistService>(), protection.Object, _flags.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private void Rollout(bool setup, bool assist, bool ai)
		{
			// The access service carries the membership check and each rollout flag; the page reads the AI flag itself.
			_access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(setup);
			_access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(assist);
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminAssist, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = assist });
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AiAdminAssist, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = ai });
		}

		private static (string Page, bool Setup) Page(IActionResult result)
		{
			Assert.That(result, Is.InstanceOf<ViewResult>());
			var view = (ViewResult)result;
			Assert.That(view.ViewName, Is.EqualTo("Index"));
			return ((string)view.ViewData["AdminAssistPage"], (bool)view.ViewData["AdminAssistSetup"]);
		}

		[Test]
		public async Task Before_Admin_AI_launches_every_route_opens_the_page_in_setup_mode()
		{
			Rollout(setup: true, assist: true, ai: false);

			Assert.That(Page(await _controller.Index(default)), Is.EqualTo(("wizard", true)));
			Assert.That(Page(await _controller.SetupWizard(default)), Is.EqualTo(("wizard", true)));
			Assert.That(Page(await _controller.SetupReport(default)), Is.EqualTo(("report", true)));
		}

		[Test]
		public async Task With_Admin_AI_on_the_page_reads_the_workspace()
		{
			Rollout(setup: true, assist: true, ai: true);

			Assert.That(Page(await _controller.Index(default)), Is.EqualTo(("wizard", false)));
			Assert.That(Page(await _controller.SetupReport(default)), Is.EqualTo(("report", false)));
		}

		[Test]
		public async Task Admin_AI_alone_opens_the_page_without_the_setup_rollout()
		{
			Rollout(setup: false, assist: true, ai: true);

			Assert.That(Page(await _controller.Index(default)), Is.EqualTo(("wizard", false)));
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		public async Task Page_is_not_found_when_neither_setup_nor_Admin_AI_is_available(bool assist, bool ai)
		{
			Rollout(setup: false, assist: assist, ai: ai);

			Assert.That(await _controller.Index(default), Is.InstanceOf<NotFoundResult>());
			Assert.That(await _controller.SetupWizard(default), Is.InstanceOf<NotFoundResult>());
		}

		[Test]
		public async Task Plans_belongs_to_Admin_AI()
		{
			Rollout(setup: true, assist: true, ai: false);
			var before = Resgrid.Config.AdminAssistConfig.PlansEnabled;
			try
			{
				Resgrid.Config.AdminAssistConfig.PlansEnabled = true;
				Assert.That(await _controller.Plans(default), Is.InstanceOf<NotFoundResult>());
				Rollout(setup: true, assist: true, ai: true);
				Assert.That(Page(await _controller.Plans(default)), Is.EqualTo(("plans", false)));
			}
			finally { Resgrid.Config.AdminAssistConfig.PlansEnabled = before; }
		}
	}
}
