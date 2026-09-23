using NUglify.JavaScript;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// NUglify settings for the Release-only WebOptimizer minification of /js/app/**/*.js and /js/site.js.
	///
	/// InvertIfReturn must stay disabled. It turns `if (!x) return; const y = ...;` into `if (x) { const y = ...; }`
	/// but leaves sibling function declarations outside that block, so they reference a const that is no longer in
	/// scope. After local renaming that is either a ReferenceError (RESGRID-WEB-1MP, inventory-operations.js) or a
	/// silent rebind to an unrelated outer name (RESGRID-WEB-1MK, checklists.js), and it only happens in Release.
	/// ScriptMinificationSettingsTests fails if the kill switch is dropped.
	/// </summary>
	public static class ScriptMinificationSettings
	{
		public static CodeSettings Create()
		{
			return new CodeSettings { KillSwitch = (long)TreeModifications.InvertIfReturn };
		}
	}
}
