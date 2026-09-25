using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class MappingImpactTests
	{
		private readonly DateTime _now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
		[Test]
		public void Shared_selector_matches_legacy_branching_including_boundary_and_fallback()
		{
			var dates = new DateTime?[] { null, _now.AddMinutes(-61), _now.AddMinutes(-60), _now.AddMinutes(-5), _now };
			foreach (var ping in dates)
			foreach (var status in dates)
			foreach (var hasLocation in new[] { true, false })
			foreach (var overwrite in new[] { true, false })
			foreach (var ttl in new[] { 0, 60 })
			{
				var expected = Legacy(ping, status, hasLocation, ttl, overwrite);
				Assert.That(MappingMarkerSelection.Select(ping, status, hasLocation, ttl, overwrite, _now), Is.EqualTo(expected));
			}
		}
		private MappingMarkerSource Legacy(DateTime? ping, DateTime? status, bool hasLocation, int ttl, bool overwrite)
		{
			if (ttl > 0 && ping.HasValue && _now.AddMinutes(-ttl) > ping) ping = null;
			if (ping.HasValue && status.HasValue)
			{
				if (ping > status) return MappingMarkerSource.LocationPing;
				if (hasLocation) return MappingMarkerSource.Status;
				if (!overwrite) return MappingMarkerSource.LocationPing;
			}
			else if (ping.HasValue) return MappingMarkerSource.LocationPing;
			else if (status.HasValue && hasLocation) return MappingMarkerSource.Status;
			return MappingMarkerSource.None;
		}
		[Test]
		public void Newer_ping_does_not_parse_malformed_older_status()
		{
			Assert.That(MappingMarkerSelection.Select(_now, _now.AddMinutes(-1), () => throw new FormatException(), 60, true, _now), Is.EqualTo(MappingMarkerSource.LocationPing));
		}
		[TestCase(false, false, EvidenceState.Known)]
		[TestCase(true, false, EvidenceState.Redacted)]
		[TestCase(false, true, EvidenceState.Unknown)]
		public async Task Unit_preview_counts_fallbacks_and_never_converts_missing_or_restricted_data_to_zero(bool restricted, bool outage, EvidenceState expected)
		{
			var units = new Mock<IUnitsService>(); var rows = new Mock<IUnitsRepository>(); var states = new Mock<IUnitStatesRepository>(); var authorization = new Mock<IAdminAssistPermissionEvaluator>();
			rows.Setup(r => r.GetAllUnitsByDepartmentIdAsync(7)).ReturnsAsync(new List<Unit> { new() { UnitId = 1, DepartmentId = 7 }, new() { UnitId = 2, DepartmentId = 7 } });
			units.Setup(u => u.ReadLatestLocationsForAdministrationAsync(7)).ReturnsAsync(new List<UnitsLocation> {
				new() { UnitId = 1, DepartmentId = 7, Timestamp = _now.AddMinutes(-90) }, new() { UnitId = 2, DepartmentId = 7, Timestamp = _now.AddMinutes(-90) }
			});
			if (outage) units.Setup(u => u.ReadLatestLocationsForAdministrationAsync(7)).ThrowsAsync(new InvalidOperationException("source failed"));
			states.Setup(s => s.GetLatestUnitStatesForDepartmentAsync(7)).ReturnsAsync(new List<UnitState> {
				new() { UnitId = 1, Timestamp = _now.AddHours(-2), Latitude = 40, Longitude = -120 }, new() { UnitId = 2, Timestamp = _now.AddHours(-2) }
			});
			authorization.Setup(a => a.EvaluateCurrentTargetsAsync(It.IsAny<AdminAssistActor>(), nameof(PermissionTypes.CanSeeUnitLocations), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, bool> { ["1"] = !restricted, ["2"] = !restricted });
			var service = new MappingImpactProvider(Mock.Of<IUsersService>(), units.Object, rows.Object, states.Object, Mock.Of<IActionLogsRepository>(), authorization.Object, Mock.Of<IRecordsAuthorizationService>());
			var facts = new Dictionary<string, ConfigurationEvidence> {
				["MappingUnitLocationTTL"] = new("MappingUnitLocationTTL", EvidenceState.Known, "test", "1", _now, Number: 0),
				["MappingUnitAllowStatusWithNoLocationToOverwrite"] = new("MappingUnitAllowStatusWithNoLocationToOverwrite", EvidenceState.Known, "test", "1", _now, Boolean: false)
			};
			var report = await service.EvaluateAsync(new(7, "admin"), new(7, "admin", "1", _now, true, facts), new("setting.MappingUnitLocationTTL", "1", Number: 60), CancellationToken.None);
			var metric = report.Metrics.First(); Assert.That(metric.State, Is.EqualTo(expected));
			if (expected == EvidenceState.Known)
			{
				Assert.That(metric.Before, Is.EqualTo(2)); Assert.That(metric.After, Is.EqualTo(1));
				Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.MarkersRemoved").After, Is.EqualTo(1));
				Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.StatusFallbackMarkers").After, Is.EqualTo(1));
			}
			else { Assert.That(metric.Before, Is.Null); Assert.That(metric.After, Is.Null); }
			var proposed = new ConfigurationSnapshot(7, "admin", "1", _now, true, facts.ToDictionary(p => p.Key, p => p.Key == "MappingUnitLocationTTL" ? p.Value with { Number = 60 } : p.Value));
			var composed = await service.EvaluateComposedAsync(new(7, "admin"), new(7, "admin", "1", _now, true, facts), proposed, new[] { "setting.MappingUnitLocationTTL" }, CancellationToken.None);
			Assert.That(composed.Metrics.Single().Before, Is.EqualTo(expected == EvidenceState.Known ? 2m : (decimal?)null));
			Assert.That(composed.Metrics.Single().After, Is.EqualTo(expected == EvidenceState.Known ? 1m : (decimal?)null));
		}
	}
}
