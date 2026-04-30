using MediatR;

namespace AICopilot.SharedKernel.Messaging;

public interface IHumanRequest<out TResponse> : IRequest<TResponse>;

public interface IInternalRequest<out TResponse> : IRequest<TResponse>;

public interface IAnonymousRequest<out TResponse> : IRequest<TResponse>;

public interface IHumanCommand<out TResponse> : ICommand<TResponse>, IHumanRequest<TResponse>;

public interface IHumanQuery<out TResponse> : IQuery<TResponse>, IHumanRequest<TResponse>;

public interface IInternalCommand<out TResponse> : ICommand<TResponse>, IInternalRequest<TResponse>;

public interface IInternalQuery<out TResponse> : IQuery<TResponse>, IInternalRequest<TResponse>;

public interface IAnonymousCommand<out TResponse> : ICommand<TResponse>, IAnonymousRequest<TResponse>;

public interface IAnonymousQuery<out TResponse> : IQuery<TResponse>, IAnonymousRequest<TResponse>;
