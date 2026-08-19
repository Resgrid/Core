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
		[TestCase("debris etc on roadway", "debris et cetera on roadway.")]
		[TestCase("debris ETC on roadway", "debris et cetera on roadway.")]
		[TestCase("debris Etc on roadway", "debris et cetera on roadway.")]
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
		//  CAD patient age/sex shorthand.
		// -----------------------------------------------------------

		[TestCase("35/F fall victim", "35 Year Old Female fall victim.")]
		[TestCase("35/f fall victim", "35 Year Old Female fall victim.")]
		[TestCase("9/M seizure", "nine Year Old Male seizure.")]
		[TestCase("35F chest pain", "35 Year Old Female chest pain.")]
		[TestCase("35f chest pain", "35 Year Old Female chest pain.")]
		[TestCase("104M fall", "104 Year Old Male fall.")]
		[TestCase("35YOM chest pain", "35 Year Old Male chest pain.")]
		[TestCase("35 yof unconscious", "35 Year Old Female unconscious.")]
		[TestCase("35yo diabetic", "35 Year Old diabetic.")]
		public void Preprocess_ExpandsAgeSexShorthand(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("Apt 5F", "Apt 5F.")]
		[TestCase("I-35F at exit 12", "I-35F at exit 12.")]
		public void Preprocess_LeavesNonPatientDigitLetterTokensAlone(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("PT C/O SOB", "Patient Complaining Of Shortness of Breath.")]
		[TestCase("N/V since morning", "Nausea and Vomiting since morning.")]
		[TestCase("AMS UNRESP on arrival", "Altered Mental Status Unresponsive on arrival.")]
		[TestCase("FX to left leg, LAC to head", "Fracture to left leg, Laceration to head.")]
		public void Preprocess_ExpandsMedicalShorthand(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Domain coverage: fire, police/security, SAR, industrial,
		//  emergency management.
		// -----------------------------------------------------------

		[TestCase("STRU FIRE SMK SHOWING", "Structure FIRE Smoke SHOWING.")]
		[TestCase("AFA CHIM FIRE", "Automatic Fire Alarm Chimney FIRE.")]
		[TestCase("VEG FIRE NEAR XFMR", "Vegetation FIRE NEAR Transformer.")]
		public void Preprocess_ExpandsFireDispatchCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("BOLO W/M NB HWY 101", "Be On the Lookout White Male Northbound Highway 101.")]
		[TestCase("B&E IN PROGRESS WPN SEEN", "Breaking and Entering IN PROGRESS Weapon SEEN.")]
		[TestCase("DUI STOP", "D, U, I STOP.")]
		[TestCase("SUSP SUBJ GOA", "Suspicious Subject Gone on Arrival.")]
		public void Preprocess_ExpandsPoliceAndSecurityCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("MISPER LKP TRAILHEAD", "Missing Person Last Known Position TRAILHEAD.")]
		[TestCase("LSW RED JACKET", "Last Seen Wearing RED JACKET.")]
		[TestCase("USAR TEAM TO ICP", "Urban Search and Rescue TEAM TO Incident Command Post.")]
		public void Preprocess_ExpandsSearchAndRescueCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("H2S ALARM LEL 15 PPM", "Hydrogen Sulfide ALARM Lower Explosive Limit fifteen Parts Per Million.")]
		[TestCase("CO2 DISCHARGE RM 4", "Carbon Dioxide DISCHARGE Room 4.")]
		[TestCase("LOTO NOT VERIFIED", "Lockout Tagout NOT VERIFIED.")]
		public void Preprocess_ExpandsIndustrialCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("EOC ACTIVATED SITREP TO FOLLOW", "Emergency Operations Center ACTIVATED Situation Report TO FOLLOW.")]
		[TestCase("SIP ORDERED FOR EVAC ZONE", "Shelter in Place ORDERED FOR Evacuation ZONE.")]
		public void Preprocess_ExpandsEmergencyManagementCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Directional bounds: NB/SB/EB/WB are the standard tokens, and
		//  compass corners expand. NH/SH/EH/WH must never expand as bounds —
		//  NH is New Hampshire (it spells out instead).
		// -----------------------------------------------------------

		// "5 AT" → "five AT" comes from the long-standing small-number rule.
		[TestCase("NB I-5 AT EXIT 120", "Northbound I-five AT EXIT 120.")]
		[TestCase("VEH S/B ON MAIN", "Vehicle Southbound ON MAIN.")]
		[TestCase("NW corner of BLDG", "Northwest corner of Building.")]
		public void Preprocess_HandlesDirectionalBounds(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Spell-out codes: no safe expansion, so read as spaced letters.
		//  Word-colliding codes (OK, OH, IN, ...) must stay untouched.
		// -----------------------------------------------------------

		[TestCase("Nashua, NH", "Nashua, N H.")]
		[TestCase("Detroit, MI", "Detroit, M I.")]
		[TestCase("Vancouver BC", "Vancouver B C.")]
		[TestCase("CHECK ID", "CHECK I D.")]
		[TestCase("IS EVERYONE OK", "IS EVERYONE OK.")]
		[TestCase("HEAD TO OH", "HEAD TO OH.")]
		public void Preprocess_SpellsOutUnexpandableCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Ten-codes keep their numbers but lose the dash for pacing.
		// -----------------------------------------------------------

		[TestCase("10-4", "10 4.")]
		[TestCase("10-50 AT MAIN", "10 50 AT MAIN.")]
		[TestCase("11-99 OFC DOWN", "11 99 Officer DOWN.")]
		public void Preprocess_PacesTenCodes(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
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
