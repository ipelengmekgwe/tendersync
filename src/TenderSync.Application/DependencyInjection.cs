using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Common.Behaviours;

namespace TenderSync.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers, the pipeline behaviours (outermost first: logging → authorization → validation → unit of work → audit)
    /// and FluentValidation validators from this assembly.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
            configuration.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            configuration.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(UnitOfWorkBehaviour<,>));
            configuration.AddOpenBehavior(typeof(AuditBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly, includeInternalTypes: true);
        return services;
    }
}
