using System.Reflection;
using NetArchTest.Rules;

namespace TenderSync.Architecture.Tests;

/// <summary>
/// Enforces the Clean Architecture layer rules from PLAN.md §5.
/// Domain → nothing. Application → Domain. Infrastructure → Application + Domain. Api/Worker → Infrastructure + Application.
/// Provider types (EF Core, Npgsql, Hangfire, Anthropic, HttpClient) stay inside Infrastructure and the hosts.
/// </summary>
public class LayerDependencyTests
{
    private const string DomainNs = "TenderSync.Domain";
    private const string ApplicationNs = "TenderSync.Application";
    private const string InfrastructureNs = "TenderSync.Infrastructure";
    private const string ApiNs = "TenderSync.Api";
    private const string WorkerNs = "TenderSync.Worker";
    private const string ContractsNs = "TenderSync.Contracts";

    private static readonly string[] ProviderNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Dapper",
        "Hangfire",
        "Anthropic",
        "System.Net.Http",
        "Azure",
        "MailKit",
        "UglyToad.PdfPig",
        "DocumentFormat.OpenXml",
        "ClosedXML",
    ];

    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(Contracts.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_in_the_solution_or_on_frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                ApplicationNs, InfrastructureNs, ApiNs, WorkerNs, ContractsNs,
                "MediatR", "FluentValidation", "Mapster", "Microsoft.Extensions",
                .. ProviderNamespaces,
            ])
            .GetResult();

        AssertSuccess(result, "Domain");
    }

    [Fact]
    public void Application_depends_only_on_domain_and_abstractions()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny([InfrastructureNs, ApiNs, WorkerNs, ContractsNs, .. ProviderNamespaces])
            .GetResult();

        AssertSuccess(result, "Application");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny([ApiNs, WorkerNs, ContractsNs])
            .GetResult();

        AssertSuccess(result, "Infrastructure");
    }

    [Fact]
    public void Contracts_depends_on_nothing_in_the_solution()
    {
        var result = Types.InAssembly(ContractsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny([DomainNs, ApplicationNs, InfrastructureNs, ApiNs, WorkerNs, .. ProviderNamespaces])
            .GetResult();

        AssertSuccess(result, "Contracts");
    }

    [Fact]
    public void Project_references_match_the_layer_rules()
    {
        // Belt and braces: the csproj graph itself must not grant a forbidden reference.
        ReferencedNames(DomainAssembly).Should().NotContain(n => n.StartsWith("TenderSync.", StringComparison.Ordinal));
        ReferencedNames(ApplicationAssembly).Should().OnlyContain(n => !n.StartsWith("TenderSync.", StringComparison.Ordinal) || n == DomainNs);
        ReferencedNames(InfrastructureAssembly).Should().NotContain(n => n == ApiNs || n == WorkerNs || n == ContractsNs);
    }

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    private static void AssertSuccess(TestResult result, string layer)
    {
        var failing = result.FailingTypeNames is null ? string.Empty : string.Join(", ", result.FailingTypeNames);
        result.IsSuccessful.Should().BeTrue($"{layer} must not reach across the layer boundary. Offending types: {failing}");
    }
}
