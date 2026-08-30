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

		/// <summary>Every catalog entry, at the CURRENT catalog version.</summary>
		IReadOnlyList<ProtectedFieldDefinition> GetAll();

		/// <summary>
		/// Every entry a department pinned at <paramref name="catalogVersion"/> owns — that is, every
		/// entry whose AddedInCatalogVersion is at or below it. A department that enrolled under an
		/// older catalog must NOT start encrypting fields added later: its policy still records the
		/// old version, its AAD is computed from that version, and the new fields have never been
		/// swept. They become its rows only when a catalog upgrade runs for that department.
		/// </summary>
		IReadOnlyList<ProtectedFieldDefinition> GetAllForVersion(int catalogVersion);

		/// <summary>Entries for one physical table (SQL Server casing; lookup is case-insensitive). Empty when none.</summary>
		IReadOnlyList<ProtectedFieldDefinition> GetForTable(string tableName);

		/// <summary>Entries for one physical table that a department at <paramref name="catalogVersion"/> owns.</summary>
		IReadOnlyList<ProtectedFieldDefinition> GetForTableAndVersion(string tableName, int catalogVersion);

		/// <summary>
		/// Entries added strictly after <paramref name="fromCatalogVersion"/> and at or below
		/// <paramref name="toCatalogVersion"/> — exactly the fields a catalog-upgrade sweep must
		/// encrypt for a department moving between those versions.
		/// </summary>
		IReadOnlyList<ProtectedFieldDefinition> GetAddedBetween(int fromCatalogVersion, int toCatalogVersion);

		/// <summary>The entry with the given stable field id, or null.</summary>
		ProtectedFieldDefinition GetById(string fieldId);

		/// <summary>True when (table, column) is cataloged (case-insensitive).</summary>
		bool IsProtectedField(string tableName, string columnName);
	}
}
