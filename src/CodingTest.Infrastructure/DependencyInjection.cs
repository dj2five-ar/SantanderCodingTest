using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using CodingTest.Core.Interfaces;
using CodingTest.Infrastructure.Clients;
using CodingTest.Infrastructure.Configuration;
using CodingTest.Infrastructure.HostedServices;
using CodingTest.Infrastructure.Services;

namespace CodingTest.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HackerNewsOptions>(
            configuration.GetSection(HackerNewsOptions.SectionName));
        services.Configure<CacheOptions>(
            configuration.GetSection(CacheOptions.SectionName));
        services.Configure<ResilienceOptions>(
            configuration.GetSection(ResilienceOptions.SectionName));

        services.AddMemoryCache();

        // Resolve ResilienceOptions early so the Polly lambda captures a plain object
        // rather than needing IServiceProvider access inside the pipeline builder
        var resilienceConfig = configuration
            .GetSection(ResilienceOptions.SectionName)
            .Get<ResilienceOptions>() ?? new ResilienceOptions();

        services.AddHttpClient<IHackerNewsClient, HackerNewsClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<HackerNewsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodingTest/1.0");
            // client.Timeout is intentionally NOT set here - all timeouts are owned
            // by the Polly pipeline to avoid ObjectDisposedException conflicts with
            // Polly's own cancellation tokens
        })
        .AddStandardResilienceHandler(resilience =>
        {
            resilience.AttemptTimeout.Timeout =
                TimeSpan.FromSeconds(resilienceConfig.AttemptTimeoutSeconds);

            resilience.TotalRequestTimeout.Timeout =
                TimeSpan.FromSeconds(resilienceConfig.TotalTimeoutSeconds);

            resilience.Retry.MaxRetryAttempts = resilienceConfig.RetryMaxAttempts;
            resilience.Retry.BackoffType = DelayBackoffType.Exponential;
            resilience.Retry.UseJitter = true;
            resilience.Retry.Delay = TimeSpan.FromMilliseconds(resilienceConfig.RetryBaseDelayMs);

            resilience.CircuitBreaker.FailureRatio = resilienceConfig.CircuitBreakerFailureRatio;
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(resilienceConfig.CircuitBreakerSamplingWindowSeconds);
            resilience.CircuitBreaker.MinimumThroughput = resilienceConfig.CircuitBreakerMinimumThroughput;
            resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(resilienceConfig.CircuitBreakerBreakDurationSeconds);
        });

        // StoryService is singleton so its SemaphoreSlim fields are shared across requests
        services.AddSingleton<StoryService>();
        services.AddSingleton<IStoryService>(sp => sp.GetRequiredService<StoryService>());

        services.AddHostedService<CacheRefreshBackgroundService>();

        return services;
    }
}
