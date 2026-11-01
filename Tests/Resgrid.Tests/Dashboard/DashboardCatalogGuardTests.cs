using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard
{
	/// <summary>
	/// The extension contract (home-dashboard-redesign-plan.md §11, §13): every widget definition is well formed, has exactly
	/// one keyed provider, real gates (policies, flags), a client component, and its strings — so a half-added widget fails
	/// the build instead of rendering a broken card.
	/// </summary>
	[TestFixture]
	public class DashboardCatalogGuardTests
	{
		private static readonly Regex KeyFormat = new Regex(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)+$", RegexOptions.Compiled);

		private static IReadOnlyList<DashboardWidgetDefinition> All => DashboardWidgetCatalog.All;

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return directory!.FullName;
		}

		private static Dictionary<string, Type> ProviderTypes(Type contract, Func<Type, string> keyOf)
		{
			var types = typeof(ServicesModule).Assembly.GetTypes()
				.Where(t => contract.IsAssignableFrom(t) && !t.IsAbstract && keyOf(t) != null)
				.ToList();

			types.GroupBy(keyOf).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty("each key has exactly one implementation");
			return types.ToDictionary(keyOf, t => t, StringComparer.Ordinal);
		}

		private static Dictionary<string, Type> WidgetProviders() =>
			ProviderTypes(typeof(IDashboardWidgetDataProvider), t => t.GetCustomAttribute<DashboardWidgetAttribute>()?.Key);

		[Test]
		public void Keys_are_unique_and_well_formed()
		{
			All.Select(d => d.Key).Should().OnlyHaveUniqueItems();
			foreach (var definition in All)
				KeyFormat.IsMatch(definition.Key).Should().BeTrue($"'{definition.Key}' must be module.name in lower camel");

			All.Select(d => d.Key).Intersect(DashboardWidgetCatalog.RetiredKeys).Should().BeEmpty("a retired key is never reused");
		}

		[Test]
		public void Definitions_are_well_formed()
		{
			var sources = typeof(DashboardOptionSources).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)).ToList();

			foreach (var d in All)
			{
				d.AllowedSizes.Should().NotBeEmpty(d.Key);
				d.AllowedSizes.Should().OnlyContain(s => DashboardWidgetSizes.IsKnown(s), d.Key);
				d.AllowedSizes.Should().Contain(d.DefaultSize, $"{d.Key} default size must be allowed");
				d.RealtimeTopics.Should().OnlyContain(t => DashboardTopics.All.Contains(t), d.Key);
				d.DefaultRefreshSeconds.Should().BeGreaterThanOrEqualTo(d.MinRefreshSeconds, d.Key);
				d.Icon.Should().StartWith("fa-", d.Key);
				d.ModuleUrl.Should().StartWith("/", $"{d.Key} needs a module link");
				d.Settings.Select(s => s.Name).Should().OnlyHaveUniqueItems(d.Key);

				foreach (var setting in d.Settings)
				{
					if (setting.OptionsSource != null)
						sources.Should().Contain(setting.OptionsSource, $"{d.Key}.{setting.Name} draws from a known option source");

					if (setting.Type == DashboardSettingTypes.Select && setting.OptionsSource == null && setting.Default is string selected)
						setting.Options.Should().Contain(selected, $"{d.Key}.{setting.Name} default must be one of its options");
				}

				if (d.Gates.Addon.HasValue)
					d.Gates.Access.Should().NotBeEmpty($"{d.Key} is a paid widget: entitlement must come from the module's access service");
			}
		}

		[Test]
		public void Every_widget_has_exactly_one_provider_and_every_provider_a_widget()
		{
			var providers = WidgetProviders();

			All.Select(d => d.Key).Except(providers.Keys).Should().BeEmpty("every widget needs a [DashboardWidget] provider");
			providers.Keys.Except(All.Select(d => d.Key)).Should().BeEmpty("a provider without a catalog entry is unreachable");
		}

		[Test]
		public void Every_option_source_used_has_an_implementation()
		{
			var implemented = ProviderTypes(typeof(IDashboardOptionSource), t => t.GetCustomAttribute<DashboardOptionSourceAttribute>()?.Name);
			var used = All.SelectMany(d => d.Settings).Where(s => s.OptionsSource != null).Select(s => s.OptionsSource).Distinct();

			used.Except(implemented.Keys).Should().BeEmpty();
		}

		[Test]
		public void Gates_name_real_policies_and_flags()
		{
			var policies = typeof(ResgridResources).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()).ToHashSet(StringComparer.Ordinal);
			var flags = typeof(FeatureFlagKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()).ToHashSet(StringComparer.Ordinal);

			foreach (var d in All)
			{
				d.Gates.Policies.Should().OnlyContain(p => policies.Contains(p), $"{d.Key} policies must be ResgridResources names");
				d.Gates.Flags.Concat(d.Gates.AnyFlags).Should().OnlyContain(f => flags.Contains(f), $"{d.Key} flags must be FeatureFlagKeys");
			}

			policies.Should().Contain(DashboardEntitlementService.DispatcherPolicy);
		}

		[Test]
		public void Gate_policies_are_registered_by_the_web_host()
		{
			// A ResgridResources constant is not enough: AuthorizeAsync throws for a policy the host never added
			// (Chat_View and WeatherAlert_View exist as constants but the web app registers neither).
			var startup = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Startup.cs"));
			var registered = Regex.Matches(startup, @"AddPolicy\(ResgridResources\.(\w+)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

			foreach (var d in All)
				d.Gates.Policies.Where(p => !registered.Contains(p)).Should().BeEmpty($"{d.Key} gates on policies the web host must register");

			registered.Should().Contain(DashboardEntitlementService.DispatcherPolicy);
		}

		[Test]
		public void Built_in_layouts_only_use_catalog_widgets()
		{
			DashboardBuiltInLayouts.AllKeys.Where(k => DashboardWidgetCatalog.Find(k) == null).Should().BeEmpty();
		}

		[Test]
		public void Built_in_defaults_tile_without_holes()
		{
			// Sizes span fixed rows (dashboard.css), so a default that mixes 3- and 4-wide cards in one band leaves gaps the dense
			// flow can't close. Pack each default the way CSS grid-auto-flow: row dense does, with the spans read from the
			// stylesheet, and require every cell above the last card's bottom edge to be filled.
			var css = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Apps", "src", "components", "dashboard", "dashboard.css"));
			var spans = Regex.Matches(css, @"\.rg-w\[data-size='(\w+)'\]\s*\{\s*grid-column:\s*span (\d+);\s*grid-row:\s*span (\d+);")
				.GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => (Columns: int.Parse(g.First().Groups[2].Value), Rows: int.Parse(g.First().Groups[3].Value)));
			spans.Keys.Should().BeEquivalentTo(new[] { "xs", "s", "m", "l", "xl", "full" }, "dashboard.css defines the 12-column spans of every size");

			foreach (var audience in Enum.GetValues(typeof(DashboardAudiences)).Cast<DashboardAudiences>())
			{
				var layout = DashboardBuiltInLayouts.For(audience);
				var filled = new List<bool[]>();
				var bottom = 0;

				foreach (var widget in layout.Widgets)
				{
					var (columns, rows) = spans[widget.Size];
					for (var row = 0; ; row++)
					{
						var column = Enumerable.Range(0, 13 - columns).Cast<int?>().FirstOrDefault(c => Fits(filled, row, c.Value, columns, rows));
						if (column == null)
							continue;

						for (var r = row; r < row + rows; r++)
							for (var c = column.Value; c < column.Value + columns; c++)
								Row(filled, r)[c] = true;

						bottom = Math.Max(bottom, row + rows);
						break;
					}
				}

				var holes = Enumerable.Range(0, bottom).SelectMany(r => Enumerable.Range(0, 12).Where(c => !Row(filled, r)[c]).Select(c => $"row {r + 1} col {c + 1}")).ToList();
				holes.Should().BeEmpty($"the {audience} default should tile the 12-column grid");
			}

			static bool[] Row(List<bool[]> grid, int row)
			{
				while (grid.Count <= row)
					grid.Add(new bool[12]);
				return grid[row];
			}

			static bool Fits(List<bool[]> grid, int row, int column, int columns, int rows)
			{
				for (var r = row; r < row + rows; r++)
					for (var c = column; c < column + columns; c++)
						if (Row(grid, r)[c])
							return false;
				return true;
			}
		}

		[Test]
		public void Every_widget_has_its_strings()
		{
			var path = Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Home", "DashboardWidgets.en.resx");
			var keys = XDocument.Load(path).Root!.Elements("data").Select(x => (string)x.Attribute("name")).ToHashSet(StringComparer.Ordinal);

			var missing = new List<string>();
			foreach (var d in All)
			{
				missing.AddRange(new[] { d.TitleResourceKey, d.DescriptionResourceKey }.Where(k => !keys.Contains(k)));
				foreach (var setting in d.Settings)
				{
					var label = $"W.{d.Key}.Setting.{setting.Name}";
					if (!keys.Contains(label))
						missing.Add(label);

					missing.AddRange(setting.Options.Select(o => $"W.{d.Key}.Option.{o}").Where(k => !keys.Contains(k)));
				}
			}

			missing.Should().BeEmpty("titles, descriptions, setting labels and option labels live in DashboardWidgets.en.resx");
		}

		[Test]
		public void Every_widget_has_exactly_one_client_component()
		{
			var widgetsRoot = Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Apps", "src", "components", "dashboard", "widgets");
			Directory.Exists(widgetsRoot).Should().BeTrue("the rg-dashboard widget registry lives in Apps/src/components/dashboard/widgets");

			var registered = Directory.GetFiles(widgetsRoot, "index.ts", SearchOption.AllDirectories)
				.SelectMany(f => Regex.Matches(System.IO.File.ReadAllText(f), @"^\s*'([a-z][a-zA-Z0-9]*(?:\.[a-z][a-zA-Z0-9]*)+)'\s*:", RegexOptions.Multiline).Select(m => m.Groups[1].Value))
				.ToList();

			registered.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty("a key is registered once");
			All.Select(d => d.Key).Except(registered).Should().BeEmpty("every widget needs a React component in widgets/<category>/index.ts");
			registered.Except(All.Select(d => d.Key)).Should().BeEmpty("a component without a catalog entry is dead code");
		}

		[Test]
		public void Protected_fields_name_catalogued_adp_fields()
		{
			var catalog = new ProtectedFieldCatalog();

			foreach (var d in All)
				foreach (var field in d.ProtectedFields)
				{
					var parts = field.FieldId.Split('.');
					parts.Should().HaveCount(2, d.Key);
					catalog.GetAll().Should().Contain(f => string.Equals(f.FieldId, field.FieldId, StringComparison.OrdinalIgnoreCase),
						$"{d.Key} names {field.FieldId}, which must be in ProtectedFieldCatalog");
				}
		}
	}
}
