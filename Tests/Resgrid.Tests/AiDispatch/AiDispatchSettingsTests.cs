using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Services;
using Resgrid.Services.AiDispatch;

namespace Resgrid.Tests.AiDispatch
{
	/// <summary>Per-department AI dispatch settings: bounds, sender allowlist and the admin service's save and status rules.</summary>
	[TestFixture, NonParallelizable]
	public class AiDispatchSettingsTests
	{
		[TestCase(0.49, false)]
		[TestCase(0.5, true)]
		[TestCase(0.95, true)]
		[TestCase(0.96, false)]
		public void Confidence_floor_is_bounded(double confidence, bool valid)
		{
			var errors = AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig { MinimumConfidence = (decimal)confidence });
			errors.Contains("ConfidenceOutOfRange").Should().Be(!valid);
		}

		[Test]
		public void Retention_cap_and_allowlist_are_validated()
		{
			AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig { AuditRetentionDays = 29 }).Should().Contain("RetentionOutOfRange");
			AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig { MonthlyTokenCap = 100 }).Should().Contain("CapOutOfRange");
			AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig { SenderAllowlist = "cad.county.gov\nnot an address" }).Should().Contain("AllowlistInvalid");
			AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig { SenderAllowlist = string.Join("\n", Enumerable.Range(1, 51).Select(i => $"relay{i}.example.org")) })
				.Should().Contain("AllowlistTooLong");
			AiDispatchSettingsPolicy.Validate(new DepartmentAiDispatchConfig()).Should().BeEmpty("the defaults are valid");
		}

		[Test]
		public void Allowlist_is_normalized_to_one_lower_case_entry_per_line()
		{
			AiDispatchSettingsPolicy.NormalizeAllowlist(" CAD.County.gov ; @Relay.Example.org\nDispatch@CAD.County.gov, cad.county.gov ")
				.Should().Be("cad.county.gov\nrelay.example.org\ndispatch@cad.county.gov");
			AiDispatchSettingsPolicy.NormalizeAllowlist("  ").Should().BeNull();
		}

		[TestCase("", "anyone@example.com", true)]
		[TestCase("cad.county.gov", "Relay@CAD.county.gov", true)]
		[TestCase("cad.county.gov", "relay@evil-cad.county.gov", false)]
		[TestCase("cad.county.gov", "relay@county.gov", false)]
		[TestCase("dispatch@cad.county.gov", "dispatch@cad.county.gov", true)]
		[TestCase("dispatch@cad.county.gov", "other@cad.county.gov", false)]
		[TestCase("cad.county.gov", null, false)]
		public void Senders_match_an_exact_address_or_domain(string allowlist, string sender, bool allowed)
		{
			AiDispatchSettingsPolicy.IsSenderAllowed(allowlist, sender).Should().Be(allowed);
		}

		[Test]
		public void Effective_confidence_uses_the_department_value_within_bounds()
		{
			AiDispatchSettingsPolicy.EffectiveMinimumConfidence(null, 0.6).Should().Be(0.6);
			AiDispatchSettingsPolicy.EffectiveMinimumConfidence(new DepartmentAiDispatchConfig { MinimumConfidence = 0.8m }, 0.6).Should().Be(0.8);
			AiDispatchSettingsPolicy.EffectiveMinimumConfidence(null, 0.1).Should().Be(0.5, "a host misconfiguration cannot drop below the floor");
		}

		private sealed class FixedClock : TimeProvider
		{
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
		}

		private Mock<IAiDispatchConfigRepository> _repository;
		private AiDispatchAdminService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IAiDispatchConfigRepository>();
			_service = new AiDispatchAdminService(_repository.Object, Mock.Of<IAiDispatchAuditRepository>(), Mock.Of<IAiBackgroundAdmission>(),
				Mock.Of<IEnhancedAiAccessService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IDepartmentsService>(), new FixedClock());
		}

		[Test]
		public async Task Saving_forces_the_signed_in_department_and_normalizes_the_allowlist()
		{
			DepartmentAiDispatchConfig written = null;
			_repository.Setup(r => r.SaveAsync(It.IsAny<DepartmentAiDispatchConfig>(), 3, It.IsAny<CancellationToken>()))
				.Callback((DepartmentAiDispatchConfig c, long _, CancellationToken __) => written = c).ReturnsAsync(true);

			var errors = await _service.SaveSettingsAsync(7, new DepartmentAiDispatchConfig { DepartmentId = 999, SenderAllowlist = "CAD.County.gov" }, 3, "admin-1", CancellationToken.None);

			errors.Should().BeEmpty();
			written.DepartmentId.Should().Be(7, "the department never comes from the form");
			written.SenderAllowlist.Should().Be("cad.county.gov");
			written.UpdatedByUserId.Should().Be("admin-1");
			written.UpdatedOnUtc.Should().Be(new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc));
			written.Revision.Should().Be(4);
		}

		[Test]
		public async Task Invalid_settings_are_not_written_and_a_concurrent_edit_is_a_conflict()
		{
			(await _service.SaveSettingsAsync(7, new DepartmentAiDispatchConfig { AuditRetentionDays = 5 }, 0, "admin-1", CancellationToken.None))
				.Should().Contain("RetentionOutOfRange");
			_repository.Verify(r => r.SaveAsync(It.IsAny<DepartmentAiDispatchConfig>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);

			_repository.Setup(r => r.SaveAsync(It.IsAny<DepartmentAiDispatchConfig>(), 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			(await _service.SaveSettingsAsync(7, new DepartmentAiDispatchConfig(), 2, "admin-1", CancellationToken.None)).Should().Equal("Conflict");
		}

		[Test]
		public async Task The_sender_check_fails_closed_when_settings_cannot_be_read()
		{
			_repository.Setup(r => r.GetAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((DepartmentAiDispatchConfig)null);
			(await _service.IsSenderAllowedAsync(7, "anyone@example.com")).Should().BeTrue("no settings row means no allowlist");

			_repository.Setup(r => r.GetAsync(7, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database down"));
			(await _service.IsSenderAllowedAsync(7, "anyone@example.com")).Should().BeFalse();
		}

		[Test]
		public async Task Status_names_each_missing_prerequisite()
		{
			var saved = (AiDispatchConfig.EnrichEnabled, AiConfig.SelfHostedDepartmentIds);
			try
			{
				AiDispatchConfig.EnrichEnabled = true;
				AiConfig.SelfHostedDepartmentIds = "";
				var access = new Mock<IEnhancedAiAccessService>();
				access.Setup(a => a.IsEnabledAsync(7)).ReturnsAsync(true);
				access.Setup(a => a.GetActiveAddonStateAsync(7)).ReturnsAsync(false);
				var flags = new Mock<IFeatureToggleService>();
				flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AiDispatchTemplate, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentEmailSettingsAsync(7)).ReturnsAsync(new DepartmentCallEmail { FormatType = (int)CallEmailTypes.AI });
				var service = new AiDispatchAdminService(_repository.Object, Mock.Of<IAiDispatchAuditRepository>(), Mock.Of<IAiBackgroundAdmission>(),
					access.Object, flags.Object, departments.Object, new FixedClock());

				var status = await service.GetStatusAsync(7);

				status.HostEnabled.Should().BeTrue();
				status.RolledOut.Should().BeTrue();
				status.Entitled.Should().BeFalse();
				status.FormatSelected.Should().BeTrue();
				status.Running.Should().BeFalse();
			}
			finally { (AiDispatchConfig.EnrichEnabled, AiConfig.SelfHostedDepartmentIds) = saved; }
		}
	}
}
