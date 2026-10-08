using Resgrid.Web.Services.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Web.Services.Helpers;
using System.Net.Mime;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Web.Services.Models.v4.CallProtocols;
using Resgrid.Web.Services.Models.v4.ContactFiles;
using Resgrid.Web.Services.Models.v4.Contacts;
using Resgrid.Web.Helpers;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Calls, also referred to as Dispatches.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallsController : V4AuthenticatedApiControllerbaseSystemAuth
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IQueueService _queueService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IProtocolsService _protocolsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IShiftsService _shiftsService;
		private readonly IMappingService _mappingService;
		private readonly IUserDefinedFieldsService _userDefinedFieldsService;
		private readonly ICommunicationService _communicationService;
		private readonly IWeatherAlertService _weatherAlertService;
		private readonly ICallDispatchStatusService _callDispatchStatusService;
		private readonly IDispatchRecommendationService _dispatchRecommendationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDepartmentDataProtectionService _dataProtectionService;
		private readonly IProtectedReadService _protectedCallReadService;
		private readonly ICallLocationHistoryService _callLocationHistoryService;
		private readonly IContactsService _contactsService;
		private readonly IDispatchScopeService _dispatchScopeService;
		private readonly IProtectedWriteService _protectedWriteService;
		private readonly IPendingCallsService _pendingCallsService;
		private readonly ICallClosureService _callClosureService;

		public CallsController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IQueueService queueService,
			IUsersService usersService,
			IUnitsService unitsService,
			IActionLogsService actionLogsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IProtocolsService protocolsService,
			IEventAggregator eventAggregator,
			ICustomStateService customStateService,
			IDepartmentSettingsService departmentSettingsService,
			IShiftsService shiftsService,
			IMappingService mappingService,
			IUserDefinedFieldsService userDefinedFieldsService,
			ICommunicationService communicationService,
			IWeatherAlertService weatherAlertService,
			ICallDispatchStatusService callDispatchStatusService,
			IDispatchRecommendationService dispatchRecommendationService,
			IFeatureToggleService featureToggleService,
			IDepartmentDataProtectionService dataProtectionService,
			IProtectedReadService protectedCallReadService,
			IProtectedWriteService protectedWriteService,
			IContactsService contactsService,
			IDispatchScopeService dispatchScopeService,
			ICallLocationHistoryService callLocationHistoryService,
			IPendingCallsService pendingCallsService,
			ICallClosureService callClosureService
			)
		{
			_callClosureService = callClosureService;
			_callLocationHistoryService = callLocationHistoryService;
			_pendingCallsService = pendingCallsService;
			_contactsService = contactsService;
			_dispatchScopeService = dispatchScopeService;
			_dataProtectionService = dataProtectionService;
			_protectedCallReadService = protectedCallReadService;
			_protectedWriteService = protectedWriteService;
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_queueService = queueService;
			_usersService = usersService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_protocolsService = protocolsService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_departmentSettingsService = departmentSettingsService;
			_shiftsService = shiftsService;
			_mappingService = mappingService;
			_userDefinedFieldsService = userDefinedFieldsService;
			_communicationService = communicationService;
			_weatherAlertService = weatherAlertService;
			_callDispatchStatusService = callDispatchStatusService;
			_dispatchRecommendationService = dispatchRecommendationService;
			_featureToggleService = featureToggleService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// True when the caller authenticated as the BigBoard client application (numeric
		/// UserSessionClientApplication claim; tokens predating the claim read as ordinary Api).
		/// </summary>
		private bool IsBigBoardSession =>
			string.Equals(User?.FindFirst(Resgrid.Model.Security.SessionClaimTypes.ClientApp)?.Value,
				((int)UserSessionClientApplication.BigBoard).ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);

		/// <summary>
		/// ADP plan section 7.3: BigBoard is an unattended display and is structurally stepped down.
		/// For a protection-enforced department it receives only a safe shell — system-generated call
		/// number, priority/status/state and safe timestamps survive; user-authored nature/name,
		/// notes, identity, exact address/location and reference identifiers do not. Egress policy
		/// can never relax this.
		/// </summary>
		private async Task<bool> ApplyBigBoardSafeShellAsync(IEnumerable<CallResultData> calls)
		{
			if (calls == null || !IsBigBoardSession)
				return false;

			if (!await _dataProtectionService.IsProtectionEnforcedAsync(DepartmentId))
				return false;

			foreach (var call in calls)
			{
				if (call == null)
					continue;

				call.Name = "Protected incident — open Resgrid to view details.";
				call.Nature = null;
				call.Note = null;
				call.Address = null;
				call.DestinationName = null;
				call.DestinationAddress = null;
				call.DestinationTypeName = null;
				call.DestinationPoiId = null;
				call.DestinationPoiTypeId = null;
				call.DestinationLatitude = null;
				call.DestinationLongitude = null;
				call.Geolocation = null;
				call.What3Words = null;
				call.ContactName = null;
				call.ContactInfo = null;
				call.ReferenceId = null;
				call.ExternalId = null;
				call.SubjectIdentifiers = null;
				call.IncidentId = null;
				call.AudioFileId = null;
				call.Type = null;
				call.Latitude = null;
				call.Longitude = null;
				call.Contacts = new List<CallContactResultData>();
			}

			return true;
		}

		/// <summary>The caller's Protected Data Grant, when presented (plan section 3.1 step 6).</summary>
		private string ProtectedGrantToken => Request.Headers[DataProtectionController.GrantHeader].ToString();

		/// <summary>
		/// Attended protected-read resolution (plan section 7.1), run BEFORE ConvertCall so the DTO
		/// carries broker-decrypted plaintext (valid grant) or the exact REDACTED placeholder —
		/// never ciphertext. One broker round trip per request. BigBoard sessions still get the
		/// safe shell afterwards, which strips everything regardless.
		/// </summary>
		private async Task<Dictionary<int, ProtectedReadResult>> ResolveProtectedReadsAsync(IReadOnlyList<Call> calls)
		{
			var results = await _protectedCallReadService.ResolveForReadAsync(DepartmentId, calls, ProtectedGrantToken, UserId);

			var map = new Dictionary<int, ProtectedReadResult>();
			foreach (var read in results)
			{
				if (read.Call != null)
					map[read.Call.CallId] = read;
			}

			return map;
		}

		/// <summary>Maps a blocked protected write to its value-free problem response (plan 3.3/19.2).</summary>
		private ObjectResult ProtectedWriteProblem(ProtectedWriteResult write) =>
			Problem(type: write.Reason,
				title: write.Reason == "broker_unavailable"
					? "Protected storage is temporarily unavailable; the change was not saved."
					: "Recent multi-factor verification is required to modify protected data.",
				statusCode: write.Reason == "broker_unavailable" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden);

		/// <summary>
		/// The people, of those given, whose position the caller may see (Security &gt; See Personnel Locations, the
		/// location matrix the map uses). A department system key sees all of them.
		/// </summary>
		private async Task<HashSet<string>> GetLocatablePersonIdsAsync(IEnumerable<string> userIds)
		{
			var locatable = new HashSet<string>(StringComparer.Ordinal);

			foreach (var userId in userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
			{
				if (IsSystemApiKeyRequest || await _authorizationService.CanUserViewPersonLocationViaMatrixAsync(userId, UserId, DepartmentId))
					locatable.Add(userId);
			}

			return locatable;
		}

		/// <summary>
		/// The units, of those given, whose position the caller may see (Security &gt; See Unit Locations, the location
		/// matrix the map uses). A department system key sees all of them.
		/// </summary>
		private async Task<HashSet<int>> GetLocatableUnitIdsAsync(IEnumerable<int> unitIds)
		{
			var locatable = new HashSet<int>();

			foreach (var unitId in unitIds.Distinct())
			{
				if (IsSystemApiKeyRequest || await UnitLocationVisibility.CanSeeAsync(_authorizationService, unitId, UserId, DepartmentId))
					locatable.Add(unitId);
			}

			return locatable;
		}

		private static void ApplyProtectedReadMetadata(CallResultData data, ProtectedReadResult read)
		{
			if (data == null || read == null)
				return;

			data.IsProtected = read.IsProtected;
			data.RedactedFields = read.RedactedFields ?? new List<string>();
			data.ProtectedReason = read.ProtectedReason;
		}

		/// <summary>
		/// Returns all the active calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetActiveCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<ActiveCallsResult>> GetActiveCalls()
		{
			var result = new ActiveCallsResult();

			// Group-scoped dispatch (off by default) trims this to the caller's area and the calls they are on.
			var calls = (await ScopeCallsAsync(await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)))
				.OrderByDescending(x => x.LoggedOn).ToList();
			var destinationPois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var destinationPoiLookup = destinationPois.ToDictionary(x => x.PoiId);

			if (calls != null && calls.Any())
			{
				// Resolve BEFORE per-call processing so geocoding and templates see plaintext (or
				// REDACTED), never envelopes.
				var protectedReads = await ResolveProtectedReadsAsync(calls);
				var contactPairs = new List<KeyValuePair<Call, CallResultData>>();

				foreach (var c in calls)
				{
					var callWithData = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
							address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
					}
					else
						address = c.Address;

					destinationPoiLookup.TryGetValue(callWithData.DestinationPoiId.GetValueOrDefault(), out var destinationPoi);
					var callData = ConvertCall(callWithData, null, address, TimeZone, destinationPoi);
					if (protectedReads.TryGetValue(callWithData.CallId, out var protectedRead))
						ApplyProtectedReadMetadata(callData, protectedRead);
					contactPairs.Add(new KeyValuePair<Call, CallResultData>(callWithData, callData));
					result.Data.Add(callData);
				}

				// Linked contacts with pre-plan / alert / hazard indicators (Contacts plan Phase A, 3a).
				await ApplyCallContactsAsync(DepartmentId, contactPairs);

				await ApplyBigBoardSafeShellAsync(result.Data);
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Returns a specific call from the Resgrid System
		/// </summary>
		/// <param name="callId">Id of the call trying to be retrived</param>
		/// <returns>CallResult of the call in the Resgrid system</returns>
		[HttpGet("GetCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<GetCallResult>> GetCall(string callId, [FromQuery] string departmentId = null)
		{
			if (!int.TryParse(callId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCallId))
				return BadRequest();

			var result = new CallResult();
			var c = await _callsService.GetCallByIdAsync(parsedCallId);

			if (c == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var effectiveDepartmentId = GetEffectiveDepartmentId(departmentId);

			if (c.DepartmentId != effectiveDepartmentId)
				return Unauthorized();

			if (!IsSystemApiKeyRequest && !await CanViewOrEditCallAsync(parsedCallId, edit: false))
				return Unauthorized();

			c = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

			// Attended protected read (plan 7.1), AFTER populate so loaded notes/attachments resolve
			// in the same batch, and BEFORE geocoding, protocols and conversion. A system API key
			// carries no grant, so protected values stay REDACTED for it by construction (plan 3.4).
			var protectedRead = await _protectedCallReadService.ResolveForReadAsync(effectiveDepartmentId, c,
				ProtectedGrantToken, UserId);

			var destinationPoi = await GetValidatedDestinationPoiAsync(c.DestinationPoiId, effectiveDepartmentId);

			string address = "";
			if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
			{
				var geo = c.GeoLocationData.Split(char.Parse(","));

				if (geo.Length == 2)
					address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
			}
			else
				address = c.Address;

			var protocols = new List<DispatchProtocol>();
			if (c.Protocols != null && c.Protocols.Any())
			{
				foreach (var callProtocol in c.Protocols)
				{
					var protocol = await _protocolsService.GetProtocolByIdAsync(callProtocol.DispatchProtocolId);
					if (protocol != null)
						protocols.Add(protocol);
				}
			}

			result.Data = ConvertCall(c, protocols, address, TimeZone, destinationPoi);
			ApplyProtectedReadMetadata(result.Data, protectedRead);

			// Linked contacts with pre-plan / alert / hazard indicators (Contacts plan Phase A, 3a).
			await ApplyCallContactsAsync(effectiveDepartmentId, new List<KeyValuePair<Call, CallResultData>> { new KeyValuePair<Call, CallResultData>(c, result.Data) });

			// BigBoard shells also suppress UDF submissions — user-authored free text defaults to
			// sensitive in a protected department (plan section 5.2).
			if (await ApplyBigBoardSafeShellAsync(new[] { result.Data }))
			{
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			// Populate UDF values the caller may see: the Udf view right (ViewUdfFields) and each field's visibility,
			// the same rule the UserDefinedFields endpoints apply.
			var udfValues = await _userDefinedFieldsService.GetFieldValuesForEntityAsync(effectiveDepartmentId, (int)UdfEntityType.Call, c.CallId.ToString());
			if (udfValues != null && udfValues.Any())
				udfValues = await _userDefinedFieldsService.FilterValuesVisibleToUserAsync(effectiveDepartmentId, (int)UdfEntityType.Call, udfValues,
					IsSystemApiKeyRequest || HttpContext.User.HasClaim(ResgridClaimTypes.Resources.Udf, ResgridClaimTypes.Actions.View),
					IsSystemApiKeyRequest || ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
					HttpContext.User.Claims.Any(x => x.Type.StartsWith(ResgridClaimTypes.Resources.Group + "/", StringComparison.Ordinal) && x.Value == ResgridClaimTypes.Actions.Update));
			if (udfValues != null && udfValues.Any())
			{
				result.Data.UdfValues = udfValues.Select(v => new UdfFieldValueResultData
				{
					UdfFieldValueId = v.UdfFieldValueId,
					UdfFieldId = v.UdfFieldId,
					UdfDefinitionId = v.UdfDefinitionId,
					EntityId = v.EntityId,
					EntityType = v.EntityType,
					Value = v.Value
				}).ToList();
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Previous calls at this call's location: other calls at the same address however it was typed, nearby calls
		/// without a street address, and calls linked to the same contacts, newest first with their notes. Limited to
		/// the calls the user may see; address matching is off while the department's Advanced Data Protection is on.
		/// </summary>
		/// <param name="callId">Id of the call</param>
		[HttpGet("GetCallLocationHistory")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<LocationHistoryResult>> GetCallLocationHistory(string callId)
		{
			if (!int.TryParse(callId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCallId))
				return BadRequest();

			var result = new LocationHistoryResult();
			var call = await _callsService.GetCallByIdAsync(parsedCallId);
			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!IsSystemApiKeyRequest && !await _authorizationService.CanUserViewCallAsync(UserId, parsedCallId))
				return Unauthorized();

			var history = await _callLocationHistoryService.GetHistoryForCallAsync(DepartmentId, UserId, parsedCallId);
			result.Data = await LocationHistoryResultBuilder.BuildAsync(history, DepartmentId, ProtectedGrantToken, UserId, _protectedCallReadService, _callsService, _departmentsService);
			result.PageSize = result.Data.Calls.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets the site information for every contact linked to a call in one round trip: the contact,
		/// its pre-incident plan, premise hazards, live alert notes and file metadata with signed download
		/// links (Contacts plan Phase A, decision 3b). This is the RMS/NERIS authoring prefill contract for
		/// calls linked to a Contact: additive changes only. Signed file links expire; do not persist them.
		/// </summary>
		/// <param name="callId">Id of the call</param>
		[HttpGet("GetCallSiteInfo")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallSiteInfoResult>> GetCallSiteInfo(string callId)
		{
			if (!int.TryParse(callId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCallId))
				return BadRequest();

			var result = new CallSiteInfoResult();
			var call = await _callsService.GetCallByIdAsync(parsedCallId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!IsSystemApiKeyRequest && !await _authorizationService.CanUserViewCallAsync(UserId, parsedCallId))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// Security > View Contacts: without it the call carries no site information (an empty list, not a refusal).
			var siteInfo = IsSystemApiKeyRequest || User.HasClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View)
				? await _contactsService.GetCallSiteInfoAsync(parsedCallId, DepartmentId)
				: null;

			result.Data = new CallSiteInfoData { CallId = parsedCallId.ToString() };

			if (siteInfo != null && siteInfo.Contacts.Any())
			{
				// Attended protected read (plan 7.1): contact identity and note text decrypt with a valid
				// grant or read as REDACTED. Pre-plan and hazard text is not cataloged and passes through.
				var contacts = siteInfo.Contacts.Select(x => x.Contact).ToList();
				var protectedRead = await _protectedCallReadService.ResolveContactsForReadAsync(DepartmentId, contacts, ProtectedGrantToken, UserId);
				result.Data.IsProtected = protectedRead.IsProtected;
				result.Data.ProtectedReason = protectedRead.ProtectedReason;

				var alertNotes = siteInfo.Contacts.SelectMany(x => x.AlertNotes).ToList();
				if (alertNotes.Any())
					await _protectedCallReadService.ResolveContactNotesForReadAsync(DepartmentId, alertNotes, ProtectedGrantToken, UserId);

				// Catalog v12: pre-plan text, hazard text and attachment names resolve in the same request.
				var preplans = siteInfo.Contacts.Where(x => x.Preplan != null).Select(x => x.Preplan).ToList();
				var preplanRead = preplans.Any()
					? await _protectedCallReadService.ResolveContactPreplansForReadAsync(DepartmentId, preplans, ProtectedGrantToken, UserId)
					: new ProtectedReadResult();
				var hazards = siteInfo.Contacts.SelectMany(x => x.Hazards).ToList();
				var hazardRead = hazards.Any()
					? await _protectedCallReadService.ResolveContactPreplanHazardsForReadAsync(DepartmentId, hazards, ProtectedGrantToken, UserId)
					: new ProtectedReadResult();
				var attachments = siteInfo.Contacts.SelectMany(x => x.Attachments).ToList();
				var attachmentRead = attachments.Any()
					? await _protectedCallReadService.ResolveContactAttachmentsForReadAsync(DepartmentId, attachments, ProtectedGrantToken, UserId, includeData: false)
					: new ProtectedReadResult();

				result.Data.IsProtected = result.Data.IsProtected || preplanRead.IsProtected || hazardRead.IsProtected || attachmentRead.IsProtected;
				result.Data.ProtectedReason ??= preplanRead.ProtectedReason ?? hazardRead.ProtectedReason ?? attachmentRead.ProtectedReason;

				var noteTypes = await _contactsService.GetContactNoteTypesByDepartmentIdAsync(DepartmentId);

				foreach (var entry in siteInfo.Contacts)
				{
					var data = new CallSiteContactData
					{
						ContactId = entry.Contact.ContactId,
						Name = SafeContactName(entry.Contact),
						ContactType = entry.Contact.ContactType,
						CallContactType = entry.CallContactType,
						LocationGpsCoordinates = entry.Contact.LocationGpsCoordinates,
						EntranceGpsCoordinates = entry.Contact.EntranceGpsCoordinates,
						PhoneNumber = FirstNonEmpty(entry.Contact.CellPhoneNumber, entry.Contact.OfficePhoneNumber, entry.Contact.HomePhoneNumber)
					};

					if (entry.Preplan != null)
					{
						data.Preplan = ContactsController.ConvertPreplanData(entry.Preplan, department);
						ContactsController.ApplyProtection(data.Preplan, preplanRead, hazardRead);
						if (!string.IsNullOrWhiteSpace(entry.Preplan.ContactPreplanId) && entry.Preplan.ContactPreplanId.StartsWith("occ:", StringComparison.Ordinal))
							data.OccupancyId = entry.Preplan.ContactPreplanId.Substring(4);
					}

					data.Hazards = entry.Hazards.Select(h =>
					{
						var hazardData = ContactsController.ConvertHazardData(h, department);
						hazardData.IsProtected = hazardRead.IsProtected;
						hazardData.ProtectedReason = hazardRead.ProtectedReason;
						return hazardData;
					}).ToList();

					foreach (var note in entry.AlertNotes)
					{
						var noteType = string.IsNullOrWhiteSpace(note.ContactNoteTypeId) ? null : noteTypes.FirstOrDefault(t => t.ContactNoteTypeId == note.ContactNoteTypeId);
						var noteData = new ContactNoteResultData
						{
							ContactNoteId = note.ContactNoteId,
							ContactId = note.ContactId,
							ContactNoteTypeId = note.ContactNoteTypeId,
							NoteType = noteType?.Name,
							Note = note.Note,
							ShouldAlert = note.ShouldAlert,
							Visibility = note.Visibility,
							ExpiresOnUtc = note.ExpiresOn,
							AddedOnUtc = note.AddedOn,
							AddedOn = note.AddedOn.FormatForDepartment(department),
							AddedByUserId = note.AddedByUserId,
							IsProtected = result.Data.IsProtected,
							ProtectedReason = result.Data.ProtectedReason
						};
						if (note.ExpiresOn.HasValue)
							noteData.ExpiresOn = note.ExpiresOn.Value.FormatForDepartment(department);
						data.AlertNotes.Add(noteData);
					}

					data.Attachments = entry.Attachments.Select(a =>
					{
						var fileData = ContactFilesController.ConvertContactFileData(a, department, false);
						fileData.IsProtected = attachmentRead.IsProtected;
						fileData.ProtectedReason = attachmentRead.ProtectedReason;
						return fileData;
					}).ToList();

					result.Data.Contacts.Add(data);
				}
			}

			result.PageSize = result.Data.Contacts.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		[HttpGet("GetCallExtraData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CallExtraDataResult>> GetCallExtraData(int callId)
		{
			var result = new CallExtraDataResult();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			// Attended protected read (plan 7.1): CallFormData is a cataloged field — decrypt with a
			// valid grant or hand back REDACTED, never an envelope.
			await _protectedCallReadService.ResolveForReadAsync(DepartmentId, call, ProtectedGrantToken, UserId);

			// BigBoard step-down: dispatched unit/personnel state below is allowlisted resource
			// state, but submitted call form data is protected content (plan section 7.3).
			if (IsBigBoardSession && await _dataProtectionService.IsProtectionEnforcedAsync(DepartmentId))
				result.Data.CallFormData = null;
			else
				result.Data.CallFormData = call.CallFormData;

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			// The call's units as dispatched, deleted ones included.
			var units = await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(call.DepartmentId);
			var unitStates = (await _unitsService.GetUnitStatesForCallAsync(call.DepartmentId, callId)).OrderBy(y => y.Timestamp).ThenBy(x => x.UnitId).ToList();
			var actionLogs = (await _actionLogsService.GetActionLogsForCallAsync(call.DepartmentId, callId)).OrderBy(y => y.Timestamp).ThenBy(x => x.UserId).ToList();
			var names = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(call.DepartmentId);

			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId);
			var defaultUnitStatuses = _customStateService.GetDefaultUnitStatuses();
			var defaultUserStatuses = _customStateService.GetDefaultPersonStatuses();

			if (priority != null)
			{
				result.Data.Priority = CallPrioritiesController.ConvertPriorityData(priority);
			}

			// The positions each status was set at go out under See Personnel Locations / See Unit Locations, the rules the map applies.
			var locatablePeople = await GetLocatablePersonIdsAsync(actionLogs.Select(x => x.UserId));
			var locatableUnits = await GetLocatableUnitIdsAsync(unitStates.Select(x => x.UnitId));

			foreach (var actionLog in actionLogs)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = actionLog.ActionLogId.ToString();
				eventResult.Timestamp = actionLog.Timestamp;
				eventResult.Type = "User";

				var name = names.FirstOrDefault(x => x.UserId == actionLog.UserId);
				if (name != null)
				{
					eventResult.Name = name.Name;

					if (name.DepartmentGroupId.HasValue)
					{
						eventResult.GroupId = name.DepartmentGroupId.Value.ToString();
						eventResult.Group = name.DepartmentGroupName;
					}
				}
				else
				{
					eventResult.Name = "Unknown User";
				}

				eventResult.StatusId = actionLog.ActionTypeId;
				eventResult.Location = locatablePeople.Contains(actionLog.UserId) ? actionLog.GeoLocationData : null;
				eventResult.Note = actionLog.Note;
				eventResult.DestinationSource = actionLog.DestinationSource;

				var personnelStatus = ResolveStatusDetail(actionLog.ActionTypeId, defaultUserStatuses, customStates);
				if (personnelStatus != null)
				{
					eventResult.StatusText = personnelStatus.ButtonText;
					eventResult.StatusColor = personnelStatus.ButtonColor;
				}
				else if (actionLog.ActionTypeId <= CallStatusLinkage.MaxBuiltInStatusId)
				{
					eventResult.StatusText = actionLog.GetActionText();
				}

				if (String.IsNullOrWhiteSpace(eventResult.StatusText))
					eventResult.StatusText = "Unknown";

				if (String.IsNullOrWhiteSpace(eventResult.StatusColor))
					eventResult.StatusColor = "#ffa500";

				result.Data.Activity.Add(eventResult);
			}

			foreach (var unitLog in unitStates)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = unitLog.UnitStateId.ToString();
				eventResult.Timestamp = unitLog.Timestamp;
				eventResult.Type = "Unit";
				eventResult.Name = unitLog.Unit.Name;

				var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unitLog.Unit.StationGroupId);
				if (group != null)
				{
					eventResult.GroupId = group.DepartmentGroupId.ToString();
					eventResult.Group = group.Name;
				}

				eventResult.StatusId = unitLog.State;
				eventResult.Location = locatableUnits.Contains(unitLog.UnitId) ? unitLog.GeoLocationData : null;
				eventResult.Note = unitLog.Note;
				eventResult.DestinationSource = unitLog.DestinationSource;

				var unitStatus = ResolveStatusDetail(unitLog.State, defaultUnitStatuses, customStates);
				if (unitStatus != null)
				{
					eventResult.StatusText = unitStatus.ButtonText;
					eventResult.StatusColor = unitStatus.ButtonColor;
				}
				else if (unitLog.State <= CallStatusLinkage.MaxBuiltInStatusId)
				{
					eventResult.StatusText = unitLog.GetStatusText();
				}

				if (String.IsNullOrWhiteSpace(eventResult.StatusText))
					eventResult.StatusText = "Unknown";

				if (String.IsNullOrWhiteSpace(eventResult.StatusColor))
					eventResult.StatusColor = "#ffa500";

				result.Data.Activity.Add(eventResult);
			}

			foreach (var dispatch in call.Dispatches)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = dispatch.UserId;
				if (dispatch.LastDispatchedOn.HasValue)
				{
					eventResult.Timestamp = dispatch.LastDispatchedOn.Value;
				}
				eventResult.Type = "User";

				var name = names.FirstOrDefault(x => x.UserId == dispatch.UserId);
				if (name != null)
				{
					eventResult.Name = name.Name;

					if (name.DepartmentGroupId.HasValue)
					{
						eventResult.GroupId = name.DepartmentGroupId.Value.ToString();
						eventResult.Group = name.DepartmentGroupName;
					}
				}
				else
				{
					eventResult.Name = "Unknown User";
				}

				result.Data.Dispatches.Add(eventResult);
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = groupDispatch.DepartmentGroupId.ToString();
					if (groupDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = groupDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Group";

					var name = groups.FirstOrDefault(x => x.DepartmentGroupId == groupDispatch.DepartmentGroupId);
					if (name != null)
					{
						eventResult.Name = name.Name;
						eventResult.GroupId = name.DepartmentGroupId.ToString();
						eventResult.Group = name.Name;

					}
					else
					{
						eventResult.Name = "Unknown Group";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = unitDispatch.UnitId.ToString();
					if (unitDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = unitDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Unit";

					var unit = units.FirstOrDefault(x => x.UnitId == unitDispatch.UnitId);
					if (unit != null)
					{
						eventResult.Name = unit.Name;

						if (unit.StationGroupId.HasValue)
						{
							var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.GetValueOrDefault());
							if (group != null)
							{
								eventResult.GroupId = group.DepartmentGroupId.ToString();
								eventResult.Group = group.Name;

							}
						}

					}
					else
					{
						eventResult.Name = "Unknown Unit";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = roleDispatch.RoleId.ToString();
					if (roleDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = roleDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Role";

					var role = roles.FirstOrDefault(x => x.PersonnelRoleId == roleDispatch.RoleId);
					if (role != null)
					{
						eventResult.Name = role.Name;
					}
					else
					{
						eventResult.Name = "Unknown Role";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.Protocols != null && call.Protocols.Any())
			{
				foreach (var callProtocol in call.Protocols)
				{
					var protocol = await _protocolsService.GetProtocolByIdAsync(callProtocol.DispatchProtocolId);

					if (protocol != null)
						result.Data.Protocols.Add(CallProtocolsController.ConvertProtocolData(protocol));
				}
			}

			result.PageSize = 0;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Saves a call in the Resgrid system
		/// </summary>
		/// <param name="newCallInput"></param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns></returns>
		/// <summary>
		/// Gets the department's new-call field policy: which built-in fields the call form should show
		/// and which it must require before the call can be created.
		/// </summary>
		/// <remarks>
		/// An empty rule list means the stock form -- every field visible, nothing extra required.
		/// Clients apply this to their new and edit call forms for usability; the same policy is enforced on SaveCall
		/// and EditCall regardless.
		/// </remarks>
		[HttpGet("GetNewCallFieldPolicy")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<NewCallFieldPolicyResult>> GetNewCallFieldPolicy()
		{
			var result = new NewCallFieldPolicyResult();
			var policy = await _departmentSettingsService.GetNewCallFieldPolicyAsync(DepartmentId);

			if (policy?.Rules != null)
			{
				foreach (var rule in policy.Rules)
				{
					result.Data.Rules.Add(new NewCallFieldRuleData
					{
						Key = rule.Key,
						Visible = rule.Visible,
						Required = policy.IsRequired(rule.Key)
					});
				}
			}

			result.PageSize = result.Data.Rules.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		[HttpPost("SaveCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsCreate)]
		public async Task<ActionResult<SaveCallResult>> SaveCall([FromBody] NewCallInput newCallInput, CancellationToken cancellationToken)
		{
			var result = new SaveCallResult();

			var effectiveDepartmentId = GetEffectiveDepartmentId(newCallInput.DepartmentId);

			if (!IsSystemApiKeyRequest)
			{
				var canDoOperation = await _authorizationService.CanUserCreateCallAsync(UserId, effectiveDepartmentId);
				if (!canDoOperation)
					return Unauthorized();
			}

			if (!ModelState.IsValid)
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(effectiveDepartmentId);

			if (department == null)
				return BadRequest($"Department not found: {effectiveDepartmentId}");

			var activeUsers = await _departmentsService.GetAllMembersForDepartmentAsync(effectiveDepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(effectiveDepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(effectiveDepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(effectiveDepartmentId);
			var destinationPoi = await GetValidatedDestinationPoiAsync(newCallInput.DestinationPoiId, effectiveDepartmentId);

			if (newCallInput.DestinationPoiId.HasValue && newCallInput.DestinationPoiId.Value > 0 && destinationPoi == null)
				return BadRequest();

			// A pending call is saved for a dispatcher to send later: nobody is notified, and any scheduled
			// time is ignored because the dispatcher decides when it goes out.
			var isPending = newCallInput.IsPending == true;

			// Resolved before the policy check, so an id from another department cannot satisfy a linked-call requirement.
			Call linkedCall = null;
			if (!string.IsNullOrWhiteSpace(newCallInput.LinkedCallId))
			{
				if (!int.TryParse(newCallInput.LinkedCallId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int linkedCallId))
					return BadRequest("LinkedCallId is not a call id.");

				linkedCall = await _callsService.GetCallByIdAsync(linkedCallId);

				if (linkedCall == null || linkedCall.DepartmentId != effectiveDepartmentId)
					return BadRequest("LinkedCallId is not a call in this department.");
			}

			// Ids that are not this department's protocols are dropped, like unknown entries on the dispatch list.
			var protocols = new List<DispatchProtocol>();
			if (newCallInput.ProtocolIds != null && newCallInput.ProtocolIds.Any())
			{
				var departmentProtocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(effectiveDepartmentId) ?? new List<DispatchProtocol>();
				protocols = departmentProtocols.Where(x => newCallInput.ProtocolIds.Contains(x.DispatchProtocolId)).ToList();
			}

			// The department's new-call field policy is enforced here, not only in the clients: an old
			// build, an offline-queued call or a third-party integration must not be able to put an
			// incomplete call in front of the crews. Departments with no policy configured are unaffected.
			var fieldPolicy = await _departmentSettingsService.GetNewCallFieldPolicyAsync(effectiveDepartmentId);
			var fieldValues = new NewCallFieldValues
			{
				Note = newCallInput.Note,
				Address = newCallInput.Address,
				Geolocation = newCallInput.Geolocation,
				What3Words = newCallInput.What3Words,
				ContactName = newCallInput.ContactName,
				ContactInfo = newCallInput.ContactInfo,
				ExternalId = newCallInput.ExternalId,
				IncidentId = newCallInput.IncidentId,
				ReferenceId = newCallInput.ReferenceId,
				DestinationPoiId = newCallInput.DestinationPoiId,
				IndoorMapZoneId = newCallInput.IndoorMapZoneId,
				HasProtocols = protocols.Any(),
				HasLinkedCall = linkedCall != null,
				DispatchOn = isPending ? null : (newCallInput.DispatchOnUtc ?? newCallInput.DispatchOn),
				HasDispatchList = !string.IsNullOrWhiteSpace(newCallInput.DispatchList),
				IsPending = isPending
			};

			// A client that never sends these has no picker for them (older builds, the field apps, integrations); a
			// requirement it cannot meet would stop it creating calls at all.
			if (newCallInput.IndoorMapZoneId == null)
				fieldValues.Unsupported(NewCallFieldKeys.IndoorLocation);

			if (newCallInput.ProtocolIds == null)
				fieldValues.Unsupported(NewCallFieldKeys.Protocols);

			if (newCallInput.LinkedCallId == null)
				fieldValues.Unsupported(NewCallFieldKeys.LinkedCall);

			var fieldViolations = NewCallFieldPolicyValidator.Validate(fieldPolicy, fieldValues);

			if (fieldViolations.Count > 0)
				return BadRequest(NewCallFieldPolicyValidator.DescribeViolations(fieldViolations));

			var subjectIdentifierErrors = CallSubjectIdentifiers.Validate(newCallInput.SubjectIdentifiers);
			if (subjectIdentifierErrors.Count > 0)
				return BadRequest(string.Join("; ", subjectIdentifierErrors));

			var call = new Call
			{
				DepartmentId = effectiveDepartmentId,
				ReportingUserId = UserId,
				Priority = newCallInput.Priority,
				Name = newCallInput.Name,
				NatureOfCall = newCallInput.Nature,
				State = isPending ? (int)CallStates.Pending : (int)CallStates.Active
			};

			if (!string.IsNullOrWhiteSpace(newCallInput.ContactName))
				call.ContactName = newCallInput.ContactName;

			if (!string.IsNullOrWhiteSpace(newCallInput.ContactInfo))
				call.ContactNumber = newCallInput.ContactInfo;

			if (!string.IsNullOrWhiteSpace(newCallInput.ExternalId))
				call.ExternalIdentifier = newCallInput.ExternalId;

			// Plaintext here; the protected-write pass below envelopes it through the no-grant workload lane (or the
			// caller's grant) exactly like every other cataloged call field.
			call.SubjectIdentifiers = CallSubjectIdentifiers.Serialize(newCallInput.SubjectIdentifiers);
			call.Part2ConsentOnFile = newCallInput.Part2ConsentOnFile ?? false;

			if (!string.IsNullOrWhiteSpace(newCallInput.IncidentId))
				call.IncidentNumber = newCallInput.IncidentId;

			if (!string.IsNullOrWhiteSpace(newCallInput.ReferenceId))
				call.ReferenceNumber = newCallInput.ReferenceId;

			if (!string.IsNullOrWhiteSpace(newCallInput.Address))
				call.Address = newCallInput.Address;

			call.DestinationPoiId = destinationPoi?.PoiId;

			if (!string.IsNullOrWhiteSpace(newCallInput.What3Words))
				call.W3W = newCallInput.What3Words;

			// Forms module is disabled: CallFormData is read-only now. The input property stays on the
			// contract so older clients keep deserializing, but anything they send is dropped.

			if (!string.IsNullOrWhiteSpace(newCallInput.IndoorMapZoneId))
				call.IndoorMapZoneId = newCallInput.IndoorMapZoneId;

			if (!string.IsNullOrWhiteSpace(newCallInput.IndoorMapFloorId))
				call.IndoorMapFloorId = newCallInput.IndoorMapFloorId;

			if (protocols.Any())
				call.Protocols = protocols.Select(x => new CallProtocol { DispatchProtocolId = x.DispatchProtocolId, Data = x.Code }).ToList();

			if (linkedCall != null)
				call.References = new List<CallReference> { new CallReference { TargetCallId = linkedCall.CallId, AddedOn = DateTime.UtcNow, AddedByUserId = UserId } };

			if (newCallInput.DispatchOnUtc.HasValue && !isPending)
			{
				call.DispatchOn = DateTime.SpecifyKind(newCallInput.DispatchOnUtc.Value.Kind == DateTimeKind.Local
					? newCallInput.DispatchOnUtc.Value.ToUniversalTime() : newCallInput.DispatchOnUtc.Value, DateTimeKind.Utc);
				call.HasBeenDispatched = false;
			}
			else if (newCallInput.DispatchOn.HasValue && !isPending)
			{
				call.DispatchOn = DateTimeHelpers.ConvertToUtc(newCallInput.DispatchOn.Value, department.TimeZone);
				call.HasBeenDispatched = false;
			}

			if (!string.IsNullOrWhiteSpace(newCallInput.Note))
				call.Notes = newCallInput.Note;

			// Clients with no position send "," (or 0,0). Storing that as the location skipped the address and
			// What3Words lookups below, so the call was saved unlocated and run cards could not rank anything.
			if (GeoMath.ParseLatLonString(newCallInput.Geolocation) != null)
				call.GeoLocationData = newCallInput.Geolocation;

			if (GeoMath.ParseLatLonString(call.GeoLocationData) == null && !string.IsNullOrWhiteSpace(call.Address))
				call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

			if (GeoMath.ParseLatLonString(call.GeoLocationData) == null && !string.IsNullOrWhiteSpace(call.W3W))
			{
				var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(call.W3W);

				if (coords != null)
				{
					call.GeoLocationData = $"{coords.Latitude},{coords.Longitude}";
				}
			}

			call.LoggedOn = DateTime.UtcNow;

			if (newCallInput.CheckInTimersEnabled.HasValue)
				call.CheckInTimersEnabled = newCallInput.CheckInTimersEnabled.Value;
			else
			{
				var autoEnable = await _departmentSettingsService.GetCheckInTimersAutoEnableForNewCallsAsync(effectiveDepartmentId);
				call.CheckInTimersEnabled = autoEnable;
			}

			if (!String.IsNullOrWhiteSpace(newCallInput.Type) && newCallInput.Type != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(effectiveDepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == newCallInput.Type);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(effectiveDepartmentId);
			call.Dispatches = new Collection<CallDispatch>();
			call.GroupDispatches = new List<CallDispatchGroup>();
			call.RoleDispatches = new List<CallDispatchRole>();
			call.UnitDispatches = new List<CallDispatchUnit>();

			// An empty list means "everyone" for an immediate call (older clients send nothing), but for a pending call
			// it means no proposed recipients yet: the dispatcher who picks it up chooses them. The system key never pages
			// everyone on its own, but a pending call pages nobody until a dispatcher sends it, so "0" is honored there.
			if ((newCallInput.DispatchList == "0" && (!IsSystemApiKeyRequest || isPending)) ||
				(!IsSystemApiKeyRequest && !isPending && string.IsNullOrWhiteSpace(newCallInput.DispatchList)))
			{
				// Use case, existing clients and non-ionic2 app this will be null dispatch all users. Or we've specified everyone (0).
				foreach (var u in users)
				{
					var cd = new CallDispatch { UserId = u.UserId };

					call.Dispatches.Add(cd);
				}
			}
			else if (!string.IsNullOrWhiteSpace(newCallInput.DispatchList) && newCallInput.DispatchList != "0")
			{
				var dispatch = newCallInput.DispatchList.Split(char.Parse("|"));

				try
				{
					var usersToDispatch = dispatch.Where(x => x.StartsWith("P:")).Select(y => y.Replace("P:", ""));
					foreach (var user in usersToDispatch)
					{
						if (activeUsers.Any(x => x.UserId == user && x.IsDeleted == false && x.IsDisabled == false))
						{
							var cd = new CallDispatch { UserId = user };
							call.Dispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var groupsToDispatch = DispatchListHelper.ResolveIds(dispatch,"G:",
						name => groups.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.DepartmentGroupId);
					foreach (var group in groupsToDispatch)
					{
						if (groups.Any(x => x.DepartmentGroupId == group))
						{
							var cd = new CallDispatchGroup { DepartmentGroupId = group };
							call.GroupDispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var rolesToDispatch = DispatchListHelper.ResolveIds(dispatch,"R:",
						name => roles.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.PersonnelRoleId);
					foreach (var role in rolesToDispatch)
					{
						if (roles.Any(x => x.PersonnelRoleId == role))
						{
							var cd = new CallDispatchRole { RoleId = role };
							call.RoleDispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var unitsToDispatch = DispatchListHelper.ResolveIds(dispatch,"U:",
						name => units.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.UnitId);
					foreach (var unit in unitsToDispatch)
					{
						if (units.Any(x => x.UnitId == unit))
						{
							var cdu = new CallDispatchUnit { UnitId = unit };
							call.UnitDispatches.Add(cdu);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			var shouldDispatchNow = !isPending && (!call.DispatchOn.HasValue || call.DispatchOn.Value <= DateTime.UtcNow);

			// Call is in the past or is now, were dispatching now (at the end of this func)
			if (call.DispatchOn.HasValue && call.DispatchOn.Value <= DateTime.UtcNow)
				call.HasBeenDispatched = true;

			// Run card auto-dispatch: additively merge recommended resources before save
			// (only applies when the resolved auto-dispatch decision is on). Covers all
			// API-originated calls, including chatbot/MCP sources.
			DispatchRecommendationResult recommendationResult = null;
			if (shouldDispatchNow && await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, effectiveDepartmentId))
				recommendationResult = await _dispatchRecommendationService.EnrichCallForDispatchAsync(call, 1, true, cancellationToken);

			// ADP write preflight (plan 3.3): refuse BEFORE inserting — attended callers need a
			// current grant; system-key/workload callers pass (broker encrypt-only lane).
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(effectiveDepartmentId,
				ProtectedGrantToken, UserId, IsUnattendedWriter, cancellationToken);
			if (!writePreflight.Success)
				return ProtectedWriteProblem(writePreflight);

			// Contacts plan Phase A (A5): API-created calls can link department contacts.
			var contactLinks = await BuildCallContactLinksAsync(effectiveDepartmentId, newCallInput.ContactId, newCallInput.AdditionalContactIds);
			if (contactLinks == null)
				return BadRequest("One or more contacts do not belong to this department.");
			if (contactLinks.Any())
				call.Contacts = contactLinks;

			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			if (recommendationResult != null && recommendationResult.MatchedRunCardId.HasValue && recommendationResult.AutoDispatch)
				await _dispatchRecommendationService.RecordActivationAsync(savedCall, recommendationResult, UserId, cancellationToken);

			// Attach weather alerts as call notes if enabled
			await _weatherAlertService.AttachWeatherAlertsToCallAsync(savedCall, cancellationToken);

			// ADP two-phase write (plan 19.2): the identity pk is an AAD component, so encryption
			// runs after the insert assigned it, then the enveloped row is persisted. A failure here
			// leaves a LOGGED transient-plaintext row and fails the request (the next migration
			// sweep also envelopes it); downstream broadcast uses the enveloped call, which the
			// notification safe-projections turn into generic content.
			var protectedWrite = await _protectedWriteService.PrepareCallWriteAsync(effectiveDepartmentId, savedCall,
				null, ProtectedGrantToken, UserId, IsUnattendedWriter, cancellationToken);
			if (!protectedWrite.Success)
			{
				Logging.LogError($"ADP protected write failed AFTER insert for call {savedCall.CallId} in department {effectiveDepartmentId} ({protectedWrite.Reason}); transient plaintext row pending re-encryption.");
				return ProtectedWriteProblem(protectedWrite);
			}
			if (protectedWrite.IsProtected)
				savedCall = await _callsService.SaveCallAsync(savedCall, cancellationToken);

			//OutboundEventProvider handler = new OutboundEventProvider.CallAddedTopicHandler();
			//OutboundEventProvider..Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });
			// A pending call is not live yet: "added" (incident chat channel, Call Added workflows, the field apps'
			// lists) waits until it is dispatched. The update still refreshes dispatcher screens.
			if (isPending)
				_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = effectiveDepartmentId, Call = savedCall });
			else
				_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = effectiveDepartmentId, Call = savedCall });

			if (shouldDispatchNow && ((call.GroupDispatches != null && call.GroupDispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any())))
			{
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(savedCall,
					call.GroupDispatches?.Select(x => x.DepartmentGroupId),
					call.UnitDispatches?.Select(x => x.UnitId),
					cancellationToken);
			}

			var profiles = new List<string>();

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				profiles.AddRange(call.Dispatches.Select(x => x.UserId).ToList());
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var group = await _departmentGroupsService.GetGroupByIdAsync(groupDispatch.DepartmentGroupId);

					if (group != null && group.Members != null)
					{
						profiles.AddRange(group.Members.Select(x => x.UserId));
					}
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var members = await _personnelRolesService.GetAllMembersOfRoleAsync(roleDispatch.RoleId);

					if (members != null)
					{
						profiles.AddRange(members.Select(x => x.UserId).ToList());
					}
				}
			}

			var cqi = new CallQueueItem();
			cqi.Call = savedCall;

			if (profiles.Any())
				cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(profiles);

			if (shouldDispatchNow)
				await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

			// Save UDF field values if supplied
			if (newCallInput.UdfValues != null && newCallInput.UdfValues.Any())
			{
				bool isDeptAdmin = IsSystemApiKeyRequest || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = HttpContext.User.Claims
					.Any(c => c.Type.StartsWith(ResgridClaimTypes.Resources.Group + "/", StringComparison.Ordinal)
						&& c.Value == ResgridClaimTypes.Actions.Update);

				var udfValues = newCallInput.UdfValues.Select(v => new UdfFieldValue
				{
					UdfFieldId = v.UdfFieldId,
					Value = v.Value
				}).ToList();
				await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(effectiveDepartmentId, (int)UdfEntityType.Call, savedCall.CallId.ToString(), udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);
			}

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;

			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction("GetCall", new { callId = result.Id }, result);
		}

		/// <summary>
		/// Updates an existing Active Call in the Resgrid system
		/// </summary>
		/// <param name="editCallInput">Data to updated the call</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>OK status code if successful</returns>
		[HttpPut("EditCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsUpdate)]
		public async Task<ActionResult<EditCallResult>> EditCall([FromBody] EditCallInput editCallInput, CancellationToken cancellationToken)
		{
			var result = new EditCallResult();

			if (editCallInput == null || !ModelState.IsValid ||
				!int.TryParse(editCallInput.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int callId))
				return BadRequest();

			var canDoOperation = await CanViewOrEditCallAsync(callId, edit: true);

			if (!canDoOperation)
				return Unauthorized();

			var call = await _callsService.GetCallByIdAsync(callId);

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			// A pending call can be edited before it is dispatched; nobody is notified of those edits.
			var isPending = call.State == (int)CallStates.Pending;

			if (call.State != (int)CallStates.Active && !isPending)
				return BadRequest();

			// The new-call field policy applies to edits too: a field the department hides is not on the client's edit
			// form, so a blank value for it keeps what is stored, and the call must still have every required field after
			// the edit.
			var fieldPolicy = await _departmentSettingsService.GetNewCallFieldPolicyAsync(DepartmentId) ?? new NewCallFieldPolicy();

			var activeUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var destinationPoi = await GetValidatedDestinationPoiAsync(editCallInput.DestinationPoiId);

			if (editCallInput.DestinationPoiId.HasValue && editCallInput.DestinationPoiId.Value > 0 && destinationPoi == null)
				return BadRequest();

			// ADP: snapshot the stored cataloged values BEFORE the input overwrites them, so a
			// round-tripped REDACTED sentinel restores the stored envelope instead of persisting
			// the literal placeholder.
			var storedCatalogedValues = Resgrid.Services.ProtectedReadService.SnapshotCatalogedCallFields(call);

			call.Priority = editCallInput.Priority;
			call.Name = editCallInput.Name;
			call.NatureOfCall = editCallInput.Nature;

			if (!string.IsNullOrWhiteSpace(editCallInput.ContactName))
				call.ContactName = editCallInput.ContactName;

			if (!string.IsNullOrWhiteSpace(editCallInput.ContactInfo))
				call.ContactNumber = editCallInput.ContactInfo;

			if (!string.IsNullOrWhiteSpace(editCallInput.ExternalId))
				call.ExternalIdentifier = editCallInput.ExternalId;

			// Null leaves the stored identifiers (usually an envelope) untouched; an object replaces them.
			if (editCallInput.SubjectIdentifiers != null)
			{
				var subjectIdentifierErrors = CallSubjectIdentifiers.Validate(editCallInput.SubjectIdentifiers);
				if (subjectIdentifierErrors.Count > 0)
					return BadRequest(string.Join("; ", subjectIdentifierErrors));
				call.SubjectIdentifiers = CallSubjectIdentifiers.Serialize(editCallInput.SubjectIdentifiers);
			}

			if (editCallInput.Part2ConsentOnFile.HasValue)
				call.Part2ConsentOnFile = editCallInput.Part2ConsentOnFile.Value;

			if (!string.IsNullOrWhiteSpace(editCallInput.IncidentId))
				call.IncidentNumber = editCallInput.IncidentId;

			if (!string.IsNullOrWhiteSpace(editCallInput.ReferenceId))
				call.ReferenceNumber = editCallInput.ReferenceId;

			var addressChanged = !string.IsNullOrWhiteSpace(editCallInput.Address)
				&& !string.Equals(editCallInput.Address, call.Address, StringComparison.Ordinal);

			if (!string.IsNullOrWhiteSpace(editCallInput.Address))
				call.Address = editCallInput.Address;

			// No destination posted for a hidden destination means the client never showed it, not that it was cleared.
			if (destinationPoi != null || fieldPolicy.IsVisible(NewCallFieldKeys.DestinationPoi))
				call.DestinationPoiId = destinationPoi?.PoiId;

			if (!string.IsNullOrWhiteSpace(editCallInput.What3Words))
				call.W3W = editCallInput.What3Words;

			if (!string.IsNullOrWhiteSpace(editCallInput.IndoorMapZoneId))
			{
				call.IndoorMapZoneId = editCallInput.IndoorMapZoneId;
				call.IndoorMapFloorId = editCallInput.IndoorMapFloorId;
			}

			// Added to what the call already has, the same as the web edit form: neither can remove one here.
			if (editCallInput.ProtocolIds != null && editCallInput.ProtocolIds.Any())
			{
				var departmentProtocols = await _protocolsService.GetAllProtocolsForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>();

				if (call.Protocols == null)
					call.Protocols = new List<CallProtocol>();

				foreach (var protocol in departmentProtocols.Where(x => editCallInput.ProtocolIds.Contains(x.DispatchProtocolId)))
				{
					if (!call.Protocols.Any(x => x.DispatchProtocolId == protocol.DispatchProtocolId))
						call.Protocols.Add(new CallProtocol { CallId = call.CallId, DispatchProtocolId = protocol.DispatchProtocolId, Data = protocol.Code });
				}
			}

			if (!string.IsNullOrWhiteSpace(editCallInput.LinkedCallId))
			{
				if (!int.TryParse(editCallInput.LinkedCallId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int linkedCallId) || linkedCallId == call.CallId)
					return BadRequest("LinkedCallId is not another call's id.");

				var linkedCall = await _callsService.GetCallByIdAsync(linkedCallId);

				if (linkedCall == null || linkedCall.DepartmentId != DepartmentId)
					return BadRequest("LinkedCallId is not a call in this department.");

				if (call.References == null)
					call.References = new List<CallReference>();

				if (!call.References.Any(x => x.TargetCallId == linkedCallId))
					call.References.Add(new CallReference { SourceCallId = call.CallId, TargetCallId = linkedCallId, AddedOn = DateTime.UtcNow, AddedByUserId = UserId });
			}

			// Forms module is disabled: ignoring the posted value leaves whatever form data the call
			// already carries intact, so an edit from an older client can't wipe or replace it.

			if (editCallInput.DispatchOnUtc.HasValue || editCallInput.DispatchOn.HasValue)
			{
				var dispatchOn = editCallInput.DispatchOnUtc.HasValue
					? DateTime.SpecifyKind(editCallInput.DispatchOnUtc.Value.Kind == DateTimeKind.Local
						? editCallInput.DispatchOnUtc.Value.ToUniversalTime() : editCallInput.DispatchOnUtc.Value, DateTimeKind.Utc)
					: DateTimeHelpers.ConvertToUtc(editCallInput.DispatchOn.Value, department.TimeZone);

				if (!isPending)
				{
					call.DispatchOn = dispatchOn;
					call.HasBeenDispatched = false;
				}
				else if (dispatchOn > DateTime.UtcNow)
				{
					// Giving a pending call a future dispatch time turns it into a scheduled call, which the
					// scheduled-calls worker sends at that time. A time that has passed leaves it pending.
					call.State = (int)CallStates.Active;
					call.DispatchOn = dispatchOn;
					call.HasBeenDispatched = false;
					isPending = false;
				}
			}

			if (!string.IsNullOrWhiteSpace(editCallInput.Note))
				call.Notes = editCallInput.Note;

			// "," (or 0,0) from a client with no position is not a location: it neither replaces the stored point
			// nor blocks the lookups below. A new address with no posted point drops the old address's point.
			if (GeoMath.ParseLatLonString(editCallInput.Geolocation) != null)
				call.GeoLocationData = editCallInput.Geolocation;
			else if (addressChanged)
				call.GeoLocationData = null;

			if (GeoMath.ParseLatLonString(call.GeoLocationData) == null && !string.IsNullOrWhiteSpace(call.Address))
				call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

			if (GeoMath.ParseLatLonString(call.GeoLocationData) == null && !string.IsNullOrWhiteSpace(call.W3W))
			{
				var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(call.W3W);

				if (coords != null)
				{
					call.GeoLocationData = $"{coords.Latitude},{coords.Longitude}";
				}
			}

			if (!String.IsNullOrWhiteSpace(editCallInput.Type) && editCallInput.Type != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == editCallInput.Type);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}

			// Capture existing dispatch snapshots for cancel notification diffing
			var existingDispatches = new List<CallDispatch>(call.Dispatches ?? new List<CallDispatch>());
			var existingGroupDispatches = new List<CallDispatchGroup>(call.GroupDispatches ?? new List<CallDispatchGroup>());
			var existingUnitDispatches = new List<CallDispatchUnit>(call.UnitDispatches ?? new List<CallDispatchUnit>());
			var existingRoleDispatches = new List<CallDispatchRole>(call.RoleDispatches ?? new List<CallDispatchRole>());

			if ((isPending || !fieldPolicy.IsVisible(NewCallFieldKeys.DispatchList)) && string.IsNullOrWhiteSpace(editCallInput.DispatchList))
			{
				// No list on an edit of a pending call keeps its proposed recipients (an empty list would otherwise mean
				// "everyone"). The same when the department hides the dispatch list: the edit form never had one, so
				// treating the blank as "everyone" would page the whole department on every edit.
				if (call.Dispatches == null)
					call.Dispatches = new List<CallDispatch>();

				if (call.GroupDispatches == null)
					call.GroupDispatches = new List<CallDispatchGroup>();

				if (call.RoleDispatches == null)
					call.RoleDispatches = new List<CallDispatchRole>();

				if (call.UnitDispatches == null)
					call.UnitDispatches = new List<CallDispatchUnit>();
			}
			else if (string.IsNullOrWhiteSpace(editCallInput.DispatchList) || editCallInput.DispatchList == "0")
			{
				if (call.Dispatches == null)
					call.Dispatches = new List<CallDispatch>();

				if (call.GroupDispatches == null)
					call.GroupDispatches = new List<CallDispatchGroup>();

				if (call.RoleDispatches == null)
					call.RoleDispatches = new List<CallDispatchRole>();

				if (call.UnitDispatches == null)
					call.UnitDispatches = new List<CallDispatchUnit>();

				var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
				// Use case, existing clients and non-ionic2 app this will be null dispatch all users. Or we've specified everyone (0).
				foreach (var u in users)
				{
					var cd = new CallDispatch { UserId = u.UserId };

					call.Dispatches.Add(cd);
				}
			}
			else
			{
				var dispatch = editCallInput.DispatchList.Split(char.Parse("|"));
				var usersToDispatch = dispatch.Where(x => x.StartsWith("P:")).Select(y => y.Replace("P:", "")).ToList();
				var groupsToDispatch = DispatchListHelper.ResolveIds(dispatch,"G:",
					name => groups.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.DepartmentGroupId);
				var rolesToDispatch = DispatchListHelper.ResolveIds(dispatch,"R:",
					name => roles.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.PersonnelRoleId);
				var unitsToDispatch = DispatchListHelper.ResolveIds(dispatch,"U:",
					name => units.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.UnitId);

				try
				{
					if (call.Dispatches == null)
						call.Dispatches = new List<CallDispatch>();

					var dispatchesToRemove = call.Dispatches.Select(x => x.UserId).Where(y => !usersToDispatch.Contains(y)).ToList();

					foreach (var userId in dispatchesToRemove)
					{
						var item = call.Dispatches.First(x => x.UserId == userId);
						call.Dispatches.Remove(item);
					}

					foreach (var user in usersToDispatch)
					{
						if (!call.Dispatches.Any(x => x.UserId == user))
						{
							if (activeUsers.Any(x => x.UserId == user && x.IsDeleted == false && x.IsDisabled == false))
							{
								var cd = new CallDispatch { CallId = call.CallId, UserId = user };
								call.Dispatches.Add(cd);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.GroupDispatches == null)
						call.GroupDispatches = new List<CallDispatchGroup>();

					var dispatchesToRemove = call.GroupDispatches.Select(x => x.DepartmentGroupId).Where(y => !groupsToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.GroupDispatches.Remove(call.GroupDispatches.First(x => x.DepartmentGroupId == id));
					}

					foreach (var group in groupsToDispatch)
					{
						if (!call.GroupDispatches.Any(x => x.DepartmentGroupId == group))
						{
							if (groups.Any(x => x.DepartmentGroupId == group))
							{
								var cdg = new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = group };
								call.GroupDispatches.Add(cdg);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.RoleDispatches == null)
						call.RoleDispatches = new List<CallDispatchRole>();

					var dispatchesToRemove = call.RoleDispatches.Select(x => x.RoleId).Where(y => !rolesToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.RoleDispatches.Remove(call.RoleDispatches.First(x => x.RoleId == id));
					}

					foreach (var role in rolesToDispatch)
					{
						if (!call.RoleDispatches.Any(x => x.RoleId == role))
						{
							if (roles.Any(x => x.PersonnelRoleId == role))
							{
								var cdr = new CallDispatchRole { CallId = call.CallId, RoleId = role };
								call.RoleDispatches.Add(cdr);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.UnitDispatches == null)
						call.UnitDispatches = new List<CallDispatchUnit>();

					var dispatchesToRemove = call.UnitDispatches.Select(x => x.UnitId).Where(y => !unitsToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.UnitDispatches.Remove(call.UnitDispatches.First(x => x.UnitId == id));
					}

					foreach (var unit in unitsToDispatch)
					{
						if (!call.UnitDispatches.Any(x => x.UnitId == unit))
						{
							if (units.Any(x => x.UnitId == unit))
							{
								var cdu = new CallDispatchUnit { CallId = call.CallId, UnitId = unit };
								call.UnitDispatches.Add(cdu);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Checked against the call as the edit leaves it (a blank input keeps the stored value) and before anything
			// below changes stored data. The dispatch time only means something before a call goes out.
			var fieldValues = new NewCallFieldValues
			{
				Note = call.Notes,
				Address = call.Address,
				Geolocation = call.GeoLocationData,
				What3Words = call.W3W,
				ContactName = call.ContactName,
				ContactInfo = call.ContactNumber,
				ExternalId = call.ExternalIdentifier,
				IncidentId = call.IncidentNumber,
				ReferenceId = call.ReferenceNumber,
				DestinationPoiId = call.DestinationPoiId,
				IndoorMapZoneId = call.IndoorMapZoneId,
				HasProtocols = call.Protocols?.Any() ?? false,
				HasLinkedCall = call.References?.Any() ?? false,
				HasDispatchList = (call.Dispatches?.Any() ?? false) || (call.GroupDispatches?.Any() ?? false) ||
								  (call.UnitDispatches?.Any() ?? false) || (call.RoleDispatches?.Any() ?? false),
				IsPending = isPending
			}.Unsupported(NewCallFieldKeys.DispatchOn);

			// As on SaveCall: a client that never sends these has no picker for them, and a requirement it cannot meet
			// would stop it saving any edit.
			if (editCallInput.IndoorMapZoneId == null)
				fieldValues.Unsupported(NewCallFieldKeys.IndoorLocation);

			if (editCallInput.ProtocolIds == null)
				fieldValues.Unsupported(NewCallFieldKeys.Protocols);

			if (editCallInput.LinkedCallId == null)
				fieldValues.Unsupported(NewCallFieldKeys.LinkedCall);

			var fieldViolations = NewCallFieldPolicyValidator.Validate(fieldPolicy, fieldValues);

			if (fieldViolations.Count > 0)
				return BadRequest(NewCallFieldPolicyValidator.DescribeViolations(fieldViolations));

			// Call is in the past or is now, were dispatching now (at the end of this func)
			if (call.DispatchOn.HasValue && call.DispatchOn.Value <= DateTime.UtcNow)
				call.HasBeenDispatched = true;

			// ADP protected write (plan 19.2): cataloged plaintext encrypts in place before
			// persistence; a blocked write persists nothing.
			// Contacts plan Phase A (A5): replace the contact links only when the input carries them.
			if (editCallInput.ContactId != null || editCallInput.AdditionalContactIds != null)
			{
				var contactLinks = await BuildCallContactLinksAsync(DepartmentId, editCallInput.ContactId, editCallInput.AdditionalContactIds);
				if (contactLinks == null)
					return BadRequest("One or more contacts do not belong to this department.");

				await _callsService.DeleteCallContactsAsync(call.CallId, cancellationToken);
				call.Contacts = contactLinks;
			}

			var protectedWrite = await _protectedWriteService.PrepareCallWriteAsync(DepartmentId, call,
				storedCatalogedValues, ProtectedGrantToken, UserId, IsUnattendedWriter, cancellationToken);
			if (!protectedWrite.Success)
				return ProtectedWriteProblem(protectedWrite);

			await _callsService.SaveCallAsync(call, cancellationToken);

			// Attach weather alerts as call notes if enabled (deduplication handled inside)
			await _weatherAlertService.AttachWeatherAlertsToCallAsync(call, cancellationToken);

			var currentUserIds = call.Dispatches?.Select(x => x.UserId).ToList() ?? new List<string>();
			var currentGroupIds = call.GroupDispatches?.Select(x => x.DepartmentGroupId).ToList() ?? new List<int>();
			var currentUnitIds = call.UnitDispatches?.Select(x => x.UnitId).ToList() ?? new List<int>();
			var currentRoleIds = call.RoleDispatches?.Select(x => x.RoleId).ToList() ?? new List<int>();

			var cancelledUserIds = existingDispatches.Select(x => x.UserId)
				.Where(y => !currentUserIds.Contains(y)).ToList();
			var cancelledGroupIds = existingGroupDispatches.Select(x => x.DepartmentGroupId)
				.Where(y => !currentGroupIds.Contains(y)).ToList();
			var cancelledUnitIds = existingUnitDispatches.Select(x => x.UnitId)
				.Where(y => !currentUnitIds.Contains(y)).ToList();
			var cancelledRoleIds = existingRoleDispatches.Select(x => x.RoleId)
				.Where(y => !currentRoleIds.Contains(y)).ToList();

			var newUserIds = currentUserIds.Where(id => !existingDispatches.Any(d => d.UserId == id)).ToList();
			var newGroupIds = currentGroupIds.Where(id => !existingGroupDispatches.Any(d => d.DepartmentGroupId == id)).ToList();
			var newUnitIds = currentUnitIds.Where(id => !existingUnitDispatches.Any(d => d.UnitId == id)).ToList();
			var newRoleIds = currentRoleIds.Where(id => !existingRoleDispatches.Any(d => d.RoleId == id)).ToList();

			// Only a call that has gone out notifies anyone or moves statuses: a pending call, or a scheduled call
			// before its time, sends the edited list when it is dispatched.
			var isLive = call.State == (int)CallStates.Active &&
				(call.HasBeenDispatched.GetValueOrDefault() || !call.DispatchOn.HasValue || call.DispatchOn.Value <= DateTime.UtcNow);
			var shouldApplyDispatchStatuses = isLive;
			if (shouldApplyDispatchStatuses && (cancelledGroupIds.Any() || cancelledUnitIds.Any()))
			{
				await _callDispatchStatusService.ApplyReleaseStatusesAsync(call, cancelledGroupIds, cancelledUnitIds, cancellationToken);
			}

			if (shouldApplyDispatchStatuses && (newGroupIds.Any() || newUnitIds.Any()))
			{
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(call, newGroupIds, newUnitIds, cancellationToken);
			}

			// Send cancel notifications to removed entities
			if (isLive && editCallInput.NotifyCancelledEntities)
			{
				if (cancelledUserIds.Any() || cancelledGroupIds.Any() || cancelledUnitIds.Any() || cancelledRoleIds.Any())
				{
					var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(DepartmentId);

					// Build set of still-dispatched user IDs for dedup
					var stillDispatchedUserIds = new HashSet<string>(currentUserIds);
					foreach (var gd in call.GroupDispatches)
					{
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(gd.DepartmentGroupId);
						foreach (var m in members) stillDispatchedUserIds.Add(m.UserId);
					}
					foreach (var rd in call.RoleDispatches)
					{
						var members = await _personnelRolesService.GetAllMembersOfRoleAsync(rd.RoleId);
						foreach (var m in members) stillDispatchedUserIds.Add(m.UserId);
					}

					var notifiedUserIds = new HashSet<string>();

					// Cancel personnel
					foreach (var userId in cancelledUserIds)
					{
						if (!stillDispatchedUserIds.Contains(userId) && notifiedUserIds.Add(userId))
						{
							var cd = new CallDispatch { CallId = call.CallId, UserId = userId };
							await _communicationService.SendCancelCallAsync(call, cd, departmentNumber, DepartmentId);
						}
					}

					// Cancel group members
					foreach (var groupId in cancelledGroupIds)
					{
						var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);
						foreach (var member in members)
						{
							if (!stillDispatchedUserIds.Contains(member.UserId) && notifiedUserIds.Add(member.UserId))
							{
								var cd = new CallDispatch { CallId = call.CallId, UserId = member.UserId };
								await _communicationService.SendCancelCallAsync(call, cd, departmentNumber, DepartmentId);
							}
						}
					}

					// Cancel role members
					foreach (var roleId in cancelledRoleIds)
					{
						var members = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);
						foreach (var member in members)
						{
							if (!stillDispatchedUserIds.Contains(member.UserId) && notifiedUserIds.Add(member.UserId))
							{
								var cd = new CallDispatch { CallId = call.CallId, UserId = member.UserId };
								await _communicationService.SendCancelCallAsync(call, cd, departmentNumber, DepartmentId);
							}
						}
					}

					// Cancel units
					foreach (var unitId in cancelledUnitIds)
					{
						var cdu = new CallDispatchUnit { CallId = call.CallId, UnitId = unitId };
						await _communicationService.SendCancelUnitCallAsync(call, cdu, departmentNumber);
					}
				}
			}

			// Auto-dispatch newly added entities when RebroadcastCall is not checked
			if (isLive && !editCallInput.RebroadcastCall)
			{
				if (newUserIds.Any() || newGroupIds.Any() || newUnitIds.Any() || newRoleIds.Any())
				{
					var cqi = new CallQueueItem();
					cqi.Call = call;
					cqi.SetBroadcastDispatches(newUserIds, newGroupIds, newUnitIds, newRoleIds);

					if (newGroupIds.Any() || newUnitIds.Any() || newRoleIds.Any())
						cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId)).Select(x => x.Value).ToList();
					else
						cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(newUserIds);

					await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
				}
			}

			if (isLive && editCallInput.RebroadcastCall)
			{
				var cqi = new CallQueueItem();
				cqi.Call = call;

				// If we have any group, unit or role dispatches just bet the farm and all all profiles for now.
				if (cqi.Call.GroupDispatches.Any() || cqi.Call.UnitDispatches.Any() || cqi.Call.RoleDispatches.Any())
					cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId)).Select(x => x.Value).ToList();
				else if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
					cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());
				else
					cqi.Profiles = new List<UserProfile>();


				if (cqi.Call.Dispatches.Any() || cqi.Call.GroupDispatches.Any() || cqi.Call.UnitDispatches.Any() || cqi.Call.RoleDispatches.Any())
					await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
			}

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = call });

			// Save UDF field values if supplied
			if (editCallInput.UdfValues != null && editCallInput.UdfValues.Any())
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = HttpContext.User.Claims
					.Any(c => c.Type.StartsWith(ResgridClaimTypes.Resources.Group + "/", StringComparison.Ordinal)
						&& c.Value == ResgridClaimTypes.Actions.Update);

				var udfValues = editCallInput.UdfValues.Select(v => new UdfFieldValue
				{
					UdfFieldId = v.UdfFieldId,
					Value = v.Value
				}).ToList();
				await _userDefinedFieldsService.SaveFieldValuesForEntityAsync(DepartmentId, (int)UdfEntityType.Call, call.CallId.ToString(), udfValues, UserId, isDeptAdmin, isGroupAdmin, cancellationToken);
			}

			result.Id = call.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Updates a call's scheduled dispatch time if it has not been dispatched
		/// </summary>
		/// <param name="input">Data to update</param>
		/// <returns></returns>
		[HttpPut("UpdateScheduledDispatchTime")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsUpdate)]
		public async Task<ActionResult<UpdateScheduledDispatchTimeResult>> UpdateScheduledDispatchTime(UpdateDispatchTimeInput input)
		{
			var result = new UpdateScheduledDispatchTimeResult();
			if (input == null || !int.TryParse(input.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduledCallId))
				return BadRequest();

			var canDoOperation = await CanViewOrEditCallAsync(scheduledCallId, edit: true);

			if (!canDoOperation)
				return Unauthorized();

			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(scheduledCallId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.DispatchOn = DateTimeHelpers.ConvertToUtc(input.Date, department.TimeZone);
			call.HasBeenDispatched = false;

			var savedCall = await _callsService.SaveCallAsync(call);
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// "Strike Next Alarm": escalates a call to its next alarm level, additively
		/// dispatching the active run card's next-level requirements and notifying only
		/// the newly added resources.
		/// </summary>
		/// <param name="callId">The call to escalate</param>
		/// <returns></returns>
		[HttpPut("EscalateCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<EscalateCallResult>> EscalateCall(string callId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(callId) || !int.TryParse(callId, out var parsedCallId))
				return BadRequest();

			var canDoOperation = await _authorizationService.CanUserEditCallAsync(UserId, parsedCallId);

			if (!canDoOperation)
				return Unauthorized();

			if (!await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId))
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(parsedCallId);

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

			var previousAlarmLevel = Math.Max(1, call.AlarmLevel);
			var escalationResult = await _dispatchRecommendationService.EnrichCallForDispatchAsync(call, previousAlarmLevel + 1, false, cancellationToken);

			if (!escalationResult.MatchedRunCardId.HasValue || !escalationResult.HasRecommendations)
			{
				var noopResult = new EscalateCallResult
				{
					Id = parsedCallId.ToString(),
					Success = false,
					NewAlarmLevel = previousAlarmLevel,
					AddedUnits = 0,
					AddedPersonnel = 0,
					PageSize = 0,
					Status = ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(noopResult);

				return Ok(noopResult);
			}

			var newUnitIds = escalationResult.Units.Select(u => u.UnitId).ToList();
			var newUserIds = escalationResult.Personnel.Select(p => p.UserId).ToList();

			var escalatedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			await _dispatchRecommendationService.RecordActivationAsync(escalatedCall, escalationResult, UserId, cancellationToken);

			if (newUnitIds.Any())
				await _callDispatchStatusService.ApplyDispatchStatusesAsync(escalatedCall, null, newUnitIds, cancellationToken);

			var escalationCqi = new CallQueueItem();
			escalationCqi.Call = escalatedCall;

			if (newUserIds.Any())
				escalationCqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(newUserIds);
			else
				escalationCqi.Profiles = new List<UserProfile>();

			escalationCqi.SetBroadcastDispatches(newUserIds, new List<int>(), newUnitIds, new List<int>());

			await _queueService.EnqueueCallBroadcastAsync(escalationCqi, cancellationToken);

			_eventAggregator.SendMessage<CallAlarmEscalatedEvent>(new CallAlarmEscalatedEvent
			{
				DepartmentId = DepartmentId,
				CallId = escalatedCall.CallId,
				PreviousAlarmLevel = previousAlarmLevel,
				NewAlarmLevel = escalatedCall.AlarmLevel,
				AddedUnitIds = newUnitIds,
				AddedUserIds = newUserIds
			});

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = escalatedCall });

			var result = new EscalateCallResult
			{
				Id = escalatedCall.CallId.ToString(),
				Success = true,
				NewAlarmLevel = escalatedCall.AlarmLevel,
				AddedUnits = newUnitIds.Count,
				AddedPersonnel = newUserIds.Count,
				PageSize = 0,
				Status = ResponseHelper.Updated
			};
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Deletes a call
		/// </summary>
		/// <param name="callId">ID of the call</param>
		/// <returns></returns>
		[HttpDelete("DeleteCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Delete)]
		public async Task<ActionResult<DeleteCallResult>> DeleteCall(string callId)
		{
			var result = new DeleteCallResult();

			if (String.IsNullOrWhiteSpace(callId))
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(callId));

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var canDoOperation = await _authorizationService.CanUserDeleteCallAsync(UserId, int.Parse(callId), DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.IsDeleted = true;
			var savedCall = await _callsService.SaveCallAsync(call);

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Closes a Resgrid call
		/// </summary>
		/// <param name="closeCallInput">Data to close a call</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>OK status code if successful</returns>
		[HttpPut("CloseCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsClose)]
		public async Task<ActionResult<CloseCallResult>> CloseCall([FromBody] CloseCallInput closeCallInput, CancellationToken cancellationToken)
		{
			var result = new CloseCallResult();

			if (!ModelState.IsValid)
				return BadRequest();

			// Only a closing state: 0 (Active) or 8 (Pending) here would re-open a call or put it back in the queue.
			if (closeCallInput.Type < (int)CallStates.Closed || closeCallInput.Type > (int)CallStates.FalseAlarm)
				return BadRequest("Type must be a closed call state (1-7).");

			if (!int.TryParse(closeCallInput.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var closeCallId))
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(closeCallId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var canDoOperation = await _authorizationService.CanUserCloseCallAsync(UserId, closeCallId, DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			// A call run under an active incident command is closed from the command first (IC app: End Command,
			// which can close the call in the same step).
			if (await _callClosureService.GetBlockingIncidentCommandAsync(DepartmentId, call.CallId) != null)
				return BadRequest("This call has an active incident command. Close the incident command first, then close the call.");

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			// Captured before the state changes: a pending call, or a scheduled call that never went out, had no
			// dispatch statuses applied, so there are none to release.
			var wasLive = call.State == (int)CallStates.Active &&
				(call.HasBeenDispatched.GetValueOrDefault() || !call.DispatchOn.HasValue || call.DispatchOn.Value <= DateTime.UtcNow);

			call.ClosedByUserId = UserId;
			call.ClosedOn = DateTime.UtcNow;
			call.CompletedNotes = closeCallInput.Notes;
			call.State = closeCallInput.Type;

			// ADP protected write (plan 19.2): CompletedNotes is cataloged; encrypt before persisting.
			var protectedWrite = await _protectedWriteService.PrepareCallWriteAsync(DepartmentId, call,
				null, ProtectedGrantToken, UserId, IsUnattendedWriter, cancellationToken);
			if (!protectedWrite.Success)
				return ProtectedWriteProblem(protectedWrite);

			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			_eventAggregator.SendMessage<CallClosedEvent>(new CallClosedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			if (wasLive && ((call.GroupDispatches != null && call.GroupDispatches.Any()) || (call.UnitDispatches != null && call.UnitDispatches.Any())))
			{
				await _callDispatchStatusService.ApplyReleaseStatusesAsync(call, cancellationToken: cancellationToken);
			}

			// Nobody was told about a pending call or a scheduled call that never went out, so there is nobody to tell.
			if (closeCallInput.SendNotification == true && wasLive)
				await _callClosureService.NotifyCallClosedAsync(call, UserId, cancellationToken);

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the non-dispatched (pending) scheduled calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetAllPendingScheduledCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<ScheduledCallsResult>> GetAllPendingScheduledCalls()
		{
			var result = new ScheduledCallsResult();

			// Scheduled calls are active calls: group-scoped dispatch trims them as it does GetActiveCalls.
			var calls = (await ScopeCallsAsync(await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(DepartmentId)))
				.OrderBy(x => x.DispatchOn).ToList();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var destinationPois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var destinationPoiLookup = destinationPois.ToDictionary(x => x.PoiId);

			if (calls != null && calls.Any())
			{
				var protectedReads = await ResolveProtectedReadsAsync(calls);

				foreach (var c in calls)
				{
					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
							address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
					}
					else
						address = c.Address;

					destinationPoiLookup.TryGetValue(c.DestinationPoiId.GetValueOrDefault(), out var destinationPoi);
					var callData = ConvertCall(c, null, address, TimeZone, destinationPoi);
					if (protectedReads.TryGetValue(c.CallId, out var protectedRead))
						ApplyProtectedReadMetadata(callData, protectedRead);
					result.Data.Add(callData);
				}

				await ApplyBigBoardSafeShellAsync(result.Data);
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Returns the department's pending calls: saved but not yet dispatched (State 8), oldest first. Pending calls
		/// come from Calls/SaveCall with IsPending, e.g. follow-ups fed in from another system, and wait here for a
		/// dispatcher to send them with Calls/DispatchCallNow.
		/// </summary>
		/// <returns>Array of CallResult objects for each pending call in the department</returns>
		[HttpGet("GetPendingCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<PendingCallsResult>> GetPendingCalls()
		{
			var result = new PendingCallsResult();

			var calls = (await ScopeCallsAsync(await _callsService.GetPendingCallsByDepartmentIdAsync(DepartmentId)))
				.OrderBy(x => x.LoggedOn).ToList();
			var destinationPois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var destinationPoiLookup = destinationPois.ToDictionary(x => x.PoiId);

			if (calls.Any())
			{
				var protectedReads = await ResolveProtectedReadsAsync(calls);

				foreach (var c in calls)
				{
					destinationPoiLookup.TryGetValue(c.DestinationPoiId.GetValueOrDefault(), out var destinationPoi);
					var callData = ConvertCall(c, null, c.Address, TimeZone, destinationPoi);
					if (protectedReads.TryGetValue(c.CallId, out var protectedRead))
						ApplyProtectedReadMetadata(callData, protectedRead);
					result.Data.Add(callData);
				}

				await ApplyBigBoardSafeShellAsync(result.Data);
				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Dispatches a waiting call right now: a pending call, or a scheduled call that has not gone out yet. The call
		/// becomes active and everyone on its dispatch list is notified. Pass DispatchList to replace who it goes to.
		/// </summary>
		/// <param name="input">The call and, optionally, who to send it to</param>
		/// <param name="cancellationToken">The cancellation token</param>
		/// <returns>OK with the call id when it was dispatched; Bad Request when the call is not waiting or has nobody to send it to</returns>
		[HttpPut("DispatchCallNow")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsUpdate)]
		public async Task<ActionResult<DispatchCallNowResult>> DispatchCallNow([FromBody] DispatchCallNowInput input, CancellationToken cancellationToken)
		{
			var result = new DispatchCallNowResult();

			if (input == null || !ModelState.IsValid ||
				!int.TryParse(input.CallId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int callId))
				return BadRequest();

			if (!await CanViewOrEditCallAsync(callId, edit: true))
				return Unauthorized();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!_pendingCallsService.IsWaitingForDispatch(call))
				return BadRequest("This call is not waiting to be dispatched: it is not pending, or it has already been dispatched or closed.");

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			if (!string.IsNullOrWhiteSpace(input.DispatchList))
				await ReplaceDispatchListAsync(call, input.DispatchList);

			var outcome = await _pendingCallsService.DispatchNowAsync(call, UserId, cancellationToken);

			if (outcome == DispatchNowOutcome.NoRecipients)
				return BadRequest("Choose who to send this call to (personnel, groups, roles or units) before dispatching it.");

			if (outcome == DispatchNowOutcome.QueueFailed)
				return Problem("The dispatch could not be queued. The call is still waiting; try again.");

			if (outcome != DispatchNowOutcome.Dispatched)
				return BadRequest();

			result.Id = call.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Replaces a loaded call's personnel, group, role and unit dispatches with a "P:|G:|R:|U:" list ("0" = everyone),
		/// keeping only ids that belong to the department.
		/// </summary>
		private async Task ReplaceDispatchListAsync(Call call, string dispatchList)
		{
			call.Dispatches ??= new List<CallDispatch>();
			call.GroupDispatches ??= new List<CallDispatchGroup>();
			call.RoleDispatches ??= new List<CallDispatchRole>();
			call.UnitDispatches ??= new List<CallDispatchUnit>();

			var activeUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
			List<string> userIds;
			var groupIds = new List<int>();
			var roleIds = new List<int>();
			var unitIds = new List<int>();

			if (dispatchList.Trim() == "0")
			{
				userIds = activeUsers.Where(x => !x.IsDeleted && !x.IsDisabled.GetValueOrDefault()).Select(x => x.UserId).Distinct().ToList();
			}
			else
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);
				var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
				var entries = dispatchList.Split(char.Parse("|"));

				userIds = entries.Where(x => x.StartsWith("P:")).Select(y => y.Substring(2))
					.Where(id => activeUsers.Any(x => x.UserId == id && !x.IsDeleted && !x.IsDisabled.GetValueOrDefault())).Distinct().ToList();
				groupIds = DispatchListHelper.ResolveIds(entries, "G:",
						name => groups.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.DepartmentGroupId)
					.Where(id => groups.Any(x => x.DepartmentGroupId == id)).Distinct().ToList();
				roleIds = DispatchListHelper.ResolveIds(entries, "R:",
						name => roles.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.PersonnelRoleId)
					.Where(id => roles.Any(x => x.PersonnelRoleId == id)).Distinct().ToList();
				unitIds = DispatchListHelper.ResolveIds(entries, "U:",
						name => units.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.UnitId)
					.Where(id => units.Any(x => x.UnitId == id)).Distinct().ToList();
			}

			foreach (var item in call.Dispatches.Where(x => !userIds.Contains(x.UserId)).ToList())
				call.Dispatches.Remove(item);
			foreach (var id in userIds.Where(id => !call.Dispatches.Any(x => x.UserId == id)))
				call.Dispatches.Add(new CallDispatch { CallId = call.CallId, UserId = id });

			foreach (var item in call.GroupDispatches.Where(x => !groupIds.Contains(x.DepartmentGroupId)).ToList())
				call.GroupDispatches.Remove(item);
			foreach (var id in groupIds.Where(id => !call.GroupDispatches.Any(x => x.DepartmentGroupId == id)))
				call.GroupDispatches.Add(new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = id });

			foreach (var item in call.RoleDispatches.Where(x => !roleIds.Contains(x.RoleId)).ToList())
				call.RoleDispatches.Remove(item);
			foreach (var id in roleIds.Where(id => !call.RoleDispatches.Any(x => x.RoleId == id)))
				call.RoleDispatches.Add(new CallDispatchRole { CallId = call.CallId, RoleId = id });

			foreach (var item in call.UnitDispatches.Where(x => !unitIds.Contains(x.UnitId)).ToList())
				call.UnitDispatches.Remove(item);
			foreach (var id in unitIds.Where(id => !call.UnitDispatches.Any(x => x.UnitId == id)))
				call.UnitDispatches.Add(new CallDispatchUnit { CallId = call.CallId, UnitId = id });
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		[HttpGet("GetCallHistory")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CallHistoryResult>> GetCallHistory(int callId)
		{
			var result = new CallHistoryResult();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!IsSystemApiKeyRequest && !await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			// Attended protected read (plan 7.1): the history entries below embed note text —
			// decrypt with a valid grant or embed REDACTED, never an envelope.
			await _protectedCallReadService.ResolveForReadAsync(DepartmentId, call, ProtectedGrantToken, UserId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			result.Data.Add(new CallHistoryResultData()
			{
				Id = call.CallId.ToString(),
				TimestampUtc = call.LoggedOn,
				Timestamp = call.LoggedOn.TimeConverter(department),
				Type = 0,
				Info = $"Call created"
			});

			if (call.ClosedOn.HasValue)
			{
				result.Data.Add(new CallHistoryResultData()
				{
					Id = call.CallId.ToString(),
					TimestampUtc = call.ClosedOn.Value,
					Timestamp = call.ClosedOn.Value.TimeConverter(department),
					Type = 0,
					Info = $"Call closed"
				});
			}

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			// The call's units as dispatched, deleted ones included.
			var units = await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(call.DepartmentId);
			var unitStates = (await _unitsService.GetUnitStatesForCallAsync(call.DepartmentId, callId)).OrderBy(y => y.Timestamp).ThenBy(x => x.UnitId).ToList();
			var actionLogs = (await _actionLogsService.GetActionLogsForCallAsync(call.DepartmentId, callId)).OrderBy(y => y.Timestamp).ThenBy(x => x.UserId).ToList();
			var names = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(call.DepartmentId);

			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId);
			var defaultUnitStatuses = _customStateService.GetDefaultUnitStatuses();
			var defaultUserStatuses = _customStateService.GetDefaultPersonStatuses();

			// The positions each status was set at go out under See Personnel Locations / See Unit Locations, the rules the map applies.
			var locatablePeople = await GetLocatablePersonIdsAsync(actionLogs.Select(x => x.UserId));
			var locatableUnits = await GetLocatableUnitIdsAsync(unitStates.Select(x => x.UnitId));

			foreach (var actionLog in actionLogs)
			{
				var nameInfo = names.FirstOrDefault(x => x.UserId == actionLog.UserId);
				var state = ResolveStatusDetail(actionLog.ActionTypeId, defaultUserStatuses, customStates);
				var statusText = state?.ButtonText ?? (actionLog.ActionTypeId <= CallStatusLinkage.MaxBuiltInStatusId ? actionLog.GetActionText() : "Unknown");

				if (nameInfo != null)
				{
					result.Data.Add(new CallHistoryResultData()
					{
						Id = actionLog.ActionLogId.ToString(),
						TimestampUtc = actionLog.Timestamp,
						Timestamp = actionLog.Timestamp.TimeConverter(department),
						Type = 2,
						Info = $"{nameInfo.LastName},{nameInfo.FirstName} set status to {statusText} at {(locatablePeople.Contains(actionLog.UserId) ? actionLog.GeoLocationData : null)}{LinkSuffix(actionLog.DestinationSource)}",
						DestinationSource = actionLog.DestinationSource
					});
				}
			}

			foreach (var unitLog in unitStates)
			{
				var state = ResolveStatusDetail(unitLog.State, defaultUnitStatuses, customStates);
				var statusText = state?.ButtonText ?? (unitLog.State <= CallStatusLinkage.MaxBuiltInStatusId ? unitLog.GetStatusText() : "Unknown");
				var unitName = unitLog.Unit?.Name ?? units?.FirstOrDefault(x => x.UnitId == unitLog.UnitId)?.Name ?? "Unknown Unit";

				result.Data.Add(new CallHistoryResultData()
				{
					Id = unitLog.UnitStateId.ToString(),
					TimestampUtc = unitLog.Timestamp,
					Timestamp = unitLog.Timestamp.TimeConverter(department),
					Type = 3,
					Info = $"{unitName} set status to {statusText} at {(locatableUnits.Contains(unitLog.UnitId) ? unitLog.GeoLocationData : null)}{LinkSuffix(unitLog.DestinationSource)}",
					DestinationSource = unitLog.DestinationSource
				});
			}

			foreach (var dispatch in call.Dispatches)
			{
				var nameInfo = names.FirstOrDefault(x => x.UserId == dispatch.UserId);

				if (nameInfo != null)
				{
					result.Data.Add(new CallHistoryResultData()
					{
						TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
						Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
						Type = 0,
						Info = $"{nameInfo.LastName}, {nameInfo.FirstName} was dispatched to the call"
					});
				}
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var name = groups.FirstOrDefault(x => x.DepartmentGroupId == groupDispatch.DepartmentGroupId);
					if (name != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Group {name.Name} was dispatched to the call"
						});

					}
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var unit = units.FirstOrDefault(x => x.UnitId == unitDispatch.UnitId);
					if (unit != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Unit {unit.Name} was dispatched to the call"
						});
					}
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var role = roles.FirstOrDefault(x => x.PersonnelRoleId == roleDispatch.RoleId);
					if (role != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Role {role.Name} was dispatched to the call"
						});
					}
				}
			}

			if (call.CallNotes != null && call.CallNotes.Any())
			{
				foreach (var note in call.CallNotes)
				{
					var nameInfo = names.FirstOrDefault(x => x.UserId == note.UserId);

					if (nameInfo != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = note.Timestamp,
							Timestamp = note.Timestamp.TimeConverter(department),
							Type = 1,
							Info = $"{nameInfo.LastName}, {nameInfo.FirstName} added note '{note.Note}'"
						});
					}
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the calls for the department inclusive in the date range
		/// </summary>
		/// <param name="startDate">Start date as UTC to get calls for</param>
		/// <param name="endDate">End date as UTC to get calls for</param>
		/// <returns>Array of CallResult objects for each call in the department within the range</returns>
		[HttpGet("GetCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		[DepartmentApiKeyScope(DepartmentApiKeyScopes.CallsRead)]
		public async Task<ActionResult<ActiveCallsResult>> GetCalls(DateTime startDate, DateTime endDate)
		{
			// Missing query params bind to DateTime.MinValue (0001-01-01), which is below the SQL
			// Server datetime floor (1753-01-01) and throws SqlDateTime overflow at the repository.
			var sqlMinDate = new DateTime(1753, 1, 1);
			if (startDate < sqlMinDate || endDate < sqlMinDate)
				return BadRequest("startDate and endDate are required and must be valid dates (on or after 1753-01-01).");

			if (endDate < startDate)
				return BadRequest("endDate must be on or after startDate.");

			var result = new ActiveCallsResult();

			// Group-scoped dispatch (off by default) trims this to the caller's area and the calls they are on, as GetCall does.
			var calls = (await ScopeCallsAsync(await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, startDate, endDate)))
				.OrderByDescending(x => x.LoggedOn).ToList();
			var destinationPois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var destinationPoiLookup = destinationPois.ToDictionary(x => x.PoiId);

			if (calls != null && calls.Any())
			{
				var protectedReads = await ResolveProtectedReadsAsync(calls);

				foreach (var c in calls)
				{
					var callWithData = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
						{
							double lat, lng;
							if (double.TryParse(geo[0], out lat) && double.TryParse(geo[1], out lng))
							{
								address = await _geoLocationProvider.GetAddressFromLatLong(lat, lng);
							}
						}
					}
					else
						address = c.Address;

					destinationPoiLookup.TryGetValue(callWithData.DestinationPoiId.GetValueOrDefault(), out var destinationPoi);
					var callData = ConvertCall(callWithData, null, address, TimeZone, destinationPoi);
					if (protectedReads.TryGetValue(callWithData.CallId, out var protectedRead))
						ApplyProtectedReadMetadata(callData, protectedRead);
					result.Data.Add(callData);
				}

				await ApplyBigBoardSafeShellAsync(result.Data);
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// The subject identifiers for a response: only resolved plaintext is returned. An envelope or the REDACTED
		/// placeholder (no grant) comes back as null, with calls.subjectidentifiers in RedactedFields.
		/// </summary>
		private static Dictionary<string, string> ToSubjectIdentifiersResult(string stored)
		{
			if (string.IsNullOrWhiteSpace(stored) || stored == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(stored))
				return null;
			return CallSubjectIdentifiers.TryParse(stored, out var identifiers) && identifiers.Count > 0
				? new Dictionary<string, string>(identifiers, StringComparer.Ordinal)
				: null;
		}

		public static CallResultData ConvertCall(Call call, List<DispatchProtocol> protocol, string geoLocationAddress, string timeZone, Poi destinationPoi = null)
		{
			var callResult = new CallResultData();

			callResult.CallId = call.CallId.ToString();
			callResult.Priority = call.Priority;
			callResult.Name = StringHelpers.SanitizeHtmlInString(call.Name);

			// Calls created before run cards existed have AlarmLevel 0; normalise to 1 so clients can
			// treat the value as a level number rather than "0 means unknown".
			callResult.AlarmLevel = Math.Max(1, call.AlarmLevel);
			callResult.ActiveRunCardId = call.ActiveRunCardId;

			if (!String.IsNullOrWhiteSpace(call.NatureOfCall))
				callResult.Nature = StringHelpers.SanitizeHtmlInString(call.NatureOfCall);

			if (!String.IsNullOrWhiteSpace(call.Notes))
				callResult.Note = StringHelpers.SanitizeHtmlInString(call.Notes);

			if (call.CallNotes != null)
				callResult.NotesCount = call.CallNotes.Count();
			else
				callResult.NotesCount = 0;

			if (call.Attachments != null)
			{
				callResult.AudioCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
				callResult.ImgagesCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
				callResult.FileCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
			}
			else
			{
				callResult.AudioCount = 0;
				callResult.ImgagesCount = 0;
				callResult.FileCount = 0;
			}

			if (!String.IsNullOrWhiteSpace(geoLocationAddress))
				callResult.Address = geoLocationAddress;
			else
				callResult.Address = call.Address;

			callResult.DestinationPoiId = call.DestinationPoiId;

			if (destinationPoi != null)
			{
				callResult.DestinationName = PoiDisplayHelper.GetDisplayName(destinationPoi, destinationPoi.Type?.Name);
				callResult.DestinationAddress = destinationPoi.Address;
				callResult.DestinationTypeName = PoiDisplayHelper.GetTypeName(destinationPoi);
				callResult.DestinationPoiTypeId = destinationPoi.Type?.PoiTypeId;
				callResult.DestinationLatitude = destinationPoi.Latitude;
				callResult.DestinationLongitude = destinationPoi.Longitude;
			}

			callResult.Geolocation = call.GeoLocationData;
			callResult.LoggedOn = call.LoggedOn.TimeConverter(new Department() { TimeZone = timeZone });
			callResult.LoggedOnUtc = call.LoggedOn;
			callResult.State = call.State;
			callResult.Number = call.Number;

			if (call.DispatchOn.HasValue)
			{
				callResult.DispatchedOnUtc = call.DispatchOn.Value;
				callResult.DispatchedOn = call.DispatchOn.Value.TimeConverter(new Department() { TimeZone = timeZone });
			}

			callResult.What3Words = call.W3W;
			callResult.ContactName = call.ContactName;
			callResult.ContactInfo = call.ContactNumber;
			callResult.ReferenceId = call.ReferenceNumber;
			callResult.ExternalId = call.ExternalIdentifier;
			callResult.SubjectIdentifiers = ToSubjectIdentifiersResult(call.SubjectIdentifiers);
			callResult.Part2ConsentOnFile = call.Part2ConsentOnFile;
			callResult.IncidentId = call.IncidentNumber;
			callResult.Type = call.Type;

			callResult.CheckInTimersEnabled = call.CheckInTimersEnabled;

			callResult.Protocols = new List<CallProtocolResultData>();
			if (protocol != null && protocol.Any())
			{
				foreach (var callProtocol in protocol)
				{
					callResult.Protocols.Add(CallProtocolsController.ConvertProtocolData(callProtocol));
				}
			}

			return callResult;
		}

		/// <summary>Validates the requested contact links belong to the department; null when any does not, empty when none were requested.</summary>
		private async Task<List<CallContact>> BuildCallContactLinksAsync(int departmentId, string primaryContactId, List<string> additionalContactIds)
		{
			var links = new List<CallContact>();
			var seen = new HashSet<string>(StringComparer.Ordinal);

			async Task<bool> AddAsync(string contactId, int callContactType)
			{
				if (string.IsNullOrWhiteSpace(contactId) || !seen.Add(contactId))
					return true;

				var contact = await _contactsService.GetContactByIdAsync(contactId);
				if (contact == null || contact.IsDeleted || contact.DepartmentId != departmentId)
					return false;

				links.Add(new CallContact { DepartmentId = departmentId, ContactId = contact.ContactId, CallContactType = callContactType });
				return true;
			}

			if (!await AddAsync(primaryContactId, 0))
				return null;

			foreach (var contactId in additionalContactIds ?? new List<string>())
			{
				if (!await AddAsync(contactId, 1))
					return null;
			}

			return links;
		}

		/// <summary>
		/// Fills CallResultData.Contacts for the given calls (Contacts plan Phase A, decision 3a). Contact
		/// names honor the caller's protected-read grant: plaintext with a grant, REDACTED without.
		/// </summary>
		private async Task ApplyCallContactsAsync(int departmentId, IReadOnlyList<KeyValuePair<Call, CallResultData>> pairs)
		{
			var calls = pairs.Select(x => x.Key).Where(x => x?.Contacts != null && x.Contacts.Any()).ToList();
			if (!calls.Any())
				return;

			var summaries = await _contactsService.GetCallContactSummariesAsync(departmentId, calls) ?? new Dictionary<int, List<CallContactSummary>>();
			var contacts = summaries.Values.SelectMany(x => x).Select(x => x.Contact).GroupBy(x => x.ContactId).Select(g => g.First()).ToList();
			if (contacts.Any())
				await _protectedCallReadService.ResolveContactsForReadAsync(departmentId, contacts, ProtectedGrantToken, UserId);

			foreach (var pair in pairs)
			{
				if (pair.Key == null || pair.Value == null || !summaries.TryGetValue(pair.Key.CallId, out var list))
					continue;

				pair.Value.Contacts = list.Select(x => new CallContactResultData
				{
					ContactId = x.Contact.ContactId,
					Name = SafeContactName(x.Contact),
					ContactType = x.Contact.ContactType,
					CallContactType = x.CallContactType,
					HasPreplan = x.HasPreplan,
					AlertNoteCount = x.AlertNoteCount,
					HazardCount = x.HazardCount
				}).ToList();
			}
		}

		/// <summary>A redacted name part collapses the whole display name to the REDACTED placeholder.</summary>
		private static string SafeContactName(Contact contact)
		{
			if (contact == null)
				return null;

			if (contact.FirstName == ProtectedDataEnvelope.RedactionValue || contact.LastName == ProtectedDataEnvelope.RedactionValue ||
				contact.CompanyName == ProtectedDataEnvelope.RedactionValue)
				return ProtectedDataEnvelope.RedactionValue;

			return contact.GetName()?.Trim();
		}

		private static string FirstNonEmpty(params string[] values)
		{
			return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
		}

		/// <summary>
		/// Marks history text for statuses that reached the call without the sender naming it.
		/// </summary>
		private static string LinkSuffix(int? destinationSource)
		{
			if (CallStatusAttribution.IsInferred(destinationSource))
				return " (inferred)";

			if (CallStatusAttribution.IsAutoLinked(destinationSource))
				return " (auto-linked)";

			return string.Empty;
		}

		/// <summary>
		/// Resolves a raw unit state / personnel action type (built-in value or custom status detail id) to its
		/// status detail for labelling. Returns null when the status can't be resolved (e.g. a built-in value with
		/// no default button, or a custom detail from another department).
		/// </summary>
		private static CustomStateDetail ResolveStatusDetail(int rawStatus, List<CustomStateDetail> builtInStatuses, List<CustomState> customStates)
		{
			if (rawStatus <= CallStatusLinkage.MaxBuiltInStatusId)
				return builtInStatuses?.FirstOrDefault(x => x.CustomStateDetailId == rawStatus);

			return customStates?.Where(x => x?.Details != null).SelectMany(x => x.Details).FirstOrDefault(x => x != null && x.CustomStateDetailId == rawStatus);
		}

		/// <summary>
		/// Group-scoped dispatch for a person's call lists. A department API key acts for its whole department, as
		/// <see cref="CanViewOrEditCallAsync"/> does: its identity is the managing user, whose membership the key does not
		/// depend on, so filtering by that user could hide calls from a valid key.
		/// </summary>
		private async Task<List<Call>> ScopeCallsAsync(List<Call> calls)
		{
			if (IsDepartmentApiKeyRequest)
				return calls ?? new List<Call>();

			return await _dispatchScopeService.FilterCallsForUserAsync(DepartmentId, UserId, calls);
		}

		/// <summary>
		/// The per-user call check, except for a department API key: the key acts for its department, so the call only has
		/// to belong to it. (The user checks resolve the acting user's active department, which for the managing user need
		/// not be the key's department; the key's scope already limits what it may do.)
		/// </summary>
		private async Task<bool> CanViewOrEditCallAsync(int callId, bool edit)
		{
			if (IsDepartmentApiKeyRequest)
			{
				var call = await _callsService.GetCallByIdAsync(callId, false);
				return call != null && call.DepartmentId == DepartmentId;
			}

			return edit
				? await _authorizationService.CanUserEditCallAsync(UserId, callId)
				: await _authorizationService.CanUserViewCallAsync(UserId, callId);
		}

		private async Task<Poi> GetValidatedDestinationPoiAsync(int? destinationPoiId, int? departmentIdOverride = null)
		{
			if (!destinationPoiId.HasValue || destinationPoiId.Value <= 0)
				return null;

			var deptId = departmentIdOverride ?? DepartmentId;
			return await _mappingService.GetDestinationPOIByIdAsync(deptId, destinationPoiId.Value);
		}
	}
}
