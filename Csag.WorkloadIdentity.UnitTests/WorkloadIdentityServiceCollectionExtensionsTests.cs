namespace Csag.WorkloadIdentity.UnitTests
{
    using Azure.Core;
    using Csag.WorkloadIdentity.Abstractions;
    using Csag.WorkloadIdentity.Internal;
    using Csag.WorkloadIdentity.Internal.Services;
    using Csag.WorkloadIdentity.Options;
    using Google.Cloud.Iam.Credentials.V1;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NSubstitute;
    using Shouldly;

    /// <summary>
    /// Unit tests for the <see cref="WorkloadIdentityServiceCollectionExtensions"/> class.
    /// </summary>
    public class WorkloadIdentityServiceCollectionExtensionsTests
    {
        /// <summary>
        /// The Microsoft Entra tenant ID in the test configuration.
        /// </summary>
        private const string TenantId = "tenant";

        /// <summary>
        /// The client ID of the app registration in the test configuration.
        /// </summary>
        private const string ClientId = "client";

        /// <summary>
        /// The Google service account email in the test configuration.
        /// </summary>
        private const string ServiceAccountEmail = "sa@example.iam.gserviceaccount.com";

        /// <summary>
        /// The client ID of the user-assigned managed identity in the test configuration.
        /// </summary>
        private const string UserAssignedClientId = "11111111-2222-3333-4444-555555555555";

        /// <summary>
        /// The refresh-ahead window in the test configuration.
        /// </summary>
        private static readonly TimeSpan RefreshAheadWindow = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Verifies that the parameterless overload binds the default section of the registered configuration and
        /// wires up both token providers, both exchangers and the refresh service.
        /// </summary>
        [Fact]
        public void Given_ConfigurationInContainer_When_AddWorkloadIdentity_Then_BindsOptionsAndResolvesPipeline()
        {
            // Arrange
            var services = CreateServices(CreateValidConfiguration());

            // Act
            services.AddWorkloadIdentity();
            using var serviceProvider = services.BuildServiceProvider();

            // Assert
            var options = serviceProvider.GetRequiredService<IOptions<WorkloadIdentityOptions>>().Value;
            var azureSql = options.AzureSql.ShouldNotBeNull();
            azureSql.Provider.ShouldBe(WorkloadIdentityProvider.Google);
            var google = azureSql.Google.ShouldNotBeNull();
            google.TenantId.ShouldBe(TenantId);
            google.ClientId.ShouldBe(ClientId);
            google.ServiceAccountEmail.ShouldBe(ServiceAccountEmail);
            var blobStorage = options.BlobStorage.ShouldNotBeNull();
            blobStorage.Provider.ShouldBe(WorkloadIdentityProvider.ManagedIdentity);
            blobStorage.ManagedIdentity.ShouldNotBeNull().ClientId.ShouldBe(UserAssignedClientId);
            options.RefreshAheadWindow.ShouldBe(RefreshAheadWindow);
            options.EnableBackgroundRefresh.ShouldBeFalse();
            serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().ShouldBeOfType<AzureSqlTokenProvider>();
            serviceProvider.GetRequiredService<IBlobStorageTokenProvider>().ShouldBeOfType<BlobStorageTokenProvider>();
            serviceProvider.GetServices<IWorkloadIdentityTokenExchanger>().Select(exchanger => exchanger.GetType())
                .ShouldBe([typeof(GoogleFederatedTokenExchanger), typeof(ManagedIdentityTokenExchanger)], ignoreOrder: true);
            serviceProvider.GetServices<IHostedService>().ShouldHaveSingleItem().ShouldBeOfType<TokenRefreshService>();
        }

        /// <summary>
        /// Verifies that a host without any configuration starts, and that resolving a token provider then fails with
        /// a message naming its section.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_MissingConfiguration_When_HostStarts_Then_StartsAndProvidersNameTheirSection()
        {
            // Arrange
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddWorkloadIdentity();
            using var host = builder.Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert
            var exception = Should.Throw<InvalidOperationException>(() => host.Services.GetRequiredService<IAzureSqlTokenProvider>());
            exception.Message.ShouldContain("Csag.WorkloadIdentity:AzureSql");
        }

        /// <summary>
        /// Verifies that a host whose section holds only an Entra entry, without a provider value, starts.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_SectionWithOnlyEntraWithoutProvider_When_HostStarts_Then_StartsWithGoogleProvider()
        {
            // Arrange
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(CreateEntraConfiguration(ServiceAccountEmail));
            builder.Services.AddWorkloadIdentity();
            using var host = builder.Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert
            var entra = host.Services.GetRequiredService<IOptions<WorkloadIdentityOptions>>().Value.Entra.ShouldNotBeNull();
            entra.Provider.ShouldBe(WorkloadIdentityProvider.Google);
        }

        /// <summary>
        /// Verifies that the background refresh renews the token of a configured resource and asks Google for no ID
        /// token for the Entra entry. The substituted credential does not call its assertion callback, so every call
        /// to Google comes from the Entra entry.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_ResourceAndEntra_When_BackgroundRefreshRuns_Then_MakesNoGoogleCallForEntra()
        {
            // Arrange
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var credential = Substitute.For<TokenCredential>();
            credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    refreshed.TrySetResult();
                    return new AccessToken("federated-token", DateTimeOffset.UtcNow.AddHours(1));
                });
            var credentialFactory = Substitute.For<IClientAssertionCredentialFactory>();
            credentialFactory.Create(TenantId, ClientId, Arg.Any<Func<CancellationToken, Task<string>>>()).Returns(credential);
            var googleIdTokenProvider = Substitute.For<IGoogleIdTokenProvider>();
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddSingleton(credentialFactory);
            builder.Services.AddSingleton(googleIdTokenProvider);
            builder.Services.AddWorkloadIdentity(options =>
            {
                options.AzureSql = CreateGoogleResource();
                options.Entra = new EntraClientAssertionOptions
                {
                    Google = new GoogleServiceAccountOptions { ServiceAccountEmail = ServiceAccountEmail },
                };
            });
            using var host = builder.Build();

            // Act
            await host.StartAsync();
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await host.StopAsync();

            // Assert
            await googleIdTokenProvider.DidNotReceiveWithAnyArgs().GetIdTokenAsync(default!, default);
        }

        /// <summary>
        /// Verifies that an Entra entry without a service account email fails host startup and names the key.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_EntraWithoutServiceAccountEmail_When_HostStarts_Then_ThrowsOptionsValidationExceptionNamingKey()
        {
            // Arrange
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(CreateEntraConfiguration(serviceAccountEmail: null));
            builder.Services.AddWorkloadIdentity();
            using var host = builder.Build();

            // Act
            var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

            // Assert
            exception.Message.ShouldContain("Entra:Google:ServiceAccountEmail must be provided.");
        }

        /// <summary>
        /// Verifies that the client assertion provider asks Google for an ID token for the service account of the
        /// Entra entry, with the audience Microsoft Entra ID expects, and returns that token.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_EntraEntry_When_GetClientAssertionAsync_Then_ReturnsGoogleIdTokenForServiceAccountWithEntraAudience()
        {
            // Arrange
            var client = Substitute.For<IAMCredentialsClient>();
            client.GenerateIdTokenAsync(Arg.Any<GenerateIdTokenRequest>(), Arg.Any<CancellationToken>())
                .Returns(new GenerateIdTokenResponse { Token = "google-id-token" });
            var clientFactory = Substitute.For<IIamCredentialsClientFactory>();
            clientFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);
            var services = CreateServices(new ConfigurationBuilder().AddInMemoryCollection(CreateEntraConfiguration(ServiceAccountEmail)).Build());
            services.AddSingleton(clientFactory);
            services.AddWorkloadIdentity();
            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var assertion = await serviceProvider.GetRequiredService<IEntraClientAssertionProvider>().GetClientAssertionAsync(CancellationToken.None);

            // Assert
            assertion.ShouldBe("google-id-token");
            await client.Received(1).GenerateIdTokenAsync(
                Arg.Is<GenerateIdTokenRequest>(request =>
                    request.Name == $"projects/-/serviceAccounts/{ServiceAccountEmail}"
                    && request.Audience == "api://AzureADTokenExchange"),
                Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Verifies that resolving the client assertion provider without an Entra entry fails with a message naming
        /// the entry, while the configured resource's provider still resolves.
        /// </summary>
        [Fact]
        public void Given_NoEntraEntry_When_ResolvingClientAssertionProvider_Then_ThrowsInvalidOperationExceptionNamingEntry()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkloadIdentity(options => options.AzureSql = CreateGoogleResource());
            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var exception = Should.Throw<InvalidOperationException>(() => serviceProvider.GetRequiredService<IEntraClientAssertionProvider>());

            // Assert
            exception.Message.ShouldContain("Csag.WorkloadIdentity:Entra");
            serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().ShouldBeOfType<AzureSqlTokenProvider>();
        }

        /// <summary>
        /// Verifies that an incomplete resource section fails host startup with a validation error naming the path.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_IncompleteResourceSection_When_HostStarts_Then_ThrowsOptionsValidationExceptionNamingPath()
        {
            // Arrange
            var section = WorkloadIdentityOptions.ConfigurationSectionName;
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{section}:AzureSql:Provider"] = nameof(WorkloadIdentityProvider.Google),
                [$"{section}:AzureSql:Google:TenantId"] = TenantId,
            });
            builder.Services.AddWorkloadIdentity();
            using var host = builder.Build();

            // Act
            var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

            // Assert
            exception.Message.ShouldContain("AzureSql:Google:ClientId must be provided.");
            exception.Message.ShouldContain("AzureSql:Google:ServiceAccountEmail must be provided.");
        }

        /// <summary>
        /// Verifies that a token provider registered by the consumer beforehand is kept.
        /// </summary>
        [Fact]
        public void Given_ConsumerRegisteredTokenProvider_When_AddWorkloadIdentity_Then_ConsumerRegistrationWins()
        {
            // Arrange
            var consumerProvider = Substitute.For<IAzureSqlTokenProvider>();
            var services = CreateServices(CreateValidConfiguration());
            services.AddSingleton(consumerProvider);

            // Act
            services.AddWorkloadIdentity();
            using var serviceProvider = services.BuildServiceProvider();

            // Assert
            serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().ShouldBeSameAs(consumerProvider);
            serviceProvider.GetRequiredService<IBlobStorageTokenProvider>().ShouldBeOfType<BlobStorageTokenProvider>();
        }

        /// <summary>
        /// Verifies that registering twice does not duplicate the services or the hosted service.
        /// </summary>
        [Fact]
        public void Given_CalledTwice_When_AddWorkloadIdentity_Then_RegistersEachServiceOnce()
        {
            // Arrange
            var services = CreateServices(CreateValidConfiguration());

            // Act
            services.AddWorkloadIdentity();
            services.AddWorkloadIdentity();

            // Assert
            services.Count(descriptor => descriptor.ServiceType == typeof(IAzureSqlTokenProvider)).ShouldBe(1);
            services.Count(descriptor => descriptor.ServiceType == typeof(IBlobStorageTokenProvider)).ShouldBe(1);
            services.Count(descriptor => descriptor.ServiceType == typeof(IEntraClientAssertionProvider)).ShouldBe(1);
            services.Count(descriptor => descriptor.ServiceType == typeof(IWorkloadIdentityTokenExchanger)).ShouldBe(2);
            services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)).ShouldBe(1);
            services.Count(descriptor => descriptor.ServiceType == typeof(IValidateOptions<WorkloadIdentityOptions>)).ShouldBe(1);
        }

        /// <summary>
        /// Verifies that the configure-action overload applies the options without any configuration in the container.
        /// </summary>
        [Fact]
        public void Given_ConfigureAction_When_AddWorkloadIdentity_Then_AppliesOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddWorkloadIdentity(options => options.AzureSql = CreateGoogleResource());
            using var serviceProvider = services.BuildServiceProvider();

            // Assert
            var options = serviceProvider.GetRequiredService<IOptions<WorkloadIdentityOptions>>().Value;
            options.AzureSql.ShouldNotBeNull().Google.ShouldNotBeNull().TenantId.ShouldBe(TenantId);
            options.BlobStorage.ShouldBeNull();
            options.EnableBackgroundRefresh.ShouldBeTrue();
            serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().ShouldBeOfType<AzureSqlTokenProvider>();
        }

        /// <summary>
        /// Verifies that the configuration overload binds from the given instance without any configuration in the container.
        /// </summary>
        [Fact]
        public void Given_ConfigurationInstance_When_AddWorkloadIdentity_Then_BindsOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddWorkloadIdentity(CreateValidConfiguration());
            using var serviceProvider = services.BuildServiceProvider();

            // Assert
            var options = serviceProvider.GetRequiredService<IOptions<WorkloadIdentityOptions>>().Value;
            options.AzureSql.ShouldNotBeNull().Google.ShouldNotBeNull().ClientId.ShouldBe(ClientId);
            options.RefreshAheadWindow.ShouldBe(RefreshAheadWindow);
            serviceProvider.GetRequiredService<IBlobStorageTokenProvider>().ShouldBeOfType<BlobStorageTokenProvider>();
        }

        /// <summary>
        /// Verifies that resolving the provider of a resource without a section fails with a message naming the
        /// section, while the configured resource's provider still resolves.
        /// </summary>
        [Fact]
        public void Given_UnconfiguredResource_When_ResolvingItsTokenProvider_Then_ThrowsInvalidOperationExceptionNamingSection()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkloadIdentity(options => options.AzureSql = CreateGoogleResource());
            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var exception = Should.Throw<InvalidOperationException>(() => serviceProvider.GetRequiredService<IBlobStorageTokenProvider>());

            // Assert
            exception.Message.ShouldContain("Csag.WorkloadIdentity:BlobStorage");
            serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().ShouldBeOfType<AzureSqlTokenProvider>();
        }

        /// <summary>
        /// Verifies that the overloads reject a missing configuration or configure action.
        /// </summary>
        [Fact]
        public void Given_NullArguments_When_AddWorkloadIdentity_Then_ThrowsArgumentNullException()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var configurationException = Should.Throw<ArgumentNullException>(() => services.AddWorkloadIdentity((IConfiguration)null!));
            var actionException = Should.Throw<ArgumentNullException>(() => services.AddWorkloadIdentity((Action<WorkloadIdentityOptions>)null!));

            // Assert
            configurationException.ParamName.ShouldBe("configuration");
            actionException.ParamName.ShouldBe("configureOptions");
        }

        /// <summary>
        /// Verifies that a resource selecting the managed identity provider is served through the managed identity
        /// credential seam with its configured identity and scope.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_ManagedIdentityResourceAndSubstitutedCredentialFactory_When_GetBlobStorageAccessTokenAsync_Then_ReturnsCredentialsToken()
        {
            // Arrange
            var credential = Substitute.For<TokenCredential>();
            credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
                .Returns(new AccessToken("managed-identity-token", DateTimeOffset.UtcNow.AddHours(1)));
            var credentialFactory = Substitute.For<IManagedIdentityCredentialFactory>();
            credentialFactory.Create(Arg.Any<ManagedIdentityOptions>()).Returns(credential);
            var services = CreateServices(CreateValidConfiguration());
            services.AddSingleton(credentialFactory);
            services.AddWorkloadIdentity();
            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var token = await serviceProvider.GetRequiredService<IBlobStorageTokenProvider>().GetBlobStorageAccessTokenAsync(CancellationToken.None);

            // Assert
            token.ShouldBe("managed-identity-token");
            credentialFactory.Received(1).Create(Arg.Is<ManagedIdentityOptions>(identity => identity.ClientId == UserAssignedClientId));
            await credential.Received(1).GetTokenAsync(Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "https://storage.azure.com/.default"), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Verifies that a resource selecting the Google provider is served through the client assertion credential
        /// seam with its configured identity and scope.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_GoogleResourceAndSubstitutedCredentialFactory_When_GetAzureSqlAccessTokenAsync_Then_ReturnsCredentialsToken()
        {
            // Arrange
            var credential = Substitute.For<TokenCredential>();
            credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
                .Returns(new AccessToken("federated-token", DateTimeOffset.UtcNow.AddHours(1)));
            var credentialFactory = Substitute.For<IClientAssertionCredentialFactory>();
            credentialFactory.Create(TenantId, ClientId, Arg.Any<Func<CancellationToken, Task<string>>>()).Returns(credential);
            var services = CreateServices(CreateValidConfiguration());
            services.AddSingleton(credentialFactory);
            services.AddWorkloadIdentity();
            using var serviceProvider = services.BuildServiceProvider();

            // Act
            var token = await serviceProvider.GetRequiredService<IAzureSqlTokenProvider>().GetAzureSqlAccessTokenAsync(CancellationToken.None);

            // Assert
            token.ShouldBe("federated-token");
            await credential.Received(1).GetTokenAsync(Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "https://database.windows.net/.default"), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Creates an in-memory configuration holding a complete default section: Azure SQL over Google federation
        /// and Blob Storage over a user-assigned managed identity.
        /// </summary>
        /// <returns>The configuration.</returns>
        private static IConfiguration CreateValidConfiguration()
        {
            var section = WorkloadIdentityOptions.ConfigurationSectionName;
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{section}:AzureSql:Provider"] = nameof(WorkloadIdentityProvider.Google),
                    [$"{section}:AzureSql:Google:TenantId"] = TenantId,
                    [$"{section}:AzureSql:Google:ClientId"] = ClientId,
                    [$"{section}:AzureSql:Google:ServiceAccountEmail"] = ServiceAccountEmail,
                    [$"{section}:BlobStorage:Provider"] = nameof(WorkloadIdentityProvider.ManagedIdentity),
                    [$"{section}:BlobStorage:ManagedIdentity:ClientId"] = UserAssignedClientId,
                    [$"{section}:RefreshAheadWindow"] = RefreshAheadWindow.ToString(),
                    [$"{section}:EnableBackgroundRefresh"] = "false",
                })
                .Build();
        }

        /// <summary>
        /// Creates the configuration values of a default section that holds only an Entra entry. The entry has no
        /// provider value, because Google is the default.
        /// </summary>
        /// <param name="serviceAccountEmail">The service account email of the entry, or <see langword="null"/> to leave it out.</param>
        /// <returns>The configuration values.</returns>
        private static Dictionary<string, string?> CreateEntraConfiguration(string? serviceAccountEmail)
        {
            var section = WorkloadIdentityOptions.ConfigurationSectionName;
            return new Dictionary<string, string?>
            {
                [$"{section}:Entra:Google:ServiceAccountEmail"] = serviceAccountEmail,
            };
        }

        /// <summary>
        /// Creates a resource section that federates through Google with every value set.
        /// </summary>
        /// <returns>The section.</returns>
        private static WorkloadIdentityResourceOptions CreateGoogleResource()
        {
            return new WorkloadIdentityResourceOptions
            {
                Provider = WorkloadIdentityProvider.Google,
                Google = new GoogleOptions
                {
                    TenantId = TenantId,
                    ClientId = ClientId,
                    ServiceAccountEmail = ServiceAccountEmail,
                },
            };
        }

        /// <summary>
        /// Creates a service collection with the given configuration and logging registered, as a host would.
        /// </summary>
        /// <param name="configuration">The configuration to register.</param>
        /// <returns>The service collection.</returns>
        private static ServiceCollection CreateServices(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddLogging();
            return services;
        }
    }
}
