using System;
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Web.Attributes;
using Resgrid.Framework;
using System.Globalization;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Links;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Helpers;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Units;

namespace Resgrid.Web.Areas.User.Controllers
{
	// Department links are scoped to the caller's department claim on every action. Creating, enabling, disabling
	// and deleting a link are department-admin actions (Department_Update, the claim the Index page checks before
	// showing those buttons), and every write is an antiforgery-validated POST.
	[Area("User")]
	[Authorize]
	[DepartmentLocalTime]
	public class LinksController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailService _emailService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserStateService _userStateService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ILimitsService _limitsService;
		private readonly IMappingService _mappingService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IStringLocalizer<Resgrid.Localization.Common> _localizer;
		private readonly ILinkedProtocolService _linkedProtocols;
		private readonly IMutualAidCountersignService _countersign;
		private readonly IRecordDeploymentPartnerService _deploymentPartners;
		private readonly ISignatureService _signatures;

		public LinksController(IDepartmentLinksService departmentLinksService, IDepartmentsService departmentsService, IEmailService emailService,
			ICallsService callsService, IUnitsService unitsService, IActionLogsService actionLogsService, IDepartmentGroupsService departmentGroupsService,
			IUserStateService userStateService, IPersonnelRolesService personnelRolesService, ILimitsService limitsService, IMappingService mappingService,
			IDepartmentSettingsService departmentSettingsService, IStringLocalizer<Resgrid.Localization.Common> localizer, ILinkedProtocolService linkedProtocols,
			IMutualAidCountersignService countersign, IRecordDeploymentPartnerService deploymentPartners, ISignatureService signatures)
		{
			_deploymentPartners = deploymentPartners;
			_signatures = signatures;
			_linkedProtocols = linkedProtocols;
			_countersign = countersign;
			_departmentLinksService = departmentLinksService;
			_departmentsService = departmentsService;
			_emailService = emailService;
			_callsService = callsService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_userStateService = userStateService;
			_personnelRolesService = personnelRolesService;
			_limitsService = limitsService;
			_mappingService = mappingService;
			_departmentSettingsService = departmentSettingsService;
			_localizer = localizer;
		}
		#endregion Private Members and Constructors

		public async Task<IActionResult> Index()
		{
			var model = new LinksIndexView();
			model.DepartmentId = DepartmentId;
			model.Links = await _departmentLinksService.GetAllLinksForDepartmentAsync(DepartmentId);
			model.CanCreateLinks = await _limitsService.CanDepartmentUseLinksAsync(DepartmentId);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (department != null)
				model.Code = department.LinkCode;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> New()
		{
			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> New(NewLinksView model, CancellationToken cancellationToken)
		{
			if (!await _limitsService.CanDepartmentUseLinksAsync(DepartmentId))
			{
				model.Message = $"Unable to a created a link while on the Free Plan.";
				return View(model);
			}
			
			var linkedDepartment = await _departmentLinksService.GetDepartmentByLinkCodeAsync(model.LinkCode);

			if (linkedDepartment == null)
			{
				model.Message = $"Unable to find a department with a link code of {model.LinkCode}, please check the code and try again.";
				return View(model);
			}

			if (linkedDepartment.DepartmentId == DepartmentId)
			{
				model.Message = $"Unable to a link to your own department.";
				return View(model);
			}

			if (!await _limitsService.CanDepartmentUseLinksAsync(linkedDepartment.DepartmentId))
			{
				model.Message = $"Unable to a link to a department on the Free Plan.";
				return View(model);
			}

			// Build the link from the fields the creator may choose: what IT shares and its color. The posted row id and
			// the linked department's share flags are never bound from the form — a posted id would overwrite another
			// department's link, and posted LinkedDepartmentShare* flags would grant the creator the other side's data.
			var posted = model.Link ?? new DepartmentLink();
			var link = new DepartmentLink
			{
				DepartmentId = DepartmentId,
				LinkedDepartmentId = linkedDepartment.DepartmentId,
				DepartmentColor = posted.DepartmentColor,
				DepartmentShareCalls = posted.DepartmentShareCalls,
				DepartmentShareUnits = posted.DepartmentShareUnits,
				DepartmentSharePersonnel = posted.DepartmentSharePersonnel,
				DepartmentShareOrders = posted.DepartmentShareOrders,
				DepartmentShareProtocols = posted.DepartmentShareProtocols,
				DepartmentShareTraining = posted.DepartmentShareTraining,
				LinkEnabled = false,
				LinkCreated = DateTime.UtcNow
			};

			await _departmentLinksService.SaveAsync(link, cancellationToken);
			await _emailService.SendNewDepartmentLinkMailAsync(link);

			return RedirectToAction("Index");
		}

		/// <summary>
		/// Accepting a link is an L2 signature by the accepting department's admin (round 10 A3, e-Sign §20 "Department Links"): the step-up the
		/// action required is this session's proof, and the signature binds to the link's parties and sharing as accepted. Refused, nothing changes.
		/// </summary>
		private async Task<bool> SignAcceptanceAsync(DepartmentLink link, CancellationToken cancellationToken)
		{
			var creator = (await _departmentsService.GetDepartmentByIdAsync(link.DepartmentId, false))?.Name ?? link.DepartmentId.ToString(CultureInfo.InvariantCulture);
			var hash = SignaturePolicy.HashArtifact(DepartmentLinkSharing.AcceptanceCanonical(link));
			string ip;
			try { ip = IpAddressHelper.GetRequestIP(Request, true); }
			catch (Exception) { ip = HttpContext?.Connection?.RemoteIpAddress?.ToString(); }
			var proof = SignatureProof.SessionOnly(UserId, ip, Request?.Headers["User-Agent"].ToString());
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			var signed = await _signatures.SignAsync(new SignatureRequest
			{
				DepartmentId = DepartmentId,
				Use = SignatureUse.DepartmentLinkAcceptance,
				SubjectId = link.DepartmentLinkId.ToString(CultureInfo.InvariantCulture),
				RevisionId = "link-" + link.DepartmentLinkId.ToString(CultureInfo.InvariantCulture),
				ArtifactSha256 = hash,
				StatementText = SignatureTexts.Get("StatementDepartmentLinkAcceptance", CultureInfo.CurrentUICulture.Name, creator, SignaturePolicy.ShortCode(hash))
			}, proof, cancellationToken);
			if (signed?.Succeeded == true)
				return true;
			Logging.LogError($"Department link {link.DepartmentLinkId} was not accepted: its signature was refused ({signed?.Outcome}).");
			return false;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> EnableLink(int linkId, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			// Only the linked department accepts a link. An unknown id answers the same as another department's link.
			if (!IsLinkedDepartment(link))
				return Unauthorized();
			if (!await SignAcceptanceAsync(link, cancellationToken))
				return RedirectToAction("Index");

			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			await _departmentLinksService.SaveAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DisableLink(int linkId, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			// Only the linked department turns its acceptance off; the creating department ends a link by deleting it.
			if (!IsLinkedDepartment(link))
				return Unauthorized();

			link.LinkEnabled = false;
			link.LinkAccepted = null;

			await _departmentLinksService.SaveAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Enable(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (!IsLinkedDepartment(link))
				return Unauthorized();

			var model = new NewLinksView();
			model.DepartmentId = DepartmentId;
			model.Link = link;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> Enable(NewLinksView model, CancellationToken cancellationToken)
		{
			if (model?.Link == null)
				return Unauthorized();

			var link = await _departmentLinksService.GetLinkByIdAsync(model.Link.DepartmentLinkId);

			if (!IsLinkedDepartment(link))
				return Unauthorized();
			if (!await SignAcceptanceAsync(link, cancellationToken))
				return RedirectToAction("Index");

			link.DepartmentColor = model.Link.DepartmentColor;
			link.LinkEnabled = true;
			link.LinkAccepted = DateTime.UtcNow;

			await _departmentLinksService.SaveAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> DeleteLink(int linkId, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			// Either side may end a link: the creating department revokes what it shares, the linked department drops it.
			if (link == null || (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId))
				return Unauthorized();

			await _departmentLinksService.DeleteLinkAsync(link, cancellationToken);

			return RedirectToAction("Index");
		}

		private bool IsLinkedDepartment(DepartmentLink link)
		{
			return link != null && link.LinkedDepartmentId == DepartmentId;
		}

		// Round 10 A3: each side sets what IT shares (calls, units, personnel, orders, protocols, training); it never changes what the other side
		// shares with it. A reduction rotates the share epochs and stops set subscriptions, sponsorships and countersignatures over the link.
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Sharing(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);
			if (!DepartmentLinkSharing.IsParty(link, DepartmentId))
				return Unauthorized();

			return View(new LinkSharingView
			{
				Link = link,
				IsCreator = link.DepartmentId == DepartmentId,
				OtherDepartmentName = link.DepartmentId == DepartmentId ? link.LinkedDepartment?.Name : link.Department?.Name,
				Calls = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Calls),
				Units = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Units),
				Personnel = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Personnel),
				Orders = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Orders),
				Protocols = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Protocols),
				Training = DepartmentLinkSharing.SideShares(link, DepartmentId, DepartmentLinkShareTypes.Training)
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Sharing(int linkId, bool calls, bool units, bool personnel, bool orders, bool protocols, bool training, CancellationToken cancellationToken)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);
			if (!DepartmentLinkSharing.SetSideShares(link, DepartmentId, calls, units, personnel, orders, protocols, training))
				return Unauthorized();

			await _departmentLinksService.SaveAsync(link, cancellationToken);
			return RedirectToAction("Index");
		}

		// Protocols P5 "Linked departments": the other side's published protocols, read-only, only over a link that shares Protocols toward us.
		[HttpGet]
		public async Task<IActionResult> Protocols(int linkId)
		{
			var list = await _linkedProtocols.GetSharedProtocolsAsync(DepartmentId, linkId);
			if (list == null)
				return Unauthorized();
			return View(list);
		}

		[HttpGet]
		public async Task<IActionResult> Protocol(int linkId, int id)
		{
			var page = await _linkedProtocols.GetSharedProtocolAsync(DepartmentId, linkId, id);
			if (page == null)
				return Unauthorized();
			return View(page);
		}

		// Mutual-aid agreements linked departments ask this department to countersign (department admins sign them on the signing page).
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<IActionResult> Agreements()
		{
			// Round 10 A3: the partner's queue over its links: agreements to countersign and deployment fills to accept (both L2).
			ViewData["Deployments"] = await _deploymentPartners.GetAwaitingAsync(DepartmentId) ?? new List<RecordDeploymentPartnerItem>();
			return View(await _countersign.GetAwaitingAsync(DepartmentId));
		}

		[HttpGet]
		public async Task<IActionResult> View(int linkId)
		{
			var model = new LinkDataView();
			model.Link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (!DepartmentLinkSharing.IsParty(model.Link, DepartmentId))
				return Unauthorized();

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> GetActiveCallsList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			List<CallListJson> callsJson = new List<CallListJson>();

			if (!DepartmentLinkSharing.IsParty(link, DepartmentId))
				return Unauthorized();

			var sharedDepartmentId = DepartmentLinkSharing.GetSharedDepartmentId(link, DepartmentId, DepartmentLinkShareTypes.Calls);

			if (sharedDepartmentId.HasValue)
			{
				var sourceDepartmentId = sharedDepartmentId.Value;
				var calls = (await _callsService.GetActiveCallsByDepartmentAsync(sourceDepartmentId)).OrderByDescending(x => x.LoggedOn);
				var department = await _departmentsService.GetDepartmentByIdAsync(sourceDepartmentId, false);
				// Read with the sharing department's pattern: these are its numbers, not the viewer's.
				var numberSort = CallNumberFormat.SortKeys(CallNumberFormat.EffectivePattern(await _departmentSettingsService.GetCallNumberingConfigAsync(sourceDepartmentId)));

				foreach (var call in calls)
				{
					var callJson = new CallListJson();
					callJson.CallId = call.CallId;
					callJson.Number = call.Number;
					callJson.NumberSort = numberSort(call.Number);
					// ADP: a linked department is a DIFFERENT tenant — its members can never hold a
					// grant for the sharing department, so a protected call name degrades to the
					// system-generated number rather than leaking ciphertext across the link.
					callJson.Name = ProtectedDataEnvelope.HasEnvelopePrefix(call.Name)
						? Resgrid.Services.ProtectedProjectionService.GenericDispatchText
						: call.Name;
					callJson.State = _callsService.CallStateToString((CallStates) call.State);
					callJson.StateColor = _callsService.CallStateToColor((CallStates) call.State);
					callJson.Timestamp = call.LoggedOn.TimeConverterToString(department);
					callJson.Priority = await _callsService.CallPriorityToStringAsync(call.Priority, sourceDepartmentId);
					callJson.Color = await _callsService.CallPriorityToColorAsync(call.Priority, sourceDepartmentId);

					// Always the other department's call: being an admin of your own department grants nothing here.
					callJson.CanDeleteCall = false;

					callsJson.Add(callJson);
				}
			}

			return Json(callsJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetUnitsList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (!DepartmentLinkSharing.IsParty(link, DepartmentId))
				return Unauthorized();

			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			// Only units the other side shares toward this department, over an enabled link.
			var sharedDepartmentId = DepartmentLinkSharing.GetSharedDepartmentId(link, DepartmentId, DepartmentLinkShareTypes.Units);
			if (!sharedDepartmentId.HasValue)
				return Json(unitsJson);

			var sourceDepartmentId = sharedDepartmentId.Value;
			var units = await _unitsService.GetUnitsForDepartmentAsync(sourceDepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(sourceDepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(sourceDepartmentId, false);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetPersonnelList(int linkId)
		{
			var link = await _departmentLinksService.GetLinkByIdAsync(linkId);

			if (!DepartmentLinkSharing.IsParty(link, DepartmentId))
				return Unauthorized();

			// Only personnel the other side shares toward this department, over an enabled link.
			var sharedDepartmentId = DepartmentLinkSharing.GetSharedDepartmentId(link, DepartmentId, DepartmentLinkShareTypes.Personnel);
			if (!sharedDepartmentId.HasValue)
				return Json(new List<Models.BigBoardX.PersonnelViewModel>());

			var sourceDepartmentId = sharedDepartmentId.Value;
			var department = await _departmentsService.GetDepartmentByIdAsync(sourceDepartmentId);
			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(sourceDepartmentId);
			var lastUserActionlogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(sourceDepartmentId);
			var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(sourceDepartmentId);

			var lastUserStates = await _userStateService.GetLatestStatesForDepartmentAsync(sourceDepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(sourceDepartmentId);

			var calls = await _callsService.GetActiveCallsByDepartmentAsync(sourceDepartmentId);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(sourceDepartmentId);
			var pois = await _mappingService.GetPOIsForDepartmentAsync(sourceDepartmentId);

			var names = new Dictionary<string, string>();

			var userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				var state = lastUserStates.FirstOrDefault(x => x.UserId == u.UserId);

				if (state != null)
					userStates.Add(state);
				else
					userStates.Add(await _userStateService.GetLastUserStateByUserIdAsync(u.UserId));

				var name = personnelNames.FirstOrDefault(x => x.UserId == u.UserId);
				if (name != null)
					names.Add(u.UserId, name.Name);
				else
					names.Add(u.UserId, await UserHelper.GetFullNameForUser(u.UserId));
			}

			var personnelViewModels = new List<Models.BigBoardX.PersonnelViewModel>();

			var sortedUngroupedUsers = from u in allUsers
										   // let mu = Membership.GetUser(u.UserId)
									   //let userGroup = await _departmentGroupsService.GetGroupForUserAsync(u.UserId, DepartmentId)
									   //let groupName = userGroup == null ? "" : userGroup.Name
									   //let roles = await _personnelRolesService.GetRolesForUserAsync(u.UserId, DepartmentId)
									   //let name = (ProfileBase.Create(mu.UserName, true)).GetPropertyValue("Name").ToString()
									   let name = names[u.UserId]
									   let weight = lastUserActionlogs.Where(x => x.UserId == u.UserId).FirstOrDefault().GetWeightForAction()
									   orderby weight, name ascending
									   select new
									   {
										   Name = name,
										   User = u,
										   //Group = userGroup,
										   Roles = new List<PersonnelRole>()
									   };



			foreach (var u in sortedUngroupedUsers)
			{
				//var mu = Membership.GetUser(u.User.UserId);
				var al = lastUserActionlogs.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				var us = userStates.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				// The listed people belong to the sharing department, so their roles and group are looked up there.
				u.Roles.AddRange(await _personnelRolesService.GetRolesForUserAsync(u.User.UserId, sourceDepartmentId));
				var group = await _departmentGroupsService.GetGroupForUserAsync(u.User.UserId, sourceDepartmentId);

				string callNumber = "";
				if ((al != null && al.ActionTypeId == (int)ActionTypes.RespondingToScene) || (al != null && al.DestinationType.ToDestinationEntityType() == DestinationEntityTypes.Call))
				{
					if (al.DestinationId.HasValue)
					{
						var call = calls.FirstOrDefault(x => x.CallId == al.DestinationId.Value);

						if (call != null)
							callNumber = call.Number;
					}
				}
				var respondingToDepartment = stations.Where(s => al != null && s.DepartmentGroupId == al.DestinationId).FirstOrDefault();
				var destinationType = al.GetEffectiveDestinationType();
				var destination = DestinationResolutionHelper.Resolve(al?.DestinationId, al?.DestinationType, null, calls, stations, pois);
				var personnelViewModel = await Models.BigBoardX.PersonnelViewModel.Create(u.Name, al, us, department, respondingToDepartment, group, u.Roles, callNumber, destination);

				personnelViewModels.Add(personnelViewModel);
			}

			return Json(personnelViewModels);
		}
	}
}
