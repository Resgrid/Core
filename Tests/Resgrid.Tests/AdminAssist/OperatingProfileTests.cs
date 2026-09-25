using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class OperatingProfileTests
	{
		private Mock<IDepartmentSettingsRepository> _settings;
		private Mock<IAdminAssistRepository> _metadata;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IUnitOfWork> _unit;
		private Mock<IFeatureToggleService> _flags;
		private DepartmentSettingsService _service;
		private DepartmentSetting _saved;
		[SetUp]
		public void SetUp()
		{
			_saved = null; _settings = new(); _metadata = new(); _authorization = new(); _unit = new();
			_flags = new(); _flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminSetup, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			_settings.Setup(s => s.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.DepartmentOperatingProfile)).ReturnsAsync(() => _saved);
			_settings.Setup(s => s.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentSetting value, CancellationToken _, bool firstLevel) => _saved = value);
			_metadata.Setup(r => r.ValidateOperatingProfileReferencesAsync(7, It.IsAny<DepartmentOperatingProfile>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_authorization.Setup(a => a.IsActiveMemberAsync("admin", 7)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsDepartmentAdminAsync("admin", 7)).ReturnsAsync(true);
			_service = new DepartmentSettingsService(_settings.Object, Mock.Of<IAddressService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<ICacheProvider>(),
				moduleUnit: _unit.Object, moduleFlags: new Lazy<IFeatureToggleService>(() => _flags.Object), operatingProfileRepository: _metadata.Object, operatingProfileAuthorization: new Lazy<IRecordsAuthorizationService>(() => _authorization.Object));
		}
		[Test]
		public async Task New_profile_advances_revision_without_mutating_proposal()
		{
			var proposal = new DepartmentOperatingProfile { Archetypes = new() { "ems", "mental-health" } };
			await _service.SetOperatingProfileAsync(7, proposal, "admin");
			var persisted = ObjectSerialization.Deserialize<DepartmentOperatingProfile>(_saved.Setting);
			Assert.That(persisted.Revision, Is.EqualTo(1)); Assert.That(persisted.ReviewedOnUtc, Is.Not.Null);
			Assert.That(persisted.Archetypes, Is.EquivalentTo(proposal.Archetypes));
			Assert.That(proposal.Revision, Is.Zero); Assert.That(proposal.ReviewedOnUtc, Is.Null);
			_unit.Verify(u => u.CommitChanges(), Times.Once);
		}
		[Test]
		public async Task Stale_profile_cannot_overwrite_another_administrators_save()
		{
			await _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile(), "admin");
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile { WorkforceMix = "volunteer" }, "admin"));
			Assert.That(ObjectSerialization.Deserialize<DepartmentOperatingProfile>(_saved.Setting).WorkforceMix, Is.EqualTo("unknown"));
			_settings.Verify(s => s.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_unit.Verify(u => u.DiscardChanges(), Times.Once);
		}
		[Test]
		public void Invalid_references_cannot_be_saved()
		{
			_metadata.Setup(r => r.ValidateOperatingProfileReferencesAsync(7, It.IsAny<DepartmentOperatingProfile>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.ThrowsAsync<ValidationException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile { SiteGroupReferences = new() { "123" } }, "admin"));
			Assert.That(_saved, Is.Null); _unit.Verify(u => u.DiscardChanges(), Times.Once);
		}
		[Test]
		public void Revocation_during_validation_prevents_save()
		{
			_authorization.SetupSequence(a => a.IsDepartmentAdminAsync("admin", 7)).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile(), "admin"));
			Assert.That(_saved, Is.Null); _unit.Verify(u => u.DiscardChanges(), Times.Once);
		}
		[Test]
		public void Disabled_rollout_prevents_profile_writes_before_opening_a_transaction()
		{
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminSetup, 7)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile(), "admin"));
			Assert.That(_saved, Is.Null); _unit.VerifyNoOtherCalls(); _metadata.VerifyNoOtherCalls();
		}
		[Test]
		public void Rollout_revocation_during_validation_prevents_profile_save()
		{
			_flags.SetupSequence(f => f.EvaluateFreshAsync(FeatureFlagKeys.AdminSetup, 7))
				.ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true }).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile(), "admin"));
			Assert.That(_saved, Is.Null); _unit.Verify(u => u.DiscardChanges(), Times.Once);
		}
		[Test]
		public void Inactive_member_never_opens_transaction()
		{
			_authorization.Setup(a => a.IsActiveMemberAsync("admin", 7)).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SetOperatingProfileAsync(7, new DepartmentOperatingProfile(), "admin"));
			_unit.Verify(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
