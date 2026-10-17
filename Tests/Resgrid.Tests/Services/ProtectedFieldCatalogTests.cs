using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ProtectedFieldCatalogTests
	{
		private ProtectedFieldCatalog _catalog;

		[SetUp]
		public void SetUp()
		{
			_catalog = new ProtectedFieldCatalog();
		}

		[Test]
		public void Field_ids_are_unique_and_lowercase()
		{
			var all = _catalog.GetAll();

			all.Should().NotBeEmpty();
			all.Select(e => e.FieldId).Should().OnlyHaveUniqueItems(
				"FieldIds are AAD components and must never collide");
			all.Should().OnlyContain(e => e.FieldId == e.FieldId.ToLowerInvariant(),
				"FieldIds are stable lowercase identifiers");
			all.Should().OnlyContain(e => e.FieldId == $"{e.TableName.ToLowerInvariant()}.{e.ColumnName.ToLowerInvariant()}",
				"FieldId convention is table.column so ids stay derivable and collision-free");
		}

		[Test]
		public void P0_families_are_present()
		{
			_catalog.GetById("calls.contactname").Should().NotBeNull();
			_catalog.GetById("calls.natureofcall").Classification.Should().Be(ProtectedFieldClassification.Phi);
			_catalog.GetById("calllogs.narrative").Should().NotBeNull();
			_catalog.GetById("contacts.email").Should().NotBeNull();
			_catalog.GetById("contactnotes.note").Should().NotBeNull();
			_catalog.GetById("departmentmembersensitivedata.identificationnumber").Should().NotBeNull();
		}

		[Test]
		public void Storage_kinds_match_column_types()
		{
			_catalog.GetById("callattachments.data").StorageKind.Should().Be(ProtectedFieldStorageKind.Binary);
			_catalog.GetById("contacts.image").StorageKind.Should().Be(ProtectedFieldStorageKind.Binary);
			_catalog.GetById("callnotes.note").StorageKind.Should().Be(ProtectedFieldStorageKind.Text);
			_catalog.GetById("calls.notes").StorageKind.Should().Be(ProtectedFieldStorageKind.Text);
			// Every typed (companion) value the catalog once held - call note, attachment, unit state and message
			// recipient coordinates, RMS location/exposure coordinates and checklist outcomes - is operational and
			// left the catalog on 2026-10-17, so no companion entry and no companion binding remains.
			_catalog.GetAll().Should().NotContain(e => e.StorageKind == ProtectedFieldStorageKind.CompanionColumn);
			AdpTableBindings.V1.SelectMany(b => b.Columns).Should().NotContain(c => c.StorageKind == ProtectedFieldStorageKind.CompanionColumn);
		}

		[Test]
		public void Table_lookup_is_case_insensitive()
		{
			_catalog.GetForTable("CALLS").Should().NotBeEmpty();
			_catalog.GetForTable("calls").Select(e => e.FieldId)
				.Should().BeEquivalentTo(_catalog.GetForTable("Calls").Select(e => e.FieldId));
			_catalog.IsProtectedField("calls", "NATUREOFCALL").Should().BeTrue();
			_catalog.IsProtectedField("Calls", "Number").Should().BeFalse(
				"the system-generated call number stays plaintext");
			_catalog.IsProtectedField("CALLS", "name").Should().BeFalse(
				"the call name is operational and stays plaintext in any casing");
			_catalog.GetForTable("NoSuchTable").Should().BeEmpty();
		}

		[Test]
		public void Permissions_follow_family_boundaries()
		{
			_catalog.GetForTable("Calls").Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedCallData);
			_catalog.GetForTable("Calls").Should().OnlyContain(e => e.EditPermission == PermissionTypes.EditProtectedCallData);
			_catalog.GetForTable("Contacts").Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedContactData);
			_catalog.GetForTable("DepartmentMemberSensitiveData")
				.Should().OnlyContain(e => e.ViewPermission == PermissionTypes.ViewProtectedPersonnelData);
		}

		[Test]
		public void Version_tracks_the_highest_entry_version()
		{
			// The constant must never lag the data: version-scoped queries and the upgrade work list
			// both key off it, so a hand-maintained value that fell behind would quietly hand a v1
			// department fields it does not own.
			_catalog.Version.Should().Be(_catalog.GetAll().Max(e => e.AddedInCatalogVersion));
			_catalog.GetAll().Should().OnlyContain(e => e.AddedInCatalogVersion >= 1);
		}

		[Test]
		public void Catalog_versions_are_contiguous_from_one()
		{
			// A gap (v1 then v3) would leave an upgrade range that sweeps nothing while still
			// bumping a department's pinned version past fields that were never encrypted. The only
			// permitted holes are the deliberately retired versions, whose every entry was deleted
			// before any department enrolled (2026-10-17); their numbers are never reused.
			var versions = _catalog.GetAll().Select(e => e.AddedInCatalogVersion).Distinct().OrderBy(v => v).ToList();
			var retired = ProtectedFieldCatalog.RetiredCatalogVersions;

			retired.Should().OnlyContain(v => v >= 1 && v <= _catalog.Version,
				"a retired version is a hole inside the published range, not a number past it");
			versions.Should().BeEquivalentTo(Enumerable.Range(1, _catalog.Version).Except(retired),
				"every live version from 1 to the current one owns at least one field");
			foreach (var version in retired)
				_catalog.GetAll().Should().NotContain(e => e.AddedInCatalogVersion == version,
					$"retired catalog version {version} is intentionally empty");
		}

		/// <summary>
		/// Operational data the system runs on — call names, types, addresses, locations and identifiers, unit and
		/// member positions, certification metadata, incident locations and checklist outcomes — left the catalog on
		/// 2026-10-17. It must be readable without a grant, so none of it may come back under ADP by accident.
		/// </summary>
		[Test]
		public void Operational_fields_are_neither_catalogued_nor_bound()
		{
			var operational = new (string Table, string Column)[]
			{
				("Calls", "Name"), ("Calls", "Type"), ("Calls", "Address"), ("Calls", "GeoLocationData"), ("Calls", "W3W"),
				("Calls", "SourceIdentifier"), ("Calls", "IncidentNumber"), ("Calls", "ExternalIdentifier"), ("Calls", "ReferenceNumber"),
				("CallNotes", "Latitude"), ("CallNotes", "Longitude"),
				("CallAttachments", "Latitude"), ("CallAttachments", "Longitude"),
				("MessageRecipients", "Latitude"), ("MessageRecipients", "Longitude"),
				("UnitStates", "GeoLocationData"), ("UnitStates", "Latitude"), ("UnitStates", "Longitude"),
				("Logs", "Location"), ("Logs", "OtherPersonnel"),
				("PersonnelCertifications", "Name"), ("PersonnelCertifications", "Type"), ("PersonnelCertifications", "Area"), ("PersonnelCertifications", "IssuedBy"),
				("UnitCertifications", "Number"), ("UnitCertifications", "IssuedBy"),
				("RmsOperationalRecordDetails", "CallName"), ("RmsOperationalRecordDetails", "CallAddress"), ("RmsOperationalRecordDetails", "Location"),
				("RmsOperationalRecordDetails", "CaseNumber"), ("RmsOperationalRecordDetails", "OtherPersonnel"),
				("RmsLocations", "AddressText"), ("RmsLocations", "Number"), ("RmsLocations", "NumberPrefix"), ("RmsLocations", "NumberSuffix"),
				("RmsLocations", "Street"), ("RmsLocations", "UnitValue"), ("RmsLocations", "CrossStreet1"), ("RmsLocations", "CrossStreet2"),
				("RmsLocations", "Latitude"), ("RmsLocations", "Longitude"),
				("RmsExposures", "AddressText"), ("RmsExposures", "Street"), ("RmsExposures", "Latitude"), ("RmsExposures", "Longitude"),
				("ChecklistCompletions", "Score"), ("ChecklistCompletions", "Passed"), ("ChecklistCompletionItems", "IsFailure"),
				("CalendarItems", "Title"), ("CalendarItems", "Location"), ("Documents", "Name"), ("Contacts", "CompanyName"),
				("WorkforceEmployerProfiles", "EddAddress"), ("WorkforceEmployerProfiles", "HeadquartersAddress"),
				("WorkforceEstablishments", "PhysicalAddress"), ("WorkforceWorkers", "ExternalWorkerKey"), ("WorkforceWorkers", "DisplayLabel")
			};

			var boundFieldIds = AdpTableBindings.V1.SelectMany(b => b.Columns).Select(c => c.FieldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var (table, column) in operational)
			{
				var fieldId = $"{table.ToLowerInvariant()}.{column.ToLowerInvariant()}";
				_catalog.IsProtectedField(table, column).Should().BeFalse($"{fieldId} is operational data");
				_catalog.GetById(fieldId).Should().BeNull(fieldId);
				boundFieldIds.Should().NotContain(fieldId, $"{fieldId} must not be swept by enrollment or rotation");
			}

			// A NERIS location carries nothing private at all, so the table is neither catalogued nor bound.
			_catalog.GetForTable("RmsLocations").Should().BeEmpty();
			AdpTableBindings.V1.Should().NotContain(b => string.Equals(b.TableName, "RmsLocations", StringComparison.OrdinalIgnoreCase));
			// Establishments and workers carry nothing private either (2026-10-17).
			foreach (var table in new[] { "WorkforceEstablishments", "WorkforceWorkers" })
			{
				_catalog.GetForTable(table).Should().BeEmpty(table);
				AdpTableBindings.V1.Should().NotContain(b => string.Equals(b.TableName, table, StringComparison.OrdinalIgnoreCase), table);
			}

			// Protected Workflows release only catalogued call fields; the operational ones are ordinary variables.
			ProtectedWorkflowFieldCatalog.CallFieldIds.Should().NotIntersectWith(new[]
			{
				"calls.name", "calls.type", "calls.address", "calls.geolocationdata", "calls.w3w", "calls.sourceidentifier",
				"calls.incidentnumber", "calls.externalidentifier", "calls.referencenumber"
			});

			// The private content beside them stays protected and bound.
			var stillProtected = new[]
			{
				"calls.natureofcall", "calls.notes", "calls.completednotes", "calls.contactname", "calls.contactnumber",
				"calls.callformdata", "calls.deletedreason", "calls.subjectidentifiers",
				"callnotes.note", "unitstates.note", "messagerecipients.note",
				"personnelcertifications.number", "personnelcertifications.filename", "personnelcertifications.data", "personnelcertifications.statusreason",
				"unitcertifications.notes", "rmsexposures.detailjson", "rmsoperationalrecorddetails.callnature",
				"checklistcompletions.protectedcontent", "checklistcompletionitems.protectedcontent",
				"calendaritems.description", "documents.description", "documents.data", "contacts.firstname",
				// Pre-plans stay protected, and so do affiliated entities and every FEIN / SEIN / SOS number.
				"contactpreplans.gatecode", "contactpreplans.occupantsneedingassistancenotes", "contactpreplans.knoxboxlocation",
				"workforceemployerprofiles.fein", "workforceemployerprofiles.sein", "workforceemployerprofiles.sosnumber",
				"workforceaffiliatedentities.fein", "workforceaffiliatedentities.sein", "workforceaffiliatedentities.sosnumber",
				"workforceaffiliatedentities.headquartersaddress", "workforcelaborcontractors.fein"
			};
			foreach (var fieldId in stillProtected)
			{
				_catalog.GetById(fieldId).Should().NotBeNull(fieldId);
				boundFieldIds.Should().Contain(fieldId);
			}
		}

		[Test]
		public void Checklist_outcome_version_fifteen_is_retired()
		{
			ProtectedFieldCatalog.RetiredCatalogVersions.Should().BeEquivalentTo(new[] { 15 });
			_catalog.GetAddedBetween(14, 15).Should().BeEmpty("v15 held only checklist outcomes, which are state, not private");
			foreach (var outcome in new[] { ("ChecklistCompletions", "Score"), ("ChecklistCompletions", "Passed"), ("ChecklistCompletionItems", "IsFailure") })
			{
				_catalog.IsProtectedField(outcome.Item1, outcome.Item2).Should().BeFalse($"{outcome.Item1}.{outcome.Item2} is outcome state");
				_catalog.IsProtectedField(outcome.Item1, "ProtectedContent").Should().BeTrue($"{outcome.Item1} keeps its private members sealed (review §8)");
			}
		}
	
		/// <summary>
		/// Version scoping is what keeps a department that enrolled under an older catalog from
		/// silently starting to encrypt fields added later: its AAD is computed from its pinned
		/// version and those rows were never swept. AddedInCatalogVersion drives all of it.
		/// </summary>
		[Test]
		public void Version_scoping_returns_only_what_a_pinned_department_owns()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetAllForVersion(catalog.Version).Should().BeEquivalentTo(catalog.GetAll(),
				"a current department owns the whole catalog");
			catalog.GetAllForVersion(catalog.Version + 5).Should().BeEquivalentTo(catalog.GetAll(),
				"a version beyond the code's catalog still owns only what exists");
			catalog.GetAllForVersion(0).Should().BeEmpty("an unstamped department owns nothing");
			catalog.GetAllForVersion(-1).Should().BeEmpty();

			catalog.GetAllForVersion(1).Should().OnlyContain(e => e.AddedInCatalogVersion <= 1);
		}

		[Test]
		public void Version_scoping_applies_per_table_too()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetForTableAndVersion("Calls", catalog.Version)
				.Should().BeEquivalentTo(catalog.GetForTable("Calls"));
			catalog.GetForTableAndVersion("Calls", 0).Should().BeEmpty();
			catalog.GetForTableAndVersion("NoSuchTable", catalog.Version).Should().BeEmpty();
		}

		[Test]
		public void Added_between_is_the_exact_upgrade_work_list()
		{
			var catalog = new ProtectedFieldCatalog();

			catalog.GetAddedBetween(catalog.Version, catalog.Version)
				.Should().BeEmpty("a current department is owed no upgrade sweep");
			catalog.GetAddedBetween(catalog.Version + 1, catalog.Version)
				.Should().BeEmpty("a backwards range is not an upgrade");

			// Everything in v1 is 'added between 0 and 1' — the enrollment sweep's own work list.
			catalog.GetAddedBetween(0, 1).Should().OnlyContain(e => e.AddedInCatalogVersion == 1);
			catalog.GetAddedBetween(0, catalog.Version).Should().BeEquivalentTo(catalog.GetAll(),
				"a department starting from nothing is owed every cataloged field");
		}
}
}
