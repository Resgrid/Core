using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordsLegalHoldServiceTests
	{
		private FakeIncidentStore _store;
		private Mock<IRecordsAuthorizationService> _auth;
		private RecordsLegalHoldService _service;
		[SetUp]
		public void Setup()
		{
			_store = new FakeIncidentStore(); _auth = new Mock<IRecordsAuthorizationService>();
			_auth.Setup(a => a.HasPermissionAsync("officer", 11, PermissionTypes.ManageRecordLegalHold)).ReturnsAsync(true);
			_auth.Setup(a => a.CanUserViewRecordAsync("officer", "record", 11)).ReturnsAsync(true);
			_store.Shared.Records.Add(new RmsOperationalRecord { DepartmentId = 11, RmsOperationalRecordId = "record" });
			_service = new RecordsLegalHoldService(_store.Shared.LegalHoldsRepo.Object, _store.Shared.RecordsRepo.Object, _store.ReportsRepo.Object, _auth.Object, _store.Shared.AuditsRepo.Object, _store.Shared.UnitOfWork.Object);
		}
		private RmsRecordLegalHold Input() => new RmsRecordLegalHold { RecordId = "record", Reason = "Litigation", ReferenceNumber = "Case-7", Notes = "Preserve source record, analyses and all evidence" };
		[Test]
		public async Task Hold_placement_ignores_forged_actor_state_and_release_is_once_with_recorded_authority()
		{
			var input = Input(); input.DepartmentId = 99; input.ReleasedOn = DateTime.UtcNow; input.PlacedByUserId = "forged";
			var hold = await _service.PlaceAsync(11, "officer", input); hold.DepartmentId.Should().Be(11); hold.PlacedByUserId.Should().Be("officer"); hold.IsActive.Should().BeTrue();
			Func<Task> noBasis = () => _service.ReleaseAsync(11, "officer", hold.RmsRecordLegalHoldId, 1, ""); await noBasis.Should().ThrowAsync<ArgumentException>();
			await _service.ReleaseAsync(11, "officer", hold.RmsRecordLegalHoldId, 1, "Court order dated 2026-09-04");
			Func<Task> repeated = () => _service.ReleaseAsync(11, "officer", hold.RmsRecordLegalHoldId, 1, "overwrite"); await repeated.Should().ThrowAsync<InvalidOperationException>();
			hold.ReleaseNotes.Should().Be("Court order dated 2026-09-04"); _store.Shared.Audits.Should().HaveCount(2);
		}
		[Test]
		public async Task Revoked_hold_authority_and_foreign_or_purged_records_cannot_be_held()
		{
			var foreign = Input(); foreign.RecordId = "foreign"; Func<Task> place = () => _service.PlaceAsync(11, "officer", foreign); await place.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Shared.Records[0].PurgedOn = DateTime.UtcNow; Func<Task> purged = () => _service.PlaceAsync(11, "officer", Input()); await purged.Should().ThrowAsync<ArgumentException>();
			_auth.Setup(a => a.HasPermissionAsync("officer", 11, PermissionTypes.ManageRecordLegalHold)).ReturnsAsync(false);
			Func<Task> list = () => _service.GetAsync(11, "officer"); await list.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Shared.LegalHolds.Should().BeEmpty();
		}
		[Test]
		public async Task Department_period_hold_rejects_ambiguous_scopes_and_is_not_automatically_released()
		{
			var input = Input(); input.PeriodStart = DateTime.UtcNow; Func<Task> mixed = () => _service.PlaceAsync(11, "officer", input); await mixed.Should().ThrowAsync<ArgumentException>();
			input.RecordId = null; input.PeriodEnd = input.PeriodStart.Value.AddDays(-1); await mixed.Should().ThrowAsync<ArgumentException>();
			input.PeriodEnd = null; var hold = await _service.PlaceAsync(11, "officer", input); hold.Covers("other", "system.run", DateTime.UtcNow.AddDays(2)).Should().BeTrue();
			(await _service.GetAsync(11, "officer")).Should().ContainSingle(h => h.IsActive);
		}
	}
}
