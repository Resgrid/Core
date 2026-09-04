using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>The public-records queue (RMS plan section 4.7): every open request with its statutory clock.</summary>
	public class DisclosureIndexView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public List<RmsDisclosureRequest> Requests { get; set; } = new List<RmsDisclosureRequest>();
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public int? StateFilter { get; set; }
		public List<SelectListItem> States { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RedactionProfiles { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();

		/// <summary>Who asked is restricted in most jurisdictions; without the grant the queue shows the reference only.</summary>
		public bool CanViewRestricted { get; set; }
	}

	/// <summary>One request: scope, preview of what it resolves to, and the productions already prepared.</summary>
	public class DisclosureDetailView : RecordsBaseView
	{
		public RmsDisclosureRequest Request { get; set; }
		public Department Department { get; set; }
		public RmsDisclosureScopePreview Preview { get; set; }
		public List<RmsDisclosureProduction> Productions { get; set; } = new List<RmsDisclosureProduction>();
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public bool CanViewRestricted { get; set; }
		public List<SelectListItem> RedactionProfiles { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> DefinitionKeys { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> RecordStates { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Dispositions { get; set; } = new List<SelectListItem>();

		public DisclosureScopeForm Scope { get; set; } = new DisclosureScopeForm();

		public RmsDisclosureState State => (RmsDisclosureState)Request.State;
		public bool IsOpen => Request.ClosedOn == null;

		/// <summary>The scope is frozen once anything has been produced against it.</summary>
		public bool CanEditScope => IsOpen && Productions.Count == 0;
	}

	/// <summary>The bounded Records query a production runs against.</summary>
	public class DisclosureScopeForm
	{
		public string RequestId { get; set; }
		public string ScopeNarrative { get; set; }
		public string RedactionProfile { get; set; }
		public string DefinitionKey { get; set; }
		public int? Year { get; set; }
		public int? CallId { get; set; }
		public List<int> States { get; set; } = new List<int>();
		public bool IncludeLegacy { get; set; }
	}

	/// <summary>The Records work queues an officer opens the module to look at (RMS-3).</summary>
	public class RecordsDashboardView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public RecordsDashboard Dashboard { get; set; }
		public NerisCrosswalkCoverage Coverage { get; set; }
		public bool CanManageDisclosures { get; set; }
		public bool IsDepartmentAdmin { get; set; }
	}
}
