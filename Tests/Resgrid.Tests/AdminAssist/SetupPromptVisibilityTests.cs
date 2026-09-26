using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.ViewComponents;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>The dashboard's Setup Wizard banner is for department administrators only.</summary>
	[TestFixture, NonParallelizable]
	public class SetupPromptVisibilityTests
	{
		private static readonly AdminAssistActor Actor = new(7, "user-1");
		private static readonly ConfigurationCatalog Catalog = new();
		private IHttpContextAccessor _previousAccessor;
		private Mock<IAdminAssistAccessService> _access;
		private Mock<IAdminAssistRepository> _repository;

		[SetUp]
		public void SetUp()
		{
			_previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
			_access = new Mock<IAdminAssistAccessService>(MockBehavior.Strict);
			_repository = new Mock<IAdminAssistRepository>(MockBehavior.Strict);
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task Member_never_sees_the_banner_and_costs_no_flag_or_workspace_read()
		{
			var result = await InvokeAsync(admin: false);

			Assert.That(result, Is.InstanceOf<ContentViewComponentResult>());
			Assert.That(((ContentViewComponentResult)result).Content, Is.Empty);
			_access.VerifyNoOtherCalls(); _repository.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Admin_sees_the_banner_while_setup_is_open()
		{
			_access.Setup(a => a.CanAccessAsync(It.Is<AdminAssistActor>(x => x.DepartmentId == Actor.DepartmentId && x.UserId == Actor.UserId), true, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_repository.Setup(r => r.GetWorkspaceAsync(Actor.DepartmentId, Actor.UserId, Catalog.Version, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SetupWorkspace(Actor.DepartmentId, 0, SetupMode.Fresh, new Dictionary<string, SetupAreaChoice>(), Array.Empty<string>(), Array.Empty<string>(), Catalog.Version, null));

			var result = await InvokeAsync(admin: true);

			Assert.That(result, Is.InstanceOf<ViewViewComponentResult>());
		}

		[Test]
		public async Task Admin_claim_alone_is_not_enough_when_fresh_access_is_refused()
		{
			// The claim may be stale; the fresh membership and rollout check still decides.
			_access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await InvokeAsync(admin: true);

			Assert.That(((ContentViewComponentResult)result).Content, Is.Empty);
			_repository.VerifyNoOtherCalls();
		}

		private async Task<IViewComponentResult> InvokeAsync(bool admin)
		{
			var claims = new List<Claim> { new(ClaimTypes.PrimarySid, Actor.UserId), new(ClaimTypes.PrimaryGroupSid, Actor.DepartmentId.ToString()) };
			if (admin) claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var component = new AdminAssistSetupPromptViewComponent(_access.Object, _repository.Object, Catalog)
			{
				ViewComponentContext = new ViewComponentContext { ViewContext = new ViewContext { HttpContext = http } },
				ViewEngine = Mock.Of<ICompositeViewEngine>()
			};
			return await component.InvokeAsync();
		}
	}
}
