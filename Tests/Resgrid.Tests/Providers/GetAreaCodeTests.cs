using FluentAssertions;
using NUnit.Framework;
using Resgrid.Providers.NumberProvider;

namespace Resgrid.Tests.Providers
{
	namespace GetAreaCodeTests
	{
		public class with_the_number_helper : TestBase
		{
			protected with_the_number_helper()
			{
				
			}
		}

		[TestFixture]
		public class when_trying_to_get_us_area_codes : with_the_number_helper
		{
			[Test]
			public void should_be_valid_for_parens()
			{
				var result = NumberHelper.TryGetAreaCode("(406) 555-0951");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("406");
			}

			[Test]
			public void should_be_valid_for_just_numbers()
			{
				var result = NumberHelper.TryGetAreaCode("2015555794");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("201");
			}

			[Test]
			public void should_be_valid_for_just_numbers_with_prefix()
			{
				var result = NumberHelper.TryGetAreaCode("12015555794");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("201");
			}

			[Test]
			public void should_be_valid_for_just_numbers_with_prefixplus()
			{
				var result = NumberHelper.TryGetAreaCode("+12015555794");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("201");
			}

			[Test]
			public void should_be_valid_for_dots()
			{
				var result = NumberHelper.TryGetAreaCode("1.306.555.7434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_dots_plus()
			{
				var result = NumberHelper.TryGetAreaCode("+1.306.555.7434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_dots_just_area()
			{
				var result = NumberHelper.TryGetAreaCode("306.555.7434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_spaces()
			{
				var result = NumberHelper.TryGetAreaCode("306 555 5434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_spaces_with_prefix()
			{
				var result = NumberHelper.TryGetAreaCode("1 306 555 5434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_dashes()
			{
				var result = NumberHelper.TryGetAreaCode("306-555-5434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_dashes_with_prefix()
			{
				var result = NumberHelper.TryGetAreaCode("1-306-555-5434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_valid_for_dashes_with_prefixplus()
			{
				var result = NumberHelper.TryGetAreaCode("+1-306-555-5434");
				result.Should().NotBeNullOrEmpty();
				result.Should().Be("306");
			}

			[Test]
			public void should_be_empty_for_international_spaces()
			{
				var result = NumberHelper.TryGetAreaCode("+93 077 395 9309");
				result.Should().BeNullOrWhiteSpace();
			}

			[Test]
			public void should_be_empty_for_international()
			{
				var result = NumberHelper.TryGetAreaCode("+930773959309");
				result.Should().BeNullOrWhiteSpace();
			}

			[Test]
			public void should_be_empty_for_international_noplus()
			{
				var result = NumberHelper.TryGetAreaCode("930773959309");
				result.Should().BeNullOrWhiteSpace();
			}
		}
	}
}
