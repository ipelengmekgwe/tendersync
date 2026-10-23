using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Common.Auditing;

namespace TenderSync.Application.Common.Behaviours;

/// <summary>Logs every request with its redacted payload, duration and outcome. Outermost behaviour.</summary>
public sealed class LoggingBehaviour<TRequest, TResponse>(ILogger<LoggingBehaviour<TRequest, TResponse>> logger, ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 2000)
            {
                logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms for {User}: {Request}", name, stopwatch.ElapsedMilliseconds, currentUser.DisplayName, RequestRedactor.ToJson(request));
            }
            else
            {
                logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms for {User}", name, stopwatch.ElapsedMilliseconds, currentUser.DisplayName);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Request {RequestName} failed after {ElapsedMs} ms for {User}: {Request}", name, stopwatch.ElapsedMilliseconds, currentUser.DisplayName, RequestRedactor.ToJson(request));
            throw;
        }
    }
}
