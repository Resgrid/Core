using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Plan D8: with destination suggestions set to Suggest and fill, a new call nobody entered in a form takes the destination
	/// the department's rules suggest, and only then: never over any destination the call has, never without a point, never
	/// when no rule matched the call, and never by failing or holding up the save.
	/// </summary>
	[TestFixture]
	public class DestinationAutoFillServiceTests
	{
		private const int DepartmentId = 12;
		private const int HospitalPoiId = 7;

		private Mock<IDepartmentSettingsService> _settings;
		private Mock<INearestDestinationService> _nearest;
		private DestinationAutoFillService _service;

		[SetUp]
		public void SetUp()
		{
			_settings = new Mock<IDepartmentSettingsService>();
			_nearest = new Mock<INearestDestinationService>();
			SetMode(DestinationSuggestionModes.SuggestAndFill);
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Suggestions());

			_service = new DestinationAutoFillService(_settings.Object, _nearest.Object);
		}

		private void SetMode(DestinationSuggestionModes mode)
		{
			_settings.Setup(x => x.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DestinationSuggestionConfig { Mode = (int)mode });
		}

		private static DestinationSuggestions Suggestions(bool ruleMatched = true, int? suggestedPoiId = HospitalPoiId,
			DestinationSuggestionModes mode = DestinationSuggestionModes.SuggestAndFill)
		{
			var requirements = new DestinationRequirements { PoiTypeIds = new List<int> { 3 } };
			if (ruleMatched)
				requirements.AppliedRules.Add(new DestinationRule { DestinationRuleId = "r1", Name = "Burns", PoiTypeIds = "3" });

			return new DestinationSuggestions
			{
				Mode = mode,
				Requirements = requirements,
				SuggestedPoiId = suggestedPoiId,
				Destinations = new List<NearestDestination>
				{
					new NearestDestination { Poi = new Poi { PoiId = HospitalPoiId, Name = "St. Mary Burn Center", Type = new PoiType { Name = "Hospital" } }, Tier = DestinationTiers.Ready }
				}
			};
		}

		private static Call EmailCall() => new Call
		{
			CallId = 0,
			DepartmentId = DepartmentId,
			CallSource = (int)CallSources.EmailImport,
			State = (int)CallStates.Active,
			GeoLocationData = "50.8503,4.3517",
			Type = "Burns",
			CallTypeId = 31,
			Priority = 2
		};

		private void VerifyNoLookup() =>
			_nearest.Verify(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

		[Test]
		public async Task An_unattended_new_call_takes_the_suggested_destination_from_its_location_type_and_priority()
		{
			var choice = await _service.ChooseDestinationAsync(EmailCall());

			choice.Should().NotBeNull();
			choice.PoiId.Should().Be(HospitalPoiId);
			choice.PoiName.Should().Be("St. Mary Burn Center");
			choice.RuleNames.Should().Equal("Burns");
			_nearest.Verify(x => x.SuggestDestinationsAsync(It.Is<DestinationSuggestionRequest>(r =>
				r.DepartmentId == DepartmentId && r.Latitude == 50.8503 && r.Longitude == 4.3517 && r.CallTypeId == 31 && r.CallType == "Burns" &&
				r.CallPriority == 2 && r.UnitId == null), It.IsAny<CancellationToken>()), Times.Once);

			(await _service.ChooseAsync(EmailCall())).Should().Be(HospitalPoiId);
		}

		[TestCase(CallSources.EmailImport)]
		[TestCase(CallSources.AudioImport)]
		[TestCase(CallSources.Chatbot)]
		[TestCase(CallSources.Intake)]
		[TestCase(CallSources.VoiceIntake)]
		public async Task Every_unattended_source_is_filled(CallSources source)
		{
			var call = EmailCall();
			call.CallSource = (int)source;

			(await _service.ChooseAsync(call)).Should().Be(HospitalPoiId);
		}

		[Test]
		public async Task A_pending_call_is_filled()
		{
			var call = EmailCall();
			call.State = (int)CallStates.Pending;

			(await _service.ChooseAsync(call)).Should().Be(HospitalPoiId);
		}

		[Test]
		public async Task A_recurring_schedules_call_is_filled_although_it_is_saved_as_a_user_call()
		{
			var call = EmailCall();
			call.CallSource = (int)CallSources.User;
			call.RecurringCallId = 5;

			(await _service.ChooseAsync(call)).Should().Be(HospitalPoiId);
		}

		[TestCase(DestinationSuggestionModes.Off)]
		[TestCase(DestinationSuggestionModes.Suggest)]
		public async Task Nothing_is_filled_or_routed_unless_the_department_chose_suggest_and_fill(DestinationSuggestionModes mode)
		{
			SetMode(mode);

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
			VerifyNoLookup();
		}

		[Test]
		public async Task A_missing_setting_is_off()
		{
			_settings.Setup(x => x.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync((DestinationSuggestionConfig)null);

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
			VerifyNoLookup();
		}

		[Test]
		public async Task An_existing_call_is_never_filled()
		{
			var call = EmailCall();
			call.CallId = 99;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[TestCase(CallSources.User)]
		[TestCase(CallSources.Imported)]
		[TestCase((CallSources)0)]
		public async Task A_call_a_person_entered_in_a_form_or_an_imported_call_is_never_filled(CallSources source)
		{
			var call = EmailCall();
			call.CallSource = (int)source;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[TestCase(CallStates.Closed)]
		[TestCase(CallStates.Cancelled)]
		public async Task A_call_saved_closed_is_never_filled(CallStates state)
		{
			var call = EmailCall();
			call.State = (int)state;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[Test]
		public async Task A_call_with_a_destination_poi_keeps_it()
		{
			var call = EmailCall();
			call.DestinationPoiId = 44;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[TestCase("Mercy Hospital, Main St", null, null)]
		[TestCase(null, "50.9,4.4", null)]
		[TestCase(null, null, "index.home.raft")]
		public async Task A_call_with_a_free_destination_keeps_it(string address, string point, string w3w)
		{
			var call = EmailCall();
			call.DestinationAddress = address;
			call.DestinationGeoLocationData = point;
			call.DestinationW3W = w3w;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[Test]
		public async Task A_call_following_a_route_keeps_the_routes_end()
		{
			var call = EmailCall();
			call.RoutePlanId = "route-1";

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase(",")]
		[TestCase("0,0")]
		[TestCase("not a place")]
		[TestCase("RGENC1:abcdef==")]
		[TestCase("95.1,4.3")]
		public async Task A_call_without_a_usable_point_is_not_filled(string geoLocationData)
		{
			var call = EmailCall();
			call.GeoLocationData = geoLocationData;

			(await _service.ChooseAsync(call)).Should().BeNull();
			VerifyNoLookup();
		}

		[Test]
		public async Task Nothing_is_filled_when_no_destination_rule_matched_the_call()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Suggestions(ruleMatched: false));

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
		}

		[Test]
		public async Task A_rule_that_matched_but_was_set_aside_does_not_count()
		{
			var suggestions = Suggestions(ruleMatched: false);
			suggestions.Requirements.SetAsideRules.Add(new SetAsideDestinationRule
			{
				Rule = new DestinationRule { DestinationRuleId = "r2", Name = "Old hospitals" },
				Reason = DestinationRuleSetAsideReasons.TypesRemoved
			});
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(suggestions);

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
		}

		[Test]
		public async Task Nothing_is_filled_when_no_destination_meets_the_requirements()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Suggestions(suggestedPoiId: null));

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
		}

		[Test]
		public async Task The_setting_turned_off_while_looking_fills_nothing()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(Suggestions(mode: DestinationSuggestionModes.Suggest));

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
		}

		[Test]
		public async Task A_failed_lookup_fills_nothing_and_never_throws()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("routing down"));

			Func<Task> act = async () => (await _service.ChooseAsync(EmailCall())).Should().BeNull();
			await act.Should().NotThrowAsync();
		}

		[Test]
		public async Task A_failed_setting_read_fills_nothing_and_never_throws()
		{
			_settings.Setup(x => x.GetDestinationSuggestionConfigAsync(DepartmentId, It.IsAny<bool>())).ThrowsAsync(new TimeoutException());

			(await _service.ChooseAsync(EmailCall())).Should().BeNull();
		}

		[Test]
		public async Task A_lookup_cut_short_by_cancellation_fills_nothing()
		{
			_nearest.Setup(x => x.SuggestDestinationsAsync(It.IsAny<DestinationSuggestionRequest>(), It.IsAny<CancellationToken>()))
				.Returns(async (DestinationSuggestionRequest _, CancellationToken token) =>
				{
					await Task.Delay(Timeout.Infinite, token);
					return Suggestions();
				});

			using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
			{
				(await _service.ChooseAsync(EmailCall(), cancelled.Token)).Should().BeNull();
			}
		}

		[Test]
		public void The_lookup_has_a_hard_time_budget()
		{
			DestinationAutoFillService.TimeBudget.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
		}

		[Test]
		public async Task Null_is_never_filled()
		{
			(await _service.ChooseAsync(null)).Should().BeNull();
		}

		[TestCase("50.8503,4.3517", 50.8503, 4.3517)]
		[TestCase(" 50.8503 , 4.3517 ", 50.8503, 4.3517)]
		[TestCase("50,8503,4,3517", 50.8503, 4.3517)]
		[TestCase("50,8503,-4,3517", 50.8503, -4.3517)]
		[TestCase("50.8503;4.3517", 50.8503, 4.3517)]
		[TestCase("50,8503;4,3517", 50.8503, 4.3517)]
		[TestCase("50.8503 4.3517", 50.8503, 4.3517)]
		public void Older_points_written_with_the_servers_culture_are_read(string value, double latitude, double longitude)
		{
			DestinationAutoFillService.TryReadPoint(value, out var lat, out var lng).Should().BeTrue();
			lat.Should().BeApproximately(latitude, 0.0000001);
			lng.Should().BeApproximately(longitude, 0.0000001);
		}

		[TestCase("50,8,3")]
		[TestCase("1,2,3,4,5")]
		[TestCase("abc,def")]
		[TestCase("0,0,0,0")]
		public void Ambiguous_or_unusable_points_are_not_read(string value)
		{
			DestinationAutoFillService.TryReadPoint(value, out _, out _).Should().BeFalse();
		}

		[Test]
		public void The_note_names_the_destination_and_why()
		{
			DestinationAutoFillService.BuildNoteText("St. Mary Burn Center")
				.Should().Be("Destination set from the department's destination rules: St. Mary Burn Center");
		}
	}
}
