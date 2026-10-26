using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CustomStateService : ICustomStateService
	{
		private static string CacheKey = "CustomStates_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(7);

		private readonly ICustomStateRepository _customStateRepository;
		private readonly ICustomStateDetailRepository _customStateDetailRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICacheProvider _cacheProvider;

		public CustomStateService(ICustomStateRepository customStateRepository, ICustomStateDetailRepository customStateDetailRepository,
			IEventAggregator eventAggregator, ICacheProvider cacheProvider)
		{
			_customStateRepository = customStateRepository;
			_customStateDetailRepository = customStateDetailRepository;
			_eventAggregator = eventAggregator;
			_cacheProvider = cacheProvider;
		}

		public async Task<CustomState> GetCustomSateByIdAsync(int customStateId)
		{
			return await _customStateRepository.GetCustomStatesByIdAsync(customStateId);
		}

		public async Task<List<CustomState>> GetAllActiveCustomStatesForDepartmentAsync(int departmentId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false).ToList();

			return null;
		}

		public async Task<List<CustomState>> GetAllActiveUnitStatesForDepartmentAsync(int departmentId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				return states.Where(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Unit).ToList();

			return null;
		}

		public async Task<int?> ResolvePersonnelStatusAsync(int departmentId, ActionBaseTypes baseType, DestinationEntityTypes destination = DestinationEntityTypes.None)
		{
			var personnelSet = await GetActivePersonnelStateForDepartmentAsync(departmentId);

			return StatusTargetResolver.ResolvePersonnel(StatusTarget.ForBaseType(baseType), personnelSet, null, (int)ActionTypes.StandingBy, destination).Value;
		}

		public async Task<CustomState> GetActivePersonnelStateForDepartmentAsync(int departmentId)
		{
			CustomState state = null;
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				state = states.FirstOrDefault(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Personnel);

			if (state != null)
			{
				if (state.Details == null)
					state.Details = new List<CustomStateDetail>();

				return state;
			}

			return null;
		}

		public async Task<List<CustomStateDetail>> GetCustomPersonnelStatusesOrDefaultsAsync(int departmentId)
		{
			var statuses = await GetActivePersonnelStateForDepartmentAsync(departmentId);

			if (statuses != null && statuses.GetActiveDetails().Any())
			{
				return statuses.GetActiveDetails();
			}
			else
			{
				return GetDefaultPersonStatuses();
			}
		}

		public async Task<List<CustomStateDetail>> GetCustomPersonnelStaffingsOrDefaultsAsync(int departmentId)
		{
			var statuses = await GetActiveStaffingLevelsForDepartmentAsync(departmentId);

			if (statuses != null && statuses.GetActiveDetails().Any())
			{
				return statuses.GetActiveDetails();
			}
			else
			{
				return GetDefaultPersonStaffings();
			}
		}

		public async Task<CustomState> GetActiveStaffingLevelsForDepartmentAsync(int departmentId)
		{
			CustomState state = null;
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null)
				state = states.FirstOrDefault(x => x.DepartmentId == departmentId && x.IsDeleted == false && x.Type == (int)CustomStateTypes.Staffing);

			if (state != null)
			{
				if (state.Details == null)
					state.Details = new List<CustomStateDetail>();

				return state;
			}

			return null;
		}

		public async Task<List<CustomState>> GetAllCustomStatesForDepartmentAsync(int departmentId)
		{
			async Task<List<CustomState>> getCustomStates()
			{
				var states = await _customStateRepository.GetCustomStatesByDepartmentIdAsync(departmentId);

				if (states != null)
				{
					foreach (var state in states)
					{
						if (state.Details == null)
							state.Details = new List<CustomStateDetail>();
					}

					return states.ToList();
				}
				else
				{
					return new List<CustomState>();
				}
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				var result = await _cacheProvider.RetrieveAsync<List<CustomState>>(string.Format(CacheKey, departmentId), getCustomStates, CacheLength);

				if (result == null || result.Count() <= 0)
					return await getCustomStates();

				return result;
			}
			else
				return await getCustomStates();
		}

		public void InvalidateCustomStateInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public Task<CustomState> SaveAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken))
			=> SaveAsync(customState, Resgrid.Model.Onboarding.ImportEffectPolicy.Interactive, cancellationToken);

		public async Task<CustomState> SaveAsync(CustomState customState, Resgrid.Model.Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken)
		{
			var saved = await _customStateRepository.SaveOrUpdateAsync(customState, cancellationToken);

			await _cacheProvider.RemoveAsync(string.Format(CacheKey, customState.DepartmentId));

			// An import resyncs the apps once per run instead (wizard plan section 11.5).
			if (effects.RaiseDomainEvents)
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = customState.DepartmentId });

			return saved;
		}

		public async Task<CustomStateDetail> GetCustomDetailByIdAsync(int detailId)
		{
			return await _customStateDetailRepository.GetByIdAsync(detailId);
		}

		public Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
			=> SaveDetailAsync(customStateDetail, departmentId, Resgrid.Model.Onboarding.ImportEffectPolicy.Interactive, cancellationToken);

		public async Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, int departmentId, Resgrid.Model.Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken)
		{
			var saved = await _customStateDetailRepository.SaveOrUpdateAsync(customStateDetail, cancellationToken);

			// Details are served from the department's cached custom states; without this an edited status keeps its
			// old text, colour and destination setting in the apps and call records for up to the 7 day cache length.
			await _cacheProvider.RemoveAsync(string.Format(CacheKey, departmentId));

			if (effects.RaiseDomainEvents)
				_eventAggregator.SendMessage<DepartmentSettingsUpdateEvent>(new DepartmentSettingsUpdateEvent() { DepartmentId = departmentId });

			return saved;
		}

		public async Task<CustomState> DeleteAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken))
		{
			customState.IsDeleted = true;

			foreach (var existingDetails in customState.Details)
			{
				await DeleteDetailAsync(existingDetails, cancellationToken);
			}

			return await SaveAsync(customState, cancellationToken);
		}

		public async Task<CustomStateDetail> DeleteDetailAsync(CustomStateDetail detail, CancellationToken cancellationToken = default(CancellationToken))
		{
			detail.IsDeleted = true;

			return await _customStateDetailRepository.SaveOrUpdateAsync(detail, cancellationToken);
		}

		public async Task<CustomState> UpdateAsync(CustomState state, List<CustomStateDetail> details, CancellationToken cancellationToken = default(CancellationToken))
		{
			var missingDetails = state.Details.Where(p => !details.Any(p2 => p2.CustomStateDetailId == p.CustomStateDetailId)).ToList();

			if (missingDetails != null && missingDetails.Any())
			{
				foreach (var missingDetail in missingDetails)
				{
					await DeleteDetailAsync(missingDetail, cancellationToken);
					state.Details.Remove(missingDetail);
				}
			}

			foreach (var detail in details)
			{
				detail.CustomStateId = state.CustomStateId;
				if (detail.CustomStateDetailId == 0)
				{
					state.Details.Add(detail);
				}
				else
				{
					var existingDetail = state.Details.FirstOrDefault(x => x.CustomStateDetailId == detail.CustomStateDetailId);

					if (existingDetail != null)
					{
						existingDetail.ButtonText = detail.ButtonText;
						existingDetail.ButtonColor = detail.ButtonColor;
						existingDetail.TextColor = detail.TextColor;
						existingDetail.GpsRequired = detail.GpsRequired;
						existingDetail.NoteType = detail.NoteType;
						existingDetail.DetailType = detail.DetailType;
						existingDetail.Order = detail.Order;
						existingDetail.BaseType = detail.BaseType;
					}
				}
			}

			return await SaveAsync(state, cancellationToken);
		}

		public async Task<CustomStateDetail> GetCustomDetailForDepartmentAsync(int departmentId, int detailId)
		{
			var states = await GetAllCustomStatesForDepartmentAsync(departmentId);

			if (states != null && states.Count > 0)
			{
				var detail = states.Where(state => state.Details != null).Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == detailId)).FirstOrDefault(detail => detail != null);
				return detail;
			}

			return null;
		}

		// A stored value is the department's own option when it is one of its custom option ids of that kind (deleted ones
		// included, so history still resolves); otherwise it is a built-in value. On a fresh install custom ids start at 1,
		// so the old "<= 25 is built-in" cut-off read a department's first custom options as built-in statuses.
		private async Task<CustomStateDetail> FindDepartmentOptionAsync(int departmentId, CustomStateTypes type, int rawValue)
		{
			return StatusOptionIds.FindCustom(await GetAllCustomStatesForDepartmentAsync(departmentId), type, rawValue);
		}

		public async Task<CustomStateDetail> GetCustomPersonnelStaffingAsync(int departmentId, UserState state)
		{
			if (state == null)
				return null;

			var custom = await FindDepartmentOptionAsync(departmentId, CustomStateTypes.Staffing, state.State);
			if (custom != null)
				return custom;

			if (state.State > CallStatusLinkage.MaxBuiltInStatusId)
				return null;

			var detail = new CustomStateDetail();

			detail.ButtonText = state.GetStaffingText();
			detail.ButtonColor = state.GetStaffingCss();
			detail.BaseType = (int)BuiltInStatusBaseTypes.ForStaffing(state.State);

			if (string.IsNullOrWhiteSpace(detail.ButtonColor))
				detail.ButtonColor = "label-default";

			return detail;
		}

		public async Task<CustomStateDetail> GetCustomPersonnelStatusAsync(int departmentId, ActionLog state)
		{
			if (state == null)
				return null;

			var custom = await FindDepartmentOptionAsync(departmentId, CustomStateTypes.Personnel, state.ActionTypeId);
			if (custom != null)
				return custom;

			if (state.ActionTypeId > CallStatusLinkage.MaxBuiltInStatusId)
				return null;

			var detail = new CustomStateDetail();

			detail.ButtonText = state.GetActionText();
			detail.ButtonColor = state.GetActionCss();
			detail.BaseType = (int)BuiltInStatusBaseTypes.ForPersonnelStatus(state.ActionTypeId);

			if (string.IsNullOrWhiteSpace(detail.ButtonColor))
				detail.ButtonColor = "label-default";

			return detail;
		}

		public async Task<int?> ResolvePersonnelStaffingAsync(int departmentId, StaffingBaseTypes baseType)
		{
			if (baseType == StaffingBaseTypes.None)
				return null;

			var staffingSet = await GetActiveStaffingLevelsForDepartmentAsync(departmentId);
			var options = staffingSet?.GetActiveDetails();
			if (options != null && options.Any())
				return options.FirstOrDefault(x => x.BaseType == (int)baseType)?.CustomStateDetailId;

			// The built-in levels Available..On Shift carry the same numbers as their meanings; Off Duty and On Call have none.
			return Enum.IsDefined(typeof(UserStateTypes), (int)baseType) ? (int)baseType : (int?)null;
		}

		public async Task<int?> ResolveStatusResetAsync(int departmentId, string target)
		{
			var meaning = StatusMatch.BaseTypeOf(target);
			if (meaning.HasValue)
				return meaning.Value != (int)ActionBaseTypes.None && Enum.IsDefined(typeof(ActionBaseTypes), meaning.Value)
					? await ResolvePersonnelStatusAsync(departmentId, (ActionBaseTypes)meaning.Value)
					: null;

			if (!int.TryParse(target?.Trim(), out var raw))
				return null;

			// A value saved before meanings: kept when it is still one of the department's statuses; otherwise (a built-in value
			// after the department moved to its own statuses, or an option since deleted) the status with the same meaning.
			var personnelSet = await GetActivePersonnelStateForDepartmentAsync(departmentId);
			var option = await FindDepartmentOptionAsync(departmentId, CustomStateTypes.Personnel, raw);

			if (personnelSet == null && option == null)
				return Enum.IsDefined(typeof(ActionTypes), raw) ? raw : (int?)null;

			if (personnelSet != null && option != null && !option.IsDeleted && option.CustomStateId == personnelSet.CustomStateId)
				return raw;

			var rawMeaning = option != null ? option.BaseType : (int)BuiltInStatusBaseTypes.ForPersonnelStatus(raw);
			var byMeaning = rawMeaning != (int)ActionBaseTypes.None && Enum.IsDefined(typeof(ActionBaseTypes), rawMeaning)
				? await ResolvePersonnelStatusAsync(departmentId, (ActionBaseTypes)rawMeaning)
				: null;
			if (byMeaning.HasValue)
				return byMeaning;

			// No status has that meaning (options created before meanings all read None): one of those named like the saved
			// status ("Standing By", the deleted option's text), or whose name suggests its meaning, stands in for it.
			var byName = personnelSet != null
				? StatusNameMatch.Find(personnelSet.GetActiveDetails(), CustomStateTypes.Personnel, option?.ButtonText ?? BuiltInName(GetDefaultPersonStatuses(), raw), rawMeaning)
				: null;
			if (byName != null)
				return byName.CustomStateDetailId;

			// Nothing does: keep resetting to the value the reset wrote before meanings rather than skipping everyone. Only a
			// value that is neither built-in nor one of the department's own options is dropped.
			return option != null || Enum.IsDefined(typeof(ActionTypes), raw) ? raw : (int?)null;
		}

		private static string BuiltInName(List<CustomStateDetail> builtIns, int raw) =>
			builtIns.FirstOrDefault(x => x.CustomStateDetailId == raw)?.ButtonText;

		public async Task<int?> ResolveStaffingResetAsync(int departmentId, string target)
		{
			var meaning = StatusMatch.BaseTypeOf(target);
			if (meaning.HasValue)
				return Enum.IsDefined(typeof(StaffingBaseTypes), meaning.Value) ? await ResolvePersonnelStaffingAsync(departmentId, (StaffingBaseTypes)meaning.Value) : null;

			if (!int.TryParse(target?.Trim(), out var raw))
				return null;

			// Same rule as statuses: a value that is still one of the department's levels is kept, anything else follows its
			// meaning, then a level without a meaning matched by name, and with none of those the value is written as it was
			// before meanings.
			var staffingSet = await GetActiveStaffingLevelsForDepartmentAsync(departmentId);
			var option = await FindDepartmentOptionAsync(departmentId, CustomStateTypes.Staffing, raw);

			if (staffingSet == null && option == null)
				return Enum.IsDefined(typeof(UserStateTypes), raw) ? raw : (int?)null;

			if (staffingSet != null && option != null && !option.IsDeleted && option.CustomStateId == staffingSet.CustomStateId)
				return raw;

			var rawMeaning = option != null ? option.BaseType : (int)BuiltInStatusBaseTypes.ForStaffing(raw);
			var byMeaning = Enum.IsDefined(typeof(StaffingBaseTypes), rawMeaning) ? await ResolvePersonnelStaffingAsync(departmentId, (StaffingBaseTypes)rawMeaning) : null;
			if (byMeaning.HasValue)
				return byMeaning;

			var byName = staffingSet != null
				? StatusNameMatch.Find(staffingSet.GetActiveDetails(), CustomStateTypes.Staffing, option?.ButtonText ?? BuiltInName(GetDefaultPersonStaffings(), raw), rawMeaning)
				: null;
			if (byName != null)
				return byName.CustomStateDetailId;

			return option != null || Enum.IsDefined(typeof(UserStateTypes), raw) ? raw : (int?)null;
		}

		public async Task<CustomStateDetail> GetDefaultPersonnelStatusAsync(int departmentId)
		{
			// A person with no status, or none inside the auto-available hour, reads as available. For a department on its
			// own staff statuses that is the set's first Available option; the built-in Standing By is not one of its options.
			var available = await ResolvePersonnelStatusAsync(departmentId, ActionBaseTypes.Available);

			CustomStateDetail detail = null;
			if (available.HasValue && available.Value != (int)ActionTypes.StandingBy)
				detail = await FindDepartmentOptionAsync(departmentId, CustomStateTypes.Personnel, available.Value);

			return detail ?? await GetCustomPersonnelStatusAsync(departmentId, new ActionLog { DepartmentId = departmentId, ActionTypeId = (int)ActionTypes.StandingBy });
		}

		public async Task<CustomStateDetail> GetCustomUnitStateAsync(UnitState state)
		{
			if (state == null)
				return null;

			// Callers load the unit with the state; without it only a built-in value can be resolved.
			if (state.Unit != null)
			{
				var custom = await FindDepartmentOptionAsync(state.Unit.DepartmentId, CustomStateTypes.Unit, state.State);
				if (custom != null)
					return custom;
			}

			if (state.State > CallStatusLinkage.MaxBuiltInStatusId)
				return null;

			var detail = new CustomStateDetail();

			detail.ButtonText = state.GetStatusText();
			detail.ButtonColor = state.GetStatusCss();
			detail.BaseType = (int)BuiltInStatusBaseTypes.ForUnitState(state.State);

			if (string.IsNullOrWhiteSpace(detail.ButtonColor))
				detail.ButtonColor = "label-default";

			return detail;
		}

		public List<CustomStateDetail> GetDefaultUnitStatuses()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Responding, ButtonText = "Responding", ButtonColor = "#32db64", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Available, ButtonText = "Available", ButtonColor = "#d1dade", TextColor = "#5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Unavailable, ButtonText = "Unavailable", ButtonColor = "" });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Committed, ButtonText = "Committed", ButtonColor = "#50b8de", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.CallsAndStations });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Delayed, ButtonText = "Delayed", ButtonColor = "" });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.OnScene, ButtonText = "On Scene", ButtonColor = "#69BB7B", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Staging, ButtonText = "Staging", ButtonColor = "#ffc900", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Returning, ButtonText = "Returning", ButtonColor = "#387ef5", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.OutOfService, ButtonText = "Out of Service", ButtonColor = "#ff6b69", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Cancelled, ButtonText = "Cancelled", ButtonColor = "#ff6b69" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Released, ButtonText = "Released", ButtonColor = "" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Manual, ButtonText = "Manual", ButtonColor = "" });
			//details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UnitStateTypes.Enroute, ButtonText = "Enroute", ButtonColor = "" });

			foreach (var detail in details)
				detail.BaseType = (int)BuiltInStatusBaseTypes.ForUnitState(detail.CustomStateDetailId);

			return details;

		}

		public List<CustomStateDetail> GetDefaultPersonStatuses()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.Responding, ButtonText = "Responding", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.CallsAndStations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.NotResponding, ButtonText = "Not Responding", ButtonColor = "#ed5565", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.OnScene, ButtonText = "On Scene", ButtonColor = "#262626", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.StandingBy, ButtonText = "Standing By", ButtonColor = "#d1dade", TextColor = "#5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.AvailableStation, ButtonText = "Available Station", ButtonColor = "#d1dade", TextColor = "#5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToStation, ButtonText = "Responding to Station", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Stations });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.RespondingToScene, ButtonText = "Responding to Scene", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.Calls });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)ActionTypes.OnUnit, ButtonText = "On Unit", ButtonColor = "#449d44", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional, DetailType = (int)CustomStateDetailTypes.CallsAndStations });

			foreach (var detail in details)
				detail.BaseType = (int)BuiltInStatusBaseTypes.ForPersonnelStatus(detail.CustomStateDetailId);

			return details;
		}

		public List<CustomStateDetail> GetDefaultPersonStaffings()
		{
			List<CustomStateDetail> details = new List<CustomStateDetail>();
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Available, ButtonText = "Available", ButtonColor = "#d1dade", TextColor = "#5E5E5E", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Delayed, ButtonText = "Delayed", ButtonColor = "#f8ac59", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Unavailable, ButtonText = "Unavailable", ButtonColor = "#ed5565", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.Committed, ButtonText = "Committed", ButtonColor = "#23c6c8", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });
			details.Add(new CustomStateDetail() { CustomStateDetailId = (int)UserStateTypes.OnShift, ButtonText = "On Shift", ButtonColor = "#228bcb", TextColor = "#ffffff", NoteType = (int)CustomStateNoteTypes.Optional });

			foreach (var detail in details)
				detail.BaseType = (int)BuiltInStatusBaseTypes.ForStaffing(detail.CustomStateDetailId);

			return details;
		}
	}
}
