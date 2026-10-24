using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Stored user text must not reach a page as live markup.
	/// <para>
	/// Rich-text columns (call nature and notes, messages, department notes, trainings, protocols, documents, calendar
	/// items, logs, contact notes...) are written with <c>Html.SanitizedHtml</c>, which runs the allow-list sanitizer at
	/// render time: values reach the database from the v4 API, inbound e-mail, imports and older save paths, so the save
	/// path alone cannot be trusted. Plain values (names, file names, dates) are written with <c>@value</c>, which encodes.
	/// <c>@Html.Raw(Model.Something)</c> does neither, so a stored <c>&lt;img onerror=...&gt;</c> ran in every viewer's browser.
	/// </para>
	/// <para>
	/// JSON written into a page with Newtonsoft must escape HTML (<c>Html.ScriptJson</c> or settings with
	/// <c>StringEscapeHandling.EscapeHtml</c>): by default Newtonsoft leaves <c>&lt;/script&gt;</c> inside a string as it is, and a
	/// group, unit or contact name containing it closed the script block and started the attacker's own.
	/// </para>
	/// </summary>
	[TestFixture]
	public class StoredHtmlRenderingTests
	{
		private static readonly Regex RawMemberAccess = new Regex(@"@Html\.Raw\(\s*@?(?<path>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:\?\?\s*""[^""]*""\s*)?\)", RegexOptions.Compiled);
		private static readonly Regex RawNewtonsoft = new Regex(@"Html\.Raw\(\s*(?:Newtonsoft\.Json\.)?JsonConvert\.SerializeObject\(", RegexOptions.Compiled);

		// Values written with Html.Raw that are built by the server from encoded parts or HTML-escaped JSON, reviewed by name.
		private static readonly HashSet<string> ReviewedRawValues = new HashSet<string>(StringComparer.Ordinal)
		{
			"Model.TreeData",       // HtmlHelpers.ScriptJsonSettings JSON (Contacts, Personnel and Units controllers)
			"Model.GeoJson",        // HtmlHelpers.ScriptJsonSettings JSON (MappingController.EditLayer)
			"row.MailingAddress",   // <address> built from HtmlEncode'd parts (ReportsController.PersonnelReport)
			"message",              // EscapeHtml JSON (Account/SsoReturnGrant)
			"ViewBag.BodyObjects"   // layout hook, never set from user input
		};

		private static string WebRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Web");
		}

		private static IEnumerable<string> Views(string webRoot) =>
			Directory.EnumerateFiles(webRoot, "*.cshtml", SearchOption.AllDirectories)
				.Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s == "bin" || s == "obj" || s == "node_modules"))
				.OrderBy(p => p, StringComparer.Ordinal);

		private static int Line(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;

		private static bool IsServerBuilt(string path)
		{
			if (ReviewedRawValues.Contains(path))
				return true;
			var last = path.Substring(path.LastIndexOf('.') + 1);
			// ...Json values are serialized by the controller or the view; ...Html values are generated markup (UDF forms);
			// ...Msg values are JavaScript-encoded confirm texts; ...Count values are numbers.
			return last.EndsWith("Json", StringComparison.Ordinal) || last.EndsWith("Html", StringComparison.Ordinal) ||
			       last.EndsWith("Msg", StringComparison.Ordinal) || last == "Count";
		}

		[Test]
		public void stored_text_should_not_be_written_with_html_raw()
		{
			var webRoot = WebRoot();
			var problems = new List<string>();
			var seen = 0;

			foreach (var view in Views(webRoot))
			{
				var text = File.ReadAllText(view);
				foreach (Match match in RawMemberAccess.Matches(text))
				{
					seen++;
					var path = match.Groups["path"].Value;
					if (!IsServerBuilt(path))
						problems.Add($"{Path.GetRelativePath(webRoot, view)}:{Line(text, match.Index)}  {match.Value}");
				}
			}

			seen.Should().BeGreaterThan(10, "views write server-built JSON and UDF markup with Html.Raw; if none are found the scan is broken");
			if (problems.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{problems.Count} view(s) write a stored value with Html.Raw. Use @Html.SanitizedHtml(value) for rich text, @value for plain text, or Html.ScriptJson(value) for JSON:");
				foreach (var problem in problems)
					message.AppendLine("  " + problem);
				Assert.Fail(message.ToString());
			}
		}

		[Test]
		public void json_written_into_views_should_escape_html()
		{
			var webRoot = WebRoot();
			var problems = new List<string>();
			var seen = 0;

			foreach (var view in Views(webRoot))
			{
				var text = File.ReadAllText(view);
				foreach (Match match in RawNewtonsoft.Matches(text))
				{
					seen++;
					var settings = LastArgument(text, match.Index + match.Length - 1);
					if (settings == null || !EscapesHtml(settings.Trim(), text))
						problems.Add($"{Path.GetRelativePath(webRoot, view)}:{Line(text, match.Index)}");
				}
			}

			seen.Should().BeGreaterThan(20, "views pass EscapeHtml settings to JsonConvert inside Html.Raw; if none are found the scan is broken");
			if (problems.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{problems.Count} Html.Raw(JsonConvert.SerializeObject(...)) call(s) do not escape HTML, so a stored \"</script>\" closes the script block. Use @Html.ScriptJson(value):");
				foreach (var problem in problems)
					message.AppendLine("  " + problem);
				Assert.Fail(message.ToString());
			}
		}

		/// <summary>
		/// Text after the last top-level comma of the call whose '(' is at <paramref name="open"/> (the settings, when passed);
		/// null when there is none. Generic argument lists are not tracked, so "new Dictionary&lt;string, string&gt; {...}" without
		/// settings yields "string&gt; {...}", which is not settings.
		/// </summary>
		private static string LastArgument(string text, int open)
		{
			var depth = 0;
			var comma = -1;
			for (var i = open; i < text.Length; i++)
			{
				var c = text[i];
				if (c == '"')
				{
					for (i++; i < text.Length && text[i] != '"'; i++)
						if (text[i] == '\\') i++;
					continue;
				}
				if (c == '(' || c == '{' || c == '[') depth++;
				else if (c == ')' || c == '}' || c == ']')
				{
					depth--;
					if (depth == 0)
						return comma < 0 ? null : text.Substring(comma + 1, i - comma - 1);
				}
				else if (c == ',' && depth == 1) comma = i;
			}
			return null;
		}

		private static bool EscapesHtml(string settings, string view)
		{
			if (settings.Contains("EscapeHtml", StringComparison.Ordinal) || settings.Contains("ScriptJsonSettings", StringComparison.Ordinal))
				return true;
			if (!Regex.IsMatch(settings, @"^[A-Za-z_]\w*$"))
				return false;
			// A settings variable declared in the view.
			return Regex.IsMatch(view, @"\b" + Regex.Escape(settings) + @"\s*=\s*new\s+(?:Newtonsoft\.Json\.)?JsonSerializerSettings\s*\{[^}]*EscapeHtml");
		}
	}
}
