using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// P4 shared trigger engine, written before the move: the protocol matcher (P0's ProtocolTriggerMatcher) and the run card trigger
	/// specificity (RunCardsService) are compared over a generated matrix of triggers and calls with a frozen copy of the rules as they
	/// stood before P4 (the oracles below). Any change to a current match fails here. Triggers with no P4 criteria are the only ones
	/// the oracles know; the P4 criteria are covered by DispatchTriggerEngineTests.
	/// </summary>
	[TestFixture]
	public class DispatchTriggerEngineCharacterizationTests
	{
		private static readonly DateTime At = new DateTime(2026, 11, 25, 14, 30, 0, DateTimeKind.Utc);

		private static readonly int?[] Priorities = { null, 0, 1, 3, 1042 };
		private static readonly int?[] TypeIds = { null, 7, 9 };
		private static readonly string[] TypeNames = { null, "", "No Type", "no type", "Structure Fire", "structure fire ", "Medical" };
		private static readonly DateTime?[] Starts = { null, At.AddDays(-1), At.AddMinutes(1), At };
		private static readonly DateTime?[] Ends = { null, At.AddDays(1), At.AddMinutes(-1), At };

		private static IEnumerable<DispatchProtocolTrigger> ProtocolTriggers()
		{
			var id = 1;
			foreach (var type in new[] { 0, 1, 2, 5 })
				foreach (var priority in Priorities)
					foreach (var typeId in TypeIds)
						foreach (var name in TypeNames)
							foreach (var window in new[] { (Starts[0], Ends[0]), (Starts[1], Ends[1]), (Starts[2], Ends[0]), (Starts[0], Ends[2]), (Starts[3], Ends[3]) })
								yield return new DispatchProtocolTrigger
								{
									DispatchProtocolTriggerId = id++,
									DispatchProtocolId = 1,
									Type = type,
									Priority = priority,
									CallTypeId = typeId,
									CallType = name,
									StartsOn = window.Item1,
									EndsOn = window.Item2
								};
		}

		private static IEnumerable<ProtocolCallContext> Calls()
		{
			foreach (var priority in new[] { 0, 1, 3, 1042 })
				foreach (var typeId in TypeIds)
					foreach (var name in TypeNames)
						yield return new ProtocolCallContext { Priority = priority, CallTypeId = typeId, CallTypeName = name, AtUtc = At };
		}

		[Test]
		public void Protocol_matches_are_unchanged_for_every_trigger_and_call_in_the_matrix()
		{
			var triggers = ProtocolTriggers().ToList();
			var compared = 0;

			foreach (var call in Calls())
			{
				foreach (var trigger in triggers)
				{
					var protocol = new DispatchProtocol { DispatchProtocolId = 1, Triggers = new List<DispatchProtocolTrigger> { trigger } };
					var expected = LegacyProtocolOracle.Match(protocol, call).Select(x => x.DispatchProtocolTriggerId).ToList();
					var actual = ProtocolTriggerMatcher.Match(protocol, call).Select(x => x.DispatchProtocolTriggerId).ToList();
					actual.Should().Equal(expected, $"trigger {Describe(trigger)} against call p={call.Priority} id={call.CallTypeId} name='{call.CallTypeName}'");
					compared++;
				}
			}

			compared.Should().BeGreaterThan(10000);
		}

		[Test]
		public void A_protocol_with_several_triggers_returns_the_same_set_in_the_same_order()
		{
			var triggers = ProtocolTriggers().Where((t, i) => i % 37 == 0).Take(40).ToList();
			var protocol = new DispatchProtocol { DispatchProtocolId = 1, Triggers = triggers };

			foreach (var call in Calls())
			{
				var expected = LegacyProtocolOracle.Match(protocol, call).Select(x => x.DispatchProtocolTriggerId).ToList();
				ProtocolTriggerMatcher.Match(protocol, call).Select(x => x.DispatchProtocolTriggerId).Should().Equal(expected);
				ProtocolTriggerMatcher.MostSpecific(ProtocolTriggerMatcher.Match(protocol, call))?.DispatchProtocolTriggerId
					.Should().Be(LegacyProtocolOracle.MostSpecific(LegacyProtocolOracle.Match(protocol, call))?.DispatchProtocolTriggerId);
			}
		}

		[Test]
		public void Disabled_deleted_and_trigger_less_protocols_never_match()
		{
			var call = new ProtocolCallContext { Priority = 1, CallTypeId = 7, CallTypeName = "Structure Fire", AtUtc = At };
			var trigger = new DispatchProtocolTrigger { DispatchProtocolTriggerId = 1, Type = 0, Priority = 1 };

			ProtocolTriggerMatcher.Match(new DispatchProtocol { IsDisabled = true, Triggers = new List<DispatchProtocolTrigger> { trigger } }, call).Should().BeEmpty();
			ProtocolTriggerMatcher.Match(new DispatchProtocol { IsDeleted = true, Triggers = new List<DispatchProtocolTrigger> { trigger } }, call).Should().BeEmpty();
			ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = null }, call).Should().BeEmpty();
			ProtocolTriggerMatcher.Match(null, call).Should().BeEmpty();
			ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = new List<DispatchProtocolTrigger> { trigger } }, null).Should().BeEmpty();
		}

		[Test]
		public void Known_protocol_cases_keep_their_answers()
		{
			var call = new ProtocolCallContext { Priority = 3, CallTypeId = 7, CallTypeName = "Structure Fire", AtUtc = At };
			bool Matches(DispatchProtocolTrigger t) => ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = new List<DispatchProtocolTrigger> { t } }, call).Any();

			Matches(new DispatchProtocolTrigger { Type = 0, Priority = 3 }).Should().BeTrue();
			Matches(new DispatchProtocolTrigger { Type = 0, Priority = null }).Should().BeFalse();
			Matches(new DispatchProtocolTrigger { Type = 1, CallTypeId = 7, CallType = "Renamed" }).Should().BeTrue("the id wins over the name");
			Matches(new DispatchProtocolTrigger { Type = 1, CallTypeId = 9, CallType = "Structure Fire" }).Should().BeFalse();
			Matches(new DispatchProtocolTrigger { Type = 1, CallType = "structure fire" }).Should().BeTrue("a trigger written before M0336 matches by name");
			Matches(new DispatchProtocolTrigger { Type = 1, CallType = "No Type" }).Should().BeFalse("a typed call is not 'no type'");
			Matches(new DispatchProtocolTrigger { Type = 2, Priority = 3, CallTypeId = 7 }).Should().BeTrue();
			Matches(new DispatchProtocolTrigger { Type = 2, Priority = 1, CallTypeId = 7 }).Should().BeFalse();
			Matches(new DispatchProtocolTrigger { Type = 0, Priority = 3, StartsOn = At.AddMinutes(1) }).Should().BeFalse();
			Matches(new DispatchProtocolTrigger { Type = 0, Priority = 3, EndsOn = At }).Should().BeTrue("both window ends are inclusive");

			var untyped = new ProtocolCallContext { Priority = 3, CallTypeName = "No Type", AtUtc = At };
			ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = new List<DispatchProtocolTrigger> { new DispatchProtocolTrigger { Type = 1 } } }, untyped).Should().HaveCount(1);
		}

		[Test]
		public void Run_card_specificity_is_unchanged_for_every_trigger_and_call_in_the_matrix()
		{
			var triggers = new List<RunCardTrigger>();
			var id = 1;
			foreach (var type in new[] { 0, 1, 2, 7 })
				foreach (var priority in Priorities)
					foreach (var typeId in TypeIds)
						foreach (var window in new[] { (Starts[0], Ends[0]), (Starts[1], Ends[1]), (Starts[2], Ends[0]), (Starts[0], Ends[2]), (Starts[3], Ends[3]) })
							triggers.Add(new RunCardTrigger { RunCardTriggerId = id++, TriggerType = type, Priority = priority, CallTypeId = typeId, StartsOn = window.Item1, EndsOn = window.Item2 });

			var compared = 0;
			foreach (var priority in new[] { 0, 1, 3, 1042 })
			{
				foreach (var typeId in TypeIds)
				{
					// One trigger per card, then cards with several triggers (the strongest wins).
					foreach (var trigger in triggers)
					{
						var card = new RunCard { Triggers = new List<RunCardTrigger> { trigger } };
						RunCardsService.GetTriggerMatchSpecificity(card, priority, typeId, At).Should().Be(LegacyRunCardOracle.Specificity(card, priority, typeId, At));
						compared++;
					}

					for (var skip = 0; skip < triggers.Count; skip += 11)
					{
						var card = new RunCard { Triggers = triggers.Skip(skip).Take(6).ToList() };
						RunCardsService.GetTriggerMatchSpecificity(card, priority, typeId, At).Should().Be(LegacyRunCardOracle.Specificity(card, priority, typeId, At));
					}
				}
			}

			RunCardsService.GetTriggerMatchSpecificity(null, 1, 7, At).Should().BeNull();
			RunCardsService.GetTriggerMatchSpecificity(new RunCard { Triggers = new List<RunCardTrigger>() }, 1, 7, At).Should().BeNull();
			compared.Should().BeGreaterThan(2000);
		}

		private static string Describe(DispatchProtocolTrigger t) =>
			$"type={t.Type} p={t.Priority} id={t.CallTypeId} name='{t.CallType}' {t.StartsOn:o}..{t.EndsOn:o}";

		/// <summary>ProtocolTriggerMatcher.Match as it stood before P4 (P0, M0336), frozen.</summary>
		private static class LegacyProtocolOracle
		{
			public static List<DispatchProtocolTrigger> Match(DispatchProtocol protocol, ProtocolCallContext call)
			{
				var matched = new List<DispatchProtocolTrigger>();
				if (protocol == null || call == null || protocol.IsDisabled || protocol.IsDeleted || protocol.Triggers == null)
					return matched;

				foreach (var trigger in protocol.Triggers.Where(x => x != null))
				{
					if (trigger.StartsOn.HasValue && trigger.StartsOn.Value > call.AtUtc)
						continue;
					if (trigger.EndsOn.HasValue && trigger.EndsOn.Value < call.AtUtc)
						continue;

					switch (trigger.Type)
					{
						case 0:
							if (trigger.Priority == call.Priority)
								matched.Add(trigger);
							break;
						case 1:
							if (TypeMatches(trigger, call))
								matched.Add(trigger);
							break;
						case 2:
							if (trigger.Priority == call.Priority && TypeMatches(trigger, call))
								matched.Add(trigger);
							break;
					}
				}

				return matched;
			}

			private static bool TypeMatches(DispatchProtocolTrigger trigger, ProtocolCallContext call)
			{
				if (trigger.CallTypeId.HasValue)
					return call.CallTypeId.HasValue && call.CallTypeId.Value == trigger.CallTypeId.Value;

				if (string.IsNullOrWhiteSpace(trigger.CallType) || CallTypeLink.SameName(trigger.CallType, "No Type"))
					return !call.CallTypeId.HasValue;

				return CallTypeLink.SameName(trigger.CallType, call.CallTypeName);
			}

			public static DispatchProtocolTrigger MostSpecific(IEnumerable<DispatchProtocolTrigger> triggers) =>
				triggers.OrderByDescending(x => x.Type == 2 ? 2 : x.Type == 1 ? 1 : 0).ThenBy(x => x.DispatchProtocolTriggerId).FirstOrDefault();
		}

		/// <summary>RunCardsService.GetTriggerMatchSpecificity as it stood before P4, frozen.</summary>
		private static class LegacyRunCardOracle
		{
			public static int? Specificity(RunCard card, int priority, int? callTypeId, DateTime utcNow)
			{
				if (card?.Triggers == null || !card.Triggers.Any())
					return null;

				int? best = null;
				foreach (var trigger in card.Triggers)
				{
					if (trigger.StartsOn.HasValue && trigger.StartsOn.Value > utcNow)
						continue;
					if (trigger.EndsOn.HasValue && trigger.EndsOn.Value < utcNow)
						continue;

					int? specificity = null;
					switch (trigger.TriggerType)
					{
						case 0:
							if (trigger.Priority.HasValue && trigger.Priority.Value == priority)
								specificity = 1;
							break;
						case 1:
							if (trigger.CallTypeId.HasValue && callTypeId.HasValue && trigger.CallTypeId.Value == callTypeId.Value)
								specificity = 2;
							break;
						case 2:
							if (trigger.Priority.HasValue && trigger.Priority.Value == priority && trigger.CallTypeId.HasValue && callTypeId.HasValue && trigger.CallTypeId.Value == callTypeId.Value)
								specificity = 3;
							break;
					}

					if (specificity.HasValue && (!best.HasValue || specificity.Value > best.Value))
						best = specificity;
				}

				return best;
			}
		}
	}
}
