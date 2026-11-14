namespace Csag.WorkloadIdentity.UnitTests
{
    using Csag.WorkloadIdentity.Options;
    using Shouldly;

    /// <summary>
    /// Unit tests for the <see cref="WorkloadIdentityOptionsValidator"/> class.
    /// </summary>
    public class WorkloadIdentityOptionsValidatorTests
    {
        /// <summary>
        /// The Microsoft Entra tenant ID used in complete Google sections.
        /// </summary>
        private const string TenantId = "tenant";

        /// <summary>
        /// The client ID used in complete Google and user-assigned managed identity sections.
        /// </summary>
        private const string ClientId = "client";

        /// <summary>
        /// The service account email used in complete Google sections.
        /// </summary>
        private const string ServiceAccountEmail = "sa@example.iam.gserviceaccount.com";

        /// <summary>
        /// The validator under test.
        /// </summary>
        private readonly WorkloadIdentityOptionsValidator validator = new();

        /// <summary>
        /// Verifies that Azure SQL over Google federation with every value set passes validation.
        /// </summary>
        [Fact]
        public void Given_AzureSqlWithCompleteGoogleSection_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions { AzureSql = CreateGoogleResource() };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that options without any section pass validation.
        /// </summary>
        [Fact]
        public void Given_NothingConfigured_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions();

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that an Entra entry alone passes validation.
        /// </summary>
        [Fact]
        public void Given_OnlyEntraWithGoogleServiceAccount_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions { Entra = CreateGoogleEntra() };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that an Entra entry with the Google provider and a blank service account email fails validation
        /// with a message naming the key.
        /// </summary>
        /// <param name="serviceAccountEmail">The blank service account email.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Given_EntraWithBlankServiceAccountEmail_When_Validate_Then_FailsNamingKey(string? serviceAccountEmail)
        {
            // Arrange
            var options = new WorkloadIdentityOptions { Entra = CreateGoogleEntra() };
            options.Entra.Google.ShouldNotBeNull().ServiceAccountEmail = serviceAccountEmail;

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Entra:Google:ServiceAccountEmail must be provided.");
        }

        /// <summary>
        /// Verifies that an Entra entry with the Google provider and no Google section fails validation.
        /// </summary>
        [Fact]
        public void Given_EntraWithGoogleProviderWithoutGoogleSection_When_Validate_Then_Fails()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions { Provider = WorkloadIdentityProvider.Google },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Entra:Google section must be provided");
        }

        /// <summary>
        /// Verifies that an Entra entry with a user-assigned managed identity passes validation.
        /// </summary>
        [Fact]
        public void Given_EntraWithUserAssignedManagedIdentity_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions
                {
                    Provider = WorkloadIdentityProvider.ManagedIdentity,
                    ManagedIdentity = new ManagedIdentityOptions { ClientId = ClientId },
                },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that an Entra entry with the managed identity provider and no managed identity section fails
        /// validation.
        /// </summary>
        [Fact]
        public void Given_EntraWithManagedIdentityProviderWithoutSection_When_Validate_Then_Fails()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions { Provider = WorkloadIdentityProvider.ManagedIdentity },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Entra:ManagedIdentity section must be provided");
        }

        /// <summary>
        /// Verifies that an Entra entry with a managed identity and a blank client ID fails validation naming the key.
        /// </summary>
        /// <param name="clientId">The blank client ID.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Given_EntraWithManagedIdentityWithoutClientId_When_Validate_Then_FailsNamingKey(string? clientId)
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions
                {
                    Provider = WorkloadIdentityProvider.ManagedIdentity,
                    ManagedIdentity = new ManagedIdentityOptions { ClientId = clientId },
                },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Entra:ManagedIdentity:ClientId must be provided unless Entra:ManagedIdentity:UseSystemAssignedIdentity is true.");
        }

        /// <summary>
        /// Verifies that an Entra entry with the system-assigned managed identity passes validation without a client ID.
        /// </summary>
        [Fact]
        public void Given_EntraWithSystemAssignedManagedIdentity_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions
                {
                    Provider = WorkloadIdentityProvider.ManagedIdentity,
                    ManagedIdentity = new ManagedIdentityOptions { UseSystemAssignedIdentity = true },
                },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that an Entra entry with a provider value outside the enum fails validation naming the key.
        /// </summary>
        [Fact]
        public void Given_EntraWithUndefinedProvider_When_Validate_Then_FailsNamingKey()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                Entra = new EntraClientAssertionOptions { Provider = (WorkloadIdentityProvider)42 },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Entra:Provider must be ManagedIdentity or Google.");
        }

        /// <summary>
        /// Verifies that Blob Storage over the system-assigned managed identity passes validation without a client ID.
        /// </summary>
        [Fact]
        public void Given_BlobStorageWithSystemAssignedManagedIdentity_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions { BlobStorage = CreateSystemAssignedResource() };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that a user-assigned managed identity with a client ID passes validation.
        /// </summary>
        [Fact]
        public void Given_AzureSqlWithUserAssignedManagedIdentity_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = new WorkloadIdentityResourceOptions
                {
                    Provider = WorkloadIdentityProvider.ManagedIdentity,
                    ManagedIdentity = new ManagedIdentityOptions { ClientId = ClientId },
                },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that both resources configured with different providers pass validation.
        /// </summary>
        [Fact]
        public void Given_BothResourcesConfigured_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = CreateGoogleResource(),
                BlobStorage = CreateSystemAssignedResource(),
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that a blank value in the Google section fails validation with a message naming its path.
        /// </summary>
        /// <param name="key">The key of the blank value.</param>
        /// <param name="value">The blank value.</param>
        [Theory]
        [InlineData(nameof(GoogleOptions.TenantId), null)]
        [InlineData(nameof(GoogleOptions.TenantId), "")]
        [InlineData(nameof(GoogleOptions.TenantId), "   ")]
        [InlineData(nameof(GoogleOptions.ClientId), null)]
        [InlineData(nameof(GoogleOptions.ClientId), "")]
        [InlineData(nameof(GoogleOptions.ClientId), "   ")]
        [InlineData(nameof(GoogleOptions.ServiceAccountEmail), null)]
        [InlineData(nameof(GoogleOptions.ServiceAccountEmail), "")]
        [InlineData(nameof(GoogleOptions.ServiceAccountEmail), "   ")]
        public void Given_BlankGoogleValue_When_Validate_Then_FailsNamingPath(string key, string? value)
        {
            // Arrange
            var options = new WorkloadIdentityOptions { AzureSql = CreateGoogleResource() };
            var google = options.AzureSql.Google.ShouldNotBeNull();
            switch (key)
            {
                case nameof(GoogleOptions.TenantId):
                    google.TenantId = value;
                    break;
                case nameof(GoogleOptions.ClientId):
                    google.ClientId = value;
                    break;
                default:
                    google.ServiceAccountEmail = value;
                    break;
            }

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain($"AzureSql:Google:{key} must be provided.");
        }

        /// <summary>
        /// Verifies that selecting the Google provider without a Google section fails validation.
        /// </summary>
        [Fact]
        public void Given_GoogleProviderWithoutGoogleSection_When_Validate_Then_Fails()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = new WorkloadIdentityResourceOptions { Provider = WorkloadIdentityProvider.Google },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("AzureSql:Google section must be provided");
        }

        /// <summary>
        /// Verifies that selecting the managed identity provider without a managed identity section fails validation.
        /// </summary>
        [Fact]
        public void Given_ManagedIdentityProviderWithoutSection_When_Validate_Then_Fails()
        {
            // Arrange
            var options = new WorkloadIdentityOptions { BlobStorage = new WorkloadIdentityResourceOptions() };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("BlobStorage:ManagedIdentity section must be provided");
        }

        /// <summary>
        /// Verifies that a user-assigned managed identity without a client ID fails validation.
        /// </summary>
        /// <param name="clientId">The blank client ID.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Given_UserAssignedManagedIdentityWithoutClientId_When_Validate_Then_Fails(string? clientId)
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                BlobStorage = new WorkloadIdentityResourceOptions
                {
                    ManagedIdentity = new ManagedIdentityOptions { ClientId = clientId },
                },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("BlobStorage:ManagedIdentity:ClientId must be provided");
        }

        /// <summary>
        /// Verifies that a provider value outside the enum fails validation.
        /// </summary>
        [Fact]
        public void Given_UndefinedProvider_When_Validate_Then_Fails()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = new WorkloadIdentityResourceOptions { Provider = (WorkloadIdentityProvider)42 },
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain("AzureSql:Provider");
        }

        /// <summary>
        /// Verifies that a refresh-ahead window that is not positive fails validation.
        /// </summary>
        /// <param name="minutes">The window length in minutes.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Given_NonPositiveRefreshAheadWindow_When_Validate_Then_Fails(int minutes)
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = CreateGoogleResource(),
                RefreshAheadWindow = TimeSpan.FromMinutes(minutes),
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain(nameof(WorkloadIdentityOptions.RefreshAheadWindow));
        }

        /// <summary>
        /// Verifies that a refresh-ahead window longer than five minutes fails validation.
        /// </summary>
        /// <param name="seconds">The window length in seconds.</param>
        [Theory]
        [InlineData(301)]
        [InlineData(600)]
        public void Given_RefreshAheadWindowAboveFiveMinutes_When_Validate_Then_Fails(int seconds)
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = CreateGoogleResource(),
                RefreshAheadWindow = TimeSpan.FromSeconds(seconds),
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            result.FailureMessage.ShouldNotBeNull().ShouldContain(nameof(WorkloadIdentityOptions.RefreshAheadWindow));
        }

        /// <summary>
        /// Verifies that a refresh-ahead window of exactly five minutes passes validation.
        /// </summary>
        [Fact]
        public void Given_RefreshAheadWindowOfFiveMinutes_When_Validate_Then_Succeeds()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = CreateGoogleResource(),
                RefreshAheadWindow = TimeSpan.FromMinutes(5),
            };

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Succeeded.ShouldBeTrue();
        }

        /// <summary>
        /// Verifies that every invalid value is reported at once, so that one restart fixes them all.
        /// </summary>
        [Fact]
        public void Given_SeveralInvalidValues_When_Validate_Then_ReportsAll()
        {
            // Arrange
            var options = new WorkloadIdentityOptions
            {
                AzureSql = CreateGoogleResource(),
                BlobStorage = new WorkloadIdentityResourceOptions { ManagedIdentity = new ManagedIdentityOptions() },
                RefreshAheadWindow = TimeSpan.Zero,
            };
            options.AzureSql.Google.ShouldNotBeNull().TenantId = null;

            // Act
            var result = this.validator.Validate(null, options);

            // Assert
            result.Failed.ShouldBeTrue();
            var message = result.FailureMessage.ShouldNotBeNull();
            message.ShouldContain("AzureSql:Google:TenantId");
            message.ShouldContain("BlobStorage:ManagedIdentity:ClientId");
            message.ShouldContain(nameof(WorkloadIdentityOptions.RefreshAheadWindow));
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
        /// Creates an Entra entry that mints its client assertion for a Google service account.
        /// </summary>
        /// <returns>The entry.</returns>
        private static EntraClientAssertionOptions CreateGoogleEntra()
        {
            return new EntraClientAssertionOptions
            {
                Provider = WorkloadIdentityProvider.Google,
                Google = new GoogleServiceAccountOptions { ServiceAccountEmail = ServiceAccountEmail },
            };
        }

        /// <summary>
        /// Creates a resource section that uses the system-assigned managed identity.
        /// </summary>
        /// <returns>The section.</returns>
        private static WorkloadIdentityResourceOptions CreateSystemAssignedResource()
        {
            return new WorkloadIdentityResourceOptions
            {
                Provider = WorkloadIdentityProvider.ManagedIdentity,
                ManagedIdentity = new ManagedIdentityOptions { UseSystemAssignedIdentity = true },
            };
        }
    }
}
