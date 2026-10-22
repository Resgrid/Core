using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUglify;
using NUglify.JavaScript;
using NUnit.Framework;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Guards the Release-only JS minification against NUglify's InvertIfReturn, which moved consts out of scope of
	/// the functions that use them (RESGRID-WEB-1MP, RESGRID-WEB-1MK). Debug builds do not minify, so without this
	/// test the regression only shows up in production.
	/// </summary>
	[TestFixture]
	public class ScriptMinificationSettingsTests
	{
		// The shape of checklists.js editor(): an early-return guard, a const after it, and a sibling function that
		// reads the const. InvertIfReturn wraps the const in `if (form) { ... }` and leaves render() outside it.
		private const string GuardedEditor = @"
(function () {
    'use strict';
    function editor() {
        const form = document.getElementById('editor'); if (!form) return;
        const model = JSON.parse(form.textContent);
        function render() { model.Sections.forEach(section => form.append(section.Name)); }
        render();
    }
    editor();
})();";

		private const string KeptGuard = @"if\(!(\w+)\)return;const \w+=JSON\.parse\(\1\.textContent\)";
		private const string InvertedGuard = @"if\((\w+)\)\{const \w+=JSON\.parse\(\1\.textContent\)";

		[Test]
		public void Create_DisablesInvertIfReturn()
		{
			// Act
			var settings = ScriptMinificationSettings.Create();

			// Assert
			(settings.KillSwitch & (long)TreeModifications.InvertIfReturn).Should().NotBe(0L,
				"InvertIfReturn moves consts out of scope of sibling functions in the minified /js/app scripts");
		}

		[Test]
		public void Minify_WithProductionSettings_KeepsEarlyReturnGuard()
		{
			// Act
			var result = Uglify.Js(GuardedEditor, ScriptMinificationSettings.Create());

			// Assert
			result.HasErrors.Should().BeFalse();
			result.Code.Should().MatchRegex(KeptGuard, "the guard must stay an early return so render() still sees model");
			result.Code.Should().NotMatchRegex(InvertedGuard);
		}

		[Test]
		public void Minify_WithDefaultSettings_InvertsGuard()
		{
			// Control: proves GuardedEditor still triggers InvertIfReturn, so the test above cannot pass vacuously.
			// If a NUglify upgrade makes this fail, re-check the repro before trusting the production-settings test.

			// Act
			var result = Uglify.Js(GuardedEditor, new CodeSettings());

			// Assert
			result.HasErrors.Should().BeFalse();
			result.Code.Should().MatchRegex(InvertedGuard);
		}

		[Test]
		public void Minify_EveryAppScript_WithProductionSettings_HasNoErrors()
		{
			// The same files Startup minifies in Release. A script NUglify cannot parse is a script the browser cannot run.

			// Arrange
			var scripts = GetMinifiedScripts();
			var failures = new List<string>();

			// Act
			foreach (var script in scripts)
			{
				try
				{
					var result = Uglify.Js(File.ReadAllText(script.FullName), ScriptMinificationSettings.Create());
					if (result.HasErrors)
						failures.Add($"{script.Name}: {result.Errors.First(e => e.IsError)}");
				}
				catch (Exception ex)
				{
					failures.Add($"{script.Name}: NUglify threw {ex.GetType().Name}");
				}
			}

			// Assert
			scripts.Should().NotBeEmpty();
			failures.Should().BeEmpty();
		}

		[Test]
		public void AppScripts_HaveNoRawLineSeparators()
		{
			// A raw U+2028/U+2029 is a line break to JavaScript and invisible in an editor. Inside a regex literal it
			// ends the line, so the page throws "Invalid regular expression: missing /" in every build
			// (resgrid.department.terminology.js, 2026-10-22). Write the escape instead.

			// Arrange
			var scripts = GetMinifiedScripts();

			// Act
			var offenders = scripts
				.Where(s => File.ReadAllText(s.FullName).IndexOfAny(new[] { (char)0x2028, (char)0x2029 }) >= 0)
				.Select(s => s.Name)
				.ToList();

			// Assert
			offenders.Should().BeEmpty();
		}

		private static List<FileInfo> GetMinifiedScripts()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			var js = new DirectoryInfo(Path.Combine(directory.FullName, "Web", "Resgrid.Web", "wwwroot", "js"));

			return js.GetDirectories("app").Single().GetFiles("*.js", SearchOption.AllDirectories)
				.Append(new FileInfo(Path.Combine(js.FullName, "site.js")))
				.ToList();
		}
	}
}
