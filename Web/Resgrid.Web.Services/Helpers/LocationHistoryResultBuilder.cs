using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Calls;

namespace Resgrid.Web.Helpers
{
	/// <summary>Shapes a <see cref="CallLocationHistoryResult"/> for the v4 API, through the attended protected read.</summary>
	public static class LocationHistoryResultBuilder
	{
		public static async Task<LocationHistoryResultData> BuildAsync(CallLocationHistoryResult history, int departmentId, string grantToken, string userId,
			IProtectedReadService protectedReads, ICallsService callsService, IDepartmentsService departmentsService)
		{
			var data = new LocationHistoryResultData();
			if (history == null)
				return data;

			data.AddressMatchingAvailable = history.AddressMatchingAvailable;
			data.IndexComplete = history.IndexComplete;
			data.HasMore = history.HasMore;
			data.InterpretedAddress = history.InterpretedAddress;
			if (history.Entries.Count == 0)
				return data;

			// Attended protected read (plan 7.1): call text and notes decrypt with a valid grant or read as REDACTED.
			var reads = await protectedReads.ResolveForReadAsync(departmentId, history.Entries.Select(e => e.Call).ToList(), grantToken, userId);
			var revealed = reads.Where(r => r.Call != null).GroupBy(r => r.Call.CallId).ToDictionary(g => g.Key, g => g.First());
			var notes = history.Entries.SelectMany(e => e.Notes).ToList();
			var notesRead = notes.Count > 0 ? await protectedReads.ResolveNotesForReadAsync(departmentId, notes, grantToken, userId) : null;
			var firstProtected = reads.FirstOrDefault(r => r.IsProtected);
			data.IsProtected = firstProtected != null || notesRead?.IsProtected == true;
			data.ProtectedReason = firstProtected?.ProtectedReason ?? notesRead?.ProtectedReason;

			var department = await departmentsService.GetDepartmentByIdAsync(departmentId);
			var names = notes.Count > 0
				? ((await departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId)) ?? new List<PersonName>())
					.Where(n => n.UserId != null).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var priorities = new Dictionary<int, DepartmentCallPriority>();

			foreach (var entry in history.Entries)
			{
				var call = revealed.TryGetValue(entry.Call.CallId, out var read) ? read.Call : entry.Call;
				if (!priorities.TryGetValue(call.Priority, out var priority))
					priorities[call.Priority] = priority = await callsService.GetCallPrioritiesByIdAsync(departmentId, call.Priority);

				var item = new LocationHistoryCallData
				{
					CallId = call.CallId.ToString(),
					Number = call.Number,
					Name = ProtectedDataEnvelope.SafeDisplay(call.Name),
					Nature = ProtectedDataEnvelope.SafeDisplay(call.NatureOfCall),
					Address = ProtectedDataEnvelope.SafeDisplay(call.Address),
					Type = call.Type,
					Priority = call.Priority,
					PriorityText = priority?.Name,
					PriorityColor = priority?.Color,
					State = call.State,
					LoggedOnUtc = DateTime.SpecifyKind(call.LoggedOn, DateTimeKind.Utc),
					LoggedOn = department == null ? null : call.LoggedOn.TimeConverter(department).FormatForDepartment(department),
					ClosedOnUtc = call.ClosedOn.HasValue ? DateTime.SpecifyKind(call.ClosedOn.Value, DateTimeKind.Utc) : (DateTime?)null,
					CompletedNotes = ProtectedDataEnvelope.SafeDisplay(call.CompletedNotes),
					DistanceMeters = entry.DistanceMeters.HasValue ? Math.Round(entry.DistanceMeters.Value, 1) : (double?)null
				};

				foreach (CallLocationMatch flag in new[] { CallLocationMatch.SameContact, CallLocationMatch.SameAddress, CallLocationMatch.SimilarAddress, CallLocationMatch.Nearby })
					if (entry.Match.HasFlag(flag))
						item.Matches.Add(flag.ToString());

				foreach (var note in entry.Notes)
				{
					item.Notes.Add(new LocationHistoryNoteData
					{
						CallNoteId = note.CallNoteId.ToString(),
						UserId = note.UserId,
						FullName = note.UserId != null && names.TryGetValue(note.UserId, out var name) ? name : null,
						Note = ProtectedDataEnvelope.SafeDisplay(note.Note),
						TimestampUtc = DateTime.SpecifyKind(note.Timestamp, DateTimeKind.Utc)
					});
				}

				data.Calls.Add(item);
			}

			return data;
		}
	}
}
