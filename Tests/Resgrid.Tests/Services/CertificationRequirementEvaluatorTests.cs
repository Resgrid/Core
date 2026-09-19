using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;

namespace Resgrid.Tests.Services
{
	/// <summary>Plan D10: the D1.7 validity rule (AND/OR groups, AllowTrainee, the PendingVerification setting, NeverExpires, grace boundary math) and the template catalog.</summary>
	[TestFixture]
	public class CertificationRequirementEvaluatorTests
	{
		private static readonly DateTime Today = new DateTime(2026, 9, 19);
		private const int Role = 12;
		private const string User = "user-a";

		private static DepartmentCertificationType Type(int id, string code, bool neverExpires = false)
			=> new DepartmentCertificationType { DepartmentCertificationTypeId = id, DepartmentId = 1, Type = code, Code = code, NeverExpires = neverExpires };

		private static PersonnelCertification Record(int typeId, DateTime? expires, PersonnelCertificationStatuses status = PersonnelCertificationStatuses.Active, string user = User, bool deleted = false)
			=> new PersonnelCertification { PersonnelCertificationId = typeId * 100, DepartmentId = 1, UserId = user, DepartmentCertificationTypeId = typeId, ExpiresOn = expires, Status = (int)status, IsDeleted = deleted };

		private static PersonnelRoleCertificationRequirement Req(int typeId, bool mandatory = true, int? group = null, bool trainee = false, int? grace = null, DateTime? added = null)
			=> new PersonnelRoleCertificationRequirement { PersonnelRoleCertificationRequirementId = typeId, PersonnelRoleId = Role, DepartmentId = 1, DepartmentCertificationTypeId = typeId, IsMandatory = mandatory, AnyOfGroup = group, AllowTrainee = trainee, GraceDaysOverride = grace, AddedOn = added ?? Today.AddYears(-1) };

		private static readonly Dictionary<int, DepartmentCertificationType> Types = new[] { Type(1, "EMT"), Type(2, "AEMT"), Type(3, "P"), Type(4, "ICS-100", neverExpires: true), Type(5, "BLS") }.ToDictionary(t => t.DepartmentCertificationTypeId);

		private static RoleCertificationEvaluation Eval(IEnumerable<PersonnelRoleCertificationRequirement> reqs, IEnumerable<PersonnelCertification> records, DepartmentCertificationSettings settings = null, DateTime? on = null)
			=> CertificationRequirementEvaluator.Evaluate(Role, User, reqs.ToList(), records.ToList(), Types, settings ?? new DepartmentCertificationSettings { DepartmentId = 1 }, on ?? Today);

		[Test]
		public void A_valid_active_record_satisfies_an_and_requirement_and_expiry_is_compared_by_date()
		{
			Eval(new[] { Req(1) }, new[] { Record(1, Today) }).Qualified.Should().BeTrue("valid through the printed date");
			var lapsed = Eval(new[] { Req(1) }, new[] { Record(1, Today.AddDays(-1)) });
			lapsed.Qualified.Should().BeFalse();
			lapsed.Violations.Single().ViolationStartedOn.Should().Be(Today.AddDays(-1));
			lapsed.RemovalDueOn.Should().Be(Today.AddDays(29), "30-day default grace from the lapse date");
			CertificationRequirementEvaluator.IsPastGrace(lapsed.RemovalDueOn, Today.AddDays(29)).Should().BeFalse("day 30 is the last day of grace");
			CertificationRequirementEvaluator.IsPastGrace(lapsed.RemovalDueOn, Today.AddDays(30)).Should().BeTrue("removal happens on day grace + 1");
		}

		[Test]
		public void Or_groups_are_satisfied_by_any_member_and_and_rows_stay_independent()
		{
			var reqs = new[] { Req(1, group: 1), Req(2, group: 1), Req(3, group: 1), Req(5) };
			Eval(reqs, new[] { Record(2, Today.AddYears(1)), Record(5, Today.AddYears(1)) }).Qualified.Should().BeTrue("AEMT satisfies the EMT/AEMT/P set");
			var noBls = Eval(reqs, new[] { Record(3, Today.AddYears(1)) });
			noBls.Qualified.Should().BeFalse();
			noBls.Violations.Select(v => v.DepartmentCertificationTypeId).Should().Equal(5);
			var noLadder = Eval(reqs, new[] { Record(5, Today.AddYears(1)) });
			noLadder.Violations.Select(v => v.DepartmentCertificationTypeId).Should().BeEquivalentTo(new[] { 1, 2, 3 }, "every member of a failed OR-set is reported");
		}

		[Test]
		public void Trainee_and_pending_verification_records_count_only_when_allowed()
		{
			Eval(new[] { Req(1) }, new[] { Record(1, Today.AddYears(1), PersonnelCertificationStatuses.Trainee) }).Qualified.Should().BeFalse();
			Eval(new[] { Req(1, trainee: true) }, new[] { Record(1, Today.AddYears(1), PersonnelCertificationStatuses.Trainee) }).Qualified.Should().BeTrue();
			Eval(new[] { Req(1) }, new[] { Record(1, Today.AddYears(1), PersonnelCertificationStatuses.PendingVerification) }).Qualified.Should().BeFalse();
			Eval(new[] { Req(1) }, new[] { Record(1, Today.AddYears(1), PersonnelCertificationStatuses.PendingVerification) }, new DepartmentCertificationSettings { TreatPendingVerificationAsValid = true }).Qualified.Should().BeTrue();
			foreach (var status in new[] { PersonnelCertificationStatuses.Suspended, PersonnelCertificationStatuses.Revoked, PersonnelCertificationStatuses.Expired })
				Eval(new[] { Req(1, trainee: true) }, new[] { Record(1, Today.AddYears(1), status) }, new DepartmentCertificationSettings { TreatPendingVerificationAsValid = true }).Qualified.Should().BeFalse(status.ToString());
		}

		[Test]
		public void Never_expiring_types_ignore_the_expiry_date_and_deleted_or_untyped_records_never_count()
		{
			Eval(new[] { Req(4) }, new[] { Record(4, Today.AddYears(-5)) }).Qualified.Should().BeTrue();
			Eval(new[] { Req(1) }, new[] { Record(1, Today.AddYears(1), deleted: true) }).Qualified.Should().BeFalse();
			var untyped = Record(1, Today.AddYears(1)); untyped.DepartmentCertificationTypeId = null;
			Eval(new[] { Req(1) }, new[] { untyped }).Qualified.Should().BeFalse();
			Eval(new[] { Req(1) }, new[] { Record(1, Today.AddYears(1), user: "someone-else") }).Qualified.Should().BeFalse("another member's card is not mine");
			Eval(new[] { Req(1) }, new[] { Record(1, null) }).Qualified.Should().BeTrue("a typed record with no expiry on an expiring type is open-ended");
		}

		[Test]
		public void Optional_requirements_warn_and_never_block_and_grace_overrides_and_anchors_apply()
		{
			var optional = Eval(new[] { Req(1, mandatory: false), Req(5) }, new[] { Record(5, Today.AddYears(1)) });
			optional.Qualified.Should().BeTrue(); optional.WarningsOnly.Should().BeTrue(); optional.RemovalDueOn.Should().BeNull();

			var overridden = Eval(new[] { Req(1, grace: 5) }, new[] { Record(1, Today.AddDays(-2)) });
			overridden.RemovalDueOn.Should().Be(Today.AddDays(3));

			var neverHeld = Eval(new[] { Req(1, added: Today.AddDays(-10)) }, Array.Empty<PersonnelCertification>());
			neverHeld.Violations.Single().ViolationStartedOn.Should().Be(Today.AddDays(-10), "the requirement date anchors a member who never held the type");
			neverHeld.RemovalDueOn.Should().Be(Today.AddDays(20));

			var withMembership = CertificationRequirementEvaluator.Evaluate(Role, User, new[] { Req(1, added: Today.AddDays(-10)) }, Array.Empty<PersonnelCertification>(), Types, new DepartmentCertificationSettings(), Today, memberSince: Today.AddDays(-3));
			withMembership.Violations.Single().ViolationStartedOn.Should().Be(Today.AddDays(-3), "an explicit membership date wins");

			var earliest = Eval(new[] { Req(1), Req(5, grace: 2) }, new[] { Record(1, Today.AddDays(-1)), Record(5, Today.AddDays(-1)) });
			earliest.RemovalDueOn.Should().Be(Today.AddDays(1), "the soonest mandatory deadline drives removal");
		}

		[Test]
		public void The_most_favourable_record_wins_and_days_until_expiry_is_null_for_never_expiring()
		{
			var two = Eval(new[] { Req(1) }, new[] { Record(1, Today.AddDays(-1)), new PersonnelCertification { PersonnelCertificationId = 9, DepartmentId = 1, UserId = User, DepartmentCertificationTypeId = 1, ExpiresOn = Today.AddDays(200), Status = 0 } });
			two.Qualified.Should().BeTrue(); two.Outcomes.Single().PersonnelCertificationId.Should().Be(9);
			CertificationRequirementEvaluator.DaysUntilExpiry(Today.AddDays(3), false, Today).Should().Be(3);
			CertificationRequirementEvaluator.DaysUntilExpiry(Today.AddDays(3), true, Today).Should().BeNull();
			CertificationRequirementEvaluator.DaysUntilExpiry(null, false, Today).Should().BeNull();
		}

		[Test]
		public void Lead_days_parse_defensively()
		{
			DepartmentCertificationSettings.ParseLeadDays("60, 30,14;7 1,7,0,-3,abc").Should().Equal(60, 30, 14, 7, 1);
			DepartmentCertificationSettings.ParseLeadDays("").Should().Equal(60, 30, 14, 7, 1);
			DepartmentCertificationSettings.ParseLeadDays(null).Should().Equal(60, 30, 14, 7, 1);
		}

		[Test]
		public void Template_catalog_has_unique_codes_both_scopes_every_category_and_projects_into_a_department_type()
		{
			var all = CertificationTypeTemplateCatalog.All;
			all.Count.Should().BeGreaterThanOrEqualTo(60);
			all.Select(t => t.Code).Should().OnlyHaveUniqueItems();
			all.Select(t => t.Id).Should().OnlyHaveUniqueItems();
			all.Should().OnlyContain(t => t.Code == t.Code.ToUpperInvariant() && t.Code.Length <= 50 && !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.IssuingAuthority));
			all.Count(t => t.AppliesTo == CertificationAppliesTo.Unit).Should().BeGreaterThanOrEqualTo(8);
			Enum.GetValues<CertificationCategories>().Where(c => c != CertificationCategories.Other).Should().OnlyContain(c => all.Any(t => t.Category == c));
			all.Where(t => t.NeverExpires).Should().OnlyContain(t => t.DefaultValidityMonths == null);
			all.Where(t => t.AppliesTo == CertificationAppliesTo.Unit).Should().OnlyContain(t => !t.RequiresVerification && t.RenewalCreditHoursRequired == null);

			CertificationTypeTemplateCatalog.GetById("nremt-p").Should().NotBeNull();
			CertificationTypeTemplateCatalog.Search("dot inspection").Select(t => t.Code).Should().Contain("DOT-INSPECTION");
			CertificationTypeTemplateCatalog.GetByScope(CertificationAppliesTo.Unit).Should().OnlyContain(t => t.AppliesTo == CertificationAppliesTo.Unit);

			var projected = CertificationTypeTemplateCatalog.ToDepartmentType(CertificationTypeTemplateCatalog.GetById("nremt-p"), 5);
			projected.DepartmentId.Should().Be(5); projected.Code.Should().Be("NREMT-P"); projected.Type.Should().Be("NREMT Paramedic");
			projected.DefaultValidityMonths.Should().Be(24); projected.RenewalCreditHoursRequired.Should().Be(60m); projected.AppliesTo.Should().Be((int)CertificationAppliesTo.Person); projected.IsActive.Should().BeTrue();
		}
	}
}
