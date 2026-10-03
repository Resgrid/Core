using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Vereyon.Web;

namespace Resgrid.Framework
{
	/// <summary>
	/// Result of password complexity verification
	/// </summary>
	public sealed record PasswordComplexityResult(
		bool IsValid, 
		List<string> Errors);

	public static class StringHelpers
	{
		/// <summary>
		/// Masks a phone number for diagnostic logging (PII redaction), keeping only the last
		/// four digits (e.g. "***4567"). Values with four or fewer characters are fully masked.
		/// </summary>
		public static string MaskPhoneNumber(string number)
		{
			if (string.IsNullOrWhiteSpace(number))
				return "***";

			var trimmed = number.Trim();
			if (trimmed.Length <= 4)
				return "***";

			return "***" + trimmed.Substring(trimmed.Length - 4);
		}

		/// <summary>
		/// Remove HTML tags from string using char array.
		/// </summary>
		public static string StripHtmlTagsCharArray(string source)
		{
			if (string.IsNullOrEmpty(source))
				return string.Empty;

			var strippedSource = source.Replace("\n", "").Replace("\r", "");

			char[] array = new char[strippedSource.Length];
			int arrayIndex = 0;
			bool inside = false;

			for (int i = 0; i < strippedSource.Length; i++)
			{
				char let = strippedSource[i];
				if (let == '<')
				{
					inside = true;
					continue;
				}
				if (let == '>')
				{
					inside = false;
					continue;
				}
				if (!inside)
				{
					array[arrayIndex] = let;
					arrayIndex++;
				}
			}
			return new string(array, 0, arrayIndex);
		}

		public static string SanitizeHtmlInString(string source)
		{
			if (string.IsNullOrWhiteSpace(source))
				return source;

			StringCollection sc = new StringCollection();
			string temp = source;

			// get rid of unnecessary tag spans (comments and title)
			sc.Add(@"<!--[\s\S]*?-->");
			sc.Add(@"<title>[\s\S]*?</title>");

			// Get rid of unnecessary tags but keep the text they wrap (document wrappers, Word's <o:p> and <st1:place> smart tags)
			sc.Add(@"<(meta|link|/?o:|/?style|/?st\d|/?head|/?html|body|/?body)[^>]*?>");

			// The rest of the classic Word clean-up list (class=, style='', v: attributes, &nbsp;-only tags, repeated line
			// breaks) is intentionally not applied: the sanitizer below already drops every attribute it does not allow, and
			// those patterns ran over the whole string, so they deleted matching user text too. With their backslashes lost
			// they were stripping "nr", "s" and "w" sequences out of ordinary words (#181).

			foreach (string s in sc)
			{
				source = Regex.Replace(source, s, "", RegexOptions.IgnoreCase);
			}

			if (String.IsNullOrWhiteSpace(source))
			{
				Logging.LogError("Following string could not be stripped of Word HTML: " + temp);
				return "Invalid HTML in Source String";
			}

			var sanitizer = new HtmlSanitizer();
			sanitizer.Tag("h1").RemoveEmpty();
			sanitizer.Tag("h2").RemoveEmpty();
			sanitizer.Tag("h3").RemoveEmpty();
			sanitizer.Tag("h4").RemoveEmpty();
			sanitizer.Tag("h5").RemoveEmpty();
			sanitizer.Tag("h6").RemoveEmpty();
			sanitizer.Tag("strong").RemoveEmpty();
			sanitizer.Tag("b").Rename("strong").RemoveEmpty();
			sanitizer.Tag("div").Rename("p").RemoveEmpty();
			sanitizer.Tag("i").RemoveEmpty();
			sanitizer.Tag("em");
			sanitizer.Tag("br");
			sanitizer.Tag("p").RemoveEmpty();
			sanitizer.Tag("div").NoAttributes(SanitizerOperation.FlattenTag);
			sanitizer.Tag("span").RemoveEmpty();
			sanitizer.Tag("ul");
			sanitizer.Tag("ol");
			sanitizer.Tag("li");

			// Formatting the rich text editors (Quill) produce. The sanitizer runs in white list mode, where an unlisted
			// tag is removed together with its text, so underlined words, pasted links (usually wrapped in <u>) and code
			// blocks vanished on save (#181). None of these get any attributes.
			sanitizer.Tag("u").RemoveEmpty();
			sanitizer.Tag("ins").Rename("u").RemoveEmpty();
			sanitizer.Tag("s").RemoveEmpty();
			sanitizer.Tag("strike").Rename("s").RemoveEmpty();
			sanitizer.Tag("del").Rename("s").RemoveEmpty();
			sanitizer.Tag("sub").RemoveEmpty();
			sanitizer.Tag("sup").RemoveEmpty();
			sanitizer.Tag("blockquote").RemoveEmpty();
			sanitizer.Tag("pre").RemoveEmpty();
			sanitizer.Tag("code").RemoveEmpty();

			// Presentational and structural wrappers from pasted Word, e-mail and web content: drop the tag, keep its text.
			foreach (var tag in FlattenedHtmlTags)
				sanitizer.Tag(tag).Operation(SanitizerOperation.FlattenTag);

			sanitizer.Tag("img").SanitizeAttributes("src", new SpaceEncodingUrlAttributeSanitizer())
				.RemoveEmpty();
			sanitizer.Tag("a").SetAttribute("rel", "nofollow")
							  .SanitizeAttributes("href", new SpaceEncodingUrlAttributeSanitizer())
							  .RemoveEmpty();

			string cleanHtml = sanitizer.Sanitize(source);

			if (!String.IsNullOrWhiteSpace(cleanHtml))
				return cleanHtml.Trim();

			//Logging.LogError("Following string could not be sanitized: " + source);
			return "Invalid HTML in Source String";
		}

		/// <summary>
		/// Tags <see cref="SanitizeHtmlInString"/> unwraps (tag removed, text kept) instead of deleting with their content.
		/// </summary>
		private static readonly string[] FlattenedHtmlTags =
		{
			"font", "center", "small", "big", "mark", "abbr", "cite", "q", "tt", "kbd", "samp", "var", "time",
			"section", "article", "header", "footer", "main", "address", "figure", "figcaption"
		};

		/// <summary>
		/// Link/image URL check used by <see cref="SanitizeHtmlInString"/>. Runs the sanitizer's standard URL check (same
		/// http/https/mailto/tel allow-list, so javascript:, data: etc. are still rejected) but percent-encodes interior
		/// spaces first, so a link to "some file.pdf" is kept instead of dropped. The check writes the URL back unescaped,
		/// so the spaces are re-encoded afterwards; otherwise "%20" became a space and the link was lost on the next save.
		/// </summary>
		private sealed class SpaceEncodingUrlAttributeSanitizer : IHtmlAttributeSanitizer
		{
			private readonly UrlCheckerAttributeSanitizer _urlChecker = new UrlCheckerAttributeSanitizer();

			public SanitizerOperation SanitizeAttribute(HtmlAttribute attribute, HtmlSanitizerTagRule tagRule)
			{
				if (!string.IsNullOrEmpty(attribute.Value))
					attribute.Value = attribute.Value.Trim().Replace(" ", "%20");

				var operation = _urlChecker.SanitizeAttribute(attribute, tagRule);

				if (operation == SanitizerOperation.DoNothing && !string.IsNullOrEmpty(attribute.Value))
					attribute.Value = attribute.Value.Replace(" ", "%20");

				return operation;
			}
		}

		public static string Truncate(this string value, int maxLength)
		{
			return value.Length <= maxLength ? value : value.Substring(0, maxLength);
		}

		public static string GetSizeInMemory(this long bytesize)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = Convert.ToDouble(bytesize);
			int order = 0;
			while (len >= 1024D && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}

			return string.Format(CultureInfo.CurrentCulture, "{0:0.##} {1}", len, sizes[order]);
		}

		public static bool IsValidDomainName(string name)
		{
			var value = Uri.CheckHostName(name);
			return value != UriHostNameType.Unknown;
		}

		public static bool ValidateEmail(string email)
		{
			string pattern = @"^(?!\.)(""([^""\r\\]|\\[""\r\\])*""|"
										+ @"([-a-z0-9!#$%&'*+/=?^_`{|}~]|(?<!\.)\.)*)(?<!\.)"
										+ @"@[a-z0-9][\w\.-]*[a-z0-9]\.[a-z][a-z\.]*[a-z]$";

			Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);

			return regex.IsMatch(email);
		}

		public static string GetDescription(this Enum value)
		{
			Type type = value.GetType();
			string name = Enum.GetName(type, value);
			if (name != null)
			{
				FieldInfo field = type.GetField(name);
				if (field != null)
				{
					DescriptionAttribute attr =
								 Attribute.GetCustomAttribute(field,
									 typeof(DescriptionAttribute)) as DescriptionAttribute;
					if (attr != null)
					{
						return attr.Description;
					}
				}
			}
			return null;
		}

		public static string GetDisplayString(this Enum e)
		{
			var attributes = (DisplayAttribute[])e.GetType().GetField(e.ToString()).GetCustomAttributes(typeof(DisplayAttribute), false);

			if (attributes != null)
				return attributes.Length > 0 ? attributes[0].Name : string.Empty;

			return e.ToString();
		}

		public static string SerializeProto(this object o)
		{
			return ObjectSerialization.Serialize(o);
		}

		public static string GetNumbers(string input)
		{
			return new string(input.Where(c => char.IsDigit(c)).ToArray());
		}

		public static string TrimLastCharacter(this String str)
		{
			if (String.IsNullOrEmpty(str))
			{
				return str;
			}
			else
			{
				return str.TrimEnd(str[str.Length - 1]);
			}
		}

		public static string Base64Encode(string plainText)
		{
			var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
			return System.Convert.ToBase64String(plainTextBytes);
		}

		public static string Base64Decode(string base64EncodedData)
		{
			var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
			return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
		}

		public static string SanitizeCoordinatesString(string source)
		{
			if (string.IsNullOrWhiteSpace(source))
				return "";

			HashSet<char> lstAllowedCharacters = new HashSet<char> { '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '-', '.'};

			var resultStrBuilder = new StringBuilder(source.Length);

			foreach (char c in source)
			{
				if (lstAllowedCharacters.Contains(c))
				{
					resultStrBuilder.Append(c);
				}
				else
				{
					resultStrBuilder.Append(" ");
				}
			}

			return resultStrBuilder.ToString();
		}

		/// <summary>
		/// Verifies password complexity against security requirements
		/// </summary>
		/// <param name="password">The password to verify</param>
		/// <param name="minLength">Minimum password length (default: 8)</param>
		/// <param name="requireUppercase">Require at least one uppercase letter (default: true)</param>
		/// <param name="requireLowercase">Require at least one lowercase letter (default: true)</param>
		/// <param name="requireDigit">Require at least one digit (default: true)</param>
		/// <param name="requireSpecialChar">Require at least one special character (default: false)</param>
		/// <returns>PasswordComplexityResult indicating validity and any errors</returns>
		public static PasswordComplexityResult VerifyPasswordComplexity(
			string password, 
			int minLength = 8,
			bool requireUppercase = true,
			bool requireLowercase = true,
			bool requireDigit = true,
			bool requireSpecialChar = false)
		{
			var errors = new List<string>();

			if (string.IsNullOrWhiteSpace(password))
			{
				errors.Add("Password cannot be empty");
				return new PasswordComplexityResult(false, errors);
			}

			// Check minimum length
			if (password.Length < minLength)
			{
				errors.Add($"Password must be at least {minLength} characters long");
			}

			// Check for uppercase letter
			if (requireUppercase && !password.Any(char.IsUpper))
			{
				errors.Add("Password must include an uppercase letter");
			}

			// Check for lowercase letter
			if (requireLowercase && !password.Any(char.IsLower))
			{
				errors.Add("Password must include a lowercase letter");
			}

			// Check for digit
			if (requireDigit && !password.Any(char.IsDigit))
			{
				errors.Add("Password must include a number (digit)");
			}

			// Check for special character
			if (requireSpecialChar)
			{
				var specialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";
				if (!password.Any(c => specialChars.Contains(c)))
				{
					errors.Add("Password must include a special character");
				}
			}

			return new PasswordComplexityResult(errors.Count == 0, errors);
		}

		/// <summary>
		/// Validates password complexity using default Resgrid requirements
		/// </summary>
		/// <param name="password">The password to validate</param>
		/// <returns>True if password meets complexity requirements</returns>
		public static bool IsValidPassword(string password)
		{
			var result = VerifyPasswordComplexity(password);
			return result.IsValid;
		}

	}
}
