namespace Csag.WorkloadIdentity.Options
{
    /// <summary>
    /// Configures the client assertion that the application sends to Microsoft Entra ID in place of a client secret,
    /// for example at the code exchange of a Microsoft sign-in. The sign-in configuration of the application holds
    /// the tenant ID and the client ID. This entry holds only the identity that gets the client assertion.
    /// </summary>
    public class EntraClientAssertionOptions
    {
        /// <summary>
        /// Gets or sets the identity provider the client assertion is obtained with. Defaults to
        /// <see cref="WorkloadIdentityProvider.Google"/>.
        /// </summary>
        public WorkloadIdentityProvider Provider { get; set; } = WorkloadIdentityProvider.Google;

        /// <summary>
        /// Gets or sets the managed identity settings. Required when <see cref="Provider"/> is
        /// <see cref="WorkloadIdentityProvider.ManagedIdentity"/>; ignored otherwise.
        /// </summary>
        public ManagedIdentityOptions? ManagedIdentity { get; set; }

        /// <summary>
        /// Gets or sets the Google service account settings. Required when <see cref="Provider"/> is
        /// <see cref="WorkloadIdentityProvider.Google"/>; ignored otherwise.
        /// </summary>
        public GoogleServiceAccountOptions? Google { get; set; }
    }
}
