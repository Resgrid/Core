using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// The public certificate verification page (LMS add-on, built dark: §24, §25.4a item 7): /verify/certificate/{code}. Shows the
	/// member's name, the course, the completion date, the department and the record's status, nothing else. NotFound for an unknown
	/// code and while the operator flag is off for the record's department (the service answers null then). It keeps answering after
	/// the add-on lapses (lapse rule 9). No-store, noindex (the layout).
	/// </summary>
	[AllowAnonymous]
	[Route("verify/certificate")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class CertificateVerifyController : Controller
	{
		private static readonly Regex CodeShape = new Regex("^[A-Za-z0-9-]{4,64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsTrainingService _lmsTraining;

		public CertificateVerifyController(ILmsTrainingService lmsTraining)
		{
			_lmsTraining = lmsTraining;
		}

		[HttpGet("{code}")]
		public async Task<IActionResult> Index(string code)
		{
			code = code?.Trim();
			if (string.IsNullOrEmpty(code) || !CodeShape.IsMatch(code))
				return NotFound();

			Resgrid.Model.CertificateVerification verification;
			try
			{
				verification = await _lmsTraining.VerifyCertificateAsync(code);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Certificate verification failed.");
				return NotFound();
			}

			if (verification == null)
				return NotFound();

			return View("Index", verification);
		}
	}
}
