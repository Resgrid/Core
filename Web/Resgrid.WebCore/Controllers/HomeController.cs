using System;
using Microsoft.AspNetCore.Mvc;
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
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

		public IActionResult About()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
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
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

		public IActionResult Features()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

		public IActionResult OpenSource()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

		[HttpPost, ValidateAntiForgeryToken]
		public IActionResult Contact(ContactView model)
		{
			if (ModelState.IsValid)
			{
				try
				{
					EmailNotification email = new EmailNotification();
					email.Name = model.Name;
					email.Subject = "Message from Resgrid Contact Form";
					email.From = model.Email;
					email.Body = model.Message;

					_emailService.Notify(email);

					model.Result = "Your message has been sent.";
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

		public IActionResult Pricing()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

		public IActionResult Why()
		{
			if (Config.SystemBehaviorConfig.RedirectHomeToLogin)
				return RedirectToAction("LogOn", "Account");

			return View();
		}

	}
}
