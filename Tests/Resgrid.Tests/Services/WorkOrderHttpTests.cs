using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Web.Helpers;
using ApiController = Resgrid.Web.Services.Controllers.v4.WorkOrdersController;
using MvcController = Resgrid.Web.Areas.User.Controllers.WorkOrdersController;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options,logger,encoder) { }
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue("Test-Member", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
                var claims = new[] { new Claim(ClaimTypes.PrimarySid,user.ToString()),new Claim(ClaimTypes.PrimaryGroupSid,"77") };
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
            }
        }
        private sealed class WorkOrderBodyFilter : IResultFilter
        {
            public void OnResultExecuting(ResultExecutingContext c)
            {
                if (c.Result is ViewResult v) c.Result = new PartialViewResult { ViewName="/Areas/User/Views/"+(c.RouteData.Values["controller"]?.ToString()=="ReadinessProBilling"?"ReadinessProBilling":"WorkOrders")+"/"+(v.ViewName??c.RouteData.Values["action"])+".cshtml",ViewData=v.ViewData,TempData=v.TempData,StatusCode=v.StatusCode };
            }
            public void OnResultExecuted(ResultExecutedContext c) { }
        }
        [Test,NonParallelizable]
        public async Task Http_routes_bind_forms_enforce_CSRF_preserve_conflicts_and_localize_protected_failures()
        {
            Maintenance();
            _auth.Setup(a=>a.ChoicesAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(new WorkOrderChoices());
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while(directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName,"Resgrid.sln"))) directory=directory.Parent;
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath=Path.Combine(directory.FullName,"Web","Resgrid.Web"),EnvironmentName="Testing" });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddApiVersioning();
            const string scheme=OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            builder.Services.AddAuthentication(scheme).AddScheme<AuthenticationSchemeOptions,TestAuthentication>(scheme,_=>{});
            builder.Services.AddAuthorization();
            builder.Services.AddControllersWithViews(o=>o.Filters.Add(new WorkOrderBodyFilter())).AddApplicationPart(typeof(MvcController).Assembly).AddApplicationPart(typeof(ApiController).Assembly)
                .AddNewtonsoftJson(o=>o.SerializerSettings.ContractResolver=new Newtonsoft.Json.Serialization.DefaultContractResolver());
            builder.Services.AddSingleton<IWorkOrdersService>(_service); builder.Services.AddSingleton<IWorkOrderMaintenanceService>(_service); builder.Services.AddSingleton<IWorkOrderReportingService>(_service); builder.Services.AddSingleton(_auth.Object); builder.Services.AddSingleton(_access.Object);
            var departments=new Mock<IDepartmentsService>();
            departments.Setup(d=>d.GetDepartmentMemberAsync(It.IsAny<string>(),77,true)).ReturnsAsync((string user,int dept,bool fresh)=>new DepartmentMember {DepartmentId=77,UserId=user});
            departments.Setup(d=>d.GetDepartmentByIdAsync(77,true)).ReturnsAsync(new Department {DepartmentId=77,ManagingUserId="manager"});
            var billing=new Mock<IReadinessProBillingService>();billing.Setup(b=>b.GetAsync(77)).ReturnsAsync(new ReadinessProBillingStatus {Provider="Stripe",Currency="USD",MonthlyAmount=150,CheckoutAvailable=true});
            billing.Setup(b=>b.BeginCheckoutAsync(77)).ReturnsAsync(new ReadinessProCheckout {Provider="Stripe",Url="https://checkout.stripe.com/synthetic"});
            builder.Services.AddSingleton(departments.Object);builder.Services.AddSingleton(billing.Object);
            builder.Services.AddSingleton(Mock.Of<IProtectedGrantContext>()); builder.Services.AddSingleton(Mock.Of<IDepartmentDataProtectionService>());
            await using var app=builder.Build(); var previous=ClaimsAuthorizationHelper._httpContextAccessor; var previousApi=ApiClaims._httpContextAccessor;
            ClaimsAuthorizationHelper._httpContextAccessor=ApiClaims._httpContextAccessor=app.Services.GetRequiredService<IHttpContextAccessor>();
            app.Use(async(c,next)=>{try{await next();}catch(Exception ex){c.Response.StatusCode=500;await c.Response.WriteAsync(ex.ToString());}});
            app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("fr").AddSupportedCultures("fr").AddSupportedUICultures("fr"));
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapControllerRoute("areas","{area:exists}/{controller}/{action=Index}/{id?}");
            try
            {
                await app.StartAsync(); using var handler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new CookieContainer() };
                using var client=new HttpClient(handler){BaseAddress=new Uri(app.Urls.Single())};
                (await client.GetAsync("/User/WorkOrders/New")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                (await client.GetAsync("/api/v4/WorkOrders/GetWorkOrders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                client.DefaultRequestHeaders.Add("Test-Member","manager");
                var response=await client.GetAsync("/User/WorkOrders/New"); var html=await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK,html); WebUtility.HtmlDecode(html).Should().Contain("Nouvel ordre de travail").And.NotContain("New work order");
                var csrf=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
                var fields=new Dictionary<string,string> { ["Input.RequestId"]=Guid.NewGuid().ToString(),["Input.Content.Title"]="HTTP synthetic repair",["Input.Content.EstimatedCost"]="1234.56",["Input.Content.Currency"]="EUR",["Input.Priority"]="1",["Input.Type"]="0" };
                (await client.PostAsync("/User/WorkOrders/Save",new FormUrlEncodedContent(fields))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                fields["__RequestVerificationToken"]=csrf;
                response=await client.PostAsync("/User/WorkOrders/Save",new FormUrlEncodedContent(fields));
                response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync()); var id=JObject.Parse(await response.Content.ReadAsStringAsync())["id"].Value<int>();
                (await _service.GetAsync(_actor,id)).Input.Content.EstimatedCost.Should().Be(1234.56m);
                response=await client.GetAsync("/User/ReadinessProBilling");response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                (await client.PostAsync("/User/ReadinessProBilling/Checkout",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                response=await client.PostAsync("/User/ReadinessProBilling/Checkout",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=csrf,["DepartmentId"]="88"}));
                response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());billing.Verify(b=>b.BeginCheckoutAsync(77),Times.Once);billing.Verify(b=>b.BeginCheckoutAsync(88),Times.Never);
                client.DefaultRequestHeaders.Remove("Test-Member");client.DefaultRequestHeaders.Add("Test-Member","crew");
                (await client.GetAsync("/User/ReadinessProBilling")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
                client.DefaultRequestHeaders.Remove("Test-Member");client.DefaultRequestHeaders.Add("Test-Member","manager");
                response=await client.PostAsync("/api/v4/WorkOrders/NewWorkOrder",new StringContent("{\"Priority\":\"SYNTHETIC-PHI-CANARY\"}",Encoding.UTF8,"application/json"));
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);(await response.Content.ReadAsStringAsync()).Should().NotContain("CANARY");
                var reopen=new Dictionary<string,string>{["destination"]="Index",["Page"]="0",["Status"]="Requested",["__RequestVerificationToken"]=csrf};
                response=await client.PostAsync("/User/WorkOrders/Reopen",new FormUrlEncodedContent(reopen));
                response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                (await response.Content.ReadAsStringAsync()).Should().Contain("HTTP synthetic repair");
                foreach(var page in new[]{"Detail","Edit"})
                { response=await client.GetAsync("/User/WorkOrders/"+page+"?id="+id); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync()); }
                var body=new StringContent(JsonConvert.SerializeObject(new { Id=id,Input=new WorkOrderTransition {Revision=1,Status=WorkOrderStatus.Accepted}}),Encoding.UTF8,"application/json");
                response=await client.PostAsync("/api/v4/WorkOrders/SetWorkOrderStatus",body); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                response=await client.PostAsync("/api/v4/WorkOrders/SetWorkOrderStatus",body); response.StatusCode.Should().Be(HttpStatusCode.Conflict);
                response=await client.GetAsync("/User/WorkOrders/NewRecurrence"); html=await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK,html); html.Should().Contain("name=\"Input.Template.Content.Title\"");
                var pmFields=new Dictionary<string,string> { ["Input.Template.RequestId"]=Guid.NewGuid().ToString("D"),["Input.Template.Content.Title"]="Synthetic HTTP PM",["Input.Template.Content.Currency"]="EUR",
                    ["Input.AnchorLocal"]="2026-09-12T12:00",["Input.TimeZoneId"]="UTC",["Input.Calendar"]="3",["serviceStart"]="00:00",["serviceEnd"]="23:59",["Input.IsActive"]="true" };
                (await client.PostAsync("/User/WorkOrders/SaveRecurrence",new FormUrlEncodedContent(pmFields))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                pmFields["__RequestVerificationToken"]=csrf;
                response=await client.PostAsync("/User/WorkOrders/SaveRecurrence",new FormUrlEncodedContent(pmFields));
                response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync()); var pmId=JObject.Parse(await response.Content.ReadAsStringAsync())["id"].Value<int>();
                foreach(var pmPage in new[]{"Recurrences","Recurrence?id="+pmId,"EditRecurrence?id="+pmId})
                { response=await client.GetAsync("/User/WorkOrders/"+pmPage); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync()); }
                response=await client.GetAsync("/api/v4/WorkOrders/GetWorkOrderRecurrence?id="+pmId); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                (await client.GetAsync("/api/v4/WorkOrders/GetWorkOrderRecurrence?id=9999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
                var replayBody=new StringContent(JsonConvert.SerializeObject(new {Id=id,Input=new WorkOrderDeferralInput {Revision=2,DueOn=_maintenanceClock.Utc.AddDays(2),Reason="Approved"}}),Encoding.UTF8,"application/json");
                response=await client.PostAsync("/api/v4/WorkOrders/DeferWorkOrder",replayBody); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                foreach (var operationsPage in new[] { "Operations?id="+id, "Policy", "Bulk" })
                { response=await client.GetAsync("/User/WorkOrders/"+operationsPage); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync()); }
                var policyFields=new Dictionary<string,string> { ["Revision"]="0",["SpendingRules[0].Currency"]="EUR",["SpendingRules[0].Threshold"]="100.25",["Calendar.TimeZoneId"]="UTC",["businessStart"]="09:00",["businessEnd"]="17:00",["weekdays"]="2",["Calendar.Targets[0].Priority"]="1",["Calendar.Targets[0].ResponseMinutes"]="60",["Calendar.Targets[0].RepairMinutes"]="480" };
                (await client.PostAsync("/User/WorkOrders/SavePolicy",new FormUrlEncodedContent(policyFields))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                policyFields["__RequestVerificationToken"]=csrf;
                response=await client.PostAsync("/User/WorkOrders/SavePolicy",new FormUrlEncodedContent(policyFields)); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                (await _service.PolicyAsync(_actor)).SpendingRules.Single().Threshold.Should().Be(100.25m);
                response=await client.GetAsync("/User/WorkOrders/Policy"); (await response.Content.ReadAsStringAsync()).Should().Contain("value=\"100.25\"").And.NotContain("value=\"100,25\"");
                var csvInput=new { RequestId=Guid.NewGuid().ToString("D"),Csv=Resgrid.Services.WorkOrderCsvImport.Header+"\nSynthetic import,,0,1,,,,,USD,25,," };
                response=await client.PostAsync("/api/v4/WorkOrders/PreviewWorkOrderImport",new StringContent(JsonConvert.SerializeObject(csvInput),Encoding.UTF8,"application/json")); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                var previewJson=JObject.Parse(await response.Content.ReadAsStringAsync()); var batch=previewJson.SelectToken("Data.Input") ?? previewJson.SelectToken("data.input"); batch.Should().NotBeNull(previewJson.ToString());
                response=await client.PostAsync("/api/v4/WorkOrders/ApplyWorkOrderBatch",new StringContent(batch.ToString(),Encoding.UTF8,"application/json")); response.StatusCode.Should().Be(HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
                _store.All<WorkOrder>().Count(o=>o.RequestId!=null && o.Id!=id).Should().BeGreaterThan(0);
                _access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
                foreach (var reportPage in new[] { "/User/WorkOrders/Reports", "/User/WorkOrders/History", "/api/v4/WorkOrders/GetWorkOrderStats", "/api/v4/WorkOrders/GetWorkOrderServiceHistory", "/api/v4/WorkOrders/GetWorkOrderActivity?id=" + id, "/api/v4/WorkOrders/GetWorkOrderHolds?id=" + id })
                { response = await client.GetAsync(reportPage); response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); response.Headers.CacheControl.NoStore.Should().BeTrue(); }
                response = await client.GetAsync("/User/WorkOrders/Reports"); html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()); html.Should().Contain("Rapports de maintenance").And.NotContain("Maintenance reports");
                (await client.PostAsync("/User/WorkOrders/ExportCsv", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                response = await client.PostAsync("/User/WorkOrders/ExportCsv", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = csrf }));
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); response.Content.Headers.ContentType.MediaType.Should().Be("text/csv");
                response = await client.PostAsync("/api/v4/WorkOrders/ExportWorkOrderHistory", new StringContent("{}", Encoding.UTF8, "application/json")); response.StatusCode.Should().Be(HttpStatusCode.OK);
                response=await client.GetAsync("/api/v4/WorkOrders/GetWorkOrder?id="+id); response.StatusCode.Should().Be(HttpStatusCode.OK);
                response=await client.PostAsync("/api/v4/WorkOrders/NewWorkOrder",new StringContent(JsonConvert.SerializeObject(Input()),Encoding.UTF8,"application/json")); response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
                _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult {RedactedFields={"workorders.content"}}));
                response = await client.GetAsync("/api/v4/WorkOrders/GetWorkOrderStats"); response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                response = await client.GetAsync("/User/WorkOrders/Reports?FromUtc=2026-08-01T00:00:00Z&UntilUtc=2026-10-01T00:00:00Z");
                html = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.OK, html); html.Should().Contain("name=\"FromUtc\" value=\"2026-08-01").And.NotContain("HTTP synthetic repair");
                response=await client.GetAsync("/api/v4/WorkOrders/GetWorkOrder?id="+id); var protectedError=await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden); protectedError.Should().Contain("protected_data_required").And.NotContain("HTTP synthetic repair");
                response=await client.GetAsync("/User/WorkOrders/Index?Status=Accepted&Page=0"); html=await response.Content.ReadAsStringAsync();
                response.StatusCode.Should().Be(HttpStatusCode.OK,html); html.Should().Contain("value=\"Accepted\"").And.Contain("name=\"Page\" value=\"0\"").And.NotContain("HTTP synthetic repair");
            }
            finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor=previous;ApiClaims._httpContextAccessor=previousApi; }
        }
    }
}
