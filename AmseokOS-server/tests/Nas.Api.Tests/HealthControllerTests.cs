//--------------------------//
//--------本地 API 测试覆盖公开 HTTP 控制器契约---------//
//--------Local API tests cover public HTTP controller contracts--------//
//-------------------------//
using Microsoft.AspNetCore.Mvc;
using Nas.Api.Contracts;
using Nas.Api.Controllers;

namespace Nas.Api.Tests;

public sealed class HealthControllerTests
{
    [Fact]
    public void GetReturnsHealthyStatus()
    {
        var controller = new HealthController();

        var result = controller.Get();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.Equal("Healthy", response.Status);
    }
}
