using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	/// <summary>The location history widget's payload (Views/Shared/_CallLocationHistory, resgrid.dispatch.locationhistory.js).</summary>
	public class CallLocationHistoryJson
	{
		public bool AddressMatchingAvailable { get; set; }
		public bool IndexComplete { get; set; }
		public bool HasMore { get; set; }
		public string InterpretedAddress { get; set; }
		public List<CallLocationHistoryEntryJson> Entries { get; set; } = new List<CallLocationHistoryEntryJson>();

		/// <summary>
		/// Shapes a history for display: department-local times, priority names and colors, localized state and match
		/// labels, note authors. Protected values (an Advanced Data Protection department without a grant) show the
		/// redaction placeholder, as every other server-rendered call list does.
		/// </summary>
		public static async Task<CallLocationHistoryJson> FromAsync(CallLocationHistoryResult result, Department department, ICallsService callsService,
			IDepartmentsService departmentsService, IStringLocalizer localizer)
		{
			var json = new CallLocationHistoryJson();
			if (result == null || department == null)
				return json;

			json.AddressMatchingAvailable = result.AddressMatchingAvailable;
			json.IndexComplete = result.IndexComplete;
			json.HasMore = result.HasMore;
			json.InterpretedAddress = result.InterpretedAddress;

			if (result.Entries.Count == 0)
				return json;

			var priorities = ((await callsService.GetCallPrioritiesForDepartmentAsync(department.DepartmentId)) ?? new List<DepartmentCallPriority>())
				.Where(p => p.DepartmentCallPriorityId > 3)
				.Concat(callsService.GetDefaultCallPriorities() ?? new List<DepartmentCallPriority>())
				.GroupBy(p => p.DepartmentCallPriorityId).ToDictionary(g => g.Key, g => g.First());

			var names = result.Entries.Any(e => e.Notes.Count > 0)
				? ((await departmentsService.GetAllPersonnelNamesForDepartmentAsync(department.DepartmentId)) ?? new List<PersonName>())
					.Where(n => n.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var entry in result.Entries)
			{
				var call = entry.Call;
				priorities.TryGetValue(call.Priority, out var priority);

				var item = new CallLocationHistoryEntryJson
				{
					CallId = call.CallId,
					Number = call.Number,
					Name = ProtectedDataEnvelope.SafeDisplay(call.Name),
					Nature = ProtectedDataEnvelope.SafeDisplay(call.NatureOfCall),
					Address = ProtectedDataEnvelope.SafeDisplay(call.Address),
					Type = ProtectedDataEnvelope.SafeDisplay(call.Type),
					LoggedOn = call.LoggedOn.TimeConverter(department).FormatForDepartment(department),
					LoggedOnSort = call.LoggedOn.ToString("o", CultureInfo.InvariantCulture),
					ClosedOn = call.ClosedOn.HasValue ? call.ClosedOn.Value.TimeConverter(department).FormatForDepartment(department) : null,
					IsActive = call.State == (int)CallStates.Active,
					State = StateName(call.State, localizer),
					PriorityName = priority?.Name ?? localizer["UnknownPriority"].Value,
					PriorityColor = string.IsNullOrWhiteSpace(priority?.Color) ? "#777777" : priority.Color,
					CompletedNotes = ProtectedDataEnvelope.SafeDisplay(call.CompletedNotes),
					Distance = entry.DistanceMeters.HasValue && entry.DistanceMeters.Value >= 1
						? string.Format(CultureInfo.CurrentCulture, localizer["MetersAway"].Value, Math.Round(entry.DistanceMeters.Value))
						: null
				};

				if (entry.Match.HasFlag(CallLocationMatch.SameAddress)) item.Matches.Add(new CallLocationMatchJson("address", localizer["MatchSameAddress"].Value));
				if (entry.Match.HasFlag(CallLocationMatch.SimilarAddress)) item.Matches.Add(new CallLocationMatchJson("similar", localizer["MatchSimilarAddress"].Value));
				if (entry.Match.HasFlag(CallLocationMatch.Nearby)) item.Matches.Add(new CallLocationMatchJson("nearby", localizer["MatchNearby"].Value));
				if (entry.Match.HasFlag(CallLocationMatch.SameContact)) item.Matches.Add(new CallLocationMatchJson("contact", localizer["MatchSameContact"].Value));

				foreach (var note in entry.Notes)
				{
					item.Notes.Add(new CallLocationHistoryNoteJson
					{
						Name = note.UserId != null && names.TryGetValue(note.UserId, out var name) ? name : localizer["UnknownUser"].Value,
						Timestamp = note.Timestamp.TimeConverter(department).FormatForDepartment(department),
						Note = ProtectedDataEnvelope.SafeDisplay(note.Note)
					});
				}

				json.Entries.Add(item);
			}

			return json;
		}

		private static string StateName(int state, IStringLocalizer localizer)
		{
			switch ((CallStates)state)
			{
				case CallStates.Active: return localizer["StateActive"].Value;
				case CallStates.Closed: return localizer["StateClosed"].Value;
				case CallStates.Cancelled: return localizer["StateCancelled"].Value;
				case CallStates.Unfounded: return localizer["StateUnfounded"].Value;
				case CallStates.Founded: return localizer["StateFounded"].Value;
				case CallStates.Minor: return localizer["StateMinor"].Value;
				default: return state.ToString(CultureInfo.InvariantCulture);
			}
		}
	}

	public class CallLocationHistoryEntryJson
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public string Nature { get; set; }
		public string Address { get; set; }
		public string Type { get; set; }
		public string LoggedOn { get; set; }
		public string LoggedOnSort { get; set; }
		public string ClosedOn { get; set; }
		public bool IsActive { get; set; }
		public string State { get; set; }
		public string PriorityName { get; set; }
		public string PriorityColor { get; set; }
		public string CompletedNotes { get; set; }
		public string Distance { get; set; }
		public List<CallLocationMatchJson> Matches { get; set; } = new List<CallLocationMatchJson>();
		public List<CallLocationHistoryNoteJson> Notes { get; set; } = new List<CallLocationHistoryNoteJson>();
	}

	public class CallLocationMatchJson
	{
		public CallLocationMatchJson(string kind, string label)
		{
			Kind = kind;
			Label = label;
		}

		/// <summary>address, similar, nearby or contact; the widget colors the badge by it.</summary>
		public string Kind { get; set; }
		public string Label { get; set; }
	}

	public class CallLocationHistoryNoteJson
	{
		public string Name { get; set; }
		public string Timestamp { get; set; }
		public string Note { get; set; }
	}
}
