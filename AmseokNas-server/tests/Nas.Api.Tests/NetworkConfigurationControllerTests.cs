//--------------------------//
//--------验证网络配置预览与变更 HTTP 安全和结果映射---------//
//--------Verifies network preview and change HTTP security and result mapping--------//
//-------------------------//
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Nas.Api.Contracts;
using Nas.Api.Controllers;
using Nas.Application.Authentication;
using Nas.Application.NetworkConfiguration;

namespace Nas.Api.Tests;

public sealed class NetworkConfigurationControllerTests
{
    [Fact]
    public void PreviewRequiresNetworkManagePolicyAndAntiforgery()
    {
        var policy = typeof(NetworkConfigurationController)
            .GetCustomAttribute<AuthorizeAttribute>()?
            .Policy;
        var antiforgery = typeof(NetworkConfigurationController)
            .GetMethod(nameof(NetworkConfigurationController.CreatePreview))?
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();

        Assert.Equal(AuthenticationDefaults.NetworkManagePolicy, policy);
        Assert.NotNull(antiforgery);
    }

    [Theory]
    [InlineData(nameof(NetworkConfigurationController.Apply))]
    [InlineData(nameof(NetworkConfigurationController.Confirm))]
    [InlineData(nameof(NetworkConfigurationController.Rollback))]
    public void ChangeCommandsRequireAntiforgery(string methodName)
    {
        var antiforgery = typeof(NetworkConfigurationController)
            .GetMethod(methodName)?
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();

        Assert.NotNull(antiforgery);
    }

    [Fact]
    public async Task CreatedPreviewMapsNormalizedConfigurationWithoutPassword()
    {
        var service = new NetworkConfigurationServiceStub
        {
            Outcome = new NetworkConfigurationPreviewCreated(
                new NetworkConfigurationPreview(
                    "mac:00:11:22:33:44:55",
                    "enp1s0",
                    "dhcp",
                    ["192.168.1.10/24"],
                    "192.168.1.1",
                    new NormalizedNetworkConfiguration(
                        NetworkAddressingMode.StaticIpv4,
                        "192.168.1.20",
                        "255.255.255.0",
                        24,
                        "192.168.1.1"),
                    false,
                    ["network.write_unavailable"],
                    ["network.management_connection_may_be_interrupted"]))
        };
        var controller = CreateController(service);

        var result = await controller.CreatePreview(
            new CreateNetworkConfigurationPreviewRequest(
                "mac:00:11:22:33:44:55",
                "static",
                "192.168.1.20",
                "255.255.255.0",
                "192.168.1.1",
                "secret-not-returned"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<NetworkConfigurationPreviewResponse>(ok.Value);
        Assert.Equal("static", response.RequestedMode);
        Assert.Equal(24, response.RequestedPrefixLength);
        Assert.False(response.CanApply);
        Assert.DoesNotContain("secret-not-returned", response.ToString());
        Assert.Equal(1, service.CreationCount);
    }

    [Fact]
    public async Task UnsupportedModeIsRejectedBeforeCallingTheApplicationUseCase()
    {
        var service = new NetworkConfigurationServiceStub();
        var controller = CreateController(service);

        var result = await controller.CreatePreview(
            new CreateNetworkConfigurationPreviewRequest(
                "mac:00:11:22:33:44:55",
                "automatic",
                null,
                null,
                null,
                "secret"),
            CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problemResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal("NetworkConfigurationInvalid", problem.Extensions["code"]);
        Assert.Equal(0, service.CreationCount);
    }

    [Fact]
    public async Task ReauthenticationFailureReturnsUnauthorizedWithoutEchoingThePassword()
    {
        var service = new NetworkConfigurationServiceStub
        {
            Outcome = new NetworkConfigurationPreviewRejected(
                NetworkConfigurationPreviewFailure.ReauthenticationFailed,
                [])
        };
        var controller = CreateController(service);

        var result = await controller.CreatePreview(
            new CreateNetworkConfigurationPreviewRequest(
                "mac:00:11:22:33:44:55",
                "dhcp",
                null,
                null,
                null,
                "incorrect-secret"),
            CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(problemResult.Value);
        Assert.Equal("NetworkReauthenticationFailed", problem.Extensions["code"]);
        Assert.DoesNotContain("incorrect-secret", problem.ToString());
    }

    [Fact]
    public async Task ApplyReturnsAcceptedOperationWithoutEchoingPassword()
    {
        var operationId = Guid.NewGuid();
        var service = new NetworkConfigurationServiceStub
        {
            CommandOutcome = new NetworkConfigurationCommandSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.AwaitingConfirmation,
                    DateTimeOffset.UtcNow.AddMinutes(2)))
        };
        var controller = CreateController(service);

        var result = await controller.Apply(
            new ApplyNetworkConfigurationRequest(
                "mac:00:11:22:33:44:55",
                "static",
                "192.168.1.20",
                "255.255.255.0",
                "192.168.1.1",
                "secret-not-returned"),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<NetworkConfigurationOperationResponse>(accepted.Value);
        Assert.Equal(operationId, response.OperationId);
        Assert.Equal("awaitingConfirmation", response.State);
        Assert.DoesNotContain("secret-not-returned", response.ToString());
        Assert.Equal(1, service.ApplyCount);
    }

    [Fact]
    public async Task ApplyMapsUnavailableExecutorToServiceUnavailable()
    {
        var service = new NetworkConfigurationServiceStub
        {
            CommandOutcome = new NetworkConfigurationCommandRejected(
                NetworkConfigurationCommandFailure.ExecutorUnavailable,
                "network.write_unavailable",
                Retryable: false,
                [])
        };
        var controller = CreateController(service);

        var result = await controller.Apply(
            new ApplyNetworkConfigurationRequest(
                "mac:00:11:22:33:44:55",
                "dhcp",
                null,
                null,
                null,
                "secret"),
            CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(unavailable.Value);
        Assert.Equal("network.write_unavailable", problem.Extensions["code"]);
    }

    [Fact]
    public async Task ConfirmAndRollbackMapTheirTerminalStates()
    {
        var operationId = Guid.NewGuid();
        var service = new NetworkConfigurationServiceStub
        {
            ConfirmOutcome = new NetworkConfigurationCommandSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.Confirmed,
                    null)),
            RollbackOutcome = new NetworkConfigurationCommandSucceeded(
                new NetworkConfigurationOperation(
                    operationId,
                    NetworkConfigurationOperationState.RolledBack,
                    null))
        };
        var controller = CreateController(service);

        var confirmation = await controller.Confirm(operationId, CancellationToken.None);
        var rollback = await controller.Rollback(operationId, CancellationToken.None);

        var confirmationResponse = Assert.IsType<NetworkConfigurationOperationResponse>(
            Assert.IsType<OkObjectResult>(confirmation.Result).Value);
        var rollbackResponse = Assert.IsType<NetworkConfigurationOperationResponse>(
            Assert.IsType<OkObjectResult>(rollback.Result).Value);
        Assert.Equal("confirmed", confirmationResponse.State);
        Assert.Equal("rolledBack", rollbackResponse.State);
        Assert.Equal(1, service.ConfirmCount);
        Assert.Equal(1, service.RollbackCount);
    }

    private static NetworkConfigurationController CreateController(
        INetworkConfigurationService service)
    {
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))],
                    "test"))
        };
        return new NetworkConfigurationController(
            service,
            NullLogger<NetworkConfigurationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class NetworkConfigurationServiceStub : INetworkConfigurationService
    {
        public NetworkConfigurationPreviewOutcome? Outcome { get; init; }
        public NetworkConfigurationCommandOutcome? CommandOutcome { get; init; }
        public NetworkConfigurationCommandOutcome? ConfirmOutcome { get; init; }
        public NetworkConfigurationCommandOutcome? RollbackOutcome { get; init; }
        public int CreationCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task<NetworkConfigurationPreviewOutcome> CreatePreviewAsync(
            Guid userId,
            string password,
            RequestedNetworkConfiguration requested,
            CancellationToken cancellationToken)
        {
            CreationCount++;
            return Task.FromResult(
                Outcome
                    ?? new NetworkConfigurationPreviewRejected(
                        NetworkConfigurationPreviewFailure.InvalidConfiguration,
                        []));
        }

        public Task<NetworkConfigurationCommandOutcome> ApplyAsync(
            Guid userId,
            string password,
            RequestedNetworkConfiguration requested,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(CommandOutcome ?? Unavailable());
        }

        public Task<NetworkConfigurationCommandOutcome> ConfirmAsync(
            Guid userId,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmOutcome ?? Unavailable());
        }

        public Task<NetworkConfigurationCommandOutcome> RollbackAsync(
            Guid userId,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            RollbackCount++;
            return Task.FromResult(RollbackOutcome ?? Unavailable());
        }

        private static NetworkConfigurationCommandOutcome Unavailable()
        {
            return new NetworkConfigurationCommandRejected(
                NetworkConfigurationCommandFailure.ExecutorUnavailable,
                "network.write_unavailable",
                Retryable: false,
                []);
        }
    }
}
