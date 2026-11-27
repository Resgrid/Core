using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Localization
{
	/// <summary>
	/// The workflow pages (list, New and Edit) name a trigger through <see cref="WorkflowTriggerNames"/>: a Workflows "Trigger.&lt;enum name&gt;"
	/// string first, then the strings of the module that owns the trigger. The list page used to show raw enum names for every trigger
	/// outside the content families, Edit missed three module families, and 91 older triggers (calls, personnel, shifts, incident command,
	/// run cards, Records, Admin Assist and recurring calls) had no name anywhere. Every trigger must now have a plain name in every language,
	/// and the three pages must share the one lookup.
	/// </summary>
	[TestFixture]
	public class WorkflowTriggerNameCoverageTests
	{
		private const int FirstContentTrigger = 218;
		private static readonly string[] Locales = { "", "en", "ar", "de", "el", "es", "fr", "it", "pl", "sv", "uk" };

		private static WorkflowTriggerEventType[] AllTriggers() =>
			Enum.GetValues(typeof(WorkflowTriggerEventType)).Cast<WorkflowTriggerEventType>().ToArray();

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		[TestCaseSource(nameof(Locales))]
		public void every_trigger_no_module_names_should_have_a_name_in_the_workflow_strings(string locale)
		{
			var file = Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Workflows",
				string.IsNullOrEmpty(locale) ? "Workflows.resx" : $"Workflows.{locale}.resx");
			var values = XDocument.Load(file).Root!.Elements("data")
				.ToDictionary(d => (string)d.Attribute("name"), d => (string)d.Element("value"));

			var missing = AllTriggers()
				.Where(t => (int)t >= FirstContentTrigger || WorkflowTriggerNames.ModuleString((int)t) == null)
				.Where(t => !values.TryGetValue("Trigger." + t, out var name) || string.IsNullOrWhiteSpace(name))
				.Select(t => $"{(int)t} {t}").ToList();

			missing.Should().BeEmpty("the workflow pages would show these triggers by their raw enum names");
		}

		[TestCase("en")]
		[TestCase("de")]
		[TestCase("ar")]
		public void every_trigger_should_resolve_to_a_plain_name(string culture)
		{
			var previous = CultureInfo.CurrentUICulture;
			try
			{
				CultureInfo.CurrentUICulture = new CultureInfo(culture);
				using var provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
				var factory = provider.GetRequiredService<IStringLocalizerFactory>();

				var raw = AllTriggers()
					.Select(t => (Trigger: t, Name: WorkflowTriggerNames.For(factory, (int)t)))
					.Where(x => string.IsNullOrWhiteSpace(x.Name) || x.Name == x.Trigger.ToString())
					.Select(x => $"{(int)x.Trigger} {x.Trigger}").ToList();

				raw.Should().BeEmpty("a trigger without a name shows its enum name on the workflow list, the picker and the edit page");
			}
			finally { CultureInfo.CurrentUICulture = previous; }
		}

		[TestCase("New.cshtml")]
		[TestCase("Index.cshtml")]
		[TestCase("Edit.cshtml")]
		public void workflow_pages_should_share_the_trigger_name_lookup(string view)
		{
			var text = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Views", "Workflows", view));

			text.Should().Contain("WorkflowTriggerNames.For(localizerFactory, ", "the page should name triggers through the shared lookup");
			text.Should().NotContain("localizer[\"Trigger.\" + ", "a page-local name chain drifts from the other pages");
		}
	}
}
