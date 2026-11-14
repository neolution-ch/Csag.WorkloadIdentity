namespace Csag.WorkloadIdentity.Abstractions
{
    /// <summary>
    /// Provides the client assertion that the application sends to Microsoft Entra ID in place of a client secret.
    /// An example is the token request of a Microsoft sign-in.
    /// </summary>
    public interface IEntraClientAssertionProvider
    {
        /// <summary>
        /// Returns a token with audience <c>api://AzureADTokenExchange</c> for the identity of the
        /// <see cref="Options.WorkloadIdentityOptions.Entra"/> entry. With the Google provider, each call requests a new
        /// ID token from Google. With the managed identity provider, the credential returns its cached token until the
        /// token nears expiry.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The token, to send as the <c>client_assertion</c>.</returns>
        Task<string> GetClientAssertionAsync(CancellationToken cancellationToken);
    }
}
