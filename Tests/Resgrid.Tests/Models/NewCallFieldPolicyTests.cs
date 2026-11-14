using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// The policy decides whether a call-taker can forward an incident to the field. Two rules carry
	/// the weight: a department that has configured nothing behaves exactly as before, and a hidden
	/// field can never be required (which would lock the department out of creating calls at all).
	/// </summary>
	[TestFixture]
	public class NewCallFieldPolicyTests
	{
		private static NewCallFieldPolicy PolicyWith(params NewCallFieldRule[] rules) =>
			new NewCallFieldPolicy { Rules = new List<NewCallFieldRule>(rules) };

		[Test]
		public void An_unconfigured_policy_shows_everything_and_requires_nothing()
		{
			var policy = new NewCallFieldPolicy();

			policy.IsEmpty.Should().BeTrue();

			foreach (var key in NewCallFieldKeys.All)
			{
				policy.IsVisible(key).Should().BeTrue($"{key} should default to visible");
				policy.IsRequired(key).Should().BeFalse($"{key} should default to optional");
			}
		}

		[Test]
		public void A_field_with_no_rule_keeps_the_default_even_when_others_are_configured()
		{
			var policy = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.ContactInfo, Visible = false });

			policy.IsVisible(NewCallFieldKeys.Address).Should().BeTrue();
			policy.IsRequired(NewCallFieldKeys.Address).Should().BeFalse();
		}

		[Test]
		public void A_hidden_field_is_never_required()
		{
			// Stored data can be inconsistent — an admin hides a field that was previously required.
			// Honouring both would make call creation impossible.
			var policy = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.IncidentId, Visible = false, Required = true });

			policy.IsVisible(NewCallFieldKeys.IncidentId).Should().BeFalse();
			policy.IsRequired(NewCallFieldKeys.IncidentId).Should().BeFalse();
		}

		[Test]
		public void Keys_are_matched_case_insensitively()
		{
			var policy = PolicyWith(new NewCallFieldRule { Key = "ADDRESS", Visible = false });

			policy.IsVisible(NewCallFieldKeys.Address).Should().BeFalse();
		}

		[Test]
		public void Normalize_drops_unknown_keys_and_rules_that_say_nothing()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = "somethingWeRemoved", Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = true, Required = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.Address, Visible = true, Required = true });

			policy.Normalize();

			policy.Rules.Should().HaveCount(1);
			policy.Rules[0].Key.Should().Be(NewCallFieldKeys.Address);
		}

		[Test]
		public void Normalize_keeps_the_last_rule_when_a_key_is_duplicated()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = true, Required = true });

			policy.Normalize();

			policy.Rules.Should().HaveCount(1);
			policy.Rules[0].Required.Should().BeTrue();
			policy.Rules[0].Visible.Should().BeTrue();
		}

		[Test]
		public void A_hidden_field_survives_the_stored_round_trip()
		{
			// The policy is stored protobuf-serialized. Visible defaults to true, so an unmarked false is
			// the wire's implicit zero: it is never written, and reading it back leaves the initializer's
			// true in place -- Normalize then drops the rule and every hidden field comes back visible.
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.Address, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.Note, Visible = false, Required = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.IncidentId, Visible = true, Required = true }).Normalize();

			var stored = ObjectSerialization.Serialize(policy);
			var restored = ObjectSerialization.Deserialize<NewCallFieldPolicy>(stored).Normalize();

			restored.Rules.Should().HaveCount(3);
			restored.IsVisible(NewCallFieldKeys.Address).Should().BeFalse();
			restored.IsVisible(NewCallFieldKeys.Note).Should().BeFalse();
			restored.IsRequired(NewCallFieldKeys.Note).Should().BeFalse();
			restored.IsVisible(NewCallFieldKeys.IncidentId).Should().BeTrue();
			restored.IsRequired(NewCallFieldKeys.IncidentId).Should().BeTrue();
			restored.IsVisible(NewCallFieldKeys.DispatchList).Should().BeTrue();
		}

		[Test]
		public void A_plus_code_can_be_hidden_but_never_required()
		{
			// It is only a way of finding the location and is never stored on the call; the web form has no input for it,
			// so a stored requirement blocked every web call.
			var policy = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.PlusCode, Visible = true, Required = true });

			NewCallFieldKeys.CanBeRequired(NewCallFieldKeys.PlusCode).Should().BeFalse();
			policy.IsRequired(NewCallFieldKeys.PlusCode).Should().BeFalse();
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues()).Should().BeEmpty();
		}

		[Test]
		public void Normalize_drops_a_stored_plus_code_requirement_but_keeps_it_hidden()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.PlusCode, Visible = true, Required = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.What3Words, Visible = true, Required = true }).Normalize();

			policy.Rules.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.What3Words);

			var hidden = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.PlusCode, Visible = false, Required = true }).Normalize();

			hidden.IsVisible(NewCallFieldKeys.PlusCode).Should().BeFalse();
			hidden.Rules.Should().ContainSingle().Which.Required.Should().BeFalse();
		}

		[Test]
		public void Only_the_hide_only_fields_cannot_be_required()
		{
			// Switches whose "off" is an answer, and the single recipient lists (requiring recipients as a whole covers them).
			var hideOnly = new[]
			{
				NewCallFieldKeys.PlusCode,
				NewCallFieldKeys.AdditionalContacts,
				NewCallFieldKeys.CheckInTimers,
				NewCallFieldKeys.Part2Consent,
				NewCallFieldKeys.PendingCall,
				NewCallFieldKeys.DispatchPersonnel,
				NewCallFieldKeys.DispatchGroups,
				NewCallFieldKeys.DispatchRoles,
				NewCallFieldKeys.DispatchUnits
			};

			foreach (var key in NewCallFieldKeys.All)
				NewCallFieldKeys.CanBeRequired(key).Should().Be(System.Array.IndexOf(hideOnly, key) < 0, key);
		}

		[Test]
		public void Every_field_but_the_recipients_section_can_be_hidden()
		{
			foreach (var key in NewCallFieldKeys.All)
				NewCallFieldKeys.CanBeHidden(key).Should().Be(key != NewCallFieldKeys.DispatchList, key);
		}

		[Test]
		public void A_recipients_section_hidden_before_the_lists_could_be_hides_every_list()
		{
			// Stored before the four lists had keys of their own: hiding the section hid all of them.
			var legacy = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = false });

			foreach (var target in NewCallFieldKeys.DispatchTargets)
				legacy.IsVisible(target).Should().BeFalse(target);

			legacy.IsVisible(NewCallFieldKeys.DispatchList).Should().BeFalse();
			legacy.AreAllDispatchTargetsHidden.Should().BeTrue();
		}

		[Test]
		public void The_recipients_section_shows_while_any_list_does()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchPersonnel, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchGroups, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchRoles, Visible = false });

			policy.IsVisible(NewCallFieldKeys.DispatchUnits).Should().BeTrue();
			policy.IsVisible(NewCallFieldKeys.DispatchList).Should().BeTrue();
			policy.AreAllDispatchTargetsHidden.Should().BeFalse();
		}

		[Test]
		public void A_list_set_on_its_own_wins_over_a_section_hidden_before()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = true });

			policy.IsVisible(NewCallFieldKeys.DispatchUnits).Should().BeTrue();
			policy.IsVisible(NewCallFieldKeys.DispatchPersonnel).Should().BeFalse();
			policy.IsVisible(NewCallFieldKeys.DispatchList).Should().BeTrue();
		}

		[Test]
		public void Requiring_recipients_lapses_when_every_list_is_hidden()
		{
			// Nothing to pick from: the call forms offer only the optional All Call, so the requirement cannot apply.
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = true, Required = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchPersonnel, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchGroups, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchRoles, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = false });

			policy.IsRequired(NewCallFieldKeys.DispatchList).Should().BeFalse();
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues()).Should().BeEmpty();

			policy.Rules.Find(x => x.Key == NewCallFieldKeys.DispatchUnits).Visible = true;

			policy.IsRequired(NewCallFieldKeys.DispatchList).Should().BeTrue();
		}

		[Test]
		public void Normalize_writes_the_section_rule_so_older_clients_hide_their_picker_only_when_every_list_is_hidden()
		{
			// Apps that predate the per-list keys read only "dispatchList".
			var allHidden = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = true, Required = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchPersonnel, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchGroups, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchRoles, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = false }).Normalize();

			var section = allHidden.Rules.Find(x => x.Key == NewCallFieldKeys.DispatchList);
			section.Should().NotBeNull();
			section.Visible.Should().BeFalse();

			var personnelHidden = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchPersonnel, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchGroups, Visible = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchRoles, Visible = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = true }).Normalize();

			personnelHidden.Rules.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.DispatchPersonnel);
			personnelHidden.IsVisible(NewCallFieldKeys.DispatchList).Should().BeTrue();
			personnelHidden.IsVisible(NewCallFieldKeys.DispatchGroups).Should().BeTrue();
		}

		[Test]
		public void Normalize_writes_down_the_lists_a_section_hidden_before_still_hides()
		{
			var legacy = PolicyWith(new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = false }).Normalize();

			foreach (var target in NewCallFieldKeys.DispatchTargets)
				legacy.Rules.Should().Contain(x => x.Key == target && !x.Visible, target);

			legacy.AreAllDispatchTargetsHidden.Should().BeTrue();
		}

		[Test]
		public void Every_list_hidden_survives_the_stored_round_trip()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchPersonnel, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchGroups, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchRoles, Visible = false },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = false }).Normalize();

			var restored = ObjectSerialization.Deserialize<NewCallFieldPolicy>(ObjectSerialization.Serialize(policy)).Normalize();

			restored.AreAllDispatchTargetsHidden.Should().BeTrue();
			restored.IsVisible(NewCallFieldKeys.DispatchList).Should().BeFalse();
		}

		[Test]
		public void A_hide_only_field_never_carries_a_requirement()
		{
			var policy = PolicyWith(
				new NewCallFieldRule { Key = NewCallFieldKeys.CheckInTimers, Visible = true, Required = true },
				new NewCallFieldRule { Key = NewCallFieldKeys.DispatchUnits, Visible = true, Required = true });

			policy.IsRequired(NewCallFieldKeys.CheckInTimers).Should().BeFalse();
			policy.IsRequired(NewCallFieldKeys.DispatchUnits).Should().BeFalse();
			policy.Normalize().Rules.Should().BeEmpty();
		}

		[Test]
		public void A_policy_stored_before_visibility_was_written_still_reads_as_visible()
		{
			// Blobs already in the database never carry field 2 for a visible rule; they must keep
			// reading as visible after the contract change.
			var legacy = new NewCallFieldPolicy
			{
				Rules = new List<NewCallFieldRule> { new NewCallFieldRule { Key = NewCallFieldKeys.Address, Visible = true, Required = true } }
			};

			var restored = ObjectSerialization.Deserialize<NewCallFieldPolicy>(ObjectSerialization.Serialize(legacy));

			restored.IsVisible(NewCallFieldKeys.Address).Should().BeTrue();
			restored.IsRequired(NewCallFieldKeys.Address).Should().BeTrue();
		}
	}

	[TestFixture]
	public class NewCallFieldPolicyValidatorTests
	{
		private static NewCallFieldPolicy Requiring(params string[] keys)
		{
			var policy = new NewCallFieldPolicy();

			foreach (var key in keys)
				policy.Rules.Add(new NewCallFieldRule { Key = key, Visible = true, Required = true });

			return policy;
		}

		[Test]
		public void An_unconfigured_department_can_submit_an_empty_call()
		{
			var violations = NewCallFieldPolicyValidator.Validate(new NewCallFieldPolicy(), new NewCallFieldValues());

			violations.Should().BeEmpty();
		}

		[Test]
		public void Reports_every_required_field_left_blank()
		{
			var policy = Requiring(NewCallFieldKeys.Address, NewCallFieldKeys.ContactInfo, NewCallFieldKeys.DispatchList);

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues());

			violations.Should().HaveCount(3);
			violations.ConvertAll(x => x.Key).Should().BeEquivalentTo(new[]
			{
				NewCallFieldKeys.Address,
				NewCallFieldKeys.ContactInfo,
				NewCallFieldKeys.DispatchList
			});
		}

		[Test]
		public void Whitespace_does_not_satisfy_a_required_field()
		{
			var policy = Requiring(NewCallFieldKeys.Address);

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { Address = "   " });

			violations.Should().HaveCount(1);
		}

		[Test]
		public void Passes_when_every_required_field_is_supplied()
		{
			var policy = Requiring(NewCallFieldKeys.Address, NewCallFieldKeys.DestinationPoi, NewCallFieldKeys.DispatchOn, NewCallFieldKeys.DispatchList);

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues
			{
				Address = "Nieuwstraat 14, 9620 Zottegem",
				DestinationPoiId = 12,
				DispatchOn = System.DateTime.UtcNow,
				HasDispatchList = true
			});

			violations.Should().BeEmpty();
		}

		[Test]
		public void A_required_map_page_and_primary_contact_must_be_supplied()
		{
			var policy = Requiring(NewCallFieldKeys.MapPage, NewCallFieldKeys.PrimaryContact);

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { MapPage = " ", PrimaryContactId = "" })
				.ConvertAll(x => x.Key).Should().BeEquivalentTo(new[] { NewCallFieldKeys.MapPage, NewCallFieldKeys.PrimaryContact });

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { MapPage = "12-C4", PrimaryContactId = "c-1" })
				.Should().BeEmpty();
		}

		[Test]
		public void A_client_with_no_map_page_or_contact_input_is_not_held_to_them()
		{
			var policy = Requiring(NewCallFieldKeys.MapPage, NewCallFieldKeys.PrimaryContact);

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues()
					.Unsupported(NewCallFieldKeys.MapPage, NewCallFieldKeys.PrimaryContact))
				.Should().BeEmpty();
		}

		[Test]
		public void A_zero_destination_poi_does_not_count_as_supplied()
		{
			var policy = Requiring(NewCallFieldKeys.DestinationPoi);

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { DestinationPoiId = 0 });

			violations.Should().HaveCount(1);
		}

		[Test]
		public void A_hidden_required_field_is_not_enforced()
		{
			var policy = new NewCallFieldPolicy();
			policy.Rules.Add(new NewCallFieldRule { Key = NewCallFieldKeys.Address, Visible = false, Required = true });

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues());

			violations.Should().BeEmpty();
		}

		[Test]
		public void A_field_the_surface_cannot_collect_is_not_enforced_there()
		{
			// The field apps have no protocol or linked-call picker, and an edit cannot schedule a call that already went
			// out: requiring those there would stop calls being created or edited at all.
			var policy = Requiring(NewCallFieldKeys.Protocols, NewCallFieldKeys.LinkedCall, NewCallFieldKeys.DispatchOn, NewCallFieldKeys.Address);

			var violations = NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues()
				.Unsupported(NewCallFieldKeys.Protocols, "LINKEDCALL", NewCallFieldKeys.DispatchOn));

			violations.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.Address);
		}

		[Test]
		public void A_supported_field_is_still_enforced()
		{
			var policy = Requiring(NewCallFieldKeys.Protocols, NewCallFieldKeys.LinkedCall);

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { HasProtocols = true })
				.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.LinkedCall);
		}

		[Test]
		public void Describes_violations_for_an_error_body()
		{
			var policy = Requiring(NewCallFieldKeys.Address);

			var description = NewCallFieldPolicyValidator.DescribeViolations(
				NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues()));

			description.Should().Contain(NewCallFieldKeys.Address);
		}

		[Test]
		public void A_pin_placed_on_the_web_form_satisfies_a_required_geolocation()
		{
			// The web form posts the pin as Latitude/Longitude; Call.GeoLocationData is only filled in once the call is saved,
			// so a policy reading it rejected every web call, pin or no pin.
			var policy = Requiring(NewCallFieldKeys.Geolocation);
			var model = new Resgrid.Web.Areas.User.Models.Calls.NewCallView { Call = new Call(), Latitude = "39.2733", Longitude = "-119.5841" };

			model.PostedGeoLocation().Should().Be("39.2733,-119.5841");
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { Geolocation = model.PostedGeoLocation() }).Should().BeEmpty();
		}

		[Test]
		public void An_address_without_a_pin_does_not_satisfy_a_required_geolocation()
		{
			// Same rule as the v4 SaveCall, which checks the policy before it geocodes the address.
			var policy = Requiring(NewCallFieldKeys.Geolocation);
			var model = new Resgrid.Web.Areas.User.Models.Calls.NewCallView { Call = new Call { Address = "1 Main St" }, Latitude = "39.2733", Longitude = "" };

			model.PostedGeoLocation().Should().BeNull();
			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { Address = model.Call.Address, Geolocation = model.PostedGeoLocation() })
				.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.Geolocation);
		}

		[TestCase(",")]
		[TestCase("0,0")]
		[TestCase("abc,def")]
		public void A_placeholder_from_a_client_with_no_position_does_not_satisfy_a_required_geolocation(string geolocation)
		{
			// The Dispatch and BigBoard apps sent "," when nothing was picked; it is text, but not a location.
			var policy = Requiring(NewCallFieldKeys.Geolocation);

			NewCallFieldPolicyValidator.Validate(policy, new NewCallFieldValues { Geolocation = geolocation })
				.Should().ContainSingle().Which.Key.Should().Be(NewCallFieldKeys.Geolocation);
		}

		[TestCase("39.2733", "-119.5841", false)]
		[TestCase(" 39.2733 ", "-119.5841 ", false)]
		[TestCase(null, null, false)]
		[TestCase(" ", "", false)]
		[TestCase("39.2733", "", true)]
		[TestCase("", "-119.5841", true)]
		[TestCase("90.5", "-119.5841", true)]
		[TestCase("39.2733", "-180.5", true)]
		[TestCase("39.2733a", "-119.5841", true)]
		public void A_typed_pin_must_be_a_real_coordinate(string latitude, string longitude, bool invalid)
		{
			// NewCall refuses an invalid pin before the policy runs, so a typo cannot satisfy a required geolocation.
			var model = new Resgrid.Web.Areas.User.Models.Calls.NewCallView { Call = new Call(), Latitude = latitude, Longitude = longitude };

			model.HasInvalidPin().Should().Be(invalid);
		}

		[Test]
		public void A_posted_pin_is_stored_without_surrounding_spaces()
		{
			var model = new Resgrid.Web.Areas.User.Models.Calls.NewCallView { Call = new Call(), Latitude = " 39.2733 ", Longitude = "-119.5841 " };

			model.PostedGeoLocation().Should().Be("39.2733,-119.5841");
		}
	}
}
