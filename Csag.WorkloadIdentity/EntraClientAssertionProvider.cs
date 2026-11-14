namespace Csag.WorkloadIdentity
{
    using Azure.Core;
    using Csag.WorkloadIdentity.Abstractions;
    using Csag.WorkloadIdentity.Internal;
    using Csag.WorkloadIdentity.Options;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Provides the client assertion for Microsoft Entra ID with the identity of the
    /// <see cref="WorkloadIdentityOptions.Entra"/> entry. The identity is a Google service account or a managed
    /// identity. The provider holds no token. Sign-ins are not frequent, so the background refresh does not
    /// include this entry. <see cref="WorkloadIdentityServiceCollectionExtensions"/> registers the provider. A caller
    /// can resolve it only when the entry is configured.
    /// </summary>
    internal sealed class EntraClientAssertionProvider : IEntraClientAssertionProvider
    {
        /// <summary>
        /// The scope of a managed identity token that Microsoft Entra ID accepts as a client assertion. It gives the
        /// token the audience <c>api://AzureADTokenExchange</c>, the audience of the federated credential.
        /// </summary>
        private const string TokenExchangeScope = "api://AzureADTokenExchange/.default";

        /// <summary>
        /// Gets the client assertion with the identity the entry selects.
        /// </summary>
        private readonly Func<CancellationToken, Task<string>> getAssertion;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntraClientAssertionProvider"/> class.
        /// </summary>
        /// <param name="options">The workload identity options, validated when they are read.</param>
        /// <param name="googleIdTokenProvider">Mints the Google ID tokens.</param>
        /// <param name="credentialFactory">Creates the managed identity credential.</param>
        /// <exception cref="InvalidOperationException">The Entra entry is not configured.</exception>
        public EntraClientAssertionProvider(
            IOptions<WorkloadIdentityOptions> options,
            IGoogleIdTokenProvider googleIdTokenProvider,
            IManagedIdentityCredentialFactory credentialFactory)
        {
            ArgumentNullException.ThrowIfNull(options);

            var entra = options.Value.Entra
                ?? throw new InvalidOperationException($"The {nameof(WorkloadIdentityOptions.Entra)} entry is not configured. Add the '{WorkloadIdentityOptions.ConfigurationSectionName}:{nameof(WorkloadIdentityOptions.Entra)}' configuration section, or set {nameof(WorkloadIdentityOptions)}.{nameof(WorkloadIdentityOptions.Entra)} when configuring the options in code.");

            if (entra.Provider == WorkloadIdentityProvider.ManagedIdentity)
            {
                var credential = credentialFactory.Create(entra.ManagedIdentity!);
                this.getAssertion = async cancellationToken =>
                {
                    var token = await credential.GetTokenAsync(new TokenRequestContext([TokenExchangeScope]), cancellationToken).ConfigureAwait(false);
                    return token.Token;
                };
            }
            else
            {
                var serviceAccountEmail = entra.Google!.ServiceAccountEmail!;
                this.getAssertion = cancellationToken => googleIdTokenProvider.GetIdTokenAsync(serviceAccountEmail, cancellationToken);
            }
        }

        /// <inheritdoc />
        public Task<string> GetClientAssertionAsync(CancellationToken cancellationToken)
        {
            return this.getAssertion(cancellationToken);
        }
    }
}
