namespace Csag.WorkloadIdentity.Options
{
    /// <summary>
    /// Options for obtaining Azure access tokens with the application's workload identity. Each supported resource
    /// has its own section; a resource whose section is absent is not configured, and its token provider cannot be
    /// resolved. The <see cref="Entra"/> entry configures the client assertion that the application sends to
    /// Microsoft Entra ID. Without the entry, the client assertion provider cannot be resolved.
    /// </summary>
    public class WorkloadIdentityOptions
    {
        /// <summary>
        /// Gets the configuration section the options are bound from by default.
        /// </summary>
        public static string ConfigurationSectionName { get; } = "Csag.WorkloadIdentity";

        /// <summary>
        /// Gets or sets how the client assertion for Microsoft Entra ID is obtained, or <see langword="null"/> when
        /// the application sends no client assertion, for example because its Microsoft sign-in uses a client secret.
        /// </summary>
        public EntraClientAssertionOptions? Entra { get; set; }

        /// <summary>
        /// Gets or sets how access tokens for Azure SQL are obtained, or <see langword="null"/> when the application
        /// does not use Azure SQL.
        /// </summary>
        public WorkloadIdentityResourceOptions? AzureSql { get; set; }

        /// <summary>
        /// Gets or sets how access tokens for Azure Blob Storage are obtained, or <see langword="null"/> when the
        /// application does not use Blob Storage.
        /// </summary>
        public WorkloadIdentityResourceOptions? BlobStorage { get; set; }

        /// <summary>
        /// Gets or sets how long before an access token expires it is treated as due for refresh. A call that finds
        /// the current token inside this window obtains a fresh one, and the background refresh service refreshes
        /// each token this long before it expires. Applies to every resource. Must be positive and at most five
        /// minutes, because the credentials return their cached token until five minutes before expiry; defaults to
        /// five minutes.
        /// </summary>
        public TimeSpan RefreshAheadWindow { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets a value indicating whether a hosted service keeps the access token of every configured
        /// resource refreshed ahead of its expiry, so that callers are served from the held token instead of waiting
        /// for a token request. Defaults to <see langword="true"/>.
        /// </summary>
        public bool EnableBackgroundRefresh { get; set; } = true;
    }
}
