using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace TenderSync.Application;

public static class DependencyInjection
{
    /// <summary>Registers MediatR handlers and FluentValidation validators from this assembly. Pipeline behaviours are added in WP02.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly));
        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly, includeInternalTypes: true);
        return services;
    }
}
