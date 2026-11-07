using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;
using PolicyAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 Counts/GetBadgeCounts passes the member, the asked-for keys and a policy check built on the signed-in principal
	/// to the registry, and returns one entry per count.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CountsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member-user";

		private Mock<IBadgeCountsService> _badgeCounts;
		private Mock<PolicyAuthorizationService> _policies;
		private HashSet<string> _grantedPolicies;
		private CountsController _controller;
		private Activity _activity;
		private BadgeCountContext _context;
		private IReadOnlyCollection<string> _keys;
		private Func<string, Task<bool>> _policyCheck;
		private CancellationToken _cancellationToken;

		[SetUp]
		public void SetUp()
		{
			_badgeCounts = new Mock<IBadgeCountsService>();
			_policies = new Mock<PolicyAuthorizationService>();
			_grantedPolicies = new HashSet<string> { "Call_View" };

			_policies
				.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()))
				.ReturnsAsync((ClaimsPrincipal _, object _, string policy) => _grantedPolicies.Contains(policy) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
			_badgeCounts
				.Setup(x => x.GetBadgeCountsAsync(It.IsAny<BadgeCountContext>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Func<string, Task<bool>>>(), It.IsAny<CancellationToken>()))
				.Callback((BadgeCountContext context, IReadOnlyCollection<string> keys, Func<string, Task<bool>> check, CancellationToken cancellationToken) =>
				{
					_context = context;
					_keys = keys;
					_policyCheck = check;
					_cancellationToken = cancellationToken;
				})
				.ReturnsAsync(new List<BadgeCount>
				{
					new BadgeCount("calls", 2),
					new BadgeCount("weather", 3, new Dictionary<string, int> { ["extreme"] = 1, ["severe"] = 2 })
				});

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("CountsControllerTests").Start();

			_controller = new CountsController(_badgeCounts.Object, _policies.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[TestCase(null, new string[0])]
		[TestCase("", new string[0])]
		[TestCase("calls", new[] { "calls" })]
		[TestCase("calls, chats,Calls", new[] { "calls", "chats" })]
		public void include_list_parses_to_distinct_keys(string include, string[] expected)
		{
			CountsController.ParseInclude(include).Should().Equal(expected);
		}

		[Test]
		public async Task returns_one_entry_per_count_in_a_success_envelope()
		{
			var result = (await _controller.GetBadgeCounts("calls,weather", 7)).Value;

			result.Status.Should().Be(ResponseHelper.Success);
			result.PageSize.Should().Be(2);
			result.Data.Should().HaveCount(2);
			result.Data[0].Key.Should().Be("calls");
			result.Data[0].Count.Should().Be(2);
			result.Data[0].Breakdown.Should().BeNull();
			result.Data[1].Breakdown.Should().Equal(new Dictionary<string, int> { ["extreme"] = 1, ["severe"] = 2 });
			_context.DepartmentId.Should().Be(DepartmentId);
			_context.UserId.Should().Be(UserId);
			_context.ActiveUnitId.Should().Be(7);
			_keys.Should().Equal("calls", "weather");
		}

		[Test]
		public async Task the_policy_check_asks_the_signed_in_principal()
		{
			await _controller.GetBadgeCounts(null, null);

			(await _policyCheck("Call_View")).Should().BeTrue();
			(await _policyCheck("Chat_View")).Should().BeFalse();
		}

		[Test]
		public async Task the_request_cancellation_reaches_the_registry()
		{
			using var aborted = new CancellationTokenSource();

			await _controller.GetBadgeCounts("calls", null, aborted.Token);

			_cancellationToken.Should().Be(aborted.Token);
		}
	}
}
