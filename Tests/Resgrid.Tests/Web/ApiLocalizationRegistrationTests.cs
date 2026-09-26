using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NUnit.Framework;
using AdminAssistLabels = Resgrid.Localization.Areas.User.AdminAssist.AdminAssist;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The API host never registered localization, so every v4 controller taking an IStringLocalizer (Admin Assist among
	/// them) failed to construct and returned 500. The Setup Wizard and Setup Report showed only "The report could not be
	/// loaded" because their Catalog and Overview reads never reached the controller.
	/// </summary>
	[TestFixture]
	public class ApiLocalizationRegistrationTests
	{
		[Test]
		public void Api_startup_registers_localization_for_controllers_that_take_string_localizers()
		{
			var localized = typeof(Resgrid.Web.Services.Controllers.v4.AdminAssistController).Assembly.GetTypes()
				.Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
				.Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType.IsGenericType &&
					p.ParameterType.GetGenericTypeDefinition() == typeof(IStringLocalizer<>))))
				.Select(t => t.Name)
				.ToList();
			localized.Should().Contain("AdminAssistController");

			var startup = File.ReadAllText(FindRepositoryFile("Web/Resgrid.Web.Services/Startup.cs"));
			Regex.IsMatch(startup, @"^\s*services\.AddLocalization\(", RegexOptions.Multiline)
				.Should().BeTrue($"the API constructs {string.Join(", ", localized)} with an IStringLocalizer");
		}

		[TestCase("")]
		[TestCase("en")]
		[TestCase("de")]
		public void Admin_assist_catalog_strings_resolve_from_the_registered_localizer(string culture)
		{
			var previous = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
				// The host supplies logging; the localizer factory depends on it.
				using var provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
				var labels = provider.GetRequiredService<IStringLocalizer<AdminAssistLabels>>();

				// The same projection the Catalog endpoint returns to the page.
				var strings = labels.GetAllStrings(true).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().Value);

				strings.Should().ContainKey("Ui.Error");
				strings["Ui.Title"].Should().Be("Admin Assist");
			}
			finally { CultureInfo.CurrentUICulture = previous; }
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
