using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Chatbot.Interfaces;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Web.Services.Controllers
{
    // Preserve existing Telegram webhook registrations while sharing verified native intake.
    [AllowAnonymous]
    [Route("api/v{VersionId:apiVersion}/[controller]")]
    [ApiVersion("4.0")]
    [ApiExplorerSettings(GroupName = "v4")]
    [RequestSizeLimit(262144)]
    public class ChatbotTelegramController : ControllerBase
    {
        private readonly IQueueService _queue;
        private readonly IChatbotAdapterRegistry _registry;
        private readonly ChatbotJwtValidator _jwt;
        private readonly IIntakeChannelsService _intakeChannels;
        private readonly IIntakeService _intake;
        public ChatbotTelegramController(IQueueService queue, IChatbotAdapterRegistry registry, ChatbotJwtValidator jwt,
            IIntakeChannelsService intakeChannels, IIntakeService intake)
        { _queue = queue; _registry = registry; _jwt = jwt; _intakeChannels = intakeChannels; _intake = intake; }
        [HttpPost("Webhook")]
        public Task<IActionResult> Webhook() => new ChatbotPlatformsController(_queue, _registry, _jwt, _intakeChannels, _intake)
        { ControllerContext = ControllerContext }.Receive("Telegram");
    }
}
