using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Search;

namespace Resgrid.Services.Search
{
	/// <summary>
	/// The system-functionality catalog behind "search also finds features": every page and action the web app's
	/// navigation and command palette expose, with the claim / module / feature-flag gate the page itself applies.
	/// Paths are the MVC routes (Areas/User). Claim resource names mirror ResgridClaimTypes.Resources; they are string
	/// literals here because the services layer does not reference the claims provider.
	/// </summary>
	public static class SystemActionCatalog
	{
		public const string View = "View";
		public const string Create = "Create";
		public const string Update = "Update";

		// ResgridClaimTypes.Resources
		private const string Call = "Call";
		private const string Personnel = "Personnel";
		private const string Unit = "Unit";
		private const string Contacts = "Contacts";
		private const string Documents = "Documents";
		private const string Notes = "Notes";
		private const string Training = "Training";
		private const string Messages = "Messages";
		private const string Shift = "Shift";
		private const string Schedule = "Schedule";
		private const string Inventory = "Inventory";
		private const string Reports = "Reports";
		private const string Record = "Record";
		private const string Checklist = "Checklist";
		private const string WorkOrder = "WorkOrder";
		private const string Invoicing = "Invoicing";
		private const string Certifications = "Certifications";
		private const string Deployments = "Deployments";
		private const string Group = "Group";
		private const string Protocols = "Protocols";
		private const string Forms = "Forms";
		private const string Workflow = "Workflow";
		private const string Voice = "Voice";
		private const string Log = "Log";

		private static SystemActionDefinition Nav(string key, string title, string description, string path, string[] keywords = null,
			string claimResource = null, string claimAction = null, string module = null, string flag = null, bool adminOnly = false, bool hiddenWhenRecords = false)
		{
			return new SystemActionDefinition
			{
				Key = key,
				Title = title,
				Description = description,
				WebPath = path,
				Keywords = keywords ?? new string[0],
				Category = SystemActionCategories.Navigate,
				ClaimResource = claimResource,
				ClaimAction = claimAction,
				Module = module,
				FeatureFlag = flag,
				DepartmentAdminOnly = adminOnly,
				HiddenWhenRecordsEnabled = hiddenWhenRecords
			};
		}

		private static SystemActionDefinition Act(string key, string title, string description, string path, string category, string[] keywords = null,
			string claimResource = null, string claimAction = null, string module = null, string flag = null, bool adminOnly = false, bool hiddenWhenRecords = false)
		{
			var d = Nav(key, title, description, path, keywords, claimResource, claimAction, module, flag, adminOnly, hiddenWhenRecords);
			d.Category = category;
			return d;
		}

		public static readonly IReadOnlyList<SystemActionDefinition> All = new List<SystemActionDefinition>
		{
			// ---- Home / account
			Nav("dashboard", "Dashboard", "Department home: status, staffing and activity", "/User/Home/Dashboard", new[] { "home", "overview", "start" }),
			Act("profile", "My Profile", "View and edit your own profile, contact methods and notifications", "/User/Home/EditUserProfile?UserId={userId}", SystemActionCategories.Account, new[] { "account", "settings", "phone", "email", "password", "notifications" }),
			Act("departments", "Your Departments", "Switch between the departments you belong to", "/User/Profile/YourDepartments", SystemActionCategories.Account, new[] { "switch", "membership", "organizations" }),
			Act("two-factor", "Two-Factor Authentication", "Set up or manage two-factor sign-in", "/User/TwoFactor", SystemActionCategories.Account, new[] { "2fa", "mfa", "authenticator", "security", "passkey" }),

			// ---- Calls / dispatch
			Nav("calls", "Calls", "View calls and dispatches", "/User/Dispatch/Dashboard", new[] { "dispatch", "incidents", "active calls", "cad" }, Call, View),
			Act("new-call", "New Call", "Create and dispatch a new call", "/User/Dispatch/NewCall", SystemActionCategories.Create, new[] { "dispatch", "incident", "page", "alert", "create call" }, Call, Create),
			Nav("archived-calls", "Archived Calls", "Closed and historical calls", "/User/Dispatch/ArchivedCalls", new[] { "closed", "history", "old calls" }, Call, View),

			// ---- Personnel
			Nav("personnel", "Personnel", "People in the department, status and staffing", "/User/Personnel", new[] { "people", "members", "users", "staff", "responders", "roster" }, Personnel, View),
			Act("add-person", "Add Person", "Manually create a user account in the department", "/User/Personnel/AddPerson", SystemActionCategories.Create, new[] { "new user", "create user", "add member", "invite" }, Personnel, Create),
			Act("invites", "Manage Invites", "Send email invites so people create their own accounts", "/User/Department/Invites", SystemActionCategories.Manage, new[] { "invite", "email invite", "onboard" }, Personnel, Create),
			Nav("groups", "Groups & Stations", "Department groups and stations", "/User/Groups", new[] { "stations", "battalions", "teams", "groups" }, Group, View),
			Act("new-group", "New Group", "Create a group or station", "/User/Groups/NewGroup", SystemActionCategories.Create, new[] { "station", "add group" }, Group, Create),

			// ---- Units
			Nav("units", "Units", "Apparatus, vehicles and teams", "/User/Units", new[] { "apparatus", "vehicles", "trucks", "engines", "teams", "rigs" }, Unit, View),
			Act("new-unit", "New Unit", "Add an apparatus, vehicle or team", "/User/Units/NewUnit", SystemActionCategories.Create, new[] { "add unit", "apparatus", "vehicle" }, Unit, Create),
			Act("unit-staffing", "Unit Staffing", "Assign personnel to units", "/User/Units/UnitStaffing", SystemActionCategories.Manage, new[] { "crew", "assign", "staffing" }, Unit, Update),

			// ---- Contacts
			Nav("contacts", "Contacts", "People and organizations outside the department", "/User/Contacts", new[] { "businesses", "vendors", "customers", "address book", "pre-plans" }, Contacts, View),
			Act("new-contact", "New Contact", "Add a person or organization contact", "/User/Contacts/Add", SystemActionCategories.Create, new[] { "add contact", "company", "person" }, Contacts, Create),
			Nav("contact-categories", "Contact Categories", "Manage contact categories", "/User/Contacts/Categories", new[] { "categories" }, Contacts, Update),

			// ---- Mapping
			Nav("mapping", "Mapping", "Large map with layers, personnel and units", "/User/Mapping", new[] { "map", "gps", "avl", "location", "layers" }, module: SystemActionModules.Mapping),
			Nav("pois", "Points of Interest", "Manage map points of interest", "/User/Mapping/POIs", new[] { "poi", "hydrants", "landmarks", "map markers" }, module: SystemActionModules.Mapping),
			Nav("map-layers", "Map Layers", "Manage map layers", "/User/Mapping/Layers", new[] { "layers", "kml", "geojson" }, module: SystemActionModules.Mapping),
			Nav("live-routing", "Live Routing", "Routing and directions for active resources", "/User/Mapping/LiveRouting", new[] { "routes", "directions", "navigation" }, module: SystemActionModules.Mapping),

			// ---- Shifts / calendar
			Nav("shifts", "Shifts", "Shift signups, recurring shifts and trades", "/User/Shifts", new[] { "schedule", "signup", "trades", "workshift", "roster" }, Shift, View, SystemActionModules.Shifts),
			Nav("calendar", "Calendar", "Events, meetings and trainings you can sign up for", "/User/Calendar", new[] { "events", "meetings", "schedule", "rsvp" }, Schedule, View, SystemActionModules.Calendar),
			Act("new-calendar-item", "New Calendar Event", "Create a calendar event", "/User/Calendar/New", SystemActionCategories.Create, new[] { "event", "meeting", "schedule" }, Schedule, Create, SystemActionModules.Calendar),

			// ---- Logs (legacy) / Records
			Nav("logs", "Logs", "Run, training, work and meeting logs", "/User/Logs", new[] { "run log", "activity", "reports", "training log", "work log" }, Log, View, SystemActionModules.Logs, hiddenWhenRecords: true),
			Act("new-log", "New Log", "Create a run report, training log or work log", "/User/Logs/NewLog", SystemActionCategories.Create, new[] { "run report", "training log", "work log" }, Log, Create, SystemActionModules.Logs, hiddenWhenRecords: true),
			Nav("records", "Records", "Records queue: run reports, training and operational records", "/User/Records", new[] { "rms", "run reports", "incident reports", "neris", "logs" }, Record, View, SystemActionModules.Logs, FeatureFlagKeys.RecordsSystem),
			Nav("records-dashboard", "Records Dashboard", "Records due, submissions and quality at a glance", "/User/Records/Dashboard", new[] { "rms", "overview", "due" }, Record, View, SystemActionModules.Logs, FeatureFlagKeys.RecordsSystem),
			Act("records-settings", "Records Settings", "Lifecycle, numbering, search, retention and visibility settings for Records", "/User/Records/Settings", SystemActionCategories.Manage, new[] { "rms settings", "retention", "numbering" }, Record, View, SystemActionModules.Logs, FeatureFlagKeys.RecordsSystem, adminOnly: true),

			// ---- Reports
			Nav("reports", "Reports", "Generate reports from department data", "/User/Reports", new[] { "reporting", "export", "statistics", "analytics" }, Reports, View, SystemActionModules.Reports),

			// ---- Documents / notes / training
			Nav("documents", "Documents", "Upload and share documents", "/User/Documents", new[] { "files", "pdf", "word", "excel", "attachments", "sops" }, Documents, View, SystemActionModules.Documents),
			Act("new-document", "Upload Document", "Upload a new document", "/User/Documents/NewDocument", SystemActionCategories.Create, new[] { "upload", "file", "attach" }, Documents, Create, SystemActionModules.Documents),
			Nav("notes", "Notes", "Department notes: small bits of shared information", "/User/Notes", new[] { "memo", "bulletin", "announcements" }, Notes, View, SystemActionModules.Notes),
			Act("new-note", "New Note", "Post a department note", "/User/Notes/NewNote", SystemActionCategories.Create, new[] { "memo", "bulletin", "announce" }, Notes, Create, SystemActionModules.Notes),
			Nav("trainings", "Trainings", "Trainings, study guides and procedures", "/User/Trainings", new[] { "study guide", "quiz", "procedures", "education", "certification" }, Training, View, SystemActionModules.Training),
			Act("new-training", "New Training", "Create a training with optional quiz", "/User/Trainings/New", SystemActionCategories.Create, new[] { "quiz", "study", "course" }, Training, Create, SystemActionModules.Training),

			// ---- Inventory / readiness
			Nav("inventory", "Inventory", "Inventory for stations and units", "/User/Inventory", new[] { "stock", "supplies", "equipment", "assets", "consumables" }, Inventory, View, SystemActionModules.Inventory),
			Nav("inventory-status", "Inventory Status", "On-hand, low-stock and expiring inventory", "/User/Inventory/Status", new[] { "on hand", "low stock", "expiring", "counts" }, Inventory, View, SystemActionModules.Inventory),
			Act("inventory-transfer", "Transfer Inventory", "Move inventory between locations", "/User/Inventory/Transfer", SystemActionCategories.Manage, new[] { "move stock", "transfer" }, Inventory, Update, SystemActionModules.Inventory),
			Act("inventory-issue", "Issue Equipment", "Issue or return equipment to personnel", "/User/Inventory/Issue", SystemActionCategories.Manage, new[] { "issue", "return", "check out", "equipment" }, Inventory, Update, SystemActionModules.Inventory),
			Nav("checklists", "Checklists", "Apparatus, station and readiness checklists", "/User/Checklists", new[] { "truck check", "daily check", "readiness", "inspection" }, Checklist, View, null, FeatureFlagKeys.ChecklistsSystem),
			Act("new-checklist", "New Checklist", "Author a checklist definition", "/User/Checklists/New", SystemActionCategories.Create, new[] { "checklist definition", "template" }, Checklist, Update, null, FeatureFlagKeys.ChecklistsSystem),
			Nav("checklist-templates", "Checklist Templates", "Start from a checklist template", "/User/Checklists/Templates", new[] { "templates", "library" }, Checklist, View, null, FeatureFlagKeys.ChecklistsSystem),
			Nav("work-orders", "Work Orders", "Maintenance and repair work orders", "/User/WorkOrders", new[] { "maintenance", "repair", "service", "fleet", "defect" }, WorkOrder, View, SystemActionModules.Maintenance, FeatureFlagKeys.MaintenanceWorkOrders),
			Act("new-work-order", "New Work Order", "Open a maintenance work order", "/User/WorkOrders/New", SystemActionCategories.Create, new[] { "repair", "defect", "maintenance request" }, WorkOrder, Update, SystemActionModules.Maintenance, FeatureFlagKeys.MaintenanceWorkOrders),

			// ---- Business operations (Workforce & Business Operations plan, Phase B)
			Nav("invoices", "Invoices", "Customer invoices and payments", "/User/Invoicing", new[] { "invoice", "billing", "accounts receivable", "customer", "payment" }, Invoicing, View, SystemActionModules.BusinessOperations, FeatureFlagKeys.CustomerInvoicing),
			Act("new-invoice", "New Invoice", "Draft an invoice for a customer", "/User/Invoicing/New", SystemActionCategories.Create, new[] { "invoice", "bill", "charge" }, Invoicing, Create, SystemActionModules.BusinessOperations, FeatureFlagKeys.CustomerInvoicing),
			Nav("rate-cards", "Rate Cards", "Billing rates for units, personnel and fees", "/User/Invoicing/RateCards", new[] { "rates", "pricing", "hourly", "fees" }, Invoicing, View, SystemActionModules.BusinessOperations, FeatureFlagKeys.CustomerInvoicing),
			Nav("invoice-aging", "Accounts Receivable Aging", "Outstanding invoice balances by age", "/User/Invoicing/Aging", new[] { "aging", "overdue", "receivables", "outstanding" }, Invoicing, View, SystemActionModules.BusinessOperations, FeatureFlagKeys.CustomerInvoicing),
			Act("billing-settings", "Billing Settings", "Legal name, remit-to address and tax registrations printed on invoices", "/User/Invoicing/Settings", SystemActionCategories.Manage, new[] { "remit to", "tax id", "vat", "invoice footer" }, Invoicing, Update, SystemActionModules.BusinessOperations, FeatureFlagKeys.CustomerInvoicing),
			Act("online-payment-settings", "Online Payment Settings", "Connect your Stripe account to collect invoice payments online", "/User/Invoicing/Settings?tab=online", SystemActionCategories.Manage, new[] { "stripe", "pay online", "card", "ach", "payment link" }, Invoicing, Update, SystemActionModules.BusinessOperations, FeatureFlagKeys.OnlinePayments, adminOnly: true),

			// Workforce & Business Operations plan, Phase D (certifications; free). Records are never projected (decision 41).
			Nav("certification-dashboard", "Certification Dashboard", "Expiring and expired certifications for people and units", "/User/Certifications", new[] { "certification", "certs", "expiring", "expired", "license", "credential", "compliance", "dot inspection" }, Certifications, View),
			Nav("certification-types", "Certification Types", "The department's certification catalog and template gallery", "/User/Certifications/Types", new[] { "certification", "catalog", "types", "nremt", "cdl", "nwcg", "template" }, Certifications, "Setup"),
			Nav("certification-settings", "Certification Settings", "Enforcement mode, grace period and expiry notifications", "/User/Certifications/Settings", new[] { "certification", "enforcement", "grace", "expiry", "notifications" }, Certifications, "Setup"),
			Nav("my-certifications", "My Certifications", "Your own certification records", "/User/Profile/Certifications", new[] { "my certifications", "my certs", "my licenses", "credentials" }),

			// Workforce & Business Operations plan, Phase C (deployment core; free behind Operations.Deployments). DTRs, expenses and attachments are never projected (decision 41).
			Nav("deployments", "Deployment Finance", "Deployments, rosters, daily time reports and expenses", "/User/Deployments", new[] { "deployment", "deployments", "strike team", "mutual aid", "time report", "dtr", "shift ticket", "roster", "expenses" }, flag: FeatureFlagKeys.Deployments),
			Act("new-deployment", "New Deployment", "Create a deployment finance wrapper", "/User/Deployments/New", SystemActionCategories.Create, new[] { "deployment", "deploy", "strike team" }, Deployments, Update, flag: FeatureFlagKeys.Deployments),
			Act("deployment-from-external-order", "Deployment From External Order", "Create a deployment from an open RMS mutual-aid order", "/User/Deployments/FromExternalOrder", SystemActionCategories.Create, new[] { "external order", "mutual aid", "resource order", "deployment" }, Deployments, Update, flag: FeatureFlagKeys.Deployments),

			// ---- Messaging / chat
			Nav("inbox", "Inbox", "Your messages inbox", "/User/Messages/Inbox", new[] { "messages", "mail", "read" }, Messages, View, SystemActionModules.Messaging),
			Nav("outbox", "Sent Messages", "Messages you sent", "/User/Messages/Outbox", new[] { "sent", "outbox" }, Messages, View, SystemActionModules.Messaging),
			Act("compose-message", "New Message", "Send a message, poll or callback request", "/User/Messages/Compose", SystemActionCategories.Create, new[] { "send", "compose", "email", "poll", "callback", "broadcast" }, Messages, Create, SystemActionModules.Messaging),
			Nav("chat", "Chat", "Real-time department chat", "/User/Chat", new[] { "channels", "direct message", "dm", "team chat" }, flag: FeatureFlagKeys.ChatSystem),

			// ---- Automation / configuration
			Nav("workflows", "Workflows", "Automations triggered by department events", "/User/Workflows", new[] { "automation", "triggers", "webhooks", "integrations" }, Workflow, View),
			Act("new-workflow", "New Workflow", "Create an automation workflow", "/User/Workflows/New", SystemActionCategories.Create, new[] { "automation", "trigger" }, Workflow, Create),
			Nav("workflow-runs", "Workflow Runs", "Workflow execution history", "/User/Workflows/Runs", new[] { "runs", "history", "automation log" }, Workflow, View),
			Nav("protocols", "Protocols", "Dispatch protocols and procedures", "/User/Protocols", new[] { "dispatch protocols", "sop", "procedures" }, Protocols, View),
			Act("new-protocol", "New Protocol", "Create a dispatch protocol", "/User/Protocols/New", SystemActionCategories.Create, new[] { "protocol", "procedure" }, Protocols, Create),
			Nav("forms", "Forms", "Custom call and dispatch forms", "/User/Forms", new[] { "custom forms", "fields", "templates" }, Forms, View),
			Nav("voice", "Voice", "Voice channels and push-to-talk", "/User/Voice", new[] { "ptt", "push to talk", "radio", "audio" }, Voice, View),

			// ---- Department administration
			Act("department-settings", "Department Settings", "Department profile, address, API keys and module settings", "/User/Department", SystemActionCategories.Manage, new[] { "settings", "admin", "configuration", "modules", "api key" }, adminOnly: true),
			Act("call-settings", "Call Settings", "Call types, priorities, email import and dispatch settings", "/User/Department/CallSettings", SystemActionCategories.Manage, new[] { "call types", "priorities", "email import", "dispatch settings" }, adminOnly: true),
			Act("dispatch-settings", "Dispatch Settings", "Dispatch behaviour and notification settings", "/User/Department/DispatchSettings", SystemActionCategories.Manage, new[] { "dispatch", "notifications", "paging" }, adminOnly: true),
			Act("data-protection", "Data Protection", "Advanced Data Protection enrollment and policies", "/User/DataProtection", SystemActionCategories.Manage, new[] { "adp", "encryption", "privacy", "protected data", "kms" }, adminOnly: true)
		};
	}
}
