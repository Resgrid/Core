using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
    [TestFixture, NonParallelizable]
    public class DeploymentWorkspaceHttpTests
    {
        private Mock<IDeploymentService> _operations;
        private Mock<IRecordDeploymentsService> _orders;
        private Mock<ITimeTrackingService> _time;
        private Deployment _deployment;
        private RecordDeploymentAggregate _order;
        private bool _enabled;

        [SetUp]
        public void Setup()
        {
            _enabled = true;
            _deployment = new Deployment { DeploymentId = "deployment", DepartmentId = 77, Name = "Synthetic deployment", Notes = "PRIVATE-NOTES",
                Personnel = new List<DeploymentPersonnel> { new DeploymentPersonnel { UserId = "member", AddedOn = DateTime.UtcNow } } };
            _order = new RecordDeploymentAggregate
            {
                Order = new RmsExternalOrder { RmsExternalOrderId = "order", DepartmentId = 77, OrderNumber = "=1+1", IncidentName = "Synthetic incident", ProfileKey = "generic", CreatedOn = DateTime.UtcNow },
                Fills = new List<RmsExternalOrderFill> { new RmsExternalOrderFill { RmsExternalOrderFillId = "fill", Status = 1, RequestNumber = "R1", RowVersion = 3 } }
            };
            _operations = new Mock<IDeploymentService>(); _orders = new Mock<IRecordDeploymentsService>(); _time = new Mock<ITimeTrackingService>();
            _operations.Setup(x => x.GetDeploymentByIdAsync("deployment", 77)).ReturnsAsync(_deployment);
            _operations.Setup(x => x.GetDeploymentsForUserAsync(77, It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((int _, string user, bool __) => user == "member" ? new List<Deployment> { _deployment } : new List<Deployment>());
            _operations.Setup(x => x.GetDeploymentsForDepartmentAsync(77, It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<Deployment> { _deployment });
            _operations.Setup(x => x.CountDeploymentsForDepartmentAsync(77, It.IsAny<bool>())).ReturnsAsync(1);
            _operations.Setup(x => x.GetAttachmentsAsync("deployment", 77)).ReturnsAsync(new List<DeploymentAttachment>());
            _operations.Setup(x => x.RenderManifestHtmlAsync("deployment", 77)).ReturnsAsync("<html><body>Manifest</body></html>");
            _operations.Setup(x => x.CreateFromExternalOrderAsync(77, It.IsAny<ExternalOrderDeploymentInput>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(_deployment);
            _operations.Setup(x => x.SynchronizeExternalOrderAsync("order", 77, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(_deployment);
            _orders.Setup(x => x.ListAsync(77, It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(new List<RmsExternalOrder> { _order.Order });
            _orders.Setup(x => x.GetAsync(77, It.IsAny<string>(), "order", It.IsAny<bool>())).ReturnsAsync(_order);
            _orders.Setup(x => x.CreateFromExternalOrderAsync(77, It.IsAny<string>(), It.IsAny<RecordDeploymentCreateInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(_order);
            _orders.Setup(x => x.TransitionFillAsync(77, It.IsAny<string>(), "fill", It.IsAny<RecordDeploymentFillTransitionInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(_order.Fills[0]);
            _time.Setup(x => x.GetTimeReportsAsync("deployment", 77)).ReturnsAsync(new List<DeploymentTimeReport>());
            _time.Setup(x => x.GetTimeReportsWithEntriesAsync("deployment", 77)).ReturnsAsync(new List<DeploymentTimeReport>
            {
                new DeploymentTimeReport { DeploymentTimeReportId = "time", ReportNumber = 41, Entries = new List<DeploymentTimeEntry> { new DeploymentTimeEntry { MileageKm = 123.5m } } }
            });
            _time.Setup(x => x.GetExpensesAsync("deployment", 77)).ReturnsAsync(new List<DeploymentExpense>());
            _time.Setup(x => x.ExportTimeEntriesCsvAsync("deployment", 77)).ReturnsAsync("Subject,Hours\r\nMember,8\r\n");
        }

        [Test]
        public async Task Reporting_pages_render_read_only_for_rostered_members_and_exclude_internal_notes()
        {
            await WithServer(async client =>
            {
                SignIn(client, "member");
                foreach (var route in new[] { "Index", "Report?id=deployment", "Print?id=deployment", "Details?id=order" })
                {
                    var response = await client.GetAsync("/User/RecordDeployments/" + route);
                    var html = await response.Content.ReadAsStringAsync();
                    response.StatusCode.Should().Be(HttpStatusCode.OK, html);
                    html.Should().NotContain("method=\"post\"").And.NotContain("Preview").And.NotContain("PRIVATE-NOTES");
                    response.Headers.CacheControl.NoStore.Should().BeTrue();
                    html.Should().Contain(route.StartsWith("Details") ? "Synthetic incident" : "Synthetic deployment");
                }
            });
            _operations.Verify(x => x.SaveDeploymentAsync(It.IsAny<Deployment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Report_index_hides_linked_orders_without_reloading_their_deployment()
        {
            // The linked deployment returned by the order lookup already carries its roster; it is not loaded a second time.
            _operations.Setup(x => x.GetDeploymentByExternalOrderIdAsync("order", 77)).ReturnsAsync(_deployment);
            await WithServer(async client =>
            {
                SignIn(client, "member");
                var html = await client.GetStringAsync("/User/RecordDeployments/Index");
                html.Should().NotContain("Synthetic incident");
                SignIn(client, "outsider");
                (await client.GetStringAsync("/User/RecordDeployments/Index")).Should().Contain("Synthetic incident");
            });
            _operations.Verify(x => x.GetDeploymentByIdAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Test]
        public async Task Report_downloads_obey_membership_and_export_permission()
        {
            await WithServer(async client =>
            {
                SignIn(client, "outsider");
                foreach (var action in new[] { "Report", "Export", "ExportTimeEntries", "Manifest" })
                    (await client.GetAsync($"/User/RecordDeployments/{action}?id=deployment")).StatusCode.Should().Be(HttpStatusCode.NotFound);
                SignIn(client, "member");
                var json = await client.GetAsync("/User/RecordDeployments/Export?id=deployment");
                json.StatusCode.Should().Be(HttpStatusCode.OK);
                (await json.Content.ReadAsStringAsync()).Should().Contain("Synthetic deployment").And.NotContain("PRIVATE-NOTES").And.Contain("41").And.Contain("123.5");
                client.DefaultRequestHeaders.Add("Test-No-Export", "true");
                (await client.GetAsync("/User/RecordDeployments/Export?id=deployment")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            });
        }

        [Test]
        public async Task Old_RMS_commands_are_not_routable_and_readers_cannot_enter_operations()
        {
            await WithServer(async client =>
            {
                SignIn(client, "member");
                foreach (var action in new[] { "New", "AddFill", "Transition", "Snapshot", "Closeout" })
                    (await client.PostAsync("/User/RecordDeployments/" + action, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
                (await client.GetAsync("/User/DeploymentOrders/Index")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            });
            _orders.Verify(x => x.CloseoutAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Creating_an_external_order_from_operations_links_it_and_opens_the_deployment()
        {
            await WithServer(async client =>
            {
                SignIn(client, "manager");
                var form = await Form(client, "/User/DeploymentOrders/New");
                form["ProfileKey"] = "generic"; form["OrderNumber"] = "ORDER-1"; form["IncidentName"] = "Synthetic incident";
                form["IdempotencyKey"] = "creation-retry";
                var response = await client.PostAsync("/User/DeploymentOrders/New", new FormUrlEncodedContent(form));
                response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
                response.Headers.Location.ToString().Should().Contain("/Deployments/View/deployment");
            });
            _orders.Verify(x => x.CreateFromExternalOrderAsync(77, "manager", It.Is<RecordDeploymentCreateInput>(i => i.IdempotencyKey == "creation-retry"), It.IsAny<CancellationToken>()), Times.Once);
            _operations.Verify(x => x.CreateFromExternalOrderAsync(77, It.Is<ExternalOrderDeploymentInput>(i => i.RmsExternalOrderId == "order" && i.PrefillRoster), "manager", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Resource_transition_requires_csrf_and_matching_order_and_preserves_row_version()
        {
            await WithServer(async client =>
            {
                SignIn(client, "manager");
                var response = await client.PostAsync("/User/DeploymentOrders/Transition?id=order", new FormUrlEncodedContent(new Dictionary<string, string> { ["fillId"] = "fill", ["status"] = "2" }));
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var form = await Form(client, "/User/DeploymentOrders/Details?id=order");
                form["fillId"] = "foreign-fill"; form["status"] = "2"; form["rowVersion"] = "3";
                (await client.PostAsync("/User/DeploymentOrders/Transition?id=order", new FormUrlEncodedContent(form))).StatusCode.Should().Be(HttpStatusCode.NotFound);
                form["fillId"] = "fill";
                (await client.PostAsync("/User/DeploymentOrders/Transition?id=order", new FormUrlEncodedContent(form))).StatusCode.Should().Be(HttpStatusCode.Redirect);
            });
            _orders.Verify(x => x.TransitionFillAsync(77, "manager", "fill", It.Is<RecordDeploymentFillTransitionInput>(i => i.ExpectedRowVersion == 3 && i.Status == RmsDeploymentFillStatus.Accepted), It.IsAny<CancellationToken>()), Times.Once);
            _operations.Verify(x => x.SynchronizeExternalOrderAsync("order", 77, "manager", null, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Reports_remain_readable_with_operations_disabled_and_resource_csv_escapes_formulas()
        {
            _enabled = false;
            await WithServer(async client =>
            {
                SignIn(client, "manager");
                (await client.GetAsync("/User/DeploymentOrders/Index")).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await client.GetAsync("/User/RecordDeployments/Report?id=deployment")).StatusCode.Should().Be(HttpStatusCode.OK);
                var csv = await client.GetStringAsync("/User/RecordDeployments/ExportOrders?id=order");
                csv.Should().Contain("\"'=1+1\"");
            });
        }

        [Test]
        public async Task Generic_Records_authoring_redirects_to_operations_and_rejects_deployment_writes()
        {
            await WithServer(async client =>
            {
                SignIn(client, "manager");
                var create = await client.GetAsync("/User/Records/New?definitionKey=mutual-aid.deployment");
                create.StatusCode.Should().Be(HttpStatusCode.Redirect);
                create.Headers.Location.ToString().Should().Contain("/DeploymentOrders/New");
                var read = await client.GetAsync("/User/Records/Details/deployment-record");
                read.StatusCode.Should().Be(HttpStatusCode.Redirect);
                read.Headers.Location.ToString().Should().Contain("/RecordDeployments/ForRecord");
                var edit = await client.GetAsync("/User/Records/Edit/deployment-record");
                edit.Headers.Location.ToString().Should().Contain("/DeploymentOrders/ForRecord");
                var form = await Form(client, "/User/DeploymentOrders/New");
                form["RecordId"] = "deployment-record";
                form["DefinitionKey"] = "mutual-aid.deployment";
                var write = await client.PostAsync("/User/Records/Edit", new FormUrlEncodedContent(form));
                write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            });
        }

        [Test]
        public void API_commands_use_operational_routes_and_deployment_management_policy()
        {
            var controller = typeof(Resgrid.Web.Services.Controllers.v4.RecordDeploymentsController);
            foreach (var name in new[] { "Create", "AddFill", "TransitionFill", "Snapshot", "Closeout" })
            {
                var method = controller.GetMethod(name);
                method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>().Single().Template.Should().StartWith("~/api/v{VersionId:apiVersion}/DeploymentOrders/");
                method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Should().Contain(a => a.Policy == ResgridResources.Deployments_Update);
            }
        }

        [Test]
        public async Task Summary_export_reads_every_page_instead_of_truncating_at_the_first_page()
        {
            var first = Enumerable.Range(0, 200).Select(i => new Deployment { DeploymentId = i.ToString(), DepartmentId = 77, Name = "Row " + i }).ToList();
            _operations.Setup(x => x.GetDeploymentsForDepartmentAsync(77, false, 0, 200)).ReturnsAsync(first);
            _operations.Setup(x => x.GetDeploymentsForDepartmentAsync(77, false, 200, 200)).ReturnsAsync(new List<Deployment> { new Deployment { DeploymentId = "last", DepartmentId = 77, Name = "Final row" } });
            await WithServer(async client =>
            {
                SignIn(client, "manager");
                var csv = await client.GetStringAsync("/User/RecordDeployments/ExportSummary");
                csv.Should().Contain("Final row");
                csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(202);
            });
        }

        [Test]
        public async Task Documents_cannot_be_downloaded_through_an_unrelated_deployment()
        {
            _time.Setup(x => x.GetTimeReportByIdAsync("foreign-time", 77)).ReturnsAsync(new DeploymentTimeReport { DeploymentTimeReportId = "foreign-time", DeploymentId = "hidden" });
            _operations.Setup(x => x.GetAttachmentAsync(9, 77, false)).ReturnsAsync(new DeploymentAttachment { DeploymentAttachmentId = 9, DeploymentId = "hidden" });
            await WithServer(async client =>
            {
                SignIn(client, "member");
                (await client.GetAsync("/User/RecordDeployments/TimeReport?id=foreign-time")).StatusCode.Should().Be(HttpStatusCode.NotFound);
                (await client.GetAsync("/User/RecordDeployments/Attachment?id=9")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            });
            _operations.Verify(x => x.GetAttachmentAsync(9, 77, true), Times.Never);
            _time.Verify(x => x.RenderTimeReportHtmlAsync("foreign-time", 77), Times.Never);
        }

        private static void SignIn(HttpClient client, string user)
        {
            client.DefaultRequestHeaders.Remove("Test-Member"); client.DefaultRequestHeaders.Add("Test-Member", user);
        }
        private static async Task<Dictionary<string, string>> Form(HttpClient client, string url)
        {
            var response = await client.GetAsync(url); var html = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, html);
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            match.Success.Should().BeTrue(html);
            return new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) };
        }

        private async Task WithServer(Func<HttpClient, Task> test)
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddHttpContextAccessor(); builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, DeploymentAuthentication>("test", _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy(ResgridResources.Record_View, p => p.RequireAuthenticatedUser());
                options.AddPolicy(ResgridResources.Record_Create, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create));
                options.AddPolicy(ResgridResources.Record_Export, p => p.RequireClaim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export));
                options.AddPolicy(ResgridResources.Deployments_Update, p => p.RequireClaim(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.Update));
            });
            builder.Services.AddControllersWithViews(o => o.Filters.Add(new BodyOnly())).AddApplicationPart(typeof(RecordDeploymentsController).Assembly);
            builder.Services.AddSingleton(_operations.Object); builder.Services.AddSingleton(_orders.Object); builder.Services.AddSingleton(_time.Object);
            var departments = new Mock<IDepartmentsService>();
            departments.Setup(x => x.GetDepartmentByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 77, Name = "Synthetic department", TimeZone = "Pacific Standard Time" });
            departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(77)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "member", FirstName = "Test", LastName = "Member" } });
            var cutover = new Mock<IRecordsCutoverService>(); cutover.Setup(x => x.GetModuleStateAsync(77, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });
            var flags = new Mock<IFeatureToggleService>(); flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.Deployments, 77, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(() => _enabled);
            builder.Services.AddSingleton(departments.Object); builder.Services.AddSingleton(cutover.Object); builder.Services.AddSingleton(flags.Object);
            builder.Services.AddSingleton(Mock.Of<IDepartmentGroupsService>()); builder.Services.AddSingleton(Mock.Of<IUnitsService>());
            var records = new Mock<IRecordsService>();
            records.Setup(x => x.GetAsync(77, "deployment-record", It.IsAny<bool>())).ReturnsAsync(new RecordAggregate
            { Record = new RmsOperationalRecord { RmsOperationalRecordId = "deployment-record", DepartmentId = 77, DefinitionKey = "mutual-aid.deployment" } });
            var authorization = new Mock<IRecordsAuthorizationService>();
            authorization.Setup(x => x.CanUserViewRecordAsync("manager", "deployment-record", 77)).ReturnsAsync(true);
            var definitions = new Mock<IRecordDefinitionsService>();
            definitions.Setup(x => x.GetAsync(77, "mutual-aid.deployment")).ReturnsAsync(new RecordDefinitionAggregate
            { Definition = new RmsRecordDefinition { TemplateKey = "pack.mutual-aid.deployment" } });
            builder.Services.AddSingleton(records.Object); builder.Services.AddSingleton(authorization.Object); builder.Services.AddSingleton(definitions.Object);
            foreach (var parameter in typeof(RecordsController).GetConstructors().Single().GetParameters())
            {
                var type = parameter.ParameterType;
                if (builder.Services.Any(s => s.ServiceType == type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Localization.IStringLocalizer<>))) continue;
                var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type));
                builder.Services.AddSingleton(type, mock.Object);
            }
            await using var app = builder.Build();
            var previous = ClaimsAuthorizationHelper._httpContextAccessor;
            ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
            app.UseDeveloperExceptionPage();
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
            try
            {
                await app.StartAsync();
                using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
                await test(client);
            }
            finally { await app.StopAsync(); ClaimsAuthorizationHelper._httpContextAccessor = previous; }
        }

        private class BodyOnly : IResultFilter
        {
            public void OnResultExecuting(ResultExecutingContext context)
            {
                if (context.Result is not ViewResult view) return;
                var action = (ControllerActionDescriptor)context.ActionDescriptor;
                context.Result = new PartialViewResult { ViewName = $"/Areas/User/Views/{action.ControllerName}/{view.ViewName ?? action.ActionName}.cshtml", ViewData = view.ViewData, TempData = view.TempData };
            }
            public void OnResultExecuted(ResultExecutedContext context) { }
        }
        private class DeploymentAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public DeploymentAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue("Test-Member", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
                var claims = new List<Claim> { new Claim(ClaimTypes.PrimarySid, user.ToString()), new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim(ClaimTypes.PrimaryGroupSid, "77") };
                if (Request.Headers["Test-No-Export"] != "true") claims.Add(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export));
                if (user == "manager")
                {
                    claims.Add(new Claim(ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create));
                    claims.Add(new Claim(ResgridClaimTypes.Resources.Deployments, ResgridClaimTypes.Actions.Update));
                }
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
            }
        }
    }
}
