using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The DispatchList is client supplied text, and a call that reaches the API with one bad entry still
	/// has to dispatch everything else in it. Prod hit this with a Spanish department whose client sent
	/// role names ("R:PARAMÉDICO") instead of role ids, which threw and dropped every role on the call.
	/// </summary>
	[TestFixture]
	public class DispatchListHelperTests
	{
		private static readonly Dictionary<string, int> Roles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Paramédico", 12 },
			{ "Comandante", 34 }
		};

		private static int? ResolveRole(string name) => Roles.TryGetValue(name, out var id) ? id : (int?)null;

		private static string[] Split(string dispatchList) => dispatchList.Split('|');

		[Test]
		public void Numeric_entries_are_returned_for_their_prefix_only()
		{
			var ids = DispatchListHelper.ResolveIds(Split("P:abc|G:1|R:12|U:7|R:34"), "R:", ResolveRole);

			ids.Should().Equal(12, 34);
		}

		[Test]
		public void A_name_is_resolved_when_the_entry_isnt_numeric()
		{
			var ids = DispatchListHelper.ResolveIds(Split("R:PARAMÉDICO"), "R:", ResolveRole);

			ids.Should().Equal(12);
		}

		[Test]
		public void One_unresolvable_entry_does_not_drop_the_rest()
		{
			var ids = DispatchListHelper.ResolveIds(Split("R:12|R:NOT A ROLE|R:34"), "R:", ResolveRole);

			ids.Should().Equal(12, 34);
		}

		[Test]
		public void Duplicates_and_empty_entries_are_ignored()
		{
			var ids = DispatchListHelper.ResolveIds(Split("R:12|R:|R:PARAMÉDICO|R: 12 ||R:34"), "R:", ResolveRole);

			ids.Should().Equal(12, 34);
		}

		[Test]
		public void An_id_wins_over_a_name_lookup()
		{
			var ids = DispatchListHelper.ResolveIds(Split("R:99"), "R:", _ => 12);

			ids.Should().Equal(99);
		}

		[Test]
		public void No_matching_prefix_returns_an_empty_list()
		{
			DispatchListHelper.ResolveIds(Split("G:1|U:2"), "R:", ResolveRole).Should().BeEmpty();
			DispatchListHelper.ResolveIds(null, "R:", ResolveRole).Should().BeEmpty();
		}
	}
}
