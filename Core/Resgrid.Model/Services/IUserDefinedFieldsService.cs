using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Service for managing User Defined Field definitions, fields, and values.
	/// </summary>
	public interface IUserDefinedFieldsService
	{
		/// <summary>Gets the currently active UDF definition for a department and entity type.</summary>
		Task<UdfDefinition> GetActiveDefinitionAsync(int departmentId, int entityType);

		/// <summary>Gets the fields for the active definition for a department and entity type, ordered by SortOrder.</summary>
		Task<List<UdfField>> GetFieldsForActiveDefinitionAsync(int departmentId, int entityType);

		/// <summary>
		/// Gets the fields for the active definition that are visible to the caller based on their role.
		/// Fields are filtered by their <see cref="UdfFieldVisibility"/> setting:
		/// <list type="bullet">
		///   <item><see cref="UdfFieldVisibility.Everyone"/> — always included.</item>
		///   <item><see cref="UdfFieldVisibility.DepartmentAndGroupAdmins"/> — included when <paramref name="isDepartmentAdmin"/> or <paramref name="isGroupAdmin"/> is true.</item>
		///   <item><see cref="UdfFieldVisibility.DepartmentAdminsOnly"/> — included only when <paramref name="isDepartmentAdmin"/> is true.</item>
		/// </list>
		/// </summary>
		Task<List<UdfField>> GetVisibleFieldsForActiveDefinitionAsync(int departmentId, int entityType,
			bool isDepartmentAdmin, bool isGroupAdmin);

		/// <summary>
		/// Saves a new UDF definition version with the supplied fields.
		/// The previous active definition is marked inactive (history preserved).
		/// </summary>
		Task<UdfDefinition> SaveDefinitionAsync(int departmentId, int entityType, List<UdfField> fields,
			string userId, CancellationToken cancellationToken = default);

		/// <summary>Gets all stored UDF field values for a specific entity under its active definition.</summary>
		Task<List<UdfFieldValue>> GetFieldValuesForEntityAsync(int departmentId, int entityType, string entityId);

		/// <summary>
		/// Batch-fetches all stored UDF field values for multiple entities of the same type under the active definition.
		/// Results are returned as a flat list; callers should group by <see cref="UdfFieldValue.EntityId"/> as needed.
		/// Returns an empty list when no active definition exists or <paramref name="entityIds"/> is empty.
		/// </summary>
		Task<List<UdfFieldValue>> GetFieldValuesForEntitiesAsync(int departmentId, int entityType, IEnumerable<string> entityIds);

		/// <summary>
		/// Validates and saves field values for an entity against the active definition.
		/// Returns a dictionary of fieldId → error list; empty dict means all values are valid and were saved.
		/// </summary>
		Task<Dictionary<string, List<string>>> SaveFieldValuesForEntityAsync(int departmentId, int entityType,
			string entityId, List<UdfFieldValue> values, string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new definition version without the specified field, preserving history.
		/// </summary>
		Task<UdfDefinition> DeleteFieldFromDefinitionAsync(string fieldId, int departmentId,
			string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Pure validation: validates the supplied values against the given field definitions.
		/// Returns a dictionary of fieldId → error messages. Empty means all valid.
		/// </summary>
		Dictionary<string, List<string>> ValidateFieldValues(List<UdfField> fields, List<UdfFieldValue> values);
	}
}

