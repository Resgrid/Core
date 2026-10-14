using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace TextCommandServiceTests
	{
		public class with_the_text_command_service : TestBase
		{
			protected ITextCommandService _textCommandService;

			protected with_the_text_command_service()
			{
				_textCommandService = Resolve<ITextCommandService>();
			}
		}

		[TestFixture]
		public class when_determining_a_type : with_the_text_command_service
		{
			[Test]
			public void should_validate_available_number()
			{
				var valid = _textCommandService.DetermineType("4");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int) ActionTypes.StandingBy).ToString());
			}

			[Test]
			public void should_validate_available_text()
			{
				var valid = _textCommandService.DetermineType("standingby");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.StandingBy).ToString());
			}

			[Test]
			public void should_validate_responding_number()
			{
				var valid = _textCommandService.DetermineType("1");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.Responding).ToString());
			}

			[Test]
			public void should_validate_responding_text()
			{
				var valid = _textCommandService.DetermineType("responding");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.Responding).ToString());
			}

			[Test]
			public void should_validate_notresponding_text()
			{
				var valid = _textCommandService.DetermineType("notresponding");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.NotResponding).ToString());
			}

			[Test]
			public void should_validate_notresponding_text2()
			{
				var valid = _textCommandService.DetermineType("not responding");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.NotResponding).ToString());
			}

			[Test]
			public void should_validate_notresponding_number()
			{
				var valid = _textCommandService.DetermineType("2");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.NotResponding).ToString());
			}

			[Test]
			public void should_validate_onscene_text()
			{
				var valid = _textCommandService.DetermineType("onscene");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.OnScene).ToString());
			}

			[Test]
			public void should_validate_onscene_text2()
			{
				var valid = _textCommandService.DetermineType("on scene");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.OnScene).ToString());
			}

			[Test]
			public void should_validate_onscene_number()
			{
				var valid = _textCommandService.DetermineType("3");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Action);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)ActionTypes.OnScene).ToString());
			}

			[Test]
			public void should_validate_normal_text()
			{
				var valid = _textCommandService.DetermineType("available");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Available).ToString());
			}

			[Test]
			public void should_validate_normal_number()
			{
				var valid = _textCommandService.DetermineType("s1");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Available).ToString());
			}

			[Test]
			public void should_validate_delayed_text()
			{
				var valid = _textCommandService.DetermineType("delayed");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Delayed).ToString());
			}

			[Test]
			public void should_validate_delayed_number()
			{
				var valid = _textCommandService.DetermineType("s2");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Delayed).ToString());
			}

			[Test]
			public void should_validate_unavailable_text()
			{
				var valid = _textCommandService.DetermineType("unavailable");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Unavailable).ToString());
			}

			[Test]
			public void should_validate_unavailable_number()
			{
				var valid = _textCommandService.DetermineType("s3");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Unavailable).ToString());
			}

			[Test]
			public void should_validate_committed_text()
			{
				var valid = _textCommandService.DetermineType("committed");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Committed).ToString());
			}

			[Test]
			public void should_validate_committed_number()
			{
				var valid = _textCommandService.DetermineType("s4");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.Committed).ToString());
			}

			[Test]
			public void should_validate_onshift_text()
			{
				var valid = _textCommandService.DetermineType("onshift");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.OnShift).ToString());
			}

			[Test]
			public void should_validate_onshift_text2()
			{
				var valid = _textCommandService.DetermineType("on shift");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.OnShift).ToString());
			}

			[Test]
			public void should_validate_onshift_number()
			{
				var valid = _textCommandService.DetermineType("s5");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Staffing);
				valid.Data.Should().NotBeNull();
				valid.Data.Should().Be(((int)UserStateTypes.OnShift).ToString());
			}

			[Test]
			public void should_validate_help_text()
			{
				var valid = _textCommandService.DetermineType("help");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.Help);
				valid.Data.Should().BeNull();
			}

			[Test]
			public void should_validate_unknown()
			{
				var valid = _textCommandService.DetermineType("asdasdasdasd");

				valid.Should().NotBeNull();
				valid.Type.Should().Be(TextCommandTypes.None);
				valid.Data.Should().BeNull();
			}
		}

		[TestFixture]
		public class when_determining_a_type_for_a_department_with_custom_sets : with_the_text_command_service
		{
			private static CustomState Set(int type, params (int Id, string Text, int Order, bool Deleted)[] options)
			{
				var state = new CustomState { CustomStateId = 900 + type, Type = type, Details = new System.Collections.Generic.List<CustomStateDetail>() };
				foreach (var option in options)
					state.Details.Add(new CustomStateDetail { CustomStateDetailId = option.Id, ButtonText = option.Text, Order = option.Order, IsDeleted = option.Deleted });

				return state;
			}

			private readonly CustomState _statuses = Set((int)CustomStateTypes.Personnel,
				(5001, "Responding", 0, false), (5002, "In Quarters", 2, false), (5003, "Old Status", 1, true), (5004, "On-Scene: Fire", 3, false));

			private readonly CustomState _staffing = Set((int)CustomStateTypes.Staffing,
				(6001, "On Duty", 0, false), (6002, "Off Duty", 1, false));

			[TestCase("1", 5001)]
			[TestCase("2", 5002)] // positions skip deleted options and follow Order, as the HELP list does
			[TestCase("3", 5004)]
			[TestCase("inquarters", 5002)]
			[TestCase("In Quarters", 5002)]
			[TestCase("onscenefire", 5004)]
			public void custom_statuses_match_by_help_position_or_name(string text, int expectedId)
			{
				var payload = _textCommandService.DetermineType(text, _statuses, _staffing);

				payload.Type.Should().Be(TextCommandTypes.CustomAction);
				payload.Data.Should().Be(expectedId.ToString());
			}

			[TestCase("s1", 6001)]
			[TestCase("S2", 6002)]
			[TestCase("offduty", 6002)]
			public void custom_staffing_matches_by_s_position_or_name(string text, int expectedId)
			{
				var payload = _textCommandService.DetermineType(text, _statuses, _staffing);

				payload.Type.Should().Be(TextCommandTypes.CustomStaffing);
				payload.Data.Should().Be(expectedId.ToString());
			}

			[TestCase("4")]
			[TestCase("s3")]
			[TestCase("oldstatus")]
			[TestCase("standingby")]
			public void unknown_positions_deleted_options_and_built_in_names_do_not_match(string text)
			{
				var payload = _textCommandService.DetermineType(text, _statuses, _staffing);

				payload.Type.Should().Be(TextCommandTypes.None);
			}

			[TestCase("help", TextCommandTypes.Help)]
			[TestCase("stop", TextCommandTypes.Stop)]
			[TestCase("calls", TextCommandTypes.Calls)]
			[TestCase("c1445", TextCommandTypes.CallDetail)]
			public void core_commands_still_work(string text, TextCommandTypes expected)
			{
				var payload = _textCommandService.DetermineType(text, _statuses, _staffing);

				payload.Type.Should().Be(expected);
			}

			[Test]
			public void built_in_staffing_still_applies_when_only_statuses_are_custom()
			{
				var payload = _textCommandService.DetermineType("s1", _statuses, null);

				payload.Type.Should().Be(TextCommandTypes.Staffing);
				payload.Data.Should().Be(((int)UserStateTypes.Available).ToString());
			}
		}
	}
}