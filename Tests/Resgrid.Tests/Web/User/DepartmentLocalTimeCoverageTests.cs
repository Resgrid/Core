using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// A view that calls <c>DepartmentTime.From(ViewData)</c> throws "Department time context was not initialized" unless the controller
	/// that renders it carries <see cref="DepartmentLocalTimeAttribute"/>. Knowledge Settings / Gaps and Signatures Subject (through the
	/// SignatureRequests component and _SignaturesList) shipped without it and answered 500. This walks the views on disk: a view needs the
	/// time context when it calls DepartmentTime.From itself or renders a shared partial or view component that does.
	/// </summary>
	[TestFixture]
	public class DepartmentLocalTimeCoverageTests
	{
		private static string WebRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Web", "Resgrid.Web");
		}

		/// <summary>Shared partials and view components (by the name views use to render them) that need the time context, closed transitively.</summary>
		private static HashSet<string> SharedNeedingTime(string viewsRoot)
		{
			var shared = Path.Combine(viewsRoot, "Shared");
			var files = Directory.GetFiles(shared, "*.cshtml", SearchOption.AllDirectories)
				.ToDictionary(f => f, File.ReadAllText);

			string NameOf(string file)
			{
				var relative = Path.GetRelativePath(shared, file).Replace('\\', '/');
				// Components/SignatureRequests/Default.cshtml is rendered as the "SignatureRequests" component.
				var component = Regex.Match(relative, @"^Components/([^/]+)/");
				return component.Success ? component.Groups[1].Value : Path.GetFileNameWithoutExtension(file);
			}

			var needing = new HashSet<string>(files.Where(f => f.Value.Contains("DepartmentTime.From")).Select(f => NameOf(f.Key)), StringComparer.OrdinalIgnoreCase);
			bool grew;
			do
			{
				grew = false;
				foreach (var file in files)
				{
					var name = NameOf(file.Key);
					if (!needing.Contains(name) && needing.Any(n => References(file.Value, n)))
						grew = needing.Add(name) || grew;
				}
			} while (grew);

			return needing;
		}

		private static bool References(string view, string sharedName) =>
			Regex.IsMatch(view, "[\"/]" + Regex.Escape(sharedName) + "(\\.cshtml)?\"", RegexOptions.IgnoreCase);

		private static Type ControllerFor(Assembly web, string folder) =>
			web.GetTypes().FirstOrDefault(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract &&
				string.Equals(t.Name, folder + "Controller", StringComparison.Ordinal) &&
				(t.Namespace ?? string.Empty).Contains("Areas.User", StringComparison.Ordinal));

		[Test]
		public void every_user_area_controller_whose_views_need_department_time_should_carry_the_filter()
		{
			var viewsRoot = Path.Combine(WebRoot(), "Areas", "User", "Views");
			var shared = SharedNeedingTime(viewsRoot);
			shared.Should().Contain("_SignaturesList", "the scan should find the shared partials that read the time context");

			var web = typeof(DepartmentLocalTimeAttribute).Assembly;
			var missing = new List<string>();
			foreach (var folder in Directory.GetDirectories(viewsRoot).Select(Path.GetFileName).Where(f => f != "Shared"))
			{
				var needs = Directory.GetFiles(Path.Combine(viewsRoot, folder), "*.cshtml", SearchOption.AllDirectories)
					.Select(File.ReadAllText)
					.Any(v => v.Contains("DepartmentTime.From") || shared.Any(s => References(v, s)));
				if (!needs)
					continue;

				var controller = ControllerFor(web, folder);
				if (controller == null)
					continue; // views rendered by another controller by path; those controllers are covered by their own folders

				if (controller.GetCustomAttribute<DepartmentLocalTimeAttribute>(inherit: true) == null)
					missing.Add(controller.Name);
			}

			missing.Should().BeEmpty("a view of these controllers reads DepartmentTime.From(ViewData), which throws without [DepartmentLocalTime]");
		}

		[TestCase(typeof(Resgrid.Web.Areas.User.Controllers.KnowledgeController))]
		[TestCase(typeof(Resgrid.Web.Areas.User.Controllers.SignaturesController))]
		public void controllers_fixed_for_the_missing_time_context_should_carry_the_filter(Type controller)
		{
			controller.GetCustomAttribute<DepartmentLocalTimeAttribute>(inherit: true).Should().NotBeNull();
		}
	}
}
