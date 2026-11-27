using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	/// <summary>Step-through on a call (P3): one protocol on the call run like a checklist.</summary>
	public class ProtocolStepRunModel
	{
		public Call Call { get; set; }
		public ProtocolCallChecklist Checklist { get; set; }
		public bool CanEdit { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>The cited version's stored content, shown beside the steps (sanitized on output).</summary>
		public string ContentHtml { get; set; }
		public string DosingToolUrl { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The incident SOP checklists of a call (View Call's protocols tab): those running and the procedures that can be started.</summary>
	public class ProtocolCallSopModel
	{
		public int CallId { get; set; }
		public bool CanEdit { get; set; }
		public List<ProtocolCallChecklist> Checklists { get; set; } = new List<ProtocolCallChecklist>();
		public List<DispatchProtocol> Library { get; set; } = new List<DispatchProtocol>();
	}

	/// <summary>The QR poster of a protocol (apparatus, station walls): its deep link and code.</summary>
	public class ProtocolQrModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public string DeepLink { get; set; }
		public string QrDataUrl { get; set; }
	}

	/// <summary>The print view of one version: the content as members read it, stamped with version, effective date and approver.</summary>
	public class ProtocolPrintModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		/// <summary>The stored text shown (the translation's or the source's), sanitized on output.</summary>
		public string Html { get; set; }
		public string DosingToolUrl { get; set; }
		public string Locale { get; set; }
		public bool IsTranslation { get; set; }
		public string ApproverNames { get; set; }
		public string DeepLink { get; set; }
		public string QrDataUrl { get; set; }
		public string DepartmentName { get; set; }
	}

	/// <summary>The PDF book page: a category (or the whole library) and whether sealing is configured.</summary>
	public class ProtocolBookModel
	{
		public List<ProtocolCategory> Categories { get; set; } = new List<ProtocolCategory>();
		public bool SealingReady { get; set; }
		public int ReadableCount { get; set; }
	}

	/// <summary>A version's translations (P3).</summary>
	public class ProtocolTranslationsModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public string SourceLocale { get; set; }
		public List<ProtocolVersionTranslation> Translations { get; set; } = new List<ProtocolVersionTranslation>();
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public bool CanAuthor { get; set; }
		public bool CanApprove { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
	}

	/// <summary>One translation side by side with its source: the source wording read-only, the translated wording editable.</summary>
	public class ProtocolTranslationEditModel
	{
		public DispatchProtocol Protocol { get; set; }
		public ProtocolVersion Version { get; set; }
		public ProtocolVersionTranslation Translation { get; set; }
		public ProtocolContentDocument Source { get; set; }
		public ProtocolContentDocument Translated { get; set; }
		/// <summary>The translation's stored HTML, sanitized on output.</summary>
		public string TranslatedHtml { get; set; }
		public bool CanEdit { get; set; }
		public bool CanApprove { get; set; }
		public bool SourceChanged { get; set; }
		public string Statement { get; set; }
		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string Message { get; set; }
		public string Error { get; set; }

		/// <summary>Round 7A: the wording shown is an unsaved Enhanced AI draft (the author reviews it and saves the draft translation).</summary>
		public bool IsAiDraft { get; set; }

		/// <summary>Round 7A: what the AI draft did (fields drafted, refused, left for another draft).</summary>
		public string AiNotice { get; set; }
	}
}
