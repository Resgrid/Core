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
	}
}
