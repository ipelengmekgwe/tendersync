using MediatR;

namespace TenderSync.Application.Common.Messaging;

/// <summary>A request that changes state. Runs inside the unit-of-work and audit behaviours.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A request that only reads. No unit of work, no audit row.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
