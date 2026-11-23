using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Every key the dispatcher desk's web pages ask for (views, controllers, the script string tables, and the keys built from
	/// enum names) exists in DispatcherDesk.en.resx. A missing key renders as its own name; the completeness and parity tests
	/// then carry it into every language.
	/// </summary>
	[TestFixture]
	public class DispatcherDeskLocalizationTests
	{
		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		[Test]
		public void Dispatcher_desk_pages_resolve_real_resource_keys()
		{
			var root = RepositoryRoot();
			var user = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User");

			// File -> the name its DispatcherDesk localizer goes by there.
			var sources = Directory.GetFiles(Path.Combine(user, "Views", "DispatcherDesk"), "*.cshtml").ToDictionary(x => x, _ => "desk");
			foreach (var view in new[] { "_ShiftDispatchSection", "_ShiftDayDispatchers", "_OnDutyDispatchers", "NewShift", "EditShiftDetails", "Index" })
				sources[Path.Combine(user, "Views", "Shifts", view + ".cshtml")] = "desk";
			sources[Path.Combine(user, "Views", "Shared", "_DispatcherBreakPlanEditor.cshtml")] = "desk";
			sources[Path.Combine(user, "Views", "Shared", "_Navigation.cshtml")] = "dispatcherDeskLocalizer";
			sources[Path.Combine(user, "Controllers", "DispatcherDeskController.cs")] = "_localizer";
			sources[Path.Combine(user, "Controllers", "ShiftsController.cs")] = "_deskLocalizer";

			var keys = new HashSet<string>(StringComparer.Ordinal);

			foreach (var (file, name) in sources)
			{
				var source = System.IO.File.ReadAllText(file);

				foreach (Match match in Regex.Matches(source, @"(?<![A-Za-z_])" + Regex.Escape(name) + @"\[""([^""]+)""\]"))
				{
					if (!match.Groups[1].Value.EndsWith(".", StringComparison.Ordinal))
						keys.Add(match.Groups[1].Value);
				}

				// A key picked by a condition: localizer[ok ? "A" : "B"].
				foreach (Match match in Regex.Matches(source, Regex.Escape(name) + @"\[([^\]""]*\?[^\]]*)\]"))
				{
					foreach (Match literal in Regex.Matches(match.Groups[1].Value, @"""([^""]+)"""))
						keys.Add(literal.Groups[1].Value);
				}

				// The script string tables: var strings = new[] { "A", "B" }.ToDictionary(...).
				foreach (Match table in Regex.Matches(source, @"var strings = new\[\]\s*\{(.*?)\}\.ToDictionary", RegexOptions.Singleline))
				{
					foreach (Match literal in Regex.Matches(table.Groups[1].Value, @"""([^""]+)"""))
						keys.Add(literal.Groups[1].Value);
				}
			}

			// Keys built from enum names.
			foreach (var error in Enum.GetNames(typeof(DispatcherDeskErrors))) keys.Add("Error." + error);
			foreach (var state in Enum.GetNames(typeof(DispatcherDisplayStates))) keys.Add("State." + state);
			foreach (var type in Enum.GetNames(typeof(DispatcherBreakTypes))) keys.Add("BreakType." + type);
			foreach (var kind in Enum.GetNames(typeof(DispatcherQueueItemKinds))) keys.Add("Kind." + kind);
			foreach (var kind in Enum.GetNames(typeof(DispatcherDeskWarningKinds))) keys.Add("Warning." + kind);

			keys.Should().HaveCountGreaterThan(200, "the scan should see the pages' text");

			var english = XDocument.Load(Path.Combine(root, "Core", "Resgrid.Localization", "Areas", "User", "DispatcherDesk", "DispatcherDesk.en.resx"))
				.Root!.Elements("data").Select(x => (string)x.Attribute("name")).ToHashSet(StringComparer.Ordinal);

			keys.Where(x => !english.Contains(x)).OrderBy(x => x).Should().BeEmpty("every key the dispatcher desk pages use needs English text");
		}
	}
}
