using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class TextImportImpactTests
	{
		[Test]
		public void Shared_route_decisions_preserve_all_legacy_provider_branches()
		{
			foreach (var match in new[] { false, true })
			foreach (var calls in new[] { false, true })
			foreach (var commands in new[] { false, true })
			{
				var twilio = TextIntakeRouting.Decide(TextIntakePath.TwilioLegacy, match, calls, commands);
				Assert.That(twilio.CallBranch, Is.EqualTo(match)); Assert.That(twilio.CommandBranch, Is.EqualTo(!match));
				var signalWire = TextIntakeRouting.Decide(TextIntakePath.SignalWire, match, calls, commands);
				Assert.That(signalWire.CallBranch, Is.EqualTo((match || !commands) && calls));
				Assert.That(signalWire.CommandBranch, Is.EqualTo(!match && commands));
			}
		}
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Repository = new();
			public readonly Mock<IDepartmentSettingsRepository> Settings = new();
			public readonly Mock<INumbersService> Numbers = new();
			public TextImportImpactService Service => new(Access.Object, Repository.Object, new ConfigurationCatalog(), Settings.Object, Numbers.Object, TimeProvider.System);
			public Fixture()
			{
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentSetting>());
			}
		}
		[Test]
		public async Task Disabling_commands_can_route_an_unmatched_sender_to_calls_and_returns_only_a_masked_reference()
		{
			var f = new Fixture();
			f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.EnableTextCommand, Setting = "True" } });
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", "SignalWire", "+1 202 555 0142", true, false));
			var calls = report.Impact.Metrics.Single(m => m.LabelKey == "Impact.TextDispatchBranch");
			Assert.That(calls.Before, Is.Zero); Assert.That(calls.After, Is.EqualTo(1)); Assert.That(report.MaskedSource, Is.EqualTo("••••0142"));
			Assert.That(report.Impact.Metrics.Single(m => m.LabelKey == "Impact.TextActualAcceptance").State, Is.EqualTo(EvidenceState.Unknown));
			f.Numbers.VerifyNoOtherCalls(); f.Settings.Verify(s => s.GetAllByDepartmentIdAsync(7), Times.Exactly(2)); f.Settings.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Twilio_legacy_pattern_match_stays_on_call_branch_even_when_both_proposed_switches_are_off()
		{
			var f = new Fixture();
			f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.TextToCallSourceNumbers, Setting = "202555XXXX" } });
			f.Numbers.Setup(n => n.DoesNumberMatchAnyPattern(It.Is<List<string>>(p => p.SequenceEqual(new[] { "202555XXXX" })), "+12025550142")).Returns(true);
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", "TwilioLegacy", "+12025550142", false, false));
			var calls = report.Impact.Metrics.Single(m => m.LabelKey == "Impact.TextDispatchBranch");
			Assert.That(calls.Before, Is.EqualTo(1)); Assert.That(calls.After, Is.EqualTo(1));
			f.Numbers.Verify(n => n.DoesNumberMatchAnyPattern(It.IsAny<List<string>>(), It.IsAny<string>()), Times.Once); f.Numbers.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Unavailable_or_cross_tenant_settings_produce_unknown_not_an_acceptance_claim()
		{
			var f = new Fixture();
			f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 8, SettingType = 0 } });
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", "SignalWire", "2025550142", false, false));
			Assert.That(report.Impact.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown)); f.Numbers.VerifyNoOtherCalls();
		}
		[TestCase(null)][TestCase("invalid")]
		public async Task Malformed_stored_switch_is_unknown_not_an_absent_default(string value)
		{
			var f = new Fixture(); f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.EnableTextToCall, Setting = value } });
			var report = await f.Service.PreviewAsync(new(7, "admin"), new("0", "SignalWire", "2025550142", true, false));
			Assert.That(report.Impact.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public void Settings_drift_revision_change_or_revoked_access_invalidates_the_scenario()
		{
			var f = new Fixture(); var request = new TextImportImpactRequest("0", "SignalWire", "2025550142", false, false);
			f.Settings.SetupSequence(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentSetting>()).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.EnableTextCommand, Setting = "True" } });
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "admin"), request));
			f = new Fixture(); f.Repository.SetupSequence(r => r.GetConfigurationRevisionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(0).ReturnsAsync(1);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "admin"), request));
			f = new Fixture(); f.Access.SetupSequence(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "admin"), request));
		}
		[TestCase("Twilio", "2025550142")][TestCase("SignalWire", "secret@example.test")][TestCase("SignalWire", "-------")][TestCase("SignalWire", "123")]
		public void Invalid_scenarios_are_rejected_before_sources(string path, string source)
		{
			var f = new Fixture(); Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(new(7, "admin"), new("0", path, source, false, false)));
			f.Settings.VerifyNoOtherCalls(); f.Numbers.VerifyNoOtherCalls();
		}
	}
}
