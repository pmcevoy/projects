using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProbHammer.Core.Catalogue;

namespace ProbHammer.Web.Services;

public sealed class CatalogueStartupService : IHostedService
{
    private readonly CatalogueStore _store;
    private readonly ILogger<CatalogueStartupService> _logger;

    public CatalogueStartupService(CatalogueStore store, ILogger<CatalogueStartupService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _store.InitialiseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise catalogue store on startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
