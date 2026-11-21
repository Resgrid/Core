using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Dedicated department numbers (Dedicated Number add-on): who may buy one, what a purchase leaves behind (the Active row, the
	/// legacy TextToCallNumber setting, the SMS and voice intake channels), worker 80's lapse / release / reactivation lifecycle with T0
	/// from add-on or plan loss (plan §8 D10), the admins' release warnings (D12) and the number search filters (D13).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DepartmentPhoneNumbersServiceTests
	{
		private const int DepartmentId = 42;
		private const string Number = "+17755550100";

		private MemoryNumbers _repository;
		private Mock<IDedicatedNumberAccessService> _access;
		private Mock<INumberProvider> _carrier;
		private Mock<IPhoneNumberProcesserProvider> _phoneNumbers;
		private Mock<IIntakeChannelsService> _channels;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentsService> _departments;
		private Mock<ICommunicationService> _communication;
		private Mock<IGeoLocationProvider> _geo;
		private Mock<IAddressService> _addresses;
		private DepartmentPhoneNumbersService _service;
		private Department _department;
		private string _textToCall;
		private bool? _addon;
		private bool? _plan;
		private bool _delivered;
		private string[] _allowedCountries;
		private List<(string UserId, string Title, string Text)> _sent;
		private (int LapseDelay, int Grace, string WarningDays) _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (DedicatedNumberAddonConfig.LapseDelayHours, DedicatedNumberAddonConfig.ReleaseGraceDays, DedicatedNumberAddonConfig.ReleaseWarningDays);
			DedicatedNumberAddonConfig.LapseDelayHours = 72;
			DedicatedNumberAddonConfig.ReleaseGraceDays = 30;
			DedicatedNumberAddonConfig.ReleaseWarningDays = "15,5,1";

			_repository = new MemoryNumbers();
			_addon = true;
			_plan = true;
			_delivered = true;
			_allowedCountries = new[] { "US", "CA" };
			_access = new Mock<IDedicatedNumberAccessService>();
			_access.Setup(a => a.GetAccessAsync(It.IsAny<int>())).ReturnsAsync(() => new DedicatedNumberAccess
			{
				HasBillingRelationship = _plan == true, IsOffered = true, HasActiveAddon = _addon == true, BillingProvider = "Stripe", AllowedCountries = _allowedCountries
			});
			_access.Setup(a => a.HasActiveAddonAsync(It.IsAny<int>())).ReturnsAsync(() => _addon == true);
			_access.Setup(a => a.GetActiveAddonStateAsync(It.IsAny<int>())).ReturnsAsync(() => _addon);
			// The real service reads the plan only while the add-on is paid (DedicatedNumberAccessServiceTests).
			_access.Setup(a => a.GetPaymentStateAsync(It.IsAny<int>())).ReturnsAsync(() => _addon == true
				? new DedicatedNumberPaymentState { AddonActive = true, PaidEntityPlan = _plan }
				: new DedicatedNumberPaymentState { AddonActive = _addon });

			_carrier = new Mock<INumberProvider>();
			_carrier.Setup(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string number, string country, string name, CancellationToken _) => new CarrierNumberPurchase
					{ Success = true, Provider = "Twilio", ProviderNumberId = "PN0001", PhoneNumber = number, SmsCapable = true, VoiceCapable = true });
			_carrier.Setup(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_phoneNumbers = new Mock<IPhoneNumberProcesserProvider>();
			_phoneNumbers.Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>()))
				.Returns((string number, string _) => new PhoneNumberResult { IsValid = true, Region = number.StartsWith("+44") ? "GB" : "US" });

			_channels = new Mock<IIntakeChannelsService>();

			_textToCall = null;
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetTextToCallNumberForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _textToCall);
			_settings.Setup(s => s.SaveOrUpdateSettingAsync(DepartmentId, It.IsAny<string>(), DepartmentSettingTypes.TextToCallNumber, It.IsAny<CancellationToken>()))
				.Callback((int d, string value, DepartmentSettingTypes t, CancellationToken _) => _textToCall = value)
				.ReturnsAsync(new DepartmentSetting());
			_settings.Setup(s => s.DeleteSettingAsync(DepartmentId, DepartmentSettingTypes.TextToCallNumber, It.IsAny<CancellationToken>()))
				.Callback(() => _textToCall = null).ReturnsAsync(true);

			_department = new Department { DepartmentId = DepartmentId, Name = "Station Fixture", TimeZone = "Eastern Standard Time" };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _department);
			_departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "admin-1" }, new IdentityUser { Id = "admin-2" }, new IdentityUser { Id = "admin-1" } });

			_sent = new List<(string, string, string)>();
			_communication = new Mock<ICommunicationService>();
			_communication.Setup(c => c.SendNotificationDeliveredAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()))
				.Callback((string userId, int d, string text, string number, Department dep, string title, UserProfile p, bool ic, string code) => _sent.Add((userId, title, text)))
				.ReturnsAsync(() => _delivered);

			_geo = new Mock<IGeoLocationProvider>();
			_addresses = new Mock<IAddressService>();

			_service = new DepartmentPhoneNumbersService(_repository, _access.Object, _carrier.Object, _phoneNumbers.Object, _channels.Object, _settings.Object,
				_departments.Object, _communication.Object, _geo.Object, _addresses.Object);
		}

		[TearDown]
		public void TearDown() =>
			(DedicatedNumberAddonConfig.LapseDelayHours, DedicatedNumberAddonConfig.ReleaseGraceDays, DedicatedNumberAddonConfig.ReleaseWarningDays) = _savedConfig;

		/// <summary>The warnings one sweep sent (one per admin), by title.</summary>
		private List<string> SentTitles() => _sent.Select(s => s.Title).Distinct().ToList();

		private string LocalDate(DateTime utc) => utc.TimeConverter(_department).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

		private string LocalDateTime(DateTime utc) => utc.TimeConverter(_department).ToString("MMMM d, yyyy, HH:mm", CultureInfo.InvariantCulture);

		private DepartmentPhoneNumber Held(DepartmentPhoneNumberStatuses status, int departmentId = DepartmentId, string number = Number, DateTime? releaseAfter = null)
		{
			var row = new DepartmentPhoneNumber
			{
				DepartmentPhoneNumberId = Guid.NewGuid().ToString(), DepartmentId = departmentId, PhoneNumber = number, Provider = "Twilio", ProviderNumberId = "PN" + departmentId,
				CountryCode = "US", Status = (int)status, SmsIntakeEnabled = true, ReleaseAfter = releaseAfter, CreatedOn = DateTime.UtcNow.AddDays(-30)
			};
			_repository.Rows.Add(row);
			return row;
		}

		private DepartmentPhoneNumber Row(string id) => _repository.Rows.Single(r => r.DepartmentPhoneNumberId == id);

		[Test]
		public async Task Provisioning_without_the_add_on_is_refused_before_the_carrier_is_asked()
		{
			_addon = false;

			var refused = await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, Number, "admin")).Should().ThrowAsync<DedicatedNumberException>();

			refused.Which.Error.Should().Be(DedicatedNumberErrors.AddonRequired);
			_carrier.Verify(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_repository.Rows.Should().BeEmpty();
		}

		[TestCase(false)]
		[TestCase(null)]
		public async Task Provisioning_needs_an_active_paid_entity_plan_too(bool? plan)
		{
			// The add-on is still in its paid interval, but the plan behind it ended (or cannot be read): no new number is bought.
			_plan = plan;

			var refused = await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, Number, "admin")).Should().ThrowAsync<DedicatedNumberException>();

			refused.Which.Error.Should().Be(DedicatedNumberErrors.PlanRequired);
			_carrier.Verify(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_repository.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_department_keeps_one_number_and_a_lapsed_one_counts()
		{
			Held(DepartmentPhoneNumberStatuses.Lapsed, number: "+17755550199");

			var refused = await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, Number, "admin")).Should().ThrowAsync<DedicatedNumberException>();

			refused.Which.Error.Should().Be(DedicatedNumberErrors.AlreadyHasNumber);
			_carrier.Verify(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_number_outside_the_allowed_countries_or_held_by_another_department_is_refused()
		{
			(await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, "+442071234567", "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.CountryNotAllowed);

			Held(DepartmentPhoneNumberStatuses.Active, departmentId: 7);
			(await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, Number, "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.NumberUnavailable);

			(await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, "555", "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.InvalidNumber);
		}

		[Test]
		public async Task A_purchase_activates_the_number_mirrors_text_to_call_and_ensures_both_intake_channels()
		{
			var row = await _service.ProvisionAsync(DepartmentId, "+1 (775) 555-0100", "admin");

			row.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			row.PhoneNumber.Should().Be(Number);
			row.ProviderNumberId.Should().Be("PN0001");
			row.CountryCode.Should().Be("US");
			row.SmsIntakeEnabled.Should().BeTrue();
			row.VoiceIntakeEnabled.Should().BeFalse("voice intake needs Enhanced AI, so an admin turns it on");
			row.ProvisionedOn.Should().NotBeNull();
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			_textToCall.Should().Be("17755550100", "the legacy setting holds digits only, as NumbersService wrote it");
			_carrier.Verify(c => c.PurchaseDedicatedNumberAsync(Number, "US", It.Is<string>(n => n.Contains("Station Fixture")), It.IsAny<CancellationToken>()), Times.Once);
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()), Times.Once);
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, Number, It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()), Times.Once);
			(await _service.GetActiveForDepartmentAsync(DepartmentId)).DepartmentPhoneNumberId.Should().Be(row.DepartmentPhoneNumberId);
			(await _service.GetByNumberAsync("1-775-555-0100")).DepartmentId.Should().Be(DepartmentId);
		}

		[Test]
		public async Task A_carrier_refusal_leaves_a_failed_row_and_nothing_routed()
		{
			_carrier.Setup(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new CarrierNumberPurchase { Error = "Twilio 21422 (HTTP 400): number not available" });

			var row = await _service.ProvisionAsync(DepartmentId, Number, "admin");

			row.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Failed);
			row.LastError.Should().Contain("21422");
			row.ProviderNumberId.Should().BeNull();
			_textToCall.Should().BeNull();
			_channels.Verify(c => c.EnsureChannelAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			(await _service.GetByNumberAsync(Number)).Should().BeNull();

			// A failed purchase holds nothing: the department may try again.
			_carrier.Setup(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new CarrierNumberPurchase { Success = true, ProviderNumberId = "PN0002", PhoneNumber = Number, SmsCapable = true, VoiceCapable = true });
			(await _service.ProvisionAsync(DepartmentId, Number, "admin")).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
		}

		[Test]
		public async Task Sms_intake_follows_its_switch_on_the_sms_channel_and_voice_is_kept_on_the_row()
		{
			Held(DepartmentPhoneNumberStatuses.Active);

			var row = await _service.UpdateIntakeOptionsAsync(DepartmentId, false, true, "admin");

			row.SmsIntakeEnabled.Should().BeFalse();
			row.VoiceIntakeEnabled.Should().BeTrue();
			_channels.Verify(c => c.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<CancellationToken>()), Times.Once);
			_channels.Verify(c => c.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Voice, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			await _service.UpdateIntakeOptionsAsync(DepartmentId, true, true, "admin");
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()), Times.Once);

			_repository.Rows.Clear();
			(await FluentActions.Awaiting(() => _service.UpdateIntakeOptionsAsync(DepartmentId, true, true, "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.NoActiveNumber);
		}

		[Test]
		public async Task Lapsing_stops_intake_and_clears_text_to_call_only_while_it_names_the_number()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_textToCall = "17755550100";
			var releaseAfter = DateTime.UtcNow.AddDays(14);

			await _service.MarkLapsedAsync(DepartmentId, releaseAfter);

			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
			Row(row.DepartmentPhoneNumberId).ReleaseAfter.Should().Be(releaseAfter);
			_textToCall.Should().BeNull();
			_channels.Verify(c => c.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<CancellationToken>()), Times.Once);
			_channels.Verify(c => c.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Voice, Number, It.IsAny<CancellationToken>()), Times.Once);
			(await _service.GetByNumberAsync(Number)).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed, "intake still finds a lapsed number to answer it");
		}

		[Test]
		public async Task A_text_to_call_number_set_by_hand_survives_a_release()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_textToCall = "15555550123";

			(await _service.ReleaseAsync(row.DepartmentPhoneNumberId)).Should().BeTrue();

			_textToCall.Should().Be("15555550123");
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Released);
			Row(row.DepartmentPhoneNumberId).ReleasedOn.Should().NotBeNull();
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync("PN" + DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_release_the_carrier_refuses_changes_nothing_but_the_error()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_textToCall = "17755550100";
			_carrier.Setup(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await _service.ReleaseAsync(row.DepartmentPhoneNumberId)).Should().BeFalse();

			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			Row(row.DepartmentPhoneNumberId).LastError.Should().NotBeNullOrEmpty();
			_textToCall.Should().Be("17755550100");
			_channels.Verify(c => c.DisableChannelsForAddressAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Add_on_loss_starts_T0_intake_stops_72_hours_later_and_the_number_goes_at_T0_plus_30_days()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_textToCall = "17755550100";
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);

			(await _service.ProcessLifecycleAsync(t0)).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active, "a renewal invoice is paid a little after the period ends");
			Row(row.DepartmentPhoneNumberId).AddonInactiveSince.Should().Be(t0);
			Row(row.DepartmentPhoneNumberId).ReleaseAfter.Should().BeNull("an Active number has no release date stored");
			DedicatedNumberReleaseSchedule.For(Row(row.DepartmentPhoneNumberId)).ReleaseOn.Should().Be(t0.AddDays(30));
			DedicatedNumberReleaseSchedule.For(Row(row.DepartmentPhoneNumberId)).IntakeStopsOn.Should().Be(t0.AddHours(72));

			(await _service.ProcessLifecycleAsync(t0.AddHours(71))).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			Row(row.DepartmentPhoneNumberId).AddonInactiveSince.Should().Be(t0, "T0 is the first sweep that saw it, not the latest");

			(await _service.ProcessLifecycleAsync(t0.AddHours(72))).Should().Be(1);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
			Row(row.DepartmentPhoneNumberId).ReleaseAfter.Should().Be(t0.AddDays(30), "release is T0 + ReleaseGraceDays, not the lapse time + grace");
			_textToCall.Should().BeNull();
			_channels.Verify(c => c.DisableChannelsForAddressAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<CancellationToken>()), Times.Once);

			(await _service.ProcessLifecycleAsync(t0.AddDays(30).AddMinutes(-1))).Should().Be(0);
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			(await _service.ProcessLifecycleAsync(t0.AddDays(30))).Should().Be(1);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Released);
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync("PN" + DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Plan_loss_with_the_add_on_still_paid_also_starts_T0_and_its_warning_waits_an_hour_for_confirmation()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_plan = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);

			await _service.ProcessLifecycleAsync(t0);
			Row(row.DepartmentPhoneNumberId).AddonInactiveSince.Should().Be(t0);
			_sent.Should().BeEmpty("one plan read that says free may be the Billing API's safety net, so the warning waits");

			// A paying sweep inside the hour clears T0, and nobody was told anything.
			_plan = true;
			await _service.ProcessLifecycleAsync(t0.AddMinutes(15));
			Row(row.DepartmentPhoneNumberId).AddonInactiveSince.Should().BeNull();
			_sent.Should().BeEmpty();

			_plan = false;
			await _service.ProcessLifecycleAsync(t0.AddMinutes(30));
			await _service.ProcessLifecycleAsync(t0.AddMinutes(89));
			_sent.Should().BeEmpty();
			await _service.ProcessLifecycleAsync(t0.AddMinutes(90));
			SentTitles().Should().Equal("Private Inbound Dispatch Number: plan payment stopped");
			_sent.Single(s => s.UserId == "admin-1").Text.Should().Contain("no longer an active paid plan")
				.And.Contain(LocalDate(t0.AddMinutes(30).AddDays(30)));

			(await _service.ProcessLifecycleAsync(t0.AddMinutes(30).AddHours(72))).Should().Be(1);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
			Row(row.DepartmentPhoneNumberId).ReleaseAfter.Should().Be(t0.AddMinutes(30).AddDays(30));
		}

		[Test]
		public async Task A_renewal_inside_the_gap_clears_the_marker_and_a_returning_add_on_reactivates_a_lapsed_number()
		{
			var active = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var now = DateTime.UtcNow;
			await _service.ProcessLifecycleAsync(now);
			Row(active.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be((int)DedicatedNumberReleaseWarnings.Stopped);
			_addon = true;
			await _service.ProcessLifecycleAsync(now.AddHours(2));
			Row(active.DepartmentPhoneNumberId).AddonInactiveSince.Should().BeNull();
			Row(active.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(0, "the warnings belonged to the unpaid period that ended");
			Row(active.DepartmentPhoneNumberId).LastReleaseWarningOn.Should().BeNull();

			_repository.Rows.Clear();
			var lapsed = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: now.AddDays(-1));
			lapsed.AddonInactiveSince = now.AddDays(-31);
			lapsed.ReleaseWarningsSent = 15;
			lapsed.LastReleaseWarningOn = now.AddDays(-2);
			(await _service.ProcessLifecycleAsync(now)).Should().Be(1);

			Row(lapsed.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active, "the carrier still holds it, so it comes back instead of being released");
			Row(lapsed.DepartmentPhoneNumberId).ReleaseAfter.Should().BeNull();
			Row(lapsed.DepartmentPhoneNumberId).AddonInactiveSince.Should().BeNull();
			Row(lapsed.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(0);
			Row(lapsed.DepartmentPhoneNumberId).LastReleaseWarningOn.Should().BeNull();
			_textToCall.Should().Be("17755550100");
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, Number, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_returning_add_on_does_not_restore_while_the_plan_is_known_not_to_be_paid()
		{
			var lapsed = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: DateTime.UtcNow.AddDays(10));
			lapsed.AddonInactiveSince = DateTime.UtcNow.AddDays(-20);
			_plan = false;

			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(0);
			Row(lapsed.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);

			(await FluentActions.Awaiting(() => _service.ReactivateAsync(DepartmentId, "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.PlanRequired);
		}

		[TestCase(null, null)]
		[TestCase(true, null)]
		public async Task An_unknown_add_on_or_plan_state_never_starts_T0_lapses_releases_or_warns(bool? addon, bool? plan)
		{
			var active = Held(DepartmentPhoneNumberStatuses.Active, departmentId: DepartmentId);
			var lapsed = Held(DepartmentPhoneNumberStatuses.Lapsed, departmentId: 43, number: "+17755550111", releaseAfter: DateTime.UtcNow.AddDays(-30));
			_addon = addon;
			_plan = plan;

			(await _service.ProcessLifecycleAsync(DateTime.UtcNow.AddDays(60))).Should().Be(0);

			Row(active.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			Row(active.DepartmentPhoneNumberId).AddonInactiveSince.Should().BeNull();
			Row(lapsed.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_sent.Should().BeEmpty();
		}

		[Test]
		public async Task The_lifecycle_asks_billing_only_about_departments_that_hold_a_number()
		{
			Held(DepartmentPhoneNumberStatuses.Active, departmentId: 5, number: "+17755550105");
			Held(DepartmentPhoneNumberStatuses.Lapsed, departmentId: 6, number: "+17755550106", releaseAfter: DateTime.UtcNow.AddDays(5));
			Held(DepartmentPhoneNumberStatuses.Released, departmentId: 7, number: "+17755550107");
			Held(DepartmentPhoneNumberStatuses.Failed, departmentId: 8, number: "+17755550108");

			await _service.ProcessLifecycleAsync(DateTime.UtcNow);

			_access.Verify(a => a.GetPaymentStateAsync(5), Times.Once);
			_access.Verify(a => a.GetPaymentStateAsync(6), Times.Once);
			_access.Verify(a => a.GetPaymentStateAsync(7), Times.Never);
			_access.Verify(a => a.GetPaymentStateAsync(8), Times.Never);
		}

		[Test]
		public async Task Payment_stopped_is_sent_once_at_T0_with_the_lapse_and_release_dates()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);

			await _service.ProcessLifecycleAsync(t0);

			_sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { "admin-1", "admin-2" }, "each active admin once");
			_sent.Should().OnlyContain(s => s.Title == "Private Inbound Dispatch Number payment stopped");
			_sent[0].Text.Should().Be("Station Fixture: payment for the Private Inbound Dispatch Number add-on has stopped (cancelled or unpaid). " +
				$"Texts and calls to {Number} stop on {LocalDateTime(t0.AddHours(72))} and the number is released on {LocalDate(t0.AddDays(30))}. " +
				"Renew the add-on (and your plan) before then to keep it.");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be((int)DedicatedNumberReleaseWarnings.Stopped);
			Row(row.DepartmentPhoneNumberId).LastReleaseWarningOn.Should().Be(t0);

			_sent.Clear();
			await _service.ProcessLifecycleAsync(t0.AddHours(1));
			await _service.ProcessLifecycleAsync(t0.AddHours(72));
			_sent.Should().BeEmpty("a stage is never sent twice");
		}

		[Test]
		public async Task Each_warning_stage_goes_once_on_its_day_before_release()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			var release = t0.AddDays(30);
			var titles = new List<string>();

			// Worker 80 sweeps every 15 minutes; walk the whole unpaid month a few hours at a time.
			for (var now = t0; now < release; now = now.AddHours(6))
			{
				_sent.Clear();
				await _service.ProcessLifecycleAsync(now);
				titles.AddRange(SentTitles());
				if (now == release.AddDays(-15))
					SentTitles().Should().Equal("Private Inbound Dispatch Number release in 15 days");
			}

			titles.Should().Equal("Private Inbound Dispatch Number payment stopped", "Private Inbound Dispatch Number release in 15 days", "Private Inbound Dispatch Number release in 5 days",
				"Private Inbound Dispatch Number release in 1 day");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(15);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
		}

		[Test]
		public async Task The_last_warning_names_the_release_date_and_says_intake_has_stopped()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			await _service.ProcessLifecycleAsync(t0);
			await _service.ProcessLifecycleAsync(t0.AddDays(15));
			await _service.ProcessLifecycleAsync(t0.AddDays(25));
			_sent.Clear();

			await _service.ProcessLifecycleAsync(t0.AddDays(29));

			_sent.Should().HaveCount(2);
			_sent[0].Text.Should().Be($"Station Fixture: the private inbound dispatch number {Number} will be released on {LocalDate(t0.AddDays(30))}, in 1 day, because payment " +
				$"for the Private Inbound Dispatch Number add-on has stopped. Texts and calls to it stopped on {LocalDateTime(t0.AddHours(72))}. Renew the add-on (and your plan) " +
				"before then to keep the number; once released it goes back to the carrier and may not be available again.");
		}

		[Test]
		public async Task A_late_sweep_sends_only_the_latest_due_stage_and_marks_the_earlier_ones()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			await _service.ProcessLifecycleAsync(t0);
			_sent.Clear();

			// The worker was down from T0 + 1 day to T0 + 26 days: the 15-day and 5-day stages are both due; only the 5-day one goes.
			await _service.ProcessLifecycleAsync(t0.AddDays(26));

			SentTitles().Should().Equal("Private Inbound Dispatch Number release in 4 days");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be((int)(DedicatedNumberReleaseWarnings.Stopped | DedicatedNumberReleaseWarnings.FifteenDays |
				DedicatedNumberReleaseWarnings.FiveDays));

			_sent.Clear();
			await _service.ProcessLifecycleAsync(t0.AddDays(27));
			_sent.Should().BeEmpty();
			await _service.ProcessLifecycleAsync(t0.AddDays(29));
			SentTitles().Should().Equal("Private Inbound Dispatch Number release in 1 day");
		}

		[Test]
		public async Task A_first_sweep_that_is_already_late_sends_one_message_not_four()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: new DateTime(2026, 12, 31, 15, 0, 0, DateTimeKind.Utc));
			row.AddonInactiveSince = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			_addon = false;

			await _service.ProcessLifecycleAsync(new DateTime(2026, 12, 30, 16, 0, 0, DateTimeKind.Utc));

			SentTitles().Should().Equal("Private Inbound Dispatch Number release in 1 day");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(15);
		}

		[Test]
		public async Task A_failed_send_is_retried_by_the_next_sweep_and_then_never_again()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Active);
			_addon = false;
			var t0 = new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
			_delivered = false;

			await _service.ProcessLifecycleAsync(t0);
			_sent.Should().NotBeEmpty("the admins were tried");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(0, "nothing reached anyone, so nothing is marked sent");
			Row(row.DepartmentPhoneNumberId).LastReleaseWarningOn.Should().BeNull();

			_delivered = true;
			_sent.Clear();
			await _service.ProcessLifecycleAsync(t0.AddMinutes(15));
			SentTitles().Should().Equal("Private Inbound Dispatch Number payment stopped");
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be((int)DedicatedNumberReleaseWarnings.Stopped);

			_sent.Clear();
			await _service.ProcessLifecycleAsync(t0.AddMinutes(30));
			_sent.Should().BeEmpty();

			// A throwing channel is a failed send too.
			_communication.Setup(c => c.SendNotificationDeliveredAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(),
					It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>(), It.IsAny<string>()))
				.ThrowsAsync(new InvalidOperationException("email down"));
			await _service.ProcessLifecycleAsync(t0.AddDays(15));
			Row(row.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be((int)DedicatedNumberReleaseWarnings.Stopped);
		}

		[Test]
		public async Task A_cancelled_renewal_tells_the_admins_when_texts_stop_and_when_the_number_is_released()
		{
			Held(DepartmentPhoneNumberStatuses.Active);
			var end = DateTime.UtcNow.AddDays(12);

			(await _service.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.AddonRenewal, end)).Should().BeTrue();

			_sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { "admin-1", "admin-2" });
			// Product name "Private Inbound Dispatch Number" (renamed 2026-11-20; brand kept in English like the other add-ons).
			_sent[0].Title.Should().Be("Private Inbound Dispatch Number add-on cancelled");
			_sent[0].Text.Should().Be($"Station Fixture: the Private Inbound Dispatch Number add-on renewal was cancelled. The add-on ends on {LocalDate(end)}; texts and calls to {Number} " +
				$"stop on {LocalDateTime(end.AddHours(72))} and the number is released on {LocalDate(end.AddDays(30))}. Renew the add-on before then to keep the number.");

			_sent.Clear();
			(await _service.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.Plan, end)).Should().BeTrue();
			_sent[0].Text.Should().StartWith("Station Fixture: the department's Resgrid plan was cancelled.").And.Contain(LocalDate(end.AddDays(30)));

			_sent.Clear();
			(await _service.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.AddonRenewal, null)).Should().BeTrue();
			_sent[0].Text.Should().Contain("stop 72 hours after it ends and the number is released 30 days after it ends");

			_sent.Clear();
			(await _service.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.Plan, DateTime.UtcNow)).Should().BeFalse(
				"an immediate end is the lifecycle's payment-stopped warning");
			_repository.Rows.Clear();
			(await _service.NotifyCancellationScheduledAsync(DepartmentId, DedicatedNumberCancellations.AddonRenewal, end)).Should().BeFalse("no number, no notice");
			_sent.Should().BeEmpty();
		}

		[Test]
		public async Task Search_filters_reach_the_carrier_validated_and_normalized()
		{
			DedicatedNumberSearch asked = null;
			_carrier.Setup(c => c.SearchDedicatedNumbersAsync(It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.Callback((DedicatedNumberSearch s, CancellationToken _) => asked = s)
				.ReturnsAsync(new List<AvailablePhoneNumber>
				{
					new AvailablePhoneNumber { PhoneNumber = Number, CountryCode = "US", Locality = "Reno", Region = "NV", PostalCode = "89501", NumberType = DedicatedNumberTypes.Local },
					new AvailablePhoneNumber { PhoneNumber = "not a number" }
				});
			_settings.Setup(s => s.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId)).ReturnsAsync("39.5296,-119.8138");

			var results = await _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch
			{
				CountryCode = " us", Region = "nevada", Locality = "  Reno ", PostalCode = "89501-1234", AreaCode = "775", Contains = "55",
				NearDepartment = true, DistanceMiles = 900
			});

			results.Should().ContainSingle().Which.PostalCode.Should().Be("89501");
			asked.CountryCode.Should().Be("US");
			asked.Region.Should().Be("NV");
			asked.Locality.Should().Be("Reno");
			asked.PostalCode.Should().Be("89501");
			asked.AreaCode.Should().Be("775");
			asked.Contains.Should().Be("55");
			asked.NearLatitude.Should().Be(39.5296);
			asked.NearLongitude.Should().Be(-119.8138);
			asked.DistanceMiles.Should().Be(500, "Twilio's radius tops out at 500 miles");
			asked.Limit.Should().Be(20);
			_geo.Verify(g => g.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "the map center is known");

			await _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch { CountryCode = "CA", Region = "on", PostalCode = "k1a0b1" });
			asked.Region.Should().Be("ON");
			asked.PostalCode.Should().Be("K1A 0B1");
			asked.NearLatitude.Should().BeNull();
			asked.DistanceMiles.Should().BeNull();

			// The old signature still searches (country, area code, digits).
			await _service.SearchAvailableAsync(DepartmentId, "US", "775", null);
			asked.AreaCode.Should().Be("775");
			asked.Region.Should().BeNull();
		}

		[Test]
		public async Task Outside_the_united_states_and_canada_only_city_and_digits_reach_the_carrier()
		{
			_allowedCountries = new[] { "GB" };
			DedicatedNumberSearch asked = null;
			_carrier.Setup(c => c.SearchDedicatedNumbersAsync(It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.Callback((DedicatedNumberSearch s, CancellationToken _) => asked = s).ReturnsAsync(new List<AvailablePhoneNumber>());

			await _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch
			{
				CountryCode = "GB", Region = "nowhere", Locality = "London", PostalCode = "SW1A 1AA", AreaCode = "20", NearDepartment = true
			});

			asked.CountryCode.Should().Be("GB");
			asked.Locality.Should().Be("London");
			asked.Region.Should().BeNull();
			asked.PostalCode.Should().BeNull();
			asked.NearLatitude.Should().BeNull();
			asked.AreaCode.Should().Be("20", "outside NANP the provider folds it into the digit pattern");
			_settings.Verify(s => s.GetBigBoardCenterGpsCoordinatesDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Near_the_department_geocodes_its_address_and_refuses_when_nothing_is_known()
		{
			DedicatedNumberSearch asked = null;
			_carrier.Setup(c => c.SearchDedicatedNumbersAsync(It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.Callback((DedicatedNumberSearch s, CancellationToken _) => asked = s).ReturnsAsync(new List<AvailablePhoneNumber>());
			_department.AddressId = 9;
			_addresses.Setup(a => a.GetAddressByIdAsync(9)).ReturnsAsync(new Address { Address1 = "1 Main St", City = "Reno", State = "NV", PostalCode = "89501", Country = "United States" });
			_geo.Setup(g => g.GeocodeAddressAsync("1 Main St Reno NV 89501 United States", It.IsAny<CancellationToken>())).ReturnsAsync(GeocodeResult.Found(39.5, -119.8));

			await _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch { CountryCode = "US", NearDepartment = true });
			asked.NearLatitude.Should().Be(39.5);
			asked.NearLongitude.Should().Be(-119.8);
			asked.DistanceMiles.Should().Be(25);

			_geo.Setup(g => g.GeocodeAddressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GeocodeResult.Of(GeocodeOutcome.NotFound));
			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch { CountryCode = "US", NearDepartment = true }))
				.Should().ThrowAsync<DedicatedNumberException>()).Which.Error.Should().Be(DedicatedNumberErrors.DepartmentLocationUnknown);
		}

		[TestCase("US", "ZZ", null, null)]
		[TestCase("US", "ON", null, null)]
		[TestCase("US", null, "8950", null)]
		[TestCase("CA", null, "89501", null)]
		[TestCase("US", null, null, "Reno 1")]
		[TestCase("US", null, null, "Reno;DROP")]
		public async Task Bad_search_filters_are_refused_before_the_carrier_is_asked(string country, string region, string postalCode, string city)
		{
			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, new DedicatedNumberSearch
			{
				CountryCode = country, Region = region, PostalCode = postalCode, Locality = city
			})).Should().ThrowAsync<DedicatedNumberException>()).Which.Error.Should().Be(DedicatedNumberErrors.InvalidNumber);

			_carrier.Verify(c => c.SearchDedicatedNumbersAsync(It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("15,5,1", new[] { 15, 5, 1 })]
		[TestCase(" 1 ; 5 , 15 ", new[] { 15, 5, 1 })]
		[TestCase("30,15,5,1", new[] { 15, 5, 1 })]
		[TestCase("10,x,-3,0,10", new[] { 10 })]
		[TestCase("", new[] { 15, 5, 1 })]
		[TestCase("nonsense", new[] { 15, 5, 1 })]
		public void Release_warning_days_parse_robustly(string configured, int[] expected)
		{
			DedicatedNumberReleaseSchedule.WarningDays(configured).Should().Equal(expected);
		}

		[Test]
		public async Task A_purchase_that_never_finished_is_failed_by_the_lifecycle()
		{
			var stale = Held(DepartmentPhoneNumberStatuses.Provisioning);
			stale.CreatedOn = DateTime.UtcNow.AddHours(-2);
			stale.ProviderNumberId = null;

			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(1);

			Row(stale.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Failed);
			Row(stale.DepartmentPhoneNumberId).LastError.Should().NotBeNullOrEmpty();
		}

		[Test]
		public async Task Reactivation_by_an_admin_needs_the_add_on()
		{
			var lapsed = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: DateTime.UtcNow.AddDays(3));
			lapsed.ReleaseWarningsSent = 3;
			_addon = false;
			(await FluentActions.Awaiting(() => _service.ReactivateAsync(DepartmentId, "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.AddonRequired);

			_addon = true;
			_plan = null;
			(await _service.ReactivateAsync(DepartmentId, "admin")).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active, "an unreadable plan does not block the admin");
			Row(lapsed.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			Row(lapsed.DepartmentPhoneNumberId).ReleaseWarningsSent.Should().Be(0);
		}

		[Test]
		public async Task Search_needs_the_add_on_an_allowed_country_and_sane_digits()
		{
			_carrier.Setup(c => c.SearchDedicatedNumbersAsync(It.Is<DedicatedNumberSearch>(s => s.CountryCode == "US" && s.AreaCode == "775" && s.Contains == null),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<AvailablePhoneNumber>
				{
					new AvailablePhoneNumber { PhoneNumber = Number, CountryCode = "US", SmsCapable = true, VoiceCapable = true },
					new AvailablePhoneNumber { PhoneNumber = "+15195550100", CountryCode = "CA", SmsCapable = true, VoiceCapable = true }
				});

			(await _service.SearchAvailableAsync(DepartmentId, "us", "775", null)).Select(n => n.PhoneNumber).Should().Equal(Number);

			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, "GB", null, null)).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.CountryNotAllowed);
			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, "US", "77a", null)).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.InvalidNumber);

			_carrier.Setup(c => c.SearchDedicatedNumbersAsync(It.IsAny<DedicatedNumberSearch>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("The carrier number search failed."));
			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, "US", null, "55")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.CarrierUnavailable);

			_addon = false;
			(await FluentActions.Awaiting(() => _service.SearchAvailableAsync(DepartmentId, "US", null, null)).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.AddonRequired);
		}

		[Test]
		public async Task Department_deletion_releases_only_held_numbers_with_a_carrier_id()
		{
			var active = Held(DepartmentPhoneNumberStatuses.Active);
			var released = Held(DepartmentPhoneNumberStatuses.Released, number: "+17755550188");
			released.ProviderNumberId = "PNold";
			var provisioning = Held(DepartmentPhoneNumberStatuses.Provisioning, number: "+17755550177");
			provisioning.ProviderNumberId = null;

			(await _service.ReleaseAtCarrierAsync(new[] { active, released, provisioning })).Should().Be(1);

			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync("PN" + DepartmentId, It.IsAny<CancellationToken>()), Times.Once);
			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync("PNold", It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Two_admins_buying_different_numbers_at_once_buy_one()
		{
			// PR #588: both requests passed the "already has a number" read before either claim landed. The department index lets only one
			// claim insert; the other is refused before the carrier is asked.
			_repository.BeforeInsert = entity =>
			{
				_repository.BeforeInsert = null;
				Held(DepartmentPhoneNumberStatuses.Provisioning, number: "+17755550155").CreatedOn = DateTime.UtcNow.AddSeconds(1);
			};

			(await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, Number, "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.AlreadyHasNumber);

			_carrier.Verify(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_repository.Rows.Should().ContainSingle().Which.PhoneNumber.Should().Be("+17755550155");
		}

		[Test]
		public async Task An_unanswered_purchase_stays_claimed_until_the_carrier_account_is_checked()
		{
			_carrier.Setup(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new CarrierNumberPurchase { Uncertain = true, Error = "The carrier did not confirm the purchase (TaskCanceledException)" });

			var row = await _service.ProvisionAsync(DepartmentId, Number, "admin");

			row.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Provisioning, "a Failed row would leave a possibly bought number unmanaged");
			Row(row.DepartmentPhoneNumberId).LastError.Should().Contain("did not confirm");
			_textToCall.Should().BeNull();
			(await FluentActions.Awaiting(() => _service.ProvisionAsync(DepartmentId, "+17755550166", "admin")).Should().ThrowAsync<DedicatedNumberException>())
				.Which.Error.Should().Be(DedicatedNumberErrors.AlreadyHasNumber, "no second purchase while the first may have gone through");

			// Before the provisioning timeout nothing is asked; after it the carrier account is.
			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(0);
			_carrier.Verify(c => c.FindDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var later = DateTime.UtcNow.AddHours(2);
			_carrier.Setup(c => c.FindDedicatedNumberAsync(Number, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			(await _service.ProcessLifecycleAsync(later)).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Provisioning, "unknown either way: asked again next sweep");

			_carrier.Setup(c => c.FindDedicatedNumberAsync(Number, It.IsAny<CancellationToken>())).ReturnsAsync(new CarrierNumberPurchase
				{ Success = true, Provider = "Twilio", ProviderNumberId = "PN0009", PhoneNumber = Number, SmsCapable = true, VoiceCapable = true });
			(await _service.ProcessLifecycleAsync(later)).Should().BeGreaterThan(0);

			var adopted = Row(row.DepartmentPhoneNumberId);
			adopted.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			adopted.ProviderNumberId.Should().Be("PN0009");
			adopted.LastError.Should().BeNull();
			_textToCall.Should().Be("17755550100");
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, Number, It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_unanswered_purchase_the_carrier_account_does_not_hold_fails_and_can_be_bought_again()
		{
			_carrier.Setup(c => c.PurchaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TaskCanceledException());

			var row = await _service.ProvisionAsync(DepartmentId, Number, "admin");
			row.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Provisioning, "a provider throw is no answer, not a refusal");

			_carrier.Setup(c => c.FindDedicatedNumberAsync(Number, It.IsAny<CancellationToken>())).ReturnsAsync((CarrierNumberPurchase)null);
			(await _service.ProcessLifecycleAsync(DateTime.UtcNow.AddHours(2))).Should().Be(1);

			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Failed);
			Row(row.DepartmentPhoneNumberId).LastError.Should().Contain("does not hold");
		}

		[Test]
		public async Task Routing_that_failed_after_a_purchase_is_retried_by_the_lifecycle_until_it_is_set_up()
		{
			// PR #588: a failed channel write after the purchase was logged and forgotten; the paying sweep never revisits an Active number.
			_channels.Setup(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException());

			var row = await _service.ProvisionAsync(DepartmentId, Number, "admin");

			row.Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active, "the number is bought");
			Row(row.DepartmentPhoneNumberId).LastError.Should().Be(DepartmentPhoneNumbersService.RoutingIncompleteError);

			// Still failing: the marker stays. Billing unknown does not stop the retry (routing an Active number needs no billing answer).
			_addon = null;
			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).LastError.Should().Be(DepartmentPhoneNumbersService.RoutingIncompleteError);

			_channels.Setup(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeChannel());
			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(1);
			Row(row.DepartmentPhoneNumberId).LastError.Should().BeNull();
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Sms, Number, It.IsAny<string>(), "admin", It.IsAny<CancellationToken>()), Times.Exactly(3),
				"the saved SMS switch is respected on every attempt");

			(await _service.ProcessLifecycleAsync(DateTime.UtcNow)).Should().Be(0);
			_channels.Verify(c => c.EnsureChannelAsync(DepartmentId, IntakeChannelTypes.Voice, Number, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3),
				"a complete setup is not repeated");
		}

		[Test]
		public async Task A_due_release_rechecks_the_stored_row_and_loses_to_a_reactivation()
		{
			// PR #588: worker 80 read the Lapsed row and an unpaid state, then paused; the admin renewed and reactivated meanwhile.
			var row = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
			row.AddonInactiveSince = row.ReleaseAfter.Value.AddDays(-30);
			_addon = false;
			_repository.BeforeConditionalWrite = id =>
			{
				_repository.BeforeConditionalWrite = null;
				var stored = _repository.Rows.Single(r => r.DepartmentPhoneNumberId == id);
				stored.Status = (int)DepartmentPhoneNumberStatuses.Active;
				stored.ReleaseAfter = null;
			};

			(await _service.ProcessLifecycleAsync(row.ReleaseAfter.Value.AddHours(1))).Should().Be(0);

			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
			Row(row.DepartmentPhoneNumberId).ReleasedOn.Should().BeNull();
		}

		[Test]
		public async Task A_due_release_is_skipped_when_payment_resumed_after_the_sweeps_billing_read()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
			row.AddonInactiveSince = row.ReleaseAfter.Value.AddDays(-30);
			var reads = 0;
			_access.Setup(a => a.GetPaymentStateAsync(DepartmentId)).ReturnsAsync(() => ++reads == 1
				? new DedicatedNumberPaymentState { AddonActive = false }
				: new DedicatedNumberPaymentState { AddonActive = true, PaidEntityPlan = true });

			(await _service.ProcessLifecycleAsync(row.ReleaseAfter.Value.AddHours(1))).Should().Be(0);

			_carrier.Verify(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed, "the next paying sweep restores it");
		}

		[Test]
		public async Task A_release_whose_carrier_answer_was_lost_is_never_reactivated_and_is_finished_by_the_next_sweep()
		{
			// PR #588: the carrier may already have the number back. Restoring the row would leave an Active number nobody holds.
			var row = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
			row.AddonInactiveSince = row.ReleaseAfter.Value.AddDays(-30);
			_addon = false;
			_carrier.Setup(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			(await _service.ProcessLifecycleAsync(row.ReleaseAfter.Value.AddHours(1))).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);
			Row(row.DepartmentPhoneNumberId).ReleasedOn.Should().NotBeNull("the release began");

			// Paying again: the admin cannot reactivate it, and neither does the paying sweep; it finishes the release instead.
			_addon = true;
			(await _service.ReactivateAsync(DepartmentId, "admin")).Should().BeNull();
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Lapsed);

			_carrier.Setup(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			(await _service.ProcessLifecycleAsync(row.ReleaseAfter.Value.AddHours(2))).Should().Be(1);
			Row(row.DepartmentPhoneNumberId).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Released);
			_channels.Verify(c => c.EnsureChannelAsync(It.IsAny<int>(), It.IsAny<IntakeChannelTypes>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_release_the_carrier_refused_can_still_be_reactivated()
		{
			var row = Held(DepartmentPhoneNumberStatuses.Lapsed, releaseAfter: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
			row.AddonInactiveSince = row.ReleaseAfter.Value.AddDays(-30);
			_addon = false;
			_carrier.Setup(c => c.ReleaseDedicatedNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await _service.ProcessLifecycleAsync(row.ReleaseAfter.Value.AddHours(1))).Should().Be(0);
			Row(row.DepartmentPhoneNumberId).ReleasedOn.Should().BeNull("the carrier answered and kept the number");

			_addon = true;
			(await _service.ReactivateAsync(DepartmentId, "admin")).Status.Should().Be((int)DepartmentPhoneNumberStatuses.Active);
		}

		[TestCase("+1 (775) 555-0100", "+17755550100")]
		[TestCase("17755550100", "+17755550100")]
		[TestCase("+44 20 7123 4567", "+442071234567")]
		[TestCase("555-0100", null)]
		[TestCase("", null)]
		[TestCase("+0123456789", null)]
		public void Numbers_are_normalized_to_e164(string input, string expected)
		{
			DepartmentPhoneNumbersService.NormalizeE164(input).Should().Be(expected);
		}

		/// <summary>In-memory DepartmentPhoneNumbers: the statuses the unique index and the queries filter on behave as the SQL does.</summary>
		private sealed class MemoryNumbers : IDepartmentPhoneNumbersRepository
		{
			public readonly List<DepartmentPhoneNumber> Rows = new List<DepartmentPhoneNumber>();

			private static DepartmentPhoneNumber Copy(DepartmentPhoneNumber row) =>
				Newtonsoft.Json.JsonConvert.DeserializeObject<DepartmentPhoneNumber>(Newtonsoft.Json.JsonConvert.SerializeObject(row));

			public Task<List<DepartmentPhoneNumber>> GetByNumberAsync(string phoneNumber, IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default) =>
				Task.FromResult(Rows.Where(r => string.Equals(r.PhoneNumber, phoneNumber, StringComparison.OrdinalIgnoreCase) && statuses.Contains(r.Status))
					.OrderByDescending(r => r.CreatedOn).Select(Copy).ToList());

			public Task<List<DepartmentPhoneNumber>> GetByStatusesAsync(IReadOnlyCollection<int> statuses, CancellationToken cancellationToken = default) =>
				Task.FromResult(Rows.Where(r => statuses.Contains(r.Status)).Select(Copy).ToList());

			public Task<IEnumerable<DepartmentPhoneNumber>> GetAllAsync() => Task.FromResult(Rows.Select(Copy));

			public Task<DepartmentPhoneNumber> GetByIdAsync(object id) => Task.FromResult(Rows.Where(r => r.DepartmentPhoneNumberId == (string)id).Select(Copy).FirstOrDefault());

			public Task<IEnumerable<DepartmentPhoneNumber>> GetAllByDepartmentIdAsync(int departmentId) =>
				Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId).Select(Copy).ToList().AsEnumerable());

			/// <summary>Runs just before an insert is checked: a concurrent request's write landing between a check and the insert.</summary>
			public Action<DepartmentPhoneNumber> BeforeInsert;

			/// <summary>Runs just before a conditional write: a concurrent writer landing between a read and the write.</summary>
			public Action<string> BeforeConditionalWrite;

			public Task<DepartmentPhoneNumber> InsertAsync(DepartmentPhoneNumber entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				BeforeInsert?.Invoke(entity);
				// UX_DepartmentPhoneNumbers_Number: one Provisioning / Active / Lapsed row per number.
				if (entity.IsHeld && Rows.Any(r => r.IsHeld && r.PhoneNumber == entity.PhoneNumber))
					throw new InvalidOperationException("Cannot insert duplicate key row in object 'DepartmentPhoneNumbers' with unique index 'UX_DepartmentPhoneNumbers_Number'.");
				// UX_DepartmentPhoneNumbers_DepartmentHeld: one Provisioning / Active / Lapsed row per department.
				if (entity.IsHeld && Rows.Any(r => r.IsHeld && r.DepartmentId == entity.DepartmentId))
					throw new InvalidOperationException("Cannot insert duplicate key row in object 'DepartmentPhoneNumbers' with unique index 'UX_DepartmentPhoneNumbers_DepartmentHeld'.");
				Rows.Add(Copy(entity));
				return Task.FromResult(entity);
			}

			public Task<bool> TryBeginReleaseAsync(string departmentPhoneNumberId, int expectedStatus, DateTime startedOn, CancellationToken cancellationToken = default)
			{
				BeforeConditionalWrite?.Invoke(departmentPhoneNumberId);
				var row = Rows.FirstOrDefault(r => r.DepartmentPhoneNumberId == departmentPhoneNumberId && r.Status == expectedStatus && !r.ReleasedOn.HasValue);
				if (row == null)
					return Task.FromResult(false);
				row.ReleasedOn = startedOn;
				row.UpdatedOn = startedOn;
				return Task.FromResult(true);
			}

			public Task<bool> TryReactivateAsync(string departmentPhoneNumberId, DateTime updatedOn, CancellationToken cancellationToken = default)
			{
				BeforeConditionalWrite?.Invoke(departmentPhoneNumberId);
				var row = Rows.FirstOrDefault(r => r.DepartmentPhoneNumberId == departmentPhoneNumberId && r.Status == (int)DepartmentPhoneNumberStatuses.Lapsed &&
					!r.ReleasedOn.HasValue);
				if (row == null)
					return Task.FromResult(false);
				row.Status = (int)DepartmentPhoneNumberStatuses.Active;
				row.AddonInactiveSince = null;
				row.ReleaseAfter = null;
				row.ReleaseWarningsSent = 0;
				row.LastReleaseWarningOn = null;
				row.UpdatedOn = updatedOn;
				return Task.FromResult(true);
			}

			public Task<DepartmentPhoneNumber> UpdateAsync(DepartmentPhoneNumber entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
			{
				var index = Rows.FindIndex(r => r.DepartmentPhoneNumberId == entity.DepartmentPhoneNumberId);
				if (index >= 0)
					Rows[index] = Copy(entity);
				return Task.FromResult(entity);
			}

			public Task<bool> DeleteAsync(DepartmentPhoneNumber entity, CancellationToken cancellationToken) =>
				Task.FromResult(Rows.RemoveAll(r => r.DepartmentPhoneNumberId == entity.DepartmentPhoneNumberId) > 0);

			public Task<DepartmentPhoneNumber> SaveOrUpdateAsync(DepartmentPhoneNumber entity, CancellationToken cancellationToken, bool firstLevelOnly = false) =>
				Rows.Any(r => r.DepartmentPhoneNumberId == entity.DepartmentPhoneNumberId) ? UpdateAsync(entity, cancellationToken) : InsertAsync(entity, cancellationToken);

			public Task<IEnumerable<DepartmentPhoneNumber>> GetAllByUserIdAsync(string userId) => Task.FromResult(Enumerable.Empty<DepartmentPhoneNumber>());

			public Task<bool> DeleteMultipleAsync(DepartmentPhoneNumber entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) =>
				Task.FromResult(false);
		}
	}
}
