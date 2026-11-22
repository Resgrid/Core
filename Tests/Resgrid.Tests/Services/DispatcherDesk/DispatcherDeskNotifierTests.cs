using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// Desk pushes (M0324, PR #589 round 3): a message preview reaches only a holder who may still use Dispatch, and a supervisor
	/// break alert counts as delivered only when a supervisor's push was accepted (a supervisor whose own break holds alerts back is
	/// not reached).
	/// </summary>
	[TestFixture]
	public class DispatcherDeskNotifierTests
	{
		private const int Dept = 7;
		private static readonly DateTime Now = new DateTime(2026, 11, 22, 16, 0, 0, DateTimeKind.Utc);

		private InMemoryDispatcherDeskStore _store;
		private Mock<IDispatcherDeskPushProvider> _push;
		private Mock<IDispatchAccessService> _access;
		private HashSet<string> _refusing;
		private DispatcherDeskNotifier _notifier;

		[SetUp]
		public void SetUp()
		{
			_store = new InMemoryDispatcherDeskStore();
			_refusing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			_push = new Mock<IDispatcherDeskPushProvider>();
			_push.Setup(x => x.SendAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PushSoundTypes>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string user, string t, string b, string code, PushSoundTypes s, CancellationToken c) => !_refusing.Contains(user));

			_access = new Mock<IDispatchAccessService>();
			_access.Setup(x => x.CanUseDispatchAsync(Dept, It.IsAny<string>())).ReturnsAsync(true);

			var config = new Mock<IDispatcherDeskConfigService>();
			config.Setup(x => x.GetConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new DispatcherDeskConfig { Enabled = true, SuppressAlertsOnBreak = true });

			_notifier = new DispatcherDeskNotifier(_push.Object, new Mock<IEventAggregator>().Object, config.Object, _store.PresencesRepository, _access.Object);
		}

		private static DispatcherQueueItem Text() => new DispatcherQueueItem
		{
			DispatcherQueueItemId = "i-1", DepartmentId = Dept, Kind = (int)DispatcherQueueItemKinds.Text, State = (int)DispatcherQueueItemStates.Accepted,
			AssignedUserId = "ann", SourceAddress = "+15555550123", CreatedOn = Now, LastActivityOn = Now
		};

		private static DispatcherBreak Overdue() => new DispatcherBreak
		{
			DispatcherBreakId = "b-1", DepartmentId = Dept, UserId = "ann", BreakType = (int)DispatcherBreakTypes.Break, State = (int)DispatcherBreakStates.Active,
			DurationMinutes = 15, StartedOn = Now.AddMinutes(-25), ExpectedReturnOn = Now.AddMinutes(-10)
		};

		private int PushesTo(string user) =>
			_push.Invocations.Count(x => x.Method.Name == nameof(IDispatcherDeskPushProvider.SendAsync) && (string)x.Arguments[1] == user);

		[Test]
		public async Task A_message_preview_reaches_a_holder_who_may_use_dispatch()
		{
			await _notifier.NotifyMessageAsync(Text(), "ann", "Smoke in the hallway");

			PushesTo("ann").Should().Be(1);
		}

		[Test]
		public async Task A_message_preview_is_not_pushed_to_a_holder_who_lost_dispatch_access()
		{
			_access.Setup(x => x.CanUseDispatchAsync(Dept, "ann")).ReturnsAsync(false);

			await _notifier.NotifyMessageAsync(Text(), "ann", "Smoke in the hallway");

			PushesTo("ann").Should().Be(0, "the preview carries the sender's words and she may no longer dispatch");
		}

		[Test]
		public async Task A_failed_dispatch_access_check_sends_no_preview()
		{
			_access.Setup(x => x.CanUseDispatchAsync(Dept, "ann")).ThrowsAsync(new InvalidOperationException("permissions read failed"));

			Func<Task> notify = () => _notifier.NotifyMessageAsync(Text(), "ann", "Smoke in the hallway");

			await notify.Should().NotThrowAsync("notifications never fail the desk action that raised them");
			PushesTo("ann").Should().Be(0);
		}

		[Test]
		public async Task A_supervisor_alert_is_not_delivered_when_the_only_supervisor_reachable_refused_it()
		{
			// Sam is on a break that holds desk alerts back; the push to Lee is refused.
			_store.AddPresence(Dept, "sam", DispatcherPresenceStates.OnBreak, Now);
			_refusing.Add("lee");

			(await _notifier.NotifyBreakOverAsync(Overdue(), new[] { "sam", "lee" }, autoReturned: false)).Should().BeFalse(
				"nobody was told: the alert must be sent again");
			PushesTo("sam").Should().Be(0);
		}

		[Test]
		public async Task A_supervisor_alert_is_delivered_once_one_supervisor_push_is_accepted()
		{
			_store.AddPresence(Dept, "sam", DispatcherPresenceStates.OnBreak, Now);
			_refusing.Add("lee");

			(await _notifier.NotifyBreakOverAsync(Overdue(), new[] { "sam", "lee", "kim" }, autoReturned: false)).Should().BeTrue();
		}

		[Test]
		public async Task A_supervisor_alert_with_every_supervisor_held_back_counts_as_handled()
		{
			_store.AddPresence(Dept, "sam", DispatcherPresenceStates.OnBreak, Now);
			_store.AddPresence(Dept, "lee", DispatcherPresenceStates.OnBreak, Now);

			(await _notifier.NotifyBreakOverAsync(Overdue(), new[] { "sam", "lee" }, autoReturned: false)).Should().BeTrue(
				"there is nobody who can be told, like no supervisor at all");
			_push.Invocations.Should().BeEmpty();
		}
	}
}
