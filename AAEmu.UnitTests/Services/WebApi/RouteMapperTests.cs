// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
﻿using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Services.WebApi.Controllers;
using NetCoreServer;
namespace AAEmu.UnitTests.Services.WebApi;

public class RouteMapperTests
{

    [Test]
    public Task GetRoute_WhenSimpleRoute_ShouldFindAndMatch()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute("/world/logged-characters", HttpMethod.Get);

        // Assert
        await Assert.That(route).IsNotNull();
        await Assert.That(matches).IsNotNull();
        await Assert.That(matches).HasSingleItem();
        await Assert.That(route.Path).IsEqualTo("/world/logged-characters");

        return Task.CompletedTask;
    }

    [Test]
    [Arguments("/world/logged-characters")]
    [Arguments("/world/LOGGED-CHARACTERS")]
    [Arguments("/WORLD/LOGGED-CHARACTERS")]
    [Arguments("/WORLD/LOGGED-charactERS")]
    [Arguments("/WOrLD/LOggED-charactERS")]
    public Task GetRoute_WhenSimpleRoute_CaseInsensitiveShouldFindAndMatch(string path)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute(path, HttpMethod.Get);

        // Assert
        await Assert.That(route).IsNotNull();
        await Assert.That(matches).IsNotNull();
        await Assert.That(matches).HasSingleItem();
        Assert.Equal("/world/logged-characters", route.Path, true);

        return Task.CompletedTask;
    }

    [Test]
    public Task GetRoute_WhenRegexRoutes_ShouldFindAndMatch()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        // Act
        var (route, matches) = routeMapper.GetRoute("/world/logged-characters", HttpMethod.Get);

        // Assert
        await Assert.That(route).IsNotNull();
        await Assert.That(matches).IsNotNull();
        await Assert.That(matches).HasSingleItem();
        await Assert.That(matches[0].Groups[1].Value).IsEqualTo("logged-characters");
        await Assert.That(matches[0].Groups[0].Value).IsEqualTo("/world/logged-characters");

        return Task.CompletedTask;
    }

    [Test]
    public Task GetRoute_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute("not-found", HttpMethod.Get);

        // Assert
        await Assert.That(route).IsNull();
        await Assert.That(matches).IsNull();

        return Task.CompletedTask;
    }
    internal sealed class MyController : BaseController
    {
        [WebApiGet("/world/logged-characters")]
        public HttpResponse GetCharacter(HttpRequest request)
        {
            return OkJson(new { id = 1, name = "test" });
        }
    }

    internal sealed class MyRegexController : BaseController
    {
        [WebApiGet("/world/(.+)")]
        public HttpResponse GetCharacter(HttpRequest request)
        {
            return OkJson(new { id = 1, name = "test" });
        }
    }
}



#endif
