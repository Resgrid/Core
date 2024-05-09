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
using Vereyon.Web;

namespace Resgrid.Framework
{
	public static class StringHelpers
	{
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
			sc.Add(@"<!--(w|W)+?-->");
			sc.Add(@"<title>(w|W)+?</title>");

			// Get rid of classes and styles
			sc.Add(@"s?class=w+");
			sc.Add(@"s+style='[^']+'");

			// Get rid of unnecessary tags
			sc.Add(@"<(meta|link|/?o:|/?style|/?std|/?head|/?html|body|/?body)[^>]*?>");

			// Get rid of empty paragraph tags
			sc.Add(@"(<[^>]+>)+&nbsp;(</w+>)+");

			// remove bizarre v: element attached to <img> tag
			sc.Add(@"s+v:w+=""[^""]+""");

			// remove extra lines
			sc.Add(@"(nr){2,}");

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
			sanitizer.Tag("img").CheckAttribute("src", HtmlSanitizerCheckType.Url)
				.RemoveEmpty();
			sanitizer.Tag("a").SetAttribute("rel", "nofollow")
							  .CheckAttribute("href", HtmlSanitizerCheckType.Url)
							  .RemoveEmpty();

			string cleanHtml = sanitizer.Sanitize(source);

			if (!String.IsNullOrWhiteSpace(cleanHtml))
				return cleanHtml.Trim();

			//Logging.LogError("Following string could not be sanitized: " + source);
			return "Invalid HTML in Source String";
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

	}
}
