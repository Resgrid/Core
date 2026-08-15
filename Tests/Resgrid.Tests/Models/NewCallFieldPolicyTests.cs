using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
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
	}
}
