using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Identity;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;

namespace Resgrid.Services
{
	public class CallsService : ICallsService
	{
		private static string CacheKey = "Call_{0}";
		private static string CallPrioritiesCacheKey = "CallPriosForDep_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(3);
		private static TimeSpan Day30CacheLength = TimeSpan.FromDays(30);

		private static List<DepartmentCallPriority> _callPriorities;

		private readonly ICallsRepository _callsRepository;
		private readonly ICallDispatchesRepository _callDispatchesRepository;
		private readonly ICallDispatchGroupRepository _callDispatchGroupRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly ICallDispatchRoleRepository _callDispatchRoleRepository;
		private readonly ICommunicationService _communicationService;
		private readonly ICallTypesRepository _callTypesRepository;
		private readonly ICallEmailFactory _callEmailFactory;
		private readonly ICacheProvider _cacheProvider;
		private readonly ICallNotesRepository _callNotesRepository;
		private readonly ICallAttachmentRepository _callAttachmentRepository;
		private readonly IDepartmentCallPriorityRepository _departmentCallPriorityRepository;
		private readonly IShortenUrlProvider _shortenUrlProvider;
		private readonly ICallProtocolsRepository _callProtocolsRepository;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallReferencesRepository _callReferencesRepository;
		private readonly ICallContactsRepository _callContactsRepository;

		public CallsService(ICallsRepository callsRepository, ICommunicationService communicationService,
			ICallDispatchesRepository callDispatchesRepository, ICallTypesRepository callTypesRepository, ICallEmailFactory callEmailFactory,
			ICacheProvider cacheProvider, ICallNotesRepository callNotesRepository,
			ICallAttachmentRepository callAttachmentRepository, ICallDispatchGroupRepository callDispatchGroupRepository,
			ICallDispatchUnitRepository callDispatchUnitRepository, ICallDispatchRoleRepository callDispatchRoleRepository,
			IDepartmentCallPriorityRepository departmentCallPriorityRepository, IShortenUrlProvider shortenUrlProvider,
			ICallProtocolsRepository callProtocolsRepository, IGeoLocationProvider geoLocationProvider, IDepartmentsService departmentsService,
			ICallReferencesRepository callReferencesRepository, ICallContactsRepository callContactsRepository)
		{
			_callsRepository = callsRepository;
			_communicationService = communicationService;
			_callDispatchesRepository = callDispatchesRepository;
			_callTypesRepository = callTypesRepository;
			_callEmailFactory = callEmailFactory;
			_cacheProvider = cacheProvider;
			_callNotesRepository = callNotesRepository;
			_callAttachmentRepository = callAttachmentRepository;
			_callDispatchGroupRepository = callDispatchGroupRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_callDispatchRoleRepository = callDispatchRoleRepository;
			_departmentCallPriorityRepository = departmentCallPriorityRepository;
			_shortenUrlProvider = shortenUrlProvider;
			_callProtocolsRepository = callProtocolsRepository;
			_geoLocationProvider = geoLocationProvider;
			_departmentsService = departmentsService;
			_callReferencesRepository = callReferencesRepository;
			_callContactsRepository = callContactsRepository;
		}

		public async Task<Call> SaveCallAsync(Call call, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (String.IsNullOrWhiteSpace(call.Number))
				call.Number = await GetCurrentCallNumberAsync(call.LoggedOn, call.DepartmentId);

			if (String.IsNullOrWhiteSpace(call.Name))
				call.Name = "New Call " + DateTime.UtcNow.ToShortDateString();

			// Got some bad data where geolocation is "," which passes some checks.
			if (!String.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Length == 1)
				call.GeoLocationData = "";

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				foreach (var dispatch in call.Dispatches)
				{
					if (dispatch.CallDispatchId == 0)
						dispatch.DispatchedOn = DateTime.UtcNow;
				}
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var dispatch in call.GroupDispatches)
				{
					if (dispatch.CallDispatchGroupId == 0)
						dispatch.DispatchedOn = DateTime.UtcNow;
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var dispatch in call.RoleDispatches)
				{
					if (dispatch.CallDispatchRoleId == 0)
						dispatch.DispatchedOn = DateTime.UtcNow;
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var dispatch in call.UnitDispatches)
				{
					if (dispatch.CallDispatchUnitId == 0)
						dispatch.DispatchedOn = DateTime.UtcNow;
				}
			}

			if (call.References != null && call.References.Any())
			{
				foreach (var reference in call.References)
				{
					if (String.IsNullOrWhiteSpace(reference.CallReferenceId))
						reference.AddedOn = DateTime.UtcNow;
				}
			}

			var savedCall = await _callsRepository.SaveOrUpdateAsync(call, cancellationToken);

			if (call.References != null && call.References.Any())
			{
				foreach (var reference in call.References)
				{
					reference.SourceCallId = savedCall.CallId;

					await _callReferencesRepository.SaveOrUpdateAsync(reference, cancellationToken);
				}
			}

			return savedCall;
		}

		public async Task<bool> RegenerateCallNumbersAsync(int departmentId, int year, CancellationToken cancellationToken = default(CancellationToken))
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var start = (new DateTime(year, 1, 1, 1, 1, 1, DateTimeKind.Local)).SetToMidnight();
			var end = (new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Local)).SetToEndOfDay();

			//var calls = (await _callsRepository.GetAllByDepartmentIdAsync(departmentId)).OrderBy(x => x.LoggedOn);
			var calls = await _callsRepository.GetAllCallsByDepartmentDateRangeAsync(departmentId, DateTimeHelpers.ConvertToUtc(start, department.TimeZone), DateTimeHelpers.ConvertToUtc(end, department.TimeZone));
			calls = calls.OrderBy(x => x.LoggedOn);
			int count = 1;

			foreach (var call in calls)
			{
				call.Number = string.Format("{0}-{1}", year % 100, count);
				await _callsRepository.SaveOrUpdateAsync(call, cancellationToken);
				count++;
			}

			return true;
		}

		public async Task<string> GetCurrentCallNumberAsync(DateTime utcDate, int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			DateTime localTime = DateTimeHelpers.GetLocalDateTime(utcDate, department.TimeZone);
			int year = localTime.Year;

			var localYearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
			var localYearEnd = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Unspecified);

			var utcYearStart = DateTimeHelpers.ConvertToUtc(localYearStart, department.TimeZone);
			var utcYearEnd = DateTimeHelpers.ConvertToUtc(localYearEnd, department.TimeZone);

			var callCount = await _callsRepository.GetCallsCountByDepartmentDateRangeAsync(departmentId, utcYearStart, utcYearEnd);

			return string.Format("{0}-{1}", year % 100, callCount + 1);
		}

		public async Task<List<Call>> GetAllCallsByDepartmentAsync(int departmentId)
		{
			var calls = from c in (await _callsRepository.GetAllByDepartmentIdAsync(departmentId))
						where c.IsDeleted == false
						orderby c.State
						select c;

			return calls.ToList();
		}

		public async Task<List<Call>> GetAllCallsByDepartmentDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			var calls = (from c in (await _callsRepository.GetAllCallsByDepartmentDateRangeAsync(departmentId, startDate, endDate))
						 orderby c.State
						 select c).ToList();

			foreach (var call in calls)
			{
				call.Dispatches = (await _callDispatchesRepository.GetCallDispatchesByCallIdAsync(call.CallId)).ToList();
				call.GroupDispatches = (await _callDispatchGroupRepository.GetAllCallDispatchGroupByCallIdAsync(call.CallId)).ToList();
				call.UnitDispatches = (await _callDispatchUnitRepository.GetCallUnitDispatchesByCallIdAsync(call.CallId)).ToList();
				call.RoleDispatches = (await _callDispatchRoleRepository.GetCallRoleDispatchesByCallIdAsync(call.CallId)).ToList();
			}

			return calls;
		}

		public async Task<List<Call>> GetActiveCallsByDepartmentAsync(int departmentId)
		{
			var items = await _callsRepository.GetAllOpenCallsByDepartmentAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<Call>();
		}

		public async Task<List<Call>> GetLatest10ActiveCallsByDepartmentAsync(int departmentId)
		{
			var calls = (from c in await _callsRepository.GetAllOpenCallsByDepartmentAsync(departmentId)
						 orderby c.LoggedOn descending
						 select c).Take(10);

			return calls.ToList();
		}

		public async Task<List<Call>> GetClosedCallsByDepartmentAsync(int departmentId)
		{
			var calls = await _callsRepository.GetAllClosedCallsByDepartmentAsync(departmentId);

			return calls.ToList();
		}

		public async Task<List<Call>> GetClosedCallsByDepartmentYearAsync(int departmentId, string year)
		{
			var calls = await _callsRepository.GetAllClosedCallsByDepartmentYearAsync(departmentId, year);

			return calls.ToList();
		}

		public async Task<bool> DeleteCallByIdAsync(int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var call = await GetCallByIdAsync(callId);
			return await _callsRepository.DeleteAsync(call, cancellationToken);
		}

		public async Task<Call> ReOpenCallByIdAsync(int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var call = await GetCallByIdAsync(callId);
			call.State = (int)CallStates.Active;
			call.ClosedByUser = null;
			call.ClosedByUserId = null;
			call.ClosedOn = null;
			call.CompletedNotes = null;

			return await _callsRepository.SaveOrUpdateAsync(call, cancellationToken);
		}

		public async Task<Call> GetCallByIdAsync(int callId, bool bypassCache = true)
		{
			async Task<Call> getCall()
			{
				return await _callsRepository.GetByIdAsync(callId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(CacheKey, callId), getCall, CacheLength);
			}

			return await getCall();
		}

		public async Task<int> GetTodayCallsCountAsync(int departmentId)
		{
			var date = DateTime.UtcNow.Date;
			return (await _callsRepository.GetAllCallsByDepartmentIdLoggedOnAsync(departmentId, date)).Count();
		}

		public async Task<int> GetActiveCallsForDepartmentAsync(int departmentId)
		{
			return (await GetActiveCallsByDepartmentAsync(departmentId)).Count();
		}

		public async Task<bool> DeleteDispatchesAsync(List<CallDispatch> dispatches, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var callDispatch in dispatches)
			{
				await _callDispatchesRepository.DeleteAsync(callDispatch, cancellationToken);
			}

			return true;
		}

		public async Task<bool> DeleteGroupDispatchesAsync(List<CallDispatchGroup> dispatches, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var callDispatch in dispatches)
			{
				await _callDispatchGroupRepository.DeleteAsync(callDispatch, cancellationToken);
			}

			return true;
		}

		public async Task<bool> DeleteRoleDispatchesAsync(List<CallDispatchRole> dispatches, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var callDispatch in dispatches)
			{
				await _callDispatchRoleRepository.DeleteAsync(callDispatch, cancellationToken);
			}

			return true;
		}

		public async Task<bool> DeleteUnitDispatchesAsync(List<CallDispatchUnit> dispatches, CancellationToken cancellationToken = default(CancellationToken))
		{
			foreach (var callDispatch in dispatches)
			{
				await _callDispatchUnitRepository.DeleteAsync(callDispatch, cancellationToken);
			}

			return true;
		}

		public async Task<List<Call>> GetLast2MonthCallsByDepartmentAsync(int departmentId)
		{
			var date = DateTime.Now.AddMonths(-2).ToUniversalTime();
			var calls = await _callsRepository.GetAllCallsByDepartmentIdLoggedOnAsync(departmentId, date);

			return calls.ToList();
		}

		public async Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId)
		{
			var callTypes = await _callTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			return callTypes.ToList();
		}

		public async Task<bool> DeleteCallTypeAsync(int callTypeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var callType = await _callTypesRepository.GetByIdAsync(callTypeId);

			return await _callTypesRepository.DeleteAsync(callType, cancellationToken);
		}

		public async Task<CallType> GetCallTypeByIdAsync(int callTypeId)
		{
			return await _callTypesRepository.GetByIdAsync(callTypeId);
		}

		public async Task<CallType> SaveCallTypeAsync(CallType callType, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _callTypesRepository.SaveOrUpdateAsync(callType, cancellationToken);
		}

		public async Task<Call> GenerateCallFromEmail(int type, CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes)
		{
			return await _callEmailFactory.GenerateCallFromEmailText((CallEmailTypes)type, email, managingUser, users, department, activeCalls, units, priority, activePriorities, callTypes, _geoLocationProvider);
		}

		public async Task<CallNote> SaveCallNoteAsync(CallNote note, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _callNotesRepository.SaveOrUpdateAsync(note, cancellationToken);
		}

		public async Task<CallAttachment> GetCallAttachmentAsync(int callAttachmentId)
		{
			var attachment = await _callAttachmentRepository.GetByIdAsync(callAttachmentId);

			if (attachment != null && attachment.Call == null)
				attachment.Call = await GetCallByIdAsync(attachment.CallId);

			return attachment;
		}

		public async Task<CallAttachment> SaveCallAttachmentAsync(CallAttachment attachment, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _callAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken);
		}

		public async Task<bool> MarkCallDispatchesAsSentAsync(int callId, List<Guid> usersToMark)
		{
			return await _callDispatchesRepository.MarkCallDispatchesAsSentByCallIdUsersAsync(callId, usersToMark);
		}

		public async Task<List<DepartmentCallPriority>> GetAllCallPrioritiesAsync()
		{
			var items = await _departmentCallPriorityRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<DepartmentCallPriority>();
		}

		public async Task<DepartmentCallPriority> GetCallPrioritiesByIdAsync(int departmentId, int priorityId, bool bypassCache = false)
		{
			if (priorityId > 3)
				return (await GetCallPrioritiesForDepartmentAsync(departmentId, bypassCache)).FirstOrDefault(x => x.DepartmentCallPriorityId == priorityId);
			else
				return GetDefaultCallPriorities().FirstOrDefault(x => x.DepartmentCallPriorityId == priorityId);
		}

		public async Task<List<DepartmentCallPriority>> GetCallPrioritiesForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<DepartmentCallPriority>> getCallPriorities()
			{
				var items = await _departmentCallPriorityRepository.GetAllByDepartmentIdAsync(departmentId);

				if (items != null && items.Any())
					return items.ToList();

				return new List<DepartmentCallPriority>();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var departmentPriorities = await _cacheProvider.RetrieveAsync(string.Format(CallPrioritiesCacheKey, departmentId), getCallPriorities, Day30CacheLength);

				if (departmentPriorities == null || !departmentPriorities.Any())
					return GetDefaultCallPriorities();
			}

			var departmentPriorities2 = await getCallPriorities();
			if (departmentPriorities2 == null || !departmentPriorities2.Any())
				return GetDefaultCallPriorities();

			return departmentPriorities2;
		}

		public async Task<List<DepartmentCallPriority>> GetActiveCallPrioritiesForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			var priorities = await GetCallPrioritiesForDepartmentAsync(departmentId, bypassCache);

			if (priorities == null || !priorities.Any())
				return GetDefaultCallPriorities();

			var activePriorities = priorities.Where(x => x.IsDeleted == false).ToList();

			if (activePriorities == null || !activePriorities.Any())
				return GetDefaultCallPriorities();

			return activePriorities;
		}

		public async Task<Call> PopulateCallData(Call call, bool getDispatches, bool getAttachments, bool getNotes, bool getGroupDispatches, bool getUnitDispatches, bool getRoleDispatches, bool getProtocols, bool getReferences, bool getContacts)
		{
			if (call == null)
				return null;

			if (getDispatches && call.Dispatches == null)
			{
				var items = await _callDispatchesRepository.GetCallDispatchesByCallIdAsync(call.CallId);

				if (items != null)
					call.Dispatches = items.ToList();
				else
					call.Dispatches = new List<CallDispatch>();
			}

			if (getAttachments && call.Attachments == null)
			{
				var items = await _callAttachmentRepository.GetCallDispatchesByCallIdAsync(call.CallId);

				if (items != null)
					call.Attachments = items.ToList();
				else
					call.Attachments = new List<CallAttachment>();
			}
			if (getNotes && call.CallNotes == null)
			{
				var items = await _callNotesRepository.GetCallNotesByCallIdAsync(call.CallId);

				if (items != null)
					call.CallNotes = items.ToList();
				else
					call.CallNotes = new List<CallNote>();
			}
			if (getGroupDispatches && call.GroupDispatches == null)
			{
				var items = await _callDispatchGroupRepository.GetAllCallDispatchGroupByCallIdAsync(call.CallId);

				if (items != null)
					call.GroupDispatches = items.ToList();
				else
					call.GroupDispatches = new List<CallDispatchGroup>();
			}
			if (getUnitDispatches && call.UnitDispatches == null)
			{
				var items = await _callDispatchUnitRepository.GetCallUnitDispatchesByCallIdAsync(call.CallId);

				if (items != null)
					call.UnitDispatches = items.ToList();
				else
					call.UnitDispatches = new List<CallDispatchUnit>();
			}
			if (getRoleDispatches && call.RoleDispatches == null)
			{
				var items = await _callDispatchRoleRepository.GetCallRoleDispatchesByCallIdAsync(call.CallId);

				if (items != null)
					call.RoleDispatches = items.ToList();
				else
					call.RoleDispatches = new List<CallDispatchRole>();
			}
			if (getProtocols && (call.Protocols == null || !call.Protocols.Any()))
			{
				var items = await _callProtocolsRepository.GetCallProtocolsByCallIdAsync(call.CallId);

				if (items != null)
					call.Protocols = items.ToList();
				else
					call.Protocols = new List<CallProtocol>();
			}
			if (getReferences && call.References == null)
			{
				var items = await _callReferencesRepository.GetCallReferencesBySourceCallIdAsync(call.CallId);

				if (items != null)
					call.References = items.ToList();
				else
					call.References = new List<CallReference>();
			}
			if (getContacts && call.Contacts == null)
			{
				var items = await _callContactsRepository.GetCallContactsByCallIdAsync(call.CallId);

				if (items != null)
					call.Contacts = items.ToList();
				else
					call.Contacts = new List<CallContact>();
			}

			return call;
		}

		public async Task<bool> DeleteCallContactsAsync(int callId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var callContacts = await _callContactsRepository.GetCallContactsByCallIdAsync(callId);

			if (callContacts != null || callContacts.Any())
			{
				foreach (var contact in callContacts)
				{
					await _callContactsRepository.DeleteAsync(contact, cancellationToken);
				}

				return true;
			}

			return false;
		}

		public List<DepartmentCallPriority> GetDefaultCallPriorities()
		{
			if (_callPriorities == null)
			{
				_callPriorities = new List<DepartmentCallPriority>();

				_callPriorities.Add(new DepartmentCallPriority()
				{
					DepartmentCallPriorityId = 0,
					DepartmentId = 0,
					Name = "Low",
					Color = "#028602",
					Sort = 0,
					IsDeleted = false,
					IsDefault = false,
					PushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Low_call.wav"),
					IOSPushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Low_call.caf"),
					ShortNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "New_Call.wav"),
					DispatchPersonnel = true,
					DispatchUnits = true,
					ForceNotifyAllPersonnel = false,
					IsSystemPriority = true
				});

				_callPriorities.Add(new DepartmentCallPriority()
				{
					DepartmentCallPriorityId = 1,
					DepartmentId = 0,
					Name = "Medium",
					Color = "#DBDB2E",
					Sort = 1,
					IsDeleted = false,
					IsDefault = false,
					PushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Medium_call.wav"),
					IOSPushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Medium_call.caf"),
					ShortNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "New_Call.wav"),
					DispatchPersonnel = true,
					DispatchUnits = true,
					ForceNotifyAllPersonnel = false,
					IsSystemPriority = true
				});

				_callPriorities.Add(new DepartmentCallPriority()
				{
					DepartmentCallPriorityId = 2,
					DepartmentId = 0,
					Name = "High",
					Color = "#F9A203",
					Sort = 1,
					IsDeleted = false,
					IsDefault = false,
					PushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "High_call.wav"),
					IOSPushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "High_call.caf"),
					ShortNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "New_Call.wav"),
					DispatchPersonnel = true,
					DispatchUnits = true,
					ForceNotifyAllPersonnel = false,
					IsSystemPriority = true
				});

				_callPriorities.Add(new DepartmentCallPriority()
				{
					DepartmentCallPriorityId = 3,
					DepartmentId = 0,
					Name = "Emergency",
					Color = "#FD0303",
					Sort = 1,
					IsDeleted = false,
					IsDefault = true,
					PushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Emergency_call.wav"),
					IOSPushNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "Emergency_call.caf"),
					ShortNotificationSound = FileHelper.ExtractResource(typeof(DepartmentCallPriority), "New_Call.wav"),
					DispatchPersonnel = true,
					DispatchUnits = true,
					ForceNotifyAllPersonnel = false,
					IsSystemPriority = true
				});
			}

			return _callPriorities;
		}

		public async Task<DepartmentCallPriority> SaveCallPriorityAsync(DepartmentCallPriority callPriority, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _departmentCallPriorityRepository.SaveOrUpdateAsync(callPriority, cancellationToken);
			InvalidateCallPrioritiesForDepartmentInCache(callPriority.DepartmentId);

			return saved;
		}

		public async Task<List<CallProtocol>> GetCallProtocolsByCallIdAsync(int callId)
		{
			var items = await _callProtocolsRepository.GetCallProtocolsByCallIdAsync(callId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<CallProtocol>();
		}

		public async Task<List<string>> GetCallYearsByDeptartmentAsync(int departmentId)
		{
			var items = await _callsRepository.SelectCallYearsByDeptAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<string>();
		}

		public void InvalidateCallPrioritiesForDepartmentInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CallPrioritiesCacheKey, departmentId));
		}

		public async Task<string> GetShortenedAudioUrlAsync(int callId, int callAttachmentId)
		{
			try
			{
				if (callAttachmentId > 0)
				{
					var encryptedQuery =
						Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt(callAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase)));
					string shortenedUrl =
						await _shortenUrlProvider.Shorten(
							$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/calls/getcallaudio?query={encryptedQuery}");

					if (String.IsNullOrWhiteSpace(shortenedUrl))
						return String.Empty;

					return shortenedUrl;
				}
				else
				{
					var attachment = await
						_callAttachmentRepository.GetCallAttachmentByCallIdAndTypeAsync(callId, CallAttachmentTypes.DispatchAudio);

					if (attachment == null)
						return String.Empty;

					var encryptedQuery =
						Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt(attachment.CallAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase)));
					string shortenedUrl =
						await _shortenUrlProvider.Shorten(
							$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/calls/getcallaudio?query={encryptedQuery}");

					if (String.IsNullOrWhiteSpace(shortenedUrl))
						return String.Empty;

					return shortenedUrl;
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return String.Empty;
			}
		}

		public async Task<string> GetShortenedCallLinkUrl(int callId, bool pdf = false, int? stationId = null)
		{
			try
			{
				string encryptedQuery = "";

				if (!stationId.HasValue && !pdf)
				{
					encryptedQuery = Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)));
				}
				else
				{
					string type = pdf ? "pdf" : "web";
					string station = stationId.HasValue ? stationId.Value.ToString() : "0";

					encryptedQuery = Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt($"{callId.ToString()}|${type}|${station}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)));
				}


				string shortenedUrl =
					await _shortenUrlProvider.Shorten(
						$"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/CallExportEx?query={encryptedQuery}");

				if (String.IsNullOrWhiteSpace(shortenedUrl))
					return String.Empty;

				return shortenedUrl;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return String.Empty;
			}
		}

		public async Task<string> GetShortenedCallPdfUrl(int callId, bool pdf = false, int? stationId = null)
		{
			try
			{
				string shortenedUrl =
					await _shortenUrlProvider.Shorten(GetCallPdfUrl(callId, pdf, stationId));

				if (String.IsNullOrWhiteSpace(shortenedUrl))
					return String.Empty;

				return shortenedUrl;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return String.Empty;
			}
		}

		public string GetCallPdfUrl(int callId, bool pdf = false, int? stationId = null)
		{
			try
			{
				string encryptedQuery = "";

				if (!stationId.HasValue && !pdf)
				{
					encryptedQuery = Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)));
				}
				else
				{
					string type = pdf ? "pdf" : "web";
					string station = stationId.HasValue ? stationId.Value.ToString() : "0";

					encryptedQuery = Convert.ToBase64String(Encoding.UTF8.GetBytes(SymmetricEncryption.Encrypt($"{callId.ToString()}|{type}|{station}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)));
				}


				return $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/CallExportPdf?query={encryptedQuery}";


			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return String.Empty;
			}
		}

		public async Task<bool> ClearGroupForDispatchesAsync(int departmentGroupId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var groupDispatches = await _callDispatchGroupRepository.GetAllCallDispatchGroupByGroupIdAsync(departmentGroupId);

			foreach (var groupDispatch in groupDispatches)
			{
				await _callDispatchGroupRepository.DeleteAsync(groupDispatch, cancellationToken);
			}

			return true;
		}

		public async Task<List<Call>> GetAllNonDispatchedScheduledCallsWithinDateRange(DateTime startDate, DateTime endDate)
		{
			var calls = await _callsRepository.GetAllNonDispatchedScheduledCallsWithinDateRange(startDate, endDate);

			if (calls != null && calls.Any())
				return calls.ToList();

			return new List<Call>();
		}

		public async Task<List<Call>> GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(int departmentId)
		{
			var calls = await _callsRepository.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(departmentId);

			if (calls != null && calls.Any())
				return calls.ToList();

			return new List<Call>();
		}

		public async Task<List<CallReference>> GetChildCallsForCallAsync(int callId)
		{
			var calls = await _callReferencesRepository.GetCallReferencesByTargetCallIdAsync(callId);

			if (calls != null && calls.Any())
				return calls.ToList();

			return new List<CallReference>();
		}

		public async Task<bool> DeleteCallReferenceAsync(CallReference callReference, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _callReferencesRepository.DeleteAsync(callReference, cancellationToken);
		}

		public string CallStateToString(CallStates state)
		{
			switch (state)
			{
				case CallStates.Active:
					return "Active";
				case CallStates.Closed:
					return "Closed";
				case CallStates.Cancelled:
					return "Cancelled";
				case CallStates.Unfounded:
					return "Unfounded";
				case CallStates.Founded:
					return "Founded";
				case CallStates.Minor:
					return "Minor";
				default:
					return "Unknown";
			}
		}

		public string CallStateToColor(CallStates state)
		{
			switch (state)
			{
				case CallStates.Active:
					return "#008000";
				case CallStates.Closed:
					return "#808080";
				case CallStates.Cancelled:
					return "#000000";
				case CallStates.Unfounded:
					return "#000000";
				default:
					return "#000000";
			}
		}

		public async Task<string> CallPriorityToStringAsync(int priority, int departmentId)
		{
			switch (priority)
			{
				case (int)CallPriority.Low:
					return "Low";
				case (int)CallPriority.Medium:
					return "Medium";
				case (int)CallPriority.High:
					return "High";
				case (int)CallPriority.Emergency:
					return "Emergency";
				default:
					var priorities = await GetCallPrioritiesForDepartmentAsync(departmentId);

					if (priorities != null && priorities.Any(x => x.DepartmentCallPriorityId == priority))
						return priorities.First(x => x.DepartmentCallPriorityId == priority).Name;
					else
						return "Low";
			}
		}

		public async Task<string> CallPriorityToColorAsync(int priority, int departmentId)
		{
			switch (priority)
			{
				case (int)CallPriority.Low:
					return "#008000";
				case (int)CallPriority.Medium:
					return "#DBDB2E";
				case (int)CallPriority.High:
					return "#FFA500";
				case (int)CallPriority.Emergency:
					return "#FF0000";
				default:
					var priorities = await GetCallPrioritiesForDepartmentAsync(departmentId);

					if (priorities != null && priorities.Any(x => x.DepartmentCallPriorityId == priority))
						return priorities.First(x => x.DepartmentCallPriorityId == priority).Color;
					else
						return "#008000";
			}
		}

		public async Task<List<Call>> GetCallsByContactIdAsync(string contactId, int departmentId)
		{
			var calls = await _callsRepository.GetAllCallsByContactIdAsync(contactId, departmentId);

			if (calls != null && calls.Any())
				return calls.ToList();

			return new List<Call>();
		}
	}
}
