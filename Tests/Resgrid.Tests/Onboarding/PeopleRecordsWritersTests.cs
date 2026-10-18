using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The people and qualification records wave 2 left out (wizard plan section 8.1 B and C): emergency contacts (protected,
	/// added and never rewritten), unit certifications (expired ones imported expired, quietly) and role certification
	/// requirements (a role's other requirements kept). Plus: a row identity made of personal values is stored hashed.
	/// </summary>
	[TestFixture]
	public class PeopleRecordsWritersTests
	{
		private const int DepartmentId = 7;

		private static ImportWriteContext Context(ImportWriteMode mode = ImportWriteMode.CreateOnly) => new()
		{
			DepartmentId = DepartmentId,
			WriteMode = mode,
			Authority = ImportAuthority.ForAdministrator(DepartmentId, "ADMIN"),
			ResolveReference = (entity, token, _) => Task.FromResult(entity switch
			{
				"personnel" => "U1",
				"units" => "21",
				"roles" => "3",
				_ => token == "src:CT-UNIT" ? "8" : "9"
			})
		};

		private static (ImportJournalWriter Writer, List<ImportChangeJournalEntry> Entries) Journal()
		{
			var entries = new List<ImportChangeJournalEntry>();
			return (new ImportJournalWriter((entry, _) =>
			{
				entry.Id = Guid.NewGuid().ToString();
				entries.Add(entry);
				return Task.CompletedTask;
			}), entries);
		}

		private static ImportCanonicalRow Row(ImportRowDecision decision, params (string Key, string Value)[] values)
		{
			var row = new ImportCanonicalRow { StagedRowId = "S1", RowNumber = 2, Decision = decision };
			foreach (var (key, value) in values)
				row.Values[key] = value;
			return row;
		}

		// ---- emergency contacts ------------------------------------------------------------------------------------------

		[Test]
		public async Task An_emergency_contact_is_added_for_its_person_and_rollback_removes_it()
		{
			var contacts = new Mock<IDepartmentMemberEmergencyContactService>();
			var stored = new List<DepartmentMemberEmergencyContact>();
			contacts.Setup(c => c.SaveAsync(It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentMemberEmergencyContact c, CancellationToken _) =>
				{
					c.DepartmentMemberEmergencyContactId = 5;
					stored.Add(c);
					return c;
				});
			contacts.Setup(c => c.GetAllForMemberAsync(DepartmentId, "U1")).ReturnsAsync(() => stored.Where(c => !c.IsDeleted).ToList());
			contacts.Setup(c => c.DeleteAsync(5, DepartmentId, "U1", "ADMIN", It.IsAny<CancellationToken>()))
				.Callback(() => stored[0].IsDeleted = true).ReturnsAsync(true);
			var writer = new EmergencyContactsImportWriter(contacts.Object);
			var (journal, entries) = Journal();

			var result = await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Create, ("person", "src:P1"), ("name", "Jordan Rivera"),
				("relationship", "Spouse"), ("phone", "+15559876543"), ("is_primary", "true")), journal, CancellationToken.None);

			result.Outcome.Should().Be(ImportRowOutcome.Created);
			result.TargetKey.Should().Be("U1:5", "the key carries the member: contacts are read per member");
			stored.Single().Should().BeEquivalentTo(new { UserId = "U1", DepartmentId, Name = "Jordan Rivera", Relationship = "Spouse", IsPrimary = true, CreatedByUserId = "ADMIN" },
				o => o.ExcludingMissingMembers());

			var rolledBack = await writer.RollbackAsync(Context(), entries.Single(), CancellationToken.None);
			rolledBack.Outcome.Should().Be(ImportRollbackOutcome.SoftDeleted);
		}

		[Test]
		public async Task An_emergency_contact_is_never_matched_by_its_protected_values_or_rewritten()
		{
			var writer = new EmergencyContactsImportWriter(Mock.Of<IDepartmentMemberEmergencyContactService>());
			var known = Row(ImportRowDecision.Undecided, ("person", "src:P1"), ("name", "Jordan Rivera"));
			known.TargetKey = "U1:5";
			var fresh = Row(ImportRowDecision.Undecided, ("person", "src:P1"), ("name", "Jordan Rivera"));
			fresh.StagedRowId = "S2";

			var plan = await writer.PlanAsync(Context(ImportWriteMode.ReviewedUpdate), new[] { known, fresh }, CancellationToken.None);

			plan.Rows.Select(r => r.Decision).Should().Equal(ImportRowDecision.Link, ImportRowDecision.Create);
		}

		[Test]
		public async Task A_contact_the_protection_seam_refuses_fails_its_row_only()
		{
			var contacts = new Mock<IDepartmentMemberEmergencyContactService>();
			contacts.Setup(c => c.SaveAsync(It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Protected write blocked (no-grant); emergency contact 0 has transient plaintext pending re-encryption."));
			var writer = new EmergencyContactsImportWriter(contacts.Object);

			var thrown = await FluentActions.Awaiting(() => writer.WriteRowAsync(Context(), Row(ImportRowDecision.Create, ("person", "src:P1"), ("name", "A")), Journal().Writer,
				CancellationToken.None)).Should().ThrowAsync<ImportRowRejectedException>();

			thrown.Which.Code.Should().Be("IMP-PROTECTED-WRITE-BLOCKED");
		}

		// ---- unit certifications -----------------------------------------------------------------------------------------

		[Test]
		public async Task An_already_expired_unit_certification_is_imported_expired_and_quietly()
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.GetUnitCertificationsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitCertification>());
			UnitCertification saved = null;
			ImportEffectPolicy effects = null;
			certifications.Setup(c => c.SaveUnitCertificationAsync(It.IsAny<UnitCertification>(), "ADMIN", It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.Callback((UnitCertification c, string _, ImportEffectPolicy e, CancellationToken _) => (saved, effects) = (c, e))
				.ReturnsAsync((UnitCertification c, string _, ImportEffectPolicy _, CancellationToken _) =>
				{
					c.UnitCertificationId = 40;
					return c;
				});
			var writer = new UnitCertificationsImportWriter(certifications.Object);
			var expired = Row(ImportRowDecision.Create, ("unit", "src:E1"), ("certification_type", "src:PUMP"), ("expires_on", "2020-01-31"));

			var plan = await writer.PlanAsync(Context(), new[] { expired }, CancellationToken.None);
			var result = await writer.WriteRowAsync(Context(), expired, Journal().Writer, CancellationToken.None);

			plan.Consequences.Should().Contain(UnitCertificationsImportWriter.AlreadyExpiredConsequence, 1);
			result.TargetKey.Should().Be("40");
			saved.Should().BeEquivalentTo(new { UnitId = 21, DepartmentCertificationTypeId = 9, Status = (int)UnitCertificationStatuses.Expired, StatusReason = "imported-expired" },
				o => o.ExcludingMissingMembers());
			effects.RaiseDomainEvents.Should().BeFalse("no 'certification added' event for imported history");
			effects.PerRowAudit.Should().BeFalse();
		}

		[Test]
		public async Task A_unit_certification_with_a_type_for_people_fails_its_row()
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.SaveUnitCertificationAsync(It.IsAny<UnitCertification>(), It.IsAny<string>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("certifications_type_scope"));
			var writer = new UnitCertificationsImportWriter(certifications.Object);

			var thrown = await FluentActions.Awaiting(() => writer.WriteRowAsync(Context(), Row(ImportRowDecision.Create, ("unit", "src:E1"), ("certification_type", "src:FF1")),
				Journal().Writer, CancellationToken.None)).Should().ThrowAsync<ImportRowRejectedException>();

			thrown.Which.Code.Should().Be("IMP-UNIT-CERT-TYPE-SCOPE");
		}

		// ---- role requirements -------------------------------------------------------------------------------------------

		private static Mock<ICertificationService> Requirements(List<PersonnelRoleCertificationRequirement> store, CertificationEnforcementModes mode = CertificationEnforcementModes.Off)
		{
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(c => c.GetAllRoleRequirementsAsync(DepartmentId)).ReturnsAsync(() => store.ToList());
			certifications.Setup(c => c.GetRoleRequirementsAsync(3)).ReturnsAsync(() => store.Where(r => r.PersonnelRoleId == 3).ToList());
			certifications.Setup(c => c.GetCertificationSettingsAsync(DepartmentId)).ReturnsAsync(new DepartmentCertificationSettings { EnforcementMode = (int)mode });
			certifications.Setup(c => c.SaveRoleRequirementsAsync(DepartmentId, 3, It.IsAny<List<PersonnelRoleCertificationRequirement>>(), "ADMIN", It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, int _, List<PersonnelRoleCertificationRequirement> list, string _, CancellationToken _) =>
				{
					store.RemoveAll(r => r.PersonnelRoleId == 3);
					foreach (var r in list)
					{
						if (r.PersonnelRoleCertificationRequirementId == 0)
							r.PersonnelRoleCertificationRequirementId = 100 + r.DepartmentCertificationTypeId;
						r.PersonnelRoleId = 3;
						store.Add(r);
					}
					return store.Where(r => r.PersonnelRoleId == 3).ToList();
				});
			return certifications;
		}

		[Test]
		public async Task A_role_requirement_is_added_without_touching_the_roles_other_requirements_and_rollback_removes_only_it()
		{
			var store = new List<PersonnelRoleCertificationRequirement>
			{
				new() { PersonnelRoleCertificationRequirementId = 50, PersonnelRoleId = 3, DepartmentId = DepartmentId, DepartmentCertificationTypeId = 7, IsMandatory = true }
			};
			var writer = new RoleRequirementsImportWriter(Requirements(store).Object);
			var (journal, entries) = Journal();

			var result = await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Create, ("role", "src:ENG"), ("certification_type", "src:DRIVER"), ("allow_trainee", "true")),
				journal, CancellationToken.None);

			result.TargetKey.Should().Be("109");
			store.Select(r => r.DepartmentCertificationTypeId).Should().BeEquivalentTo(new[] { 7, 9 });
			store.Single(r => r.DepartmentCertificationTypeId == 9).Should().BeEquivalentTo(new { IsMandatory = true, AllowTrainee = true }, o => o.ExcludingMissingMembers());

			(await writer.RollbackAsync(Context(), entries.Single(), CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.Deleted);
			store.Select(r => r.DepartmentCertificationTypeId).Should().Equal(7);
		}

		[Test]
		public async Task A_requirement_matches_only_when_role_and_type_name_existing_records()
		{
			var store = new List<PersonnelRoleCertificationRequirement>
			{
				new() { PersonnelRoleCertificationRequirementId = 50, PersonnelRoleId = 3, DepartmentId = DepartmentId, DepartmentCertificationTypeId = 7 }
			};
			var writer = new RoleRequirementsImportWriter(Requirements(store, CertificationEnforcementModes.Enforce).Object);
			var existing = Row(ImportRowDecision.Undecided, ("role", "id:3"), ("certification_type", "id:7"));
			var staged = Row(ImportRowDecision.Undecided, ("role", "src:ENG"), ("certification_type", "id:7"));
			staged.StagedRowId = "S2";

			var plan = await writer.PlanAsync(Context(), new[] { existing, staged }, CancellationToken.None);

			plan.Rows.Select(r => (r.Decision, r.TargetKey)).Should().Equal((ImportRowDecision.Link, "50"), (ImportRowDecision.Create, null));
			plan.Consequences.Should().Contain(RoleRequirementsImportWriter.EnforcedConsequence, 1, "the department enforces requirements");
		}

		// ---- row identity ------------------------------------------------------------------------------------------------

		[Test]
		public void A_row_identity_made_of_personal_or_protected_values_is_stored_hashed()
		{
			var people = new ImportEntityContract
			{
				Key = "personnel", NaturalKey = new List<string> { "email" },
				Fields = new List<ImportFieldContract> { new() { Key = "email", Sensitivity = "Personal" } }
			};
			var groups = new ImportEntityContract
			{
				Key = "groups", NaturalKey = new List<string> { "name" },
				Fields = new List<ImportFieldContract> { new() { Key = "name" } }
			};

			var person = ImportValidationService.SourceKeyFor(people, new Dictionary<string, object> { ["email"] = "Alex.Rivera@Example.org" });
			var sameAgain = ImportValidationService.SourceKeyFor(people, new Dictionary<string, object> { ["email"] = "alex.rivera@example.org " });

			person.Should().StartWith("nkh:").And.HaveLength(4 + 64).And.NotContainEquivalentOf("rivera");
			sameAgain.Should().Be(person, "a re-upload still recognizes the row");
			ImportValidationService.SourceKeyFor(groups, new Dictionary<string, object> { ["name"] = "Station 1" }).Should().Be("nk:station1");
		}
	}
}
