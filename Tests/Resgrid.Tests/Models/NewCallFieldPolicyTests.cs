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
	}
}
