using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The call a protocol is matched against: its priority, type and logged time. Built once per request; the web forms, the
	/// v4 API and the attach path all pass one through the same matcher (<see cref="ProtocolTriggerMatcher"/>).
	/// </summary>
	public sealed class ProtocolCallContext
	{
		public int Priority { get; set; }

		/// <summary>The department's call type id, or null when the call has no type.</summary>
		public int? CallTypeId { get; set; }

		/// <summary>The call's type name as posted; only used for triggers written before M0336 that have no type id.</summary>
		public string CallTypeName { get; set; }

		/// <summary>The call's logged time in UTC; trigger windows are compared with it (not with the current time).</summary>
		public DateTime AtUtc { get; set; }
	}

	/// <summary>
	/// Decides which of a protocol's triggers apply to a call. Pure: no data access, so it is the one place matching rules live.
	/// <list type="bullet">
	/// <item>A trigger applies inside its window: StartsOn &lt;= the call's logged time &lt;= EndsOn (both stored in UTC; an open end
	/// is unbounded).</item>
	/// <item>Priority triggers match the call's priority id; type triggers match the call type id, where "no type" is a null id
	/// rather than a translated label; priority-and-type triggers need both.</item>
	/// <item>A disabled or deleted protocol never matches.</item>
	/// </list>
	/// </summary>
	public static class ProtocolTriggerMatcher
	{
		/// <summary>The label the old trigger form stored for "no type" (English only, whatever the dispatcher's language).</summary>
		public const string LegacyNoTypeLabel = "No Type";

		public static List<DispatchProtocolTrigger> Match(DispatchProtocol protocol, ProtocolCallContext call)
		{
			var matched = new List<DispatchProtocolTrigger>();

			if (protocol == null || call == null || protocol.IsDisabled || protocol.IsDeleted || protocol.Triggers == null)
				return matched;

			foreach (var trigger in protocol.Triggers.Where(x => x != null))
			{
				if (!InWindow(trigger, call.AtUtc))
					continue;

				switch ((ProtocolTriggerTypes)trigger.Type)
				{
					case ProtocolTriggerTypes.CallPriorty:
						if (trigger.Priority == call.Priority)
							matched.Add(trigger);
						break;
					case ProtocolTriggerTypes.CallType:
						if (TypeMatches(trigger, call))
							matched.Add(trigger);
						break;
					case ProtocolTriggerTypes.CallPriortyAndType:
						if (trigger.Priority == call.Priority && TypeMatches(trigger, call))
							matched.Add(trigger);
						break;
				}
			}

			return matched;
		}

		public static bool InWindow(DispatchProtocolTrigger trigger, DateTime atUtc)
		{
			if (trigger.StartsOn.HasValue && trigger.StartsOn.Value > atUtc)
				return false;

			if (trigger.EndsOn.HasValue && trigger.EndsOn.Value < atUtc)
				return false;

			return true;
		}

		/// <summary>True for the "no type" trigger: no type id and no name (or the old English label).</summary>
		public static bool IsNoTypeTrigger(DispatchProtocolTrigger trigger)
		{
			return trigger != null && !trigger.CallTypeId.HasValue &&
				   (string.IsNullOrWhiteSpace(trigger.CallType) || CallTypeLink.SameName(trigger.CallType, LegacyNoTypeLabel));
		}

		private static bool TypeMatches(DispatchProtocolTrigger trigger, ProtocolCallContext call)
		{
			if (trigger.CallTypeId.HasValue)
				return call.CallTypeId.HasValue && call.CallTypeId.Value == trigger.CallTypeId.Value;

			if (IsNoTypeTrigger(trigger))
				return !call.CallTypeId.HasValue;

			// Written before M0336 and its type was not found by name then (deleted, or never one of the department's types):
			// the name is all there is, as before.
			return CallTypeLink.SameName(trigger.CallType, call.CallTypeName);
		}

		/// <summary>The order a reason chip reads in: priority and type together, then type, then priority.</summary>
		public static DispatchProtocolTrigger MostSpecific(IEnumerable<DispatchProtocolTrigger> triggers)
		{
			return (triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
				.OrderByDescending(x => x.Type == (int)ProtocolTriggerTypes.CallPriortyAndType ? 2 : x.Type == (int)ProtocolTriggerTypes.CallType ? 1 : 0)
				.ThenBy(x => x.DispatchProtocolTriggerId)
				.FirstOrDefault();
		}
	}

	/// <summary>One protocol evaluated for a call: whether it applies and which trigger made it apply.</summary>
	public sealed class ProtocolMatch
	{
		public DispatchProtocol Protocol { get; set; }

		/// <summary>Inactive (no trigger matched), Active (matched, no questions) or Pending (matched, questions to answer).</summary>
		public ProtocolStates State { get; set; }

		/// <summary>The most specific trigger that matched; null when none did.</summary>
		public DispatchProtocolTrigger MatchedTrigger { get; set; }
	}

	/// <summary>A protocol a call form or app asks to attach, with the answers it collected (answer ids).</summary>
	public sealed class ProtocolAttachRequest
	{
		public int ProtocolId { get; set; }

		/// <summary>The selected answer ids; null when the client collected none (an older app sends ids only).</summary>
		public List<int> AnswerIds { get; set; }

		/// <summary>
		/// True when the client runs the question flow (the web call forms): a protocol that a trigger suggested and that has
		/// questions is attached only once every question is answered and the score reaches its minimum weight. False for
		/// clients that send ids alone, whose pick is attached as before.
		/// </summary>
		public bool AnswersRequired { get; set; }
	}

	/// <summary>Why a requested protocol was not attached. Not persisted.</summary>
	public enum ProtocolAttachRefusals
	{
		/// <summary>Unknown, or another department's.</summary>
		NotFound = 0,
		Deleted = 1,
		Disabled = 2,
		/// <summary>Already on the call (removed or not), or asked for twice.</summary>
		Duplicate = 3,
		QuestionsUnanswered = 4,
		BelowMinimumWeight = 5
	}

	public sealed class ProtocolAttachRefusal
	{
		public int ProtocolId { get; set; }
		public ProtocolAttachRefusals Reason { get; set; }
	}

	/// <summary>What the attach path decided: rows ready to save on the call, and the requests it refused.</summary>
	public sealed class ProtocolAttachResult
	{
		public List<CallProtocol> Attached { get; set; } = new List<CallProtocol>();
		public List<ProtocolAttachRefusal> Refused { get; set; } = new List<ProtocolAttachRefusal>();
	}

	/// <summary>
	/// The questionnaire recorded on a call (CallProtocols.AnswersJson), in words. The server builds it from the protocol as it
	/// stood at attach time; nothing in it comes from the client except which answer ids were chosen.
	/// </summary>
	public sealed class CallProtocolAnswers
	{
		public string ProtocolCode { get; set; }
		public string ProtocolName { get; set; }
		public int MinimumWeight { get; set; }
		public int Score { get; set; }
		/// <summary>Every question had an answer.</summary>
		public bool Complete { get; set; }
		/// <summary>Complete and the score reached the minimum weight.</summary>
		public bool Passed { get; set; }
		public int? MatchedTriggerId { get; set; }
		public int? MatchedTriggerType { get; set; }
		public List<CallProtocolAnswer> Answers { get; set; } = new List<CallProtocolAnswer>();
	}

	public sealed class CallProtocolAnswer
	{
		public int QuestionId { get; set; }
		public string Question { get; set; }
		/// <summary>Null when the question was left unanswered.</summary>
		public int? AnswerId { get; set; }
		public string Answer { get; set; }
		public int Weight { get; set; }
	}

	/// <summary>A protocol trigger a call type or priority change affects (reported to the person making the change).</summary>
	public sealed class ProtocolTriggerImpact
	{
		public int ProtocolId { get; set; }
		public string ProtocolCode { get; set; }
		public string ProtocolName { get; set; }
		public int TriggerId { get; set; }
		public int TriggerType { get; set; }
	}

	/// <summary>A protocol's use on calls since a point in time (the library's "used on calls" tile and column).</summary>
	public sealed class ProtocolUsageCount
	{
		public int DispatchProtocolId { get; set; }
		public int Calls { get; set; }
	}
}
