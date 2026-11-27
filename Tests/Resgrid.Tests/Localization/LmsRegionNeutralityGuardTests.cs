using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Round 9C (Canada and EU customers): the Protocols and Training/LMS views, controllers and services never hard-code a US date format, a
	/// dollar sign, a USD default, miles or a 12-hour clock pattern. Dates and numbers go through the reader's culture and the department's
	/// time zone, amounts carry their ISO currency code (<c>LmsTrainingCurrency</c>), and every currency default goes through the one central
	/// helper. The allow-list names each genuine exception (a US state's mandated file layout, Resgrid's own USD/EUR add-on pricing) with
	/// the reason; extend it only for a format a reporting body or price list really requires.
	/// </summary>
	[TestFixture]
	public class LmsRegionNeutralityGuardTests
	{
		private static readonly string[] Scanned =
		{
			"Web/Resgrid.Web/Areas/User/Views/Lms*/**/*.cshtml",
			"Web/Resgrid.Web/Areas/User/Views/Courses/**/*.cshtml",
			"Web/Resgrid.Web/Areas/User/Views/Protocol*/**/*.cshtml",
			"Web/Resgrid.Web/Views/LmsLearn/**/*.cshtml",
			"Web/Resgrid.Web/Areas/User/Controllers/Lms*.cs",
			"Web/Resgrid.Web/Areas/User/Controllers/Courses*.cs",
			"Web/Resgrid.Web/Areas/User/Controllers/Protocol*.cs",
			"Web/Resgrid.Web/Controllers/Lms*.cs",
			"Core/Resgrid.Services/Lms*.cs",
			"Core/Resgrid.Services/Protocol*.cs",
			"Core/Resgrid.Model/Lms/*.cs"
		};

		private static readonly (string Rule, Regex Pattern)[] Rules =
		{
			("a US date format", new Regex(@"MM/dd/yy|M/d/yy|MM-dd-yyyy", RegexOptions.Compiled)),
			("a dollar sign as a currency", new Regex(@"(?<![\w$@])([""'])\$\1", RegexOptions.Compiled)),
			("a USD default", new Regex(@"""USD""", RegexOptions.Compiled)),
			("the viewer's culture currency symbol (ToString(\"C\"))", new Regex(@"ToString\(\s*""[Cc]\d?""|\{\d+:[Cc]\d?\}", RegexOptions.Compiled)),
			("miles or mph", new Regex(@"""[^""\n]*\b(miles?|mph)\b[^""\n]*""", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
			("a 12-hour clock pattern", new Regex(@"h:mm tt|hh:mm tt", RegexOptions.Compiled))
		};

		/// <summary>"relative/path|rule" pairs that are genuinely required, each with its reason.</summary>
		private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			// CAPCE and the US state CE / roster files keep the US layouts their reporting bodies use (M/d/yyyy for CE Broker).
			"Core/Resgrid.Model/Lms/LmsCeReporting.cs|a US date format",
			// The one central training-currency helper: USD only when a department chose none and the cluster does not bill in euros.
			"Core/Resgrid.Model/Lms/LmsTrainingCurrency.cs|a USD default",
			// Resgrid's own LMS add-on price list is in USD (Stripe) or EUR (Paddle); this is Resgrid's billing, not a department's training costs.
			"Web/Resgrid.Web/Areas/User/Controllers/LmsBillingController.cs|a dollar sign as a currency",
		};

		private static string Root()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull();
			return directory!.FullName;
		}

		private static IEnumerable<string> Files(string root, string pattern)
		{
			// "dir/Prefix*/**/*.ext" or "dir/Prefix*.ext": a directory (or file) prefix with an optional recursive part.
			var parts = pattern.Split('/');
			var recursive = parts.Contains("**");
			var filePart = parts.Last();
			var dirParts = parts.Take(parts.Length - 1).Where(p => p != "**").ToList();
			var last = dirParts.Last();
			IEnumerable<string> dirs;
			if (last.Contains('*'))
			{
				var parent = Path.Combine(root, Path.Combine(dirParts.Take(dirParts.Count - 1).ToArray()));
				dirs = Directory.Exists(parent) ? Directory.GetDirectories(parent, last) : Array.Empty<string>();
			}
			else
			{
				var dir = Path.Combine(root, Path.Combine(dirParts.ToArray()));
				dirs = Directory.Exists(dir) ? new[] { dir } : Array.Empty<string>();
			}

			return dirs.SelectMany(d => Directory.GetFiles(d, filePart, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
		}

		[Test]
		public void Protocols_and_lms_code_has_no_hard_coded_us_formats()
		{
			var root = Root();
			var problems = new List<string>();
			var scanned = 0;
			foreach (var pattern in Scanned)
			{
				foreach (var file in Files(root, pattern).Distinct())
				{
					scanned++;
					var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
					var lines = File.ReadAllLines(file);
					for (var i = 0; i < lines.Length; i++)
					{
						foreach (var (rule, regex) in Rules)
						{
							if (regex.IsMatch(lines[i]) && !Allowed.Contains(relative + "|" + rule))
								problems.Add($"{relative}:{i + 1} has {rule}: {lines[i].Trim()}");
						}
					}
				}
			}

			scanned.Should().BeGreaterThan(100, "the scan must reach the Protocols and LMS views and services");
			problems.Should().BeEmpty("format dates and numbers with the reader's culture, show amounts with their ISO code and take currencies from LmsTrainingCurrency");
		}

		[Test]
		public void The_guard_catches_what_it_is_for()
		{
			var samples = new[]
			{
				("@d.ToString(\"MM/dd/yyyy\")", "a US date format"),
				("var symbol = \"$\";", "a dollar sign as a currency"),
				("Currency = x ?? \"USD\",", "a USD default"),
				("@amount.ToString(\"C\")", "the viewer's culture currency symbol (ToString(\"C\"))"),
				("Notes = \"Record the road miles driven\"", "miles or mph"),
				("@t.ToString(\"h:mm tt\")", "a 12-hour clock pattern")
			};
			foreach (var (line, rule) in samples)
				Rules.Single(r => r.Rule == rule).Pattern.IsMatch(line).Should().BeTrue(line);

			Rules.Any(r => r.Pattern.IsMatch("var text = $\"Hello {name}\"; $('#x').hide();")).Should().BeFalse("interpolated strings and jQuery are not currency");
			Rules.Any(r => r.Pattern.IsMatch("@d.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture)")).Should().BeFalse();
		}
	}
}
