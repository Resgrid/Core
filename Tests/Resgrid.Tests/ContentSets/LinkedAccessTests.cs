using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Approvals;
using Resgrid.Services.ContentSets;

namespace Resgrid.Tests.ContentSets
{
	/// <summary>
	/// Round 10 A3: the other features that ride on Department Links: reading a linked department's protocols, LMS sponsorship (who may sponsor,
	/// the seats it grants, the courses it runs and how revocation lapses it) and the mutual-aid partner countersignature (routed only over the
	/// agreement's enabled link, signed by the partner's admins, lapsed with the link).
	/// </summary>
	[TestFixture]
	public class LinkedAccessTests
	{
		private const int Sponsor = 40;
		private const int Sponsored = 1;

		private static Mock<IDepartmentLinksService> LinksOver(List<DepartmentLink> links)
		{
			var mock = new Mock<IDepartmentLinksService>();
			mock.Setup(l => l.GetLinkByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => links.FirstOrDefault(l => l.DepartmentLinkId == id));
			mock.Setup(l => l.GetAllLinksForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) => links.Where(l => l.DepartmentId == d || l.LinkedDepartmentId == d).ToList());
			return mock;
		}

		private static Mock<IDepartmentsService> Departments()
		{
			var mock = new Mock<IDepartmentsService>();
			mock.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Name = "Dept " + id, ManagingUserId = "admin-" + id });
			mock.Setup(d => d.GetActiveMemberUserIdsAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => new HashSet<string>(Enumerable.Range(1, 40).Select(i => "u" + id + "-" + i), StringComparer.OrdinalIgnoreCase));
			return mock;
		}

		#region Linked department protocols

		[Test]
		public async Task A_linked_department_reads_only_eligible_published_protocols_the_other_side_shares_toward_it()
		{
			var links = new List<DepartmentLink> { new DepartmentLink { DepartmentLinkId = 1, DepartmentId = 10, LinkedDepartmentId = 20, LinkEnabled = true, DepartmentShareProtocols = true } };
			var protocols = new Mock<IProtocolsService>();
			ProtocolVersion Version(int id, int protocolId) => new ProtocolVersion
			{
				ProtocolVersionId = id, DispatchProtocolId = protocolId, DepartmentId = 10, Status = (int)ProtocolVersionStatuses.Published, Major = 1, ContentHtml = "<p>v" + id + "</p>", ContentHash = new string('a', 64)
			};
			var list = new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = 10, Name = "Open", Code = "A", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 11 },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = 10, Name = "Licensed", Code = "B", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 12, IsLicensedContent = true },
				new DispatchProtocol { DispatchProtocolId = 3, DepartmentId = 10, Name = "Restricted", Code = "C", Status = (int)ProtocolStatuses.Published, CurrentVersionId = 13 }
			};
			protocols.Setup(p => p.GetProtocolSummariesForDepartmentAsync(10, true)).ReturnsAsync(list);
			protocols.Setup(p => p.GetProtocolHeaderByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => list.FirstOrDefault(p => p.DispatchProtocolId == id));
			var library = new Mock<IProtocolLibraryService>();
			library.Setup(l => l.GetDepartmentVersionHeadersAsync(10)).ReturnsAsync(new List<ProtocolVersion> { Version(11, 1), Version(12, 2), Version(13, 3) });
			library.Setup(l => l.GetVersionAsync(10, It.IsAny<int>())).ReturnsAsync((int d, int id) => Version(id, id - 10));
			library.Setup(l => l.GetDepartmentAudiencesAsync(10)).ReturnsAsync(new List<ProtocolAudience> { new ProtocolAudience { DispatchProtocolId = 3, Kind = (int)ContentAudienceKinds.Group, RefId = "5" } });
			library.Setup(l => l.GetAudienceAsync(10, 3)).ReturnsAsync(new List<ProtocolAudience> { new ProtocolAudience { DispatchProtocolId = 3, Kind = (int)ContentAudienceKinds.Group, RefId = "5" } });
			library.Setup(l => l.GetAudienceAsync(10, It.Is<int>(i => i != 3))).ReturnsAsync(new List<ProtocolAudience>());
			library.Setup(l => l.GetCategoriesAsync(It.IsAny<int>())).ReturnsAsync(new List<ProtocolCategory>());
			var service = new LinkedProtocolService(LinksOver(links).Object, Departments().Object, protocols.Object, library.Object);

			var shared = await service.GetSharedProtocolsAsync(20, 1);
			shared.Protocols.Select(p => p.Name).Should().Equal("Open");
			(await service.GetSharedProtocolAsync(20, 1, 1)).BodyHtml.Should().Be("<p>v11</p>");
			(await service.GetSharedProtocolAsync(20, 1, 2)).Should().BeNull("licensed content is never shared");
			(await service.GetSharedProtocolAsync(20, 1, 3)).Should().BeNull("an audience-restricted protocol is never shared");

			// The creator does not read its own protocols back; a third department reads nothing; revoking cuts reads at once.
			(await service.GetSharedProtocolsAsync(10, 1)).Should().BeNull();
			(await service.GetSharedProtocolsAsync(30, 1)).Should().BeNull();
			links[0].DepartmentShareProtocols = false;
			(await service.GetSharedProtocolsAsync(20, 1)).Should().BeNull();
			(await service.GetSharedProtocolAsync(20, 1, 1)).Should().BeNull();
			links[0].DepartmentShareProtocols = true;
			links[0].LinkEnabled = false;
			(await service.GetSharedProtocolAsync(20, 1, 1)).Should().BeNull();
		}

		#endregion Linked department protocols

		#region Sponsorship

		private sealed class SponsorWorld
		{
			public readonly FakeSponsorships Sponsorships = new FakeSponsorships();
			public readonly FakeCopies Copies = new FakeCopies();
			public readonly List<DepartmentLink> Links = new List<DepartmentLink>();
			public readonly Dictionary<int, LmsSubscriptionSnapshot> Subscriptions = new Dictionary<int, LmsSubscriptionSnapshot>();
			public readonly Dictionary<int, int> SeatCounts = new Dictionary<int, int>();
			public readonly Mock<IContentSetNotifier> Notifier = new Mock<IContentSetNotifier>();
			public readonly List<LmsSponsorshipWorkflowEvent> Events = new List<LmsSponsorshipWorkflowEvent>();
			public readonly LmsSponsorshipService Service;

			public SponsorWorld()
			{
				var seats = new Mock<ILmsMemberSeatsRepository>();
				seats.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int d) =>
					Enumerable.Range(1, SeatCounts.TryGetValue(d, out var n) ? n : 0).Select(i => new LmsMemberSeat { DepartmentId = d, UserId = "u" + d + "-" + i, EnabledOn = DateTime.UtcNow.AddDays(-i) }).ToList());
				var access = new Mock<ILmsAccessService>();
				access.Setup(a => a.IsFlagEnabledAsync(It.IsAny<int>())).ReturnsAsync(true);
				access.Setup(a => a.GetSubscriptionAsync(It.IsAny<int>())).ReturnsAsync((int d) => Subscriptions.TryGetValue(d, out var s) ? s : LmsSubscriptionSnapshot.None);
				var learners = new Mock<ILmsExternalLearnersRepository>();
				learners.Setup(l => l.CountCountedAsync(It.IsAny<int>())).ReturnsAsync(0);
				var events = new Mock<IEventAggregator>();
				events.Setup(e => e.SendMessage(It.IsAny<LmsSponsorshipWorkflowEvent>())).Callback<LmsSponsorshipWorkflowEvent>(e => Events.Add(e));
				Service = new LmsSponsorshipService(Sponsorships, seats.Object, learners.Object, access.Object, Departments().Object, LinksOver(Links).Object, Copies, Notifier.Object, events.Object);
			}

			public static ContentSetActor Manager(int department) => new ContentSetActor { DepartmentId = department, UserId = "admin-" + department, IsCurrentMember = true, CanManage = true };
		}

		[Test]
		public async Task Only_a_department_with_an_entitled_add_on_that_shares_training_over_an_enabled_link_can_sponsor()
		{
			var world = new SponsorWorld();
			(await world.Service.SponsorAsync(new ContentSetActor { DepartmentId = Sponsor, UserId = "m", IsCurrentMember = true }, Sponsored, null)).Should().Be(LmsSponsorshipErrors.NotAuthorized);
			(await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null)).Should().Be(LmsSponsorshipErrors.NotShared);
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 3, DepartmentId = Sponsored, LinkedDepartmentId = Sponsor, LinkEnabled = true, DepartmentShareTraining = true });
			(await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null)).Should().Be(LmsSponsorshipErrors.NotShared, "the sponsored side sharing is the wrong direction");
			world.Links[0].LinkedDepartmentShareTraining = true;
			(await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null)).Should().Be(LmsSponsorshipErrors.SponsorNeedsLms);

			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 2 };
			(await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, 10)).Should().Be(LmsSponsorshipErrors.None);
			(await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, 10)).Should().Be(LmsSponsorshipErrors.AlreadySponsored);
			world.Notifier.Verify(n => n.SponsorshipChangedAsync(Sponsored, It.IsAny<int>(), 10, true, "Dept 40", It.IsAny<CancellationToken>()), Times.Once);
			world.Events.Should().Contain(e => e.DepartmentId == Sponsored && e.Trigger == WorkflowTriggerEventType.LmsSponsorshipStarted);
			world.Events.Should().Contain(e => e.DepartmentId == Sponsor && e.Trigger == WorkflowTriggerEventType.LmsSponsorshipStarted);
		}

		[Test]
		public async Task A_grant_seats_the_sponsored_department_in_the_sponsors_packs_while_link_and_add_on_hold()
		{
			var world = new SponsorWorld();
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 3, DepartmentId = Sponsor, LinkedDepartmentId = Sponsored, LinkEnabled = true, DepartmentShareTraining = true });
			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 2 };
			await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null);
			world.SeatCounts[Sponsor] = 30;
			world.SeatCounts[Sponsored] = 4;

			var grant = await world.Service.GetGrantAsync(Sponsored);
			grant.Sponsorship.SponsorDepartmentId.Should().Be(Sponsor);
			grant.Capacity.Should().Be(20, "50 purchased - 30 the sponsor's own - 4 held + the 4 held");
			(await world.Service.GetSponsoredSeatsAsync(Sponsor)).Should().Be(4);

			// The sponsor's add-on lapses, or the link stops sharing training: no grant (fail closed).
			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Lapsed, LapsedSinceUtc = DateTime.UtcNow.AddDays(-1) };
			(await world.Service.GetGrantAsync(Sponsored)).Should().BeNull();
			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 2 };
			world.Links[0].DepartmentShareTraining = false;
			(await world.Service.GetGrantAsync(Sponsored)).Should().BeNull();
		}

		[Test]
		public async Task Sponsored_course_copies_run_while_the_sponsorship_pays_and_lapse_from_when_it_ended()
		{
			var world = new SponsorWorld();
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 3, DepartmentId = Sponsor, LinkedDepartmentId = Sponsored, LinkEnabled = true, DepartmentShareTraining = true });
			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 1 };
			await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null);
			await world.Copies.InsertAsync(new ContentSetCopy { DepartmentId = Sponsored, PublisherDepartmentId = Sponsor, ItemKind = (int)ContentSetItemKinds.LmsCourse, LocalItemId = 900 }, CancellationToken.None);
			await world.Copies.InsertAsync(new ContentSetCopy { DepartmentId = Sponsored, PublisherDepartmentId = 99, ItemKind = (int)ContentSetItemKinds.LmsCourse, LocalItemId = 901 }, CancellationToken.None);

			var running = await world.Service.GetSponsoredCourseIdsAsync(Sponsored);
			running.Should().ContainKey(900).WhoseValue.Should().BeNull();
			running.Should().NotContainKey(901, "only the sponsor's own sets run on its packs");

			// Revoking the link ends the sponsorship; its copies then lapse from that moment.
			world.Links[0].LinkEnabled = false;
			(await world.Service.EndLapsedAsync(3, DateTime.UtcNow)).Should().Be(1);
			var ended = world.Sponsorships.Rows.Single();
			ended.Status.Should().Be((int)LmsSponsorshipStatuses.Ended);
			ended.EndReason.Should().Be((int)LmsSponsorshipEndReasons.LinkRevoked);
			(await world.Service.GetGrantAsync(Sponsored)).Should().BeNull();
			(await world.Service.GetSponsoredCourseIdsAsync(Sponsored))[900].Should().Be(ended.EndedOn);
			world.Notifier.Verify(n => n.SponsorshipChangedAsync(Sponsored, It.IsAny<int>(), It.IsAny<int>(), false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			world.Events.Should().Contain(e => e.Trigger == WorkflowTriggerEventType.LmsSponsorshipEnded && e.EndReason == "LinkRevoked");
		}

		[Test]
		public async Task Either_party_ends_a_sponsorship_and_the_sponsored_side_declines_it()
		{
			var world = new SponsorWorld();
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 3, DepartmentId = Sponsor, LinkedDepartmentId = Sponsored, LinkEnabled = true, DepartmentShareTraining = true });
			world.Subscriptions[Sponsor] = new LmsSubscriptionSnapshot { State = LmsSubscriptionStates.Active, Packs = 1 };
			await world.Service.SponsorAsync(SponsorWorld.Manager(Sponsor), Sponsored, null);
			var id = world.Sponsorships.Rows.Single().LmsSponsorshipId;

			(await world.Service.EndAsync(SponsorWorld.Manager(77), id)).Should().Be(LmsSponsorshipErrors.NotFound);
			(await world.Service.SetSeatLimitAsync(SponsorWorld.Manager(Sponsored), id, 5)).Should().Be(LmsSponsorshipErrors.NotFound, "only the sponsor sets the limit");
			(await world.Service.EndAsync(SponsorWorld.Manager(Sponsored), id)).Should().Be(LmsSponsorshipErrors.None);
			world.Sponsorships.Rows.Single().EndReason.Should().Be((int)LmsSponsorshipEndReasons.DeclinedBySponsored);
		}

		#endregion Sponsorship

		#region Mutual-aid countersignature

		private sealed class CountersignWorld
		{
			public readonly Mock<IMutualAidAgreementsRepository> Agreements = new Mock<IMutualAidAgreementsRepository>();
			public readonly Mock<ISignatureRequestService> Requests = new Mock<ISignatureRequestService>();
			public readonly List<DepartmentLink> Links = new List<DepartmentLink>();
			public readonly MutualAidAgreement Agreement;
			public MultiSignerRequestInput Requested;
			public readonly MutualAidCountersignService Service;
			public readonly MutualAidCountersignatureSource Source;

			public CountersignWorld()
			{
				Agreement = new MutualAidAgreement
				{
					MutualAidAgreementId = 9, DepartmentId = 1, Title = "North county", DepartmentLinkId = 3, PartnerDepartmentId = 40, PartnerName = "County",
					Terms = "Each side sends one engine.", Status = (int)MutualAidAgreementStatuses.Approved, RequiresCountersignature = true
				};
				Links.Add(new DepartmentLink { DepartmentLinkId = 3, DepartmentId = 1, LinkedDepartmentId = 40, LinkEnabled = true });
				Agreements.Setup(a => a.GetForDepartmentAsync(1, 9)).ReturnsAsync(() => Agreement);
				Agreements.Setup(a => a.UpdateAsync(It.IsAny<MutualAidAgreement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((MutualAidAgreement a, CancellationToken _, bool __) => a);
				Agreements.Setup(a => a.GetCountersignRequestedForLinkAsync(3)).ReturnsAsync(() => Agreement.CountersignStatus == 1 ? new List<MutualAidAgreement> { Agreement } : new List<MutualAidAgreement>());
				Requests.Setup(r => r.CreateMultiSignerAsync(It.IsAny<MultiSignerRequestInput>(), It.IsAny<CancellationToken>()))
					.Callback<MultiSignerRequestInput, CancellationToken>((i, _) => Requested = i)
					.ReturnsAsync(new SignatureRequestResult { Request = new SignatureRequestRecord { SignatureRequestId = 77, DepartmentId = 40 } });
				var links = LinksOver(Links);
				Service = new MutualAidCountersignService(Agreements.Object, links.Object, Departments().Object, new Lazy<ISignatureRequestService>(() => Requests.Object),
					Mock.Of<IEventAggregator>());
				Source = new MutualAidCountersignatureSource(Agreements.Object, links.Object, new Lazy<IMutualAidCountersignService>(() => Service));
			}
		}

		[Test]
		public async Task An_approved_agreement_is_routed_over_its_link_to_the_partners_admins_with_the_same_content_hash()
		{
			var world = new CountersignWorld();
			var agreement = await world.Service.RequestAsync(1, 9, "approver");
			agreement.CountersignStatus.Should().Be((int)MutualAidCountersignStatuses.Requested);
			agreement.CountersignRequestId.Should().Be(77);
			world.Requested.DepartmentId.Should().Be(40, "the request lives in the partner department");
			world.Requested.SubjectKind.Should().Be(SignatureSubjectKinds.MutualAidAgreementCountersignature);
			world.Requested.SubjectId.Should().Be("1:9");
			world.Requested.Steps.Single().Eligibility.DepartmentAdmins.Should().BeTrue();
			world.Requested.Steps.Single().Level.Should().Be(SignatureLevels.StepUp);

			var described = await world.Source.DescribeAsync(40, "1:9", "partner-admin");
			described.RevisionSha256.Should().Be(MutualAidAgreementApprovalSource.Describe(world.Agreement).ContentSha256, "both signatures bind to one document");
			(await world.Source.DescribeAsync(55, "1:9", null)).Should().BeNull("only the partner named on the agreement");
			(await world.Source.DescribeAsync(1, "1:9", null)).Should().BeNull("never the originating department itself");

			await world.Source.OnSignedAsync(new SignatureRequestRecord { SignatureRequestId = 77, DepartmentId = 40, SubjectId = "1:9" },
				new SignatureRequestSigner { UserId = "partner-admin", SignedOn = DateTime.UtcNow }, "partner-admin");
			world.Agreement.CountersignStatus.Should().Be((int)MutualAidCountersignStatuses.Countersigned);
			world.Agreement.CountersignedByUserId.Should().Be("partner-admin");
		}

		[Test]
		public async Task No_request_without_the_requirement_or_a_routable_link_and_a_revoked_link_voids_the_open_request()
		{
			var world = new CountersignWorld();
			world.Agreement.RequiresCountersignature = false;
			(await world.Service.RequestAsync(1, 9, "approver")).CountersignStatus.Should().Be(0);
			world.Agreement.RequiresCountersignature = true;
			world.Links[0].LinkedDepartmentId = 55;
			(await world.Service.RequestAsync(1, 9, "approver")).CountersignStatus.Should().Be(0, "the link's other party is not the partner named on the agreement");
			world.Requested.Should().BeNull();

			world.Links[0].LinkedDepartmentId = 40;
			await world.Service.RequestAsync(1, 9, "approver");
			world.Links[0].LinkEnabled = false;
			(await world.Source.DescribeAsync(40, "1:9", null)).Should().BeNull("a disabled link stops the signing page at once");
			(await world.Service.LapseForLinkAsync(3)).Should().Be(1);
			world.Agreement.CountersignStatus.Should().Be((int)MutualAidCountersignStatuses.Lapsed);
			world.Requests.Verify(r => r.VoidAsync(40, 77, null, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Mutual-aid countersignature
	}
}
