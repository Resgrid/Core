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
		private readonly IGenericDataRepository<CallDispatch> _callDispatchsRepository;
		private readonly IGenericDataRepository<CallDispatchGroup> _callDispatchGroupRepository;
		private readonly IGenericDataRepository<CallDispatchUnit> _callDispatchUnitRepository;
		private readonly IGenericDataRepository<CallDispatchRole> _callDispatchRoleRepository;
		private readonly ICommunicationService _communicationService;
		private readonly ICallTypesRepository _callTypesRepository;
		private readonly ICallEmailFactory _callEmailFactory;
		private readonly ICacheProvider _cacheProvider;
		private readonly IGenericDataRepository<CallNote> _callNotesRepository;
		private readonly IGenericDataRepository<CallAttachment> _callAttachmentRepository;
		private readonly IGenericDataRepository<DepartmentCallPriority> _departmentCallPriorityRepository;
		private readonly IShortenUrlProvider _shortenUrlProvider;

		public CallsService(ICallsRepository callsRepository, ICommunicationService communicationService,
			IGenericDataRepository<CallDispatch> callDispatchsRepository, ICallTypesRepository callTypesRepository, ICallEmailFactory callEmailFactory,
			ICacheProvider cacheProvider, IGenericDataRepository<CallNote> callNotesRepository,
			IGenericDataRepository<CallAttachment> callAttachmentRepository, IGenericDataRepository<CallDispatchGroup> callDispatchGroupRepository,
			IGenericDataRepository<CallDispatchUnit> callDispatchUnitRepository, IGenericDataRepository<CallDispatchRole> callDispatchRoleRepository,
			IGenericDataRepository<DepartmentCallPriority> departmentCallPriorityRepository, IShortenUrlProvider shortenUrlProvider)
		{
			_callsRepository = callsRepository;
			_communicationService = communicationService;
			_callDispatchsRepository = callDispatchsRepository;
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
		}

		public Call SaveCall(Call call)
		{
			if (String.IsNullOrWhiteSpace(call.Number))
				call.Number = GetCurrentCallNumber(call.DepartmentId);

			if (String.IsNullOrWhiteSpace(call.Name))
				call.Name = "New Call " + DateTime.UtcNow.ToShortDateString();

			_callsRepository.SaveOrUpdate(call);

			//return GetCallById(call.CallId);
			return call;
		}

		public void RegenerateCallNumbers(int departmentId)
		{
			var calls = _callsRepository.GetAll().Where(x => x.DepartmentId == departmentId).OrderBy(x => x.LoggedOn);
			int year = DateTime.UtcNow.Year;
			int count = 1;

			foreach (var call in calls)
			{
				call.Number = string.Format("{0}-{1}", year % 100, count);
			}

			_callsRepository.SaveOrUpdateAll(calls);
		}

		public string GetCurrentCallNumber(int departmentId)
		{
			int year = DateTime.UtcNow.Year;

			var count = _callsRepository.GetAll().Count(x => x.DepartmentId == departmentId && x.LoggedOn.Year == year) + 1;
			//var count = (from call in _callsRepository.GetAll()
			//where call.DepartmentId == departmentId &&
			//			call.LoggedOn.Year == year
			//select call).Count() + 1;

			return string.Format("{0}-{1}", year % 100, count);
		}

		public List<Call> GetAllCallsByDepartment(int departmentId)
		{
			var calls = from c in _callsRepository.GetAll()
						where c.DepartmentId == departmentId && c.IsDeleted == false
						orderby c.State
						select c;

			return calls.ToList();
		}

		public List<Call> GetAllCallsByDepartmentDateRange(int departmentId, DateTime startDate, DateTime endDate)
		{
			var calls = from c in _callsRepository.GetAll()
						where c.DepartmentId == departmentId && c.IsDeleted == false && c.LoggedOn >= startDate && c.LoggedOn <= endDate
						orderby c.State
						select c;

			return calls.ToList();
		}

		public List<Call> GetActiveCallsByDepartment(int departmentId)
		{
			return _callsRepository.GetActiveCallsByDepartment(departmentId);
		}

		public List<Call> GetActiveCallsByDepartmentForUpdate(int departmentId)
		{
			return _callsRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.State == 0 && x.IsDeleted == false).ToList();
		}

		public List<Call> GetLatest10ActiveCallsByDepartment(int departmentId)
		{
			var calls = (from c in _callsRepository.GetAll()
						 where c.State == 0 && c.DepartmentId == departmentId && c.IsDeleted == false
						 orderby c.LoggedOn descending
						 select c).Take(10);

			return calls.ToList();
		}

		public List<Call> GetClosedCallsByDepartment(int departmentId)
		{
			var calls = from c in _callsRepository.GetAll()
						where c.State > 0 && c.DepartmentId == departmentId && c.IsDeleted == false
						select c;

			return calls.ToList();
		}

		public void DeleteCallById(int callId)
		{
			var call = GetCallById(callId);
			_callsRepository.DeleteOnSubmit(call);
		}

		public void ReOpenCallById(int callId)
		{
			var call = GetCallById(callId);
			call.State = (int)CallStates.Active;
			call.ClosedByUser = null;
			call.ClosedByUserId = null;
			call.ClosedOn = null;
			call.CompletedNotes = null;

			_callsRepository.SaveOrUpdate(call);
		}

		public Call GetCallById(int callId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<Call> getCall = delegate ()
				{
					return _callsRepository.GetAll().FirstOrDefault(x => x.CallId == callId);
				};

				return _cacheProvider.Retrieve(string.Format(CacheKey, callId), getCall, CacheLength);
			}

			return _callsRepository.GetAll().FirstOrDefault(x => x.CallId == callId);
		}

		public int GetTodayCallsCount(int departmentId)
		{
			var date = DateTime.UtcNow.Date;
			return _callsRepository.GetAll().Count(x => x.LoggedOn >= date && x.DepartmentId == departmentId);
		}

		public int GetActiveCallsForDepartment(int departmentId)
		{
			return _callsRepository.GetAll().Count(x => x.State == 0 && x.IsDeleted == false && x.DepartmentId == departmentId);
		}

		public void DeleteDispatchesForUserAndRemapCalls(string remapToUserId, string userIdToDelete)
		{
			var dispatches = (from d in _callDispatchsRepository.GetAll()
							  where d.UserId == userIdToDelete
							  select d).ToList();

			_callDispatchsRepository.DeleteAll(dispatches);

			var reportingCalls = (from c in _callsRepository.GetAll()
								  where c.ReportingUserId == userIdToDelete
								  select c).ToList();

			foreach (var c in reportingCalls)
			{
				c.ReportingUserId = remapToUserId;
				_callsRepository.SaveOrUpdate(c);
			}

			var closingCalls = (from c in _callsRepository.GetAll()
								where c.ClosedByUserId == userIdToDelete
								select c).ToList();

			foreach (var c in closingCalls)
			{
				c.ClosedByUserId = remapToUserId;
				_callsRepository.SaveOrUpdate(c);
			}
		}

		public void DeleteDispatches(List<CallDispatch> dispatches)
		{
			_callDispatchsRepository.DeleteAll(dispatches);
		}

		public void DeleteGroupDispatches(List<CallDispatchGroup> dispatches)
		{
			_callDispatchGroupRepository.DeleteAll(dispatches);
		}

		public void DeleteRoleDispatches(List<CallDispatchRole> dispatches)
		{
			_callDispatchRoleRepository.DeleteAll(dispatches);
		}

		public void DeleteUnitDispatches(List<CallDispatchUnit> dispatches)
		{
			_callDispatchUnitRepository.DeleteAll(dispatches);
		}

		public List<Call> GetLast2MonthCallsByDepartment(int departmentId)
		{
			var date = DateTime.Now.AddMonths(-2).ToUniversalTime();

			var calls = from c in _callsRepository.GetAll()
						where c.State == 0 && c.DepartmentId == departmentId && c.IsDeleted == false && c.LoggedOn >= date
						select c;

			return calls.ToList();
		}

		public Dictionary<string, int> GetNewCallsCountForLast5Days()
		{
			Dictionary<string, int> data = new Dictionary<string, int>();

			var startDate = DateTime.UtcNow.AddDays(-4);
			var filteredRecords =
				_callsRepository.GetAll()
					.Where(
						x => x.LoggedOn >= startDate).ToList();

			data.Add(DateTime.UtcNow.ToShortDateString(), filteredRecords.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-1).ToShortDateString(), filteredRecords.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-1).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-2).ToShortDateString(), filteredRecords.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-2).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-3).ToShortDateString(), filteredRecords.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-3).ToShortDateString()));
			data.Add(DateTime.UtcNow.AddDays(-4).ToShortDateString(), filteredRecords.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-4).ToShortDateString()));

			return data;
		}

		public List<CallType> GetCallTypesForDepartment(int departmentId)
		{
			var callTypes = from type in _callTypesRepository.GetAll()
							where type.DepartmentId == departmentId
							select type;

			return callTypes.ToList();
		}

		public async Task<List<CallType>> GetCallTypesForDepartmentAsync(int departmentId)
		{
			return await _callTypesRepository.GetCallTypesForDepartmentAsync(departmentId);
		}

		public void DeleteCallType(int callTypeId)
		{
			var callTypes = from type in _callTypesRepository.GetAll()
							where type.CallTypeId == callTypeId
							select type;

			if (callTypes.FirstOrDefault() != null)
			{
				_callTypesRepository.DeleteOnSubmit(callTypes.First());
			}
		}

		public CallType GetCallTypeById(int callTypeId)
		{
			return _callTypesRepository.GetAll().FirstOrDefault(x => x.CallTypeId == callTypeId);
		}

		public CallType SaveNewCallType(string callType, int departmentId)
		{
			CallType newCallType = new CallType();
			newCallType.DepartmentId = departmentId;
			newCallType.Type = callType;

			_callTypesRepository.SaveOrUpdate(newCallType);

			return GetCallTypeById(newCallType.CallTypeId);
		}

		public Call GenerateCallFromEmail(int type, CallEmail email, string managingUser, List<IdentityUser> users, Department department, List<Call> activeCalls, List<Unit> units, int priority)
		{
			return _callEmailFactory.GenerateCallFromEmailText((CallEmailTypes)type, email, managingUser, users, department, activeCalls, units, priority);
		}

		public List<int> GetCallsCountForLast7DaysForDepartment(int departmentId)
		{
			List<int> actions = new List<int>();
			var startDate = DateTime.UtcNow.AddDays(-7);

			var callsForLast7Days =
				_callsRepository.GetAll()
					.Where(
						x =>
							x.DepartmentId == departmentId &&
							x.LoggedOn >= startDate).ToList();

			for (int i = 0; i < 7; i++)
			{
				actions.Add(callsForLast7Days.Count(x => x.LoggedOn.ToShortDateString() == DateTime.UtcNow.AddDays(-i).ToShortDateString()));
			}

			return actions;
		}

		public CallNote SaveCallNote(CallNote note)
		{
			_callNotesRepository.SaveOrUpdate(note);

			return note;
		}

		public CallAttachment GetCallAttachment(int callAttachmentId)
		{
			return _callAttachmentRepository.GetAll().FirstOrDefault(x => x.CallAttachmentId == callAttachmentId);
		}

		public CallAttachment SaveCallAttachment(CallAttachment attachment)
		{
			_callAttachmentRepository.SaveOrUpdate(attachment);

			return attachment;
		}

		public void MarkCallDispatchesAsSent(int callId, List<Guid> usersToMark)
		{
			_callsRepository.MarkCallDispatchesAsSent(callId, usersToMark);
		}

		public List<DepartmentCallPriority> GetAllCallPriorities()
		{
			return _departmentCallPriorityRepository.GetAll().ToList();
		}

		public DepartmentCallPriority GetCallPrioritesById(int departmentId, int priorityId, bool bypassCache = false)
		{
			if (priorityId > 3)
				return GetCallPrioritesForDepartment(departmentId, bypassCache).Where(x => x.DepartmentCallPriorityId == priorityId).FirstOrDefault();
			else
				return GetDefaultCallPriorites().Where(x => x.DepartmentCallPriorityId == priorityId).FirstOrDefault();
		}

		public List<DepartmentCallPriority> GetCallPrioritesForDepartment(int departmentId, bool bypassCache = false)
		{
			Func<List<DepartmentCallPriority>> getCallPriorites = delegate ()
			{
				return _departmentCallPriorityRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var departmentPriorities = _cacheProvider.Retrieve(string.Format(CallPrioritiesCacheKey, departmentId), getCallPriorites, Day30CacheLength);

				if (departmentPriorities == null || !departmentPriorities.Any())
					return GetDefaultCallPriorites();
			}

			var departmentPriorities2 = getCallPriorites();
			if (departmentPriorities2 == null || !departmentPriorities2.Any())
				return GetDefaultCallPriorites();

			return departmentPriorities2;
		}

		public List<DepartmentCallPriority> GetActiveCallPrioritesForDepartment(int departmentId, bool bypassCache = false)
		{
			var priorities = GetCallPrioritesForDepartment(departmentId, bypassCache);

			return priorities.Where(x => x.IsDeleted == false).ToList();
		}

		public List<DepartmentCallPriority> GetDefaultCallPriorites()
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

		public DepartmentCallPriority SaveCallPriority(DepartmentCallPriority callPriority)
		{
			_departmentCallPriorityRepository.SaveOrUpdate(callPriority);
			InvalidateCallPrioritiesForDepartmentInCache(callPriority.DepartmentId);

			return callPriority;
		}

		public List<CallProtocol> GetCallProtocolsByCallId(int callId)
		{
			return _callsRepository.GetCallProtocolsByCallId(callId);
		}

		public void InvalidateCallPrioritiesForDepartmentInCache(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CallPrioritiesCacheKey, departmentId));
		}

		public async Task<string> GetShortenedAudioUrlAsync(int callId, int callAttachmentId)
		{
			if (callAttachmentId > 0)
			{
				var attachment = _callAttachmentRepository.GetAll().FirstOrDefault(x =>
					x.CallId == callId && x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

				if (attachment == null)
					return String.Empty;

				var encryptedQuery =
					WebUtility.UrlEncode(SymmetricEncryption.Encrypt(attachment.CallAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase));
				string shortenedUrl =
					await _shortenUrlProvider.ShortenAsync(
						$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/calls/getcallaudio?query={encryptedQuery}");

				if (String.IsNullOrWhiteSpace(shortenedUrl))
					return String.Empty;

				return shortenedUrl;
			}
			else
			{
				var encryptedQuery =
					WebUtility.UrlEncode(SymmetricEncryption.Encrypt(callAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase));
				string shortenedUrl =
					await _shortenUrlProvider.ShortenAsync(
						$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/calls/getcallaudio?query={encryptedQuery}");

				if (String.IsNullOrWhiteSpace(shortenedUrl))
					return String.Empty;

				return shortenedUrl;
			}
		}

		public string GetShortenedAudioUrl(int callId, int callAttachmentId)
		{
			try
			{
				if (callAttachmentId > 0)
				{
					var encryptedQuery =
						WebUtility.UrlEncode(SymmetricEncryption.Encrypt(callAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase));
					string shortenedUrl =
						_shortenUrlProvider.Shorten(
							$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v3/calls/getcallaudio?query={encryptedQuery}");

					if (String.IsNullOrWhiteSpace(shortenedUrl))
						return String.Empty;

					return shortenedUrl;
				}
				else
				{
					var attachment = _callAttachmentRepository.GetAll().FirstOrDefault(x =>
						x.CallId == callId && x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

					if (attachment == null)
						return String.Empty;

					var encryptedQuery =
						WebUtility.UrlEncode(SymmetricEncryption.Encrypt(attachment.CallAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase));
					string shortenedUrl =
						_shortenUrlProvider.Shorten(
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

		public string GetShortenedCallLinkUrl(int callId, bool pdf = false, int? stationId = null)
		{
			try
			{
				string encryptedQuery = "";

				if (!stationId.HasValue && !pdf)
				{
					encryptedQuery = WebUtility.UrlEncode(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));
				}
				else
				{
					string type = pdf ? "pdf" : "web";
					string station = stationId.HasValue ? stationId.Value.ToString() : "0";

					encryptedQuery = WebUtility.UrlEncode(SymmetricEncryption.Encrypt($"{callId.ToString()}|${type}|${station}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));
				}


				string shortenedUrl =
					_shortenUrlProvider.Shorten(
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

		public string GetShortenedCallPdfUrl(int callId, bool pdf = false, int? stationId = null)
		{
			try
			{
				string shortenedUrl =
					_shortenUrlProvider.Shorten(GetCallPdfUrl(callId, pdf, stationId));

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
					encryptedQuery = WebUtility.UrlEncode(SymmetricEncryption.Encrypt(callId.ToString(), Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));
				}
				else
				{
					string type = pdf ? "pdf" : "web";
					string station = stationId.HasValue ? stationId.Value.ToString() : "0";

					encryptedQuery = WebUtility.UrlEncode(SymmetricEncryption.Encrypt($"{callId.ToString()}|{type}|{station}", Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase));
				}


				return $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Dispatch/CallExportPdf?query={encryptedQuery}";


			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return String.Empty;
			}
		}

		public void ClearGroupForDispatches(int departmentGroupId)
		{
			var groupDispatches = _callDispatchGroupRepository.GetAll().Where(x => x.DepartmentGroupId == departmentGroupId);
			_callDispatchGroupRepository.DeleteAll(groupDispatches);
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

		public string CallPriorityToString(int priority, int departmentId)
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
					var priorities = GetCallPrioritesForDepartment(departmentId);

					if (priorities != null && priorities.Any(x => x.DepartmentCallPriorityId == priority))
						return priorities.First(x => x.DepartmentCallPriorityId == priority).Name;
					else
						return "Low";
			}
		}

		public string CallPriorityToColor(int priority, int departmentId)
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
					var priorities = GetCallPrioritesForDepartment(departmentId);

					if (priorities != null && priorities.Any(x => x.DepartmentCallPriorityId == priority))
						return priorities.First(x => x.DepartmentCallPriorityId == priority).Color;
					else
						return "#008000";
			}
		}
	}
}
