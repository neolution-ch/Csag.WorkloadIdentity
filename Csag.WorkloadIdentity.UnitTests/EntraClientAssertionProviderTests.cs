namespace Csag.WorkloadIdentity.UnitTests
{
    using Azure.Core;
    using Csag.WorkloadIdentity.Abstractions;
    using Csag.WorkloadIdentity.Internal;
    using Csag.WorkloadIdentity.Options;
    using Microsoft.Extensions.Options;
    using NSubstitute;
    using Shouldly;

    /// <summary>
    /// Unit tests for the <see cref="EntraClientAssertionProvider"/> class.
    /// </summary>
    public class EntraClientAssertionProviderTests
    {
        /// <summary>
        /// The Google service account email of the Google entry.
        /// </summary>
        private const string ServiceAccountEmail = "sa@example.iam.gserviceaccount.com";

        /// <summary>
        /// The client ID of the user-assigned managed identity of the managed identity entry.
        /// </summary>
        private const string UserAssignedClientId = "11111111-2222-3333-4444-555555555555";

        /// <summary>
        /// The substituted Google ID token provider.
        /// </summary>
        private readonly IGoogleIdTokenProvider googleIdTokenProvider = Substitute.For<IGoogleIdTokenProvider>();

        /// <summary>
        /// The substituted managed identity credential factory.
        /// </summary>
        private readonly IManagedIdentityCredentialFactory credentialFactory = Substitute.For<IManagedIdentityCredentialFactory>();

        /// <summary>
        /// Verifies that an entry with the Google provider returns the Google ID token of its service account and uses
        /// no managed identity.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_GoogleEntry_When_GetClientAssertionAsync_Then_ReturnsGoogleIdTokenOfServiceAccount()
        {
            // Arrange
            this.googleIdTokenProvider.GetIdTokenAsync(ServiceAccountEmail, Arg.Any<CancellationToken>()).Returns("google-id-token");
            var provider = this.CreateProvider(new EntraClientAssertionOptions
            {
                Google = new GoogleServiceAccountOptions { ServiceAccountEmail = ServiceAccountEmail },
            });

            // Act
            var assertion = await provider.GetClientAssertionAsync(CancellationToken.None);

            // Assert
            assertion.ShouldBe("google-id-token");
            this.credentialFactory.DidNotReceiveWithAnyArgs().Create(default!);
        }

        /// <summary>
        /// Verifies that an entry with the managed identity provider asks its user-assigned identity for a token with
        /// the audience Microsoft Entra ID expects, and makes no call to Google.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        [Fact]
        public async Task Given_ManagedIdentityEntry_When_GetClientAssertionAsync_Then_ReturnsManagedIdentityTokenWithEntraAudience()
        {
            // Arrange
            var credential = Substitute.For<TokenCredential>();
            credential.GetTokenAsync(Arg.Any<TokenRequestContext>(), Arg.Any<CancellationToken>())
                .Returns(new AccessToken("managed-identity-token", DateTimeOffset.UtcNow.AddHours(1)));
            this.credentialFactory.Create(Arg.Any<ManagedIdentityOptions>()).Returns(credential);
            var provider = this.CreateProvider(new EntraClientAssertionOptions
            {
                Provider = WorkloadIdentityProvider.ManagedIdentity,
                ManagedIdentity = new ManagedIdentityOptions { ClientId = UserAssignedClientId },
            });

            // Act
            var assertion = await provider.GetClientAssertionAsync(CancellationToken.None);

            // Assert
            assertion.ShouldBe("managed-identity-token");
            this.credentialFactory.Received(1).Create(Arg.Is<ManagedIdentityOptions>(identity => identity.ClientId == UserAssignedClientId));
            await credential.Received(1).GetTokenAsync(Arg.Is<TokenRequestContext>(context => context.Scopes.Single() == "api://AzureADTokenExchange/.default"), Arg.Any<CancellationToken>());
            await this.googleIdTokenProvider.DidNotReceiveWithAnyArgs().GetIdTokenAsync(default!, default);
        }

        /// <summary>
        /// Verifies that options without an Entra entry fail with a message naming the entry.
        /// </summary>
        [Fact]
        public void Given_NoEntraEntry_When_Constructing_Then_ThrowsInvalidOperationExceptionNamingEntry()
        {
            // Act
            var exception = Should.Throw<InvalidOperationException>(() => this.CreateProvider(entra: null));

            // Assert
            exception.Message.ShouldContain("Csag.WorkloadIdentity:Entra");
        }

        /// <summary>
        /// Creates the provider for options that hold the given Entra entry.
        /// </summary>
        /// <param name="entra">The Entra entry, or <see langword="null"/> for none.</param>
        /// <returns>The provider.</returns>
        private EntraClientAssertionProvider CreateProvider(EntraClientAssertionOptions? entra)
        {
            return new EntraClientAssertionProvider(Options.Create(new WorkloadIdentityOptions { Entra = entra }), this.googleIdTokenProvider, this.credentialFactory);
        }
    }
}
