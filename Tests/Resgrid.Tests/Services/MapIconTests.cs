using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.Mapping;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Map icons that follow status and priority (M0289): the style groups and colours, the pin and icon-slot names, the
	/// resolver every map, the legend and the icon pages share, the server-drawn styled pins and badges, and the legend rows.
	/// </summary>
	[TestFixture]
	public class MapIconTests
	{
		private const int DepartmentId = 7;

		#region Styles

		[Test]
		public void Every_status_meaning_but_none_has_a_style_and_every_style_a_colour()
		{
			foreach (var baseType in Enum.GetValues(typeof(ActionBaseTypes)).Cast<ActionBaseTypes>())
			{
				if (baseType == ActionBaseTypes.None)
					MapIconStyles.ForStatusBaseType((int)baseType).Should().BeNull();
				else
					MapIconStyles.ForStatusBaseType((int)baseType).Should().NotBeNull(baseType.ToString());
			}

			foreach (var style in MapIconStyles.StatusStyles.Concat(MapIconStyles.PriorityStyles))
			{
				MapIconStyles.ColorOf(style).Should().MatchRegex("^#[0-9A-F]{6}$", style);
				MapIconStyles.IsStyle(style).Should().BeTrue();
			}

			MapIconStyles.StatusStyles.Should().OnlyContain(s => MapIconStyles.BaseTypesOf(s).Any(), "every status style groups at least one meaning");
			MapIconStyles.ColorOf(MapIconStyles.Unknown).Should().MatchRegex("^#[0-9A-F]{6}$");
			TypeIcons.TryParsePinName("person-unknown", out _, out _).Should().BeTrue();
			MapIconStyles.IsStyle("purple").Should().BeFalse();
			MapIconStyles.IsStyle(null).Should().BeFalse();
		}

		[Test]
		public void Statuses_group_into_the_styles_a_dispatcher_reads()
		{
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.InQuarters).Should().Be(MapIconStyles.Available);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.Enroute).Should().Be(MapIconStyles.Responding);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.AtPatient).Should().Be(MapIconStyles.OnScene);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.AtHospital).Should().Be(MapIconStyles.Transporting);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.Standby).Should().Be(MapIconStyles.Staging);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.OnBreak).Should().Be(MapIconStyles.Delayed);
			MapIconStyles.ForStatusBaseType((int)ActionBaseTypes.NotResponding).Should().Be(MapIconStyles.Unavailable);
			MapIconStyles.ForStatusBaseType(999).Should().BeNull();
		}

		[Test]
		public void Priorities_map_by_their_meaning()
		{
			MapIconStyles.ForPriorityBaseType((int)CallPriority.Low).Should().Be(MapIconStyles.PriorityLow);
			MapIconStyles.ForPriorityBaseType((int)CallPriority.Emergency).Should().Be(MapIconStyles.PriorityEmergency);
			MapIconStyles.ForPriorityBaseType(-1).Should().BeNull();
		}

		#endregion Styles

		#region Names and URLs

		[Test]
		public void Pin_names_round_trip_and_unknown_ones_are_refused()
		{
			TypeIcons.PinName("fire", MapIconStyles.PriorityHigh).Should().Be("fire-high");
			TypeIcons.TryParsePinName("ambulance-responding", out var pinBase, out var style).Should().BeTrue();
			pinBase.Should().Be("ambulance");
			style.Should().Be("responding");

			foreach (var bad in new[] { null, "", "ambulance", "-responding", "ambulance-", "ambulance-purple", "spaceship-responding", "../engine-responding", "ENGINE-responding" })
				TypeIcons.TryParsePinName(bad, out _, out _).Should().BeFalse(bad ?? "null");
		}

		[Test]
		public void Every_built_in_call_and_unit_icon_and_the_defaults_can_be_styled()
		{
			TypeIcons.PinBases.Should().Contain(new[] { TypeIcons.CallPinBase, TypeIcons.UnitPinBase, TypeIcons.PersonPinBase, "fire", "cartwo", "watercraft" });
			TypeIcons.PinBases.Should().HaveCount(43);
			TypeIcons.PinBases.Should().OnlyContain(b => MapIconPinRenderer.HasArtwork(b), "the renderer ships the artwork of every pin");
		}

		[Test]
		public void Pin_and_badge_urls_carry_the_palette_version()
		{
			TypeIcons.ApiPinUrl("https://api.example.test/", "fire-high").Should().Be($"https://api.example.test/api/v4/TypeIcons/Pins/fire-high?v={MapIconStyles.Version}");
			TypeIcons.WebPinUrl("person-onscene").Should().Be($"/TypeIcons/Pins/person-onscene?v={MapIconStyles.Version}");
			TypeIcons.ApiPinUrl("https://api.example.test", null).Should().BeNull();

			TypeIcons.ApiIconUrl("https://api.example.test", "abc", MapIconStyles.Responding).Should().Be($"https://api.example.test/api/v4/TypeIcons/abc?s=responding&v={MapIconStyles.Version}");
			TypeIcons.ApiIconUrl("https://api.example.test", "abc", null).Should().Be("https://api.example.test/api/v4/TypeIcons/abc", "no style, no badge");
			TypeIcons.WebIconUrl("abc", "nonsense").Should().Be("/TypeIcons/abc", "an unknown style is never put in a URL");
			TypeIcons.WebIconUrl(null, MapIconStyles.Responding).Should().BeNull();
		}

		[TestCase(TypeIconKinds.CallType, "p0", true)]
		[TestCase(TypeIconKinds.CallType, "p4410", true)]
		[TestCase(TypeIconKinds.CallType, "s1", false)]
		[TestCase(TypeIconKinds.UnitType, "s0", true)]
		[TestCase(TypeIconKinds.UnitType, "o12", true)]
		[TestCase(TypeIconKinds.UnitType, "o0", false)]
		[TestCase(TypeIconKinds.UnitType, "p1", false)]
		[TestCase(TypeIconKinds.PersonnelRole, "s7", true)]
		[TestCase(TypeIconKinds.PersonnelRole, "o3", true)]
		[TestCase(TypeIconKinds.PoiType, "s1", false)]
		[TestCase(TypeIconKinds.UnitType, "s01", false)]
		[TestCase(TypeIconKinds.UnitType, "s1234567890", false)]
		[TestCase(TypeIconKinds.UnitType, "s-1", false)]
		[TestCase(TypeIconKinds.UnitType, "", false)]
		public void Icon_slots_are_valid_only_for_their_kind(TypeIconKinds kind, string variant, bool valid)
		{
			TypeIcons.IsValidVariant(kind, variant).Should().Be(valid);
		}

		#endregion Names and URLs

		#region Resolver

		private static readonly CallType FireType = new CallType { CallTypeId = 11, DepartmentId = DepartmentId, Type = "Structure Fire", MapIconType = (int)MapIconTypes.Fire };
		private static readonly CallType MedicalType = new CallType { CallTypeId = 12, DepartmentId = DepartmentId, Type = "Medical", IconKey = "medical-own" };
		private static readonly UnitType AmbulanceType = new UnitType { UnitTypeId = 21, DepartmentId = DepartmentId, Type = "Ambulance", MapIconType = (int)MapIconTypes.Ambulance, CustomStatesId = 500 };
		private static readonly UnitType MugType = new UnitType { UnitTypeId = 22, DepartmentId = DepartmentId, Type = "MUG", IconKey = "mug-own" };

		private static List<CustomState> CustomStates() => new List<CustomState>
		{
			new CustomState
			{
				CustomStateId = 500, DepartmentId = DepartmentId, Type = (int)CustomStateTypes.Unit, Name = "Ambulance statuses",
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 5001, CustomStateId = 500, ButtonText = "Free", BaseType = (int)ActionBaseTypes.Available },
					new CustomStateDetail { CustomStateDetailId = 5002, CustomStateId = 500, ButtonText = "To hospital", BaseType = (int)ActionBaseTypes.Transporting },
					new CustomStateDetail { CustomStateDetailId = 5003, CustomStateId = 500, ButtonText = "Spare", BaseType = (int)ActionBaseTypes.None }
				}
			},
			new CustomState
			{
				CustomStateId = 600, DepartmentId = DepartmentId, Type = (int)CustomStateTypes.Personnel, Name = "People",
				Details = new List<CustomStateDetail>
				{
					new CustomStateDetail { CustomStateDetailId = 6001, CustomStateId = 600, ButtonText = "Ready", BaseType = (int)ActionBaseTypes.Available },
					new CustomStateDetail { CustomStateDetailId = 6002, CustomStateId = 600, ButtonText = "En route", BaseType = (int)ActionBaseTypes.Responding }
				}
			}
		};

		private static MapIconResolver Resolver(IEnumerable<DepartmentCallPriority> priorities = null, IEnumerable<PersonnelRole> roles = null,
			IEnumerable<TypeIconVariant> variants = null, CustomStateDetail defaultStatus = null, IEnumerable<UnitState> unitStates = null) =>
			new MapIconResolver(DepartmentId, new[] { FireType, MedicalType }, priorities, new[] { AmbulanceType, MugType }, CustomStates(), roles, variants, defaultStatus, unitStates);

		[Test]
		public void A_call_takes_its_types_pin_in_the_style_of_its_built_in_priority()
		{
			var icon = Resolver().ForCall(new Call { CallTypeId = FireType.CallTypeId, Type = FireType.Type, Priority = (int)CallPriority.High });

			icon.ImagePath.Should().Be("fire");
			icon.Style.Should().Be(MapIconStyles.PriorityHigh);
			icon.PinName.Should().Be("fire-high");
			icon.Variant.Should().Be("p2");
			icon.IconKey.Should().BeNull();
		}

		[Test]
		public void A_call_with_no_type_takes_the_default_call_pin()
		{
			var icon = Resolver().ForCall(new Call { Priority = (int)CallPriority.Emergency });

			icon.ImagePath.Should().Be(TypeIcons.LegacyCallImage);
			icon.PinName.Should().Be("call-emergency");
		}

		[Test]
		public void A_departments_own_priority_uses_its_meaning_and_one_without_keeps_the_plain_pin()
		{
			var priorities = new[]
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 901, DepartmentId = DepartmentId, Name = "P1", BaseType = (int)CallPriority.Emergency },
				new DepartmentCallPriority { DepartmentCallPriorityId = 902, DepartmentId = DepartmentId, Name = "Info", BaseType = -1 },
				// The oldest departments own ids 0 to 3; their own row wins over the built-in of the same value.
				new DepartmentCallPriority { DepartmentCallPriorityId = 2, DepartmentId = DepartmentId, Name = "Routine", BaseType = (int)CallPriority.Low }
			};
			var resolver = Resolver(priorities);

			resolver.ForCallType(FireType, 901).PinName.Should().Be("fire-emergency");
			resolver.ForCallType(FireType, 902).PinName.Should().Be("fire-unknown", "a priority with no meaning, whose name suggests none, is gray");
			resolver.ForCallType(FireType, 902).ImagePath.Should().Be("fire");
			resolver.ForCallType(FireType, 2).Style.Should().Be(MapIconStyles.PriorityLow);
			resolver.ForCallType(FireType, 3).Style.Should().Be(MapIconStyles.PriorityEmergency, "an old call keeps the built-in priority it was given");
			resolver.ForCallType(FireType, 7777).Style.Should().Be(MapIconStyles.Unknown);
		}

		[Test]
		public void An_uploaded_priority_icon_wins_and_the_types_own_icon_carries_a_badge_otherwise()
		{
			var resolver = Resolver(variants: new[] { new TypeIconVariant { TypeKind = (int)TypeIconKinds.CallType, TypeId = MedicalType.CallTypeId, Variant = "p3", IconKey = "medical-emergency" } });

			var emergency = resolver.ForCallType(MedicalType, (int)CallPriority.Emergency);
			emergency.IconKey.Should().Be("medical-emergency");
			emergency.IconIsVariant.Should().BeTrue();
			emergency.BadgeStyle.Should().BeNull("an icon made for the priority is shown as uploaded");

			var low = resolver.ForCallType(MedicalType, (int)CallPriority.Low);
			low.IconKey.Should().Be("medical-own");
			low.IconIsVariant.Should().BeFalse();
			low.BadgeStyle.Should().Be(MapIconStyles.PriorityLow);
			low.PinName.Should().Be("call-low", "the styled pin stays the fallback for clients that cannot load the upload");

			resolver.ForCallType(MedicalType, null).BadgeStyle.Should().BeNull("no priority, no badge");
			resolver.ForCallType(MedicalType, 7777).BadgeStyle.Should().BeNull("an unknown priority leaves the upload as it is");
			resolver.ForCallType(MedicalType, 7777).PinName.Should().Be("call-unknown");
		}

		[Test]
		public void A_unit_follows_its_custom_status_options_meaning()
		{
			var resolver = Resolver();

			var transporting = resolver.ForUnit(new Unit { Type = "Ambulance" }, new UnitState { State = 5002 });
			transporting.ImagePath.Should().Be("ambulance");
			transporting.Variant.Should().Be("o5002");
			transporting.PinName.Should().Be("ambulance-transporting");

			resolver.ForUnit(new Unit { Type = "Ambulance" }, new UnitState { State = 5003 }).PinName.Should().Be("ambulance-unknown", "an option with no meaning and a name that suggests none is gray");
		}

		[Test]
		public void An_option_or_priority_with_no_meaning_set_is_coloured_by_what_its_name_suggests()
		{
			var states = CustomStates();
			states[0].Details.Add(new CustomStateDetail { CustomStateDetailId = 5004, CustomStateId = 500, ButtonText = "Out of Service", BaseType = (int)ActionBaseTypes.None });
			states[1].Details.Add(new CustomStateDetail { CustomStateDetailId = 6003, CustomStateId = 600, ButtonText = "On Scene", BaseType = (int)ActionBaseTypes.None });
			// The oldest departments own priorities 0 to 3 named like the built-ins, with no meaning set.
			var priorities = new[] { new DepartmentCallPriority { DepartmentCallPriorityId = 3, DepartmentId = DepartmentId, Name = "Emergency", BaseType = -1 } };
			var resolver = new MapIconResolver(DepartmentId, new[] { FireType }, priorities, new[] { AmbulanceType }, states, null, null);

			resolver.ForUnit(new Unit { Type = "Ambulance" }, new UnitState { State = 5004 }).Style.Should().Be(MapIconStyles.Unavailable);
			resolver.ForPerson(null, new ActionLog { ActionTypeId = 6003 }).Style.Should().Be(MapIconStyles.OnScene);
			resolver.ForCallType(FireType, 3).PinName.Should().Be("fire-emergency");
		}

		[Test]
		public void A_unit_in_a_built_in_status_uses_the_built_in_meaning_and_one_with_no_type_the_default_pin()
		{
			var icon = Resolver().ForUnit(new Unit { Type = "Unknown type" }, new UnitState { State = (int)UnitStateTypes.OnScene });

			icon.ImagePath.Should().Be(TypeIcons.LegacyUnitImage);
			icon.Variant.Should().Be("s6");
			icon.PinName.Should().Be("engine-onscene");

			Resolver().ForUnit(new Unit { Type = "Ambulance" }, null).PinName.Should().Be("ambulance-unknown", "a unit that never set a status is gray, never the red default");
			Resolver().ForUnitType(AmbulanceType, null).Style.Should().BeNull("the type's own icon, with no status, has no style");
			Resolver().ForUnitType(null, (int)UnitStateTypes.OutOfService).PinName.Should().Be("engine-unavailable");
		}

		[Test]
		public void An_uploaded_status_icon_replaces_the_unit_types_own_icon_for_that_status_only()
		{
			var resolver = Resolver(variants: new[] { new TypeIconVariant { TypeKind = (int)TypeIconKinds.UnitType, TypeId = MugType.UnitTypeId, Variant = "s5", IconKey = "mug-responding" } });

			resolver.ForUnitType(MugType, (int)UnitStateTypes.Responding).IconKey.Should().Be("mug-responding");
			var available = resolver.ForUnitType(MugType, (int)UnitStateTypes.Available);
			available.IconKey.Should().Be("mug-own");
			available.BadgeStyle.Should().Be(MapIconStyles.Available);
			resolver.VariantIconKey(TypeIconKinds.UnitType, MugType.UnitTypeId, "s5").Should().Be("mug-responding");
			resolver.VariantIconKey(TypeIconKinds.UnitType, AmbulanceType.UnitTypeId, "s5").Should().BeNull();
		}

		[Test]
		public void A_member_takes_the_icon_of_their_first_role_in_order_with_one_for_the_status_or_of_its_own()
		{
			var officer = new PersonnelRole { PersonnelRoleId = 1, DepartmentId = DepartmentId, Name = "Officer", SortOrder = 1 };
			var paramedic = new PersonnelRole { PersonnelRoleId = 2, DepartmentId = DepartmentId, Name = "Paramedic", SortOrder = 2, IconKey = "paramedic-own" };
			var firefighter = new PersonnelRole { PersonnelRoleId = 3, DepartmentId = DepartmentId, Name = "Firefighter", SortOrder = 3, IconKey = "firefighter-own" };
			var variants = new[] { new TypeIconVariant { TypeKind = (int)TypeIconKinds.PersonnelRole, TypeId = officer.PersonnelRoleId, Variant = "o6002", IconKey = "officer-enroute" } };
			var resolver = Resolver(roles: new[] { officer, paramedic, firefighter }, variants: variants);

			var enRoute = resolver.ForPerson(new List<int> { 3, 1, 2 }, new ActionLog { ActionTypeId = 6002 });
			enRoute.IconKey.Should().Be("officer-enroute", "the officer role comes first and has an icon for this status");
			enRoute.Style.Should().Be(MapIconStyles.Responding);

			var ready = resolver.ForPerson(new List<int> { 3, 1, 2 }, new ActionLog { ActionTypeId = 6001 });
			ready.IconKey.Should().Be("paramedic-own", "the officer role has no icon for Ready and none of its own");
			ready.BadgeStyle.Should().Be(MapIconStyles.Available);

			var noRoleIcon = resolver.ForPerson(new List<int>(), new ActionLog { ActionTypeId = (int)ActionTypes.Responding });
			noRoleIcon.IconKey.Should().BeNull();
			noRoleIcon.ImagePath.Should().Be(TypeIcons.LegacyPersonImage);
			noRoleIcon.PinName.Should().Be("person-responding");
		}

		[Test]
		public void A_member_On_Unit_takes_the_style_of_the_units_latest_status()
		{
			var unitStates = new[] { new UnitState { UnitStateId = 70, UnitId = 4, State = 5002 } };
			var resolver = Resolver(unitStates: unitStates);

			var onUnit = resolver.ForPerson(null, new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 70 });
			onUnit.Variant.Should().Be("s7");
			onUnit.Style.Should().Be(MapIconStyles.Transporting);

			resolver.ForPerson(null, new ActionLog { ActionTypeId = (int)ActionTypes.OnUnit, UnitStateId = 71 }).Style.Should().Be(MapIconStyles.Unknown, "the unit has moved on since");
		}

		[Test]
		public void A_member_with_no_status_shows_the_departments_default_status()
		{
			Resolver(defaultStatus: new CustomStateDetail { CustomStateDetailId = 6001, BaseType = (int)ActionBaseTypes.Available })
				.ForPerson(null, null).Variant.Should().Be("o6001");
			Resolver(defaultStatus: new CustomStateDetail { CustomStateDetailId = 0, BaseType = (int)ActionBaseTypes.Available })
				.ForPerson(null, null).PinName.Should().Be("person-available");
			Resolver().ForPerson(null, null).Style.Should().Be(MapIconStyles.Unknown);
		}

		#endregion Resolver

		#region Renderer

		[Test]
		public void A_styled_pin_keeps_the_artworks_size_and_takes_the_styles_colour()
		{
			var data = MapIconPinRenderer.RenderPin("ambulance-responding");

			data.Should().NotBeNull();
			using var image = Image.Load<Rgba32>(data);
			image.Width.Should().Be(32);
			image.Height.Should().Be(37);

			var target = Hex(MapIconStyles.ColorOf(MapIconStyles.Responding));
			var body = MostCommonOpaqueColour(image);
			Math.Abs(body.R - target.R).Should().BeLessThan(4);
			Math.Abs(body.G - target.G).Should().BeLessThan(4);
			Math.Abs(body.B - target.B).Should().BeLessThan(4);
			image[16, 36].A.Should().BeGreaterThan(0, "the bottom tip, the marker's anchor, is still drawn");

			MapIconPinRenderer.RenderPin("ambulance-responding").Should().BeSameAs(data, "pins are drawn once per process");
			MapIconPinRenderer.RenderPin("ambulance-purple").Should().BeNull();
			MapIconPinRenderer.RenderPin("../../secrets-responding").Should().BeNull();
		}

		[Test]
		public void A_badge_is_drawn_in_the_lower_right_corner_and_leaves_the_rest_of_the_icon()
		{
			byte[] icon;
			using (var source = new Image<Rgba32>(128, 128, new Rgba32(0, 0, 0, 0)))
			using (var stream = new MemoryStream())
			{
				source[20, 20] = new Rgba32(10, 200, 10, 255);
				source.Save(stream, new PngEncoder());
				icon = stream.ToArray();
			}

			var data = MapIconPinRenderer.RenderBadge(icon, MapIconStyles.Unavailable, "checksum-a");

			using var image = Image.Load<Rgba32>(data);
			image.Width.Should().Be(128);
			image[20, 20].Should().Be(new Rgba32(10, 200, 10, 255));
			var centre = (int)(128 - 128 * 0.19 - 1);
			image[centre, centre].Should().Be(Hex(MapIconStyles.ColorOf(MapIconStyles.Unavailable)));
			MapIconPinRenderer.RenderBadge(icon, "nonsense", "checksum-a").Should().BeNull();
			MapIconPinRenderer.RenderBadge(new byte[] { 1, 2, 3 }, MapIconStyles.Available, "bad").Should().BeNull();
		}

		private static Rgba32 Hex(string hex) => new Rgba32(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), 255);

		private static Rgba32 MostCommonOpaqueColour(Image<Rgba32> image)
		{
			var counts = new Dictionary<Rgba32, int>();
			for (var y = 0; y < image.Height; y++)
				for (var x = 0; x < image.Width; x++)
				{
					var pixel = image[x, y];
					if (pixel.A == 255 && Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) < 200)
						counts[pixel] = counts.TryGetValue(pixel, out var count) ? count + 1 : 1;
				}

			return counts.OrderByDescending(x => x.Value).First().Key;
		}

		#endregion Renderer

		#region Legend

		[Test]
		public void A_unit_types_legend_row_lists_its_own_status_set_else_the_built_in_statuses()
		{
			var builtIns = new List<CustomStateDetail> { new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonText = "Available", ButtonColor = "#d1dade" } };

			var ambulance = MapIconLegendBuilder.UnitRow(Resolver(), AmbulanceType, null, CustomStates(), builtIns);
			ambulance.StatusSetName.Should().Be("Ambulance statuses");
			ambulance.States.Select(s => s.Name).Should().Equal("Free", "To hospital", "Spare");
			ambulance.States[1].Icon.Url.Should().Be(TypeIcons.WebPinUrl("ambulance-transporting"));
			ambulance.States[2].Icon.Url.Should().Be(TypeIcons.WebPinUrl("ambulance-unknown"), "no meaning and no suggestion, gray pin");

			var mug = MapIconLegendBuilder.UnitRow(Resolver(), MugType, null, CustomStates(), builtIns);
			mug.StatusSetName.Should().BeNull();
			mug.States.Single().Icon.Url.Should().Be(TypeIcons.WebIconUrl("mug-own", MapIconStyles.Available));
			mug.States.Single().Icon.IsUploaded.Should().BeTrue();
			mug.States.Single().Color.Should().Be("#d1dade");
		}

		[Test]
		public void The_icon_page_shows_what_each_status_falls_back_to_without_an_upload()
		{
			var resolver = Resolver(variants: new[] { new TypeIconVariant { TypeKind = (int)TypeIconKinds.UnitType, TypeId = MugType.UnitTypeId, Variant = "s5", IconKey = "mug-responding" } });
			var builtIns = new List<CustomStateDetail>
			{
				new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Responding, ButtonText = "Responding" },
				new CustomStateDetail { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonText = "Available" }
			};
			var row = MapIconLegendBuilder.UnitRow(resolver, MugType, null, CustomStates(), builtIns);

			var rows = MapIconLegendBuilder.IconRows(resolver, TypeIconKinds.UnitType, MugType.UnitTypeId, row, resolver.ForUnitType(MugType, null));

			rows[0].UploadedUrl.Should().Be(TypeIcons.WebIconUrl("mug-responding"));
			rows[0].DefaultIcon.Url.Should().Be(TypeIcons.WebIconUrl("mug-own", MapIconStyles.Responding));
			rows[1].UploadedUrl.Should().BeNull();
			rows[1].State.Icon.Url.Should().Be(TypeIcons.WebIconUrl("mug-own", MapIconStyles.Available));
		}

		[TestCase("#fff", "#fff")]
		[TestCase(" #12ab9F ", "#12ab9F")]
		[TestCase("red", null)]
		[TestCase("#fff;background:url(x)", null)]
		[TestCase("label-default", null)]
		[TestCase(null, null)]
		public void Only_plain_hex_colours_reach_a_style_attribute(string color, string expected)
		{
			MapIconLegendBuilder.HexOrNull(color).Should().Be(expected);
		}

		#endregion Legend
	}
}
