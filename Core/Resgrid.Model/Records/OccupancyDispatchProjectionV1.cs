using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The additive-stable structure projection RMS exposes to Contacts and dispatch once it owns the occupancy master
	/// (RMS plan section 4.3: "exposes an additive-stable projection to Contacts rather than duplicating structure
	/// data"). Fields are only ever added; <see cref="ContractVersion"/> is bumped when they are. Protected values ride
	/// the REDACTED sentinel for a caller without a grant and <see cref="RedactedFields"/> names them, exactly like the
	/// Contacts Phase A pre-plan payload, so every existing consumer keeps its concealment logic.
	/// </summary>
	public class OccupancyDispatchProjectionV1
	{
		public const int CurrentContractVersion = 1;

		public int ContractVersion { get; set; } = CurrentContractVersion;
		public string OccupancyId { get; set; }
		public string OccupancyNumber { get; set; }
		public string Name { get; set; }
		public int Status { get; set; }
		public string AddressText { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }

		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public int? Stories { get; set; }
		public int? OccupantLoad { get; set; }
		public string OccupancyHours { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }

		public int SprinklerType { get; set; }
		public bool HasStandpipe { get; set; }
		public bool HasFireAlarm { get; set; }
		public string FdcLocation { get; set; }

		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }

		public string KnoxBoxLocation { get; set; }
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }

		public string NearestHydrantId { get; set; }
		public string NearestHydrantNumber { get; set; }
		public string NearestHydrantLocation { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }

		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }

		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }
		public List<OccupancyDispatchHazardV1> Hazards { get; set; } = new List<OccupancyDispatchHazardV1>();
		public List<OccupancyDispatchContactV1> Contacts { get; set; } = new List<OccupancyDispatchContactV1>();

		public DateTime? LastReviewedOn { get; set; }
		public DateTime? NextReviewDue { get; set; }
		public bool IsReviewOverdue { get; set; }
		public DateTime? LastInspectedOn { get; set; }
		public int OpenViolationCount { get; set; }
		/// <summary>Source kinds that supplied at least one field (provenance summary): ContactPreplan, Contact, Poi, Records.</summary>
		public List<string> Sources { get; set; } = new List<string>();

		public bool IsProtected { get; set; }
		public string ProtectedReason { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();

		/// <summary>
		/// The same structure knowledge in the Contacts Phase A pre-plan shape, so the Call Site Info tab, the v4
		/// GetCallSiteInfo payload and the four apps keep working unchanged after a department's structure writes move
		/// to RMS. Hazards are returned separately because the Contacts shape carries them as a list.
		/// </summary>
		public ContactPreplan ToContactPreplanView(string contactId, int departmentId)
		{
			var view = new ContactPreplan
			{
				ContactPreplanId = "occ:" + OccupancyId,
				DepartmentId = departmentId,
				ContactId = contactId,
				ConstructionType = ConstructionType,
				RoofType = RoofType,
				OccupancyType = OccupancyType,
				OccupancyHours = OccupancyHours,
				OccupantLoad = OccupantLoad,
				HasOccupantsNeedingAssistance = HasOccupantsNeedingAssistance,
				OccupantsNeedingAssistanceNotes = OccupantsNeedingAssistanceNotes,
				GasShutoffLocation = GasShutoffLocation,
				ElectricShutoffLocation = ElectricShutoffLocation,
				WaterShutoffLocation = WaterShutoffLocation,
				UtilityNotes = UtilityNotes,
				KnoxBoxLocation = KnoxBoxLocation,
				GateCode = GateCode,
				AlarmPanelLocation = AlarmPanelLocation,
				AlarmCompany = AlarmCompany,
				AlarmCompanyPhone = AlarmCompanyPhone,
				AccessNotes = AccessNotes,
				NearestHydrantLocation = NearestHydrantLocation ?? NearestHydrantNumber,
				RequiredFireFlowGpm = RequiredFireFlowGpm,
				WaterSupplyNotes = WaterSupplyNotes,
				EmergencyContactName = EmergencyContactName,
				EmergencyContactPhone = EmergencyContactPhone,
				HazmatOnSite = HazmatOnSite,
				GeneralHazardNotes = GeneralHazardNotes,
				TacticalSummary = TacticalSummary,
				LastReviewedOn = LastReviewedOn,
				NextReviewDue = NextReviewDue,
				IsProtected = IsProtected,
				AddedOn = LastReviewedOn ?? DateTime.UtcNow
			};
			view.Hazards = Hazards.Select(h => h.ToContactHazardView(view, departmentId)).ToList();
			return view;
		}
	}

	public class OccupancyDispatchHazardV1
	{
		public string HazardId { get; set; }
		public int HazardType { get; set; }
		public int Severity { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();

		public ContactPreplanHazard ToContactHazardView(ContactPreplan preplan, int departmentId)
		{
			return new ContactPreplanHazard
			{
				ContactPreplanHazardId = "occ:" + HazardId,
				DepartmentId = departmentId,
				ContactPreplanId = preplan.ContactPreplanId,
				ContactId = preplan.ContactId,
				HazardType = HazardType,
				Severity = Severity,
				Title = Title,
				Description = Description,
				LocationDescription = LocationDescription,
				GpsCoordinates = GpsCoordinates,
				ShouldAlert = ShouldAlert,
				IsProtected = RedactedFields.Count > 0,
				AddedOn = DateTime.UtcNow
			};
		}
	}

	public class OccupancyDispatchContactV1
	{
		public string ContactId { get; set; }
		public int Role { get; set; }
		public bool IsPrimary { get; set; }
	}
}
