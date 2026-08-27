using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The versioned, code-reviewed protected-field catalog (ADP plan section 5). Static data — the
	/// catalog changes only by code review and a version increment, never at runtime. Departments
	/// record the catalog version they migrated to; the shield UI shows only after that version's
	/// migration is verified.
	/// </summary>
	public interface IProtectedFieldCatalog
	{
		/// <summary>Current catalog version. Incremented whenever entries are added.</summary>
		int Version { get; }

		/// <summary>Every catalog entry.</summary>
		IReadOnlyList<ProtectedFieldDefinition> GetAll();

		/// <summary>Entries for one physical table (SQL Server casing; lookup is case-insensitive). Empty when none.</summary>
		IReadOnlyList<ProtectedFieldDefinition> GetForTable(string tableName);

		/// <summary>The entry with the given stable field id, or null.</summary>
		ProtectedFieldDefinition GetById(string fieldId);

		/// <summary>True when (table, column) is cataloged (case-insensitive).</summary>
		bool IsProtectedField(string tableName, string columnName);
	}
}
