using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The scripts the sign-in, registration, recovery and invitation pages depend on actually load. _ScriptsPartial carried the CDN
	/// jQuery 1.12.3 integrity hash on this site's own jQuery 2.2.4, so browsers refused it (and Bootstrap after it) on every page that
	/// used the partial: the MFA, recovery and SSO sign-in pages ran with no jQuery. The invitation page loaded no jQuery at all.
	/// </summary>
	[TestFixture]
	public class AccountPageScriptsTests
	{
		private static readonly string[] SubmitOnceForms =
		{
			"LogOn", "Register", "ForgotPassword", "ResetPassword", "CompleteInvite", "LoginWith2fa", "LoginWithRecoveryCode", "LoginMfa",
			"LoginMfaSetup", "LostFactor", "Recovery", "SsoLogOn", "ForcePasswordChange"
		};

		[Test]
		public void Every_integrity_hash_in_the_scripts_partial_matches_the_local_file_it_names()
		{
			var partial = File.ReadAllText(RepositoryFile("Web/Resgrid.Web/Views/Shared/_ScriptsPartial.cshtml"));
			var tags = Regex.Matches(partial, @"<script\s+src=""~/(?<src>[^""]+)""[^>]*?integrity=""(?<integrity>sha384-[^""]+)""", RegexOptions.Singleline);

			tags.Should().NotBeEmpty();
			foreach (Match tag in tags)
			{
				var file = RepositoryFile("Web/Resgrid.Web/wwwroot/" + tag.Groups["src"].Value);
				var actual = "sha384-" + Convert.ToBase64String(SHA384.HashData(File.ReadAllBytes(file)));
				actual.Should().Be(tag.Groups["integrity"].Value, $"a browser refuses {tag.Groups["src"].Value} when its integrity does not match");
			}
		}

		[Test]
		public void The_invitation_page_loads_jquery_before_the_scripts_that_use_it()
		{
			var view = File.ReadAllText(RepositoryFile("Web/Resgrid.Web/Views/Account/CompleteInvite.cshtml"));

			var jquery = view.IndexOf("~/lib/jquery/dist/jquery.min.js", StringComparison.Ordinal);
			jquery.Should().BeGreaterThan(0);
			jquery.Should().BeLessThan(view.IndexOf("jquery.passwordRequirements.min.js", StringComparison.Ordinal));
			jquery.Should().BeLessThan(view.IndexOf("_ValidationScriptsPartial", StringComparison.Ordinal));
			jquery.Should().BeLessThan(view.IndexOf("$(function", StringComparison.Ordinal));
		}

		[TestCaseSource(nameof(SubmitOnceForms))]
		public void Each_account_form_posts_once(string viewName)
		{
			var view = File.ReadAllText(RepositoryFile($"Web/Resgrid.Web/Views/Account/{viewName}.cshtml"));

			var forms = Regex.Matches(view, @"<form\b[^>]*method=""post""[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value).ToList();
			forms.Should().NotBeEmpty();
			// Not pressed by the user: the hidden form a script posts once, and the language picker's.
			forms.Where(f => !f.Contains(" hidden") && !f.Contains("id=\"setLanguageForm\"")).Should().OnlyContain(f => f.Contains("data-submit-once"),
				"a second click while the first post is on the way must not post the form again");
			view.Should().Contain("~/js/app/public/resgrid.submit-once.js");
		}

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
