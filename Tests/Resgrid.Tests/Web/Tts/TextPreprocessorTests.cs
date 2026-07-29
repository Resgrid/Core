using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class TextPreprocessorTests
	{
		private TextPreprocessor _preprocessor;

		private const string EnglishVoice = "en-us+klatt4";

		[SetUp]
		public void SetUp()
		{
			_preprocessor = new TextPreprocessor(NullLogger<TextPreprocessor>.Instance);
		}

		// -----------------------------------------------------------
		//  Address abbreviation expansion must preserve the house
		//  number and street name — only the suffix is rewritten.
		// -----------------------------------------------------------

		[TestCase("123 Main St", "123 Main Street.")]
		[TestCase("456 Oak Ave", "456 Oak Avenue.")]
		[TestCase("789 Sunset Blvd", "789 Sunset Boulevard.")]
		public void Preprocess_ExpandsAddressSuffix_WithoutDroppingHouseNumberOrStreetName(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[Test]
		public void Preprocess_ExpandsMultipleAddressSuffixesInOneAddress()
		{
			_preprocessor.Preprocess("100 Main St Apt 4", EnglishVoice)
				.Should().Be("100 Main Street Apartment 4.");
		}

		[Test]
		public void Preprocess_DoesNotExpandAddressSuffixWithoutLeadingNumber()
		{
			// "St" with no house number ahead of it is left alone.
			_preprocessor.Preprocess("Meet at Main St.", EnglishVoice)
				.Should().Be("Meet at Main St.");
		}

		// -----------------------------------------------------------
		//  Abbreviation expansion is case-sensitive so ordinary words
		//  are never rewritten into dispatch jargon.
		// -----------------------------------------------------------

		[TestCase("Please do so now", "Please do so now.")]
		[TestCase("please pass the tools", "please pass the tools.")]
		[TestCase("an apt description", "an apt description.")]
		[TestCase("the co responder", "the co responder.")]
		public void Preprocess_LeavesOrdinaryEnglishWordsAlone(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("MVC with ENTRP", "Motor Vehicle Collision with Entrapment.")]
		[TestCase("SO on scene", "Sheriff's Office on scene.")]
		[TestCase("RP ADV 2 VEH MVC", "Reporting Party Advised two Vehicle Motor Vehicle Collision.")]
		[TestCase("HAZMAT spill", "Hazardous Materials spill.")]
		[TestCase("HazMat spill", "Hazardous Materials spill.")]
		public void Preprocess_ExpandsUppercaseDispatchCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Number handling for spoken clarity.
		// -----------------------------------------------------------

		[Test]
		public void Preprocess_ReadsLongNumbersDigitByDigit()
		{
			_preprocessor.Preprocess("12345 Elm Rd", EnglishVoice)
				.Should().Be("1 2 3 4 5 Elm Road.");
		}

		[Test]
		public void Preprocess_SpeaksSmallCountsAsWords()
		{
			_preprocessor.Preprocess("2 patients trapped", EnglishVoice)
				.Should().Be("two patients trapped.");
		}

		[Test]
		public void Preprocess_SplitsUnitIdentifiers()
		{
			_preprocessor.Preprocess("E1 and L14 responding", EnglishVoice)
				.Should().Be("E one and L fourteen responding.");
		}

		// -----------------------------------------------------------
		//  Slash notation and sentence termination.
		// -----------------------------------------------------------

		[Test]
		public void Preprocess_ExpandsSlashNotation()
		{
			_preprocessor.Preprocess("75 Y/O male", EnglishVoice)
				.Should().Be("75 Year Old male.");
		}

		[Test]
		public void Preprocess_AppendsTerminalPunctuation()
		{
			_preprocessor.Preprocess("Structure fire reported", EnglishVoice)
				.Should().EndWith(".");
		}

		[Test]
		public void Preprocess_NonEnglishVoicePassesThroughUntouched()
		{
			_preprocessor.Preprocess("123 Main St", "es")
				.Should().Be("123 Main St.");
		}
	}
}
