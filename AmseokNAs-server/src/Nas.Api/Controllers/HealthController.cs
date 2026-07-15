//--------------------------//
//--------控制器仅处理健康检查的 HTTP 协议转换---------//
//--------Controllers only translate the health-check HTTP protocol--------//
//-------------------------//
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nas.Api.Contracts;

namespace Nas.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse("Healthy"));
    }
}
