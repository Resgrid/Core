using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A v4 staffing whose save failed after its insert (the protected-data step was refused) is finished by the app's replay:
	/// the row is protected, then the staffing event the save never sent goes out, once, and only while that staffing is still
	/// the member's latest.
	/// </summary>
	[TestFixture]
	public class UserStateServiceResumeTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "responder-1";

		private Mock<IUserStatesRepository> _states;
		private Mock<IProtectedWriteService> _protectedWrite;
		private Mock<IEventAggregator> _events;
		private UserState _saved;

		[SetUp]
		public void SetUp()
		{
			_saved = new UserState { UserStateId = 300, UserId = UserId, DepartmentId = DepartmentId, State = (int)UserStateTypes.Committed, Note = "On my way" };
			_states = new Mock<IUserStatesRepository>();
			_states.Setup(x => x.GetByIdAsync(300)).ReturnsAsync(_saved);
			_states.Setup(x => x.GetLastUserStateByUserIdAsync(UserId)).ReturnsAsync(_saved);
			_protectedWrite = new Mock<IProtectedWriteService>();
			_protectedWrite.Setup(x => x.PrepareUserStateWriteAsync(It.IsAny<int>(), It.IsAny<UserState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));
			_events = new Mock<IEventAggregator>();
		}

		private UserStateService Service(ICacheProvider cache = null) =>
			new UserStateService(_states.Object, Mock.Of<IDepartmentsService>(), _events.Object, cache ?? Mock.Of<ICacheProvider>(),
				new Lazy<IProtectedWriteService>(() => _protectedWrite.Object));

		/// <summary>A cache and event aggregator that record, in order, when the latest-staffings cache is cleared and the event sent.</summary>
		private (ICacheProvider Cache, List<string> Steps) RecordingCacheAndEvents()
		{
			var steps = new List<string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.Remove(It.IsAny<string>())).Callback(() => steps.Add("invalidate"));
			_events.Setup(x => x.SendMessage(It.IsAny<UserStaffingEvent>())).Callback(() => steps.Add("announce"));
			return (cache.Object, steps);
		}

		[Test]
		public async Task A_new_staffing_clears_the_cache_before_it_is_announced_so_the_event_is_the_last_step()
		{
			_states.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UserState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UserState s, CancellationToken _, bool __) => { s.UserStateId = 302; return s; });
			var (cache, steps) = RecordingCacheAndEvents();

			await Service(cache).CreateUserStateAsync(new UserState { UserId = UserId, DepartmentId = DepartmentId, State = (int)UserStateTypes.Available, Timestamp = DateTime.UtcNow });

			steps.Should().Equal(new[] { "invalidate", "announce" });
		}

		[Test]
		public async Task A_cache_failure_stops_the_staffing_event_so_the_replay_still_owes_it()
		{
			_states.Setup(x => x.SaveOrUpdateAsync(It.IsAny<UserState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((UserState s, CancellationToken _, bool __) => { s.UserStateId = 302; return s; });
			var cache = new Mock<ICacheProvider>();
			cache.Setup(x => x.Remove(It.IsAny<string>())).Throws(new InvalidOperationException("cache unavailable"));

			Func<Task> save = () => Service(cache.Object).CreateUserStateAsync(new UserState { UserId = UserId, DepartmentId = DepartmentId, State = (int)UserStateTypes.Available, Timestamp = DateTime.UtcNow });

			await save.Should().ThrowAsync<InvalidOperationException>();
			_events.Verify(x => x.SendMessage(It.IsAny<UserStaffingEvent>()), Times.Never);
		}

		[Test]
		public async Task A_resumed_announcement_also_clears_the_cache_before_the_event()
		{
			var (cache, steps) = RecordingCacheAndEvents();

			await Service(cache).AnnounceUserStateAsync(DepartmentId, 300);

			steps.Should().Equal(new[] { "invalidate", "announce" });
		}

		[Test]
		public async Task Protecting_a_saved_staffing_again_persists_the_enveloped_row()
		{
			await Service().ProtectUserStateAsync(DepartmentId, 300);

			_protectedWrite.Verify(x => x.PrepareUserStateWriteAsync(DepartmentId, _saved, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
			_states.Verify(x => x.SaveOrUpdateAsync(_saved, It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task A_still_refused_protection_throws_so_the_receipt_stays_open()
		{
			_protectedWrite.Setup(x => x.PrepareUserStateWriteAsync(It.IsAny<int>(), It.IsAny<UserState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> protect = () => Service().ProtectUserStateAsync(DepartmentId, 300);

			await protect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broker_unavailable*");
		}

		[Test]
		public async Task The_latest_staffing_is_announced_with_its_predecessor()
		{
			var previous = new UserState { UserStateId = 299, UserId = UserId, DepartmentId = DepartmentId };
			_states.Setup(x => x.GetPreviousUserStateByUserIdAsync(UserId, 300)).ReturnsAsync(previous);
			UserStaffingEvent sent = null;
			_events.Setup(x => x.SendMessage(It.IsAny<UserStaffingEvent>())).Callback<UserStaffingEvent>(e => sent = e);

			await Service().AnnounceUserStateAsync(DepartmentId, 300);

			sent.Should().NotBeNull();
			sent.DepartmentId.Should().Be(DepartmentId);
			sent.Staffing.Should().BeSameAs(_saved);
			sent.PreviousStaffing.Should().BeSameAs(previous);
		}

		[Test]
		public async Task A_staffing_replaced_by_a_newer_one_is_not_announced()
		{
			_states.Setup(x => x.GetLastUserStateByUserIdAsync(UserId)).ReturnsAsync(new UserState { UserStateId = 301, UserId = UserId, DepartmentId = DepartmentId });

			await Service().AnnounceUserStateAsync(DepartmentId, 300);

			_events.Verify(x => x.SendMessage(It.IsAny<UserStaffingEvent>()), Times.Never);
		}

		[Test]
		public async Task Another_departments_staffing_is_neither_protected_nor_announced()
		{
			await Service().ProtectUserStateAsync(DepartmentId + 1, 300);
			await Service().AnnounceUserStateAsync(DepartmentId + 1, 300);

			_protectedWrite.Verify(x => x.PrepareUserStateWriteAsync(It.IsAny<int>(), It.IsAny<UserState>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
			_events.Verify(x => x.SendMessage(It.IsAny<UserStaffingEvent>()), Times.Never);
		}
	}
}
