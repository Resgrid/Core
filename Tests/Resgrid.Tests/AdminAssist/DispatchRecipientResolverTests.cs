using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DispatchRecipientResolverTests
	{
		private static readonly DateTime Now = new(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);
		[TestCase(true)]
		[TestCase(false)]
		public void Incremental_production_resolution_matches_isolated_preview_with_overlapping_routes(bool useShift)
		{
			var routes = new[] {
				new DispatchRoute(DispatchRouteKind.Direct, "direct", new[] { "a", "a" }),
				new DispatchRoute(DispatchRouteKind.Group, "1", new[] { "a", "b", "off" }, useShift, new[] { "a", "trade" }),
				new DispatchRoute(DispatchRouteKind.Group, "2", new[] { "b", "c" }, useShift, Array.Empty<string>()),
				new DispatchRoute(DispatchRouteKind.UnitCrew, "1", new[] { "c", "crew" }),
				new DispatchRoute(DispatchRouteKind.UnitGroup, "2", new[] { "c", "crew", "d" }),
				new DispatchRoute(DispatchRouteKind.Role, "1", new[] { "a", "d", "role" }) };
			var full = DispatchRecipientResolver.Resolve(Now, routes);
			var sent = new List<string>(); var decisions = new List<DispatchSelection>();
			foreach (var route in routes) { var part = DispatchRecipientResolver.Resolve(Now, new[] { route }, sent); sent.AddRange(part.SelectedUserIds); decisions.AddRange(part.Decisions); }
			Assert.That(sent, Is.EqualTo(full.SelectedUserIds));
			Assert.That(decisions, Is.EqualTo(full.Decisions));
			Assert.That(sent, Is.EqualTo(useShift ? new[] { "a", "a", "trade", "b", "c", "crew", "d", "role" } : new[] { "a", "a", "b", "off", "c", "crew", "d", "role" }));
			Assert.That(full.Decisions.Where(d => d.EmptyShiftFallback).Select(d => d.UserId), Is.EqualTo(useShift ? new[] { "b", "c" } : Array.Empty<string>()));
		}
		[Test]
		public void Simulation_does_not_mutate_inputs_or_prior_selection()
		{
			var prior = new HashSet<string> { "a" }; var members = new[] { "a", "b" };
			var route = new DispatchRoute(DispatchRouteKind.Role, "1", members);
			Assert.That(DispatchRecipientResolver.Resolve(Now, new[] { route }, prior).SelectedUserIds, Is.EqualTo(new[] { "b" }));
			Assert.That(prior, Is.EquivalentTo(new[] { "a" })); Assert.That(members, Is.EqualTo(new[] { "a", "b" }));
		}
		[Test]
		public void Unknown_roster_and_implicit_local_time_cannot_be_previewed_as_known()
		{
			Assert.Throws<ArgumentException>(() => DispatchRecipientResolver.Resolve(Now, new[] { new DispatchRoute(DispatchRouteKind.Group, "1", new[] { "a" }, true) }));
			Assert.Throws<ArgumentException>(() => DispatchRecipientResolver.Resolve(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), Array.Empty<DispatchRoute>()));
		}
	}
}
