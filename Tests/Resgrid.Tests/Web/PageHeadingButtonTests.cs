using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The module buttons at the top of every page: labels without icons, one distinct colour each from css/app-header.css (never the
	/// warning or danger colours), and white text that stays legible. View Call's Repeat button rendered as white text on a white button.
	/// </summary>
	[TestFixture]
	public class PageHeadingButtonTests
	{
		private static readonly Regex HeadingStart = new Regex(@"<div[^>]*class=""[^""]*\bpage-heading\b[^""]*""", RegexOptions.Compiled);
		private static readonly Regex DivTag = new Regex(@"<div\b|</div>", RegexOptions.Compiled);
		private static readonly Regex ButtonElement = new Regex(@"<(a|button)\b[^>]*class=""[^""]*\bbtn\b[^""]*""[^>]*>(?<inner>.*?)</\1>", RegexOptions.Compiled | RegexOptions.Singleline);
		private static readonly Regex Icon = new Regex(@"<i\s+class=""(fa|glyphicon)\b", RegexOptions.Compiled);

		[Test]
		public void No_page_heading_button_carries_an_icon()
		{
			var offenders = new List<string>();
			foreach (var view in Views())
			{
				var heading = HeadingBlock(File.ReadAllText(view));
				if (heading == null)
					continue;

				foreach (Match button in ButtonElement.Matches(heading))
				{
					if (Icon.IsMatch(button.Groups["inner"].Value))
						offenders.Add($"{Path.GetFileName(Path.GetDirectoryName(view))}/{Path.GetFileName(view)}: {Collapse(button.Value)}");
				}
			}

			offenders.Should().BeEmpty("module buttons at the top of a page are text only");
		}

		[Test]
		public void The_heading_palette_has_eight_distinct_legible_colours_and_none_of_them_is_an_alert_colour()
		{
			var css = File.ReadAllText(RepositoryFile("Web/Resgrid.Web/wwwroot/css/app-header.css"));
			var palette = Regex.Matches(css, @"--rg-heading-btn-(?<n>\d+):\s*(?<hex>#[0-9a-fA-F]{6})")
				.Select(m => m.Groups["hex"].Value.ToLowerInvariant()).ToList();

			palette.Should().HaveCount(8);
			palette.Should().OnlyHaveUniqueItems();
			foreach (var colour in palette)
			{
				ContrastWithWhite(colour).Should().BeGreaterThanOrEqualTo(4.5, $"{colour} carries white button text");

				// Warning (orange/amber/yellow) and danger (red/pink) hues read as alerts, not as places to go.
				var (hue, saturation) = HueAndSaturation(colour);
				if (saturation > 0.2)
					(hue >= 345 || hue < 65).Should().BeFalse($"{colour} (hue {hue:0}) falls in the red to yellow range");
			}

			// Every place in a container maps to a palette entry, with and without the "of S" form.
			for (var n = 1; n <= 8; n++)
			{
				css.Should().Contain($":nth-child(8n+{n}) {{ --rg-heading-btn: var(--rg-heading-btn-{n}); }}");
				css.Should().Contain($":nth-child(8n+{n} of .btn, :has(.btn)) {{ --rg-heading-btn: var(--rg-heading-btn-{n}); }}");
			}
		}

		[Test]
		public void The_header_stylesheet_loads_after_the_theme_in_the_internal_bundle()
		{
			var startup = File.ReadAllText(RepositoryFile("Web/Resgrid.Web/Startup.cs"));
			var bundle = Regex.Match(startup, @"AddCssBundle\(""/css/int-bundle\.css"",(?<files>.*?)\);", RegexOptions.Singleline);

			bundle.Success.Should().BeTrue();
			var files = bundle.Groups["files"].Value;
			files.IndexOf("\"css/app-header.css\"", StringComparison.Ordinal).Should()
				.BeGreaterThan(files.IndexOf("\"css/style.css\"", StringComparison.Ordinal), "the header rules must win ties with the theme");
		}

		[Test]
		public void White_text_in_a_buttons_actions_wrapper_is_only_forced_onto_coloured_buttons()
		{
			foreach (var stylesheet in new[] { "Web/Resgrid.Web/wwwroot/css/style.css", "Web/Resgrid.Web/wwwroot/scss/_custom.scss" })
			{
				var css = File.ReadAllText(RepositoryFile(stylesheet));
				Regex.IsMatch(css, @"(^|\})\s*\.buttons-actions a\s*\{", RegexOptions.Multiline).Should()
					.BeFalse($"{stylesheet} would paint a white button's label white");
				css.Should().Contain(".buttons-actions a.btn:not(.btn-default):not(.btn-white):not(.btn-link)");
			}
		}

		[Test]
		public void The_top_bar_has_a_menu_button_for_screens_that_hide_the_sidebar()
		{
			var navbar = File.ReadAllText(RepositoryFile("Web/Resgrid.Web/Areas/User/Views/Shared/_TopNavbar.cshtml"));

			// site.js binds the sidebar toggle to .navbar-minimalize.
			navbar.Should().MatchRegex(@"<button type=""button"" class=""navbar-minimalize rg-menu-toggle""[^>]*aria-label=""@commonLocalizer\[""ToggleMenu""\]""");
			navbar.Should().NotContain("style=\"height: 50px;\"", "a fixed-height bar pushed its wrapped links over the page heading");
		}

		private static IEnumerable<string> Views()
		{
			var web = RepositoryFile("Web/Resgrid.Web");
			return Directory.EnumerateFiles(Path.Combine(web, "Areas"), "*.cshtml", SearchOption.AllDirectories)
				.Concat(Directory.EnumerateFiles(Path.Combine(web, "Views"), "*.cshtml", SearchOption.AllDirectories))
				.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
		}

		private static string HeadingBlock(string view)
		{
			var start = HeadingStart.Match(view);
			if (!start.Success)
				return null;

			var depth = 0;
			foreach (Match tag in DivTag.Matches(view, start.Index))
			{
				depth += tag.Value == "<div" ? 1 : -1;
				if (depth == 0)
					return view.Substring(start.Index, tag.Index + tag.Length - start.Index);
			}

			return view.Substring(start.Index);
		}

		private static string Collapse(string html) => Regex.Replace(html, @"\s+", " ").Trim();

		private static double ContrastWithWhite(string hex)
		{
			static double Channel(int value)
			{
				var c = value / 255d;
				return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
			}

			var (r, g, b) = Rgb(hex);
			var luminance = 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
			return 1.05 / (luminance + 0.05);
		}

		private static (double Hue, double Saturation) HueAndSaturation(string hex)
		{
			var (r8, g8, b8) = Rgb(hex);
			double r = r8 / 255d, g = g8 / 255d, b = b8 / 255d;
			double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
			if (delta == 0)
				return (0, 0);

			double hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
			if (hue < 0)
				hue += 360;

			var lightness = (max + min) / 2;
			return (hue, delta / (1 - Math.Abs(2 * lightness - 1)));
		}

		private static (int R, int G, int B) Rgb(string hex) =>
			(int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber), int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber), int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));

		private static string RepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
		}
	}
}
