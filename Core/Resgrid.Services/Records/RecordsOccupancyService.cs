using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The occupancy/property master and its transition from Contacts pre-plans (RMS plan section 4.3, RMS-5).
	/// <para>
	/// Ownership never flips silently: a department starts ContactsOwned, an administrator runs the crosswalk inventory
	/// (Reconciling), decides every candidate, and only then switches structure writes to RMS (RecordsOwned). From that
	/// moment the Contacts service refuses pre-plan writes and projects reads from here through
	/// <see cref="IContactPreplanOwnershipGate"/>, so dispatch, the apps and the v4 contract keep their shape.
	/// </para>
	/// </summary>
	public class RecordsOccupancyService : IRecordsOccupancyService, IContactPreplanOwnershipGate
	{
		/// <summary>Two sources closer than this describe the same structure for grouping purposes.</summary>
		public const double ProximityMeters = 50;
		public const int DefaultReviewMonths = 12;

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsOccupancyContactLinksRepository _links;
		private readonly IRmsOccupancyHazardsRepository _hazards;
		private readonly IRmsOccupancyCrosswalksRepository _crosswalks;
		private readonly IRmsOccupancyFieldProvenancesRepository _provenance;
		private readonly IRmsOccupancyOwnershipsRepository _ownerships;
		private readonly IRmsViolationsRepository _violations;
		private readonly IRmsHydrantsRepository _hydrants;
		private readonly IContactPreplanRepository _contactPreplans;
		private readonly IContactPreplanHazardRepository _contactHazards;
		private readonly IContactsRepository _contacts;
		private readonly IAddressRepository _addresses;
		private readonly IPoisRepository _pois;
		private readonly IProtectedReadService _protectedReads;
		private readonly IProtectedGrantContext _grant;
		private readonly IRecordsProtectionService _protection;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsOccupancyService(RecordsPreventionGate gate, IRmsOccupanciesRepository occupancies, IRmsOccupancyContactLinksRepository links,
			IRmsOccupancyHazardsRepository hazards, IRmsOccupancyCrosswalksRepository crosswalks, IRmsOccupancyFieldProvenancesRepository provenance,
			IRmsOccupancyOwnershipsRepository ownerships, IRmsViolationsRepository violations, IRmsHydrantsRepository hydrants,
			IContactPreplanRepository contactPreplans, IContactPreplanHazardRepository contactHazards, IContactsRepository contacts, IAddressRepository addresses,
			IPoisRepository pois, IProtectedReadService protectedReads, IProtectedGrantContext grant, IRecordsProtectionService protection, IUnitOfWork unitOfWork)
		{
			_gate = gate;
			_occupancies = occupancies;
			_links = links;
			_hazards = hazards;
			_crosswalks = crosswalks;
			_provenance = provenance;
			_ownerships = ownerships;
			_violations = violations;
			_hydrants = hydrants;
			_contactPreplans = contactPreplans;
			_contactHazards = contactHazards;
			_contacts = contacts;
			_addresses = addresses;
			_pois = pois;
			_protectedReads = protectedReads;
			_grant = grant;
			_protection = protection;
			_unitOfWork = unitOfWork;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);

		#region Master CRUD

		public async Task<List<RmsOccupancy>> ListAsync(int departmentId, string userId, RmsOccupancyQuery query)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireViewerAsync(departmentId, userId);
			var rows = (await _occupancies.QueryAsync(departmentId, query ?? new RmsOccupancyQuery()))?.ToList() ?? new List<RmsOccupancy>();
			await _protection.RevealOccupanciesAsync(departmentId, rows);
			return rows;
		}

		public async Task<int> CountAsync(int departmentId, string userId, RmsOccupancyQuery query)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireViewerAsync(departmentId, userId);
			return await _occupancies.CountAsync(departmentId, query ?? new RmsOccupancyQuery());
		}

		public async Task<OccupancyAggregate> GetAsync(int departmentId, string userId, string occupancyId)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireViewerAsync(departmentId, userId);
			var occupancy = await LiveAsync(departmentId, occupancyId);
			if (occupancy == null)
				return null;

			var aggregate = new OccupancyAggregate
			{
				Occupancy = occupancy,
				Hazards = (await _hazards.GetForOccupancyAsync(departmentId, occupancyId))?.ToList() ?? new List<RmsOccupancyHazard>(),
				ContactLinks = (await _links.GetForOccupancyAsync(departmentId, occupancyId))?.ToList() ?? new List<RmsOccupancyContactLink>(),
				Provenance = (await _provenance.GetForOccupancyAsync(departmentId, occupancyId))?.ToList() ?? new List<RmsOccupancyFieldProvenance>(),
				Crosswalks = (await _crosswalks.GetForOccupancyAsync(departmentId, occupancyId))?.ToList() ?? new List<RmsOccupancyCrosswalk>()
			};
			var open = await _violations.CountOpenByOccupancyAsync(departmentId, new[] { occupancyId });
			aggregate.OpenViolationCount = open != null && open.TryGetValue(occupancyId, out var count) ? count : 0;
			aggregate.Protection.Merge(await _protection.RevealOccupanciesAsync(departmentId, new[] { occupancy }));
			aggregate.Protection.Merge(await _protection.RevealOccupancyHazardsAsync(departmentId, aggregate.Hazards));
			return aggregate;
		}

		public async Task<RmsOccupancy> SaveAsync(int departmentId, string userId, RmsOccupancy input, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			input.Name = RecordsPreventionGate.Require(input.Name, 250, "An occupancy needs a name.");
			var now = DateTime.UtcNow;

			RmsOccupancy entity;
			RmsOccupancy existing = null;
			var isNew = string.IsNullOrWhiteSpace(input.RmsOccupancyId);
			if (isNew)
			{
				entity = new RmsOccupancy
				{
					RmsOccupancyId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
					OccupancyNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Occupancy, now, cancellationToken),
					Status = (int)RmsOccupancyStatus.Active, CreatedOn = now, CreatedByUserId = userId, RowVersion = 1
				};
			}
			else
			{
				existing = await LiveAsync(departmentId, input.RmsOccupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
				if (input.RowVersion != 0 && input.RowVersion != existing.RowVersion)
					throw new InvalidOperationException("The occupancy changed since it was loaded. Reload it before saving.");
				entity = existing;
			}

			var changed = CopyEditable(input, entity);
			entity.NormalizedAddress = AddressNormalizer.Normalize(string.Join(" ", new[] { entity.AddressText, entity.City, entity.StateProvince, entity.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))));
			entity.ModifiedOn = now;
			entity.ModifiedByUserId = userId;
			if (!isNew) entity.RowVersion = existing.RowVersion + 1;
			if (input.Status == (int)RmsOccupancyStatus.Vacant || input.Status == (int)RmsOccupancyStatus.Demolished || input.Status == (int)RmsOccupancyStatus.Active)
				entity.Status = input.Status;

			// Seal for storage, hand the caller its plaintext back afterwards.
			var plaintext = PlaintextSnapshot<RmsOccupancy>.Take(entity, RmsProtectedFields.Occupancies);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectOccupancyAsync(departmentId, entity, isNew ? null : existing, userId, cancellationToken);
				if (isNew) await _occupancies.InsertAsync(entity, cancellationToken, true);
				else await _occupancies.UpdateAsync(entity, cancellationToken, true);
				foreach (var field in changed)
					await _provenance.InsertAsync(new RmsOccupancyFieldProvenance
					{
						RmsOccupancyFieldProvenanceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = entity.RmsOccupancyId,
						FieldKey = field, SourceKind = 0, SourceId = null, CapturedOn = now, CapturedByUserId = userId, ReviewedOn = now, ReviewedByUserId = userId
					}, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, isNew ? "Occupancy created" : "Occupancy updated", entity.RmsOccupancyId, new { entity.OccupancyNumber, fields = changed }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			plaintext.Restore();
			return entity;
		}

		public async Task DeleteAsync(int departmentId, string userId, string occupancyId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var occupancy = await LiveAsync(departmentId, occupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
			var open = await _violations.CountOpenByOccupancyAsync(departmentId, new[] { occupancyId });
			if (open != null && open.TryGetValue(occupancyId, out var count) && count > 0)
				throw new InvalidOperationException("An occupancy with open violations cannot be removed; correct or waive them first.");
			occupancy.DeletedOn = DateTime.UtcNow; occupancy.ModifiedOn = occupancy.DeletedOn.Value; occupancy.ModifiedByUserId = userId; occupancy.RowVersion++;
			await _occupancies.UpdateAsync(occupancy, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Occupancy removed", occupancyId, new { occupancy.OccupancyNumber }, cancellationToken: cancellationToken);
		}

		public async Task<RmsOccupancy> MarkReviewedAsync(int departmentId, string userId, string occupancyId, int nextReviewMonths, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var occupancy = await LiveAsync(departmentId, occupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
			var now = DateTime.UtcNow;
			occupancy.LastReviewedOn = now; occupancy.ReviewedByUserId = userId;
			occupancy.NextReviewDue = now.AddMonths(nextReviewMonths <= 0 ? DefaultReviewMonths : Math.Min(nextReviewMonths, 120));
			occupancy.ModifiedOn = now; occupancy.ModifiedByUserId = userId; occupancy.RowVersion++;
			await _occupancies.UpdateAsync(occupancy, cancellationToken, true);
			foreach (var row in await _provenance.GetForOccupancyAsync(departmentId, occupancyId) ?? Enumerable.Empty<RmsOccupancyFieldProvenance>())
			{
				row.ReviewedOn = now; row.ReviewedByUserId = userId;
				await _provenance.UpdateAsync(row, cancellationToken, true);
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Occupancy reviewed", occupancyId, new { occupancy.NextReviewDue }, cancellationToken: cancellationToken);
			return occupancy;
		}

		/// <summary>Copies the editable columns from the posted row and reports which changed (for provenance).</summary>
		private static List<string> CopyEditable(RmsOccupancy from, RmsOccupancy to)
		{
			var changed = new List<string>();
			void Set<T>(string key, Func<RmsOccupancy, T> get, Action<RmsOccupancy, T> set)
			{
				var value = get(from);
				if (!EqualityComparer<T>.Default.Equals(get(to), value)) { set(to, value); changed.Add(key); }
			}
			string T(string s, int max) => RecordsPreventionGate.Trim(s, max);
			Set("Name", o => o.Name, (o, v) => o.Name = v);
			Set("AddressText", o => T(o.AddressText, 500), (o, v) => o.AddressText = v);
			Set("City", o => T(o.City, 120), (o, v) => o.City = v);
			Set("StateProvince", o => T(o.StateProvince, 120), (o, v) => o.StateProvince = v);
			Set("PostalCode", o => T(o.PostalCode, 20), (o, v) => o.PostalCode = v);
			Set("Country", o => T(o.Country, 80), (o, v) => o.Country = v);
			Set("Latitude", o => o.Latitude, (o, v) => o.Latitude = v);
			Set("Longitude", o => o.Longitude, (o, v) => o.Longitude = v);
			Set("ParcelId", o => T(o.ParcelId, 100), (o, v) => o.ParcelId = v);
			Set("ConstructionType", o => o.ConstructionType, (o, v) => o.ConstructionType = v);
			Set("RoofType", o => o.RoofType, (o, v) => o.RoofType = v);
			Set("OccupancyType", o => o.OccupancyType, (o, v) => o.OccupancyType = v);
			Set("Stories", o => o.Stories, (o, v) => o.Stories = v);
			Set("YearBuilt", o => o.YearBuilt, (o, v) => o.YearBuilt = v);
			Set("SquareFeet", o => o.SquareFeet, (o, v) => o.SquareFeet = v);
			Set("OccupantLoad", o => o.OccupantLoad, (o, v) => o.OccupantLoad = v);
			Set("OccupancyHours", o => T(o.OccupancyHours, 250), (o, v) => o.OccupancyHours = v);
			Set("HasOccupantsNeedingAssistance", o => o.HasOccupantsNeedingAssistance, (o, v) => o.HasOccupantsNeedingAssistance = v);
			Set("OccupantsNeedingAssistanceNotes", o => T(o.OccupantsNeedingAssistanceNotes, 4000), (o, v) => o.OccupantsNeedingAssistanceNotes = v);
			Set("SprinklerType", o => o.SprinklerType, (o, v) => o.SprinklerType = v);
			Set("HasStandpipe", o => o.HasStandpipe, (o, v) => o.HasStandpipe = v);
			Set("HasFireAlarm", o => o.HasFireAlarm, (o, v) => o.HasFireAlarm = v);
			Set("FdcLocation", o => T(o.FdcLocation, 250), (o, v) => o.FdcLocation = v);
			Set("GasShutoffLocation", o => T(o.GasShutoffLocation, 500), (o, v) => o.GasShutoffLocation = v);
			Set("ElectricShutoffLocation", o => T(o.ElectricShutoffLocation, 500), (o, v) => o.ElectricShutoffLocation = v);
			Set("WaterShutoffLocation", o => T(o.WaterShutoffLocation, 500), (o, v) => o.WaterShutoffLocation = v);
			Set("UtilityNotes", o => T(o.UtilityNotes, 4000), (o, v) => o.UtilityNotes = v);
			Set("KnoxBoxLocation", o => T(o.KnoxBoxLocation, 500), (o, v) => o.KnoxBoxLocation = v);
			Set("GateCode", o => T(o.GateCode, 100), (o, v) => o.GateCode = v);
			Set("AlarmPanelLocation", o => T(o.AlarmPanelLocation, 500), (o, v) => o.AlarmPanelLocation = v);
			Set("AlarmCompany", o => T(o.AlarmCompany, 250), (o, v) => o.AlarmCompany = v);
			Set("AlarmCompanyPhone", o => T(o.AlarmCompanyPhone, 50), (o, v) => o.AlarmCompanyPhone = v);
			Set("AccessNotes", o => T(o.AccessNotes, 4000), (o, v) => o.AccessNotes = v);
			Set("NearestHydrantId", o => T(o.NearestHydrantId, 36), (o, v) => o.NearestHydrantId = v);
			Set("RequiredFireFlowGpm", o => o.RequiredFireFlowGpm, (o, v) => o.RequiredFireFlowGpm = v);
			Set("WaterSupplyNotes", o => T(o.WaterSupplyNotes, 4000), (o, v) => o.WaterSupplyNotes = v);
			Set("EmergencyContactName", o => T(o.EmergencyContactName, 250), (o, v) => o.EmergencyContactName = v);
			Set("EmergencyContactPhone", o => T(o.EmergencyContactPhone, 50), (o, v) => o.EmergencyContactPhone = v);
			Set("HazmatOnSite", o => o.HazmatOnSite, (o, v) => o.HazmatOnSite = v);
			Set("GeneralHazardNotes", o => T(o.GeneralHazardNotes, 4000), (o, v) => o.GeneralHazardNotes = v);
			Set("TacticalSummary", o => T(o.TacticalSummary, 4000), (o, v) => o.TacticalSummary = v);
			Set("PoiId", o => o.PoiId, (o, v) => o.PoiId = v);
			Set("NextReviewDue", o => o.NextReviewDue, (o, v) => o.NextReviewDue = v);
			return changed;
		}

		private async Task<RmsOccupancy> LiveAsync(int departmentId, string occupancyId)
		{
			if (string.IsNullOrWhiteSpace(occupancyId)) return null;
			var occupancy = await _occupancies.GetByIdForDepartmentAsync(departmentId, occupancyId);
			return occupancy == null || occupancy.DeletedOn != null ? null : occupancy;
		}

		#endregion

		#region Hazards and contact links

		public async Task<RmsOccupancyHazard> SaveHazardAsync(int departmentId, string userId, RmsOccupancyHazard input, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var occupancy = await LiveAsync(departmentId, input.RmsOccupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
			var now = DateTime.UtcNow;
			RmsOccupancyHazard entity, existing = null;
			if (string.IsNullOrWhiteSpace(input.RmsOccupancyHazardId))
				entity = new RmsOccupancyHazard { RmsOccupancyHazardId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancy.RmsOccupancyId, CreatedOn = now, CreatedByUserId = userId, RowVersion = 1 };
			else
			{
				existing = await _hazards.GetByIdForDepartmentAsync(departmentId, input.RmsOccupancyHazardId);
				if (existing == null || existing.DeletedOn != null || existing.RmsOccupancyId != occupancy.RmsOccupancyId) throw new ArgumentException("The hazard does not exist.");
				entity = existing;
				entity.RowVersion++;
			}
			entity.Title = RecordsPreventionGate.Require(input.Title, 200, "A hazard needs a title.");
			entity.HazardType = input.HazardType; entity.Severity = Math.Clamp(input.Severity, 1, 4);
			entity.Description = RecordsPreventionGate.Trim(input.Description, 4000);
			entity.LocationDescription = RecordsPreventionGate.Trim(input.LocationDescription, 1000);
			entity.GpsCoordinates = RecordsPreventionGate.Trim(input.GpsCoordinates, 100);
			entity.ShouldAlert = input.ShouldAlert; entity.ModifiedOn = now;
			var plaintext = PlaintextSnapshot<RmsOccupancyHazard>.Take(entity, RmsProtectedFields.OccupancyHazards);
			await _protection.ProtectOccupancyHazardAsync(departmentId, entity, existing, userId, cancellationToken);
			if (existing == null) await _hazards.InsertAsync(entity, cancellationToken, true); else await _hazards.UpdateAsync(entity, cancellationToken, true);
			plaintext.Restore();
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, existing == null ? "Occupancy hazard added" : "Occupancy hazard updated", occupancy.RmsOccupancyId, new { entity.RmsOccupancyHazardId, entity.Severity, entity.ShouldAlert }, cancellationToken: cancellationToken);
			return entity;
		}

		public async Task DeleteHazardAsync(int departmentId, string userId, string hazardId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var hazard = await _hazards.GetByIdForDepartmentAsync(departmentId, hazardId);
			if (hazard == null || hazard.DeletedOn != null) throw new ArgumentException("The hazard does not exist.");
			hazard.DeletedOn = DateTime.UtcNow; hazard.ModifiedOn = hazard.DeletedOn.Value; hazard.RowVersion++;
			await _hazards.UpdateAsync(hazard, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Occupancy hazard removed", hazard.RmsOccupancyId, new { hazardId }, cancellationToken: cancellationToken);
		}

		public async Task<RmsOccupancyContactLink> LinkContactAsync(int departmentId, string userId, string occupancyId, string contactId, RmsOccupancyContactRole role, bool isPrimary, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var occupancy = await LiveAsync(departmentId, occupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
			var contact = await _contacts.GetByIdAsync(contactId) as Contact;
			if (contact == null || contact.DepartmentId != departmentId || contact.IsDeleted) throw new ArgumentException("The contact does not exist in this department.");
			var links = (await _links.GetForOccupancyAsync(departmentId, occupancyId))?.ToList() ?? new List<RmsOccupancyContactLink>();
			var link = links.FirstOrDefault(l => l.ContactId == contactId && l.Role == (int)role);
			var now = DateTime.UtcNow;
			if (link == null)
			{
				link = new RmsOccupancyContactLink { RmsOccupancyContactLinkId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancyId, ContactId = contactId, Role = (int)role, IsPrimary = isPrimary, CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1 };
				await _links.InsertAsync(link, cancellationToken, true);
			}
			else { link.IsPrimary = isPrimary; link.ModifiedOn = now; link.RowVersion++; await _links.UpdateAsync(link, cancellationToken, true); }
			if (isPrimary)
				foreach (var other in links.Where(l => l.IsPrimary && l.RmsOccupancyContactLinkId != link.RmsOccupancyContactLinkId))
				{ other.IsPrimary = false; other.ModifiedOn = now; other.RowVersion++; await _links.UpdateAsync(other, cancellationToken, true); }
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Occupancy contact linked", occupancyId, new { contactId, role = role.ToString(), isPrimary }, cancellationToken: cancellationToken);
			return link;
		}

		public async Task UnlinkContactAsync(int departmentId, string userId, string linkId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var link = await _links.GetByIdForDepartmentAsync(departmentId, linkId);
			if (link == null || link.DeletedOn != null) throw new ArgumentException("The link does not exist.");
			link.DeletedOn = DateTime.UtcNow; link.ModifiedOn = link.DeletedOn.Value; link.RowVersion++;
			await _links.UpdateAsync(link, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Occupancy contact unlinked", link.RmsOccupancyId, new { link.ContactId, link.Role }, cancellationToken: cancellationToken);
		}

		#endregion

		#region Crosswalk

		private sealed class SourceCandidate
		{
			public RmsOccupancyCrosswalkSourceKind Kind;
			public string SourceId;
			public string ContactId;
			public string DisplayName;
			public string NormalizedAddress;
			public decimal? Latitude;
			public decimal? Longitude;
		}

		public async Task<OccupancyCrosswalkInventoryResult> InventoryCandidatesAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var result = new OccupancyCrosswalkInventoryResult();
			var now = DateTime.UtcNow;

			var sources = await CollectSourcesAsync(departmentId);
			result.SourcesScanned = sources.Count;
			var existingRows = (await _crosswalks.GetAllForDepartmentAsync(departmentId))?.ToList() ?? new List<RmsOccupancyCrosswalk>();
			var occupancies = (await _occupancies.GetAllLiveAsync(departmentId))?.ToList() ?? new List<RmsOccupancy>();

			// Group by folded address first, then by proximity for sources that only carry coordinates.
			var groups = new List<List<SourceCandidate>>();
			foreach (var source in sources)
			{
				List<SourceCandidate> group = null;
				if (source.NormalizedAddress != null)
					group = groups.FirstOrDefault(g => g.Any(s => s.NormalizedAddress != null && s.NormalizedAddress == source.NormalizedAddress));
				if (group == null && source.Latitude.HasValue && source.Longitude.HasValue)
					group = groups.FirstOrDefault(g => g.Any(s => s.Latitude.HasValue && s.Longitude.HasValue && Meters(s, source) <= ProximityMeters));
				if (group == null) { group = new List<SourceCandidate>(); groups.Add(group); }
				group.Add(source);
			}
			result.Groups = groups.Count;

			_unitOfWork.CreateOrGetConnection();
			try
			{
				var groupIndex = 0;
				foreach (var group in groups)
				{
					groupIndex++;
					var groupKey = $"g{groupIndex:0000}";
					foreach (var source in group)
					{
						var suggestion = Suggest(source, occupancies, out var confidence, out var reason);
						var row = existingRows.FirstOrDefault(r => r.SourceKind == (int)source.Kind && r.SourceId == source.SourceId);
						if (row != null && row.State != (int)RmsOccupancyCrosswalkState.Candidate) { result.AlreadyDecided++; continue; }
						var isNewRow = row == null;
						if (isNewRow)
						{
							row = new RmsOccupancyCrosswalk { RmsOccupancyCrosswalkId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), SourceKind = (int)source.Kind, SourceId = source.SourceId, State = (int)RmsOccupancyCrosswalkState.Candidate, CreatedOn = now, RowVersion = 1 };
							result.CandidatesCreated++;
						}
						else { result.CandidatesUpdated++; row.RowVersion++; }
						row.ContactId = source.ContactId; row.SourceDisplayName = RecordsPreventionGate.Trim(source.DisplayName, 250); row.NormalizedAddress = RecordsPreventionGate.Trim(source.NormalizedAddress, 500);
						row.Latitude = source.Latitude; row.Longitude = source.Longitude; row.MatchConfidence = confidence; row.MatchReason = reason; row.SuggestedOccupancyId = suggestion?.RmsOccupancyId;
						row.GroupKey = groupKey; row.InventoriedOn = now; row.ModifiedOn = now;
						if (isNewRow) await _crosswalks.InsertAsync(row, cancellationToken, true);
						else await _crosswalks.UpdateAsync(row, cancellationToken, true);
					}
				}

				var ownership = await _ownerships.GetForDepartmentAsync(departmentId) ?? new RmsOccupancyOwnership { RmsOccupancyOwnershipId = Guid.NewGuid().ToString(), DepartmentId = departmentId, State = (int)RmsOccupancyOwnershipState.ContactsOwned, CreatedOn = now, RowVersion = 0 };
				ownership.InventoriedOn = now;
				if (ownership.State == (int)RmsOccupancyOwnershipState.ContactsOwned) ownership.State = (int)RmsOccupancyOwnershipState.Reconciling;
				ownership.CandidateCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Candidate);
				ownership.LinkedCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Linked);
				ownership.RejectedCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Rejected);
				ownership.ModifiedOn = now;
				if (ownership.RowVersion == 0) { ownership.RowVersion = 1; await _ownerships.InsertAsync(ownership, cancellationToken, true); }
				else { ownership.RowVersion++; await _ownerships.UpdateAsync(ownership, cancellationToken, true); }
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Occupancy crosswalk inventoried", departmentId.ToString(), result, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			return result;
		}

		private static double Meters(SourceCandidate a, SourceCandidate b)
			=> GeoMath.HaversineMeters((double)a.Latitude.Value, (double)a.Longitude.Value, (double)b.Latitude.Value, (double)b.Longitude.Value);

		private static RmsOccupancy Suggest(SourceCandidate source, List<RmsOccupancy> occupancies, out int confidence, out string reason)
		{
			if (source.NormalizedAddress != null)
			{
				var byAddress = occupancies.FirstOrDefault(o => o.NormalizedAddress != null && o.NormalizedAddress == source.NormalizedAddress);
				if (byAddress != null) { confidence = 90; reason = "Same address"; return byAddress; }
			}
			if (source.Latitude.HasValue && source.Longitude.HasValue)
			{
				RmsOccupancy best = null; var bestMeters = double.MaxValue;
				foreach (var o in occupancies.Where(o => o.Latitude.HasValue && o.Longitude.HasValue))
				{
					var m = GeoMath.HaversineMeters((double)source.Latitude.Value, (double)source.Longitude.Value, (double)o.Latitude.Value, (double)o.Longitude.Value);
					if (m < bestMeters) { bestMeters = m; best = o; }
				}
				if (best != null && bestMeters <= 25) { confidence = 75; reason = $"Within {Math.Round(bestMeters)} m"; return best; }
				if (best != null && bestMeters <= ProximityMeters) { confidence = 60; reason = $"Within {Math.Round(bestMeters)} m"; return best; }
			}
			confidence = 0; reason = "No matching occupancy";
			return null;
		}

		private async Task<List<SourceCandidate>> CollectSourcesAsync(int departmentId)
		{
			var list = new List<SourceCandidate>();
			var contacts = ((await _contacts.GetAllByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<Contact>()).Where(c => !c.IsDeleted).ToList();
			var preplans = ((await _contactPreplans.GetPreplansByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<ContactPreplan>()).Where(p => !p.IsDeleted).ToList();
			var preplanContacts = new HashSet<string>(preplans.Select(p => p.ContactId), StringComparer.Ordinal);
			var addressCache = new Dictionary<int, string>();

			async Task<string> AddressOf(Contact c)
			{
				if (!c.PhysicalAddressId.HasValue) return null;
				if (addressCache.TryGetValue(c.PhysicalAddressId.Value, out var cached)) return cached;
				var address = await _addresses.GetByIdAsync(c.PhysicalAddressId.Value) as Address;
				var folded = address == null ? null : AddressNormalizer.Normalize(string.Join(" ", new[] { address.Address1, address.City, address.State, address.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))));
				addressCache[c.PhysicalAddressId.Value] = folded;
				return folded;
			}

			foreach (var preplan in preplans)
			{
				var contact = contacts.FirstOrDefault(c => c.ContactId == preplan.ContactId);
				var point = GeoMath.ParseLatLonString(contact?.LocationGpsCoordinates);
				list.Add(new SourceCandidate { Kind = RmsOccupancyCrosswalkSourceKind.ContactPreplan, SourceId = preplan.ContactPreplanId, ContactId = preplan.ContactId, DisplayName = contact?.Name ?? preplan.ContactId,
					NormalizedAddress = contact == null ? null : await AddressOf(contact), Latitude = point.HasValue ? (decimal?)(decimal)point.Value.Latitude : null, Longitude = point.HasValue ? (decimal?)(decimal)point.Value.Longitude : null });
			}
			foreach (var contact in contacts.Where(c => c.ContactType == 1 && !preplanContacts.Contains(c.ContactId)))
			{
				var point = GeoMath.ParseLatLonString(contact.LocationGpsCoordinates);
				var address = await AddressOf(contact);
				if (!point.HasValue && address == null) continue; // a company with no site is not a structure candidate
				list.Add(new SourceCandidate { Kind = RmsOccupancyCrosswalkSourceKind.Contact, SourceId = contact.ContactId, ContactId = contact.ContactId, DisplayName = contact.Name,
					NormalizedAddress = address, Latitude = point.HasValue ? (decimal?)(decimal)point.Value.Latitude : null, Longitude = point.HasValue ? (decimal?)(decimal)point.Value.Longitude : null });
			}
			foreach (var poi in (await _pois.GetAllByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<Poi>())
			{
				list.Add(new SourceCandidate { Kind = RmsOccupancyCrosswalkSourceKind.Poi, SourceId = poi.PoiId.ToString(), DisplayName = poi.Name, NormalizedAddress = AddressNormalizer.Normalize(poi.Address),
					Latitude = poi.Latitude == 0 && poi.Longitude == 0 ? (decimal?)null : (decimal)poi.Latitude, Longitude = poi.Latitude == 0 && poi.Longitude == 0 ? (decimal?)null : (decimal)poi.Longitude });
			}
			return list;
		}

		public async Task<List<RmsOccupancyCrosswalk>> GetCandidatesAsync(int departmentId, string userId, RmsOccupancyCrosswalkState state, int skip, int take)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			return (await _crosswalks.GetByStateAsync(departmentId, state, skip, take))?.ToList() ?? new List<RmsOccupancyCrosswalk>();
		}

		public async Task<RmsOccupancy> LinkCandidateAsync(int departmentId, string userId, string crosswalkId, string occupancyId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var candidate = await _crosswalks.GetByIdForDepartmentAsync(departmentId, crosswalkId) ?? throw new ArgumentException("The candidate does not exist.");
			if (candidate.State != (int)RmsOccupancyCrosswalkState.Candidate) throw new InvalidOperationException("The candidate was already decided.");
			var now = DateTime.UtcNow;

			RmsOccupancy occupancy;
			var created = false;
			var hazards = new List<RmsOccupancyHazard>();
			var provenanceRows = new List<RmsOccupancyFieldProvenance>();
			if (!string.IsNullOrWhiteSpace(occupancyId))
				occupancy = await LiveAsync(departmentId, occupancyId) ?? throw new ArgumentException("The occupancy does not exist.");
			else
			{
				var built = await BuildFromSourceAsync(departmentId, userId, candidate, now, cancellationToken);
				occupancy = built.Occupancy; hazards = built.Hazards; provenanceRows = built.Provenance; created = true;
			}

			_unitOfWork.CreateOrGetConnection();
			try
			{
				if (created)
				{
					var plaintext = PlaintextSnapshot<RmsOccupancy>.Take(occupancy, RmsProtectedFields.Occupancies);
					await _protection.ProtectOccupancyAsync(departmentId, occupancy, null, userId, cancellationToken);
					await _occupancies.InsertAsync(occupancy, cancellationToken, true);
					plaintext.Restore();
					foreach (var hazard in hazards)
					{
						var hp = PlaintextSnapshot<RmsOccupancyHazard>.Take(hazard, RmsProtectedFields.OccupancyHazards);
						await _protection.ProtectOccupancyHazardAsync(departmentId, hazard, null, userId, cancellationToken);
						await _hazards.InsertAsync(hazard, cancellationToken, true);
						hp.Restore();
					}
					foreach (var row in provenanceRows) await _provenance.InsertAsync(row, cancellationToken, true);
				}
				if (!string.IsNullOrWhiteSpace(candidate.ContactId))
				{
					var links = (await _links.GetForOccupancyAsync(departmentId, occupancy.RmsOccupancyId))?.ToList() ?? new List<RmsOccupancyContactLink>();
					if (!links.Any(l => l.ContactId == candidate.ContactId))
						await _links.InsertAsync(new RmsOccupancyContactLink { RmsOccupancyContactLinkId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancy.RmsOccupancyId, ContactId = candidate.ContactId, Role = (int)RmsOccupancyContactRole.Site, IsPrimary = !links.Any(l => l.IsPrimary), CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1 }, cancellationToken, true);
				}
				candidate.State = (int)RmsOccupancyCrosswalkState.Linked; candidate.RmsOccupancyId = occupancy.RmsOccupancyId; candidate.DecidedOn = now; candidate.DecidedByUserId = userId; candidate.ModifiedOn = now; candidate.RowVersion++;
				await _crosswalks.UpdateAsync(candidate, cancellationToken, true);
				await RefreshOwnershipCountsAsync(departmentId, now, cancellationToken);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, created ? "Crosswalk candidate linked to a new occupancy" : "Crosswalk candidate linked", occupancy.RmsOccupancyId, new { crosswalkId, candidate.SourceKind, candidate.SourceId }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			return occupancy;
		}

		/// <summary>
		/// Materializes an occupancy from a crosswalk source with field-level provenance. A protected pre-plan is read
		/// through the Contacts protected-read seam with the caller's grant; a concealed field is never copied as the
		/// REDACTED sentinel — the link is refused until the administrator steps up.
		/// </summary>
		private sealed class BuiltOccupancy
		{
			public RmsOccupancy Occupancy;
			public List<RmsOccupancyHazard> Hazards = new List<RmsOccupancyHazard>();
			public List<RmsOccupancyFieldProvenance> Provenance = new List<RmsOccupancyFieldProvenance>();
		}

		private async Task<BuiltOccupancy> BuildFromSourceAsync(int departmentId, string userId, RmsOccupancyCrosswalk candidate, DateTime now, CancellationToken cancellationToken)
		{
			var built = new BuiltOccupancy();
			var occupancy = new RmsOccupancy
			{
				RmsOccupancyId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				OccupancyNumber = await _gate.NextNumberAsync(departmentId, RmsPreventionNumberKinds.Occupancy, now, cancellationToken),
				Name = candidate.SourceDisplayName ?? "Occupancy", Status = (int)RmsOccupancyStatus.Active, Latitude = candidate.Latitude, Longitude = candidate.Longitude,
				NormalizedAddress = candidate.NormalizedAddress, CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, ModifiedByUserId = userId, RowVersion = 1
			};
			var kind = (RmsOccupancyCrosswalkSourceKind)candidate.SourceKind;
			var provenance = new List<RmsOccupancyFieldProvenance>();
			void Prov(string field) => provenance.Add(new RmsOccupancyFieldProvenance { RmsOccupancyFieldProvenanceId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancy.RmsOccupancyId, FieldKey = field, SourceKind = (int)kind, SourceId = candidate.SourceId, CapturedOn = now, CapturedByUserId = userId });
			Prov("Name");
			if (candidate.Latitude.HasValue) { Prov("Latitude"); Prov("Longitude"); }

			if (kind == RmsOccupancyCrosswalkSourceKind.ContactPreplan)
			{
				var preplan = await _contactPreplans.GetPreplanByContactIdAsync(candidate.ContactId, departmentId) ?? throw new InvalidOperationException("The source pre-plan no longer exists.");
				var read = await _protectedReads.ResolveContactPreplansForReadAsync(departmentId, new[] { preplan }, _grant.GrantToken, _grant.UserId ?? userId, cancellationToken);
				if (read.RedactedFields.Count > 0) throw new RecordProtectedContentException("step_up_required", "occupancy crosswalk");
				void Copy(string field, string value, Action<string> set) { if (!string.IsNullOrWhiteSpace(value)) { set(value); Prov(field); } }
				occupancy.ConstructionType = preplan.ConstructionType; occupancy.RoofType = preplan.RoofType; occupancy.OccupancyType = preplan.OccupancyType; Prov("ConstructionType"); Prov("RoofType"); Prov("OccupancyType");
				occupancy.OccupantLoad = preplan.OccupantLoad; occupancy.HasOccupantsNeedingAssistance = preplan.HasOccupantsNeedingAssistance; occupancy.HazmatOnSite = preplan.HazmatOnSite; occupancy.RequiredFireFlowGpm = preplan.RequiredFireFlowGpm;
				Copy("OccupancyHours", preplan.OccupancyHours, v => occupancy.OccupancyHours = v);
				Copy("OccupantsNeedingAssistanceNotes", preplan.OccupantsNeedingAssistanceNotes, v => occupancy.OccupantsNeedingAssistanceNotes = v);
				Copy("GasShutoffLocation", preplan.GasShutoffLocation, v => occupancy.GasShutoffLocation = v);
				Copy("ElectricShutoffLocation", preplan.ElectricShutoffLocation, v => occupancy.ElectricShutoffLocation = v);
				Copy("WaterShutoffLocation", preplan.WaterShutoffLocation, v => occupancy.WaterShutoffLocation = v);
				Copy("UtilityNotes", preplan.UtilityNotes, v => occupancy.UtilityNotes = v);
				Copy("KnoxBoxLocation", preplan.KnoxBoxLocation, v => occupancy.KnoxBoxLocation = v);
				Copy("GateCode", preplan.GateCode, v => occupancy.GateCode = v);
				Copy("AlarmPanelLocation", preplan.AlarmPanelLocation, v => occupancy.AlarmPanelLocation = v);
				Copy("AlarmCompany", preplan.AlarmCompany, v => occupancy.AlarmCompany = v);
				Copy("AlarmCompanyPhone", preplan.AlarmCompanyPhone, v => occupancy.AlarmCompanyPhone = v);
				Copy("AccessNotes", preplan.AccessNotes, v => occupancy.AccessNotes = v);
				Copy("WaterSupplyNotes", preplan.WaterSupplyNotes, v => occupancy.WaterSupplyNotes = string.IsNullOrWhiteSpace(preplan.NearestHydrantLocation) ? v : $"Nearest hydrant: {preplan.NearestHydrantLocation}\n{v}");
				if (string.IsNullOrWhiteSpace(preplan.WaterSupplyNotes) && !string.IsNullOrWhiteSpace(preplan.NearestHydrantLocation)) { occupancy.WaterSupplyNotes = $"Nearest hydrant: {preplan.NearestHydrantLocation}"; Prov("WaterSupplyNotes"); }
				Copy("EmergencyContactName", preplan.EmergencyContactName, v => occupancy.EmergencyContactName = v);
				Copy("EmergencyContactPhone", preplan.EmergencyContactPhone, v => occupancy.EmergencyContactPhone = v);
				Copy("GeneralHazardNotes", preplan.GeneralHazardNotes, v => occupancy.GeneralHazardNotes = v);
				Copy("TacticalSummary", preplan.TacticalSummary, v => occupancy.TacticalSummary = v);
				occupancy.LastReviewedOn = preplan.LastReviewedOn; occupancy.ReviewedByUserId = preplan.ReviewedByUserId; occupancy.NextReviewDue = preplan.NextReviewDue;

				var contactHazards = ((await _contactHazards.GetHazardsByContactIdAsync(candidate.ContactId, departmentId)) ?? Enumerable.Empty<ContactPreplanHazard>()).Where(h => !h.IsDeleted).ToList();
				var hazardRead = await _protectedReads.ResolveContactPreplanHazardsForReadAsync(departmentId, contactHazards, _grant.GrantToken, _grant.UserId ?? userId, cancellationToken);
				if (hazardRead.RedactedFields.Count > 0) throw new RecordProtectedContentException("step_up_required", "occupancy crosswalk");
				built.Hazards = contactHazards.Select(h => new RmsOccupancyHazard
				{
					RmsOccupancyHazardId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsOccupancyId = occupancy.RmsOccupancyId,
					HazardType = h.HazardType, Severity = h.Severity, Title = h.Title, Description = h.Description, LocationDescription = h.LocationDescription, GpsCoordinates = h.GpsCoordinates, ShouldAlert = h.ShouldAlert,
					SourceContactPreplanHazardId = h.ContactPreplanHazardId, CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, RowVersion = 1
				}).ToList();
				if (built.Hazards.Count > 0) Prov("hazards");
			}
			else if (kind == RmsOccupancyCrosswalkSourceKind.Poi && int.TryParse(candidate.SourceId, out var poiId))
			{
				var poi = await _pois.GetByIdAsync(poiId) as Poi;
				if (poi != null) { occupancy.PoiId = poi.PoiId; occupancy.AddressText = RecordsPreventionGate.Trim(poi.Address, 500); if (occupancy.AddressText != null) Prov("AddressText"); }
			}
			else if (kind == RmsOccupancyCrosswalkSourceKind.Contact)
			{
				var contact = await _contacts.GetByIdAsync(candidate.ContactId) as Contact;
				if (contact?.PhysicalAddressId != null && await _addresses.GetByIdAsync(contact.PhysicalAddressId.Value) is Address address)
				{
					occupancy.AddressText = RecordsPreventionGate.Trim(address.Address1, 500); occupancy.City = RecordsPreventionGate.Trim(address.City, 120); occupancy.StateProvince = RecordsPreventionGate.Trim(address.State, 120); occupancy.PostalCode = RecordsPreventionGate.Trim(address.PostalCode, 20); occupancy.Country = RecordsPreventionGate.Trim(address.Country, 80);
					Prov("AddressText");
				}
			}
			built.Occupancy = occupancy;
			built.Provenance = provenance;
			return built;
		}

		public async Task RejectCandidateAsync(int departmentId, string userId, string crosswalkId, string reason, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			var candidate = await _crosswalks.GetByIdForDepartmentAsync(departmentId, crosswalkId) ?? throw new ArgumentException("The candidate does not exist.");
			if (candidate.State != (int)RmsOccupancyCrosswalkState.Candidate) throw new InvalidOperationException("The candidate was already decided.");
			var now = DateTime.UtcNow;
			candidate.State = (int)RmsOccupancyCrosswalkState.Rejected; candidate.MatchReason = RecordsPreventionGate.Trim(reason, 250) ?? candidate.MatchReason; candidate.DecidedOn = now; candidate.DecidedByUserId = userId; candidate.ModifiedOn = now; candidate.RowVersion++;
			await _crosswalks.UpdateAsync(candidate, cancellationToken, true);
			await RefreshOwnershipCountsAsync(departmentId, now, cancellationToken);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Crosswalk candidate rejected", departmentId.ToString(), new { crosswalkId, candidate.SourceKind, candidate.SourceId }, cancellationToken: cancellationToken);
		}

		public async Task<RmsOccupancy> MergeAsync(int departmentId, string userId, string sourceOccupancyId, string targetOccupancyId, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			if (sourceOccupancyId == targetOccupancyId) throw new ArgumentException("Choose two different occupancies.");
			var source = await LiveAsync(departmentId, sourceOccupancyId) ?? throw new ArgumentException("The source occupancy does not exist.");
			var target = await LiveAsync(departmentId, targetOccupancyId) ?? throw new ArgumentException("The target occupancy does not exist.");
			if (source.Status == (int)RmsOccupancyStatus.Merged || target.Status == (int)RmsOccupancyStatus.Merged) throw new InvalidOperationException("A merged occupancy cannot take part in another merge.");
			var now = DateTime.UtcNow;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				foreach (var h in (await _hazards.GetForOccupancyAsync(departmentId, sourceOccupancyId)) ?? Enumerable.Empty<RmsOccupancyHazard>()) { h.RmsOccupancyId = targetOccupancyId; h.ModifiedOn = now; h.RowVersion++; await _hazards.UpdateAsync(h, cancellationToken, true); }
				var targetLinks = (await _links.GetForOccupancyAsync(departmentId, targetOccupancyId))?.ToList() ?? new List<RmsOccupancyContactLink>();
				foreach (var l in (await _links.GetForOccupancyAsync(departmentId, sourceOccupancyId)) ?? Enumerable.Empty<RmsOccupancyContactLink>())
				{
					if (targetLinks.Any(t => t.ContactId == l.ContactId && t.Role == l.Role)) { l.DeletedOn = now; }
					else { l.RmsOccupancyId = targetOccupancyId; l.IsPrimary = l.IsPrimary && !targetLinks.Any(t => t.IsPrimary); }
					l.ModifiedOn = now; l.RowVersion++; await _links.UpdateAsync(l, cancellationToken, true);
				}
				foreach (var c in (await _crosswalks.GetForOccupancyAsync(departmentId, sourceOccupancyId)) ?? Enumerable.Empty<RmsOccupancyCrosswalk>()) { c.RmsOccupancyId = targetOccupancyId; c.ModifiedOn = now; c.RowVersion++; await _crosswalks.UpdateAsync(c, cancellationToken, true); }
				foreach (var p in (await _provenance.GetForOccupancyAsync(departmentId, sourceOccupancyId)) ?? Enumerable.Empty<RmsOccupancyFieldProvenance>()) { p.RmsOccupancyId = targetOccupancyId; await _provenance.UpdateAsync(p, cancellationToken, true); }
				source.Status = (int)RmsOccupancyStatus.Merged; source.MergedIntoOccupancyId = targetOccupancyId; source.ModifiedOn = now; source.ModifiedByUserId = userId; source.RowVersion++;
				await _occupancies.UpdateAsync(source, cancellationToken, true);
				target.ModifiedOn = now; target.ModifiedByUserId = userId; target.RowVersion++;
				await _occupancies.UpdateAsync(target, cancellationToken, true);
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Occupancies merged", targetOccupancyId, new { sourceOccupancyId, targetOccupancyId }, cancellationToken: cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			return target;
		}

		private async Task RefreshOwnershipCountsAsync(int departmentId, DateTime now, CancellationToken cancellationToken)
		{
			var ownership = await _ownerships.GetForDepartmentAsync(departmentId);
			if (ownership == null) return;
			ownership.CandidateCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Candidate);
			ownership.LinkedCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Linked);
			ownership.RejectedCount = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Rejected);
			ownership.ModifiedOn = now; ownership.RowVersion++;
			await _ownerships.UpdateAsync(ownership, cancellationToken, true);
		}

		public async Task<OccupancyReconciliationStatus> GetReconciliationStatusAsync(int departmentId)
		{
			var ownership = await _ownerships.GetForDepartmentAsync(departmentId);
			var status = new OccupancyReconciliationStatus
			{
				State = ownership == null ? RmsOccupancyOwnershipState.ContactsOwned : (RmsOccupancyOwnershipState)ownership.State,
				InventoriedOn = ownership?.InventoriedOn, SwitchedOn = ownership?.SwitchedOn,
				Candidates = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Candidate),
				Linked = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Linked),
				Rejected = await _crosswalks.CountByStateAsync(departmentId, RmsOccupancyCrosswalkState.Rejected),
				Occupancies = await _occupancies.CountLiveAsync(departmentId)
			};
			var preplans = ((await _contactPreplans.GetPreplansByDepartmentIdAsync(departmentId)) ?? Enumerable.Empty<ContactPreplan>()).Where(p => !p.IsDeleted).ToList();
			if (preplans.Count > 0)
			{
				var decided = ((await _crosswalks.GetAllForDepartmentAsync(departmentId)) ?? Enumerable.Empty<RmsOccupancyCrosswalk>())
					.Where(c => c.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan && (c.State == (int)RmsOccupancyCrosswalkState.Linked || c.State == (int)RmsOccupancyCrosswalkState.Rejected || c.State == (int)RmsOccupancyCrosswalkState.Merged))
					.Select(c => c.SourceId).ToHashSet(StringComparer.Ordinal);
				status.UnreconciledPreplans = preplans.Count(p => !decided.Contains(p.ContactPreplanId));
			}
			return status;
		}

		public async Task<RmsOccupancyOwnership> SwitchWriteOwnershipAsync(int departmentId, string userId, string reason, CancellationToken cancellationToken = default)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.Occupancy);
			await _gate.RequireAdminAsync(departmentId, userId);
			reason = RecordsPreventionGate.Require(reason, 1000, "Record why structure ownership is moving to Records.");
			var status = await GetReconciliationStatusAsync(departmentId);
			if (status.State == RmsOccupancyOwnershipState.RecordsOwned) throw new InvalidOperationException("Records already owns structure writes for this department.");
			if (!status.CanSwitchToRecords)
				throw new InvalidOperationException($"Reconcile first: {status.Candidates} undecided candidate(s) and {status.UnreconciledPreplans} pre-plan(s) without a decision remain{(status.InventoriedOn.HasValue ? "" : "; run the inventory")}.");
			var now = DateTime.UtcNow;
			var ownership = await _ownerships.GetForDepartmentAsync(departmentId) ?? throw new InvalidOperationException("Run the inventory before switching ownership.");
			ownership.State = (int)RmsOccupancyOwnershipState.RecordsOwned; ownership.SwitchedOn = now; ownership.SwitchedByUserId = userId; ownership.Reason = reason; ownership.ModifiedOn = now; ownership.RowVersion++;
			await _ownerships.UpdateAsync(ownership, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "Structure write ownership switched to Records", departmentId.ToString(), new { reason, status.Linked, status.Rejected, status.Occupancies }, cancellationToken: cancellationToken);
			return ownership;
		}

		#endregion

		#region Dispatch projection and the Contacts gate

		public async Task<OccupancyDispatchProjectionV1> GetDispatchProjectionAsync(int departmentId, string occupancyId, CancellationToken cancellationToken = default)
		{
			var occupancy = await LiveAsync(departmentId, occupancyId);
			if (occupancy == null) return null;
			var projections = await BuildProjectionsAsync(departmentId, new List<RmsOccupancy> { occupancy }, cancellationToken);
			return projections.TryGetValue(occupancyId, out var projection) ? projection : null;
		}

		public async Task<OccupancyDispatchProjectionV1> GetDispatchProjectionForContactAsync(int departmentId, string contactId, CancellationToken cancellationToken = default)
		{
			var map = await GetDispatchProjectionsForContactsAsync(departmentId, new[] { contactId }, cancellationToken);
			return map.TryGetValue(contactId, out var projection) ? projection : null;
		}

		public async Task<Dictionary<string, OccupancyDispatchProjectionV1>> GetDispatchProjectionsForContactsAsync(int departmentId, IEnumerable<string> contactIds, CancellationToken cancellationToken = default)
		{
			var result = new Dictionary<string, OccupancyDispatchProjectionV1>(StringComparer.Ordinal);
			var ids = (contactIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
			if (ids.Count == 0) return result;
			var byContact = await OccupancyIdsForContactsAsync(departmentId, ids);
			if (byContact.Count == 0) return result;
			var occupancies = ((await _occupancies.GetByIdsAsync(departmentId, byContact.Values.Distinct())) ?? Enumerable.Empty<RmsOccupancy>()).Where(o => o.DeletedOn == null).ToList();
			var projections = await BuildProjectionsAsync(departmentId, occupancies, cancellationToken);
			foreach (var pair in byContact)
				if (projections.TryGetValue(pair.Value, out var projection)) result[pair.Key] = projection;
			return result;
		}

		private async Task<Dictionary<string, string>> OccupancyIdsForContactsAsync(int departmentId, List<string> contactIds)
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var link in (await _links.GetForContactsAsync(departmentId, contactIds)) ?? Enumerable.Empty<RmsOccupancyContactLink>())
				if (link.Role == (int)RmsOccupancyContactRole.Site && !map.ContainsKey(link.ContactId)) map[link.ContactId] = link.RmsOccupancyId;
			foreach (var cw in (await _crosswalks.GetForContactsAsync(departmentId, contactIds)) ?? Enumerable.Empty<RmsOccupancyCrosswalk>())
				if (cw.State == (int)RmsOccupancyCrosswalkState.Linked && cw.RmsOccupancyId != null && cw.ContactId != null && !map.ContainsKey(cw.ContactId)) map[cw.ContactId] = cw.RmsOccupancyId;
			// Follow merges so a contact linked to a merged-away occupancy projects from the survivor.
			var live = ((await _occupancies.GetByIdsAsync(departmentId, map.Values.Distinct())) ?? Enumerable.Empty<RmsOccupancy>()).ToDictionary(o => o.RmsOccupancyId);
			foreach (var key in map.Keys.ToList())
				if (live.TryGetValue(map[key], out var o) && o.Status == (int)RmsOccupancyStatus.Merged && !string.IsNullOrWhiteSpace(o.MergedIntoOccupancyId)) map[key] = o.MergedIntoOccupancyId;
			return map;
		}

		private async Task<Dictionary<string, OccupancyDispatchProjectionV1>> BuildProjectionsAsync(int departmentId, List<RmsOccupancy> occupancies, CancellationToken cancellationToken)
		{
			var result = new Dictionary<string, OccupancyDispatchProjectionV1>(StringComparer.Ordinal);
			if (occupancies.Count == 0) return result;
			var ids = occupancies.Select(o => o.RmsOccupancyId).ToList();
			var hazards = ((await _hazards.GetForOccupanciesAsync(departmentId, ids)) ?? Enumerable.Empty<RmsOccupancyHazard>()).ToList();
			var links = new List<RmsOccupancyContactLink>();
			foreach (var id in ids) links.AddRange((await _links.GetForOccupancyAsync(departmentId, id)) ?? Enumerable.Empty<RmsOccupancyContactLink>());
			var open = await _violations.CountOpenByOccupancyAsync(departmentId, ids) ?? new Dictionary<string, int>();
			var provenance = new List<RmsOccupancyFieldProvenance>();
			foreach (var id in ids) provenance.AddRange((await _provenance.GetForOccupancyAsync(departmentId, id)) ?? Enumerable.Empty<RmsOccupancyFieldProvenance>());
			var hydrantIds = occupancies.Where(o => !string.IsNullOrWhiteSpace(o.NearestHydrantId)).Select(o => o.NearestHydrantId).Distinct().ToList();
			var hydrants = hydrantIds.Count == 0 ? new Dictionary<string, RmsHydrant>() : ((await _hydrants.GetByIdsAsync(departmentId, hydrantIds)) ?? Enumerable.Empty<RmsHydrant>()).ToDictionary(h => h.RmsHydrantId);

			var read = await _protection.RevealOccupanciesAsync(departmentId, occupancies, cancellationToken);
			read.Merge(await _protection.RevealOccupancyHazardsAsync(departmentId, hazards, cancellationToken));
			var now = DateTime.UtcNow;
			foreach (var o in occupancies)
			{
				var p = new OccupancyDispatchProjectionV1
				{
					OccupancyId = o.RmsOccupancyId, OccupancyNumber = o.OccupancyNumber, Name = o.Name, Status = o.Status,
					AddressText = string.Join(", ", new[] { o.AddressText, o.City, o.StateProvince, o.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))), Latitude = o.Latitude, Longitude = o.Longitude,
					ConstructionType = o.ConstructionType, RoofType = o.RoofType, OccupancyType = o.OccupancyType, Stories = o.Stories, OccupantLoad = o.OccupantLoad, OccupancyHours = o.OccupancyHours,
					HasOccupantsNeedingAssistance = o.HasOccupantsNeedingAssistance, OccupantsNeedingAssistanceNotes = o.OccupantsNeedingAssistanceNotes,
					SprinklerType = o.SprinklerType, HasStandpipe = o.HasStandpipe, HasFireAlarm = o.HasFireAlarm, FdcLocation = o.FdcLocation,
					GasShutoffLocation = o.GasShutoffLocation, ElectricShutoffLocation = o.ElectricShutoffLocation, WaterShutoffLocation = o.WaterShutoffLocation, UtilityNotes = o.UtilityNotes,
					KnoxBoxLocation = o.KnoxBoxLocation, GateCode = o.GateCode, AlarmPanelLocation = o.AlarmPanelLocation, AlarmCompany = o.AlarmCompany, AlarmCompanyPhone = o.AlarmCompanyPhone, AccessNotes = o.AccessNotes,
					NearestHydrantId = o.NearestHydrantId, RequiredFireFlowGpm = o.RequiredFireFlowGpm, WaterSupplyNotes = o.WaterSupplyNotes,
					EmergencyContactName = o.EmergencyContactName, EmergencyContactPhone = o.EmergencyContactPhone,
					HazmatOnSite = o.HazmatOnSite, GeneralHazardNotes = o.GeneralHazardNotes, TacticalSummary = o.TacticalSummary,
					LastReviewedOn = o.LastReviewedOn, NextReviewDue = o.NextReviewDue, IsReviewOverdue = o.IsReviewOverdue(now), LastInspectedOn = o.LastInspectedOn,
					OpenViolationCount = open.TryGetValue(o.RmsOccupancyId, out var count) ? count : 0,
					IsProtected = o.IsProtected || read.IsProtected, ProtectedReason = read.ProtectedReason
				};
				if (o.NearestHydrantId != null && hydrants.TryGetValue(o.NearestHydrantId, out var hydrant)) { p.NearestHydrantNumber = hydrant.HydrantNumber; p.NearestHydrantLocation = hydrant.AddressText ?? hydrant.HydrantNumber; }
				p.Hazards = hazards.Where(h => h.RmsOccupancyId == o.RmsOccupancyId).Select(h => new OccupancyDispatchHazardV1
				{
					HazardId = h.RmsOccupancyHazardId, HazardType = h.HazardType, Severity = h.Severity, Title = h.Title, Description = h.Description, LocationDescription = h.LocationDescription, GpsCoordinates = h.GpsCoordinates, ShouldAlert = h.ShouldAlert,
					RedactedFields = read.RedactedFields.Where(f => f.StartsWith("rmsoccupancyhazards.", StringComparison.Ordinal)).ToList()
				}).ToList();
				p.Contacts = links.Where(l => l.RmsOccupancyId == o.RmsOccupancyId).Select(l => new OccupancyDispatchContactV1 { ContactId = l.ContactId, Role = l.Role, IsPrimary = l.IsPrimary }).ToList();
				p.Sources = provenance.Where(x => x.RmsOccupancyId == o.RmsOccupancyId).Select(x => x.SourceKind == 0 ? "Records" : ((RmsOccupancyCrosswalkSourceKind)x.SourceKind).ToString()).Distinct().OrderBy(x => x).ToList();
				p.RedactedFields = read.RedactedFields.Where(f => f.StartsWith("rmsoccupancies.", StringComparison.Ordinal)).Distinct().ToList();
				result[o.RmsOccupancyId] = p;
			}
			return result;
		}

		public async Task<bool> IsRecordsOwnedAsync(int departmentId)
		{
			try
			{
				var ownership = await _ownerships.GetForDepartmentAsync(departmentId);
				return ownership != null && ownership.State == (int)RmsOccupancyOwnershipState.RecordsOwned;
			}
			catch (Exception ex)
			{
				// A missing table (module never migrated) or a transient failure must never take Contacts down; Contacts stays the owner.
				Logging.LogException(ex, $"Occupancy ownership lookup failed for department {departmentId}; treating structure writes as Contacts-owned.");
				return false;
			}
		}

		public async Task<Dictionary<string, ContactPreplan>> GetPreplanProjectionsAsync(int departmentId, IEnumerable<string> contactIds, CancellationToken cancellationToken = default)
		{
			var result = new Dictionary<string, ContactPreplan>(StringComparer.Ordinal);
			if (!await IsRecordsOwnedAsync(departmentId)) return result;
			foreach (var pair in await GetDispatchProjectionsForContactsAsync(departmentId, contactIds, cancellationToken))
				result[pair.Key] = pair.Value.ToContactPreplanView(pair.Key, departmentId);
			return result;
		}

		public async Task<string> GetOccupancyIdForContactAsync(int departmentId, string contactId)
		{
			if (string.IsNullOrWhiteSpace(contactId)) return null;
			var map = await OccupancyIdsForContactsAsync(departmentId, new List<string> { contactId });
			return map.TryGetValue(contactId, out var id) ? id : null;
		}

		#endregion
	}
}
