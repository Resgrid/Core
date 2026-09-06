using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordsRetentionPolicyHistoryTests
	{
		[Test]
		public void A_new_finite_coroner_override_does_not_erase_previously_permanent_reports()
		{
			var now = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
			var current = new RecordsRetentionPolicy { Overrides = new List<RecordsRetentionOverride>
			{ new RecordsRetentionOverride { DefinitionKey = RmsDefinitionKeys.Coroner, RetentionYears = 2, AppliesFrom = now } } };
			current.PreserveHistory(new RecordsRetentionPolicy(), now);
			current.ResolveYears(RmsDefinitionKeys.Coroner, now.AddYears(-10)).Should().Be(0);
			current.ResolveYears(RmsDefinitionKeys.Coroner, now).Should().Be(2);
		}

		[Test]
		public void Shortening_removal_and_serialization_preserve_every_prior_policy()
		{
			var first = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var previous = new RecordsRetentionPolicy { DepartmentDefaultYears = 10, LastChangedOn = first };
			var shorter = new RecordsRetentionPolicy { DepartmentDefaultYears = 2 };
			shorter.PreserveHistory(previous, first.AddYears(3));
			var removed = new RecordsRetentionPolicy();
			removed.PreserveHistory(shorter, first.AddYears(5));
			var restored = ObjectSerialization.Deserialize<RecordsRetentionPolicy>(ObjectSerialization.Serialize(removed));
			restored.ResolveYears(RmsDefinitionKeys.Training, first.AddYears(1)).Should().Be(10);
			restored.ResolveYears(RmsDefinitionKeys.Training, first.AddYears(4)).Should().Be(2);
			restored.ResolveYears(RmsDefinitionKeys.Training, first.AddYears(6)).Should().Be(7);
			restored.ResolveYears(RmsDefinitionKeys.Training, first.AddYears(-1)).Should().Be(0, "missing historical evidence must not shorten retention");
		}

		[Test]
		public void An_existing_override_with_a_future_effective_date_does_not_apply_early()
		{
			var starts = DateTime.UtcNow.AddYears(1);
			var policy = new RecordsRetentionPolicy { DepartmentDefaultYears = 10, Overrides = new List<RecordsRetentionOverride>
			{ new RecordsRetentionOverride { DefinitionKey = RmsDefinitionKeys.Training, RetentionYears = 2, AppliesFrom = starts } } };
			policy.ResolveYears(RmsDefinitionKeys.Training, starts.AddTicks(-1)).Should().Be(10);
			policy.ResolveYears(RmsDefinitionKeys.Training, starts).Should().Be(2);
		}
	}
}
