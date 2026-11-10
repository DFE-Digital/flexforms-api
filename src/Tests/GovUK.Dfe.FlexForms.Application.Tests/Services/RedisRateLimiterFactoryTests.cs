using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using GovUK.Dfe.FlexForms.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class RedisRateLimiterFactoryTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();
    private bool _redisAvailable = true;

    public RedisRateLimiterFactoryTests()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
        _services.GetService(typeof(IConnectionMultiplexer)).Returns(_ => _redisAvailable
            ? redis
            : throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is down"));
    }

    [Theory]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    public void IsAllowed_UsesTheSharedRedisCount(long scriptResult, bool expected)
    {
        _database
            .ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Any<RedisValue[]?>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)scriptResult));

        var limiter = CreateFactory().Create(5, TimeSpan.FromMinutes(10));

        Assert.Equal(expected, limiter.IsAllowed("reveal:admin@example.com"));
    }

    [Fact]
    public void IsAllowed_SendsAHashedKeyAndTheLimit()
    {
        _database
            .ScriptEvaluate(Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Any<RedisValue[]?>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1L));

        CreateFactory().Create(5, TimeSpan.FromMinutes(10)).IsAllowed("reveal:admin@example.com");

        _database.Received(1).ScriptEvaluate(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]?>(keys => keys != null
                && keys.Single() == RedisRateLimiterFactory.BuildKey(5, TimeSpan.FromMinutes(10), "reveal:admin@example.com")
                && !keys.Single().ToString().Contains("admin@example.com")),
            Arg.Is<RedisValue[]?>(values => values != null && values[1] == 600_000 && values[2] == 5),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void IsAllowed_WhenRedisIsUnavailable_FallsBackToThisReplicasLimit()
    {
        _redisAvailable = false;
        var limiter = CreateFactory().Create(2, TimeSpan.FromMinutes(1));

        Assert.True(limiter.IsAllowed("key"));
        Assert.True(limiter.IsAllowed("key"));
        Assert.False(limiter.IsAllowed("key"));
    }

    [Fact]
    public void BuildKey_SeparatesDifferentLimitsForTheSameCaller()
    {
        Assert.NotEqual(
            RedisRateLimiterFactory.BuildKey(5, TimeSpan.FromMinutes(1), "caller"),
            RedisRateLimiterFactory.BuildKey(10, TimeSpan.FromMinutes(1), "caller"));
    }

    private RedisRateLimiterFactory CreateFactory() =>
        new(_services, new RateLimitStore<string>(), TimeProvider.System, NullLogger<RedisRateLimiterFactory>.Instance);
}
