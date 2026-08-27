using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// How the ADP bulk repository addresses one cataloged table: primary key, department-ownership
	/// scope, and the cataloged columns with their storage kinds. Bindings are code-reviewed
	/// constants defined next to the migration engine — table and column names NEVER come from
	/// runtime input, which is what makes the repository's dynamic SQL safe.
	/// </summary>
	public sealed record AdpTableBinding
	{
		public AdpTableBinding(string tableName, string pkColumn, bool pkIsNumeric,
			string departmentColumn, string parentFkColumn, string parentTable, string parentPkColumn,
			IReadOnlyList<AdpColumnSpec> columns)
		{
			if (string.IsNullOrWhiteSpace(tableName))
				throw new ArgumentException("Table name is required.", nameof(tableName));
			if (string.IsNullOrWhiteSpace(pkColumn))
				throw new ArgumentException("Primary key column is required.", nameof(pkColumn));
			if (string.IsNullOrWhiteSpace(departmentColumn) && string.IsNullOrWhiteSpace(parentFkColumn))
				throw new ArgumentException("A binding needs a department column or a parent join.", nameof(departmentColumn));

			TableName = tableName;
			PkColumn = pkColumn;
			PkIsNumeric = pkIsNumeric;
			DepartmentColumn = departmentColumn;
			ParentFkColumn = parentFkColumn;
			ParentTable = parentTable;
			ParentPkColumn = parentPkColumn;
			Columns = columns ?? Array.Empty<AdpColumnSpec>();
		}

		/// <summary>Direct-scope binding: the table carries its own DepartmentId column.</summary>
		public static AdpTableBinding Direct(string tableName, string pkColumn, bool pkIsNumeric,
			string departmentColumn, IReadOnlyList<AdpColumnSpec> columns) =>
			new AdpTableBinding(tableName, pkColumn, pkIsNumeric, departmentColumn, null, null, null, columns);

		/// <summary>
		/// Parent-join binding: ownership derives from a verified parent
		/// (fk IN (SELECT parentPk FROM parentTable WHERE DepartmentId = @DepartmentId)).
		/// </summary>
		public static AdpTableBinding ViaParent(string tableName, string pkColumn, bool pkIsNumeric,
			string parentFkColumn, string parentTable, string parentPkColumn, IReadOnlyList<AdpColumnSpec> columns) =>
			new AdpTableBinding(tableName, pkColumn, pkIsNumeric, null, parentFkColumn, parentTable, parentPkColumn, columns);

		public string TableName { get; }
		public string PkColumn { get; }
		public bool PkIsNumeric { get; }

		/// <summary>Department column when the table is directly scoped; null for parent-join bindings.</summary>
		public string DepartmentColumn { get; }

		public string ParentFkColumn { get; }
		public string ParentTable { get; }
		public string ParentPkColumn { get; }

		public IReadOnlyList<AdpColumnSpec> Columns { get; }

		/// <summary>Row-level protection marker column ("IsProtected"), when the table has one (companion pattern).</summary>
		public string ProtectedMarkerColumn { get; init; }
	}

	/// <summary>One cataloged column inside a binding.</summary>
	public sealed class AdpColumnSpec
	{
		public AdpColumnSpec(string columnName, string fieldId, ProtectedFieldStorageKind storageKind, string companionColumn = null)
		{
			ColumnName = columnName;
			FieldId = fieldId;
			StorageKind = storageKind;
			CompanionColumn = companionColumn;

			if (storageKind == ProtectedFieldStorageKind.CompanionColumn && string.IsNullOrWhiteSpace(companionColumn))
				throw new ArgumentException("Companion storage requires a companion column name.", nameof(companionColumn));
		}

		public string ColumnName { get; }

		/// <summary>Stable catalog field id — the AAD component.</summary>
		public string FieldId { get; }

		public ProtectedFieldStorageKind StorageKind { get; }

		/// <summary>Envelope column for CompanionColumn storage (e.g. "ProtectedLatitudeEnvelope").</summary>
		public string CompanionColumn { get; }
	}
}
