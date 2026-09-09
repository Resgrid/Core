using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public sealed class InventoryM5NotificationTests
	{
		private const int DepartmentId = 77;
		private const string UserId = "inventory-delegate";
		private const string Canary = "SYNTHETIC-INVENTORY-ALERT-PRIVATE-CANARY";
		private readonly DateTimeOffset _now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
		private Mock<IInventoryAlertService> _alerts;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ICommunicationService> _communication;
		private List<DepartmentMember> _members;
		private Queue<InventoryAlertDelivery> _deliveries;
		private List<Notice> _notices;
		private Department _department;
		private UserProfile _profile;
		private InventoryAlertNotifications _service;

		[SetUp]
		public void SetUp()
		{
			_alerts = new(); _departments = new(); _profiles = new(); _settings = new(); _communication = new();
			_members = new() { new DepartmentMember { DepartmentId = DepartmentId, UserId = UserId, IsAdmin = false, IsActive = true } };
			_deliveries = new(); _notices = new();
			_department = new Department { DepartmentId = DepartmentId, Name = Canary };
			_profile = new UserProfile { UserId = UserId, FirstName = Canary, LastName = Canary, Language = "en" };
			_departments.Setup(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, true)).ReturnsAsync(() => _members);
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, true)).ReturnsAsync(() => _department);
			_profiles.Setup(x => x.GetProfileByUserIdAsync(UserId, true)).ReturnsAsync(() => _profile);
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DepartmentId)).ReturnsAsync("+15555550123");
			_alerts.Setup(x => x.ClaimAlertAsync(DepartmentId, UserId)).ReturnsAsync(() => _deliveries.Count == 0 ? null : _deliveries.Dequeue());
			_alerts.Setup(x => x.CanReceiveAlertAsync(DepartmentId, UserId, It.IsAny<string>())).ReturnsAsync(true);
			_communication.Setup(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), false))
				.ReturnsAsync((string user, int departmentId, string message, string number, Department department, string title, UserProfile profile, bool _) =>
				{
					_notices.Add(new Notice { UserId = user, DepartmentId = departmentId, Message = message, Number = number, Department = department, Title = title, Profile = profile }); return true;
				});
			_service = new InventoryAlertNotifications(_alerts.Object, _departments.Object, _profiles.Object, _settings.Object, _communication.Object, new AlertClock(_now));
		}

		[Test]
		public async Task Alerts_only_claim_current_local_enabled_members_and_include_delegated_nonadministrators_once()
		{
			var delivery = Enqueue();
			_members.AddRange(new[] {
				new DepartmentMember { DepartmentId = DepartmentId, UserId = UserId, IsActive = true },
				new DepartmentMember { DepartmentId = 88, UserId = "foreign", IsAdmin = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "disabled", IsDisabled = true, IsAdmin = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "deleted", IsDeleted = true, IsAdmin = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = " " }
			});
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(1);
			_departments.Verify(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, true), Times.Once);
			_alerts.Verify(x => x.ClaimAlertAsync(DepartmentId, UserId), Times.Exactly(2));
			_alerts.Verify(x => x.ClaimAlertAsync(It.IsAny<int>(), It.Is<string>(u => u != UserId)), Times.Never);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, true), Times.Once);
			_notices.Should().ContainSingle(); _notices[0].UserId.Should().Be(UserId);
		}

		[Test]
		public async Task Alerts_load_fresh_routing_then_recheck_current_access_immediately_before_generic_localized_handoff()
		{
			var delivery = Enqueue(); _profile.Language = "fr"; var order = new List<string>();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(UserId, true)).Callback(() => order.Add("profile")).ReturnsAsync(_profile);
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, true)).Callback(() => order.Add("department")).ReturnsAsync(_department);
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DepartmentId)).Callback(() => order.Add("number")).ReturnsAsync("+15555550456");
			_alerts.Setup(x => x.CanReceiveAlertAsync(DepartmentId, UserId, delivery.AlertId)).Callback(() => order.Add("authorize")).ReturnsAsync(true);
			_communication.Setup(x => x.SendNotificationAsync(UserId, DepartmentId, It.IsAny<string>(), "+15555550456", _department, It.IsAny<string>(), _profile, false))
				.Callback((string _, int _, string message, string number, Department department, string title, UserProfile profile, bool _) =>
				{
					order.Add("send"); _notices.Add(new Notice { UserId = UserId, DepartmentId = DepartmentId, Message = message, Number = number, Department = department, Title = title, Profile = profile });
				}).ReturnsAsync(true);
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(1);
			order.Should().Equal("profile", "department", "number", "authorize", "send");
			_profiles.Verify(x => x.GetProfileByUserIdAsync(UserId, false), Times.Never);
			var notice = _notices.Single();
			notice.Title.Should().Be(InventoryReportDocuments.Text("M5AlertNotificationTitle", CultureInfo.GetCultureInfo("fr")));
			notice.Message.Should().StartWith(InventoryReportDocuments.Text("M5AlertNotificationMessage", CultureInfo.GetCultureInfo("fr")))
				.And.EndWith("/User/Inventory/Operations?tab=Alerts").And.NotContain(Canary).And.NotContain(UserId).And.NotContain(delivery.AlertId).And.NotContain(delivery.ClaimToken).And.NotContain("grant");
			notice.Title.Should().NotContain(Canary); notice.Department.Should().BeSameAs(_department); notice.Profile.Should().BeSameAs(_profile);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, true), Times.Once);
		}

		[Test]
		public async Task Alerts_recheck_membership_after_routing_and_do_not_send_when_the_recipient_is_disabled_during_lookup()
		{
			var delivery = Enqueue();
			_settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(DepartmentId)).Callback(() => _members[0].IsDisabled = true).ReturnsAsync("+15555550123");
			_alerts.Setup(x => x.CanReceiveAlertAsync(DepartmentId, UserId, delivery.AlertId)).ReturnsAsync(() => _members.Any(m => m.UserId == UserId && !m.IsDeleted && m.IsDisabled != true));
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(0);
			_alerts.Verify(x => x.CanReceiveAlertAsync(DepartmentId, UserId, delivery.AlertId), Times.Once);
			NoSend(); _alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
		}

		[Test]
		public async Task Alerts_do_not_send_when_current_alert_or_module_or_holder_permission_is_denied()
		{
			var delivery = Enqueue(); _alerts.Setup(x => x.CanReceiveAlertAsync(DepartmentId, UserId, delivery.AlertId)).ReturnsAsync(false);
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(0);
			NoSend(); _alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
		}

		[TestCase(null)]
		[TestCase(0)]
		[TestCase(30)]
		public async Task Alerts_with_missing_expired_or_expiring_leases_release_for_retry_before_handoff(int? secondsRemaining)
		{
			var delivery = Enqueue(); delivery.LeaseUntil = secondsRemaining.HasValue ? _now.UtcDateTime.AddSeconds(secondsRemaining.Value) : null;
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None);
			var error = await act.Should().ThrowAsync<InvalidOperationException>(); error.Which.Message.Should().Be("Inventory alert notifications require a retry.");
			NoSend(); _alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
			_alerts.Verify(x => x.FinishAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.Never);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Alerts_record_provider_handoff_separately_from_retry_using_the_claim_token(bool accepted)
		{
			var delivery = Enqueue();
			_communication.Setup(x => x.SendNotificationAsync(UserId, DepartmentId, It.IsAny<string>(), It.IsAny<string>(), _department, It.IsAny<string>(), _profile, false)).ReturnsAsync(accepted);
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(accepted ? 1 : 0);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, accepted), Times.Once);
			_alerts.Verify(x => x.FinishAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), !accepted), Times.Never);
		}

		[Test]
		public async Task Alerts_provider_exception_is_retriable_and_never_propagates_private_error_text()
		{
			var delivery = Enqueue();
			_communication.Setup(x => x.SendNotificationAsync(UserId, DepartmentId, It.IsAny<string>(), It.IsAny<string>(), _department, It.IsAny<string>(), _profile, false)).ThrowsAsync(new InvalidOperationException(Canary));
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None);
			var error = await act.Should().ThrowAsync<InvalidOperationException>(); error.Which.ToString().Should().NotContain(Canary);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
			_alerts.Verify(x => x.FinishAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.Never);
		}

		[Test]
		public async Task Alerts_failed_claim_completion_is_reported_for_retry_even_if_release_also_fails()
		{
			var delivery = Enqueue();
			_alerts.Setup(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException(Canary));
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None);
			var error = await act.Should().ThrowAsync<InvalidOperationException>(); error.Which.ToString().Should().NotContain(Canary);
			_notices.Should().ContainSingle();
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, true), Times.Once);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
		}

		[TestCase("department")]
		[TestCase("user")]
		[TestCase("token")]
		public async Task Alerts_reject_invalid_claim_identity_before_looking_up_or_notifying_a_recipient(string invalid)
		{
			var delivery = Enqueue();
			if (invalid == "department") delivery.DepartmentId = 88;
			if (invalid == "user") delivery.UserId = "foreign-user";
			if (invalid == "token") delivery.ClaimToken = null;
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None);
			var error = await act.Should().ThrowAsync<InvalidOperationException>(); error.Which.Message.Should().Be("Inventory alert claim metadata is invalid.");
			NoSend(); _profiles.Verify(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			_alerts.Verify(x => x.FinishAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Alerts_reject_foreign_department_routing_even_when_a_claim_was_valid()
		{
			var delivery = Enqueue(); _department.DepartmentId = 88;
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(0);
			NoSend(); _alerts.Verify(x => x.CanReceiveAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Once);
		}

		[Test]
		public async Task Alerts_cancellation_before_lookup_or_during_routing_does_not_send_or_acknowledge_the_claim()
		{
			using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, cancellation.Token);
			await act.Should().ThrowAsync<OperationCanceledException>();
			_departments.Verify(x => x.GetAllMembersForDepartmentUnlimitedAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			using var duringLookup = new CancellationTokenSource(); Enqueue();
			_profiles.Setup(x => x.GetProfileByUserIdAsync(UserId, true)).Callback(() => duringLookup.Cancel()).ReturnsAsync(_profile);
			act = () => _service.ProcessDepartmentAsync(DepartmentId, duringLookup.Token);
			await act.Should().ThrowAsync<OperationCanceledException>(); NoSend();
			_alerts.Verify(x => x.FinishAlertAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Alerts_persist_successful_handoff_when_cancellation_arrives_inside_the_provider()
		{
			var delivery = Enqueue(); using var cancellation = new CancellationTokenSource();
			_communication.Setup(x => x.SendNotificationAsync(UserId, DepartmentId, It.IsAny<string>(), It.IsAny<string>(), _department, It.IsAny<string>(), _profile, false))
				.Callback(() => cancellation.Cancel()).ReturnsAsync(true);
			Func<Task> act = () => _service.ProcessDepartmentAsync(DepartmentId, cancellation.Token);
			await act.Should().ThrowAsync<OperationCanceledException>();
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, true), Times.Once);
			_alerts.Verify(x => x.FinishAlertAsync(DepartmentId, delivery.Id, delivery.ClaimToken, false), Times.Never);
		}

		[Test]
		public async Task Alerts_bound_each_recipient_to_one_hundred_claims_per_run()
		{
			for (var i = 0; i < 101; i++) Enqueue();
			(await _service.ProcessDepartmentAsync(DepartmentId, CancellationToken.None)).Should().Be(100);
			_deliveries.Should().ContainSingle(); _notices.Should().HaveCount(100);
			_alerts.Verify(x => x.ClaimAlertAsync(DepartmentId, UserId), Times.Exactly(100));
		}

		private InventoryAlertDelivery Enqueue()
		{
			var delivery = new InventoryAlertDelivery { Id = Guid.NewGuid().ToString("D"), DepartmentId = DepartmentId, UserId = UserId,
				AlertId = Guid.NewGuid().ToString("D"), ClaimToken = Guid.NewGuid().ToString("D"), LeaseUntil = _now.UtcDateTime.AddMinutes(5), Content = Canary };
			_deliveries.Enqueue(delivery); return delivery;
		}
		private void NoSend() => _communication.Verify(x => x.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);
		private sealed class AlertClock : TimeProvider
		{
			private readonly DateTimeOffset _now;
			public AlertClock(DateTimeOffset now) => _now = now;
			public override DateTimeOffset GetUtcNow() => _now;
		}
		private sealed class Notice
		{
			public string UserId { get; set; }
			public int DepartmentId { get; set; }
			public string Message { get; set; }
			public string Number { get; set; }
			public Department Department { get; set; }
			public string Title { get; set; }
			public UserProfile Profile { get; set; }
		}
	}
}
