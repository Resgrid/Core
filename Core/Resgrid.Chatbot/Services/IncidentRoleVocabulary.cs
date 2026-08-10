using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Maps the words an Incident Commander actually says ("safety", "ops", "staging manager", "RIT")
	/// onto <see cref="IncidentRoleType"/>, and back to a readable position name. Kept separate from the
	/// enum so radio shorthand can grow without touching the model.
	///
	/// RIT/RIC has no <see cref="IncidentRoleType"/> of its own — on a Resgrid board it is a lane, not a
	/// command position — so it resolves to null and callers answer from the structure instead.
	/// </summary>
	public static class IncidentRoleVocabulary
	{
		private static readonly Dictionary<IncidentRoleType, string> DisplayNames = new Dictionary<IncidentRoleType, string>
		{
			[IncidentRoleType.IncidentCommander] = "Incident Commander",
			[IncidentRoleType.DeputyIncidentCommander] = "Deputy Incident Commander",
			[IncidentRoleType.UnifiedCommandMember] = "Unified Command member",
			[IncidentRoleType.OperationsSectionChief] = "Operations Section Chief",
			[IncidentRoleType.PlanningSectionChief] = "Planning Section Chief",
			[IncidentRoleType.LogisticsSectionChief] = "Logistics Section Chief",
			[IncidentRoleType.FinanceAdminSectionChief] = "Finance/Admin Section Chief",
			[IncidentRoleType.SafetyOfficer] = "Safety Officer",
			[IncidentRoleType.LiaisonOfficer] = "Liaison Officer",
			[IncidentRoleType.PublicInformationOfficer] = "Public Information Officer",
			[IncidentRoleType.StagingAreaManager] = "Staging Area Manager",
			[IncidentRoleType.ResourcesUnitLeader] = "Resources Unit Leader",
			[IncidentRoleType.SituationUnitLeader] = "Situation Unit Leader",
			[IncidentRoleType.DocumentationUnitLeader] = "Documentation Unit Leader",
			[IncidentRoleType.CommunicationsUnitLeader] = "Communications Unit Leader",
			[IncidentRoleType.DivisionGroupSupervisor] = "Division/Group Supervisor",
			[IncidentRoleType.BranchDirector] = "Branch Director",
			[IncidentRoleType.StrikeTeamTaskForceLeader] = "Strike Team/Task Force Leader",
			[IncidentRoleType.MedicalUnitLeader] = "Medical Unit Leader",
			[IncidentRoleType.RehabOfficer] = "Rehab Officer",
			[IncidentRoleType.MedicalBranchDirector] = "Medical Branch Director",
			[IncidentRoleType.TriageOfficer] = "Triage Officer",
			[IncidentRoleType.TreatmentOfficer] = "Treatment Officer",
			[IncidentRoleType.TransportOfficer] = "Transport Officer",
			[IncidentRoleType.HazMatGroupSupervisor] = "HazMat Group Supervisor",
			[IncidentRoleType.DeconOfficer] = "Decon Officer",
			[IncidentRoleType.EntryTeamLeader] = "Entry Team Leader",
			[IncidentRoleType.SearchGroupSupervisor] = "Search Group Supervisor",
			[IncidentRoleType.AirOperationsBranchDirector] = "Air Operations Branch Director",
			[IncidentRoleType.ShelterMassCareCoordinator] = "Shelter/Mass Care Coordinator",
			[IncidentRoleType.DamageAssessmentLead] = "Damage Assessment Lead"
		};

		// Matched longest-first (see Aliases below) so "operations section chief" wins over "ops",
		// "medical branch director" over "branch director", and "air ops" over "ops". Declaration order
		// here is for readability only — length ordering is enforced, not assumed.
		private static readonly List<(string Alias, IncidentRoleType Role)> AliasSource = new List<(string, IncidentRoleType)>
		{
			("deputy incident commander", IncidentRoleType.DeputyIncidentCommander),
			("deputy ic", IncidentRoleType.DeputyIncidentCommander),
			("deputy", IncidentRoleType.DeputyIncidentCommander),
			("unified command", IncidentRoleType.UnifiedCommandMember),
			("incident commander", IncidentRoleType.IncidentCommander),
			("operations section chief", IncidentRoleType.OperationsSectionChief),
			("operations chief", IncidentRoleType.OperationsSectionChief),
			("operations", IncidentRoleType.OperationsSectionChief),
			("ops chief", IncidentRoleType.OperationsSectionChief),
			("ops", IncidentRoleType.OperationsSectionChief),
			("planning section chief", IncidentRoleType.PlanningSectionChief),
			("planning chief", IncidentRoleType.PlanningSectionChief),
			("planning", IncidentRoleType.PlanningSectionChief),
			("logistics section chief", IncidentRoleType.LogisticsSectionChief),
			("logistics chief", IncidentRoleType.LogisticsSectionChief),
			("logistics", IncidentRoleType.LogisticsSectionChief),
			("finance admin section chief", IncidentRoleType.FinanceAdminSectionChief),
			("finance section chief", IncidentRoleType.FinanceAdminSectionChief),
			("finance", IncidentRoleType.FinanceAdminSectionChief),
			("safety officer", IncidentRoleType.SafetyOfficer),
			("safety", IncidentRoleType.SafetyOfficer),
			("public information officer", IncidentRoleType.PublicInformationOfficer),
			("pio", IncidentRoleType.PublicInformationOfficer),
			("liaison officer", IncidentRoleType.LiaisonOfficer),
			("liaison", IncidentRoleType.LiaisonOfficer),
			("staging area manager", IncidentRoleType.StagingAreaManager),
			("staging manager", IncidentRoleType.StagingAreaManager),
			("resources unit leader", IncidentRoleType.ResourcesUnitLeader),
			("resource unit leader", IncidentRoleType.ResourcesUnitLeader),
			("situation unit leader", IncidentRoleType.SituationUnitLeader),
			("documentation unit leader", IncidentRoleType.DocumentationUnitLeader),
			("communications unit leader", IncidentRoleType.CommunicationsUnitLeader),
			("comms unit leader", IncidentRoleType.CommunicationsUnitLeader),
			("division supervisor", IncidentRoleType.DivisionGroupSupervisor),
			("group supervisor", IncidentRoleType.DivisionGroupSupervisor),
			("branch director", IncidentRoleType.BranchDirector),
			("strike team leader", IncidentRoleType.StrikeTeamTaskForceLeader),
			("task force leader", IncidentRoleType.StrikeTeamTaskForceLeader),
			("medical branch director", IncidentRoleType.MedicalBranchDirector),
			("medical unit leader", IncidentRoleType.MedicalUnitLeader),
			("rehab officer", IncidentRoleType.RehabOfficer),
			("rehab", IncidentRoleType.RehabOfficer),
			("triage officer", IncidentRoleType.TriageOfficer),
			("triage", IncidentRoleType.TriageOfficer),
			("treatment officer", IncidentRoleType.TreatmentOfficer),
			("treatment", IncidentRoleType.TreatmentOfficer),
			("transport officer", IncidentRoleType.TransportOfficer),
			("transport", IncidentRoleType.TransportOfficer),
			("hazmat group supervisor", IncidentRoleType.HazMatGroupSupervisor),
			("hazmat supervisor", IncidentRoleType.HazMatGroupSupervisor),
			("decon officer", IncidentRoleType.DeconOfficer),
			("decon", IncidentRoleType.DeconOfficer),
			("entry team leader", IncidentRoleType.EntryTeamLeader),
			("search group supervisor", IncidentRoleType.SearchGroupSupervisor),
			("air operations branch director", IncidentRoleType.AirOperationsBranchDirector),
			("air operations", IncidentRoleType.AirOperationsBranchDirector),
			("air ops", IncidentRoleType.AirOperationsBranchDirector),
			("shelter mass care coordinator", IncidentRoleType.ShelterMassCareCoordinator),
			("mass care coordinator", IncidentRoleType.ShelterMassCareCoordinator),
			("shelter coordinator", IncidentRoleType.ShelterMassCareCoordinator),
			("damage assessment lead", IncidentRoleType.DamageAssessmentLead),
			("ic", IncidentRoleType.IncidentCommander),
			("commander", IncidentRoleType.IncidentCommander)
		};

		/// <summary>
		/// The alias table ordered longest-first. A shorter alias is always a substring risk for a longer
		/// one ("ops" inside "air ops", "branch director" inside "medical branch director"), and relying
		/// on hand-maintained declaration order to avoid that has already produced wrong answers — so the
		/// ordering is computed instead.
		/// </summary>
		private static readonly List<(string Alias, IncidentRoleType Role)> Aliases =
			AliasSource.OrderByDescending(a => a.Alias.Length).ToList();

		/// <summary>Terms that name a RIT/RIC — a lane on the board rather than an ICS command position.</summary>
		private static readonly string[] RapidInterventionAliases =
		{
			"rapid intervention team", "rapid intervention crew", "rapid intervention", "rit", "ric"
		};

		public static string DisplayName(IncidentRoleType role)
			=> DisplayNames.TryGetValue(role, out var name) ? name : role.ToString();

		/// <summary>Null when the text names no known ICS position.</summary>
		public static IncidentRoleType? Resolve(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var needle = Normalize(text);
			if (needle.Length == 0)
				return null;

			foreach (var (alias, role) in Aliases)
			{
				// Whole-word containment so "ic" doesn't match inside "medic" or "logistics".
				if (ContainsWord(needle, alias))
					return role;
			}

			return null;
		}

		/// <summary>True when the question was about a RIT/RIC rather than a command position.</summary>
		public static bool IsRapidIntervention(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return false;

			var needle = Normalize(text);
			return RapidInterventionAliases.Any(alias => ContainsWord(needle, alias));
		}

		private static string Normalize(string text)
		{
			var cleaned = new string(text.Trim().ToLowerInvariant()
				.Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ')
				.ToArray());

			return string.Join(" ", cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
		}

		private static bool ContainsWord(string haystack, string needle)
			=> haystack == needle
				|| haystack.StartsWith(needle + " ", StringComparison.Ordinal)
				|| haystack.EndsWith(" " + needle, StringComparison.Ordinal)
				|| haystack.Contains(" " + needle + " ");
	}
}
