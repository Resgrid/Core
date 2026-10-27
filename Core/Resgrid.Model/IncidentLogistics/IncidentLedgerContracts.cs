using System;

namespace Resgrid.Model.IncidentLogistics
{
	// Shared shape of the Incident Back Office tables (plan §11.2), implemented by the generated entity classes so the
	// ledger repository, the audit stamps, compare-and-increment concurrency and the ADP seam are written once.

	/// <summary>Every Incident Back Office row: string GUID key (app-generated, IdType 1) and the tenant column.</summary>
	public interface IIncidentLedgerEntity : IEntity
	{
		int DepartmentId { get; set; }
		DateTime AddedOn { get; set; }
		string AddedByUserId { get; set; }
	}

	/// <summary>A row that belongs to one activation (plan §9.0). Department catalog rows carry a null activation.</summary>
	public interface IIncidentScopedEntity : IIncidentLedgerEntity
	{
		string IncidentActivationId { get; set; }
	}

	public interface IIncidentSoftDeletable
	{
		bool IsDeleted { get; set; }
	}

	/// <summary>Compare-and-increment concurrency (plan §11.2, §13.3): a stale version is a 409 with current state, never a silent overwrite.</summary>
	public interface IIncidentVersioned
	{
		int RowVersion { get; set; }
	}

	public interface IIncidentEditable
	{
		DateTime? EditedOn { get; set; }
		string EditedByUserId { get; set; }
	}

	/// <summary>ADP row markers (plan §15.3); the sealed values sit in the catalogued columns themselves.</summary>
	public interface IIncidentProtectable
	{
		bool IsProtected { get; set; }
		int? ProtectedCatalogVersion { get; set; }
	}

	/// <summary>Raised when an update carries a RowVersion that is no longer current (HTTP 409 at the edges).</summary>
	public sealed class IncidentConcurrencyException : Exception
	{
		public IncidentConcurrencyException(string table, string id) : base($"The {table} row {id} was changed by someone else.") { Table = table; Id = id; }

		public string Table { get; }
		public string Id { get; }
	}

	/// <summary>A refused business rule (illegal transition, invariant, sealed incident, separation of duties). Message is a resx key.</summary>
	public sealed class IncidentRuleException : Exception, Resgrid.Framework.IUserFacingException
	{
		public IncidentRuleException(string messageKey, params object[] args) : base(messageKey) { MessageKey = messageKey; Args = args ?? Array.Empty<object>(); }

		public string MessageKey { get; }
		public object[] Args { get; }
	}
}
