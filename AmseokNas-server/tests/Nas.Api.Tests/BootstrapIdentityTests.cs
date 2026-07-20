//--------------------------//
//--------本地测试验证初始管理员哈希与强制改密种子---------//
//--------Local tests verify the bootstrap administrator hash and forced-change seed--------//
//-------------------------//
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nas.Infrastructure.Persistence.Cluster;

namespace Nas.Api.Tests;

public sealed class BootstrapIdentityTests
{
    [Fact]
    public void SeededAdministratorUsesExpectedInitialPasswordAndRequiresChange()
    {
        var options = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        using var database = new ClusterDbContext(options);
        var userSeed = Assert.Single(
            database.GetService<IDesignTimeModel>()
                .Model
                .FindEntityType(typeof(NasUser))!
                .GetSeedData());
        var user = new NasUser { UserName = "admin" };
        var passwordHash = Assert.IsType<string>(userSeed[nameof(NasUser.PasswordHash)]);

        var result = new PasswordHasher<NasUser>()
            .VerifyHashedPassword(user, passwordHash, "AmseokNas");

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
        Assert.Equal(true, userSeed[nameof(NasUser.MustChangePassword)]);
        Assert.Equal("admin", userSeed[nameof(NasUser.UserName)]);
    }
}
