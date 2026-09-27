using Microsoft.Extensions.Options;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Jobs;

/// <summary>一定間隔で処理を回す。失敗しても次の回に続ける。</summary>
public abstract class PeriodicWorker(IServiceScopeFactory scopes, ILogger log) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }
    protected abstract Task TickAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await TickAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "{Worker} の処理に失敗しました", GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public class ReviewWorker(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<ReviewWorker> log)
    : PeriodicWorker(scopes, log)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.ReviewIntervalSeconds);

    protected override async Task TickAsync(IServiceProvider services, CancellationToken ct)
    {
        var review = services.GetRequiredService<ReviewService>();
        await review.ReviewPendingMarksAsync(10, ct);
        await review.ReviewPendingPopsAsync(20, ct);
    }
}

public class MaintenanceWorker(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<MaintenanceWorker> log)
    : PeriodicWorker(scopes, log)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.MaintenanceIntervalSeconds);

    protected override Task TickAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<MaintenanceService>().RunAsync(ct);
}

public class RecheckWorker(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<RecheckWorker> log)
    : PeriodicWorker(scopes, log)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(options.Value.RecheckIntervalSeconds);

    protected override Task TickAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ReviewService>().RecheckLinksAsync(20, ct);
}
