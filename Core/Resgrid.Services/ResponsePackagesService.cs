using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ResponsePackagesService : IResponsePackagesService
	{
		private const string PackagesCacheKey = "ResponsePackagesForDep_{0}";
		private static readonly TimeSpan CacheLength = TimeSpan.FromDays(7);

		public const int MaximumRowsPerPackage = 30;
		public const int MaximumRequiredCount = 25;

		private readonly IResponsePackagesRepository _responsePackagesRepository;
		private readonly IResponsePackageRowsRepository _responsePackageRowsRepository;
		private readonly IRunCardTargetsRepository _runCardTargetsRepository;
		private readonly IRunCardsService _runCardsService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IUnitOfWork _unitOfWork;

		public ResponsePackagesService(IResponsePackagesRepository responsePackagesRepository, IResponsePackageRowsRepository responsePackageRowsRepository,
			IRunCardTargetsRepository runCardTargetsRepository, IRunCardsService runCardsService, IUnitsService unitsService,
			IPersonnelRolesService personnelRolesService, IDepartmentSettingsService departmentSettingsService, ICacheProvider cacheProvider,
			IUnitOfWork unitOfWork)
		{
			_responsePackagesRepository = responsePackagesRepository;
			_responsePackageRowsRepository = responsePackageRowsRepository;
			_runCardTargetsRepository = runCardTargetsRepository;
			_runCardsService = runCardsService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
			_cacheProvider = cacheProvider;
			_unitOfWork = unitOfWork;
		}

		public async Task<List<ResponsePackage>> GetPackagesForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<ResponsePackage>> getPackages()
			{
				var packages = (await _responsePackagesRepository.GetAllByDepartmentIdAsync(departmentId))?.ToList() ?? new List<ResponsePackage>();

				if (!packages.Any())
					return packages;

				var rows = (await _responsePackageRowsRepository.GetRowsByDepartmentIdAsync(departmentId))?.ToList() ?? new List<ResponsePackageRow>();
				var targets = (await _runCardTargetsRepository.GetAllByDepartmentIdAsync(departmentId))?
					.Where(t => t.OwnerKind == (int)RunCardTargetOwners.PackageRow).ToList() ?? new List<RunCardTarget>();

				foreach (var row in rows)
					row.Targets = targets.Where(t => t.OwnerId == row.ResponsePackageRowId).OrderBy(t => t.RunCardTargetId).ToList();

				foreach (var package in packages)
					package.Rows = rows.Where(r => r.ResponsePackageId == package.ResponsePackageId).OrderBy(r => r.SortOrder).ThenBy(r => r.ResponsePackageRowId).ToList();

				return packages.OrderBy(p => p.IsRetired).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(string.Format(PackagesCacheKey, departmentId), getPackages, CacheLength) ?? new List<ResponsePackage>();

			return await getPackages();
		}

		public async Task<ResponsePackage> GetPackageByIdAsync(int departmentId, int responsePackageId, bool bypassCache = false)
		{
			var packages = await GetPackagesForDepartmentAsync(departmentId, bypassCache);
			return packages.FirstOrDefault(p => p.ResponsePackageId == responsePackageId);
		}

		public async Task<ResponsePackage> SavePackageAsync(ResponsePackage package, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (package == null)
				throw new ArgumentNullException(nameof(package));

			var departmentId = package.DepartmentId;
			var existingPackages = await GetPackagesForDepartmentAsync(departmentId, true);
			var isNew = package.ResponsePackageId == 0;
			ResponsePackage stored = null;

			if (!isNew)
			{
				stored = existingPackages.FirstOrDefault(p => p.ResponsePackageId == package.ResponsePackageId);

				if (stored == null)
					throw new ArgumentException("The response package does not belong to this department.", nameof(package));
			}

			var rows = (package.Rows ?? new List<ResponsePackageRow>()).Where(r => r != null).ToList();
			await ValidatePackageAsync(package, rows, stored, existingPackages);

			var now = DateTime.UtcNow;
			package.Name = package.Name.Trim();

			if (isNew)
			{
				package.AddedOn = now;
				package.AddedByUserId = userId;

				foreach (var row in rows)
					row.ResponsePackageRowId = 0;
			}
			else
			{
				package.AddedOn = stored.AddedOn;
				package.AddedByUserId = stored.AddedByUserId;
				package.UpdatedOn = now;
				package.UpdatedByUserId = userId;
			}

			var existingTargets = isNew
				? new List<RunCardTarget>()
				: ((await _runCardTargetsRepository.GetAllByDepartmentIdAsync(departmentId))?.Where(t => t.OwnerKind == (int)RunCardTargetOwners.PackageRow).ToList() ?? new List<RunCardTarget>());

			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _responsePackagesRepository.SaveOrUpdateAsync(package, cancellationToken, true);

				var storedRows = stored?.Rows ?? new List<ResponsePackageRow>();

				foreach (var removed in storedRows.Where(s => rows.All(r => r.ResponsePackageRowId != s.ResponsePackageRowId)))
				{
					foreach (var target in existingTargets.Where(t => t.OwnerId == removed.ResponsePackageRowId))
						await _runCardTargetsRepository.DeleteAsync(target, cancellationToken);

					await _responsePackageRowsRepository.DeleteAsync(removed, cancellationToken);
				}

				var sort = 0;
				foreach (var row in rows)
				{
					var targets = (row.Targets ?? new List<RunCardTarget>()).Where(t => t != null).ToList();

					row.ResponsePackageId = package.ResponsePackageId;
					row.SortOrder = sort++;
					await _responsePackageRowsRepository.SaveOrUpdateAsync(row, cancellationToken, true);

					// Targets carry no meaning of their own, so a row's set is rewritten whole.
					foreach (var old in existingTargets.Where(t => t.OwnerId == row.ResponsePackageRowId))
						await _runCardTargetsRepository.DeleteAsync(old, cancellationToken);

					var written = new List<RunCardTarget>();
					foreach (var target in targets)
					{
						var copy = new RunCardTarget
						{
							DepartmentId = departmentId,
							OwnerKind = (int)RunCardTargetOwners.PackageRow,
							OwnerId = row.ResponsePackageRowId,
							TargetKind = target.TargetKind,
							TargetRefId = target.TargetKind == (int)RunCardTargetKinds.AnyUnit || target.TargetKind == (int)RunCardTargetKinds.AnyPerson ? null : target.TargetRefId
						};

						written.Add(await _runCardTargetsRepository.SaveOrUpdateAsync(copy, cancellationToken, true));
					}

					row.Targets = written;
				}

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			package.Rows = rows;
			await InvalidatePackagesCacheAsync(departmentId);

			return package;
		}

		private async Task ValidatePackageAsync(ResponsePackage package, List<ResponsePackageRow> rows, ResponsePackage stored, List<ResponsePackage> existingPackages)
		{
			var errors = new List<string>();

			if (string.IsNullOrWhiteSpace(package.Name))
				errors.Add("A response package needs a name.");
			else if (package.Name.Trim().Length > 100)
				errors.Add("A response package name can be at most 100 characters.");
			else if (existingPackages.Any(p => p.ResponsePackageId != package.ResponsePackageId && string.Equals(p.Name?.Trim(), package.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
				errors.Add("Another response package already has this name.");

			if (!string.IsNullOrEmpty(package.Description) && package.Description.Length > 500)
				errors.Add("A response package description can be at most 500 characters.");

			if (!rows.Any())
				errors.Add("A response package needs at least one row.");

			if (rows.Count > MaximumRowsPerPackage)
				errors.Add($"A response package can have at most {MaximumRowsPerPackage} rows.");

			if (stored != null)
			{
				var storedRowIds = new HashSet<int>((stored.Rows ?? new List<ResponsePackageRow>()).Select(r => r.ResponsePackageRowId));
				if (rows.Any(r => r.ResponsePackageRowId != 0 && !storedRowIds.Contains(r.ResponsePackageRowId)))
					errors.Add("A row does not belong to this response package.");
			}

			var unitTypeIds = new HashSet<int>((await _unitsService.GetUnitTypesForDepartmentAsync(package.DepartmentId) ?? new List<UnitType>()).Select(t => t.UnitTypeId));
			var roleIds = new HashSet<int>((await _personnelRolesService.GetRolesForDepartmentAsync(package.DepartmentId) ?? new List<PersonnelRole>()).Select(r => r.PersonnelRoleId));
			var codes = await _departmentSettingsService.GetDispatchResponseCodesAsync(package.DepartmentId);
			var packageIds = new HashSet<int>(existingPackages.Select(p => p.ResponsePackageId));

			var index = 0;
			foreach (var row in rows)
			{
				index++;
				var name = string.IsNullOrWhiteSpace(row.Label) ? $"Row {index}" : $"'{row.Label.Trim()}'";

				if (!string.IsNullOrEmpty(row.Label) && row.Label.Trim().Length > 100)
					errors.Add($"{name}: the label can be at most 100 characters.");

				if (row.FillMode != (int)RequirementFillModes.Count && row.FillMode != (int)RequirementFillModes.EnsureFirstArriving)
					errors.Add($"{name}: unknown fill mode.");

				if (row.FillMode == (int)RequirementFillModes.EnsureFirstArriving)
				{
					row.RequiredCount = 1;

					if (row.SubstitutePackageId.HasValue)
						errors.Add($"{name}: an \"ensure first-arriving\" row only adds a resource when one is closer; it cannot have a substitute.");
				}
				else if (row.RequiredCount < 1 || row.RequiredCount > MaximumRequiredCount)
					errors.Add($"{name}: the count must be between 1 and {MaximumRequiredCount}.");

				if (!Enum.IsDefined(typeof(RunCardStatusFilters), row.StatusFilter))
					errors.Add($"{name}: unknown status filter.");
				else if (row.StatusFilter == (int)RunCardStatusFilters.BaseTypes && !RunCardDecisionGraph.ParseBaseTypes(row.StatusBaseTypes).Any())
					errors.Add($"{name}: choose at least one status meaning.");

				if (row.ResponseCodeId.HasValue && codes.Find(row.ResponseCodeId) == null)
					errors.Add($"{name}: the response code is not in the department's code list.");

				if (row.SubstitutePackageId.HasValue)
				{
					if (row.SubstitutePackageId.Value == package.ResponsePackageId && package.ResponsePackageId != 0)
						errors.Add($"{name}: a package cannot substitute for itself.");
					else if (!packageIds.Contains(row.SubstitutePackageId.Value))
						errors.Add($"{name}: the substitute package does not belong to this department.");
				}

				errors.AddRange(RunCardTargetRules.Validate(row.Targets, unitTypeIds, roleIds, name));
			}

			if (errors.Any())
				throw new RunCardValidationException(errors);
		}

		public async Task<List<string>> GetPackageUsageAsync(int departmentId, int responsePackageId)
		{
			var usage = new List<string>();

			foreach (var card in await _runCardsService.GetAllRunCardsForDepartmentAsync(departmentId, true) ?? new List<RunCard>())
			{
				foreach (var level in card.AlarmLevels ?? new List<RunCardAlarmLevel>())
				{
					if (level.ResponsePackageId == responsePackageId)
						usage.Add($"{card.Name}, alarm level {level.AlarmLevel}");

					foreach (var node in (level.DecisionNodes ?? new List<RunCardDecisionNode>()).Where(n => n.ResponsePackageId == responsePackageId))
						usage.Add($"{card.Name}, alarm level {level.AlarmLevel}: {(string.IsNullOrWhiteSpace(node.Label) ? "outcome" : node.Label)}");
				}
			}

			foreach (var package in await GetPackagesForDepartmentAsync(departmentId, true))
			{
				if (package.ResponsePackageId == responsePackageId)
					continue;

				if ((package.Rows ?? new List<ResponsePackageRow>()).Any(r => r.SubstitutePackageId == responsePackageId))
					usage.Add($"Substitute in package '{package.Name}'");
			}

			return usage.Distinct().ToList();
		}

		public async Task<bool> DeletePackageAsync(int departmentId, int responsePackageId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var package = await GetPackageByIdAsync(departmentId, responsePackageId, true);

			if (package == null)
				return false;

			var usage = await GetPackageUsageAsync(departmentId, responsePackageId);
			if (usage.Any())
				throw new InvalidOperationException("The response package is still used: " + string.Join("; ", usage) + ".");

			var targets = (await _runCardTargetsRepository.GetAllByDepartmentIdAsync(departmentId))?.Where(t => t.OwnerKind == (int)RunCardTargetOwners.PackageRow).ToList()
				?? new List<RunCardTarget>();

			_unitOfWork.CreateOrGetConnection();
			try
			{
				foreach (var row in package.Rows ?? new List<ResponsePackageRow>())
				{
					foreach (var target in targets.Where(t => t.OwnerId == row.ResponsePackageRowId))
						await _runCardTargetsRepository.DeleteAsync(target, cancellationToken);

					await _responsePackageRowsRepository.DeleteAsync(row, cancellationToken);
				}

				await _responsePackagesRepository.DeleteAsync(package, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			await InvalidatePackagesCacheAsync(departmentId);
			return true;
		}

		public async Task InvalidatePackagesCacheAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(PackagesCacheKey, departmentId));
		}
	}
}
