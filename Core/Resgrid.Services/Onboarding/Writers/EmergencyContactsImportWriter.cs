using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>
	/// A person's emergency contacts ("emergency-contacts", wizard plan section 8.1 B). Every value but the order and the
	/// primary flag is protected by Advanced Data Protection, so contacts are never matched by their values: a row always adds
	/// a contact unless the external-id map knows it (a re-upload in the same workspace, or a continued import), and an import
	/// never rewrites an existing contact. Saves go through the member-scoped service, which envelopes the values; rollback
	/// removes the contact the way the Personnel screen does (soft delete).
	/// </summary>
	public sealed class EmergencyContactsImportWriter : ImportRecordWriter<DepartmentMemberEmergencyContact>
	{
		private readonly IDepartmentMemberEmergencyContactService _contacts;

		private static readonly IReadOnlyList<ImportFieldBinding<DepartmentMemberEmergencyContact>> Fields = new[]
		{
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("name", r => r.Name, (r, v) => r.Name = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("relationship", r => r.Relationship, (r, v) => r.Relationship = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("phone", r => r.PhoneNumber, (r, v) => r.PhoneNumber = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("alternate_phone", r => r.AlternatePhoneNumber, (r, v) => r.AlternatePhoneNumber = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("email", r => r.Email, (r, v) => r.Email = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("notes", r => r.Notes, (r, v) => r.Notes = v),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("is_primary", r => ImportCanonical.Bool(r.IsPrimary), (r, v) => r.IsPrimary = ImportCanonical.ToBool(v)),
			new ImportFieldBinding<DepartmentMemberEmergencyContact>("sort", r => ImportCanonical.Int(r.SortOrder), (r, v) => r.SortOrder = ImportCanonical.ToInt(v))
		};

		public EmergencyContactsImportWriter(IDepartmentMemberEmergencyContactService contacts)
		{
			_contacts = contacts;
		}

		public override string EntityKey => "emergency-contacts";
		public override ImportEffects AffectedEffects => ImportEffects.None;
		protected override IReadOnlyList<ImportFieldBinding<DepartmentMemberEmergencyContact>> Bindings => Fields;

		// There is no department-wide listing (contacts are read per member), and nothing refers to a contact by name.
		protected override Task<List<DepartmentMemberEmergencyContact>> LoadAsync(int departmentId, CancellationToken cancellationToken) =>
			Task.FromResult(new List<DepartmentMemberEmergencyContact>());

		/// <summary>"{userId}:{contactId}": the service reads contacts per member, so the key carries the member.</summary>
		protected override string KeyOf(DepartmentMemberEmergencyContact record) =>
			record.UserId + ":" + ImportCanonical.Int(record.DepartmentMemberEmergencyContactId);

		protected override string NaturalKeyOf(DepartmentMemberEmergencyContact record) => null;
		protected override string MatchKeyOf(DepartmentMemberEmergencyContact record) => null;
		protected override string MatchKeyOf(ImportCanonicalRow row) => null;

		protected override async Task<DepartmentMemberEmergencyContact> GetAsync(ImportWriteContext context, string key, CancellationToken cancellationToken)
		{
			var separator = key?.LastIndexOf(':') ?? -1;
			if (separator <= 0 || ImportCanonical.ToNullableInt(key.Substring(separator + 1)) is not int id)
				return null;
			return (await _contacts.GetAllForMemberAsync(context.DepartmentId, key.Substring(0, separator)) ?? new List<DepartmentMemberEmergencyContact>())
				.FirstOrDefault(c => c.DepartmentMemberEmergencyContactId == id && !c.IsDeleted);
		}

		protected override DepartmentMemberEmergencyContact New(ImportWriteContext context, ImportCanonicalRow row) => new DepartmentMemberEmergencyContact
		{
			DepartmentId = context.DepartmentId,
			CreatedByUserId = context.ActingUserId
		};

		/// <summary>Contacts are added, never rewritten: a known contact is linked, even in a reviewed update.</summary>
		public override async Task<ImportPlanResult> PlanAsync(ImportWriteContext context, IReadOnlyList<ImportCanonicalRow> rows, CancellationToken cancellationToken)
		{
			var result = await base.PlanAsync(context, rows, cancellationToken);
			foreach (var row in result.Rows.Where(r => r.Decision == ImportRowDecision.Update))
			{
				row.Decision = ImportRowDecision.Link;
				row.Changes.Clear();
			}
			return result;
		}

		protected override async Task<string> ApplyReferencesAsync(ImportWriteContext context, DepartmentMemberEmergencyContact record, ImportCanonicalRow row,
			CancellationToken cancellationToken)
		{
			var person = row.Text("person");
			if (string.IsNullOrWhiteSpace(person) || ImportReferenceToken.Is(person, ImportReferenceToken.Label))
				return "IMP-REF-UNRESOLVED";
			record.UserId = await context.ResolveReference("personnel", person, cancellationToken);
			return string.IsNullOrWhiteSpace(record.UserId) ? "IMP-REF-UNRESOLVED" : null;
		}

		protected override async Task<DepartmentMemberEmergencyContact> SaveAsync(ImportWriteContext context, DepartmentMemberEmergencyContact record,
			CancellationToken cancellationToken)
		{
			try
			{
				return await _contacts.SaveAsync(record, cancellationToken);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("Protected write blocked", StringComparison.Ordinal))
			{
				// The protection seam refused (no grant, broker down): this row fails, the chunk continues.
				throw new ImportRowRejectedException("IMP-PROTECTED-WRITE-BLOCKED");
			}
		}

		protected override async Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, DepartmentMemberEmergencyContact record, CancellationToken cancellationToken) =>
			await _contacts.DeleteAsync(record.DepartmentMemberEmergencyContactId, context.DepartmentId, record.UserId, context.ActingUserId, cancellationToken)
				? ImportRollbackOutcome.SoftDeleted
				: ImportRollbackOutcome.ManualReconciliation;

		/// <summary>Every contact the import wrote is still there (read per member, as the service allows).</summary>
		public override async Task<ImportVerifyResult> VerifyAsync(ImportWriteContext context, IReadOnlyList<ImportExternalId> written, CancellationToken cancellationToken)
		{
			var expected = written.Where(w => w.TargetKey != null).Select(w => w.TargetKey).Distinct(StringComparer.Ordinal).ToList();
			var present = 0;
			foreach (var key in expected)
				if (await GetAsync(context, key, cancellationToken) != null)
					present++;
			var result = new ImportVerifyResult();
			result.Checks[EntityKey + ".present"] = (expected.Count, present);
			return result;
		}
	}
}
