using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace NumbersServiceTests
	{
		public class with_the_numbers_service : TestBase
		{
			protected INumbersService _numbersService;

			protected with_the_numbers_service()
			{
				_numbersService = Resolve<INumbersService>();
			}
		}

		[TestFixture]
		public class when_testing_a_number : with_the_numbers_service
		{
			[Test]
			public void should_be_able_to_validate_exact_numbers_with_punc()
			{
				var pattern = "1 (210) 100-001";
				var number = "1 (210) 100-001";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_validate_different_numbers()
			{
				var pattern = "1 (210) 100-001";
				var number = "1 (210) 155-001";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeFalse();
			}

			[Test]
			public void should_be_able_to_validate_same_numbers_different_punc()
			{
				var pattern = "1 (210) 100-001";
				var number = "1-210-100-001";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_validate_different_length_numbers()
			{
				var pattern = "1 (210) 100-001";
				var number = "1-210-100-0001";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeFalse();
			}

			[Test]
			public void should_be_able_to_validate_exact_numbers()
			{
				var pattern = "1210100001";
				var number = "1210100001";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_be_able_to_validate_patterns_with_a_wildcard()
			{
				var pattern = "12101000*0";
				var number = "1210100010";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_be_able_to_validate_patterns_with_a_multiple_wildcards()
			{
				var pattern = "12*01000**";
				var number = "1210100010";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_note_be_able_to_validate_patterns_with_a_multiple_wildcards_bad_length()
			{
				var pattern = "12*01000**";
				var number = "12101000101";

				var valid = _numbersService.IsNumberPatternValid(pattern, number);

				valid.Should().BeFalse();
			}

			[Test]
			public void should_be_able_to_validate_list_of_valid_numbers()
			{
				var patterns = new List<string> { "1210100001", "1210100002", "1210100003", "1210100004" };
				var number = "1210100003";

				var valid = _numbersService.DoesNumberMatchAnyPattern(patterns, number);

				valid.Should().BeTrue();
			}

			[Test]
			public void should_not_be_able_to_validate_list_of_invalid_numbers()
			{
				var patterns = new List<string> { "1210100001", "1210100002", "1210100003", "1210100004" };
				var number = "1210100005";

				var valid = _numbersService.DoesNumberMatchAnyPattern(patterns, number);

				valid.Should().BeFalse();
			}
		}
	}
}