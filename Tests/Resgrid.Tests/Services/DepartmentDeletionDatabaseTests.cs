using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Runs the real SQL Server department deletion against the full migration chain. Every table with a
	/// foreign-key path to Departments or AspNetUsers gets one generated row per department, so a table the
	/// delete script never clears (or clears after its parent) fails here instead of in the scheduled worker
	/// (RESGRID-WEBJOBS-76: FK_Workshifts_Department, FK_ContactCategories_Department).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class DepartmentDeletionDatabaseTests
	{
		private const string DatabasePrefix = "department_deletion_verification_";

		// Tables the generic seeder cannot populate because a CHECK constraint needs two distinct parent rows
		// of the same table (CK_InventoryTransfers_Locations). Their deletion is covered by InventoryDatabaseTests.
		private static readonly HashSet<string> UnseedableTables = new(StringComparer.OrdinalIgnoreCase)
		{
			"InventoryTransfers", "InventoryTransferItems"
		};

		// Values for module CHECK rules the generic values break. DBNull forces an optional key null so the
		// sibling holder key (the user, which exercises the member-account delete order) is the one populated.
		private static readonly Dictionary<string, object> ColumnOverrides = new(StringComparer.OrdinalIgnoreCase)
		{
			["InventoryLocations.LocationType"] = 3,
			["InventoryLocations.GroupId"] = DBNull.Value,
			["InventoryLocations.UnitId"] = DBNull.Value,
			["InventoryLocations.ContainerAssetId"] = DBNull.Value,
			["InventoryIssuances.IssuedToUnitId"] = DBNull.Value,
			["RecordInventoryUsages.SourceType"] = 0,
		};

		private string _master, _connection, _database;
		private bool _created, _configured;
		private DatabaseTypes _previousType;
		private string _previousConnection;
		private int _counter;

		private List<ColumnInfo> _columns;
		private Dictionary<string, List<ColumnInfo>> _columnsByTable;
		private Dictionary<string, List<string>> _primaryKeys;
		private Dictionary<string, List<ForeignKeyInfo>> _foreignKeysByChild;
		private HashSet<string> _triggeredTables;
		private HashSet<string> _scope, _requiredScope;
		private readonly Dictionary<string, Dictionary<string, object>> _shared = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _sharedInProgress = new(StringComparer.OrdinalIgnoreCase);

		[OneTimeSetUp]
		public async Task CreateAFullyMigratedDisposableDatabase()
		{
			var configured = Environment.GetEnvironmentVariable("RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Set RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION to enable the disposable department deletion database test.");
			// Refuse application database names; the connection is used only to create a unique disposable database.
			var builder = new SqlConnectionStringBuilder(configured);
			if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The department deletion test requires a SQL Server master connection.");
			builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using (var master = new SqlConnection(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			using (var runner = new ServiceCollection().AddFluentMigratorCore()
				.ConfigureRunner(r => r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All())
				.BuildServiceProvider(false))
			using (var scope = runner.CreateScope())
				scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

			_previousType = DataConfig.DatabaseType; _previousConnection = DataConfig.CoreConnectionString; _configured = true;
			DataConfig.DatabaseType = DatabaseTypes.SqlServer; DataConfig.CoreConnectionString = _connection;
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_configured) { DataConfig.DatabaseType = _previousType; DataConfig.CoreConnectionString = _previousConnection; }
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected department deletion fixture database name.");
			SqlConnection.ClearAllPools();
			await using var master = new SqlConnection(_master);
			await master.ExecuteAsync("ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[Test]
		public async Task DeletingADepartmentClearsEveryForeignKeyedRowAndLeavesOtherDepartmentsIntact()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);

			var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var deleted = await SeedDepartmentAsync(db, failures);
			var kept = await SeedDepartmentAsync(db, failures);

			string Reason(string t) => t + ": " + (failures.TryGetValue(t, out var f) ? f : "no attempt (parent not seeded)");
			foreach (var optional in _scope.Except(_requiredScope).Where(t => !deleted.Linked.Contains(t)).OrderBy(t => t))
				TestContext.Out.WriteLine("Not verified (reaches the department only through optional keys): " + Reason(optional));
			var unseeded = _requiredScope.Where(t => !deleted.Linked.Contains(t)).Except(UnseedableTables, StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
			unseeded.Should().BeEmpty("every table with a required foreign-key path to Departments or AspNetUsers must be seeded, or listed in UnseedableTables with a reason; failures:\n"
				+ string.Join("\n", unseeded.Select(Reason)));

			var result = await new DeleteRepository().DeleteDepartmentAndUsersAsync(deleted.DepartmentId);
			result.Should().BeTrue();

			var remaining = new List<string>();
			var lost = new List<string>();
			foreach (var (table, row) in deleted.AllRows())
				if (await RowExistsAsync(db, table, row)) remaining.Add(table);
			foreach (var (table, row) in kept.AllRows())
				if (!await RowExistsAsync(db, table, row)) lost.Add(table);

			TestContext.Out.WriteLine($"Verified {deleted.AllRows().Count()} deleted and {kept.AllRows().Count()} retained rows across {deleted.Linked.Count} tables.");
			remaining.Should().BeEmpty("the deleted department's rows must all be removed");
			lost.Should().BeEmpty("another department's rows must survive the deletion");
		}

		private sealed class ColumnInfo
		{
			public string Table { get; set; }
			public string Name { get; set; }
			public string Type { get; set; }
			public short MaxLength { get; set; }
			public bool Nullable { get; set; }
			public bool Identity { get; set; }
			public bool Computed { get; set; }
			public bool HasDefault { get; set; }
			public int Ordinal { get; set; }
		}

		private sealed class ForeignKeyColumn
		{
			public string Name { get; set; }
			public string Child { get; set; }
			public string ChildColumn { get; set; }
			public string Parent { get; set; }
			public string ParentColumn { get; set; }
			public int Ordinal { get; set; }
		}

		private sealed class ForeignKeyInfo
		{
			public string Name;
			public string Child;
			public string Parent;
			public List<(string Child, string Parent)> Columns;
			public bool IsTrivialSelfReference => Child == Parent && Columns.All(c => string.Equals(c.Child, c.Parent, StringComparison.OrdinalIgnoreCase));
		}

		private sealed class SeededDepartment
		{
			public int DepartmentId;
			public string ManagingUserId;
			public string MemberUserId;
			public readonly Dictionary<string, Dictionary<string, object>> Rows = new(StringComparer.OrdinalIgnoreCase);
			public readonly HashSet<string> Linked = new(StringComparer.OrdinalIgnoreCase) { "AspNetUsers", "Departments" };
			public readonly List<(string Table, Dictionary<string, object> Row)> Extra = new();

			public IEnumerable<(string Table, Dictionary<string, object> Row)> AllRows() =>
				Rows.Where(r => Linked.Contains(r.Key)).Select(r => (r.Key, r.Value)).Concat(Extra);
		}

		private async Task LoadSchemaAsync(SqlConnection db)
		{
			_columns = (await db.QueryAsync<ColumnInfo>(@"
				SELECT t.name AS [Table], c.name AS Name, ty.name AS Type, c.max_length AS MaxLength, c.is_nullable AS Nullable,
					c.is_identity AS [Identity], c.is_computed AS Computed, CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit) AS HasDefault, c.column_id AS Ordinal
				FROM sys.tables t
				JOIN sys.columns c ON c.object_id = t.object_id
				JOIN sys.types ty ON ty.user_type_id = c.user_type_id
				WHERE t.is_ms_shipped = 0 AND SCHEMA_NAME(t.schema_id) = 'dbo'")).ToList();
			_columnsByTable = _columns.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderBy(c => c.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

			_primaryKeys = (await db.QueryAsync<(string Table, string Column)>(@"
				SELECT t.name, c.name
				FROM sys.indexes i
				JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
				JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
				JOIN sys.tables t ON t.object_id = i.object_id
				WHERE i.is_primary_key = 1 AND SCHEMA_NAME(t.schema_id) = 'dbo'"))
				.GroupBy(k => k.Table, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Select(k => k.Column).ToList(), StringComparer.OrdinalIgnoreCase);

			_foreignKeysByChild = (await db.QueryAsync<ForeignKeyColumn>(@"
				SELECT fk.name AS Name, OBJECT_NAME(fk.parent_object_id) AS Child, c.name AS ChildColumn,
					OBJECT_NAME(fk.referenced_object_id) AS Parent, rc.name AS ParentColumn, fkc.constraint_column_id AS Ordinal
				FROM sys.foreign_keys fk
				JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
				JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
				JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
				WHERE SCHEMA_NAME(fk.schema_id) = 'dbo'"))
				.GroupBy(k => k.Name)
				.Select(g => new ForeignKeyInfo
				{
					Name = g.Key, Child = g.First().Child, Parent = g.First().Parent,
					Columns = g.OrderBy(k => k.Ordinal).Select(k => (k.ChildColumn, k.ParentColumn)).ToList()
				})
				.GroupBy(k => k.Child, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

			_triggeredTables = new HashSet<string>(await db.QueryAsync<string>(
				"SELECT OBJECT_NAME(parent_id) FROM sys.triggers WHERE parent_class = 1 AND is_disabled = 0"), StringComparer.OrdinalIgnoreCase);

			// Scope: Departments, AspNetUsers and every table with a foreign-key path to either. Tables whose path
			// runs only through required keys always hold department rows and must all be seeded and verified.
			_scope = Closure(_ => true);
			_requiredScope = Closure(IsRequired);
		}

		private HashSet<string> Closure(Func<ForeignKeyInfo, bool> follow)
		{
			var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Departments", "AspNetUsers" };
			var added = true;
			while (added)
			{
				added = false;
				foreach (var fk in _foreignKeysByChild.Values.SelectMany(f => f))
					if (tables.Contains(fk.Parent) && follow(fk) && tables.Add(fk.Child)) added = true;
			}
			return tables;
		}

		private List<ForeignKeyInfo> ForeignKeys(string table) => _foreignKeysByChild.TryGetValue(table, out var fks) ? fks : new List<ForeignKeyInfo>();

		private ColumnInfo Column(string table, string column) => _columnsByTable[table].First(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

		private bool IsRequired(ForeignKeyInfo fk) => fk.Columns.All(c => !Column(fk.Child, c.Child).Nullable);

		private async Task<SeededDepartment> SeedDepartmentAsync(SqlConnection db, Dictionary<string, string> failures)
		{
			var seeded = new SeededDepartment
			{
				ManagingUserId = "deletion-manager-" + Guid.NewGuid().ToString("N"),
				MemberUserId = "deletion-member-" + Guid.NewGuid().ToString("N")
			};

			// The member row is the parent every user foreign key points at: the per-member cursor deletes that
			// account before the department-level deletes run, which is the ordering the script must survive.
			seeded.Rows["AspNetUsers"] = await InsertOrThrowAsync(db, "AspNetUsers", seeded, new Dictionary<string, object> { ["Id"] = seeded.MemberUserId });
			seeded.Extra.Add(("AspNetUsers", await InsertOrThrowAsync(db, "AspNetUsers", seeded, new Dictionary<string, object> { ["Id"] = seeded.ManagingUserId })));
			seeded.Rows["Departments"] = await InsertOrThrowAsync(db, "Departments", seeded, new Dictionary<string, object> { ["ManagingUserId"] = seeded.ManagingUserId });
			seeded.DepartmentId = Convert.ToInt32(seeded.Rows["Departments"]["DepartmentId"]);

			foreach (var table in SeedOrder())
			{
				if (seeded.Rows.ContainsKey(table)) continue;
				var overrides = table == "DepartmentMembers" ? new Dictionary<string, object> { ["UserId"] = seeded.MemberUserId } : null;
				var (row, error) = await TryInsertAsync(db, table, seeded, overrides);
				if (row == null) { failures[table] = error; continue; }
				seeded.Rows[table] = row;
				if (IsLinked(table, row, seeded)) { seeded.Linked.Add(table); failures.Remove(table); }
				else failures[table] = "seeded only with its optional department keys left null";
			}

			// The managing member's own membership row, deleted only after the Departments row.
			if (!seeded.Linked.Contains("DepartmentMembers"))
				seeded.Extra.Add(("DepartmentMembers", await InsertOrThrowAsync(db, "DepartmentMembers", seeded, new Dictionary<string, object> { ["UserId"] = seeded.MemberUserId })));
			seeded.Extra.Add(("DepartmentMembers", await InsertOrThrowAsync(db, "DepartmentMembers", seeded, new Dictionary<string, object> { ["UserId"] = seeded.ManagingUserId })));

			await BackfillOptionalKeysAsync(db, seeded);
			var changed = true;
			while (changed)
			{
				changed = false;
				foreach (var (table, row) in seeded.Rows)
					if (!seeded.Linked.Contains(table) && IsLinked(table, row, seeded)) { seeded.Linked.Add(table); failures.Remove(table); changed = true; }
			}

			return seeded;
		}

		/// <summary>Seeding in dependency order leaves optional keys to later tables null (cycles such as work orders
		/// and their recurrence versions); fill them now so the delete has to break those cycles too.</summary>
		private async Task BackfillOptionalKeysAsync(SqlConnection db, SeededDepartment seeded)
		{
			foreach (var (table, row) in seeded.Rows.ToList())
			{
				if (!_primaryKeys.TryGetValue(table, out var keys)) continue;
				foreach (var fk in ForeignKeys(table).Where(f => f.Parent != table && !IsRequired(f)))
				{
					if (fk.Columns.All(c => row.TryGetValue(c.Child, out var v) && v != null && v != DBNull.Value)) continue;
					if (fk.Columns.Any(c => ColumnOverrides.ContainsKey(table + "." + c.Child))) continue;
					if (!seeded.Rows.TryGetValue(fk.Parent, out var parent)) continue;

					var parameters = new DynamicParameters();
					var sets = fk.Columns.Select((c, i) => { parameters.Add("v" + i, parent[c.Parent]); return $"[{c.Child}] = @v{i}"; }).ToList();
					var where = keys.Select(k => { parameters.Add("k_" + k, row[k]); return $"[{k}] = @k_{k}"; }).ToList();
					try
					{
						await db.ExecuteAsync($"UPDATE [dbo].[{table}] SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", where)}", parameters);
						foreach (var c in fk.Columns) row[c.Child] = parent[c.Parent];
					}
					catch (SqlException) { } // a module CHECK rule rejects the extra key; the row stays as seeded
				}
			}
		}

		/// <summary>Scope tables ordered so every required (non-nullable) parent is seeded first.</summary>
		private List<string> SeedOrder()
		{
			var pending = new HashSet<string>(_scope, StringComparer.OrdinalIgnoreCase);
			var order = new List<string>();
			while (pending.Count > 0)
			{
				var ready = pending.Where(t => ForeignKeys(t).Where(IsRequired)
					.All(fk => fk.Parent == t || !_scope.Contains(fk.Parent) || !pending.Contains(fk.Parent))).OrderBy(t => t).ToList();
				if (ready.Count == 0) { order.AddRange(pending.OrderBy(t => t)); break; } // cycle of required keys; attempt anyway
				order.AddRange(ready);
				pending.ExceptWith(ready);
			}
			return order;
		}

		private async Task<Dictionary<string, object>> InsertOrThrowAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides)
		{
			var (row, error) = await TryInsertAsync(db, table, seeded, overrides);
			if (row == null) throw new InvalidOperationException($"Could not seed {table}: {error}");
			return row;
		}

		/// <summary>Level 0 fills optional foreign keys too (widest delete-order coverage); level 1 leaves them
		/// null for CHECK rules; level 2 also gives non-key integers unique values for unique indexes.</summary>
		private async Task<(Dictionary<string, object> Row, string Error)> TryInsertAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides)
		{
			string error = null;
			for (var level = 0; level <= 2; level++)
			{
				var values = await BuildValuesAsync(db, table, seeded, overrides, level);
				if (values.Error != null) { error = values.Error; continue; }
				try { return (await InsertAsync(db, table, values.Values), null); }
				catch (SqlException e) { error = e.Message; }
			}
			return (null, error);
		}

		/// <summary>True when a non-null foreign key chains the row to the department's roots. A row that reached its
		/// department only through optional keys left null cannot block the delete, so it is not verified.</summary>
		private bool IsLinked(string table, Dictionary<string, object> row, SeededDepartment seeded) =>
			ForeignKeys(table).Any(fk => fk.Parent != table && seeded.Linked.Contains(fk.Parent)
				&& fk.Columns.All(c => row.TryGetValue(c.Child, out var v) && v != null && v != DBNull.Value));

		private async Task<(Dictionary<string, object> Values, string Error)> BuildValuesAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides, int level)
		{
			var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (var column in _columnsByTable[table])
				if (ColumnOverrides.TryGetValue(table + "." + column.Name, out var forced)) values[column.Name] = forced;
			if (overrides != null) foreach (var o in overrides) values[o.Key] = o.Value;
			var keyColumns = _primaryKeys.TryGetValue(table, out var pk) ? pk : new List<string>();

			foreach (var fk in ForeignKeys(table).Where(f => !f.IsTrivialSelfReference).OrderBy(f => IsRequired(f) ? 0 : 1))
			{
				var required = IsRequired(fk);
				Dictionary<string, object> parent = null;
				if (fk.Parent == table) parent = null;
				else if (_scope.Contains(fk.Parent)) { if (seeded != null) seeded.Rows.TryGetValue(fk.Parent, out parent); }
				else parent = await SharedAsync(db, fk.Parent);

				if (parent == null || (!required && level >= 1))
				{
					if (required) return (null, $"required parent {fk.Parent} ({fk.Name}) was not seeded");
					foreach (var column in fk.Columns.Where(c => Column(table, c.Child).Nullable && !values.ContainsKey(c.Child)))
						values[column.Child] = DBNull.Value;
					continue;
				}
				foreach (var column in fk.Columns)
					if (!values.TryGetValue(column.Child, out var existing) || (existing == DBNull.Value && required))
						values[column.Child] = parent[column.Parent];
			}

			foreach (var column in _columnsByTable[table])
			{
				if (column.Identity || column.Computed || column.Type == "timestamp" || values.ContainsKey(column.Name)) continue;
				var isKey = keyColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase);
				if (table != "Departments" && string.Equals(column.Name, "DepartmentId", StringComparison.OrdinalIgnoreCase) && column.Type == "int")
				{ values[column.Name] = seeded?.DepartmentId ?? 0; continue; }
				if (seeded != null && string.Equals(column.Name, "UserId", StringComparison.OrdinalIgnoreCase) && column.Type.EndsWith("char", StringComparison.Ordinal))
				{ values[column.Name] = seeded.MemberUserId; continue; }
				if (!isKey && (column.Nullable || column.HasDefault)) continue;
				var value = Generate(column, isKey || level >= 2);
				if (value == null) return (null, $"unsupported column type {column.Type} for {table}.{column.Name}");
				values[column.Name] = value;
			}

			return (values, null);
		}

		private object Generate(ColumnInfo column, bool unique)
		{
			switch (column.Type)
			{
				case "int": case "bigint": return unique ? ++_counter : 1;
				case "smallint": return unique ? (short)(++_counter % short.MaxValue) : (short)1;
				case "tinyint": return unique ? (byte)(++_counter % 250 + 1) : (byte)1;
				case "bit": return false;
				case "decimal": case "numeric": case "money": case "smallmoney": return 1m;
				case "float": return 1d;
				case "real": return 1f;
				case "date": case "datetime": case "datetime2": case "smalldatetime": return new DateTime(2026, 1, 1);
				case "datetimeoffset": return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
				case "time": return TimeSpan.FromHours(1);
				case "uniqueidentifier": return Guid.NewGuid();
				case "nvarchar": case "nchar": case "varchar": case "char": case "ntext": case "text": case "sysname": return Text(column);
				case "varbinary": case "binary": case "image": return new byte[] { 1 };
				case "xml": return "<seed/>";
				default: return null;
			}
		}

		/// <summary>Unique text, 64 characters where the column allows (hash-shaped columns check the length).</summary>
		private string Text(ColumnInfo column)
		{
			var chars = column.MaxLength == -1 || column.Type == "ntext" || column.Type == "text" ? 64
				: column.Type.StartsWith("n", StringComparison.Ordinal) ? column.MaxLength / 2 : column.MaxLength;
			var length = Math.Min(chars, 64);
			var text = (++_counter).ToString().PadLeft(length, 'a');
			return text.Length > length ? text.Substring(text.Length - length) : text;
		}

		/// <summary>One row per out-of-scope parent table (lookup data, addresses), shared by both departments.</summary>
		private async Task<Dictionary<string, object>> SharedAsync(SqlConnection db, string table)
		{
			if (_shared.TryGetValue(table, out var row)) return row;
			if (!_sharedInProgress.Add(table)) return null;
			try
			{
				var (inserted, _) = await TryInsertAsync(db, table, null, null);
				if (inserted != null) _shared[table] = inserted;
				return inserted;
			}
			finally { _sharedInProgress.Remove(table); }
		}

		private async Task<Dictionary<string, object>> InsertAsync(SqlConnection db, string table, Dictionary<string, object> values)
		{
			var parameters = new DynamicParameters();
			var columns = new List<string>();
			var slots = new List<string>();
			foreach (var (name, value) in values)
			{
				columns.Add("[" + name + "]");
				if (value == null || value == DBNull.Value) { slots.Add("NULL"); continue; }
				var slot = "@p" + parameters.ParameterNames.Count();
				parameters.Add(slot, value);
				slots.Add(slot);
			}

			var sql = new StringBuilder();
			var identity = _columnsByTable[table].FirstOrDefault(c => c.Identity);
			var keyColumns = _primaryKeys.TryGetValue(table, out var pk) ? pk : null;
			var output = identity == null && keyColumns == null && !_triggeredTables.Contains(table) ? " OUTPUT INSERTED.*" : "";
			sql.Append(columns.Count == 0
				? $"INSERT INTO [dbo].[{table}]{output} DEFAULT VALUES;"
				: $"INSERT INTO [dbo].[{table}] ({string.Join(",", columns)}){output} VALUES ({string.Join(",", slots)});");
			if (identity != null) sql.Append($" SELECT * FROM [dbo].[{table}] WHERE [{identity.Name}] = SCOPE_IDENTITY();");
			else if (keyColumns != null) sql.Append($" SELECT * FROM [dbo].[{table}] WHERE {string.Join(" AND ", keyColumns.Select(k => $"[{k}] = @k_{k}"))};");
			if (identity == null && keyColumns != null)
				foreach (var key in keyColumns) parameters.Add("k_" + key, values[key]);

			var result = (IDictionary<string, object>)await db.QuerySingleOrDefaultAsync(sql.ToString(), parameters);
			return result == null
				? new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, object>(result, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<bool> RowExistsAsync(SqlConnection db, string table, Dictionary<string, object> row)
		{
			if (!_primaryKeys.TryGetValue(table, out var keys)) return false;
			var parameters = new DynamicParameters();
			foreach (var key in keys) parameters.Add("k_" + key, row[key]);
			return await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE {string.Join(" AND ", keys.Select(k => $"[{k}] = @k_{k}"))}", parameters) > 0;
		}
	}
}
