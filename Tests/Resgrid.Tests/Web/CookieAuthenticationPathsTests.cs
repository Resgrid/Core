using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Web.Controllers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The cookie scheme redirects every challenge to LoginPath and every Forbid() to AccessDeniedPath. When one of
	/// those names an action that does not exist, the redirect ends on a bare 404 ("/Public/Forbidden/" did, for
	/// every refused Records action), so each configured path has to resolve to a real controller action.
	/// </summary>
	[TestFixture]
	public class CookieAuthenticationPathsTests
	{
		[TestCase("LoginPath")]
		[TestCase("AccessDeniedPath")]
		public void Configured_path_resolves_to_a_controller_action(string option)
		{
			var startup = File.ReadAllText(FindRepositoryFile("Web/Resgrid.Web/Startup.cs"));
			var match = Regex.Match(startup, option + @"\s*=\s*new PathString\(""/(?<controller>\w+)/(?<action>\w+)/?""\)");
			match.Success.Should().BeTrue($"Startup.cs should set the cookie {option} to a /Controller/Action path");

			var controller = typeof(PublicController).Assembly.GetTypes()
				.SingleOrDefault(t => t.Namespace == typeof(PublicController).Namespace && t.Name == match.Groups["controller"].Value + "Controller");
			controller.Should().NotBeNull($"{option} names the {match.Groups["controller"].Value} controller");
			controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Should().Contain(m => m.Name == match.Groups["action"].Value, $"{option} names the {match.Groups["action"].Value} action");
		}

		[Test]
		public void Forbidden_renders_the_permissions_page_as_403()
		{
			var result = new PublicController().Forbidden().Should().BeOfType<ViewResult>().Subject;

			result.StatusCode.Should().Be(403);
			File.Exists(FindRepositoryFile($"Web/Resgrid.Web/Views/Shared/{result.ViewName}.cshtml")).Should().BeTrue();
		}

		private static string FindRepositoryFile(string relativePath)
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
