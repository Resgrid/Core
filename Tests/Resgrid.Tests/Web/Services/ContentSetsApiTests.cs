using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.ContentSets;
using Resgrid.Web.Services.Models.v4.Lms;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Round 10 A3 follow-up: the v4 set and sponsorship endpoints run as the caller in the caller's department, map the set service's answers
	/// (404 missing, 403 not allowed, 200 failure with the error code for a business refusal) and expose the inbox, decisions and proposals.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ContentSetsApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "api-user";

		private Mock<IContentSetService> _sets;
		private Mock<ILmsSponsorshipService> _sponsorships;
		private ContentSetActor _actor;

		[SetUp]
		public void SetUp()
		{
			_actor = new ContentSetActor { DepartmentId = DepartmentId, UserId = UserId, IsCurrentMember = true, CanManage = true };
			_sets = new Mock<IContentSetService>();
			_sets.Setup(s => s.GetActorAsync(DepartmentId, UserId)).ReturnsAsync(() => _actor);
			_sponsorships = new Mock<ILmsSponsorshipService>();
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
		}

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = null;

		private V4.ContentSetsController Sets() => new V4.ContentSetsController(_sets.Object) { ControllerContext = new ControllerContext { HttpContext = ApiClaims._httpContextAccessor.HttpContext } };

		private V4.LmsSponsorshipsController Sponsorships() => new V4.LmsSponsorshipsController(_sets.Object, _sponsorships.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = ApiClaims._httpContextAccessor.HttpContext }
		};

		[Test]
		public void Every_endpoint_is_authenticated_and_every_mutation_is_a_post()
		{
			foreach (var type in new[] { typeof(V4.ContentSetsController), typeof(V4.LmsSponsorshipsController) })
			{
				type.GetCustomAttributes<AuthorizeAttribute>().Should().NotBeEmpty();
				foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.Name.StartsWith("Get", StringComparison.Ordinal)))
					method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull(type.Name + "." + method.Name + " changes something");
			}
		}

		[Test]
		public async Task The_inbox_maps_the_rows_and_refuses_a_caller_who_is_not_a_current_member()
		{
			_sets.Setup(s => s.GetInboxAsync(_actor, true)).ReturnsAsync(new List<ContentSetInboxRow>
			{
				new ContentSetInboxRow
				{
					Update = new ContentSetUpdate { ContentSetUpdateId = 5, Title = "Cardiac arrest", ItemKind = (int)ContentSetItemKinds.Protocol, Status = (int)ContentSetUpdateStatuses.Pending,
						UpdateKind = (int)ContentSetUpdateKinds.NewVersion, SourceVersionLabel = "4.0" },
					SetName = "County", PublisherName = "Dept 40", LockMode = ContentSetLockModes.Locked, CanDecline = false
				}
			});
			var ok = (await Sets().GetInbox()).Result.Should().BeOfType<OkObjectResult>().Subject;
			var row = ((ContentSetInboxResult)ok.Value).Data.Single();
			row.ContentSetUpdateId.Should().Be(5);
			row.ItemKind.Should().Be("Protocol");
			row.Status.Should().Be("Pending");
			row.LockMode.Should().Be("Locked");
			row.CanDecline.Should().BeFalse();

			_actor = new ContentSetActor { DepartmentId = DepartmentId, UserId = UserId };
			(await Sets().GetInbox()).Result.Should().BeOfType<ForbidResult>();
		}

		[Test]
		public async Task Decisions_map_missing_to_404_forbidden_to_403_and_a_business_refusal_to_a_failure_with_its_code()
		{
			_sets.Setup(s => s.AcceptUpdateAsync(_actor, 1, null, It.IsAny<CancellationToken>())).ReturnsAsync(new ContentSetResult());
			_sets.Setup(s => s.DeclineUpdateAsync(_actor, 2, It.IsAny<CancellationToken>())).ReturnsAsync(ContentSetResult.Fail(ContentSetErrors.LockedCannotDecline));
			_sets.Setup(s => s.DeclineUpdateAsync(_actor, 3, It.IsAny<CancellationToken>())).ReturnsAsync(ContentSetResult.Fail(ContentSetErrors.NotFound));
			_sets.Setup(s => s.DeclineUpdateAsync(_actor, 4, It.IsAny<CancellationToken>())).ReturnsAsync(ContentSetResult.Fail(ContentSetErrors.NotAuthorized));

			var accepted = (ContentSetActionResult)((await Sets().AcceptUpdate(new ContentSetAcceptInput { ContentSetUpdateId = 1 }, CancellationToken.None)).Result as OkObjectResult).Value;
			accepted.Status.Should().Be("success");
			var refused = (ContentSetActionResult)((await Sets().DeclineUpdate(new ContentSetUpdateIdInput { ContentSetUpdateId = 2 }, CancellationToken.None)).Result as OkObjectResult).Value;
			refused.Status.Should().Be("failure");
			refused.Error.Should().Be(nameof(ContentSetErrors.LockedCannotDecline));
			(await Sets().DeclineUpdate(new ContentSetUpdateIdInput { ContentSetUpdateId = 3 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await Sets().DeclineUpdate(new ContentSetUpdateIdInput { ContentSetUpdateId = 4 }, CancellationToken.None)).Result.Should().BeOfType<ForbidResult>();
			(await Sets().DeferUpdate(new ContentSetDeferInput { ContentSetUpdateId = 1 }, CancellationToken.None)).Result.Should().BeOfType<BadRequestResult>();
			(await Sets().Propose(new ContentSetProposeInput { ContentSetCopyId = 1, Body = " " }, CancellationToken.None)).Result.Should().BeOfType<BadRequestResult>();
		}

		[Test]
		public async Task A_member_sees_the_proposals_it_sent_and_only_a_manager_also_sees_those_received()
		{
			var sent = new ContentSetProposalView { Proposal = new ContentSetProposal { ContentSetProposalId = 1, Body = "Fix dose" }, SetName = "County" };
			var received = new ContentSetProposalView { Proposal = new ContentSetProposal { ContentSetProposalId = 2, Body = "Add step" }, SetName = "Ours" };
			_sets.Setup(s => s.GetProposalsFromDepartmentAsync(It.IsAny<ContentSetActor>())).ReturnsAsync(new List<ContentSetProposalView> { sent });
			_sets.Setup(s => s.GetProposalsToPublisherAsync(It.IsAny<ContentSetActor>(), false)).ReturnsAsync(new List<ContentSetProposalView> { received });

			var manager = (ContentSetProposalsResult)((await Sets().GetProposals()).Result as OkObjectResult).Value;
			manager.Data.Received.Select(p => p.ContentSetProposalId).Should().Equal(2);
			manager.Data.Sent.Select(p => p.ContentSetProposalId).Should().Equal(1);

			_actor = new ContentSetActor { DepartmentId = DepartmentId, UserId = UserId, IsCurrentMember = true };
			var member = (ContentSetProposalsResult)((await Sets().GetProposals()).Result as OkObjectResult).Value;
			member.Data.Received.Should().BeEmpty();
			member.Data.Sent.Should().ContainSingle();
		}

		[Test]
		public async Task Sponsorships_list_for_a_manager_and_map_the_sponsorship_errors()
		{
			_sponsorships.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<LmsSponsorshipView>
			{
				new LmsSponsorshipView { Sponsorship = new LmsSponsorship { LmsSponsorshipId = 9, DepartmentId = 20, SponsorDepartmentId = DepartmentId, SeatLimit = 15 }, SponsoredName = "Dept 20", SeatsUsed = 4, LinkOk = true }
			});
			_sponsorships.Setup(s => s.SponsorAsync(_actor, 20, 15, It.IsAny<CancellationToken>())).ReturnsAsync(LmsSponsorshipErrors.SponsorNeedsLms);
			_sponsorships.Setup(s => s.EndAsync(_actor, 9, It.IsAny<CancellationToken>())).ReturnsAsync(LmsSponsorshipErrors.None);
			_sponsorships.Setup(s => s.EndAsync(_actor, 8, It.IsAny<CancellationToken>())).ReturnsAsync(LmsSponsorshipErrors.NotFound);

			var list = (LmsSponsorshipsResult)((await Sponsorships().GetSponsorships()).Result as OkObjectResult).Value;
			list.Data.Sponsorships.Single().Should().Match<LmsSponsorshipData>(s => s.LmsSponsorshipId == 9 && s.SponsoredDepartmentId == 20 && s.SeatLimit == 15 && s.SeatsUsed == 4 && s.Status == "Active");
			var refused = (LmsSponsorshipActionResult)((await Sponsorships().Sponsor(new LmsSponsorInput { SponsoredDepartmentId = 20, SeatLimit = 15 }, CancellationToken.None)).Result as OkObjectResult).Value;
			refused.Error.Should().Be(nameof(LmsSponsorshipErrors.SponsorNeedsLms));
			((LmsSponsorshipActionResult)((await Sponsorships().EndSponsorship(new LmsSponsorshipIdInput { LmsSponsorshipId = 9 }, CancellationToken.None)).Result as OkObjectResult).Value)
				.Status.Should().Be("success");
			(await Sponsorships().EndSponsorship(new LmsSponsorshipIdInput { LmsSponsorshipId = 8 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();

			_actor = new ContentSetActor { DepartmentId = DepartmentId, UserId = UserId, IsCurrentMember = true };
			(await Sponsorships().GetSponsorships()).Result.Should().BeOfType<ForbidResult>();
		}
	}
}
