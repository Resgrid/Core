using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS add-on features built dark in wave 2 (§24, §25.4a item 7): the formal ISO FSRS training report, NREMT NCCP progress,
	/// certificate verification codes with the public verify page, and custom certificate templates. Consumes the baseline
	/// ledger; nothing baseline calls it (§17.3). Its callers check ILmsAccessService first; the public verification answer
	/// checks only the operator flag, so a code issued before a lapse keeps verifying (lapse rule 9).
	/// </summary>
	public class LmsTrainingService : ILmsTrainingService
	{
		private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
		public const int CodeLength = 10;

		private readonly ILearningRecordsRepository _records;
		private readonly ITrainingCategoriesRepository _categories;
		private readonly ILmsCertificateTemplatesRepository _templates;
		private readonly ICertificationService _certifications;
		private readonly IPersonnelCertificationRepository _certificationRecords;
		private readonly IDepartmentsService _departments;
		private readonly ILmsAccessService _access;
		private readonly ILmsSeatService _seats;
		private readonly ILmsExternalLearnersRepository _externalLearners;

		public LmsTrainingService(ILearningRecordsRepository records, ITrainingCategoriesRepository categories, ILmsCertificateTemplatesRepository templates,
			ICertificationService certifications, IPersonnelCertificationRepository certificationRecords, IDepartmentsService departments, ILmsAccessService access,
			ILmsSeatService seats, ILmsExternalLearnersRepository externalLearners)
		{
			_externalLearners = externalLearners;
			_records = records;
			_categories = categories;
			_templates = templates;
			_certifications = certifications;
			_certificationRecords = certificationRecords;
			_departments = departments;
			_access = access;
			_seats = seats;
		}

		/// <summary>The members licensed to be trained through LMS features; empty (everyone not licensed) when it cannot be read.</summary>
		private async Task<HashSet<string>> LicensedAsync(int departmentId)
		{
			try
			{
				return await _seats.GetLicensedUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"LMS seats could not be read for DepartmentId: {departmentId}");
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
		}

		private async Task<Dictionary<string, string>> NamesAsync(int departmentId) =>
			(await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => n != null && !string.IsNullOrWhiteSpace(n.UserId))
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

		private async Task<List<LearningRecord>> VerifiedAsync(int departmentId, IEnumerable<int> categoryIds, DateTime? fromUtc, DateTime? toUtc, IEnumerable<string> userIds = null)
		{
			var ids = categoryIds?.ToList();
			if (ids == null || ids.Count == 0)
				return new List<LearningRecord>();

			return await _records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = departmentId,
				CategoryIds = ids,
				From = fromUtc,
				To = toUtc,
				UserIds = userIds?.ToList(),
				Statuses = new List<int> { (int)LearningRecordStatuses.Verified },
				Take = 5000
			}) ?? new List<LearningRecord>();
		}

		public async Task<IsoFsrsReport> GetIsoFsrsReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, bool includeFormerMembers)
		{
			var report = new IsoFsrsReport { From = fromUtc, To = toUtc };
			report.Categories = (await _categories.GetForDepartmentAsync(departmentId, false) ?? new List<TrainingCategory>())
				.Where(c => c.Kind == (int)TrainingCategoryKinds.IsoFsrs).OrderBy(c => c.SortOrder).ToList();
			if (report.Categories.Count == 0)
				return report;

			var codes = report.Categories.ToDictionary(c => c.TrainingCategoryId, c => c.Code ?? c.Name);
			var records = await VerifiedAsync(departmentId, codes.Keys, fromUtc, toUtc);
			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (!includeFormerMembers)
				records = records.Where(r => active.Contains(r.UserId ?? string.Empty)).ToList();

			var names = await NamesAsync(departmentId);
			var licensed = await LicensedAsync(departmentId);
			foreach (var member in records.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase))
			{
				var row = new IsoFsrsReportRow
				{
					UserId = member.Key,
					Name = names.TryGetValue(member.Key, out var name) ? name : member.Key,
					NotLicensed = !licensed.Contains(member.Key ?? string.Empty)
				};
				foreach (var byCategory in member.GroupBy(r => r.CategoryId.Value))
					row.HoursByCode[codes[byCategory.Key]] = byCategory.Sum(r => r.Hours);
				report.Rows.Add(row);
			}

			report.Rows = report.Rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
			return report;
		}

		public async Task<NccpProgressReport> GetNccpProgressAsync(int departmentId, bool includeFormerMembers, DateTime utcNow)
		{
			var report = new NccpProgressReport();
			var categories = (await _categories.GetForDepartmentAsync(departmentId, false) ?? new List<TrainingCategory>()).ToList();
			var national = categories.FirstOrDefault(c => string.Equals(c.Code, TrainingCategoryTemplateCatalog.NccpNationalCode, StringComparison.OrdinalIgnoreCase));
			var local = categories.FirstOrDefault(c => string.Equals(c.Code, TrainingCategoryTemplateCatalog.NccpLocalCode, StringComparison.OrdinalIgnoreCase));
			var individual = categories.FirstOrDefault(c => string.Equals(c.Code, TrainingCategoryTemplateCatalog.NccpIndividualCode, StringComparison.OrdinalIgnoreCase));
			report.CategoriesApplied = national != null && local != null && individual != null;
			if (!report.CategoriesApplied)
				return report;

			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				.Where(t => !t.IsDeleted && !t.IsUnitScoped)
				.Select(t => (Type: t, Level: TrainingCategoryTemplateCatalog.NccpLevelFor(t.Code) ?? TrainingCategoryTemplateCatalog.NccpLevelFor(t.Type)))
				.Where(t => t.Level != null)
				.ToList();
			if (types.Count == 0)
				return report;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var names = await NamesAsync(departmentId);
			var licensed = await LicensedAsync(departmentId);
			var holders = (await _certificationRecords.GetByTypeIdsAsync(departmentId, types.Select(t => t.Type.DepartmentCertificationTypeId)) ?? Enumerable.Empty<PersonnelCertification>())
				.Where(h => !h.IsDeleted && (includeFormerMembers || active.Contains(h.UserId ?? string.Empty)))
				.ToList();

			foreach (var member in holders.GroupBy(h => (h.UserId ?? string.Empty).ToUpperInvariant()))
			{
				foreach (var (type, level) in types)
				{
					var cycle = LearningRecordRules.FindCycle(member, type.DepartmentCertificationTypeId, utcNow)
						?? member.Where(h => h.DepartmentCertificationTypeId == type.DepartmentCertificationTypeId).OrderByDescending(h => h.RecievedOn ?? DateTime.MinValue).FirstOrDefault();
					if (cycle == null)
						continue;

					var records = await VerifiedAsync(departmentId, new[] { national.TrainingCategoryId, local.TrainingCategoryId, individual.TrainingCategoryId },
						cycle.RecievedOn?.Date, cycle.ExpiresOn?.Date.AddDays(1), new[] { cycle.UserId });
					report.Rows.Add(new NccpProgressRow
					{
						UserId = cycle.UserId,
						Name = names.TryGetValue(cycle.UserId ?? string.Empty, out var name) ? name : cycle.UserId,
						CertificationName = type.Type,
						PersonnelCertificationId = cycle.PersonnelCertificationId,
						CycleStart = cycle.RecievedOn,
						CycleEnd = cycle.ExpiresOn,
						Target = level,
						National = records.Where(r => r.CategoryId == national.TrainingCategoryId).Sum(r => r.Hours),
						Local = records.Where(r => r.CategoryId == local.TrainingCategoryId).Sum(r => r.Hours),
						Individual = records.Where(r => r.CategoryId == individual.TrainingCategoryId).Sum(r => r.Hours),
						NotLicensed = !licensed.Contains(cycle.UserId ?? string.Empty)
					});
				}
			}

			report.Rows = report.Rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.CertificationName).ToList();
			return report;
		}

		public static string NewCode()
		{
			var bytes = RandomNumberGenerator.GetBytes(CodeLength);
			return new string(bytes.Select(b => CodeAlphabet[b % CodeAlphabet.Length]).ToArray());
		}

		public async Task<string> EnsureVerificationCodeAsync(int departmentId, int learningRecordId, CancellationToken cancellationToken = default)
		{
			var record = await _records.GetByIdForDepartmentAsync(departmentId, learningRecordId);
			if (record == null || !record.Counts)
				return null;
			if (!string.IsNullOrWhiteSpace(record.VerificationCode))
				return record.VerificationCode;

			for (var attempt = 0; attempt < 5; attempt++)
			{
				record.VerificationCode = NewCode();
				try
				{
					await _records.UpdateColumnsAsync(record, new[] { "VerificationCode" }, cancellationToken);
					return record.VerificationCode;
				}
				catch (Exception ex) when (attempt < 4)
				{
					// A code collision (unique index) is astronomically rare; try another.
					Logging.LogException(ex, $"Verification code for learning record {learningRecordId} was not stored; retrying");
				}
			}

			return null;
		}

		public async Task<CertificateVerification> VerifyCertificateAsync(string verificationCode)
		{
			var code = (verificationCode ?? string.Empty).Trim().ToUpperInvariant();
			if (code.Length != CodeLength || code.Any(c => CodeAlphabet.IndexOf(c) < 0))
				return null;

			var record = await _records.GetByVerificationCodeAsync(code);
			if (record == null)
				return null;

			// Lapse rule 9: an issued certificate keeps verifying after the add-on ends; only the operator flag hides the page.
			if (!await _access.IsFlagEnabledAsync(record.DepartmentId))
				return null;

			var department = await _departments.GetDepartmentByIdAsync(record.DepartmentId, false);
			var names = await NamesAsync(record.DepartmentId);
			if (!names.TryGetValue(record.UserId ?? string.Empty, out var name) && Resgrid.Model.Lms.LmsExternalLearnerIds.IsExternal(record.UserId))
			{
				// An external learner's certificate (T5) names the learner; an erased learner's shows no name.
				var learner = await _externalLearners.GetByLearnerUserIdAsync(record.DepartmentId, record.UserId);
				name = string.IsNullOrWhiteSpace(learner?.Name) ? null : learner.Name;
			}
			return new CertificateVerification
			{
				MemberName = name,
				Course = record.Title,
				CompletedOn = record.CompletedOn,
				DepartmentName = department?.Name,
				Status = (LearningRecordStatuses)record.Status
			};
		}

		public async Task<List<LmsCertificateTemplate>> GetCertificateTemplatesAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsCertificateTemplate>() : await _templates.GetForDepartmentAsync(departmentId) ?? new List<LmsCertificateTemplate>();

		public async Task<LmsCertificateTemplate> SaveCertificateTemplateAsync(LmsCertificateTemplate template, string userId, CancellationToken cancellationToken = default)
		{
			if (template == null || template.DepartmentId <= 0 || string.IsNullOrWhiteSpace(template.Name))
				throw new InvalidOperationException("learning_record_template_invalid");

			var all = await GetCertificateTemplatesAsync(template.DepartmentId);
			var now = DateTime.UtcNow;
			LmsCertificateTemplate stored;
			if (template.LmsCertificateTemplateId > 0)
			{
				stored = all.FirstOrDefault(t => t.LmsCertificateTemplateId == template.LmsCertificateTemplateId);
				if (stored == null)
					throw new InvalidOperationException("learning_record_template_invalid");
				stored.UpdatedOn = now;
			}
			else
			{
				stored = new LmsCertificateTemplate { DepartmentId = template.DepartmentId, CreatedOn = now, CreatedByUserId = userId };
			}

			stored.Name = LearningRecordRules.Trim(template.Name, 150);
			stored.Body = StringHelpers.SanitizeHtmlForStorage(template.Body);
			stored.IsDefault = template.IsDefault;
			var saved = await _templates.SaveOrUpdateAsync(stored, cancellationToken, true);

			if (saved.IsDefault)
				foreach (var other in all.Where(t => t.IsDefault && t.LmsCertificateTemplateId != saved.LmsCertificateTemplateId))
				{
					other.IsDefault = false;
					await _templates.SaveOrUpdateAsync(other, cancellationToken, true);
				}

			return saved;
		}

		public async Task<bool> DeleteCertificateTemplateAsync(int departmentId, int lmsCertificateTemplateId, CancellationToken cancellationToken = default)
		{
			var stored = (await GetCertificateTemplatesAsync(departmentId)).FirstOrDefault(t => t.LmsCertificateTemplateId == lmsCertificateTemplateId);
			return stored != null && await _templates.DeleteAsync(stored, cancellationToken);
		}
	}
}
