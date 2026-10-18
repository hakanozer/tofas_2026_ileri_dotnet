using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using EvolutionaryLogistics.BuildingBlocks.Grpc.Authentication;
using EvolutionaryLogistics.BuildingBlocks.Grpc.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace EvolutionaryLogistics.BuildingBlocks.Grpc.DependencyInjection;

public static class GrpcPipelineExtensions
{
    /// <summary>
    /// Registers global server interceptor + JWT metadata parser for ASP.NET Core gRPC hosts.
    /// </summary>
    public static IServiceCollection AddEvolutionaryGrpcServerPipeline(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<JwtMetadataParser>();
        services.AddSingleton<GlobalGrpcServerInterceptor>();
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<GlobalGrpcServerInterceptor>();
            options.EnableDetailedErrors = false;
        });
        return services;
    }

    /// <summary>
    /// Configures GrpcChannel with client interceptor for deadline + OTEL + claim propagation.
    /// </summary>
    public static IServiceCollection AddEvolutionaryGrpcClientPipeline(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<JwtMetadataParser>();
        services.AddSingleton<GlobalGrpcClientInterceptor>();
        return services;
    }

    public static CallInvoker CreateEvolutionaryCallInvoker(
        this IServiceProvider serviceProvider,
        Uri address,
        Action<GrpcChannelOptions>? configure = null)
    {
        var interceptor = serviceProvider.GetRequiredService<GlobalGrpcClientInterceptor>();
        var options = new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            },
            ServiceConfig = new global::Grpc.Net.Client.Configuration.ServiceConfig
            {
                MethodConfigs =
                {
                    new global::Grpc.Net.Client.Configuration.MethodConfig
                    {
                        Names = { global::Grpc.Net.Client.Configuration.MethodName.Default },
                        RetryPolicy = new global::Grpc.Net.Client.Configuration.RetryPolicy
                        {
                            MaxAttempts = 3,
                            InitialBackoff = TimeSpan.FromMilliseconds(100),
                            MaxBackoff = TimeSpan.FromSeconds(2),
                            BackoffMultiplier = 1.5,
                            RetryableStatusCodes =
                            {
                                global::Grpc.Core.StatusCode.Unavailable,
                                global::Grpc.Core.StatusCode.DeadlineExceeded
                            }
                        }
                    }
                }
            }
        };

        configure?.Invoke(options);
        var channel = GrpcChannel.ForAddress(address, options);
        return channel.Intercept(interceptor);
    }
}
