using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model.Dashboard;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard
{
	/// <summary>Layout normalization (home-dashboard-redesign-plan.md §5.1): bounds, ids, sizes, settings, preserved-unknown keys.</summary>
	[TestFixture]
	public class DashboardLayoutValidatorTests
	{
		private static DashboardWidgetDefinition Definition() => new DashboardWidgetDefinition
		{
			Key = "test.widget",
			AllowedSizes = new[] { DashboardWidgetSizes.S, DashboardWidgetSizes.M },
			DefaultSize = DashboardWidgetSizes.M,
			Settings = new[]
			{
				new DashboardWidgetSetting { Name = "rows", Type = DashboardSettingTypes.Number, Default = 6, Min = 3, Max = 12 },
				new DashboardWidgetSetting { Name = "hide", Type = DashboardSettingTypes.Toggle, Default = false },
				new DashboardWidgetSetting { Name = "mode", Type = DashboardSettingTypes.Select, Default = "a", Options = new[] { "a", "b" } },
				new DashboardWidgetSetting { Name = "groups", Type = DashboardSettingTypes.MultiSelect, OptionsSource = DashboardOptionSources.Groups },
				new DashboardWidgetSetting { Name = "note", Type = DashboardSettingTypes.Text, Max = 5 }
			}
		};

		[Test]
		public void Normalize_rejects_a_future_schema_and_too_many_widgets()
		{
			DashboardLayoutValidator.Normalize(new DashboardLayout { SchemaVersion = DashboardLayout.CurrentSchemaVersion + 1 }).Should().BeNull();

			var crowded = new DashboardLayout();
			for (var i = 0; i <= DashboardLayout.MaxWidgets; i++)
				crowded.Widgets.Add(new DashboardLayoutWidget { Id = $"w{i}", Key = "department.info" });

			DashboardLayoutValidator.Normalize(crowded).Should().BeNull();
		}

		[Test]
		public void Normalize_preserves_unknown_keys_verbatim_but_bounded()
		{
			var layout = new DashboardLayout
			{
				Widgets =
				{
					new DashboardLayoutWidget { Id = "x1", Key = "retired.widget", Size = "l", Settings = new Dictionary<string, object> { { "keep", "yes" } } }
				}
			};

			var result = DashboardLayoutValidator.Normalize(layout);

			result.Widgets.Should().ContainSingle();
			result.Widgets[0].Key.Should().Be("retired.widget");
			result.Widgets[0].Size.Should().Be("l");
			result.Widgets[0].Settings["keep"].Should().Be("yes");
		}

		[Test]
		public void Normalize_replaces_duplicate_or_bad_ids_and_drops_second_single_instance()
		{
			var layout = new DashboardLayout
			{
				Widgets =
				{
					new DashboardLayoutWidget { Id = "same", Key = "retired.a" },
					new DashboardLayoutWidget { Id = "same", Key = "retired.b" },
					new DashboardLayoutWidget { Id = "<script>", Key = "retired.c" },
					new DashboardLayoutWidget { Id = "d1", Key = "department.info" },
					new DashboardLayoutWidget { Id = "d2", Key = "department.info" }
				}
			};

			var result = DashboardLayoutValidator.Normalize(layout);

			result.Widgets.Select(w => w.Id).Should().OnlyHaveUniqueItems();
			result.Widgets.Should().NotContain(w => w.Id == "<script>");
			result.Widgets.Count(w => w.Key == "department.info").Should().Be(1, "department.info does not support multiple instances");
		}

		[Test]
		public void Normalize_falls_back_to_the_default_size_when_not_allowed()
		{
			var result = DashboardLayoutValidator.Normalize(new DashboardLayout
			{
				Widgets = { new DashboardLayoutWidget { Id = "d1", Key = "department.info", Size = DashboardWidgetSizes.Full } }
			});

			result.Widgets[0].Size.Should().Be(DashboardWidgetCatalog.Find("department.info").DefaultSize);
		}

		[Test]
		public void NormalizeSettings_keeps_only_the_allow_list_typed_and_clamped()
		{
			var settings = DashboardLayoutValidator.NormalizeSettings(Definition(), new Dictionary<string, object>
			{
				{ "rows", 99 },
				{ "hide", "true" },
				{ "mode", "z" },
				{ "groups", new[] { "1", "2", "2" } },
				{ "note", "too long text" },
				{ "injected", "nope" }
			});

			settings["rows"].Should().Be(12);
			settings["hide"].Should().Be(true);
			settings.Should().NotContainKey("mode", "z is not one of the fixed options");
			((string[])settings["groups"]).Should().Equal("1", "2");
			settings["note"].Should().Be("too l");
			settings.Should().NotContainKey("injected");
		}

		[Test]
		public void NormalizeSettings_unwraps_System_Text_Json_and_Newtonsoft_values()
		{
			var stj = System.Text.Json.JsonDocument.Parse("{\"rows\":4,\"hide\":true,\"groups\":[\"7\"]}").RootElement;
			var fromStj = DashboardLayoutValidator.NormalizeSettings(Definition(), new Dictionary<string, object>
			{
				{ "rows", stj.GetProperty("rows") },
				{ "hide", stj.GetProperty("hide") },
				{ "groups", stj.GetProperty("groups") }
			});

			fromStj["rows"].Should().Be(4);
			fromStj["hide"].Should().Be(true);
			((string[])fromStj["groups"]).Should().Equal("7");

			var fromNewtonsoft = DashboardLayoutValidator.NormalizeSettings(Definition(), new Dictionary<string, object>
			{
				{ "rows", new JValue(5L) },
				{ "mode", new JValue("b") }
			});

			fromNewtonsoft["rows"].Should().Be(5);
			fromNewtonsoft["mode"].Should().Be("b");
		}

		[Test]
		public void WithDefaults_overlays_stored_values_on_defaults()
		{
			var effective = DashboardLayoutValidator.WithDefaults(Definition(), new Dictionary<string, object> { { "rows", 8 } });

			effective["rows"].Should().Be(8);
			effective["hide"].Should().Be(false);
			effective["mode"].Should().Be("a");
		}

		[Test]
		public void Deserialize_round_trips_and_tolerates_garbage()
		{
			var layout = new DashboardLayout { Density = DashboardDensities.Compact, Widgets = { new DashboardLayoutWidget { Id = "a", Key = "department.info", Size = "s" } } };
			var json = DashboardLayoutValidator.Serialize(layout);

			DashboardLayoutValidator.Deserialize(json).Widgets.Single().Key.Should().Be("department.info");
			DashboardLayoutValidator.Deserialize("{not json").Should().BeNull();
			DashboardLayoutValidator.Deserialize(null).Should().BeNull();
		}

		[Test]
		public void Built_in_layouts_normalize_without_losing_known_widgets()
		{
			foreach (var audience in Enum.GetValues(typeof(DashboardAudiences)).Cast<DashboardAudiences>())
			{
				var raw = DashboardBuiltInLayouts.For(audience);
				var normalized = DashboardLayoutValidator.Normalize(raw);

				normalized.Should().NotBeNull();
				normalized.Widgets.Select(w => w.Id).Should().Equal(raw.Widgets.Select(w => w.Id), $"{audience} keeps its fixed instance ids");
			}
		}
	}
}
