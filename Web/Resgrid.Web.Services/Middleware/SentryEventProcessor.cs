using Microsoft.AspNetCore.Http;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.ServicesCore.Helpers;
using Sentry;
using Sentry.Extensibility;
using System;

namespace Resgrid.Web.ServicesCore.Middleware
{
	public class SentryEventProcessor : ISentryEventProcessor
	{
		private readonly IHttpContextAccessor _httpContext;

		public SentryEventProcessor(IHttpContextAccessor httpContext) => _httpContext = httpContext;

		public SentryEvent Process(SentryEvent @event)
		{
			@event.SetExtra("Response:HasStarted", _httpContext.HttpContext?.Response.HasStarted);

			try
			{
				var user = new SentryUser();
				user.Username = ClaimsAuthorizationHelper.GetUsername();
				user.Id = ClaimsAuthorizationHelper.GetUserId();
				user.IpAddress = IpAddressHelper.GetRequestIP(_httpContext.HttpContext?.Request, true);
				user.Other.Add("Name", ClaimsAuthorizationHelper.GetFullName());
				user.Other.Add("Department", ClaimsAuthorizationHelper.GetDepartmentName());
				user.Other.Add("DepartmentId", ClaimsAuthorizationHelper.GetDepartmentId().ToString());
				@event.User = user;
			} catch (Exception ex) { }

			return @event;
		}
	}
}
