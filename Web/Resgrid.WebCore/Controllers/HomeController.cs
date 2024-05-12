using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Models;

namespace Resgrid.Web.Controllers
{
	public class HomeController : Controller
	{
		private readonly IEmailService _emailService;

		public HomeController(IEmailService emailService)
		{
			_emailService = emailService;
		}

		public IActionResult Index()
		{
			return RedirectToAction("LogOn", "Account");
		}

		public IActionResult About()
		{
			return RedirectToAction("LogOn", "Account");
		}

		public IActionResult Contact()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			ContactView model = new ContactView();

			return View(model);
		}

		public IActionResult Error()
		{
			return View();
		}

		public IActionResult Apps()
		{
			return RedirectToAction("LogOn", "Account");
		}

		public IActionResult Features()
		{
			return RedirectToAction("LogOn", "Account");
		}

		public IActionResult OpenSource()
		{
			return RedirectToAction("LogOn", "Account");
		}

		[HttpPost, ValidateAntiForgeryToken]
		public IActionResult Contact(ContactView model)
		{
			CaptchaResponse response = ValidateCaptcha(Request.Form["g-recaptcha-response"]);

			if (response.Success && ModelState.IsValid)
			{
				try
				{
					EmailNotification email = new EmailNotification();
					email.Name = model.Name;
					email.Subject = "Message from Resgrid Contact Form";
					email.From = model.Email;
					email.Body = model.Message;

					_emailService.Notify(email);

					model.Result = "Your message has been sent. We will get back to you within 48 hours M-F.";
					model.Name = String.Empty;
					model.Email = String.Empty;
					model.Message = String.Empty;
				}
				catch (Exception ex)
				{

				}


				return View(model);
			}

			return View(model);
		}

		public static CaptchaResponse ValidateCaptcha(string response)
		{
			var client = new WebClient();
			var jsonResult = client.DownloadString(string.Format("https://www.google.com/recaptcha/api/siteverify?secret={0}&response={1}", Resgrid.Config.WebConfig.RecaptchaPrivateKey, response));
			return JsonConvert.DeserializeObject<CaptchaResponse>(jsonResult.ToString());
		}

		public IActionResult Pricing()
		{
			return RedirectToAction("LogOn", "Account");
		}

		public IActionResult Why()
		{
			return RedirectToAction("LogOn", "Account");
		}

	}

	public class CaptchaResponse
	{
		[JsonProperty("success")]
		public bool Success
		{
			get;
			set;
		}
		[JsonProperty("error-codes")]
		public List<string> ErrorMessage
		{
			get;
			set;
		}
	}
}
