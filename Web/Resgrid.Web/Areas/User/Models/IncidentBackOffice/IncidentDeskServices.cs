using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using NodaTime;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Models.IncidentBackOffice
{
	/// <summary>
	/// Times on an incident desk render in the activation's own zone (plan §11.2, R26: a meal cutoff is local to the incident, not to
	/// the department's headquarters), shown with the zone; calendar dates are never shifted. Outside an activation the department
	/// zone applies, exactly as DepartmentTime.
	/// </summary>
	public sealed class IncidentTime
	{
		private readonly DateTimeZone _zone;

		public IncidentTime(string zoneId, string fallbackZoneId)
		{
			_zone = DepartmentTime.Resolve(string.IsNullOrWhiteSpace(zoneId) ? fallbackZoneId : zoneId);
		}

		public string ZoneId => _zone.Id;

		/// <summary>
		/// DateTime.MinValue and MaxValue cannot be shifted into a zone on the far side of UTC (the result falls outside years
		/// 1-9999), so the sentinels come back unshifted instead of throwing.
		/// </summary>
		public DateTime Local(DateTime utc) => DateTimeHelpers.IsZoneShiftSentinel(utc)
			? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)
			: Instant.FromDateTimeUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).InZone(_zone).ToDateTimeUnspecified();

		// default(DateTime) is a non-nullable column that has not been filled in yet (every new ledger row before its first save),
		// so it renders as blank rather than as 0001-01-01.
		public string Format(DateTime? utc) => IsSet(utc) ? Local(utc.Value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;
		public string Input(DateTime? utc) => IsSet(utc) ? Local(utc.Value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : null;
		public string Time(DateTime? utc) => IsSet(utc) ? Local(utc.Value).ToString("HH:mm", CultureInfo.InvariantCulture) : null;
		public DateTime ToUtc(DateTime local) => local.Kind == DateTimeKind.Utc ? local
			: DateTimeHelpers.IsZoneShiftSentinel(local) ? DateTime.SpecifyKind(local, DateTimeKind.Utc)
			: LocalDateTime.FromDateTime(local).InZoneLeniently(_zone).ToDateTimeUtc();
		public DateTime? Parse(string value) => string.IsNullOrWhiteSpace(value) ? null
			: DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ? ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified)) : null;
		public static string Date(DateTime? date) => IsSet(date) ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

		private static bool IsSet(DateTime? value) => value.HasValue && value.Value != default;
	}

	/// <summary>Services every Incident Back Office desk needs, injected once so each desk controller lists only its own.</summary>
	public sealed class IncidentDeskServices
	{
		public IncidentDeskServices(IIncidentBackOfficeAccessService access, IIncidentActivationService activations, IIncidentLookupService lookups,
			IUserProfileService profiles, IUnitsService units, IContactsService contacts, IDepartmentGroupsService groups, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> strings)
		{
			Access = access;
			Activations = activations;
			Lookups = lookups;
			Profiles = profiles;
			Units = units;
			Contacts = contacts;
			Groups = groups;
			Departments = departments;
			Strings = strings;
		}

		public IIncidentBackOfficeAccessService Access { get; }
		public IIncidentActivationService Activations { get; }
		public IIncidentLookupService Lookups { get; }
		public IUserProfileService Profiles { get; }
		public IUnitsService Units { get; }
		public IContactsService Contacts { get; }
		public IDepartmentGroupsService Groups { get; }
		public IDepartmentsService Departments { get; }
		public IStringLocalizer<Resgrid.Localization.Areas.User.IncidentBackOffice.IncidentBackOffice> Strings { get; }

		private Dictionary<string, string> _people;
		private Dictionary<string, string> _units;
		private Dictionary<string, string> _contacts;
		private Dictionary<string, string> _groups;

		/// <summary>Current members by user id (inactive members are never offered as assignees).</summary>
		public async Task<Dictionary<string, string>> PeopleAsync(int departmentId)
		{
			if (_people != null) return _people;
			var members = await Departments.GetAllMembersForDepartmentAsync(departmentId) ?? new List<DepartmentMember>();
			var profiles = await Profiles.GetAllProfilesForDepartmentAsync(departmentId) ?? new Dictionary<string, UserProfile>();
			_people = members.Where(m => DepartmentMemberStateHelper.IsCurrentMember(m, departmentId))
				.ToDictionary(m => m.UserId, m => profiles.TryGetValue(m.UserId, out var p) ? p.FullName.AsFirstNameLastName : m.UserId, StringComparer.OrdinalIgnoreCase);
			return _people;
		}

		public async Task<Dictionary<string, string>> UnitsAsync(int departmentId)
			=> _units ??= (await Units.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>()).ToDictionary(u => u.UnitId.ToString(CultureInfo.InvariantCulture), u => u.Name);

		public async Task<Dictionary<string, string>> ContactsAsync(int departmentId)
			=> _contacts ??= (await Contacts.GetAllContactsForDepartmentAsync(departmentId) ?? new List<Contact>()).GroupBy(c => c.ContactId).ToDictionary(g => g.Key, g => g.First().GetName(), StringComparer.OrdinalIgnoreCase);

		public async Task<Dictionary<string, string>> GroupsAsync(int departmentId)
			=> _groups ??= (await Groups.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).ToDictionary(g => g.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), g => g.Name);
	}
}
