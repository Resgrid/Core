using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The smoke / CO alarm request queue (M0284) inside the CRR module. A request is Open until completing it records the
	/// CRR activity for the units left (the yearly alarm counts come from those activities) or the department declines it.
	/// Every state change claims the row by RowVersion first, so a double-submitted completion records one activity, and an
	/// edit saved after the request closed is refused instead of reopening it.
	/// </summary>
	public class RecordsAlarmRequestsService : IRecordsAlarmRequestsService
	{
		private const string ConflictMessage = "The request was changed by someone else; reload it and try again.";

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsCrrAlarmRequestsRepository _requests;
		private readonly IRmsCrrActivitiesRepository _activities;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IContactsRepository _contacts;
		private readonly ICallsRepository _calls;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsAlarmRequestsService(RecordsPreventionGate gate, IRmsCrrAlarmRequestsRepository requests, IRmsCrrActivitiesRepository activities, IRmsOccupanciesRepository occupancies,
			IContactsRepository contacts, ICallsRepository calls, IRecordsAuthorizationService authorization, IUnitOfWork unitOfWork)
		{
			_gate = gate;
			_requests = requests;
			_activities = activities;
			_occupancies = occupancies;
			_contacts = contacts;
			_calls = calls;
			_authorization = authorization;
			_unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Crr);
		private async Task RequireViewAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireViewerAsync(departmentId, userId); }
		private async Task RequireAdminAsync(int departmentId, string userId) { await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Crr); await _gate.RequireAdminAsync(departmentId, userId); }

		public async Task<List<RmsCrrAlarmRequest>> ListAsync(int departmentId, string userId, RmsAlarmRequestQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			query = RequireStorableQuery(query);
			return (await _requests.QueryAsync(departmentId, query))?.ToList() ?? new List<RmsCrrAlarmRequest>();
		}

		public async Task<int> CountAsync(int departmentId, string userId, RmsAlarmRequestQuery query)
		{
			await RequireViewAsync(departmentId, userId);
			return await _requests.CountAsync(departmentId, RequireStorableQuery(query));
		}

		public async Task<RmsCrrAlarmRequest> GetAsync(int departmentId, string userId, string requestId)
		{
			await RequireViewAsync(departmentId, userId);
			return await LiveAsync(departmentId, requestId);
		}

		public async Task<List<RmsCrrAlarmRequest>> GetForOccupancyAsync(int departmentId, string userId, string occupancyId)
		{
			await RequireViewAsync(departmentId, userId);
			if (string.IsNullOrWhiteSpace(occupancyId)) return new List<RmsCrrAlarmRequest>();
			return ((await _requests.GetForOccupancyAsync(departmentId, occupancyId)) ?? Enumerable.Empty<RmsCrrAlarmRequest>()).Where(r => r != null && r.DeletedOn == null).ToList();
		}

		public async Task<RmsCrrAlarmRequest> SaveAsync(int departmentId, string userId, RmsCrrAlarmRequest input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			// A blank id creates a request. An id that no longer loads (deleted, or not this department's) is refused rather than
			// saved as a new request, so an edit to a request removed meanwhile does not bring it back under a new number.
			var existing = string.IsNullOrWhiteSpace(input.RmsCrrAlarmRequestId) ? null
				: await LiveAsync(departmentId, input.RmsCrrAlarmRequestId) ?? throw new ArgumentException("The request does not exist.");
			var isNew = existing == null;
			if (!isNew)
			{
				if (!existing.IsOpen()) throw new InvalidOperationException("Only an open request can be changed.");
				if (input.RowVersion > 0 && input.RowVersion != existing.RowVersion) throw new InvalidOperationException(ConflictMessage);
			}

			// Ids posted by the client are checked against the department when they are set or changed.
			RmsOccupancy occupancy = null;
			var occupancyId = RecordsPreventionGate.Trim(input.RmsOccupancyId, 36);
			if (occupancyId != null) occupancy = await CrrAlarmRules.RequireOccupancyAsync(_occupancies, departmentId, occupancyId);
			var contactId = RecordsPreventionGate.Trim(input.RequesterContactId, 128);
			if (contactId != null && (isNew || existing.RequesterContactId != contactId)) await CrrAlarmRules.RequireContactAsync(_contacts, departmentId, contactId);
			if (input.CallId.HasValue && (isNew || existing.CallId != input.CallId)) await CrrAlarmRules.RequireCallAsync(_calls, _authorization, departmentId, userId, input.CallId.Value);
			var assignee = RecordsPreventionGate.Trim(input.AssignedToUserId, 128);
			if (assignee != null && (isNew || existing.AssignedToUserId != assignee) && !await _gate.IsAssignableAsync(departmentId, assignee))
				throw new ArgumentException("The assignee is not an active member of this department.");

			var location = RecordsPreventionGate.Trim(input.LocationText, 500);
			if (location == null && occupancy != null) location = RecordsPreventionGate.Trim(string.Join(", ", new[] { occupancy.AddressText, occupancy.City }.Where(s => !string.IsNullOrWhiteSpace(s))), 500);
			if (occupancy == null && location == null && contactId == null)
				throw new ArgumentException("A request needs an occupancy, a location or a contact so the crew can find the home.");

			var now = DateTime.UtcNow;
			var entity = existing ?? new RmsCrrAlarmRequest
			{
				RmsCrrAlarmRequestId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				Status = (int)RmsAlarmRequestStatus.Open, CreatedOn = now, CreatedByUserId = userId, RowVersion = 1
			};
			entity.Source = CrrAlarmRules.Source(input.Source);
			entity.Reason = CrrAlarmRules.Reason(input.Reason);
			entity.RequestedOn = RecordsPreventionGate.RequireStorableDate(input.RequestedOn == default ? (existing?.RequestedOn ?? now) : input.RequestedOn, "The request date is not valid.");
			entity.SmokeAlarmsRequested = CrrAlarmRules.Units(input.SmokeAlarmsRequested, "Smoke alarms");
			entity.CoAlarmsRequested = CrrAlarmRules.Units(input.CoAlarmsRequested, "CO alarms");
			entity.ComboAlarmsRequested = CrrAlarmRules.Units(input.ComboAlarmsRequested, "Combination alarms");
			entity.RmsOccupancyId = occupancyId;
			entity.LocationText = location;
			entity.Latitude = input.Latitude ?? (input.Longitude.HasValue ? null : occupancy?.Latitude);
			entity.Longitude = input.Longitude ?? (input.Latitude.HasValue ? null : occupancy?.Longitude);
			entity.RequesterContactId = contactId;
			entity.CallId = input.CallId;
			entity.Notes = RecordsPreventionGate.Trim(input.Notes, 4000);
			entity.ScheduledFor = RecordsPreventionGate.RequireStorableDate(input.ScheduledFor, "The visit date is not valid.");
			entity.AssignedToUserId = assignee;
			entity.ModifiedOn = now; entity.ModifiedByUserId = userId;

			if (isNew)
			{
				entity.RequestNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.AlarmRequest, now, cancellationToken);
				await _requests.InsertAsync(entity, cancellationToken, true);
			}
			else
			{
				await InTransactionAsync(async () =>
				{
					await ClaimAsync(entity, cancellationToken);
					await _requests.UpdateAsync(entity, cancellationToken, true);
				});
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, isNew ? "Alarm request recorded" : "Alarm request updated", entity.RmsCrrAlarmRequestId,
				new { entity.RequestNumber, reason = ((RmsAlarmReason)entity.Reason).ToString(), requested = entity.AlarmsRequestedTotal() }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task<AlarmRequestCompletion> CompleteAsync(int departmentId, string userId, AlarmRequestCompletionInput input, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var request = await LiveAsync(departmentId, input.RequestId) ?? throw new ArgumentException("The request does not exist.");
			if (!request.IsOpen()) throw new InvalidOperationException("The request is already closed.");

			var now = DateTime.UtcNow;
			var activity = new RmsCrrActivity
			{
				RmsCrrActivityId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation,
				OccurredOn = RecordsPreventionGate.RequireStorableDate(input.OccurredOn ?? now, "The installation date is not valid."),
				Title = RecordsPreventionGate.Trim("Alarm request " + request.RequestNumber, 250),
				RmsOccupancyId = request.RmsOccupancyId, LocationText = request.LocationText, Latitude = request.Latitude, Longitude = request.Longitude,
				SmokeAlarmsInstalled = CrrAlarmRules.Units(input.SmokeAlarmsInstalled, "Smoke alarms"),
				CoAlarmsInstalled = CrrAlarmRules.Units(input.CoAlarmsInstalled, "CO alarms"),
				ComboAlarmsInstalled = CrrAlarmRules.Units(input.ComboAlarmsInstalled, "Combination alarms"),
				AlarmReason = input.AlarmReason == (int)RmsAlarmReason.Unspecified ? request.Reason : CrrAlarmRules.Reason(input.AlarmReason),
				CallId = request.CallId, RmsCrrAlarmRequestId = request.RmsCrrAlarmRequestId,
				HoursSpent = Math.Max(0, input.HoursSpent),
				StaffUserIdsCsv = RecordsPreventionGate.Trim(input.StaffUserIdsCsv, 4000), Outcome = RecordsPreventionGate.Trim(input.Outcome, 4000),
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1
			};
			if (activity.AlarmsInstalledTotal() == 0) throw new ArgumentException("Record at least one alarm left, or decline the request.");

			await InTransactionAsync(async () =>
			{
				await ClaimAsync(request, cancellationToken);
				await _activities.InsertAsync(activity, cancellationToken, true);
				request.Status = (int)RmsAlarmRequestStatus.Completed; request.ClosedOn = now; request.ClosedByUserId = userId; request.RmsCrrActivityId = activity.RmsCrrActivityId;
				request.DeclineReason = (int)RmsAlarmRequestDeclineReason.None; request.ModifiedOn = now; request.ModifiedByUserId = userId;
				await _requests.UpdateAsync(request, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Alarm request completed", request.RmsCrrAlarmRequestId,
					new { request.RequestNumber, activityId = activity.RmsCrrActivityId, smoke = activity.SmokeAlarmsInstalled, co = activity.CoAlarmsInstalled, combo = activity.ComboAlarmsInstalled }, cancellationToken: cancellationToken);
			});
			return new AlarmRequestCompletion { Request = request, Activity = activity };
		}

		public async Task<RmsCrrAlarmRequest> DeclineAsync(int departmentId, string userId, string requestId, RmsAlarmRequestDeclineReason reason, string notes, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var request = await LiveAsync(departmentId, requestId) ?? throw new ArgumentException("The request does not exist.");
			if (!request.IsOpen()) throw new InvalidOperationException("The request is already closed.");
			if (reason == RmsAlarmRequestDeclineReason.None || !Enum.IsDefined(typeof(RmsAlarmRequestDeclineReason), reason)) throw new ArgumentException("Choose why the request is declined.");
			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await ClaimAsync(request, cancellationToken);
				request.Status = (int)RmsAlarmRequestStatus.Declined; request.DeclineReason = (int)reason; request.ResolutionNotes = RecordsPreventionGate.Trim(notes, 4000);
				request.ClosedOn = now; request.ClosedByUserId = userId; request.ModifiedOn = now; request.ModifiedByUserId = userId;
				await _requests.UpdateAsync(request, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Alarm request declined", request.RmsCrrAlarmRequestId, new { request.RequestNumber, reason = reason.ToString() }, cancellationToken: cancellationToken);
			});
			return request;
		}

		public async Task<RmsCrrAlarmRequest> ReopenAsync(int departmentId, string userId, string requestId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var request = await LiveAsync(departmentId, requestId) ?? throw new ArgumentException("The request does not exist.");
			if (request.Status == (int)RmsAlarmRequestStatus.Completed) throw new InvalidOperationException("A completed request reopens when its activity is deleted.");
			if (request.IsOpen()) throw new InvalidOperationException("The request is already open.");
			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await ClaimAsync(request, cancellationToken);
				request.Status = (int)RmsAlarmRequestStatus.Open; request.DeclineReason = (int)RmsAlarmRequestDeclineReason.None; request.ClosedOn = null; request.ClosedByUserId = null;
				request.ModifiedOn = now; request.ModifiedByUserId = userId;
				await _requests.UpdateAsync(request, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Alarm request reopened", request.RmsCrrAlarmRequestId, new { request.RequestNumber }, cancellationToken: cancellationToken);
			});
			return request;
		}

		public async Task DeleteAsync(int departmentId, string userId, string requestId, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			var request = await LiveAsync(departmentId, requestId) ?? throw new ArgumentException("The request does not exist.");
			if (request.Status == (int)RmsAlarmRequestStatus.Completed) throw new InvalidOperationException("A completed request stays with its activity; delete the activity to reopen it first.");
			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await ClaimAsync(request, cancellationToken);
				request.DeletedOn = now; request.ModifiedOn = now; request.ModifiedByUserId = userId;
				await _requests.UpdateAsync(request, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Alarm request removed", request.RmsCrrAlarmRequestId, new { request.RequestNumber }, cancellationToken: cancellationToken);
			});
		}

		private async Task<RmsCrrAlarmRequest> LiveAsync(int departmentId, string requestId)
		{
			if (string.IsNullOrWhiteSpace(requestId)) return null;
			var request = await _requests.GetByIdForDepartmentAsync(departmentId, requestId);
			return request == null || request.DeletedOn != null ? null : request;
		}

		/// <summary>Moves the stored RowVersion on by one, or refuses when another change got there first.</summary>
		private async Task ClaimAsync(RmsCrrAlarmRequest request, CancellationToken cancellationToken)
		{
			if (!await _requests.TryBumpRowVersionAsync(request.DepartmentId, request.RmsCrrAlarmRequestId, request.RowVersion, cancellationToken))
				throw new InvalidOperationException(ConflictMessage);
			request.RowVersion++;
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		private static RmsAlarmRequestQuery RequireStorableQuery(RmsAlarmRequestQuery query)
		{
			query ??= new RmsAlarmRequestQuery();
			RecordsPreventionGate.RequireStorableDate(query.RequestedFrom, "The start date is not valid.");
			RecordsPreventionGate.RequireStorableDate(query.RequestedBefore, "The end date is not valid.");
			return query;
		}
	}
}
