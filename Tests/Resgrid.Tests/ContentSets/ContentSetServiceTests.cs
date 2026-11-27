using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.ContentSets;

namespace Resgrid.Tests.ContentSets
{
	/// <summary>
	/// Round 10 A3: the publisher/subscriber machinery of protocol and course sets over Department Links, on in-memory repositories and an
	/// in-memory item source: access only over a link that shares the kind toward the subscriber, the inbox with deadlines, acceptance at the
	/// update time (never before the effective date), lock/override across updates, revocation, proposals upstream and unsubscribing.
	/// </summary>
	[TestFixture]
	public class ContentSetServiceTests
	{
		private const int Publisher = 40;
		private const int Subscriber = 1;
		private const int Other = 77;
		private static string Admin(int department) => "admin-" + department;

		private sealed class World
		{
			public readonly FakeSets Sets = new FakeSets();
			public readonly FakeItems Items = new FakeItems();
			public readonly FakeSubscriptions Subscriptions = new FakeSubscriptions();
			public readonly FakeCopies Copies = new FakeCopies();
			public readonly FakeUpdates Updates = new FakeUpdates();
			public readonly FakeProposals Proposals = new FakeProposals();
			public readonly FakeItemSource Protocols = new FakeItemSource();
			public readonly FakeItemSource Courses = new FakeItemSource(ContentSetItemKinds.LmsCourse, true);
			public readonly FakePathSource Paths = new FakePathSource();
			public readonly List<DepartmentLink> Links = new List<DepartmentLink>();
			public readonly Mock<IProtocolCommentsRepository> Comments = new Mock<IProtocolCommentsRepository>();
			public readonly Mock<IContentSetNotifier> Notifier = new Mock<IContentSetNotifier>();
			public readonly List<object> Events = new List<object>();
			public bool SubscriberHasLms;
			public LmsSponsorshipGrant Grant;
			public readonly ContentSetService Service;

			public World()
			{
				Updates.Subscriptions = Subscriptions;
				var links = new Mock<IDepartmentLinksService>();
				links.Setup(l => l.GetLinkByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Links.FirstOrDefault(l => l.DepartmentLinkId == id));
				links.Setup(l => l.GetAllLinksForDepartmentAsync(It.IsAny<int>()))
					.ReturnsAsync((int d) => Links.Where(l => l.DepartmentId == d || l.LinkedDepartmentId == d).ToList());

				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync((int id, bool _) => new Department { DepartmentId = id, Name = "Dept " + id, ManagingUserId = Admin(id), TimeZone = "UTC" });
				departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync((string u, int id, bool _) => new DepartmentMember { DepartmentId = id, UserId = u });

				var roles = new Mock<IPersonnelRolesService>();
				roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());
				var lms = new Mock<ILmsAccessService>();
				lms.Setup(l => l.CanAuthorAndManageAsync(It.IsAny<int>())).ReturnsAsync(() => SubscriberHasLms);
				var sponsorships = new Mock<ILmsSponsorshipService>();
				sponsorships.Setup(s => s.GetGrantAsync(It.IsAny<int>())).ReturnsAsync(() => Grant);
				sponsorships.Setup(s => s.GetForDepartmentAsync(It.IsAny<int>())).ReturnsAsync(new List<LmsSponsorshipView>());
				Comments.Setup(c => c.InsertAsync(It.IsAny<ProtocolComment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ProtocolComment c, CancellationToken _, bool __) => { c.ProtocolCommentId = 555; return c; });
				var events = new Mock<IEventAggregator>();
				events.Setup(e => e.SendMessage(It.IsAny<ContentSetWorkflowEvent>())).Callback<ContentSetWorkflowEvent>(e => Events.Add(e));

				Service = new ContentSetService(Sets, Items, Subscriptions, Copies, Updates, Proposals, links.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(),
					Mock.Of<IPermissionsService>(), Mock.Of<IDepartmentGroupsService>(), roles.Object, Mock.Of<IUserProfileService>(), new IContentSetItemSource[] { Protocols, Courses, Paths },
					lms.Object, sponsorships.Object, Comments.Object, Mock.Of<IProtocolLibraryService>(), Notifier.Object, events.Object);
			}

			public DepartmentLink Link(bool publisherSharesProtocols = true, bool publisherSharesTraining = false, bool enabled = true)
			{
				var link = new DepartmentLink
				{
					DepartmentLinkId = Links.Count + 1, DepartmentId = Publisher, LinkedDepartmentId = Subscriber, LinkEnabled = enabled,
					DepartmentShareProtocols = publisherSharesProtocols, DepartmentShareTraining = publisherSharesTraining
				};
				Links.Add(link);
				return link;
			}

			public Task<ContentSetActor> ActorAsync(int department) => Service.GetActorAsync(department, Admin(department));

			public FakeItemSource.Item Protocol(int id, params (string Key, string Html)[] blocks) =>
				Add(Protocols, id, blocks);

			public FakeItemSource.Item Add(FakeItemSource source, int id, params (string Key, string Html)[] blocks)
			{
				var document = new ProtocolContentDocument();
				foreach (var (key, html) in blocks)
					document.Blocks.Add(new ProtocolContentBlock { Type = "html", Key = key, Html = html });
				var item = new FakeItemSource.Item { DepartmentId = Publisher, ItemId = id, Title = "Item " + id, VersionId = id * 10, Document = document };
				source.Items.Add(item);
				return item;
			}

			public async Task<(ContentSet Set, ContentSetItem Item)> SetWithAsync(int itemId, ContentSetLockModes mode, ContentSetKinds kind = ContentSetKinds.Protocols,
				ContentSetItemKinds itemKind = ContentSetItemKinds.Protocol, params string[] keys)
			{
				var publisher = await ActorAsync(Publisher);
				var set = (await Service.SaveSetAsync(publisher, 0, kind, "County set", null, 14)).Set;
				var added = await Service.AddItemAsync(publisher, set.ContentSetId, itemKind, itemId, mode, keys);
				added.Succeeded.Should().BeTrue(added.Error + " " + added.Reason);
				return (set, added.Item);
			}

			public void Bump(FakeItemSource.Item item, DateTime? effectiveOn, params (string Key, string Html)[] blocks)
			{
				item.VersionId++;
				item.Label = "1." + (item.VersionId % 10);
				item.EffectiveOn = effectiveOn;
				var document = new ProtocolContentDocument();
				foreach (var (key, html) in blocks)
					document.Blocks.Add(new ProtocolContentBlock { Type = "html", Key = key, Html = html });
				item.Document = document;
			}
		}

		[Test]
		public async Task A_learning_path_joins_a_set_only_after_its_courses_and_is_copied_after_them()
		{
			var world = new World { SubscriberHasLms = true };
			world.Link(publisherSharesProtocols: false, publisherSharesTraining: true);
			world.Add(world.Courses, 11);
			world.Add(world.Paths.Inner, 7);
			world.Paths.Dependencies[7] = new List<KeyValuePair<ContentSetItemKinds, int>> { new KeyValuePair<ContentSetItemKinds, int>(ContentSetItemKinds.LmsCourse, 11) };
			var publisher = await world.ActorAsync(Publisher);
			var set = (await world.Service.SaveSetAsync(publisher, 0, ContentSetKinds.Training, "Academy", null, 14)).Set;

			var early = await world.Service.AddItemAsync(publisher, set.ContentSetId, ContentSetItemKinds.LmsPath, 7, ContentSetLockModes.Locked, null);
			early.Error.Should().Be(ContentSetErrors.ItemNotShareable);
			early.Reason.Should().Be("path_steps");

			(await world.Service.AddItemAsync(publisher, set.ContentSetId, ContentSetItemKinds.LmsCourse, 11, ContentSetLockModes.Locked, null)).Succeeded.Should().BeTrue();
			(await world.Service.AddItemAsync(publisher, set.ContentSetId, ContentSetItemKinds.LmsPath, 7, ContentSetLockModes.Locked, null)).Succeeded.Should().BeTrue();

			var subscribed = await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);
			subscribed.Succeeded.Should().BeTrue();
			world.Copies.Rows.Select(c => (ContentSetItemKinds)c.ItemKind).Should().Equal(ContentSetItemKinds.LmsCourse, ContentSetItemKinds.LmsPath);
		}

		[Test]
		public async Task A_set_is_offered_and_subscribed_only_over_a_link_that_shares_the_kind_toward_the_subscriber()
		{
			var world = new World();
			world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			var subscriber = await world.ActorAsync(Subscriber);

			// No link at all, a link sharing only calls, a link where the subscriber (not the publisher) shares protocols, a disabled link.
			(await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false)).Error.Should().Be(ContentSetErrors.NotShared);
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 9, DepartmentId = Publisher, LinkedDepartmentId = Subscriber, LinkEnabled = true, DepartmentShareCalls = true });
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 10, DepartmentId = Publisher, LinkedDepartmentId = Subscriber, LinkEnabled = true, LinkedDepartmentShareProtocols = true });
			world.Links.Add(new DepartmentLink { DepartmentLinkId = 11, DepartmentId = Publisher, LinkedDepartmentId = Subscriber, LinkEnabled = false, DepartmentShareProtocols = true });
			(await world.Service.GetAvailableSetsAsync(subscriber, ContentSetKinds.Protocols)).Should().BeEmpty();
			(await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false)).Error.Should().Be(ContentSetErrors.NotShared);

			// A department that is not a party sees nothing either.
			(await world.Service.GetAvailableSetsAsync(await world.ActorAsync(Other), ContentSetKinds.Protocols)).Should().BeEmpty();

			world.Link();
			(await world.Service.GetAvailableSetsAsync(subscriber, ContentSetKinds.Protocols)).Should().ContainSingle(a => a.Set.ContentSetId == set.ContentSetId);
			var subscribed = await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false);
			subscribed.Succeeded.Should().BeTrue();
			subscribed.Subscription.DepartmentLinkId.Should().Be(world.Links.Last().DepartmentLinkId);
			world.Copies.Rows.Should().ContainSingle(c => c.DepartmentId == Subscriber && c.SourceVersionId == 10 && c.IsManaged);
			(await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false)).Error.Should().Be(ContentSetErrors.AlreadySubscribed);
			(await world.Service.SubscribeAsync(await world.ActorAsync(Publisher), set.ContentSetId, false)).Error.Should().Be(ContentSetErrors.OwnSet);
		}

		[Test]
		public async Task Lms_items_reach_a_subscriber_only_with_its_own_add_on_or_the_publishers_sponsorship()
		{
			var world = new World();
			world.Add(world.Courses, 5, ("b1", "<p>Course</p>"));
			var (set, _) = await world.SetWithAsync(5, ContentSetLockModes.Locked, ContentSetKinds.Training, ContentSetItemKinds.LmsCourse);
			world.Link(publisherSharesProtocols: false, publisherSharesTraining: true);
			var subscriber = await world.ActorAsync(Subscriber);

			var first = await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false);
			first.Succeeded.Should().BeTrue();
			first.Skipped.Should().ContainSingle(s => s.Contains("(lms)"));
			world.Copies.Rows.Should().BeEmpty();

			// Sponsored by the publisher (a free-plan subscriber): the course is copied on sync.
			world.Grant = new LmsSponsorshipGrant { Sponsorship = new LmsSponsorship { SponsorDepartmentId = Publisher, DepartmentId = Subscriber, Status = 0 }, Capacity = 25 };
			var synced = await world.Service.SyncAsync(subscriber, first.Subscription.ContentSetSubscriptionId);
			synced.Skipped.Should().BeEmpty();
			world.Copies.Rows.Should().ContainSingle(c => c.ItemKind == (int)ContentSetItemKinds.LmsCourse);

			// Another department's sponsorship does not count.
			world.Grant = new LmsSponsorshipGrant { Sponsorship = new LmsSponsorship { SponsorDepartmentId = Other, DepartmentId = Subscriber, Status = 0 } };
			(await world.Service.GetAvailableSetAsync(subscriber, set.ContentSetId)).CanRunLms.Should().BeFalse();
			world.SubscriberHasLms = true;
			(await world.Service.GetAvailableSetAsync(subscriber, set.ContentSetId)).CanRunLms.Should().BeTrue();
		}

		[Test]
		public async Task A_new_version_lands_in_the_inbox_with_the_locked_items_deadline_and_supersedes_older_rows()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			world.Link();
			await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);

			// The same version again queues nothing.
			(await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1)).Should().Be(0);

			var effective = DateTime.UtcNow.AddDays(3);
			world.Bump(protocol, effective, ("b1", "<p>One, revised</p>"));
			(await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1)).Should().Be(1);
			var update = world.Updates.Rows.Single();
			update.Status.Should().Be((int)ContentSetUpdateStatuses.Pending);
			update.SourceEffectiveOn.Should().BeCloseTo(effective, TimeSpan.FromSeconds(1));
			update.DeadlineOn.Should().BeCloseTo(effective.AddDays(14), TimeSpan.FromSeconds(1));
			world.Notifier.Verify(n => n.UpdatesArrivedAsync(Subscriber, "County set", "Dept 40", It.IsAny<IReadOnlyList<ContentSetUpdate>>(), It.IsAny<CancellationToken>()), Times.Once);
			world.Events.OfType<ContentSetWorkflowEvent>().Should().Contain(e => e.Trigger == WorkflowTriggerEventType.ContentSetUpdateArrived && e.DepartmentId == Subscriber);

			world.Bump(protocol, effective, ("b1", "<p>One, revised twice</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			world.Updates.Rows.Should().HaveCount(2);
			world.Updates.Rows.First().Status.Should().Be((int)ContentSetUpdateStatuses.Superseded);
			world.Updates.Rows.Last().IsOpen.Should().BeTrue();
		}

		[Test]
		public async Task An_accepted_update_goes_into_force_at_the_update_time_never_before_the_effective_date()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			world.Link();
			await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);
			var effective = DateTime.UtcNow.Date.AddDays(2).AddHours(15);
			world.Bump(protocol, effective, ("b1", "<p>One, revised</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			var update = world.Updates.Rows.Single();

			// An apply time asked for before the effective date is moved to it; without one, the next 07:00 (department UTC) after it.
			var subscriber = await world.ActorAsync(Subscriber);
			var accepted = await world.Service.AcceptUpdateAsync(subscriber, update.ContentSetUpdateId, null);
			accepted.Succeeded.Should().BeTrue();
			accepted.Update.Status.Should().Be((int)ContentSetUpdateStatuses.Scheduled);
			accepted.Update.ApplyOn.Should().Be(effective.Date.AddDays(1).AddHours(7));

			// Nothing goes into force before its time; at it, the copy moves to the new version.
			var early = await world.Service.RunDueAsync(effective);
			early.Applied.Should().Be(0);
			var run = await world.Service.RunDueAsync(effective.Date.AddDays(1).AddHours(7).AddMinutes(1));
			run.Applied.Should().Be(1);
			world.Updates.Rows.Single().Status.Should().Be((int)ContentSetUpdateStatuses.Applied);
			world.Copies.Rows.Single().SourceVersionId.Should().Be(protocol.VersionId);
			world.Events.OfType<ContentSetWorkflowEvent>().Should().Contain(e => e.Trigger == WorkflowTriggerEventType.ContentSetUpdateApplied);
		}

		[Test]
		public async Task Locked_content_cannot_be_declined_and_is_never_deferred_past_its_deadline_which_schedules_it()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			world.Link();
			await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);
			var effective = DateTime.UtcNow.AddHours(1);
			world.Bump(protocol, effective, ("b1", "<p>New</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			var update = world.Updates.Rows.Single();
			var subscriber = await world.ActorAsync(Subscriber);

			(await world.Service.DeclineUpdateAsync(subscriber, update.ContentSetUpdateId)).Error.Should().Be(ContentSetErrors.LockedCannotDecline);
			(await world.Service.DeferUpdateAsync(subscriber, update.ContentSetUpdateId, update.DeadlineOn.Value.AddDays(1))).Error.Should().Be(ContentSetErrors.DateOutOfRange);
			(await world.Service.DeferUpdateAsync(subscriber, update.ContentSetUpdateId, update.DeadlineOn.Value.AddDays(-1))).Succeeded.Should().BeTrue();

			// The deadline passes: worker 100 accepts it for the subscriber.
			var run = await world.Service.RunDueAsync(update.DeadlineOn.Value.AddMinutes(1));
			run.Accepted.Should().Be(1);
			var scheduled = world.Updates.Rows.Single();
			scheduled.Status.Should().BeOneOf((int)ContentSetUpdateStatuses.Scheduled, (int)ContentSetUpdateStatuses.Applied);
			scheduled.DecidedAutomatically.Should().BeTrue();
		}

		[Test]
		public async Task An_open_item_can_be_declined_and_auto_accept_schedules_without_a_decision()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Open);
			world.Link();
			var subscription = (await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false)).Subscription;
			world.Bump(protocol, null, ("b1", "<p>New</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			var update = world.Updates.Rows.Single();
			update.DeadlineOn.Should().BeNull("open items have no deadline");
			(await world.Service.DeclineUpdateAsync(await world.ActorAsync(Subscriber), update.ContentSetUpdateId)).Succeeded.Should().BeTrue();

			await world.Service.SetAutoAcceptAsync(await world.ActorAsync(Subscriber), subscription.ContentSetSubscriptionId, true);
			world.Bump(protocol, null, ("b1", "<p>Newer</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			var result = await world.Service.RunDueAsync(DateTime.UtcNow);
			result.Accepted.Should().Be(1);
			world.Updates.Rows.Last().Status.Should().Be((int)ContentSetUpdateStatuses.Scheduled);
		}

		[Test]
		public async Task Customized_blocks_survive_upstream_updates_while_locked_blocks_follow_the_publisher()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>Clinical</p>"), ("b2", "<p>Call 555-0100</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.CustomizableBlocks, ContentSetKinds.Protocols, ContentSetItemKinds.Protocol, "b2");
			world.Link();
			var subscriber = await world.ActorAsync(Subscriber);
			await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false);
			var copy = world.Copies.Rows.Single();

			(await world.Service.SaveCustomizationAsync(subscriber, copy.ContentSetCopyId, new Dictionary<string, string> { ["b1"] = "<p>Mine</p>" }, null))
				.Error.Should().Be(ContentSetErrors.BlockLocked);
			var saved = await world.Service.SaveCustomizationAsync(subscriber, copy.ContentSetCopyId, new Dictionary<string, string> { ["b2"] = "<p>Call 555-0199</p>" },
				new List<string> { "<p>Our local note</p>" });
			saved.Succeeded.Should().BeTrue();
			world.Protocols.Local[copy.LocalItemId].Render().Should().Contain("555-0199").And.Contain("Our local note").And.Contain("Clinical");

			world.Bump(protocol, null, ("b1", "<p>Clinical, revised</p>"), ("b2", "<p>Call 555-0100 (new hours)</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			await world.Service.AcceptUpdateAsync(subscriber, world.Updates.Rows.Single().ContentSetUpdateId, DateTime.UtcNow);

			var html = world.Protocols.Local[copy.LocalItemId].Render();
			html.Should().Contain("Clinical, revised").And.Contain("555-0199").And.Contain("Our local note");
			html.Should().NotContain("new hours");
		}

		[Test]
		public async Task Revoking_the_link_or_withdrawing_the_share_suspends_the_subscription_and_stops_every_update()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			var link = world.Link();
			var subscriber = await world.ActorAsync(Subscriber);
			await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false);
			world.Bump(protocol, null, ("b1", "<p>Two</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			var update = world.Updates.Rows.Single();

			// The publisher stops sharing protocols over the link.
			link.DepartmentShareProtocols = false;
			(await world.Service.AcceptUpdateAsync(subscriber, update.ContentSetUpdateId, null)).Error.Should().Be(ContentSetErrors.NotShared);
			(await world.Service.ProposeAsync(subscriber, world.Copies.Rows.Single().ContentSetCopyId, null, "Please change")).Error.Should().Be(ContentSetErrors.NotShared);
			(await world.Service.GetAvailableSetsAsync(subscriber, ContentSetKinds.Protocols)).Should().BeEmpty();
			(await world.Service.SuspendLapsedAsync(link.DepartmentLinkId, DateTime.UtcNow)).Should().Be(1);
			world.Subscriptions.Rows.Single().Status.Should().Be((int)ContentSetSubscriptionStatuses.Suspended);
			world.Updates.Rows.Single().IsOpen.Should().BeFalse();

			world.Bump(protocol, null, ("b1", "<p>Three</p>"));
			(await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1)).Should().Be(0);

			// Sharing again resumes it (the next publish arrives).
			link.DepartmentShareProtocols = true;
			await world.Service.SuspendLapsedAsync(null, DateTime.UtcNow);
			world.Subscriptions.Rows.Single().Status.Should().Be((int)ContentSetSubscriptionStatuses.Active);

			// A disabled link suspends it as well.
			link.LinkEnabled = false;
			(await world.Service.SuspendLapsedAsync(null, DateTime.UtcNow)).Should().Be(1);
		}

		[Test]
		public async Task A_scheduled_update_whose_link_was_revoked_is_not_applied()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			var link = world.Link();
			var subscriber = await world.ActorAsync(Subscriber);
			await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false);
			world.Bump(protocol, DateTime.UtcNow.AddDays(1), ("b1", "<p>Two</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			await world.Service.AcceptUpdateAsync(subscriber, world.Updates.Rows.Single().ContentSetUpdateId, null);

			world.Links.Remove(link);
			var run = await world.Service.RunDueAsync(DateTime.UtcNow.AddDays(3));
			run.Applied.Should().Be(0);
			world.Copies.Rows.Single().SourceVersionId.Should().Be(10, "the copy never moves after the link is gone");
		}

		[Test]
		public async Task A_member_proposes_upstream_as_a_suggestion_thread_and_a_decline_needs_an_answer()
		{
			var world = new World();
			world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			world.Link();
			await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);
			var copy = world.Copies.Rows.Single();

			// Any current member may propose (no permission needed).
			var member = await world.Service.GetActorAsync(Subscriber, "member-7");
			member.CanManage.Should().BeFalse();
			var proposed = await world.Service.ProposeAsync(member, copy.ContentSetCopyId, "p-dosing", "Add the pediatric dose table.");
			proposed.Succeeded.Should().BeTrue();
			proposed.Proposal.PublisherCommentId.Should().Be(555);
			world.Comments.Verify(c => c.InsertAsync(It.Is<ProtocolComment>(p => p.DepartmentId == Publisher && p.DispatchProtocolId == 1 &&
				p.Kind == (int)ProtocolCommentKinds.Suggestion && p.Body.Contains("Dept 1") && p.Anchor == "p-dosing"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			world.Notifier.Verify(n => n.ProposalReceivedAsync(It.IsAny<ContentSetProposal>(), "County set", "Item 1", It.IsAny<CancellationToken>()), Times.Once);

			var publisher = await world.ActorAsync(Publisher);
			(await world.Service.GetProposalsToPublisherAsync(publisher, true)).Should().ContainSingle();
			(await world.Service.RespondToProposalAsync(publisher, proposed.Proposal.ContentSetProposalId, false, null)).Error.Should().Be(ContentSetErrors.InvalidInput);
			(await world.Service.RespondToProposalAsync(member, proposed.Proposal.ContentSetProposalId, true, "ok")).Error.Should().Be(ContentSetErrors.NotAuthorized);
			(await world.Service.RespondToProposalAsync(publisher, proposed.Proposal.ContentSetProposalId, true, "In 4.0")).Succeeded.Should().BeTrue();
			world.Proposals.Rows.Single().Status.Should().Be((int)ContentSetProposalStatuses.Accepted);
			(await world.Service.GetProposalsFromDepartmentAsync(member)).Single().Proposal.ResponseBody.Should().Be("In 4.0");
		}

		[Test]
		public async Task Only_managers_subscribe_and_decide_and_unsubscribing_detaches_the_copies()
		{
			var world = new World();
			var protocol = world.Protocol(1, ("b1", "<p>One</p>"));
			var (set, _) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			world.Link();
			var member = await world.Service.GetActorAsync(Subscriber, "member-7");
			(await world.Service.SubscribeAsync(member, set.ContentSetId, false)).Error.Should().Be(ContentSetErrors.NotAuthorized);

			var subscriber = await world.ActorAsync(Subscriber);
			var subscription = (await world.Service.SubscribeAsync(subscriber, set.ContentSetId, false)).Subscription;
			world.Bump(protocol, null, ("b1", "<p>Two</p>"));
			await world.Service.OnSourcePublishedAsync(Publisher, ContentSetItemKinds.Protocol, 1);
			(await world.Service.AcceptUpdateAsync(member, world.Updates.Rows.Single().ContentSetUpdateId, null)).Error.Should().Be(ContentSetErrors.NotAuthorized);

			(await world.Service.GetCopyInfoAsync(subscriber, ContentSetItemKinds.Protocol, world.Copies.Rows.Single().LocalItemId)).Should().NotBeNull();
			(await world.Service.UnsubscribeAsync(subscriber, subscription.ContentSetSubscriptionId)).Succeeded.Should().BeTrue();
			world.Copies.Rows.Single().IsManaged.Should().BeFalse("the copy is the department's own content now");
			world.Updates.Rows.Single().IsOpen.Should().BeFalse();
			(await world.Service.GetCopyInfoAsync(subscriber, ContentSetItemKinds.Protocol, world.Copies.Rows.Single().LocalItemId)).Should().BeNull();
		}

		[Test]
		public async Task Removing_an_item_or_archiving_the_set_releases_subscribers_copies()
		{
			var world = new World();
			world.Protocol(1, ("b1", "<p>One</p>"));
			world.Protocol(2, ("b1", "<p>Two</p>"));
			var (set, item) = await world.SetWithAsync(1, ContentSetLockModes.Locked);
			var publisher = await world.ActorAsync(Publisher);
			world.Link();
			await world.Service.SubscribeAsync(await world.ActorAsync(Subscriber), set.ContentSetId, false);

			// A new item reaches the subscriber's inbox as a new item.
			(await world.Service.AddItemAsync(publisher, set.ContentSetId, ContentSetItemKinds.Protocol, 2, ContentSetLockModes.Locked, null)).Succeeded.Should().BeTrue();
			world.Updates.Rows.Should().ContainSingle(u => u.UpdateKind == (int)ContentSetUpdateKinds.NewItem);

			(await world.Service.RemoveItemAsync(publisher, item.ContentSetItemId)).Succeeded.Should().BeTrue();
			world.Copies.Rows.Single(c => c.SourceItemId == 1).IsManaged.Should().BeFalse();

			(await world.Service.ArchiveSetAsync(publisher, set.ContentSetId)).Succeeded.Should().BeTrue();
			world.Subscriptions.Rows.Single().Status.Should().Be((int)ContentSetSubscriptionStatuses.Ended);
			world.Updates.Rows.Should().OnlyContain(u => !u.IsOpen);

			// Another department never manages the publisher's set.
			(await world.Service.GetSetAsync(await world.ActorAsync(Subscriber), set.ContentSetId)).Should().BeNull();
		}
	}
}
