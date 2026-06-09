using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	/// <summary>
	/// Verifies <see cref="Utf8Sanitizer"/> produces content that is always safe for a PostgreSQL
	/// UTF-8 column: no NUL, no unpaired surrogates, with best-effort Windows-1252 mojibake repair,
	/// while leaving already-clean text untouched (and allocation-free).
	///
	/// Non-ASCII characters are built from explicit (char)0xNN code points so the byte-level intent
	/// is exact and unambiguous.
	/// </summary>
	[TestFixture]
	public class Utf8SanitizerTests
	{
		private const char Win1252RightQuote = (char)0x92; // repairs to U+2019
		private const char Win1252EnDash = (char)0x96;     // repairs to U+2013
		private const char Undefined1252 = (char)0x81;     // dropped (no Windows-1252 mapping)
		private const char RightSingleQuote = (char)0x2019;
		private const char EnDash = (char)0x2013;
		private const char HighSurrogate = (char)0xD83D;   // lead of U+1F600
		private const char LowSurrogate = (char)0xDE00;    // trail of U+1F600
		private const char CombiningAcute = (char)0x0301;
		private const char EAcute = (char)0x00E9;          // precomposed "é"
		private const char UDiaeresis = (char)0x00FC;      // "ü"
		private const char CapitalATilde = (char)0x00C3;   // "Ã"
		private const char CopyrightSign = (char)0x00A9;   // "©" (second byte of double-encoded é)

		[Test]
		public void Clean_Null_ReturnsNull()
		{
			Utf8Sanitizer.Clean(null).Should().BeNull();
		}

		[Test]
		public void Clean_Empty_ReturnsEmpty()
		{
			Utf8Sanitizer.Clean(string.Empty).Should().BeEmpty();
		}

		[Test]
		public void Clean_AlreadyCleanString_ReturnsSameReference()
		{
			// ASCII + Latin-1 accented chars (above the C1 range) + a valid emoji surrogate pair.
			var input = "Engine 51 responding " + EAcute + UDiaeresis + " " + HighSurrogate + LowSurrogate;

			var result = Utf8Sanitizer.Clean(input);

			result.Should().BeSameAs(input);
			Utf8Sanitizer.IsClean(input).Should().BeTrue();
		}

		[Test]
		public void Clean_StripsNulCharacters()
		{
			var input = "a\0b\0c";

			var result = Utf8Sanitizer.Clean(input);

			result.Should().Be("abc");
			result.Should().NotContain("\0");
		}

		[Test]
		public void Clean_ReplacesUnpairedHighSurrogate()
		{
			var input = "before" + HighSurrogate + "after"; // high surrogate with no trailing low surrogate

			var result = Utf8Sanitizer.Clean(input);

			result.Should().Be("before" + Utf8Sanitizer.ReplacementChar + "after");
		}

		[Test]
		public void Clean_ReplacesLoneLowSurrogate()
		{
			var input = "x" + LowSurrogate + "y"; // low surrogate with no preceding high surrogate

			var result = Utf8Sanitizer.Clean(input);

			result.Should().Be("x" + Utf8Sanitizer.ReplacementChar + "y");
		}

		[Test]
		public void Clean_PreservesValidSurrogatePair()
		{
			var input = "grin " + HighSurrogate + LowSurrogate + " done"; // U+1F600

			var result = Utf8Sanitizer.Clean(input);

			result.Should().BeSameAs(input);
		}

		[Test]
		public void Clean_RepairsWindows1252SmartQuote()
		{
			var input = "It" + Win1252RightQuote + "s here";

			var result = Utf8Sanitizer.Clean(input);

			result.Should().Be("It" + RightSingleQuote + "s here");
		}

		[Test]
		public void Clean_RepairsWindows1252EnDash()
		{
			var input = "9" + Win1252EnDash + "10";

			Utf8Sanitizer.Clean(input).Should().Be("9" + EnDash + "10");
		}

		[Test]
		public void Clean_DropsUndefinedWindows1252Bytes()
		{
			var input = "a" + Undefined1252 + "b";

			Utf8Sanitizer.Clean(input).Should().Be("ab");
		}

		[Test]
		public void TryClean_ReturnsFalseForCleanInput()
		{
			var changed = Utf8Sanitizer.TryClean("plain text", out var cleaned);

			changed.Should().BeFalse();
			cleaned.Should().Be("plain text");
		}

		[Test]
		public void TryClean_ReturnsTrueWhenChanged()
		{
			var changed = Utf8Sanitizer.TryClean("bad\0value", out var cleaned);

			changed.Should().BeTrue();
			cleaned.Should().Be("badvalue");
		}

		[Test]
		public void Clean_Output_IsAlwaysValidUtf8WithoutNul()
		{
			// NUL + lone high surrogate + C1 mojibake + lone low surrogate.
			var input = "mix\0" + HighSurrogate + " C1" + Win1252EnDash + " " + LowSurrogate + "tail";

			var result = Utf8Sanitizer.Clean(input);

			result.Should().NotContain("\0");

			var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
			var act = () => strict.GetByteCount(result);
			act.Should().NotThrow();
		}

		[Test]
		public void Clean_AppliesNfcNormalizationWhenRequested()
		{
			// "e" + combining acute accent; NFC composes to precomposed "é" (U+00E9).
			var input = "e" + CombiningAcute;

			var withoutNorm = Utf8Sanitizer.Clean(input);
			var withNorm = Utf8Sanitizer.Clean(input, repairDoubleEncoding: false, normalization: NormalizationForm.FormC);

			withoutNorm.Should().Be(input);
			withNorm.Should().Be(string.Empty + EAcute);
		}

		[Test]
		public void Clean_DoubleEncodingRepair_OptIn()
		{
			// CapitalATilde + CopyrightSign is the double-encoded (Latin-1-read) form of "é" (U+00E9).
			var input = "caf" + CapitalATilde + CopyrightSign;

			Utf8Sanitizer.Clean(input).Should().Be(input); // off by default
			Utf8Sanitizer.Clean(input, repairDoubleEncoding: true).Should().Be("caf" + EAcute);
		}

		[Test]
		public void CleanEntity_ScrubsStringProperties()
		{
			var entity = new SampleEntity
			{
				Name = "Unit\0 1",
				Notes = "Quote" + Win1252RightQuote + "s",
				Ignored = null,
				Number = 42
			};

			Utf8Sanitizer.CleanEntity(entity);

			entity.Name.Should().Be("Unit 1");
			entity.Notes.Should().Be("Quote" + RightSingleQuote + "s");
			entity.Ignored.Should().BeNull();
			entity.Number.Should().Be(42);
		}

		private class SampleEntity
		{
			public string Name { get; set; }
			public string Notes { get; set; }
			public string Ignored { get; set; }
			public int Number { get; set; }
		}
	}
}
