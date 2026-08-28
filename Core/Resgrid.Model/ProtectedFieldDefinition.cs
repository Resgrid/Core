using System;

namespace Resgrid.Model
{
	/// <summary>
	/// One entry of the versioned protected-field catalog. FieldId is STABLE FOREVER — it is bound
	/// into the AAD of every envelope written for the field, so renaming or renumbering an entry
	/// after any department has migrated makes that ciphertext unreadable. Add entries; never mutate
	/// shipped ones.
	/// </summary>
	public sealed class ProtectedFieldDefinition
	{
		public ProtectedFieldDefinition(string fieldId, string family, string tableName, string columnName,
			ProtectedFieldStorageKind storageKind, ProtectedFieldClassification classification,
			PermissionTypes viewPermission, PermissionTypes? editPermission = null, int addedInCatalogVersion = 1)
		{
			if (string.IsNullOrWhiteSpace(fieldId))
				throw new ArgumentException("FieldId is required.", nameof(fieldId));
			if (string.IsNullOrWhiteSpace(tableName))
				throw new ArgumentException("TableName is required.", nameof(tableName));
			if (string.IsNullOrWhiteSpace(columnName))
				throw new ArgumentException("ColumnName is required.", nameof(columnName));

			FieldId = fieldId;
			Family = family;
			TableName = tableName;
			ColumnName = columnName;
			StorageKind = storageKind;
			Classification = classification;
			ViewPermission = viewPermission;
			EditPermission = editPermission;
			AddedInCatalogVersion = addedInCatalogVersion;
		}

		/// <summary>Stable catalog field id ("calls.name") — part of the envelope AAD; never changes.</summary>
		public string FieldId { get; }

		/// <summary>Catalog family for grant scoping and UI grouping ("Calls", "Contacts", "Personnel").</summary>
		public string Family { get; }

		/// <summary>Physical table (SQL Server casing; PostgreSQL is the lowercase form).</summary>
		public string TableName { get; }

		/// <summary>Physical column the plaintext lived in.</summary>
		public string ColumnName { get; }

		public ProtectedFieldStorageKind StorageKind { get; }

		public ProtectedFieldClassification Classification { get; }

		/// <summary>Permission required (with a current grant) to reveal the field.</summary>
		public PermissionTypes ViewPermission { get; }

		/// <summary>Permission required (with a current grant) to write the field; null = ViewPermission governs.</summary>
		public PermissionTypes? EditPermission { get; }

		/// <summary>Catalog version that introduced this entry.</summary>
		public int AddedInCatalogVersion { get; }
	}
}
