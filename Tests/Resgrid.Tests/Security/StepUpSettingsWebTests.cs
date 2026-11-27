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
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;
using WebClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Round 10 A5: Security → Step-up verification. Administrators only; an administrator who is not the managing member cannot change
	/// how protected data is verified even by posting it; the page shows, per area, who could not complete the step-up.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class StepUpSettingsWebTests
	{
		private const string UserId = "user-1";
		private const string ManagingId = "owner-1";
		private const int DepartmentId = 42;

		private Microsoft.AspNetCore.Http.IHttpContextAccessor _previous;
		private Mock<IStepUpPolicyService> _policy;
		private List<IReadOnlyList<StepUpRuleInput>> _saved;
		private Mock<IStepUpCapabilityService> _capabilities;

		[SetUp]
		public void SetUp()
		{
			_previous = WebClaims._httpContextAccessor;
			_saved = new();
			_policy = StepUpTestPolicies.Of(a => a == StepUpArea.ProtectedData ? StepUpTestPolicies.Rule(a, methods: StepUpMethodClasses.Sso) : null);
			_policy.Setup(p => p.SaveAsync(DepartmentId, It.IsAny<IReadOnlyList<StepUpRuleInput>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.Callback((int _, IReadOnlyList<StepUpRuleInput> rules, string _, bool _, CancellationToken _) => _saved.Add(rules))
				.ReturnsAsync(StepUpPolicySaveOutcome.Saved);
			_capabilities = new Mock<IStepUpCapabilityService>();
			_capabilities.Setup(c => c.GetReadinessAsync(DepartmentId, It.IsAny<IReadOnlyList<StepUpRule>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, IReadOnlyList<StepUpRule> rules, int _, CancellationToken _) => (IReadOnlyList<StepUpReadiness>)rules.Select(r => new StepUpReadiness
				{
					Rule = r, MemberCount = r.Area == StepUpArea.Signatures ? 2 : 0,
					Members = r.Area == StepUpArea.Signatures ? new List<StepUpMemberGap> { new() { UserId = "u-1", Name = "Ann Lee", Reason = StepUpGapReasons.NoSecondFactor } } : new List<StepUpMemberGap>()
				}).ToList());
		}

		[TearDown]
		public void TearDown() => WebClaims._httpContextAccessor = _previous;

		private StepUpSettingsController Controller(bool admin = true, string userId = UserId)
		{
			var claims = new List<Claim> { new(ClaimTypes.PrimarySid, userId), new(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) };
			if (admin)
				claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			WebClaims._httpContextAccessor = accessor.Object;

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = ManagingId });
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			return new StepUpSettingsController(_policy.Object, _capabilities.Object, departments.Object, Mock.Of<IDepartmentDataProtectionService>(), localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		private static StepUpSettingsView Posted(bool adpPassword) => new()
		{
			Areas = new List<StepUpAreaRowView>
			{
				new() { Area = (int)StepUpArea.Signatures, Required = false, AllowPassword = true },
				new() { Area = (int)StepUpArea.ProtectedData, Required = true, AllowPassword = adpPassword }
			}
		};

		[Test]
		public async Task A_member_who_is_not_an_administrator_is_sent_back()
		{
			(await Controller(admin: false).Index(CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			(await Controller(admin: false).Index(Posted(true), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task An_administrator_who_is_not_the_managing_member_cannot_change_protected_data_by_posting_it()
		{
			await Controller().Index(Posted(adpPassword: true), CancellationToken.None);

			var rules = _saved.Should().ContainSingle().Subject;
			rules.Single(r => r.Area == StepUpArea.Signatures).Should().Match<StepUpRuleInput>(r => !r.Required && r.AllowPassword);
			rules.Single(r => r.Area == StepUpArea.ProtectedData).Should().Match<StepUpRuleInput>(r => !r.AllowPassword && r.AllowSso,
				"the saved protected-data methods are kept exactly");
			_policy.Verify(p => p.SaveAsync(DepartmentId, It.IsAny<IReadOnlyList<StepUpRuleInput>>(), UserId, false, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_managing_member_saves_protected_data_methods_as_posted()
		{
			await Controller(userId: ManagingId).Index(Posted(adpPassword: true), CancellationToken.None);
			_saved.Single().Single(r => r.Area == StepUpArea.ProtectedData).AllowPassword.Should().BeTrue();
			_policy.Verify(p => p.SaveAsync(DepartmentId, It.IsAny<IReadOnlyList<StepUpRuleInput>>(), ManagingId, true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_page_shows_every_area_with_who_could_not_complete_it_were_it_required()
		{
			var view = (ViewResult)await Controller().Index(CancellationToken.None);
			var model = (StepUpSettingsView)view.Model;

			model.Areas.Select(a => a.AreaValue).Should().Equal(StepUpArea.Signatures, StepUpArea.Acknowledgments, StepUpArea.Approvals, StepUpArea.ProtectedData);
			model.Areas.Single(a => a.AreaValue == StepUpArea.Signatures).Should().Match<StepUpAreaRowView>(a => a.UnableCount == 2 && a.UnableMembers.Single().Name == "Ann Lee");
			model.Areas.Single(a => a.AreaValue == StepUpArea.ProtectedData).AllowSso.Should().BeTrue();
			_capabilities.Verify(c => c.GetReadinessAsync(DepartmentId, It.Is<IReadOnlyList<StepUpRule>>(rules => rules.All(r => r.Required)), It.IsAny<int>(),
				It.IsAny<CancellationToken>()), Times.Once, "the warning is computed as if each area were required, so it shows before one is turned on");
		}
	}
}
