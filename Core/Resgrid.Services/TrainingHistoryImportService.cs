using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Keys = Resgrid.Services.TrainingHistoryImportCsv.Keys;

namespace Resgrid.Services
{
	/// <summary>
	/// History import (R-C1, free): a CSV of past completions becomes Import learning records (Verified, IsBackfill, so they
	/// never auto-credit through category mappings, R-T3), with a dry run and a member-match review. Members match by user id,
	/// email, the department's identification number, then an exact unique full name; former members (deleted, disabled,
	/// hidden) match too, since history may be theirs (R-T13). A row that names a certification type also gets its
	/// certification record (found by cycle, or created) and a credit linked to the written learning record. Idempotent:
	/// each row has a stable source ref (the source system's record id, or a hash of the row's facts).
	/// </summary>
	public class TrainingHistoryImportService : ITrainingHistoryImportService
	{
		private const int RefChunkSize = 1000;
		private const int MaxCourseCodeLength = 100;
		private const int MaxCeProviderLength = 200;
		private const int MaxCeCourseNumberLength = 100;
		private const int MaxInstructorLength = 1000;
		private const int MaxExternalIdLength = 100;
		private const int MaxCreditCategoryLength = 100;
		private const int MaxCertificationNumberLength = 100;

		private readonly ILearningRecordWriter _writer;
		private readonly ILearningRecordsRepository _learningRecordsRepository;
		private readonly ILearningRecordService _learningRecordService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentMemberSensitiveDataService _memberSensitiveDataService;
		private readonly ICertificationService _certificationService;

		public TrainingHistoryImportService(ILearningRecordWriter writer, ILearningRecordsRepository learningRecordsRepository,
			ILearningRecordService learningRecordService, IDepartmentsService departmentsService, IUserProfileService userProfileService,
			IDepartmentMemberSensitiveDataService memberSensitiveDataService, ICertificationService certificationService)
		{
			_writer = writer;
			_learningRecordsRepository = learningRecordsRepository;
			_learningRecordService = learningRecordService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_memberSensitiveDataService = memberSensitiveDataService;
			_certificationService = certificationService;
		}

		public byte[] Template() => TrainingHistoryImportCsv.Template();

		public async Task<TrainingHistoryImportResult> DryRunAsync(int departmentId, byte[] file, IReadOnlyDictionary<int, string> memberOverrides)
		{
			var (result, _) = await AnalyzeAsync(departmentId, file, memberOverrides);
			result.DryRun = true;
			return result;
		}

		public async Task<TrainingHistoryImportResult> ImportAsync(int departmentId, byte[] file, IReadOnlyDictionary<int, string> memberOverrides, string userId,
			CancellationToken cancellationToken = default)
		{
			var (result, context) = await AnalyzeAsync(departmentId, file, memberOverrides);
			result.DryRun = false;
			if (!string.IsNullOrWhiteSpace(result.FileError) || context == null)
				return result;

			foreach (var row in result.Rows)
			{
				// Only between rows: a row's record and its certification credit are written together.
				cancellationToken.ThrowIfCancellationRequested();

				if (!row.IsImportable || !context.SourceRefs.TryGetValue(row.RowNumber, out var sourceRefId))
				{
					result.RecordsSkipped++;
					continue;
				}

				LearningRecordWriteResult write;
				try
				{
					write = await _writer.WriteSourceAsync(departmentId, LearningRecordSourceKinds.Import, sourceRefId, new[] { Draft(row, userId) }, userId,
						cancellationToken);
					var written = write?.Written ?? 0;
					result.RecordsWritten += written;
					if (written == 0)
						result.RecordsSkipped++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Training history import: row {row.RowNumber} of department {departmentId} could not be written.");
					result.RecordsSkipped++;
					continue;
				}

				if (!row.MatchedCertificationTypeId.HasValue)
					continue;

				try
				{
					await CertificationAsync(departmentId, row, sourceRefId, write, context, result, userId, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Training history import: the certification credit of row {row.RowNumber} of department {departmentId} could not be written.");
				}
			}

			return result;
		}

		/// <summary>
		/// The row's idempotency key: "ext:" and the source system's record id when the file has one, else "row:" and the first
		/// 40 hex characters of SHA-256 over the member, course, date, hours and course code (the same row always gives the same key).
		/// </summary>
		public static string SourceRefIdFor(string userId, TrainingHistoryImportRow row)
		{
			if (row == null)
				return null;

			var recordId = row.RecordId?.Trim();
			if (!string.IsNullOrEmpty(recordId))
				return "ext:" + (recordId.Length > MaxExternalIdLength ? recordId.Substring(0, MaxExternalIdLength) : recordId);

			var facts = string.Join("|",
				(userId ?? string.Empty).Trim().ToLowerInvariant(),
				(row.Course ?? string.Empty).Trim().ToLowerInvariant(),
				row.CompletedOn.HasValue ? row.CompletedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
				(row.Hours ?? 0m).ToString("0.##", CultureInfo.InvariantCulture),
				(row.CourseCode ?? string.Empty).Trim().ToLowerInvariant());

			using var sha = SHA256.Create();
			var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(facts));
			var hex = new StringBuilder(hash.Length * 2);
			foreach (var b in hash)
				hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			return "row:" + hex.ToString(0, 40);
		}

		#region Analysis

		private sealed class MemberEntry
		{
			public string UserId { get; set; }
			public string Name { get; set; }
			public string Email { get; set; }
			public string ExternalId { get; set; }
		}

		private sealed class ImportContext
		{
			public Dictionary<string, MemberEntry> MembersById { get; } = new Dictionary<string, MemberEntry>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, List<MemberEntry>> ByEmail { get; } = new Dictionary<string, List<MemberEntry>>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, List<MemberEntry>> ByExternalId { get; } = new Dictionary<string, List<MemberEntry>>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, List<MemberEntry>> ByName { get; } = new Dictionary<string, List<MemberEntry>>(StringComparer.OrdinalIgnoreCase);
			public List<TrainingCategory> Categories { get; set; } = new List<TrainingCategory>();
			public Dictionary<int, DepartmentCertificationType> CertificationTypes { get; } = new Dictionary<int, DepartmentCertificationType>();
			public Dictionary<int, string> SourceRefs { get; } = new Dictionary<int, string>();
			public Dictionary<string, List<PersonnelCertification>> CertificationsByUser { get; } = new Dictionary<string, List<PersonnelCertification>>(StringComparer.OrdinalIgnoreCase);
		}

		private async Task<(TrainingHistoryImportResult Result, ImportContext Context)> AnalyzeAsync(int departmentId, byte[] file,
			IReadOnlyDictionary<int, string> memberOverrides)
		{
			var parse = TrainingHistoryImportCsv.Parse(file, CultureInfo.CurrentCulture);
			var result = new TrainingHistoryImportResult
			{
				DryRun = true,
				FileError = parse.FileError,
				IgnoredColumns = parse.IgnoredColumns ?? new List<string>()
			};
			if (!string.IsNullOrWhiteSpace(parse.FileError))
				return (result, null);

			result.Rows = parse.Rows;
			var context = await LoadContextAsync(departmentId, result.Rows.Any(r => !string.IsNullOrWhiteSpace(r.CertificationType)));
			var latest = DateTime.UtcNow.AddDays(1);

			foreach (var row in result.Rows)
			{
				if (row.Errors.Contains(Keys.Unreadable))
					continue;

				MatchMember(row, context, memberOverrides);
				if (row.CompletedOn.HasValue && row.CompletedOn.Value > latest)
					AddOnce(row.Errors, Keys.FutureDate);
				MatchCategory(row, context);
				MatchCertificationType(row, context);
				if (!string.IsNullOrWhiteSpace(row.Method) && TrainingHistoryImportCsv.MethodFor(row.Method) == null)
					AddOnce(row.Warnings, Keys.UnknownMethod);
			}

			// The same source ref twice in one file: the second row would overwrite (and void) the first, so it is held back.
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var row in result.Rows.Where(r => r.Errors.Count == 0 && !string.IsNullOrWhiteSpace(r.MatchedUserId)))
			{
				var sourceRefId = SourceRefIdFor(row.MatchedUserId, row);
				context.SourceRefs[row.RowNumber] = sourceRefId;
				if (!seen.Add(sourceRefId))
				{
					row.AlreadyImported = true;
					AddOnce(row.Warnings, Keys.DuplicateRow);
				}
			}

			var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var refs = context.SourceRefs.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			for (var i = 0; i < refs.Count; i += RefChunkSize)
			{
				var found = await _learningRecordsRepository.GetExistingSourceRefIdsAsync(departmentId, (int)LearningRecordSourceKinds.Import,
					refs.Skip(i).Take(RefChunkSize).ToList());
				if (found != null)
					existing.UnionWith(found.Where(f => !string.IsNullOrWhiteSpace(f)));
			}

			foreach (var row in result.Rows)
			{
				if (context.SourceRefs.TryGetValue(row.RowNumber, out var sourceRefId) && existing.Contains(sourceRefId))
					row.AlreadyImported = true;
			}

			return (result, context);
		}

		/// <summary>Every member the department has had (deleted, disabled and hidden included), with names, emails and id numbers.</summary>
		private async Task<ImportContext> LoadContextAsync(int departmentId, bool needCertificationTypes)
		{
			var context = new ImportContext();

			var memberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var member in await _departmentsService.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
			{
				if (member != null && member.DepartmentId == departmentId && !string.IsNullOrWhiteSpace(member.UserId))
					memberIds.Add(member.UserId.Trim());
			}

			var profiles = new Dictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);
			foreach (var profile in (await _userProfileService.GetAllProfilesForDepartmentIncDisabledDeletedAsync(departmentId) ?? new Dictionary<string, UserProfile>()).Values)
			{
				if (profile == null || string.IsNullOrWhiteSpace(profile.UserId))
					continue;
				profiles[profile.UserId.Trim()] = profile;
				memberIds.Add(profile.UserId.Trim());
			}

			IReadOnlyDictionary<string, DepartmentMemberSensitiveData> sensitive = null;
			try
			{
				// Department-issued id numbers live per department (ADP 5.1); a protected department without a grant answers the
				// placeholder, which never matches.
				sensitive = await _memberSensitiveDataService.GetResolvedForDepartmentAsync(departmentId, null, null);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training history import: identification numbers of department {departmentId} could not be read; external ids will not match.");
			}

			foreach (var userId in memberIds)
			{
				profiles.TryGetValue(userId, out var profile);
				var entry = new MemberEntry
				{
					UserId = userId,
					Name = FullName(profile?.FirstName, profile?.LastName),
					Email = (profile?.MembershipEmail ?? profile?.User?.Email)?.Trim()
				};
				if (sensitive != null && sensitive.TryGetValue(userId, out var data) && IsUsableIdentifier(data?.IdentificationNumber))
					entry.ExternalId = data.IdentificationNumber.Trim();

				context.MembersById[userId] = entry;
				Index(context.ByEmail, entry.Email, entry);
				Index(context.ByExternalId, entry.ExternalId, entry);
				Index(context.ByName, NameKey(entry.Name), entry);
				Index(context.ByName, NameKey(FullName(profile?.LastName, profile?.FirstName, ", ")), entry);
			}

			context.Categories = (await _learningRecordService.GetCategoriesAsync(departmentId) ?? new List<TrainingCategory>())
				.Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted).ToList();

			if (needCertificationTypes)
			{
				foreach (var type in await _certificationService.GetAllCertificationTypesByDepartmentAsync(departmentId) ?? new List<DepartmentCertificationType>())
				{
					if (type != null && type.DepartmentId == departmentId && !type.IsDeleted && !type.IsUnitScoped)
						context.CertificationTypes[type.DepartmentCertificationTypeId] = type;
				}
			}

			return context;
		}

		private static void MatchMember(TrainingHistoryImportRow row, ImportContext context, IReadOnlyDictionary<int, string> overrides)
		{
			row.MatchedUserId = null;
			row.MatchedName = null;
			row.MatchMethod = TrainingImportMatchMethods.None;

			MemberEntry match = null;
			var method = TrainingImportMatchMethods.None;

			if (overrides != null && overrides.TryGetValue(row.RowNumber, out var picked) && !string.IsNullOrWhiteSpace(picked)
				&& context.MembersById.TryGetValue(picked.Trim(), out var chosen))
			{
				match = chosen;
				method = TrainingImportMatchMethods.Manual;
			}

			if (match == null && !string.IsNullOrWhiteSpace(row.MemberId) && context.MembersById.TryGetValue(row.MemberId.Trim(), out var byId))
			{
				match = byId;
				method = TrainingImportMatchMethods.MemberId;
			}

			if (match == null && (match = Unique(context.ByEmail, row.Email?.Trim())) != null)
				method = TrainingImportMatchMethods.Email;

			if (match == null && (match = Unique(context.ByExternalId, row.MemberExternalId?.Trim())) != null)
				method = TrainingImportMatchMethods.ExternalId;

			if (match == null && (match = Unique(context.ByName, NameKey(row.MemberName))) != null)
				method = TrainingImportMatchMethods.Name;

			if (match == null)
			{
				AddOnce(row.Errors, Keys.MemberNotFound);
				return;
			}

			row.MatchedUserId = match.UserId;
			row.MatchedName = !string.IsNullOrWhiteSpace(match.Name) ? match.Name : !string.IsNullOrWhiteSpace(match.Email) ? match.Email : match.UserId;
			row.MatchMethod = method;
			if (method == TrainingImportMatchMethods.Name)
				AddOnce(row.Warnings, Keys.NameMatch);
		}

		private static void MatchCategory(TrainingHistoryImportRow row, ImportContext context)
		{
			row.MatchedCategoryId = null;
			var text = row.Category?.Trim();
			if (string.IsNullOrEmpty(text))
				return;

			var category = context.Categories.FirstOrDefault(c => string.Equals(c.Code?.Trim(), text, StringComparison.OrdinalIgnoreCase))
				?? context.Categories.FirstOrDefault(c => string.Equals(c.Name?.Trim(), text, StringComparison.OrdinalIgnoreCase));
			if (category == null)
			{
				AddOnce(row.Warnings, Keys.UnknownCategory);
				return;
			}

			row.MatchedCategoryId = category.TrainingCategoryId;
		}

		private static void MatchCertificationType(TrainingHistoryImportRow row, ImportContext context)
		{
			row.MatchedCertificationTypeId = null;
			var text = row.CertificationType?.Trim();
			if (string.IsNullOrEmpty(text))
				return;

			var types = context.CertificationTypes.Values.OrderBy(t => t.DepartmentCertificationTypeId).ToList();
			var type = types.FirstOrDefault(t => string.Equals(t.Code?.Trim(), text, StringComparison.OrdinalIgnoreCase))
				?? types.FirstOrDefault(t => string.Equals(t.Type?.Trim(), text, StringComparison.OrdinalIgnoreCase));
			if (type == null)
			{
				AddOnce(row.Warnings, Keys.UnknownCertification);
				return;
			}

			row.MatchedCertificationTypeId = type.DepartmentCertificationTypeId;
		}

		#endregion Analysis

		#region Writing

		private static LearningRecordDraft Draft(TrainingHistoryImportRow row, string actingUserId)
		{
			var method = TrainingHistoryImportCsv.MethodFor(row.Method);
			return new LearningRecordDraft
			{
				UserId = row.MatchedUserId,
				Title = Cut(row.Course?.Trim(), LearningRecord.MaxTitleLength),
				CourseCode = Cut(row.CourseCode?.Trim(), MaxCourseCodeLength),
				CompletedOn = DateTime.SpecifyKind(row.CompletedOn.Value, DateTimeKind.Utc),
				Hours = row.Hours ?? 0m,
				CategoryId = row.MatchedCategoryId,
				MethodOfTraining = method.HasValue ? (int)method.Value : (int?)null,
				CeProvider = Cut(row.CeProvider?.Trim(), MaxCeProviderLength),
				CeCourseNumber = Cut(row.CeCourseNumber?.Trim(), MaxCeCourseNumberLength),
				CeHours = row.CeHours,
				InstructorNames = Cut(row.Instructor?.Trim(), MaxInstructorLength),
				ExternalId = Cut(row.RecordId?.Trim(), MaxExternalIdLength),
				Notes = Cut(row.Notes?.Trim(), LearningRecord.MaxNotesLength),
				Status = LearningRecordStatuses.Verified,
				VerifiedByUserId = actingUserId,
				// Imported history never auto-credits through category mappings (R-T3); this import adds the credits it names itself.
				IsBackfill = true
			};
		}

		/// <summary>
		/// The member's typed certification record whose cycle contains the completion date (or one created from the row's
		/// issue date and number), and a credit for the written record unless that record already credits it.
		/// </summary>
		private async Task CertificationAsync(int departmentId, TrainingHistoryImportRow row, string sourceRefId, LearningRecordWriteResult write,
			ImportContext context, TrainingHistoryImportResult result, string actingUserId, CancellationToken cancellationToken)
		{
			if (!context.CertificationTypes.TryGetValue(row.MatchedCertificationTypeId.Value, out var type) || !row.CompletedOn.HasValue)
				return;

			var record = await WrittenRecordAsync(departmentId, row.MatchedUserId, sourceRefId, write);
			if (record == null)
				return;

			var date = row.CompletedOn.Value;
			var certifications = await CertificationsOfAsync(departmentId, row.MatchedUserId, context);
			var ofType = certifications.Where(c => c.DepartmentCertificationTypeId == type.DepartmentCertificationTypeId).ToList();

			var certification = ofType.Where(c => Contains(c, date))
				.OrderByDescending(c => c.RecievedOn ?? DateTime.MinValue).ThenByDescending(c => c.PersonnelCertificationId).FirstOrDefault();

			if (certification == null && (row.CertificationIssuedOn.HasValue || !string.IsNullOrWhiteSpace(row.CertificationNumber)))
			{
				// An earlier row of this file (or an earlier import) may have created the same record already.
				var same = ofType.FirstOrDefault(c => SameIssue(c, row));
				if (same == null)
				{
					same = await CreateCertificationAsync(departmentId, row, type, actingUserId, cancellationToken);
					certifications.Add(same);
					result.CertificationsCreated++;
				}

				if (Contains(same, date))
					certification = same;
			}

			if (certification == null || certification.PersonnelCertificationId <= 0 || record.Hours <= 0)
				return;

			var credits = await _certificationService.GetCertificationCreditsAsync(certification.PersonnelCertificationId) ?? new List<PersonnelCertificationCredit>();
			if (credits.Any(c => c != null && c.LearningRecordId == record.LearningRecordId))
				return;

			var category = row.MatchedCategoryId.HasValue ? context.Categories.FirstOrDefault(c => c.TrainingCategoryId == row.MatchedCategoryId.Value) : null;
			await _certificationService.AddCertificationCreditAsync(new PersonnelCertificationCredit
			{
				PersonnelCertificationId = certification.PersonnelCertificationId,
				DepartmentId = departmentId,
				CreditDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
				Hours = record.Hours,
				Category = Cut(category?.Name?.Trim(), MaxCreditCategoryLength),
				Description = Cut(row.Course?.Trim(), LearningRecord.MaxTitleLength),
				LearningRecordId = record.LearningRecordId
			}, actingUserId, cancellationToken);
			result.CreditsCreated++;
		}

		/// <summary>The live record the write stored (or left unchanged) for the member; read back by source when the result has none.</summary>
		private async Task<LearningRecord> WrittenRecordAsync(int departmentId, string userId, string sourceRefId, LearningRecordWriteResult write)
		{
			bool Live(LearningRecord r) => r != null && r.LearningRecordId > 0 && !r.IsVoided && SameUser(r.UserId, userId) && (r.DepartmentId == 0 || r.DepartmentId == departmentId);

			var record = (write?.Records ?? new List<LearningRecord>()).Where(Live).OrderByDescending(r => r.LearningRecordId).FirstOrDefault();
			if (record != null)
				return record;

			return (await _learningRecordsRepository.GetBySourceAsync(departmentId, (int)LearningRecordSourceKinds.Import, sourceRefId) ?? new List<LearningRecord>())
				.Where(Live).OrderByDescending(r => r.LearningRecordId).FirstOrDefault();
		}

		private async Task<List<PersonnelCertification>> CertificationsOfAsync(int departmentId, string userId, ImportContext context)
		{
			if (context.CertificationsByUser.TryGetValue(userId, out var cached))
				return cached;

			var list = (await _certificationService.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>())
				.Where(c => c != null && c.DepartmentId == departmentId && !c.IsDeleted && SameUser(c.UserId, userId)).ToList();
			context.CertificationsByUser[userId] = list;
			return list;
		}

		/// <summary>A typed record from the row, mirroring the onboarding certification writer (already expired = imported Expired).</summary>
		private async Task<PersonnelCertification> CreateCertificationAsync(int departmentId, TrainingHistoryImportRow row, DepartmentCertificationType type,
			string actingUserId, CancellationToken cancellationToken)
		{
			var certification = new PersonnelCertification
			{
				DepartmentId = departmentId,
				UserId = row.MatchedUserId,
				DepartmentCertificationTypeId = type.DepartmentCertificationTypeId,
				Type = type.Type,
				Name = type.Type,
				Number = Cut(row.CertificationNumber?.Trim(), MaxCertificationNumberLength),
				IssuedBy = type.IssuingAuthority,
				RecievedOn = row.CertificationIssuedOn.HasValue ? DateTime.SpecifyKind(row.CertificationIssuedOn.Value.Date, DateTimeKind.Utc) : (DateTime?)null,
				ExpiresOn = !type.NeverExpires && row.CertificationExpiresOn.HasValue ? DateTime.SpecifyKind(row.CertificationExpiresOn.Value.Date, DateTimeKind.Utc) : (DateTime?)null,
				Status = (int)PersonnelCertificationStatuses.Active
			};

			// Already past its expiry: Expired now, so the expiry sweep has nothing to announce about history.
			if (certification.ExpiresOn.HasValue && certification.ExpiresOn.Value.Date < DateTime.UtcNow.Date)
			{
				certification.Status = (int)PersonnelCertificationStatuses.Expired;
				certification.StatusChangedOn = DateTime.UtcNow;
				certification.StatusChangedByUserId = actingUserId;
				certification.StatusReason = "imported-expired";
			}

			// History, not news: audited per row, no workflow triggers or notifications.
			var saved = await _certificationService.SaveCertificationAsync(certification, new ImportEffectPolicy { PerRowAudit = true }, cancellationToken);
			return saved ?? certification;
		}

		/// <summary>The completion date falls in the record's cycle (issued on or before, expires on or after; open ends allowed).</summary>
		private static bool Contains(PersonnelCertification certification, DateTime date) =>
			certification != null
			&& (!certification.RecievedOn.HasValue || certification.RecievedOn.Value.Date <= date.Date)
			&& (!certification.ExpiresOn.HasValue || date.Date <= certification.ExpiresOn.Value.Date);

		/// <summary>The row describes this record: the same issue date, or the same number when the stored number is readable.</summary>
		private static bool SameIssue(PersonnelCertification certification, TrainingHistoryImportRow row)
		{
			if (row.CertificationIssuedOn.HasValue && certification.RecievedOn.HasValue && certification.RecievedOn.Value.Date == row.CertificationIssuedOn.Value.Date)
				return true;
			var number = row.CertificationNumber?.Trim();
			return !string.IsNullOrEmpty(number) && !string.IsNullOrWhiteSpace(certification.Number)
				&& !ProtectedDataEnvelope.HasEnvelopePrefix(certification.Number)
				&& string.Equals(certification.Number.Trim(), number, StringComparison.OrdinalIgnoreCase);
		}

		#endregion Writing

		#region Helpers

		private static void Index(Dictionary<string, List<MemberEntry>> index, string key, MemberEntry entry)
		{
			if (string.IsNullOrWhiteSpace(key))
				return;
			key = key.Trim();
			if (!index.TryGetValue(key, out var list))
				index[key] = list = new List<MemberEntry>();
			if (!list.Any(e => SameUser(e.UserId, entry.UserId)))
				list.Add(entry);
		}

		/// <summary>The one member the key names; null when none or several do.</summary>
		private static MemberEntry Unique(Dictionary<string, List<MemberEntry>> index, string key)
		{
			if (string.IsNullOrWhiteSpace(key) || !index.TryGetValue(key.Trim(), out var list) || list.Count != 1)
				return null;
			return list[0];
		}

		private static string FullName(string first, string last, string separator = " ")
		{
			first = first?.Trim();
			last = last?.Trim();
			if (string.IsNullOrEmpty(first))
				return string.IsNullOrEmpty(last) ? null : last;
			return string.IsNullOrEmpty(last) ? first : first + separator + last;
		}

		/// <summary>A name compared case-insensitively with runs of spaces collapsed.</summary>
		private static string NameKey(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return null;
			return string.Join(" ", name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
		}

		private static bool IsUsableIdentifier(string value) =>
			!string.IsNullOrWhiteSpace(value) && value.Trim() != ProtectedDataEnvelope.RedactionValue && !ProtectedDataEnvelope.HasEnvelopePrefix(value);

		private static bool SameUser(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

		private static void AddOnce(List<string> list, string key)
		{
			if (!list.Contains(key))
				list.Add(key);
		}

		private static string Cut(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return null;
			return value.Length > max ? value.Substring(0, max) : value;
		}

		#endregion Helpers
	}
}
