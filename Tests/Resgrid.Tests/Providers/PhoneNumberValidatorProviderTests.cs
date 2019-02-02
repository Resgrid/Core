using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Tests.Providers
{
	namespace PhoneNumberValidatorProviderTests
	{
		public class with_the_phone_number_validation_provider : TestBase
		{
			protected IPhoneNumberProcesserProvider _phoneNumberValidatorProvider;

			protected with_the_phone_number_validation_provider()
			{
				_phoneNumberValidatorProvider = Resolve<IPhoneNumberProcesserProvider>();
			}
		}

		[TestFixture]
		public class when_validating_us_phone_numbers : with_the_phone_number_validation_provider
		{
			[Test]
			public void should_be_valid_all_flat()
			{
				var result = _phoneNumberValidatorProvider.Process("5555555555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_all_flat_with_country_code()
			{
				var result = _phoneNumberValidatorProvider.Process("15555555555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_all_flat_with_country_code_plus()
			{
				var result = _phoneNumberValidatorProvider.Process("+15555555555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_formatted()
			{
				var result = _phoneNumberValidatorProvider.Process("(555) 555-5555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_formatted_with_country_code()
			{
				var result = _phoneNumberValidatorProvider.Process("1 (555) 555-5555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_formatted_with_country_code_plus()
			{
				var result = _phoneNumberValidatorProvider.Process("+1 (555) 555-5555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_formatted_no_perens()
			{
				var result = _phoneNumberValidatorProvider.Process("555-555-5555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_invalid_not_enough_numbers()
			{
				var result = _phoneNumberValidatorProvider.Process("555555555");
				result.IsValid.Should().BeFalse();
			}

			[Test]
			public void should_be_valid_periods_with_country_code()
			{
				var result = _phoneNumberValidatorProvider.Process("1.555.555.5555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_periods()
			{
				var result = _phoneNumberValidatorProvider.Process("555.555.5555");
				result.IsValid.Should().BeTrue();
			}
		}

		[TestFixture]
		public class when_validating_international_phone_numbers : with_the_phone_number_validation_provider
		{
			[Test]
			public void should_be_valid_seperated_uk_with_country_code()
			{
				var result = _phoneNumberValidatorProvider.Process("+44 5555 555555");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_invalid_all_flat_uk_with_country_code()
			{
				var result = _phoneNumberValidatorProvider.Process("1445555666666", "gb");
				result.IsValid.Should().BeFalse();
			}

			[Test]
			public void should_be_invalid_all_flat_uk_with_country_code_plus()
			{
				var result = _phoneNumberValidatorProvider.Process("+1445555555583", "gb");
				result.IsValid.Should().BeFalse();
			}

			[Test]
			public void should_be_valid_all_flat_uk_local()
			{
				var result = _phoneNumberValidatorProvider.Process("01505555713", "gb");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_all_flat_uk_country_plus()
			{
				var result = _phoneNumberValidatorProvider.Process("+4405555555513");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			[Ignore("")]
			public void should_be_valid_all_flat_uk_country()
			{
				var result = _phoneNumberValidatorProvider.Process("4405555555513", "gb");
				result.IsValid.Should().BeTrue();
			}

			[Test]
			public void should_be_valid_all_flat_uk_country_2()
			{
				var result = _phoneNumberValidatorProvider.Process("+31555555554");
				result.IsValid.Should().BeTrue();
			}
		}
	}
}
