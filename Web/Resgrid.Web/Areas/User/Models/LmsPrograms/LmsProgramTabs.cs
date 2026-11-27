using System;
using System.Globalization;
using Resgrid.Localization.Areas.User.LmsExports;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Models.LmsPrograms
{
	/// <summary>
	/// The Trainings → Programs strip (Shared/_ModuleTabs), shared by the Programs pages (LmsProgramsController) and the data export and CE
	/// reporting pages (LmsExportsController, round 6D), so both show the same sections with the current one highlighted.
	/// </summary>
	public static class LmsProgramTabs
	{
		/// <summary>
		/// <paramref name="text"/> reads a Trainings resource; <paramref name="active"/> is "Controller/Action" or a Programs action name.
		/// Data export shows for department owners and admins; CE reporting for officers.
		/// </summary>
		public static ModuleTabsView Build(Func<string, string> text, string active, bool officer, bool certifications, bool departmentAdmin)
		{
			var tabs = new ModuleTabsView { Label = text("Prg.StripLabel") };
			bool IsActive(string controller, string action) =>
				string.Equals(active, action, StringComparison.Ordinal) && controller == "LmsPrograms" || string.Equals(active, controller + "/" + action, StringComparison.Ordinal);
			void Add(string label, string icon, string controller, string action) => tabs.Tabs.Add(new ModuleTab(label, icon, controller, action, IsActive(controller, action)));

			Add(text("Prg.Tab.Home"), "fa-th-large", "LmsPrograms", "Index");
			if (officer)
				Add(text("Prg.Tab.Plans"), "fa-calendar", "LmsPrograms", "Plans");
			Add(text("Prg.Tab.Frameworks"), "fa-sitemap", "LmsPrograms", "Frameworks");
			Add(text("Prg.Tab.Requests"), "fa-paper-plane-o", "LmsPrograms", "Requests");
			if (officer)
				Add(text("Prg.Tab.Reports"), "fa-table", "LmsPrograms", "Reports");
			if (certifications)
				Add(text("Prg.Tab.Cycles"), "fa-refresh", "LmsPrograms", "Cycles");
			// Round 9A: certification requirement sets (the certification view right reads them; managing them edits).
			if (certifications || ClaimsAuthorizationHelper.CanViewCertifications())
				Add(text("CertReq.Tab"), "fa-list-ol", "LmsPrograms", "CertificationRequirements");
			if (officer)
				Add(text("Prg.Tab.Settings"), "fa-cog", "LmsPrograms", "Settings");
			// Training T5 (round 6D): CE files for officers, the department data export for owners and admins.
			var culture = CultureInfo.CurrentUICulture.Name;
			if (officer)
				Add(LmsExportTexts.Get("Ce.Tab", culture), "fa-id-badge", "LmsExports", "Ce");
			if (departmentAdmin)
				Add(LmsExportTexts.Get("Exit.Tab", culture), "fa-archive", "LmsExports", "Index");
			return tabs;
		}
	}
}
