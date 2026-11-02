using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Services;

namespace Resgrid.Tests.Services.Routing
{
	/// <summary>Live ETAs of navigating units (maplibre-mapping-routing-navigation-plan.md §9.4).</summary>
	[TestFixture]
	public class UnitNavigationStateServiceTests
	{
		private Dictionary<string, string> _cache;
		private Mock<IEventAggregator> _events;
		private List<UnitNavigationUpdatedEvent> _sent;
		private UnitNavigationStateService _service;

		[SetUp]
		public void SetUp()
		{
			_cache = new Dictionary<string, string>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync((string key, string value, TimeSpan _) => { _cache[key] = value; return true; });
			cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string key) => _cache.TryGetValue(key, out var v) ? v : null);
			cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ReturnsAsync((string key) => _cache.Remove(key));

			_sent = new List<UnitNavigationUpdatedEvent>();
			_events = new Mock<IEventAggregator>();
			_events.Setup(e => e.SendMessageAsync(It.IsAny<UnitNavigationUpdatedEvent>()))
				.Callback((UnitNavigationUpdatedEvent e) => _sent.Add(e))
				.Returns(Task.CompletedTask);

			_service = new UnitNavigationStateService(cache.Object, _events.Object);
		}

		private static UnitNavigationState Report(int unitId, int? callId, UnitNavigationStates state = UnitNavigationStates.Progress) => new UnitNavigationState
		{
			DepartmentId = 7,
			UnitId = unitId,
			CallId = callId,
			RouteInstanceId = callId.HasValue ? null : "inst",
			State = (int)state,
			EtaUtc = DateTime.UtcNow.AddMinutes(4),
			RemainingSeconds = 240,
			RemainingMeters = 3100
		};

		[Test]
		public async Task a_report_is_stored_listed_and_announced_without_the_eta()
		{
			await _service.ReportAsync(Report(30, 900));

			(await _service.GetForUnitAsync(7, 30)).RemainingSeconds.Should().Be(240);
			(await _service.GetActiveAsync(7)).Should().ContainSingle(s => s.UnitId == 30 && s.CallId == 900);
			_sent.Should().ContainSingle();
			_sent[0].Payload.Should().Contain("\"callId\":900").And.NotContain("240").And.NotContain("Eta");
		}

		[Test]
		public async Task arrival_clears_the_state()
		{
			await _service.ReportAsync(Report(30, 900));
			await _service.ReportAsync(Report(30, 900, UnitNavigationStates.Arrived));

			(await _service.GetForUnitAsync(7, 30)).Should().BeNull();
			(await _service.GetActiveAsync(7)).Should().BeEmpty();
			_sent.Should().HaveCount(2);
		}

		[Test]
		public async Task a_response_ending_status_clears_a_call_navigation_only()
		{
			await _service.ReportAsync(Report(30, 900));
			await _service.ReportAsync(Report(31, null));

			await _service.ClearForUnitStatusAsync(7, 30, (int)ActionBaseTypes.Responding);
			(await _service.GetForUnitAsync(7, 30)).Should().NotBeNull();

			await _service.ClearForUnitStatusAsync(7, 30, (int)ActionBaseTypes.OnScene);
			(await _service.GetForUnitAsync(7, 30)).Should().BeNull();

			// Route navigation is not a response.
			await _service.ClearForUnitStatusAsync(7, 31, (int)ActionBaseTypes.Available);
			(await _service.GetForUnitAsync(7, 31)).Should().NotBeNull();
		}

		[Test]
		public async Task another_departments_state_is_not_read()
		{
			await _service.ReportAsync(Report(30, 900));

			(await _service.GetForUnitAsync(8, 30)).Should().BeNull();
			(await _service.GetActiveAsync(8)).Should().BeEmpty();
		}

		[Test]
		public void the_eventing_type_value_is_pinned()
		{
			((int)EventingTypes.UnitNavigationUpdated).Should().Be(16);
		}

		[Test]
		public void navigation_config_defaults_survive_protobuf()
		{
			var config = new MappingNavigationConfig();
			var restored = ObjectSerialization.Deserialize<MappingNavigationConfig>(ObjectSerialization.Serialize(config));

			restored.VoiceGuidanceOff.Should().BeFalse();
			restored.EffectiveArrivalRadiusMeters().Should().Be(MappingNavigationConfig.DefaultArrivalRadiusMeters);
			restored.EffectiveDeviationThreshold().Should().Be(TimeSpan.FromMinutes(15));

			var custom = ObjectSerialization.Deserialize<MappingNavigationConfig>(ObjectSerialization.Serialize(
				new MappingNavigationConfig { VoiceGuidanceOff = true, ArrivalRadiusMeters = 80, DeviationThresholdMinutes = 5, DefaultAvoidOptions = 3 }));
			custom.VoiceGuidanceOff.Should().BeTrue();
			custom.EffectiveArrivalRadiusMeters().Should().Be(80);
			custom.EffectiveDeviationThreshold().Should().Be(TimeSpan.FromMinutes(5));
			custom.DefaultAvoidOptions.Should().Be(3);
		}
	}
}
