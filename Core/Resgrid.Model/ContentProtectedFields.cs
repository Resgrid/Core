using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// ADP catalog 39 (round 8, "both systems work with ADP", 2026-11-25): the protected values the Protocols and Training/LMS work
	/// copies. Two families, nothing else:
	/// <list type="bullet">
	/// <item>Calls — a call's case record (P4): the answers in words (<c>CallProtocols.CaseJson</c>, <c>AnswersJson</c>) are caller
	/// content, protected like <c>Calls.NatureOfCall</c> and <c>Calls.CallFormData</c>; the dispatcher's override and removal reasons are
	/// free text about the call, protected like <c>Calls.DeletedReason</c>. The
	/// determinant (<c>CallProtocols.Determinant</c>) is operational, like a call type: run cards, the dispatch texts and the
	/// analytics read it without a grant.</item>
	/// <item>Records — the external signer of a Records-owned signature (an inspection's occupant representative, an evidence custody
	/// transfer's outside party; the same people <c>RmsInspections.SignatureName</c> and <c>RmsInvestigationCustody.ToExternal</c>
	/// protect). Rows are scoped through the request's subject kind, so a customer, vendor or outside evaluator signing a Business
	/// Operations, Readiness or LMS document is never encrypted (customer-facing and external data is not under ADP).</item>
	/// </list>
	/// Protocol and course content, scores, statuses and every id stay plaintext (the operational-data rule, 2026-10-17).
	/// </summary>
	public static class ContentProtectedFields
	{
		public const int CatalogVersion = 39;

		public const string CallsFamily = "Calls";
		public const string RecordsFamily = "Records";

		/// <summary>The signature subject kinds whose external signer identity is Records data.</summary>
		public static readonly IReadOnlyList<int> RecordsSignatureSubjectKinds = new[] { (int)SignatureSubjectKinds.RecordsInspection, (int)SignatureSubjectKinds.CustodyTransfer };

		public static bool IsRecordsSignatureSubject(int subjectKind) => RecordsSignatureSubjectKinds.Contains(subjectKind);

		/// <summary>The case record, the questionnaire in words and the override and removal reasons of a protocol on a call (catalog 39, Calls family).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallProtocol, string> Get, Action<CallProtocol, string> Set)> CallProtocols =
			new Dictionary<string, (Func<CallProtocol, string>, Action<CallProtocol, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["callprotocols.casejson"] = (p => p.CaseJson, (p, v) => p.CaseJson = v),
				["callprotocols.answersjson"] = (p => p.AnswersJson, (p, v) => p.AnswersJson = v),
				["callprotocols.overridereason"] = (p => p.OverrideReason, (p, v) => p.OverrideReason = v),
				["callprotocols.removedreason"] = (p => p.RemovedReason, (p, v) => p.RemovedReason = v)
			};

		/// <summary>
		/// The note of a step-through event (P3), kept in its own row (M0403) because the step log is appended by clients that never decrypt
		/// (catalog 39, Calls family: call narrative like <c>CallNotes.Note</c>).
		/// </summary>
		public static readonly IReadOnlyDictionary<string, (Func<CallProtocolStepNote, string> Get, Action<CallProtocolStepNote, string> Set)> StepNotes =
			new Dictionary<string, (Func<CallProtocolStepNote, string>, Action<CallProtocolStepNote, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["callprotocolstepnotes.note"] = (n => n.Note, (n, v) => n.Note = v)
			};

		/// <summary>The external signer's name on a Records-owned request (catalog 39, Records family).</summary>
		public static readonly IReadOnlyDictionary<string, (Func<SignatureRequestSigner, string> Get, Action<SignatureRequestSigner, string> Set)> Signers =
			new Dictionary<string, (Func<SignatureRequestSigner, string>, Action<SignatureRequestSigner, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["signaturerequestsigners.externalname"] = (s => s.ExternalName, (s, v) => s.ExternalName = v)
			};

		/// <summary>The external signer's name kept on the signature record of a Records-owned request.</summary>
		public static readonly IReadOnlyDictionary<string, (Func<SignatureRecord, string> Get, Action<SignatureRecord, string> Set)> Records =
			new Dictionary<string, (Func<SignatureRecord, string>, Action<SignatureRecord, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["signaturerecords.externalsignername"] = (r => r.ExternalSignerName, (r, v) => r.ExternalSignerName = v)
			};

		/// <summary>
		/// The in-person capture's printed and witness names on a Records-owned request. The columns already hold the application-key
		/// ciphertext; ADP wraps that value (so offboarding restores exactly what was there).
		/// </summary>
		public static readonly IReadOnlyDictionary<string, (Func<SignatureCapture, string> Get, Action<SignatureCapture, string> Set)> Captures =
			new Dictionary<string, (Func<SignatureCapture, string>, Action<SignatureCapture, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["signaturecaptures.printednameencrypted"] = (c => c.PrintedNameEncrypted, (c, v) => c.PrintedNameEncrypted = v),
				["signaturecaptures.witnessnameencrypted"] = (c => c.WitnessNameEncrypted, (c, v) => c.WitnessNameEncrypted = v)
			};

		/// <summary>(table, column, classification, family) for the catalog registration and the table bindings.</summary>
		public static IEnumerable<(string Table, string Column, string Classification, string Family)> All()
		{
			yield return ("CallProtocols", "CaseJson", "Phi", CallsFamily);
			yield return ("CallProtocols", "AnswersJson", "Phi", CallsFamily);
			yield return ("CallProtocols", "OverrideReason", "Sensitive", CallsFamily);
			yield return ("CallProtocols", "RemovedReason", "Sensitive", CallsFamily);
			yield return ("CallProtocolStepNotes", "Note", "Phi", CallsFamily);
			yield return ("SignatureRequestSigners", "ExternalName", "Pii", RecordsFamily);
			yield return ("SignatureRecords", "ExternalSignerName", "Pii", RecordsFamily);
			yield return ("SignatureCaptures", "PrintedNameEncrypted", "Pii", RecordsFamily);
			yield return ("SignatureCaptures", "WitnessNameEncrypted", "Pii", RecordsFamily);
		}

		/// <summary>The primary key of a catalog 39 table.</summary>
		public static string KeyColumn(string table) => table switch
		{
			"CallProtocols" => "CallProtocolId",
			"CallProtocolStepNotes" => "CallProtocolStepNoteId",
			"SignatureRequestSigners" => "SignatureRequestSignerId",
			"SignatureRecords" => "SignatureRecordId",
			"SignatureCaptures" => "SignatureCaptureId",
			_ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a catalog 39 table.")
		};

		/// <summary>
		/// The columns of the catalog 39 tables that must stay plaintext: run cards, the dispatch texts, analytics, sampling and the
		/// signature lists read them without a grant (operational).
		/// </summary>
		public static readonly IReadOnlyList<string> OperationalColumns = new[]
		{
			"CallProtocols.Determinant", "CallProtocols.Data", "CallProtocols.Source", "CallProtocols.ProtocolVersionId",
			"SignatureRecords.SignerNameSnapshot", "SignatureRequestSigners.Role", "SignatureRequestSigners.ContactMasked"
		};
	}
}
