namespace Csag.WorkloadIdentity.Options
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="WorkloadIdentityOptions"/>. Every section is optional. Every configured resource must
    /// carry the complete settings of the provider it selects. A configured Entra entry must name a service account
    /// or a managed identity. The refresh-ahead window must be positive and at most five minutes. Every
    /// failure is reported, each naming the configuration path of the offending value relative to the options section.
    /// </summary>
    internal sealed class WorkloadIdentityOptionsValidator : IValidateOptions<WorkloadIdentityOptions>
    {
        /// <summary>
        /// The longest refresh-ahead window that still obtains a fresh token. The credentials cache their tokens in
        /// MSAL, which keeps returning the cached token until five minutes before it expires, so an earlier refresh
        /// gets the same token back.
        /// </summary>
        private static readonly TimeSpan MaximumRefreshAheadWindow = TimeSpan.FromMinutes(5);

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, WorkloadIdentityOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var builder = new ValidateOptionsResultBuilder();
            ValidateResource(nameof(options.AzureSql), options.AzureSql, builder);
            ValidateResource(nameof(options.BlobStorage), options.BlobStorage, builder);
            ValidateEntra(nameof(options.Entra), options.Entra, builder);

            if (options.RefreshAheadWindow <= TimeSpan.Zero)
            {
                builder.AddError($"{nameof(options.RefreshAheadWindow)} must be a positive duration.");
            }

            if (options.RefreshAheadWindow > MaximumRefreshAheadWindow)
            {
                builder.AddError($"{nameof(options.RefreshAheadWindow)} must not exceed five minutes: the credentials return their cached token until five minutes before expiry, so a longer window cannot refresh earlier.");
            }

            return builder.Build();
        }

        /// <summary>
        /// Validates a resource section, if present, against the provider it selects.
        /// </summary>
        /// <param name="path">The configuration path of the section.</param>
        /// <param name="resource">The section, or <see langword="null"/> when the resource is not configured.</param>
        /// <param name="builder">Receives a message for every invalid value.</param>
        private static void ValidateResource(string path, WorkloadIdentityResourceOptions? resource, ValidateOptionsResultBuilder builder)
        {
            if (resource is null)
            {
                return;
            }

            switch (resource.Provider)
            {
                case WorkloadIdentityProvider.ManagedIdentity:
                    ValidateManagedIdentity(path, resource.ManagedIdentity, builder);
                    break;
                case WorkloadIdentityProvider.Google:
                    ValidateGoogle(path, resource.Google, builder);
                    break;
                default:
                    builder.AddError($"{path}:{nameof(resource.Provider)} must be {nameof(WorkloadIdentityProvider.ManagedIdentity)} or {nameof(WorkloadIdentityProvider.Google)}.");
                    break;
            }
        }

        /// <summary>
        /// Validates the Entra entry, if present, against the provider it selects.
        /// </summary>
        /// <param name="path">The configuration path of the entry.</param>
        /// <param name="entra">The entry, or <see langword="null"/> when it is not configured.</param>
        /// <param name="builder">Receives a message for every invalid value.</param>
        private static void ValidateEntra(string path, EntraClientAssertionOptions? entra, ValidateOptionsResultBuilder builder)
        {
            if (entra is null)
            {
                return;
            }

            switch (entra.Provider)
            {
                case WorkloadIdentityProvider.ManagedIdentity:
                    ValidateManagedIdentity(path, entra.ManagedIdentity, builder);
                    break;
                case WorkloadIdentityProvider.Google:
                    ValidateEntraGoogle(path, entra.Google, builder);
                    break;
                default:
                    builder.AddError($"{path}:{nameof(entra.Provider)} must be {nameof(WorkloadIdentityProvider.ManagedIdentity)} or {nameof(WorkloadIdentityProvider.Google)}.");
                    break;
            }
        }

        /// <summary>
        /// Validates the Google section of the Entra entry.
        /// </summary>
        /// <param name="path">The configuration path of the entry.</param>
        /// <param name="google">The Google section, or <see langword="null"/> when it is absent.</param>
        /// <param name="builder">Receives a message for every invalid value.</param>
        private static void ValidateEntraGoogle(string path, GoogleServiceAccountOptions? google, ValidateOptionsResultBuilder builder)
        {
            var sectionPath = $"{path}:{nameof(EntraClientAssertionOptions.Google)}";
            if (google is null)
            {
                builder.AddError($"{sectionPath} section must be provided when {path}:{nameof(EntraClientAssertionOptions.Provider)} is {nameof(WorkloadIdentityProvider.Google)}.");
                return;
            }

            RequireValue(sectionPath, nameof(google.ServiceAccountEmail), google.ServiceAccountEmail, builder);
        }

        /// <summary>
        /// Validates the managed identity section of a resource or of the Entra entry.
        /// </summary>
        /// <param name="path">The configuration path of the resource section or of the Entra entry.</param>
        /// <param name="managedIdentity">The managed identity section, or <see langword="null"/> when it is absent.</param>
        /// <param name="builder">Receives a message for every invalid value.</param>
        private static void ValidateManagedIdentity(string path, ManagedIdentityOptions? managedIdentity, ValidateOptionsResultBuilder builder)
        {
            var sectionPath = $"{path}:{nameof(WorkloadIdentityResourceOptions.ManagedIdentity)}";
            if (managedIdentity is null)
            {
                builder.AddError($"{sectionPath} section must be provided when {path}:{nameof(WorkloadIdentityResourceOptions.Provider)} is {nameof(WorkloadIdentityProvider.ManagedIdentity)}.");
                return;
            }

            if (!managedIdentity.UseSystemAssignedIdentity && string.IsNullOrWhiteSpace(managedIdentity.ClientId))
            {
                builder.AddError($"{sectionPath}:{nameof(managedIdentity.ClientId)} must be provided unless {sectionPath}:{nameof(managedIdentity.UseSystemAssignedIdentity)} is true.");
            }
        }

        /// <summary>
        /// Validates the Google section of a resource.
        /// </summary>
        /// <param name="path">The configuration path of the resource section.</param>
        /// <param name="google">The Google section, or <see langword="null"/> when it is absent.</param>
        /// <param name="builder">Receives a message for every invalid value.</param>
        private static void ValidateGoogle(string path, GoogleOptions? google, ValidateOptionsResultBuilder builder)
        {
            var sectionPath = $"{path}:{nameof(WorkloadIdentityResourceOptions.Google)}";
            if (google is null)
            {
                builder.AddError($"{sectionPath} section must be provided when {path}:{nameof(WorkloadIdentityResourceOptions.Provider)} is {nameof(WorkloadIdentityProvider.Google)}.");
                return;
            }

            RequireValue(sectionPath, nameof(google.TenantId), google.TenantId, builder);
            RequireValue(sectionPath, nameof(google.ClientId), google.ClientId, builder);
            RequireValue(sectionPath, nameof(google.ServiceAccountEmail), google.ServiceAccountEmail, builder);
        }

        /// <summary>
        /// Records a failure when a required value is blank.
        /// </summary>
        /// <param name="sectionPath">The configuration path of the section holding the value.</param>
        /// <param name="key">The key of the value within the section.</param>
        /// <param name="value">The value.</param>
        /// <param name="builder">Receives the message when the value is blank.</param>
        private static void RequireValue(string sectionPath, string key, string? value, ValidateOptionsResultBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                builder.AddError($"{sectionPath}:{key} must be provided.");
            }
        }
    }
}
