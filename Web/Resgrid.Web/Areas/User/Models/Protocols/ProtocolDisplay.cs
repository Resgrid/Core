using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>Protocol triggers in words, for the library, the detail page and the call forms' reason chips.</summary>
	public static class ProtocolDisplay
	{
		/// <summary>The resource, or its key when there is no localizer or no resource (tests, a key not yet translated).</summary>
		private static string L(IStringLocalizer localizer, string key) => localizer?[key]?.Value ?? key;

		public static string PriorityName(int? priorityId, IEnumerable<DepartmentCallPriority> priorities, IStringLocalizer localizer)
		{
			if (!priorityId.HasValue)
				return L(localizer, "NotApplicableLabel");

			var priority = priorities?.FirstOrDefault(x => x.DepartmentCallPriorityId == priorityId.Value);
			if (priority == null)
				return L(localizer, "RemovedPriority");

			return priority.IsDeleted ? string.Format(L(localizer, "RemovedNamed"), priority.Name) : priority.Name;
		}

		public static string TypeName(DispatchProtocolTrigger trigger, IEnumerable<CallType> types, IStringLocalizer localizer)
		{
			if (ProtocolTriggerMatcher.IsNoTypeTrigger(trigger))
				return L(localizer, "NoTypeOption");

			if (trigger.CallTypeId.HasValue)
			{
				var type = types?.FirstOrDefault(x => x.CallTypeId == trigger.CallTypeId.Value);
				if (type != null)
					return type.Type;

				// The type was deleted since: the stored name, marked.
				return string.Format(L(localizer, "RemovedNamed"), trigger.CallType ?? trigger.CallTypeId.Value.ToString());
			}

			return trigger.CallType;
		}

		/// <summary>"Priority: High", "Type: Structure Fire" or "Structure Fire · priority High".</summary>
		public static string Describe(DispatchProtocolTrigger trigger, IEnumerable<DepartmentCallPriority> priorities, IEnumerable<CallType> types, IStringLocalizer localizer)
		{
			switch ((ProtocolTriggerTypes)trigger.Type)
			{
				case ProtocolTriggerTypes.CallPriorty:
					return string.Format(L(localizer, "TriggerSummaryPriority"), PriorityName(trigger.Priority, priorities, localizer));
				case ProtocolTriggerTypes.CallType:
					return string.Format(L(localizer, "TriggerSummaryType"), TypeName(trigger, types, localizer));
				case ProtocolTriggerTypes.CallPriortyAndType:
					return string.Format(L(localizer, "TriggerSummaryPriorityAndType"), TypeName(trigger, types, localizer), PriorityName(trigger.Priority, priorities, localizer));
				default:
					return L(localizer, "NotApplicableLabel");
			}
		}

		/// <summary>The reason chip a suggested protocol shows: which trigger matched.</summary>
		public static string Reason(DispatchProtocolTrigger matched, IEnumerable<DepartmentCallPriority> priorities, IEnumerable<CallType> types, IStringLocalizer localizer)
		{
			if (matched == null)
				return string.Empty;

			return string.Format(L(localizer, "ReasonMatched"), Describe(matched, priorities, types, localizer));
		}

		public static string TriggerTypeName(int type, IStringLocalizer localizer) => (ProtocolTriggerTypes)type switch
		{
			ProtocolTriggerTypes.CallPriorty => L(localizer, "CallPriorityTrigger"),
			ProtocolTriggerTypes.CallType => L(localizer, "CallTypeTrigger"),
			ProtocolTriggerTypes.CallPriortyAndType => L(localizer, "CallPriorityAndTypeTrigger"),
			_ => L(localizer, "NotApplicableLabel")
		};
	}
}
