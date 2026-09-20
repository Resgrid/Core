using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Inventories;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Phase C deployment core (Workforce &amp; Business Operations plan, C4). Status transitions happen only here,
	/// every lifecycle change publishes its Workflow trigger through the domain outbox (decision 22), the RMS
	/// external order is read through <see cref="IRecordDeploymentsService"/> and never written (decision 39), and
	/// inventory issuance is optional (absent module = free-text equipment rows). Callers authorize.
	/// </summary>
	public partial class DeploymentService : IDeploymentService
	{
		private readonly IDeploymentRepository _deployments;
		private readonly IDeploymentUnitRepository _units;
		private readonly IDeploymentPersonnelRepository _personnel;
		private readonly IDeploymentEquipmentRepository _equipment;
		private readonly IDeploymentAttachmentRepository _attachments;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUnitsService _unitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICertificationService _certificationService;
		private readonly IContactsService _contactsService;
		private readonly ICallsService _callsService;
		private readonly IRecordDeploymentsService _recordDeployments;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IEventAggregator _eventAggregator;
		private readonly IPdfProvider _pdfProvider;
		private readonly IUnitOfWork _unitOfWork;
		private readonly Lazy<IInventoryIssuanceService> _inventoryIssuance;
		private readonly Lazy<IProtectedWriteService> _protectedWrite;
		private readonly Lazy<IProtectedReadService> _protectedRead;

		public DeploymentService(IDeploymentRepository deployments, IDeploymentUnitRepository units, IDeploymentPersonnelRepository personnel,
			IDeploymentEquipmentRepository equipment, IDeploymentAttachmentRepository attachments,
			IDepartmentsService departmentsService, IUnitsService unitsService, IUserProfileService userProfileService,
			IPersonnelRolesService personnelRolesService, ICertificationService certificationService, IContactsService contactsService,
			ICallsService callsService, IRecordDeploymentsService recordDeployments, IDomainEventOutboxService outbox, IEventAggregator eventAggregator,
			IPdfProvider pdfProvider, IUnitOfWork unitOfWork,
			Lazy<IInventoryIssuanceService> inventoryIssuance = null, Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null)
		{
			_deployments = deployments;
			_units = units;
			_personnel = personnel;
			_equipment = equipment;
			_attachments = attachments;
			_departmentsService = departmentsService;
			_unitsService = unitsService;
			_userProfileService = userProfileService;
			_personnelRolesService = personnelRolesService;
			_certificationService = certificationService;
			_contactsService = contactsService;
			_callsService = callsService;
			_recordDeployments = recordDeployments;
			_outbox = outbox;
			_eventAggregator = eventAggregator;
			_pdfProvider = pdfProvider;
			_unitOfWork = unitOfWork;
			_inventoryIssuance = inventoryIssuance;
			_protectedWrite = protectedWrite;
			_protectedRead = protectedRead;
		}

		#region Reads

		public async Task<Deployment> GetDeploymentByIdAsync(string deploymentId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(deploymentId)) return null;
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || deployment.IsDeleted) return null;
			await LoadRosterAsync(deployment);
			return deployment;
		}

		public async Task<Deployment> GetDeploymentByCallIdAsync(int callId, int departmentId)
		{
			var deployment = await _deployments.GetByCallIdAsync(callId, departmentId);
			if (deployment == null) return null;
			await LoadRosterAsync(deployment);
			return deployment;
		}

		public async Task<Deployment> GetDeploymentByExternalOrderIdAsync(string rmsExternalOrderId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(rmsExternalOrderId)) return null;
			var deployment = await _deployments.GetByExternalOrderIdAsync(rmsExternalOrderId, departmentId);
			if (deployment == null) return null;
			await LoadRosterAsync(deployment);
			return deployment;
		}

		public async Task<List<Deployment>> GetDeploymentsForDepartmentAsync(int departmentId, bool openOnly, int skip = 0, int take = 100) =>
			(await _deployments.GetForDepartmentAsync(departmentId, openOnly, skip, take))?.ToList() ?? new List<Deployment>();

		public Task<int> CountDeploymentsForDepartmentAsync(int departmentId, bool openOnly) => _deployments.CountForDepartmentAsync(departmentId, openOnly);

		public async Task<List<Deployment>> GetDeploymentsForUserAsync(int departmentId, string userId, bool openOnly)
		{
			var rows = (await _personnel.GetForUserAsync(departmentId, userId))?.ToList() ?? new List<DeploymentPersonnel>();
			if (rows.Count == 0) return new List<Deployment>();
			var ids = rows.Select(r => r.DeploymentId).Distinct().ToList();
			var deployments = (await _deployments.GetByIdsAsync(departmentId, ids))?.ToList() ?? new List<Deployment>();
			return openOnly ? deployments.Where(d => d.IsOpen).ToList() : deployments;
		}

		public async Task<bool> IsRosteredAsync(string deploymentId, int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(deploymentId) || string.IsNullOrWhiteSpace(userId)) return false;
			var rows = await _personnel.GetByDeploymentAsync(deploymentId);
			return rows != null && rows.Any(r => r.DepartmentId == departmentId && r.UserId == userId);
		}

		private async Task LoadRosterAsync(Deployment deployment)
		{
			deployment.Units = (await _units.GetByDeploymentAsync(deployment.DeploymentId))?.ToList() ?? new List<DeploymentUnit>();
			deployment.Personnel = (await _personnel.GetByDeploymentAsync(deployment.DeploymentId))?.ToList() ?? new List<DeploymentPersonnel>();
			deployment.Equipment = (await _equipment.GetByDeploymentAsync(deployment.DeploymentId))?.ToList() ?? new List<DeploymentEquipment>();
			await DecorateNamesAsync(deployment);
		}

		/// <summary>Unit and member display names for the roster; a lookup failure leaves the name blank rather than failing the read.</summary>
		private async Task DecorateNamesAsync(Deployment deployment)
		{
			try
			{
				if (deployment.Units.Count > 0)
				{
					var units = (await _unitsService.GetUnitsForDepartmentUnlimitedAsync(deployment.DepartmentId))?.ToDictionary(u => u.UnitId, u => u.Name) ?? new Dictionary<int, string>();
					foreach (var unit in deployment.Units) unit.UnitName = units.TryGetValue(unit.UnitId, out var name) ? name : null;
				}
				if (deployment.Personnel.Count > 0)
				{
					var profiles = await _userProfileService.GetSelectedUserProfilesAsync(deployment.Personnel.Select(p => p.UserId).Distinct().ToList());
					var names = profiles?.Where(p => p != null).GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First().FullName.AsFirstNameLastName) ?? new Dictionary<string, string>();
					foreach (var person in deployment.Personnel) person.DisplayName = names.TryGetValue(person.UserId, out var name) ? name : null;
				}
			}
			catch (Exception ex) { Logging.LogException(ex, "Deployment roster names could not be resolved."); }
		}

		#endregion

		#region Deployment lifecycle

		public async Task<Deployment> SaveDeploymentAsync(Deployment deployment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (deployment == null) throw new ArgumentNullException(nameof(deployment));
			if (deployment.DepartmentId <= 0) throw new ArgumentException("DepartmentId is required.", nameof(deployment));
			if (string.IsNullOrWhiteSpace(deployment.Name)) throw new InvalidOperationException("deployments_name_required");
			if (!Enum.IsDefined(typeof(DeploymentFinanceModes), deployment.FinanceMode)) throw new InvalidOperationException("deployments_finance_mode_invalid");
			if (deployment.StartOn.HasValue && deployment.EndOn.HasValue && deployment.EndOn < deployment.StartOn) throw new InvalidOperationException("deployments_window_invalid");
			if (deployment.DiscountPercent.HasValue && (deployment.DiscountPercent < 0 || deployment.DiscountPercent > 100)) throw new InvalidOperationException("deployments_discount_invalid");

			if (!string.IsNullOrWhiteSpace(deployment.ContactId))
			{
				var contact = await _contactsService.GetContactByIdAsync(deployment.ContactId);
				if (contact == null || contact.DepartmentId != deployment.DepartmentId) throw new InvalidOperationException("deployments_contact_not_found");
			}
			if (deployment.CallId.HasValue)
			{
				var call = await _callsService.GetCallByIdAsync(deployment.CallId.Value);
				if (call == null || call.DepartmentId != deployment.DepartmentId) throw new InvalidOperationException("deployments_call_not_found");
				var linked = await _deployments.GetByCallIdAsync(deployment.CallId.Value, deployment.DepartmentId);
				if (linked != null && linked.DeploymentId != deployment.DeploymentId) throw new InvalidOperationException("deployments_call_already_linked");
			}

			var isNew = string.IsNullOrWhiteSpace(deployment.DeploymentId);
			Deployment existing = null;
			if (!isNew)
			{
				existing = await _deployments.GetByIdForDepartmentAsync(deployment.DeploymentId, deployment.DepartmentId);
				if (existing == null || existing.IsDeleted) throw new InvalidOperationException("deployments_not_found");
				// Status, provenance and audit columns never change through a save; they have their own paths.
				deployment.Status = existing.Status;
				deployment.StatusChangedOn = existing.StatusChangedOn;
				deployment.RmsExternalOrderId = existing.RmsExternalOrderId;
				deployment.BidId = existing.BidId;
				deployment.AddedOn = existing.AddedOn;
				deployment.AddedByUserId = existing.AddedByUserId;
				deployment.IsProtected = existing.IsProtected;
				deployment.ProtectedCatalogVersion = existing.ProtectedCatalogVersion;
				deployment.EditedOn = DateTime.UtcNow;
				deployment.EditedByUserId = userId;
			}
			else
			{
				deployment.DeploymentId = null;
				deployment.Status = (int)DeploymentStatuses.Planned;
				deployment.StatusChangedOn = null;
				deployment.AddedOn = DateTime.UtcNow;
				deployment.AddedByUserId = userId;
				deployment.EditedOn = null;
				deployment.EditedByUserId = null;
			}
			deployment.IsDeleted = false;
			deployment.Currency = string.IsNullOrWhiteSpace(deployment.Currency) ? null : deployment.Currency.Trim().ToUpperInvariant();

			var audit = NewAuditEvent(deployment.DepartmentId, userId, isNew ? AuditLogTypes.DeploymentCreated : AuditLogTypes.DeploymentUpdated, ipAddress, userAgent);
			audit.Before = existing == null ? null : Snapshot(existing);
			var saved = await _deployments.SaveOrUpdateAsync(deployment, cancellationToken);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);

			if (isNew) await PublishAsync(saved, WorkflowTriggerEventType.DeploymentCreated, cancellationToken: cancellationToken);
			await LoadRosterAsync(saved);
			return saved;
		}

		public async Task<Deployment> SetDeploymentStatusAsync(string deploymentId, int departmentId, DeploymentStatuses status, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("deployments_not_found");
			var oldStatus = (DeploymentStatuses)deployment.Status;
			if (oldStatus == status) { await LoadRosterAsync(deployment); return deployment; }
			if (!IsValidTransition(oldStatus, status)) throw new InvalidOperationException("deployments_status_transition_invalid");

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.DeploymentStatusChanged, ipAddress, userAgent);
			audit.Before = Snapshot(deployment);
			deployment.Status = (int)status;
			deployment.StatusChangedOn = DateTime.UtcNow;
			deployment.EditedOn = DateTime.UtcNow;
			deployment.EditedByUserId = userId;
			if (status == DeploymentStatuses.Active && !deployment.StartOn.HasValue) deployment.StartOn = DateTime.UtcNow;
			if ((status == DeploymentStatuses.Completed || status == DeploymentStatuses.Cancelled) && !deployment.EndOn.HasValue) deployment.EndOn = DateTime.UtcNow;
			var saved = await _deployments.SaveOrUpdateAsync(deployment, cancellationToken);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);

			await PublishAsync(saved, WorkflowTriggerEventType.DeploymentStatusChanged, oldStatus: (int)oldStatus, cancellationToken: cancellationToken);
			await LoadRosterAsync(saved);
			return saved;
		}

		/// <summary>Planned→Standby→Active→Demobilizing→Completed; Cancelled from any open state; a step may be skipped forward, never backward.</summary>
		public static bool IsValidTransition(DeploymentStatuses from, DeploymentStatuses to)
		{
			if (from is DeploymentStatuses.Completed or DeploymentStatuses.Cancelled) return false;
			if (to == DeploymentStatuses.Cancelled) return true;
			if (to == DeploymentStatuses.Planned) return false;
			return (int)to > (int)from;
		}

		public async Task<bool> DeleteDeploymentAsync(string deploymentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || deployment.IsDeleted) return false;
			if (deployment.Status is (int)DeploymentStatuses.Active or (int)DeploymentStatuses.Demobilizing) throw new InvalidOperationException("deployments_delete_open");

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.DeploymentUpdated, ipAddress, userAgent);
			audit.Before = Snapshot(deployment);
			deployment.IsDeleted = true;
			deployment.EditedOn = DateTime.UtcNow;
			deployment.EditedByUserId = userId;
			await _deployments.SaveOrUpdateAsync(deployment, cancellationToken);
			audit.After = Snapshot(deployment);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return true;
		}

		#endregion

		#region External orders (decision 39)

		public async Task<Deployment> CreateFromExternalOrderAsync(int departmentId, ExternalOrderDeploymentInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(input.RmsExternalOrderId)) throw new InvalidOperationException("deployments_external_order_required");
			var linked = await _deployments.GetByExternalOrderIdAsync(input.RmsExternalOrderId, departmentId);
			if (linked != null) throw new InvalidOperationException("deployments_external_order_already_linked");

			var aggregate = await _recordDeployments.GetAsync(departmentId, userId, input.RmsExternalOrderId);
			var order = aggregate?.Order;
			if (order == null || order.DepartmentId != departmentId || order.DeletedOn.HasValue) throw new InvalidOperationException("deployments_external_order_not_found");
			var fills = aggregate.Fills?.Where(f => !f.DeletedOn.HasValue).ToList() ?? new List<RmsExternalOrderFill>();
			var firstFill = fills.OrderBy(f => f.RequestNumber, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

			return await TransactionAsync(async () =>
			{
				var deployment = new Deployment
				{
					DepartmentId = departmentId,
					RmsExternalOrderId = order.RmsExternalOrderId,
					FinanceMode = (int)input.FinanceMode,
					ContactId = input.ContactId,
					Name = string.IsNullOrWhiteSpace(input.Name) ? (string.IsNullOrWhiteSpace(order.IncidentName) ? $"Order {order.OrderNumber}" : order.IncidentName) : input.Name.Trim(),
					IncidentNumber = order.IncidentNumber,
					ResourceOrderNumber = order.OrderNumber,
					RequestNumber = fills.Count == 1 ? firstFill?.RequestNumber : null,
					CostCode = order.CostCode ?? firstFill?.CostCode,
					PointOfHire = fills.Count == 1 ? firstFill?.PointOfHire : null,
					HostCountry = order.IncidentCountry,
					HostSubdivision = order.IncidentSubdivision,
					LocalTimeZoneId = order.TimeZoneId,
					Currency = order.CurrencyCode,
					MeasurementSystem = order.MeasurementSystem,
					StartOn = order.MobilizedOn ?? fills.Where(f => f.NeededOn.HasValue).Select(f => f.NeededOn).OrderBy(d => d).FirstOrDefault(),
					EndOn = order.ReleasedOn,
					Notes = input.Notes
				};

				if (input.CreateCall)
				{
					var call = new Call
					{
						DepartmentId = departmentId,
						ReportingUserId = userId,
						Name = deployment.Name,
						NatureOfCall = string.IsNullOrWhiteSpace(order.IncidentName) ? $"Mutual aid order {order.OrderNumber}" : $"Mutual aid: {order.IncidentName} (order {order.OrderNumber})",
						IncidentNumber = order.IncidentNumber,
						Priority = input.CallPriority,
						Type = input.CallType,
						LoggedOn = DateTime.UtcNow,
						State = (int)CallStates.Active,
						CallSource = (int)CallSources.User,
						Notes = string.IsNullOrWhiteSpace(order.RequestingAgency) ? null : $"Requesting agency: {order.RequestingAgency}"
					};
					if (!string.IsNullOrWhiteSpace(input.ContactId))
						call.Contacts = new List<CallContact> { new CallContact { DepartmentId = departmentId, ContactId = input.ContactId, CallContactType = 0 } };
					var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);
					deployment.CallId = savedCall.CallId;
				}

				var saved = await SaveDeploymentAsync(deployment, userId, ipAddress, userAgent, cancellationToken);

				if (input.PrefillRoster)
				{
					var seatable = fills.Where(f => f.Status is (int)RmsDeploymentFillStatus.Accepted or (int)RmsDeploymentFillStatus.Mobilized or (int)RmsDeploymentFillStatus.CheckedIn or (int)RmsDeploymentFillStatus.Assigned).ToList();
					var unitRows = new Dictionary<int, DeploymentUnit>();
					foreach (var unitId in seatable.Where(f => f.AssignedUnitId.HasValue).Select(f => f.AssignedUnitId.Value).Distinct())
					{
						try { var result = await AddUnitAsync(saved.DeploymentId, departmentId, unitId, null, null, userId, ipAddress, userAgent, cancellationToken); unitRows[unitId] = result.Unit; }
						catch (InvalidOperationException ex) { Logging.LogError($"External order {order.RmsExternalOrderId}: unit {unitId} not seated ({ex.Message})."); }
					}
					foreach (var fill in seatable.Where(f => !string.IsNullOrWhiteSpace(f.AssignedUserId)))
					{
						try
						{
							await AddPersonnelAsync(saved.DeploymentId, departmentId, new DeploymentPersonnelInput
							{
								UserId = fill.AssignedUserId,
								DeploymentUnitId = fill.AssignedUnitId.HasValue && unitRows.TryGetValue(fill.AssignedUnitId.Value, out var row) ? row.DeploymentUnitId : null,
								CertificationCode = fill.Position,
								RmsExternalOrderFillId = fill.RmsExternalOrderFillId,
								Force = true
							}, userId, ipAddress, userAgent, cancellationToken);
						}
						catch (InvalidOperationException ex) { Logging.LogError($"External order {order.RmsExternalOrderId}: fill {fill.RmsExternalOrderFillId} not seated ({ex.Message})."); }
					}
				}

				return await GetDeploymentByIdAsync(saved.DeploymentId, departmentId);
			}, cancellationToken);
		}

		public async Task<DeploymentExternalContext> GetExternalContextAsync(string deploymentId, int departmentId, string userId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || string.IsNullOrWhiteSpace(deployment.RmsExternalOrderId)) return null;
			var aggregate = await _recordDeployments.GetAsync(departmentId, userId, deployment.RmsExternalOrderId);
			if (aggregate?.Order == null) return null;
			return new DeploymentExternalContext { Order = aggregate.Order, Fills = aggregate.Fills?.Where(f => !f.DeletedOn.HasValue).ToList() ?? new List<RmsExternalOrderFill>() };
		}

		#endregion

		#region Roster

		public async Task<DeploymentRosterResult> AddUnitAsync(string deploymentId, int departmentId, int unitId, string callSign, string notes, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var deployment = await RequireOpenAsync(deploymentId, departmentId);
			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			if (unit == null || unit.DepartmentId != departmentId) throw new InvalidOperationException("deployments_unit_not_found");
			var current = (await _units.GetByDeploymentAsync(deploymentId))?.Where(u => u.IsActive).ToList() ?? new List<DeploymentUnit>();
			if (current.Any(u => u.UnitId == unitId)) throw new InvalidOperationException("deployments_unit_already_rostered");

			var result = new DeploymentRosterResult();
			result.Warnings.AddRange(await UnitConflictsAsync(deployment, new[] { unitId }));
			var row = new DeploymentUnit { DeploymentId = deploymentId, DepartmentId = departmentId, UnitId = unitId, CallSign = Trim(callSign), Notes = Trim(notes), AddedOn = DateTime.UtcNow };
			result.Unit = await _units.SaveOrUpdateAsync(row, cancellationToken);
			result.Unit.UnitName = unit.Name;

			Audit(departmentId, userId, AuditLogTypes.DeploymentRosterChanged, ipAddress, userAgent, null, result.Unit);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Unit, result.Unit.DeploymentUnitId, unit.Name, "Added"), cancellationToken: cancellationToken);
			return result;
		}

		public async Task<bool> RemoveUnitAsync(string deploymentUnitId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _units.GetByIdAsync(deploymentUnitId);
			if (row == null || row.DepartmentId != departmentId || !row.IsActive) return false;
			var deployment = await RequireOpenAsync(row.DeploymentId, departmentId);
			var before = Snapshot(row);
			row.RemovedOn = DateTime.UtcNow;
			await _units.SaveOrUpdateAsync(row, cancellationToken);
			// Seats on the unit stay rostered but lose the unit link.
			foreach (var person in (await _personnel.GetByDeploymentAsync(row.DeploymentId))?.Where(p => p.IsActive && p.DeploymentUnitId == deploymentUnitId) ?? Enumerable.Empty<DeploymentPersonnel>())
			{
				person.DeploymentUnitId = null;
				await _personnel.SaveOrUpdateAsync(person, cancellationToken);
			}
			Audit(departmentId, userId, AuditLogTypes.DeploymentRosterChanged, ipAddress, userAgent, before, row);
			var unit = await _unitsService.GetUnitByIdAsync(row.UnitId);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Unit, row.DeploymentUnitId, unit?.Name, "Removed"), cancellationToken: cancellationToken);
			return true;
		}

		public async Task<DeploymentRosterResult> AddPersonnelAsync(string deploymentId, int departmentId, DeploymentPersonnelInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(input.UserId)) throw new InvalidOperationException("deployments_user_required");
			var deployment = await RequireOpenAsync(deploymentId, departmentId);
			var profile = await _userProfileService.GetProfileByUserIdAsync(input.UserId);
			if (profile == null) throw new InvalidOperationException("deployments_user_not_found");
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var members = await _departmentsService.GetAllUsersForDepartmentAsync(departmentId);
			if (members == null || members.All(m => m.UserId != input.UserId)) throw new InvalidOperationException("deployments_user_not_in_department");

			var current = (await _personnel.GetByDeploymentAsync(deploymentId))?.Where(p => p.IsActive).ToList() ?? new List<DeploymentPersonnel>();
			if (current.Any(p => p.UserId == input.UserId)) throw new InvalidOperationException("deployments_user_already_rostered");

			DeploymentUnit seatUnit = null;
			if (!string.IsNullOrWhiteSpace(input.DeploymentUnitId))
			{
				seatUnit = (await _units.GetByDeploymentAsync(deploymentId))?.FirstOrDefault(u => u.DeploymentUnitId == input.DeploymentUnitId && u.IsActive);
				if (seatUnit == null) throw new InvalidOperationException("deployments_unit_not_found");
			}

			var result = new DeploymentRosterResult();
			result.Warnings.AddRange(await PersonnelConflictsAsync(deployment, new[] { input.UserId }));
			if (input.UnitRoleId.HasValue)
				result.Warnings.AddRange(await SeatWarningsAsync(departmentId, input.UserId, input.UnitRoleId.Value, seatUnit?.UnitId, deployment));
			if (result.HasBlockingWarnings && !input.Force) return result;

			var row = new DeploymentPersonnel
			{
				DeploymentId = deploymentId, DepartmentId = departmentId, UserId = input.UserId, DeploymentUnitId = seatUnit?.DeploymentUnitId, UnitRoleId = input.UnitRoleId,
				CertificationCode = Trim(input.CertificationCode), CallSign = Trim(input.CallSign), RmsExternalOrderFillId = Trim(input.RmsExternalOrderFillId), AddedOn = DateTime.UtcNow
			};
			result.Personnel = await _personnel.SaveOrUpdateAsync(row, cancellationToken);
			result.Personnel.DisplayName = profile.FullName.AsFirstNameLastName;
			foreach (var warning in result.Warnings) warning.Blocking = false;

			Audit(departmentId, userId, AuditLogTypes.DeploymentRosterChanged, ipAddress, userAgent, null, result.Personnel);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Personnel, result.Personnel.DeploymentPersonnelId, result.Personnel.DisplayName, "Added"), subjectUserId: input.UserId, cancellationToken: cancellationToken);
			return result;
		}

		public async Task<bool> RemovePersonnelAsync(string deploymentPersonnelId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _personnel.GetByIdAsync(deploymentPersonnelId);
			if (row == null || row.DepartmentId != departmentId || !row.IsActive) return false;
			var deployment = await RequireOpenAsync(row.DeploymentId, departmentId);
			var before = Snapshot(row);
			row.RemovedOn = DateTime.UtcNow;
			await _personnel.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DeploymentRosterChanged, ipAddress, userAgent, before, row);
			var profile = await _userProfileService.GetProfileByUserIdAsync(row.UserId);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Personnel, row.DeploymentPersonnelId, profile?.FullName.AsFirstNameLastName, "Removed"), subjectUserId: row.UserId, cancellationToken: cancellationToken);
			return true;
		}

		public async Task<DeploymentRosterResult> AddEquipmentAsync(string deploymentId, int departmentId, DeploymentEquipmentInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (string.IsNullOrWhiteSpace(input.FreeTextName) && string.IsNullOrWhiteSpace(input.InventoryAssetId) && string.IsNullOrWhiteSpace(input.InventoryItemId))
				throw new InvalidOperationException("deployments_equipment_required");
			var deployment = await RequireOpenAsync(deploymentId, departmentId);
			DeploymentUnit unit = null;
			if (!string.IsNullOrWhiteSpace(input.DeploymentUnitId))
			{
				unit = (await _units.GetByDeploymentAsync(deploymentId))?.FirstOrDefault(u => u.DeploymentUnitId == input.DeploymentUnitId && u.IsActive);
				if (unit == null) throw new InvalidOperationException("deployments_unit_not_found");
			}

			var result = new DeploymentRosterResult();
			var row = new DeploymentEquipment
			{
				DeploymentId = deploymentId, DepartmentId = departmentId, DeploymentUnitId = unit?.DeploymentUnitId, InventoryAssetId = Trim(input.InventoryAssetId), InventoryItemId = Trim(input.InventoryItemId),
				FreeTextName = Trim(input.FreeTextName), Notes = Trim(input.Notes), AddedOn = DateTime.UtcNow
			};

			// Inventory plan M1: an Issue transaction referencing the deployment when the module is present (plan C4; absent = free-text row).
			if (input.IssueFromInventory && _inventoryIssuance?.Value != null && (row.InventoryAssetId != null || row.InventoryItemId != null))
			{
				try
				{
					await _inventoryIssuance.Value.IssueAsync(new InventoryActor { DepartmentId = departmentId, UserId = userId }, new InventoryIssueInput
					{
						RequestId = Guid.NewGuid().ToString("N"), ItemId = row.InventoryItemId, AssetId = row.InventoryAssetId, FromLocationId = Trim(input.FromLocationId), Quantity = 1,
						UnitId = unit?.UnitId, ExpectedReturnOn = deployment.EndOn, ReferenceType = InventoryReferenceType.Deployment, ReferenceId = deploymentId, Note = $"Deployment {deployment.Name}"
					});
					row.IssuedOn = DateTime.UtcNow;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Deployment {deploymentId}: inventory issue failed; equipment row kept without an issuance.");
					result.Warnings.Add(new DeploymentRosterWarning { Code = "inventory_issue_failed", Detail = ex.Message });
				}
			}

			result.Equipment = await _equipment.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DeploymentEquipmentChanged, ipAddress, userAgent, null, result.Equipment);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Equipment, result.Equipment.DeploymentEquipmentId, row.FreeTextName ?? row.InventoryAssetId ?? row.InventoryItemId, "Added"), cancellationToken: cancellationToken);
			return result;
		}

		public async Task<bool> ReturnEquipmentAsync(string deploymentEquipmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _equipment.GetByIdAsync(deploymentEquipmentId);
			if (row == null || row.DepartmentId != departmentId || !row.IsActive) return false;
			var deployment = await _deployments.GetByIdForDepartmentAsync(row.DeploymentId, departmentId);
			if (deployment == null) return false;
			var before = Snapshot(row);
			row.ReturnedOn = DateTime.UtcNow;
			await _equipment.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DeploymentEquipmentChanged, ipAddress, userAgent, before, row);
			await PublishAsync(deployment, WorkflowTriggerEventType.DeploymentRosterChanged, subject: (DeploymentTimeSubjectTypes.Equipment, row.DeploymentEquipmentId, row.FreeTextName ?? row.InventoryAssetId ?? row.InventoryItemId, "Removed"), cancellationToken: cancellationToken);
			return true;
		}

		public async Task<List<DeploymentRosterWarning>> GetRosterWarningsAsync(string deploymentId, int departmentId, IEnumerable<string> userIds, IEnumerable<int> unitIds)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null) throw new InvalidOperationException("deployments_not_found");
			var warnings = new List<DeploymentRosterWarning>();
			warnings.AddRange(await PersonnelConflictsAsync(deployment, userIds?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList() ?? new List<string>()));
			warnings.AddRange(await UnitConflictsAsync(deployment, unitIds?.Distinct().ToList() ?? new List<int>()));
			return warnings;
		}

		public async Task<int> GetCrewSizeForUnitAsync(string deploymentUnitId, int departmentId)
		{
			var unit = await _units.GetByIdAsync(deploymentUnitId);
			if (unit == null || unit.DepartmentId != departmentId) return 0;
			return (await _personnel.GetByDeploymentAsync(unit.DeploymentId))?.Count(p => p.IsActive && p.DeploymentUnitId == deploymentUnitId) ?? 0;
		}

		private async Task<List<DeploymentRosterWarning>> PersonnelConflictsAsync(Deployment deployment, IReadOnlyCollection<string> userIds)
		{
			var warnings = new List<DeploymentRosterWarning>();
			if (userIds.Count == 0) return warnings;
			var (start, end) = Window(deployment);
			var overlaps = await _personnel.GetActiveAssignmentsForUsersAsync(deployment.DepartmentId, userIds, start, end, deployment.DeploymentId);
			foreach (var overlap in overlaps ?? Enumerable.Empty<DeploymentPersonnel>())
				warnings.Add(new DeploymentRosterWarning { Code = DeploymentRosterWarning.ScheduleConflict, SubjectId = overlap.UserId, Detail = overlap.DeploymentId });
			return warnings;
		}

		private async Task<List<DeploymentRosterWarning>> UnitConflictsAsync(Deployment deployment, IReadOnlyCollection<int> unitIds)
		{
			var warnings = new List<DeploymentRosterWarning>();
			if (unitIds.Count == 0) return warnings;
			var (start, end) = Window(deployment);
			var overlaps = await _units.GetActiveAssignmentsForUnitsAsync(deployment.DepartmentId, unitIds, start, end, deployment.DeploymentId);
			foreach (var overlap in overlaps ?? Enumerable.Empty<DeploymentUnit>())
				warnings.Add(new DeploymentRosterWarning { Code = DeploymentRosterWarning.ScheduleConflict, SubjectId = overlap.UnitId.ToString(), Detail = overlap.DeploymentId });
			return warnings;
		}

		/// <summary>Seat qualification chain (plan C6 step 3): UnitRole → PersonnelRoleRequired → the role's Phase D certification requirements.</summary>
		private async Task<List<DeploymentRosterWarning>> SeatWarningsAsync(int departmentId, string userId, int unitRoleId, int? unitId, Deployment deployment)
		{
			var warnings = new List<DeploymentRosterWarning>();
			UnitRole seat = null;
			try { seat = await _unitsService.GetRoleByIdAsync(unitRoleId); } catch (Exception ex) { Logging.LogException(ex, "Deployment seat role could not be read."); }
			if (seat == null || (unitId.HasValue && seat.UnitId != unitId.Value)) throw new InvalidOperationException("deployments_seat_not_found");
			if (!seat.PersonnelRoleId.HasValue) return warnings;

			var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
			var holds = roles != null && roles.Any(r => r.PersonnelRoleId == seat.PersonnelRoleId.Value);
			if (!holds)
				warnings.Add(new DeploymentRosterWarning { Code = DeploymentRosterWarning.RoleNotHeld, SubjectId = userId, Detail = seat.Name, Blocking = seat.PersonnelRoleRequired });

			try
			{
				var evaluation = await _certificationService.EvaluateUserForRoleAsync(departmentId, seat.PersonnelRoleId.Value, userId);
				if (evaluation != null)
				{
					foreach (var violation in evaluation.Violations)
						warnings.Add(new DeploymentRosterWarning { Code = DeploymentRosterWarning.CertificationMissing, SubjectId = userId, Detail = violation.TypeName ?? violation.TypeCode, Blocking = seat.PersonnelRoleRequired && violation.IsMandatory });
					var windowEnd = deployment.EndOn ?? (deployment.StartOn ?? DateTime.UtcNow).AddDays(deployment.MaxDays ?? 14);
					foreach (var outcome in evaluation.Outcomes.Where(o => o.Satisfied && o.ExpiresOn.HasValue && o.ExpiresOn.Value <= windowEnd))
						warnings.Add(new DeploymentRosterWarning { Code = DeploymentRosterWarning.CertificationExpiring, SubjectId = userId, Detail = $"{outcome.TypeName ?? outcome.TypeCode}|{outcome.ExpiresOn:yyyy-MM-dd}" });
				}
			}
			catch (Exception ex) { Logging.LogException(ex, "Deployment seat certification evaluation failed; seat allowed with no certification warnings."); }
			return warnings;
		}

		private static (DateTime Start, DateTime End) Window(Deployment deployment)
		{
			var start = deployment.StartOn ?? DateTime.UtcNow;
			var end = deployment.EndOn ?? start.AddDays(deployment.MaxDays ?? 14);
			return (start, end < start ? start : end);
		}

		private async Task<Deployment> RequireOpenAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("deployments_not_found");
			if (!deployment.IsOpen) throw new InvalidOperationException("deployments_closed");
			return deployment;
		}

		#endregion

		#region Attachments

		public async Task<List<DeploymentAttachment>> GetAttachmentsAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null) return new List<DeploymentAttachment>();
			var rows = (await _attachments.GetByDeploymentAsync(deploymentId))?.Where(a => a.DepartmentId == departmentId).ToList() ?? new List<DeploymentAttachment>();
			await ResolveReadAsync(rows, a => a.DeploymentAttachmentId.ToString(), DeploymentProtectedFields.Attachment, departmentId);
			return rows;
		}

		public async Task<DeploymentAttachment> GetAttachmentAsync(int deploymentAttachmentId, int departmentId, bool includeData)
		{
			var row = await _attachments.GetByIdWithDataAsync(deploymentAttachmentId);
			if (row == null || row.DepartmentId != departmentId || row.IsDeleted) return null;
			await ResolveReadAsync(new[] { row }, a => a.DeploymentAttachmentId.ToString(), DeploymentProtectedFields.Attachment, departmentId);
			if (includeData) await ResolveBinaryReadAsync(departmentId, DeploymentProtectedFields.AttachmentDataFieldId, row.DeploymentAttachmentId.ToString(), row.Data, bytes => row.Data = bytes);
			else row.Data = null;
			return row;
		}

		public async Task<DeploymentAttachment> SaveAttachmentAsync(DeploymentAttachment attachment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (attachment == null) throw new ArgumentNullException(nameof(attachment));
			if (attachment.Data == null || attachment.Data.Length == 0) throw new InvalidOperationException("deployments_attachment_empty");
			if (attachment.Data.Length > MaxAttachmentBytes) throw new InvalidOperationException("deployments_attachment_too_large");
			var deployment = await _deployments.GetByIdForDepartmentAsync(attachment.DeploymentId, attachment.DepartmentId);
			if (deployment == null || deployment.IsDeleted) throw new InvalidOperationException("deployments_not_found");
			if (!Enum.IsDefined(typeof(DeploymentAttachmentTypes), attachment.AttachmentType)) attachment.AttachmentType = (int)DeploymentAttachmentTypes.Other;

			attachment.DeploymentAttachmentId = 0;
			attachment.FileSize = attachment.Data.Length;
			attachment.Name = string.IsNullOrWhiteSpace(attachment.Name) ? attachment.FileName : attachment.Name.Trim();
			attachment.IsDeleted = false;
			attachment.AddedOn = DateTime.UtcNow;
			attachment.AddedByUserId = userId;
			var saved = await SaveProtectedAttachmentAsync(attachment, cancellationToken);
			Audit(attachment.DepartmentId, userId, AuditLogTypes.DeploymentAttachmentAdded, ipAddress, userAgent, null, WithoutBytes(saved));
			return WithoutBytes(saved);
		}

		public async Task<bool> DeleteAttachmentAsync(int deploymentAttachmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var row = await _attachments.GetByIdWithDataAsync(deploymentAttachmentId);
			if (row == null || row.DepartmentId != departmentId || row.IsDeleted) return false;
			row.IsDeleted = true;
			await _attachments.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DeploymentAttachmentRemoved, ipAddress, userAgent, null, WithoutBytes(row));
			return true;
		}

		public const int MaxAttachmentBytes = 30 * 1024 * 1024;

		internal static DeploymentAttachment WithoutBytes(DeploymentAttachment row)
		{
			var copy = row.CloneJson();
			copy.Data = null;
			return copy;
		}

		#endregion

		#region Events and audit

		/// <summary>Publishes a lifecycle trigger through the domain outbox. The payload is identifiers, status, dates and the roster subject; names are REDACTED on a protected row.</summary>
		private async Task PublishAsync(Deployment deployment, WorkflowTriggerEventType trigger, int? oldStatus = null,
			(DeploymentTimeSubjectTypes Type, string Id, string Name, string Action)? subject = null, string subjectUserId = null,
			DeploymentTimeReport report = null, DeploymentExpense expense = null, CancellationToken cancellationToken = default)
		{
			try
			{
				await _outbox.EnqueueAsync(deployment.DepartmentId, DeploymentWorkflowPayload.Producer, new DomainEventEnvelope
				{
					EventName = trigger.ToString(),
					AggregateType = "Deployment",
					AggregateId = deployment.DeploymentId,
					AggregateVersion = 0,
					Trigger = trigger,
					OccurredOn = DateTime.UtcNow,
					CorrelationId = report?.DeploymentTimeReportId ?? deployment.DeploymentId,
					Payload = new
					{
						deployment.DeploymentId, deployment.Name, deployment.Status, OldStatus = oldStatus, deployment.FinanceMode, deployment.CallId,
						deployment.IncidentNumber, deployment.ResourceOrderNumber, deployment.RequestNumber, deployment.CostCode, deployment.RmsExternalOrderId, deployment.ContactId,
						deployment.StartOn, deployment.EndOn,
						SubjectType = subject.HasValue ? (int?)subject.Value.Type : null,
						SubjectId = subject?.Id,
						SubjectName = subject.HasValue ? (deployment.IsProtected ? ProtectedDataEnvelope.RedactionValue : subject.Value.Name) : null,
						SubjectUserId = subjectUserId,
						RosterAction = subject?.Action,
						ReportNumber = report?.ReportNumber, ReportDate = report?.ReportDate, TimeReportId = report?.DeploymentTimeReportId,
						ExpenseType = expense?.ExpenseType, ExpenseAmount = expense?.Amount, ExpenseCurrency = expense?.Currency ?? deployment.Currency
					}
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				// A failed publish never undoes a committed change; it is logged and visible in the outbox health.
				Logging.LogException(ex, $"Deployment {deployment.DeploymentId} {trigger} could not be published.");
			}
		}

		internal Task PublishTimeReportAsync(Deployment deployment, WorkflowTriggerEventType trigger, DeploymentTimeReport report, CancellationToken cancellationToken) =>
			PublishAsync(deployment, trigger, report: report, cancellationToken: cancellationToken);

		internal Task PublishExpenseAsync(Deployment deployment, DeploymentExpense expense, CancellationToken cancellationToken) =>
			PublishAsync(deployment, WorkflowTriggerEventType.DeploymentExpenseAdded, expense: expense, cancellationToken: cancellationToken);

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		internal static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			switch (clone)
			{
				case Deployment deployment: deployment.Units = null; deployment.Personnel = null; deployment.Equipment = null; break;
				case DeploymentTimeReport report: report.Entries = null; break;
				case DeploymentAttachment attachment: attachment.Data = null; break;
			}
			return clone.CloneJsonToString();
		}

		internal static AuditEvent NewAuditEvent(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent) => new AuditEvent
		{
			DepartmentId = departmentId,
			UserId = userId,
			Type = type,
			Successful = true,
			IpAddress = ipAddress,
			UserAgent = userAgent,
			ServerName = Environment.MachineName
		};

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		/// <summary>Runs <paramref name="action"/> in the scope's transaction, joining one the caller already opened (tests pass no unit of work and run unwrapped).</summary>
		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null)
				return await action();
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var result = await action();
				_unitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		#endregion
	}
}
