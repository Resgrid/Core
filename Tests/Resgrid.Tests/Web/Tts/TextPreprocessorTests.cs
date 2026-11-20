using System.Linq;
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
		public void Preprocess_DoesNotReachAcrossACommaForStreetSuffixes()
		{
			// A street suffix belongs to the street phrase, so the match must not
			// bridge a comma into the next clause and rewrite an unrelated word.
			_preprocessor.Preprocess("Fall at 100 Center St, Dr Jones on scene", EnglishVoice)
				.Should().Be("Fall at 100 Center Street, Dr Jones on scene.");
		}

		[Test]
		public void Preprocess_ExpandsUnitDesignatorWrittenAfterAComma()
		{
			// Sub-unit designators are routinely comma-separated in CAD address fields.
			_preprocessor.Preprocess("123 Main St, Apt 4", EnglishVoice)
				.Should().Be("123 Main Street, Apartment 4.");
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

		// -----------------------------------------------------------
		//  Markup, run-on sentences and separator dashes: the engine read
		//  "**" as "asterisk asterisk", "missing.Text" as "missing dot text",
		//  and gave " - " no pause at all.
		// -----------------------------------------------------------

		[TestCase("**REQUEST APPROVED", "REQUEST APPROVED.")]
		[TestCase("Nature, ***LOST HIKERS*** 2 FEMALE HIKERS", "Nature, LOST HIKERS. two FEMALE HIKERS.")]
		[TestCase("**CANCELLATION PAGE** HIKERS FOUND", "CANCELLATION PAGE. HIKERS FOUND.")]
		[TestCase("Please **DO NOT** enter", "Please DO NOT enter.")]
		[TestCase("Crews staged ===== stand by", "Crews staged stand by.")]
		public void Preprocess_StripsDecorativeMarkup(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("non-verbal missing.Text Powell", "non-verbal missing. Text Powell.")]
		[TestCase("HIKER MISSING.TEXT CALLER", "HIKER MISSING. TEXT CALLER.")]
		[TestCase("Reported by U.S.A team", "Reported by U.S.A team.")]
		[TestCase("Updates at resgrid.com", "Updates at resgrid.com.")]
		public void Preprocess_RestoresMissingSpaceAfterSentenceEnd(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("REQUEST APPROVED - San Luis Obispo", "REQUEST APPROVED, San Luis Obispo.")]
		[TestCase("UPDATE- Missing Person SLO- Now", "UPDATE, Missing Person SLO, Now.")]
		[TestCase("Nature, - fall victim", "Nature, fall victim.")]
		[TestCase("pain — conscious", "pain, conscious.")]
		public void Preprocess_TurnsSeparatorDashesIntoPauses(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[Test]
		public void Preprocess_StructuralPassesApplyToEveryVoice()
		{
			// Markup and sentence breaks are engine problems, not English ones; the
			// vocabulary and number passes still stay English-only.
			_preprocessor.Preprocess("**ALERTE** incendie.Merci - 123 Main St", "fr")
				.Should().Be("ALERTE. incendie. Merci, 123 Main St.");
		}

		// -----------------------------------------------------------
		//  Phone numbers, digit lists and decimals.
		// -----------------------------------------------------------

		[TestCase("Text Powell 323-573-4264", "Text Powell 3 2 3, 5 7 3, 4 2 6 4.")]
		[TestCase("Text Powell 323-573-4264.", "Text Powell 3 2 3, 5 7 3, 4 2 6 4.")]
		[TestCase("Call (909) 641-7460", "Call 9 0 9, 6 4 1, 7 4 6 0.")]
		[TestCase("JULIA(909 641 7460) WRG", "JULIA(9 0 9, 6 4 1, 7 4 6 0) WRG.")]
		[TestCase("Call +1 323.573.4264 now", "Call 3 2 3, 5 7 3, 4 2 6 4 now.")]
		[TestCase("Call 573-4264 now", "Call 5 7 3, 4 2 6 4 now.")]
		public void Preprocess_ReadsPhoneNumbersInDigitGroups(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("COORDINATES 34.1679204, -117.7046650", "COORDINATES 34.1 6 7 9 2 0 4, -117.7 0 4 6 6 5 0.")]
		// A dotted seven-digit value is a decimal (a longitude), not a local phone number: its decimal point stays.
		[TestCase("longitude 117.1234 east", "longitude 117.1 2 3 4 east.")]
		[TestCase("CASE 2026-123-4567 OPEN", "CASE 2 0 2 6-123-4 5 6 7 OPEN.")]
		public void Preprocess_DoesNotReadOtherNumbersAsPhoneNumbers(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		[TestCase("requesting 30 type 1,2,3 for a team", "requesting 30 type 1, 2, 3 for a team.")]
		[TestCase("Budget 30,000 units", "Budget 30,000 units.")]
		[TestCase("BY MILE MARKER 1.5 AND", "BY MILE MARKER 1.5 AND.")]
		public void Preprocess_KeepsDigitListsAndDecimalsInDigitForm(string input, string expected)
		{
			_preprocessor.Preprocess(input, EnglishVoice).Should().Be(expected);
		}

		// -----------------------------------------------------------
		//  Sentence pacing: past ~500 phoneme ids Piper crams a sentence
		//  into the same ~12 s of audio, so long ones are broken up.
		// -----------------------------------------------------------

		[Test]
		public void Preprocess_LeavesShortSentencesWhole()
		{
			_preprocessor.Preprocess("Structure fire, smoke showing, two crews staged at the hydrant", EnglishVoice)
				.Should().Be("Structure fire, smoke showing, two crews staged at the hydrant.");
		}

		[Test]
		public void Preprocess_BreaksLongSentencesWithoutLosingWords()
		{
			const string input = "The crew should stage at the north trailhead and wait for the sheriff before heading up the canyon road "
				+ "toward the old ranger station and then the second team will follow the creek bed down toward the lower parking area "
				+ "where the family is waiting with the deputy and the medic unit until the helicopter arrives overhead and the incident "
				+ "commander gives the order to begin the grid search of the eastern slope";

			var result = _preprocessor.Preprocess(input, EnglishVoice);

			AssertEverySentenceFits(result);
			Words(result).Should().Equal(Words(input));
			// Broken before the pair of joining words, never between them.
			result.Should().Contain("ranger station. and then the second team");
		}

		[Test]
		public void Preprocess_PrefersClauseBoundariesWhenBreaking()
		{
			const string input = "Smoke reported from the second floor of the warehouse on the east side of the lot, "
				+ "crews from the north station are already on scene and requesting a second ladder truck with additional "
				+ "personnel for ventilation and search operations";

			var result = _preprocessor.Preprocess(input, EnglishVoice);

			AssertEverySentenceFits(result);
			result.Should().StartWith("Smoke reported from the second floor of the warehouse on the east side of the lot. crews from");
		}

		[Test]
		public void Preprocess_BreaksALongListOfNumbersAtItsCommas()
		{
			// "11,12,13..." gets its commas spaced as a list; a list comma is a pause like any other, so the sentence can be broken
			// there (only a phone number's digit-group commas are kept whole).
			const string input = "Respond units 11,12,13,14,15,16,17,18,19,20,21,22,23.";

			var result = _preprocessor.Preprocess(input, EnglishVoice);

			AssertEverySentenceFits(result);
			Words(result).Should().Equal(Words(input));
		}

		[Test]
		public void Preprocess_NeverBreaksInsideAGroupedPhoneNumber()
		{
			const string input = "Contact the reporting party who is waiting near the gate on the access road for the arriving units at 323-573-4264 "
				+ "or the backup number 909-641-7460 or the ranger station at 626-555-0142 or the county dispatch line at 213-555-0199";

			var result = _preprocessor.Preprocess(input, EnglishVoice);

			AssertEverySentenceFits(result);
			result.Should().Contain("3 2 3, 5 7 3, 4 2 6 4")
				.And.Contain("9 0 9, 6 4 1, 7 4 6 0")
				.And.Contain("6 2 6, 5 5 5, 0 1 4 2")
				.And.Contain("2 1 3, 5 5 5, 0 1 9 9");
			// Each number stays with the word that introduces it.
			result.Should().NotMatchRegex(@"\b(at|number)\. \d");
		}

		[Test]
		public void Preprocess_CallTwentySixTwoSeventySevenDispatchPrompt()
		{
			// Regression: the dispatch prompt for a real call (dept 2294, 26-277) that was
			// heard as "sped-up gibberish". The nature ran into one 37-word sentence through
			// "missing.Text", which Piper spoke at ~9.7 syllables a second.
			const string prompt = "New call, UPDATE- Missing Person SLO- Now Immediate Responce. Nature, **REQUEST APPROVED - San Luis Obispo "
				+ "requesting 30 type 1,2,3 for a 16 year old Autistic non-verbal missing.Text Powell if available 323-573-4264. Priority, High.";

			_preprocessor.Preprocess(prompt, EnglishVoice).Should().Be(
				"New call, UPDATE, Missing Person SLO, Now Immediate Responce. Nature, REQUEST APPROVED, San Luis Obispo "
				+ "requesting 30 type 1, 2, 3 for a sixteen year old Autistic non-verbal missing. Text Powell if available "
				+ "3 2 3, 5 7 3, 4 2 6 4. Priority, High.");
		}

		private static void AssertEverySentenceFits(string text)
		{
			foreach (var sentence in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+"))
			{
				TextPreprocessor.EstimateSpokenLength(sentence, 0, sentence.Length)
					.Should().BeLessThanOrEqualTo(TextPreprocessor.MaxSpokenSentenceLength, $"\"{sentence}\" is too long for one Piper sentence");
			}
		}

		private static string[] Words(string text)
		{
			return System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]+")
				.Select(match => match.Value)
				.ToArray();
		}
	}
}
