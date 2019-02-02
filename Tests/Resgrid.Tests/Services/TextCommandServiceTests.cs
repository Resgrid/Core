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
	}
}