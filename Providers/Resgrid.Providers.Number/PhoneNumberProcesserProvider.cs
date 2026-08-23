using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	public class PhoneNumberProcesserProvider : IPhoneNumberProcesserProvider
	{
		public PhoneNumberResult Process(string phoneNumber, string countryCode = null)
		{
			var result = new PhoneNumberResult();

			try
			{
				var territory = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.ToUpperInvariant();

				// Strip characters the parser cannot see past. Real stored numbers carry invisible
				// bidi/format marks pasted in from other apps, tabs, and non-standard brackets - all of
				// which make an otherwise perfectly good number fail to parse.
				var cleaned = Sanitize(phoneNumber);

				if (string.IsNullOrWhiteSpace(cleaned))
					return result;

				// In order of confidence. The first two are the original behaviour; the rest only ever
				// run once those have failed, so a number that parsed before still parses the same way.
				foreach (var attempt in Attempts(cleaned, territory))
				{
					if (!GlobalPhone.GlobalPhone.TryParse(attempt.Value, out var candidate, attempt.Territory) ||
						candidate == null || !candidate.IsValid)
						continue;

					result.IsValid = true;
					result.InternationalNumber = candidate.InternationalString;
					result.LocalNumber = candidate.NationalString;
					result.Region = candidate.RegionCode;

					return result;
				}

				// Nothing parsed. Report against the original input so the caller sees what it passed in.
				if (GlobalPhone.GlobalPhone.TryParse(cleaned, out var parsed, territory) && parsed != null)
				{
					result.InternationalNumber = parsed.InternationalString;
					result.LocalNumber = parsed.NationalString;
					result.Region = parsed.RegionCode;
				}
			}
			catch (Exception e)
			{
				result.IsValid = false;
				result.ErrorMessage = e.ToString();
			}

			return result;
		}

		private static (string Value, string Territory)[] Attempts(string cleaned, string territory)
		{
			var digits = new string(cleaned.Where(char.IsDigit).ToArray());

			return new[]
			{
				// Original behaviour: the caller's region, then no region hint (for "+" numbers).
				(cleaned, territory),
				(cleaned, "ZZ"),

				// "00" is the international access prefix in most of the world - the typed equivalent of
				// "+". Stored values routinely use it ("0040...", "00306..."), and it parses as nothing.
				(cleaned.StartsWith("00", StringComparison.Ordinal) && digits.Length > 4
					? "+" + digits.Substring(2)
					: null, "ZZ"),

				// A country code with no "+" at all ("447700900123"). Only worth trying when the length
				// rules out a national number, and only after the region attempts have failed - so a
				// valid national number is never reinterpreted as an international one.
				(digits.Length >= 11 && digits.Length <= 15 && !cleaned.Contains('+')
					? "+" + digits
					: null, "ZZ")
			}
			.Where(a => !string.IsNullOrWhiteSpace(a.Item1))
			.Select(a => (a.Item1, a.Item2))
			.ToArray();
		}

		/// <summary>
		/// Removes characters that carry no dialling meaning but do stop the number parsing: Unicode
		/// format and control marks (bidi overrides pasted in from other applications), and bracket
		/// styles the parser does not recognise. Digits, "+", and the ordinary separators the parser
		/// already understands are left exactly as they are.
		/// </summary>
		private static string Sanitize(string phoneNumber)
		{
			if (string.IsNullOrWhiteSpace(phoneNumber))
				return string.Empty;

			var builder = new StringBuilder(phoneNumber.Length);

			foreach (var character in phoneNumber)
			{
				var category = CharUnicodeInfo.GetUnicodeCategory(character);

				if (category == UnicodeCategory.Format || category == UnicodeCategory.Control)
					continue;

				// "{201} 555-0123" is a real stored shape; the parser handles "()" but not "{}" or "[]".
				if (character == '{' || character == '[')
				{
					builder.Append('(');
					continue;
				}

				if (character == '}' || character == ']')
				{
					builder.Append(')');
					continue;
				}

				builder.Append(character);
			}

			return builder.ToString().Trim();
		}
	}
}
