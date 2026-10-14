using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class CallTypeLinkTests
	{
		private static readonly List<CallType> Types = new List<CallType>
		{
			new CallType { CallTypeId = 10, Type = "Structure Fire" },
			new CallType { CallTypeId = 11, Type = "Medical" },
			new CallType { CallTypeId = 12, Type = "medical" } // a duplicate name: the lowest id wins
		};

		[TestCase("Structure Fire", null, 10)]
		[TestCase("  structure fire ", null, 10)]
		[TestCase("Medical", null, 11)]
		[TestCase("Unknown", null, null)]
		[TestCase("", 10, null)]
		public void links_by_name(string name, int? current, int? expected)
		{
			CallTypeLink.Resolve(name, current, Types).Should().Be(expected);
		}

		[Test]
		public void a_renamed_type_keeps_its_calls()
		{
			// Call logged as "Brush Fire" (type 10), type later renamed to "Structure Fire": the stored name matches nothing.
			CallTypeLink.Resolve("Brush Fire", 10, Types).Should().Be(10);
		}

		[Test]
		public void choosing_another_type_moves_the_link()
		{
			CallTypeLink.Resolve("Medical", 10, Types).Should().Be(11);
		}

		[Test]
		public void find_prefers_the_link_then_the_name()
		{
			CallTypeLink.Find(new Call { Type = "Brush Fire", CallTypeId = 10 }, Types).CallTypeId.Should().Be(10);
			CallTypeLink.Find(new Call { Type = "medical" }, Types).CallTypeId.Should().Be(11);
			CallTypeLink.Find(new Call { Type = "Gone", CallTypeId = 99 }, Types).Should().BeNull();
		}
	}
}
