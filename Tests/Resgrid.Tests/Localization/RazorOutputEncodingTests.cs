using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// Localized text placed inside JavaScript must be JavaScript-encoded, not HTML-encoded.
	/// <para>
	/// In <c>onclick="return confirm('@localizer["X"]');"</c> Razor turns an apostrophe into <c>&amp;#x27;</c>,
	/// but the browser decodes entities in the attribute before compiling the handler. A French or Italian
	/// value such as "l'élément" or "dell'utente" then ends the JS string early, the handler never compiles,
	/// and the click goes ahead with no confirmation: the delete, void or cancel runs unasked. In a
	/// <c>&lt;script&gt;</c> block, <c>'@Html.Raw(localizer["X"].Value)'</c> breaks the whole script the same way.
	/// </para>
	/// <para>
	/// Plain <c>'@localizer["X"]'</c> in a <c>&lt;script&gt;</c> block compiles, but the browser does not decode
	/// entities there. The JS string then holds <c>l&amp;#x27;&amp;#xE9;l&amp;#xE9;ment</c>, and confirm(), .text(),
	/// select2 placeholders, chart labels and values posted back to the server all show or store the entities.
	/// </para>
	/// <para>
	/// The check is on the view, not the translations. Every localized value in an inline handler or a script
	/// block has to go through <c>JsEncoder.Encode</c> (injected in Areas/User/Views/_ViewImports.cshtml) or
	/// another JavaScript encoder, whether or not any locale contains an apostrophe today. Translations arrive
	/// after the view is merged, and ASCII apostrophes are normal in them. The JS string then holds plain text,
	/// and script that puts it into HTML escapes it there, as it does for user data.
	/// </para>
	/// <para>
	/// Markup built as a string inside <c>@Html.Raw(...)</c> skips Razor's encoding altogether. Units/Index put
	/// <c>data-confirm='{localizer["DeleteUnitWarning"]} {u.Name}?'</c> there, and Messages/Inbox did the same with
	/// the message subject, so an apostrophe cut the prompt short and a subject containing markup was injected
	/// into the recipient's inbox. Every value spliced into such a string must be encoded where it is spliced.
	/// </para>
	/// </summary>
	[TestFixture]
	public class RazorOutputEncodingTests
	{
		private static readonly Regex LocalizerInject = new Regex(@"@inject\s+I(?:String|Html|View)Localizer(?:<[\w.]+>)?\s+(\w+)", RegexOptions.Compiled);
		private static readonly Regex HandlerAttribute = new Regex(@"\son[a-z]+\s*=\s*""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		// A real opening tag starts its line or follows markup or a Razor brace. This skips comments that
		// mention the tag, such as "JSON emitted inside <script>:" in CalOesMars/WorkItem.cshtml.
		private static readonly Regex ScriptBlock = new Regex(@"(?<=^[ \t]*|[{}>][ \t]*)<script\b[^>]*>(.*?)</script\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline);
		private static readonly Regex Identifier = new Regex(@"\G[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
		private static readonly Regex CharLiteral = new Regex(@"\G'(?:\\.|[^'\\])'", RegexOptions.Compiled);

		// Encoders whose output is safe inside a JS string and, because Razor HTML-encodes a plain string,
		// inside an HTML attribute as well. Json.Serialize is not on the attribute list: it returns
		// IHtmlContent, so its surrounding double quotes are written raw and close the attribute.
		private static readonly Regex HandlerSafe = new Regex(@"JsEncoder\.Encode\(|JavaScriptEncoder\.|JavaScriptStringEncode\(|JsonConvert\.SerializeObject\(", RegexOptions.Compiled);
		private static readonly Regex ScriptSafe = new Regex(@"JsEncoder\.Encode\(|JavaScriptEncoder\.|JavaScriptStringEncode\(|JsonConvert\.SerializeObject\(|Json\.Serialize\(|JsonSerializer\.Serialize\(", RegexOptions.Compiled);
		private static readonly Regex HandlerUnsafe = new Regex(@"Html\.Raw\(|Json\.Serialize\(", RegexOptions.Compiled);

		// Calls whose result is safe to splice into markup built inside Html.Raw.
		private static readonly Regex MarkupEncoderCall = new Regex(@"(?:Html\.Encode|WebUtility\.HtmlEncode|JsEncoder\.Encode|Url\.Action|Url\.Content)\(", RegexOptions.Compiled);
		private static readonly Regex CSharpString = new Regex(@"(?:\$@|@\$|\$|@)?""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);
		private static readonly Regex IdOperand = new Regex(@"^(?:[\w.]|\[\w+\])*Id$", RegexOptions.Compiled);

		// Spliced values reviewed as safe by name: numbers, or markup already built from encoded parts.
		private static readonly HashSet<string> ReviewedMarkupOperands = new HashSet<string>(StringComparer.Ordinal)
		{
			"customState", // int (Units/Index)
			"groupType",   // int (Personnel/Index)
			"reviewBadge", // badge built from Html.Encode'd text (Contacts/Index)
			"callsHeader", // <th> built from Html.Encode'd localized text, or empty (Contacts/Index)
			"callsCell"    // <td> built from an int call count, or empty (Contacts/Index)
		};

		private static readonly HashSet<string> RazorKeywords = new HashSet<string>(StringComparer.Ordinal)
		{
			"if", "else", "for", "foreach", "while", "do", "switch", "try", "catch", "finally", "using", "lock",
			"section", "functions", "code", "model", "inject", "inherits", "addTagHelper", "removeTagHelper",
			"tagHelperPrefix", "page", "namespace", "implements", "layout", "attribute", "helper"
		};

		private static string WebRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Web");
		}

		private static IEnumerable<string> Views(string webRoot)
		{
			return Directory.EnumerateFiles(webRoot, "*.cshtml", SearchOption.AllDirectories)
				.Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s == "bin" || s == "obj" || s == "node_modules"))
				.OrderBy(p => p, StringComparer.Ordinal);
		}

		/// <summary>Localizer property names a view can use: its own @inject lines plus every _ViewImports above it.</summary>
		private static HashSet<string> LocalizerNames(string view, string webRoot)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (Match match in LocalizerInject.Matches(File.ReadAllText(view)))
				names.Add(match.Groups[1].Value);

			for (var directory = new DirectoryInfo(Path.GetDirectoryName(view)!); directory != null && directory.FullName.StartsWith(webRoot, StringComparison.Ordinal); directory = directory.Parent)
			{
				var imports = Path.Combine(directory.FullName, "_ViewImports.cshtml");
				if (File.Exists(imports))
					foreach (Match match in LocalizerInject.Matches(File.ReadAllText(imports)))
						names.Add(match.Groups[1].Value);
			}

			return names;
		}

		private static int SkipBalanced(string text, int i, char open, char close)
		{
			var depth = 0;
			while (i < text.Length)
			{
				var c = text[i];
				if (c == '"')
				{
					i++;
					while (i < text.Length && text[i] != '"')
						i += text[i] == '\\' ? 2 : 1;
					i++;
					continue;
				}

				if (c == '\'')
				{
					var literal = CharLiteral.Match(text, i);
					if (literal.Success)
					{
						i += literal.Length;
						continue;
					}
				}

				if (c == open)
					depth++;
				else if (c == close && --depth == 0)
					return i + 1;

				i++;
			}

			return text.Length;
		}

		/// <summary>
		/// End of the Razor output expression starting at the '@' at <paramref name="at"/>: <c>@(...)</c>, or an
		/// implicit <c>@a.b["c"].d(e)</c>. Returns -1 when the '@' starts a directive, a code block or nothing.
		/// </summary>
		private static int RazorExpressionEnd(string text, int at)
		{
			var i = at + 1;
			if (i >= text.Length)
				return -1;
			if (text[i] == '(')
				return SkipBalanced(text, i, '(', ')');

			var identifier = Identifier.Match(text, i);
			if (!identifier.Success || RazorKeywords.Contains(identifier.Value))
				return -1;

			i += identifier.Length;
			while (i < text.Length)
			{
				if (text[i] == '[')
				{
					i = SkipBalanced(text, i, '[', ']');
					continue;
				}

				if (text[i] == '(')
				{
					i = SkipBalanced(text, i, '(', ')');
					continue;
				}

				var member = text[i] == '.' ? i + 1 : text[i] == '?' && i + 1 < text.Length && text[i + 1] == '.' ? i + 2 : -1;
				if (member > 0)
				{
					var name = Identifier.Match(text, member);
					if (name.Success)
					{
						i = member + name.Length;
						continue;
					}
				}

				break;
			}

			return i;
		}

		/// <summary>Razor output expressions in <paramref name="text"/> between <paramref name="start"/> and <paramref name="end"/>.</summary>
		private static IEnumerable<(int Index, string Expression)> RazorExpressions(string text, int start, int end, Func<int, bool> stopAt = null)
		{
			var i = start;
			while (i < end)
			{
				if (stopAt != null && stopAt(i))
					yield break;

				if (text[i] == '@')
				{
					if (i + 1 < text.Length && text[i + 1] == '@')
					{
						i += 2;
						continue;
					}

					var expressionEnd = RazorExpressionEnd(text, i);
					if (expressionEnd > 0)
					{
						yield return (i, text.Substring(i, expressionEnd - i));
						i = expressionEnd;
						continue;
					}
				}

				i++;
			}
		}

		private static int LineOf(string text, int index)
		{
			return text.Take(index).Count(c => c == '\n') + 1;
		}

		private sealed class Embedding
		{
			public string View;
			public int Line;
			public string Expression;
			public bool InHandler;
		}

		/// <summary>Every Razor expression that writes localized text into an inline on*="..." handler or a &lt;script&gt; block.</summary>
		private static List<Embedding> LocalizedJavaScriptEmbeddings()
		{
			var webRoot = WebRoot();
			var found = new List<Embedding>();

			foreach (var view in Views(webRoot))
			{
				var names = LocalizerNames(view, webRoot);
				if (names.Count == 0)
					continue;

				var localizer = new Regex(@"\b(?:" + string.Join("|", names.Select(Regex.Escape)) + @")\s*\[");
				var text = File.ReadAllText(view);
				var relative = Path.GetRelativePath(webRoot, view).Replace(Path.DirectorySeparatorChar, '/');

				// A handler attribute runs to the first double quote that is not inside a Razor expression.
				foreach (Match attribute in HandlerAttribute.Matches(text))
				{
					var start = attribute.Index + attribute.Length;
					foreach (var (index, expression) in RazorExpressions(text, start, text.Length, i => text[i] == '"'))
						if (localizer.IsMatch(expression))
							found.Add(new Embedding { View = relative, Line = LineOf(text, index), Expression = expression, InHandler = true });
				}

				foreach (Match script in ScriptBlock.Matches(text))
				{
					var content = script.Groups[1];
					foreach (var (index, expression) in RazorExpressions(text, content.Index, content.Index + content.Length))
						if (localizer.IsMatch(expression))
							found.Add(new Embedding { View = relative, Line = LineOf(text, index), Expression = expression, InHandler = false });
				}
			}

			return found;
		}

		[Test]
		public void localized_text_in_inline_handlers_and_scripts_should_be_javascript_encoded()
		{
			var problems = new List<string>();

			foreach (var embedding in LocalizedJavaScriptEmbeddings())
			{
				if (embedding.InHandler && (HandlerUnsafe.IsMatch(embedding.Expression) || !HandlerSafe.IsMatch(embedding.Expression)))
					problems.Add($"{embedding.View}:{embedding.Line} inline handler: {embedding.Expression}");
				else if (!embedding.InHandler && !ScriptSafe.IsMatch(embedding.Expression))
					problems.Add($"{embedding.View}:{embedding.Line} script block: {embedding.Expression}");
			}

			if (problems.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{problems.Count} localized string(s) reach JavaScript without JavaScript encoding. In an inline handler an apostrophe in any translation skips the confirmation; in a script block users see HTML entities.");
				message.AppendLine("Write '@JsEncoder.Encode(localizer[\"Key\"])' inside the JS string, and escape the value in script where it goes into HTML. Do not use Html.Raw or Json.Serialize in an on*=\"...\" attribute.");
				foreach (var problem in problems)
					message.AppendLine("  " + problem);

				Assert.Fail(message.ToString());
			}
		}

		[Test]
		public void the_scan_should_see_known_handler_and_script_embeddings()
		{
			// Guards the scan itself: if it stops finding these, the test above would pass without checking anything.
			var embeddings = LocalizedJavaScriptEmbeddings();

			embeddings.Should().Contain(e => e.InHandler && e.View.EndsWith("Views/Routes/Index.cshtml") && e.Expression.Contains("DeleteRoutePlanConfirm"));
			embeddings.Should().Contain(e => e.InHandler && e.View.EndsWith("Views/RecordHydrants/Details.cshtml") && e.Expression.Contains("L[\"DeleteHydrantConfirm\"]"));
			embeddings.Should().Contain(e => e.InHandler && e.View.EndsWith("Views/Records/Accountability.cshtml") && e.Expression.Contains("RemindAll"));
			embeddings.Should().Contain(e => !e.InHandler && e.View.EndsWith("Views/Home/EditUserProfile.cshtml") && e.Expression.Contains("VerificationCodeSent"));
			embeddings.Should().Contain(e => !e.InHandler && e.View.EndsWith("Views/Inventory/ManageTypes.cshtml") && e.Expression.Contains("commonLocalizer[\"Name\"]"));
			embeddings.Should().Contain(e => !e.InHandler && e.View.EndsWith("Views/WeatherAlerts/History.cshtml") && e.Expression.Contains("SeverityExtreme"));
			embeddings.Should().Contain(e => !e.InHandler && e.View.EndsWith("Views/Mapping/LiveRouting.cshtml") && e.Expression.Contains("DurationLabel"));
			embeddings.Should().NotContain(e => !e.InHandler && e.View.EndsWith("Views/CalOesMars/WorkItem.cshtml") && e.Expression.Contains("Printable"),
				"WorkItem's page body follows a comment that mentions <script>, and is HTML, not script");
			embeddings.Count(e => e.InHandler).Should().BeGreaterThan(50, "the Areas/User views have dozens of localized confirm() handlers");
		}

		private static IEnumerable<string> InterpolationHoles(string literal)
		{
			for (var i = 0; i < literal.Length; i++)
			{
				if (literal[i] != '{')
					continue;
				if (i + 1 < literal.Length && literal[i + 1] == '{')
				{
					i++;
					continue;
				}

				var end = SkipBalanced(literal, i, '{', '}');
				yield return literal.Substring(i + 1, Math.Max(0, end - i - 2));
				i = end - 1;
			}
		}

		/// <summary>
		/// Values spliced into a markup string built inside Html.Raw(<paramref name="argument"/>), by '+' or by
		/// interpolation, that are not encoded, not an ...Id and not reviewed. Html.Raw of a single value (stored
		/// rich text, server-built form HTML) is not a builder and returns nothing here.
		/// </summary>
		private static List<string> UnencodedMarkupOperands(string argument)
		{
			// Replace encoded calls first, so literals and '+' inside them are not read as operands.
			var text = argument;
			for (var call = MarkupEncoderCall.Match(text); call.Success; call = MarkupEncoderCall.Match(text))
			{
				var end = SkipBalanced(text, call.Index + call.Length - 1, '(', ')');
				text = text.Substring(0, call.Index) + "ENCODED" + text.Substring(end);
			}

			var operands = new List<string>();
			foreach (Match literal in CSharpString.Matches(text))
				if (literal.Value.StartsWith("$", StringComparison.Ordinal) || literal.Value.StartsWith("@$", StringComparison.Ordinal))
					operands.AddRange(InterpolationHoles(literal.Value));

			var withoutLiterals = CSharpString.Replace(text, "LITERAL");
			if (withoutLiterals.Contains('+'))
				operands.AddRange(withoutLiterals.Split('+'));

			return operands
				.Select(o => o.Trim().TrimStart('@'))
				.Where(o => o.Length > 0 && o != "LITERAL" && o != "ENCODED" && !IdOperand.IsMatch(o) && !ReviewedMarkupOperands.Contains(o))
				.ToList();
		}

		[Test]
		public void markup_built_inside_html_raw_should_encode_every_spliced_value()
		{
			var webRoot = WebRoot();
			var problems = new List<string>();
			var builders = 0;

			foreach (var view in Views(webRoot))
			{
				var text = File.ReadAllText(view);
				var relative = Path.GetRelativePath(webRoot, view).Replace(Path.DirectorySeparatorChar, '/');
				var scripts = ScriptBlock.Matches(text).Select(m => (Start: m.Groups[1].Index, End: m.Groups[1].Index + m.Groups[1].Length)).ToList();

				foreach (var (index, expression) in RazorExpressions(text, 0, text.Length))
				{
					// Script blocks are JavaScript; the test above covers them.
					if (!expression.StartsWith("@Html.Raw(", StringComparison.Ordinal) || scripts.Any(s => index >= s.Start && index < s.End))
						continue;

					var argument = expression.Substring("@Html.Raw(".Length, expression.Length - "@Html.Raw(".Length - 1);
					if (argument.Contains('+') || argument.Contains("$\""))
						builders++;

					var unencoded = UnencodedMarkupOperands(argument);
					if (unencoded.Count > 0)
						problems.Add($"{relative}:{LineOf(text, index)} {string.Join(", ", unencoded)}");
				}
			}

			builders.Should().BeGreaterThan(50, "Units, Personnel, Contacts, Messages and Dispatch build table rows inside Html.Raw; if none are found the scan is broken");

			if (problems.Count > 0)
			{
				var message = new StringBuilder();
				message.AppendLine($"{problems.Count} Html.Raw markup builder(s) splice in values without encoding. Html.Raw skips Razor's encoding, so text with ' or < breaks the markup or injects it.");
				message.AppendLine("Wrap each value in Html.Encode(...) (harmless for numbers), or add it to ReviewedMarkupOperands with the reason it is safe.");
				foreach (var problem in problems)
					message.AppendLine("  " + problem);

				Assert.Fail(message.ToString());
			}
		}

		[TestCase("Voulez-vous vraiment supprimer l'élément ?")]
		[TestCase("Confermi l'eliminazione dell'utente?")]
		[TestCase("Ім'я")]
		[TestCase("حذف العنصر؟")]
		[TestCase("Say \"yes\" \\ or no")]
		[TestCase("line one\nline two")]
		[TestCase("R&D + ops </script><script>alert(1)</script>")]
		public void javascript_encoded_text_should_pass_through_razor_html_encoding_unchanged(string text)
		{
			// The views write '@JsEncoder.Encode(x)' and let Razor HTML-encode the result. That only works because
			// the JavaScript encoder never emits a character the HTML encoder would change, and emits no quote,
			// backslash (other than its own escapes), line break or '<' that could end the string or the script.
			var javaScript = JavaScriptEncoder.Default.Encode(text);

			HtmlEncoder.Default.Encode(javaScript).Should().Be(javaScript);
			javaScript.Should().NotContainAny("'", "\"", "<", ">", "&", "\n", "\r");
			JsonSerializer.Deserialize<string>("\"" + javaScript + "\"").Should().Be(text, "the JS string literal must decode back to the original text");
		}
	}
}
