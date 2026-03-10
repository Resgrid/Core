using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class UserDefinedFieldsService : IUserDefinedFieldsService
	{
		private readonly IUdfDefinitionRepository _definitionRepository;
		private readonly IUdfFieldRepository _fieldRepository;
		private readonly IUdfFieldValueRepository _valueRepository;
		private readonly IUnitOfWork _unitOfWork;

		public UserDefinedFieldsService(
			IUdfDefinitionRepository definitionRepository,
			IUdfFieldRepository fieldRepository,
			IUdfFieldValueRepository valueRepository,
			IUnitOfWork unitOfWork)
		{
			_definitionRepository = definitionRepository;
			_fieldRepository = fieldRepository;
			_valueRepository = valueRepository;
			_unitOfWork = unitOfWork;
		}

		public async Task<UdfDefinition> GetActiveDefinitionAsync(int departmentId, int entityType)
		{
			return await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
		}

		public async Task<List<UdfField>> GetFieldsForActiveDefinitionAsync(int departmentId, int entityType)
		{
			var definition = await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
			if (definition == null)
				return new List<UdfField>();

			var fields = await _fieldRepository.GetFieldsByDefinitionIdAsync(definition.UdfDefinitionId);
			return fields?.Where(f => f.IsEnabled).OrderBy(f => f.SortOrder).ToList() ?? new List<UdfField>();
		}

		public async Task<List<UdfField>> GetVisibleFieldsForActiveDefinitionAsync(int departmentId, int entityType,
			bool isDepartmentAdmin, bool isGroupAdmin)
		{
			var allFields = await GetFieldsForActiveDefinitionAsync(departmentId, entityType);

			return allFields.Where(f => IsFieldVisibleToRole(f, isDepartmentAdmin, isGroupAdmin)).ToList();
		}

		private static bool IsFieldVisibleToRole(UdfField field, bool isDepartmentAdmin, bool isGroupAdmin)
		{
			var visibility = (UdfFieldVisibility)field.Visibility;
			return visibility switch
			{
				UdfFieldVisibility.Everyone => true,
				UdfFieldVisibility.DepartmentAndGroupAdmins => isDepartmentAdmin || isGroupAdmin,
				UdfFieldVisibility.DepartmentAdminsOnly => isDepartmentAdmin,
				_ => true
			};
		}

		public async Task<UdfDefinition> SaveDefinitionAsync(int departmentId, int entityType,
			List<UdfField> fields, string userId, CancellationToken cancellationToken = default)
		{
			// Enforce machine-name uniqueness at the service layer so callers (web, API, workers)
			// all get the same guarantee.
			if (fields != null && fields.Count > 0)
			{
				var nameErrors = Resgrid.Model.Helpers.UdfValidationHelper.ValidateFieldNamesUnique(fields);
				if (nameErrors.Count > 0)
					throw new InvalidOperationException(string.Join(" | ", nameErrors));
			}

			// Open a shared connection/transaction so the read, deactivation, and all inserts
			// are atomic. This prevents two concurrent callers from computing the same
			// nextVersion and/or leaving duplicate active definitions.
			_unitOfWork.CreateOrGetConnection();

			UdfDefinition definition;
			try
			{
				// Determine next version number (inside the transaction to get a consistent read)
				var existing = await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
				var nextVersion = (existing?.Version ?? 0) + 1;

				// Mark all current active definitions for this dept+type as inactive
				await _definitionRepository.DeactivateDefinitionsByDepartmentAndEntityTypeAsync(departmentId, entityType, cancellationToken);

				// Create new definition version
				definition = new UdfDefinition
				{
					DepartmentId = departmentId,
					EntityType = entityType,
					Version = nextVersion,
					IsActive = true,
					CreatedOn = DateTime.UtcNow,
					CreatedBy = userId
				};

				await _definitionRepository.SaveOrUpdateAsync(definition, cancellationToken);

				// Save fields linked to the new definition version.
				// When a caller supplies an explicit UdfFieldId (e.g. tests or migrations that need
				// stable IDs), it is preserved. When the ID is absent the repository will assign a
				// fresh GUID, ensuring each definition version owns independent field records.
				if (fields != null)
				{
					for (int i = 0; i < fields.Count; i++)
					{
						var field = fields[i];
						// Only clear the ID when it was not explicitly provided.
						if (string.IsNullOrEmpty(field.UdfFieldId))
							field.UdfFieldId = null; // let RepositoryBase/mock assign a new GUID
						field.UdfDefinitionId = definition.UdfDefinitionId;
						field.SortOrder = i;
						await _fieldRepository.SaveOrUpdateAsync(field, cancellationToken);
					}
				}

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			return definition;
		}

		public async Task<List<UdfFieldValue>> GetFieldValuesForEntityAsync(int departmentId, int entityType, string entityId)
		{
			var definition = await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
			if (definition == null)
				return new List<UdfFieldValue>();

			var values = await _valueRepository.GetFieldValuesByEntityAsync(entityType, entityId, definition.UdfDefinitionId);
			return values?.ToList() ?? new List<UdfFieldValue>();
		}

		public async Task<List<UdfFieldValue>> GetFieldValuesForEntitiesAsync(int departmentId, int entityType, IEnumerable<string> entityIds)
		{
			var idList = entityIds?.ToList() ?? new List<string>();
			if (idList.Count == 0)
				return new List<UdfFieldValue>();

			var definition = await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
			if (definition == null)
				return new List<UdfFieldValue>();

			var values = await _valueRepository.GetFieldValuesByEntitiesAsync(entityType, idList, definition.UdfDefinitionId);
			return values?.ToList() ?? new List<UdfFieldValue>();
		}

		public async Task<Dictionary<string, List<string>>> SaveFieldValuesForEntityAsync(int departmentId, int entityType,
			string entityId, List<UdfFieldValue> values, string userId, CancellationToken cancellationToken = default)
		{
			var definition = await _definitionRepository.GetActiveDefinitionByDepartmentAndEntityTypeAsync(departmentId, entityType);
			if (definition == null)
				return new Dictionary<string, List<string>>();

			var fields = (await _fieldRepository.GetFieldsByDefinitionIdAsync(definition.UdfDefinitionId))
				?.Where(f => f.IsEnabled).ToList() ?? new List<UdfField>();

			// Validate all values
			var errors = UdfValidationHelper.ValidateAllFieldValues(fields, values);
			if (errors.Count > 0)
				return errors;

			// Delete existing values for this entity + definition version, then re-insert.
			// Wrap both operations in a single transaction so a failed insert cannot leave
			// the entity with no field values (partial data loss).
			_unitOfWork.CreateOrGetConnection();

			try
			{
				await _valueRepository.DeleteFieldValuesByEntityAndDefinitionAsync(
					entityType, entityId, definition.UdfDefinitionId, cancellationToken);

				var now = DateTime.UtcNow;
				foreach (var value in values ?? Enumerable.Empty<UdfFieldValue>())
				{
					value.UdfFieldValueId = null;             // let RepositoryBase assign a new GUID
					value.UdfDefinitionId = definition.UdfDefinitionId;
					value.EntityId = entityId;
					value.EntityType = entityType;
					value.CreatedOn = now;
					value.CreatedBy = userId;

					await _valueRepository.SaveOrUpdateAsync(value, cancellationToken);
				}

				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}

			return new Dictionary<string, List<string>>();
		}

		public async Task<UdfDefinition> DeleteFieldFromDefinitionAsync(string fieldId, int departmentId,
			string userId, CancellationToken cancellationToken = default)
		{
			// Find the field to remove
			var fieldToRemove = await _fieldRepository.GetByIdAsync(fieldId) as UdfField;
			if (fieldToRemove == null)
				return null;

			var owningDefinition = await _definitionRepository.GetByIdAsync(fieldToRemove.UdfDefinitionId) as UdfDefinition;
			if (owningDefinition == null)
				return null;

			var allFields = (await _fieldRepository.GetFieldsByDefinitionIdAsync(owningDefinition.UdfDefinitionId))?.ToList()
				?? new List<UdfField>();

			// Build new field set without the removed field.
			// UdfFieldId is left empty; SaveDefinitionAsync always assigns fresh IDs.
			var newFields = allFields
				.Where(f => f.UdfFieldId != fieldId)
				.Select(f => new UdfField
				{
					UdfDefinitionId = string.Empty, // will be set in SaveDefinitionAsync
					Name = f.Name,
					Label = f.Label,
					Description = f.Description,
					Placeholder = f.Placeholder,
					FieldDataType = f.FieldDataType,
					IsRequired = f.IsRequired,
					IsReadOnly = f.IsReadOnly,
					DefaultValue = f.DefaultValue,
					ValidationRules = f.ValidationRules,
					SortOrder = f.SortOrder,
					GroupName = f.GroupName,
					IsVisibleOnMobile = f.IsVisibleOnMobile,
					IsVisibleOnReports = f.IsVisibleOnReports,
					IsEnabled = f.IsEnabled,
					Visibility = f.Visibility
				}).ToList();

			return await SaveDefinitionAsync(owningDefinition.DepartmentId, owningDefinition.EntityType,
				newFields, userId, cancellationToken);
		}

		public Dictionary<string, List<string>> ValidateFieldValues(List<UdfField> fields, List<UdfFieldValue> values)
		{
			return UdfValidationHelper.ValidateAllFieldValues(fields, values);
		}
	}
}


