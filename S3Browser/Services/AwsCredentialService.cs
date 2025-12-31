using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;

namespace S3Browser.Services
{
    /// <summary>
    /// Service for resolving AWS credentials from various sources including named profiles and default credential chain.
    /// </summary>
    public sealed class AwsCredentialService
    {
        private readonly CredentialProfileStoreChain _credentialChain;

        /// <summary>
        /// Initializes a new instance of the AwsCredentialService.
        /// Creates an internal credential profile store chain for resolving credentials.
        /// </summary>
        public AwsCredentialService()
        {
            _credentialChain = new CredentialProfileStoreChain();
        }

        /// <summary>
        /// Attempts to get AWS credentials for a specific profile or default sources.
        /// </summary>
        /// <param name="profileName">The AWS profile name. If null or empty, attempts to get credentials from default sources.</param>
        /// <returns>A task that represents the asynchronous operation and contains the AWS credentials.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when credentials cannot be retrieved. Error message includes helpful instructions for SSO login.
        /// </exception>
        public Task<AWSCredentials> GetCredentialsAsync(string? profileName = null)
        {
            AWSCredentials? credentials = null;

            if (!string.IsNullOrEmpty(profileName))
            {
                if (!_credentialChain.TryGetAWSCredentials(profileName, out credentials))
                {
                    throw new InvalidOperationException(
                        $"Unable to retrieve AWS credentials for profile '{profileName}'.\n\n" +
                        $"Make sure you have run 'aws sso login --profile {profileName}' before starting this application.");
                }
            }
            else
            {
                // Try to get credentials from default profile
                if (!_credentialChain.TryGetAWSCredentials(null, out credentials))
                {
                    throw new InvalidOperationException(
                        "Unable to retrieve AWS credentials from default sources.");
                }
            }

            if (credentials is null)
            {
                throw new InvalidOperationException("Unable to retrieve AWS credentials.");
            }

            return Task.FromResult(credentials);
        }

        /// <summary>
        /// Checks if a profile exists in the credential store.
        /// </summary>
        /// <param name="profileName">The name of the profile to check.</param>
        /// <param name="profile">When this method returns, contains the credential profile if found; otherwise, null.</param>
        /// <returns>True if the profile exists; otherwise, false.</returns>
        public bool TryGetProfile(string profileName, out CredentialProfile? profile)
        {
            return _credentialChain.TryGetProfile(profileName, out profile);
        }

        /// <summary>
        /// Gets immutable credentials that can be used for DuckDB S3 configuration.
        /// Resolves credentials and returns the immutable form containing access key, secret key, and session token.
        /// </summary>
        /// <param name="profileName">The AWS profile name. If null or empty, attempts to get credentials from default sources.</param>
        /// <returns>A task that represents the asynchronous operation and contains the immutable credentials.</returns>
        /// <exception cref="InvalidOperationException">Thrown when credentials cannot be retrieved.</exception>
        public async Task<ImmutableCredentials> GetImmutableCredentialsAsync(string? profileName = null)
        {
            var credentials = await GetCredentialsAsync(profileName);
            return await credentials.GetCredentialsAsync();
        }
    }
}
