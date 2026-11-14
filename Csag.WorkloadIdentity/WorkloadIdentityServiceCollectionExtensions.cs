namespace Csag.WorkloadIdentity
{
    using Csag.WorkloadIdentity.Abstractions;
    using Csag.WorkloadIdentity.Internal;
    using Csag.WorkloadIdentity.Internal.Services;
    using Csag.WorkloadIdentity.Options;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Provides extension methods for registering the workload identity token providers.
    /// </summary>
    public static class WorkloadIdentityServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the workload identity token providers, binding the options from the
        /// <see cref="WorkloadIdentityOptions.ConfigurationSectionName"/> section of the <see cref="IConfiguration"/>
        /// registered in the container. The options are validated when the host starts.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddWorkloadIdentity(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<WorkloadIdentityOptions>()
                .BindConfiguration(WorkloadIdentityOptions.ConfigurationSectionName)
                .ValidateOnStart();
            RegisterWorkloadIdentityServices(services);
            return services;
        }

        /// <summary>
        /// Adds the workload identity token providers with the options configured in code. The options are validated
        /// when the host starts.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureOptions">The action to configure options.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddWorkloadIdentity(this IServiceCollection services, Action<WorkloadIdentityOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.AddOptions<WorkloadIdentityOptions>()
                .Configure(configureOptions)
                .ValidateOnStart();
            RegisterWorkloadIdentityServices(services);
            return services;
        }

        /// <summary>
        /// Adds the workload identity token providers, binding the options from the
        /// <see cref="WorkloadIdentityOptions.ConfigurationSectionName"/> section of the given configuration. Use this
        /// when the configuration is not registered as <see cref="IConfiguration"/> in the container. The options are
        /// validated when the host starts.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddWorkloadIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddOptions<WorkloadIdentityOptions>()
                .Bind(configuration.GetSection(WorkloadIdentityOptions.ConfigurationSectionName))
                .ValidateOnStart();
            RegisterWorkloadIdentityServices(services);
            return services;
        }

        /// <summary>
        /// Registers the token pipeline. Each service is a Try* registration, so a consumer can substitute it by
        /// registering its own implementation first. The hosted service is the exception: it is added with
        /// TryAddEnumerable, which prevents duplicates but not substitution, and is turned off through
        /// <see cref="WorkloadIdentityOptions.EnableBackgroundRefresh"/>. Repeated calls do not duplicate anything. Both
        /// resource providers and the Entra client assertion provider are always registered; resolving one whose
        /// section is not configured throws an <see cref="InvalidOperationException"/> that names the missing section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        private static void RegisterWorkloadIdentityServices(IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<WorkloadIdentityOptions>, WorkloadIdentityOptionsValidator>());
            services.TryAddSingleton<IIamCredentialsClientFactory, IamCredentialsClientFactory>();
            services.TryAddSingleton<IClientAssertionCredentialFactory, ClientAssertionCredentialFactory>();
            services.TryAddSingleton<IManagedIdentityCredentialFactory, ManagedIdentityCredentialFactory>();
            services.TryAddSingleton<IGoogleIdTokenProvider, GoogleIdTokenProvider>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkloadIdentityTokenExchanger, GoogleFederatedTokenExchanger>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkloadIdentityTokenExchanger, ManagedIdentityTokenExchanger>());
            services.TryAddSingleton<ResourceTokenProviderFactory>();
            services.TryAddSingleton<IAzureSqlTokenProvider>(serviceProvider => new AzureSqlTokenProvider(serviceProvider.GetRequiredService<ResourceTokenProviderFactory>().Create(TokenScope.AzureSql)));
            services.TryAddSingleton<IBlobStorageTokenProvider>(serviceProvider => new BlobStorageTokenProvider(serviceProvider.GetRequiredService<ResourceTokenProviderFactory>().Create(TokenScope.BlobStorage)));
            services.TryAddSingleton<IEntraClientAssertionProvider, EntraClientAssertionProvider>();
            services.AddHostedService<TokenRefreshService>();
        }
    }
}
