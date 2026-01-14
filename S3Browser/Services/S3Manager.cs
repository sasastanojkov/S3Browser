using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;

namespace S3Browser.Services
{
    /// <summary>
    /// Centralized manager for all S3 operations.
    /// Handles client lifecycle, bucket operations, object operations, and region detection.
    /// </summary>
    public sealed class S3Manager
    {
        private static readonly Lazy<S3Manager> _lazyInstance = new(() => new S3Manager());
        private readonly object _lock = new();

        // Client and credential management
        private IAmazonS3? _defaultS3Client;
        private string? _awsProfile;
        private bool _isAnonymousMode;

        private readonly Dictionary<string, IAmazonS3> _clientCache = new();
        private readonly HashSet<string> _publicBuckets = new();
        private readonly HashSet<string> _authenticatedBuckets = new();
        private readonly Dictionary<string, S3Bucket> _buckets = new();

        private S3Manager()
        {
        }

        /// <summary>
        /// Gets the singleton instance of the S3Manager.
        /// </summary>
        public static S3Manager Instance => _lazyInstance.Value;

        /// <summary>
        /// Initializes the S3Manager with an AWS profile.
        /// </summary>
        /// <param name="awsProfile">AWS profile name to use for authentication.</param>
        /// <param name="isAnonymousMode">If true, operates in anonymous mode without credentials.</param>
        /// <exception cref="InvalidOperationException">Thrown if profile cannot be loaded.</exception>
        public void Initialize(string? awsProfile, bool isAnonymousMode)
        {
            lock (_lock)
            {
                _awsProfile = awsProfile;
                _isAnonymousMode = isAnonymousMode;
                _defaultS3Client = null;
                _clientCache.Clear();
            }

            if (!isAnonymousMode && !string.IsNullOrEmpty(awsProfile))
            {
                var chain = new CredentialProfileStoreChain();
                if (!chain.TryGetProfile(awsProfile, out var profile))
                {
                    throw new InvalidOperationException($"Could not load AWS profile '{awsProfile}'.");
                }

                if (!chain.TryGetAWSCredentials(awsProfile, out var credentials))
                {
                    throw new InvalidOperationException($"Could not load AWS credentials for profile '{awsProfile}'.");
                }

                RegionEndpoint region = profile.Region ?? RegionEndpoint.USEast1;
                _defaultS3Client = new AmazonS3Client(credentials, region);
            }
        }

        /// <summary>
        /// Lists all buckets accessible with the current credentials.
        /// </summary>
        /// <returns>List of S3 bucket information.</returns>
        public async Task<List<S3Bucket>> ListBucketsAsync()
        {
            if (_isAnonymousMode)
            {
                // In anonymous mode, cannot list buckets - return cached public buckets if any
                lock (_lock)
                {
                    return _buckets.Values.Where(b => _publicBuckets.Contains(b.BucketName)).ToList();
                }
            }

            if (_defaultS3Client == null)
            {
                throw new InvalidOperationException("S3Manager not initialized. Call Initialize first.");
            }

            var response = await _defaultS3Client.ListBucketsAsync();
            var buckets = new List<S3Bucket>();

            foreach (var bucket in response.Buckets)
            {
                // Mark bucket as authenticated (we have access to it)
                _authenticatedBuckets.Add(bucket.BucketName);

                // Check if we have a cached bucket with region info
                lock (_lock)
                {
                    if (_buckets.TryGetValue(bucket.BucketName, out var cachedBucket) &&
                        !string.IsNullOrEmpty(cachedBucket.BucketRegion) &&
                        string.IsNullOrEmpty(bucket.BucketRegion))
                    {
                        // Use cached region if new response doesn't have it
                        bucket.BucketRegion = cachedBucket.BucketRegion;
                        System.Diagnostics.Debug.WriteLine($"[S3Manager] Restored cached region '{bucket.BucketRegion}' for bucket '{bucket.BucketName}'");
                    }

                    // Update cache with the latest bucket info
                    _buckets[bucket.BucketName] = bucket;
                }

                if (!string.IsNullOrEmpty(bucket.BucketRegion))
                {
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Bucket '{bucket.BucketName}' has region '{bucket.BucketRegion}'");
                }

                buckets.Add(bucket);
            }

            // Pre-fetch regions for authenticated buckets that don't have region info
            _ = Task.Run(async () =>
            {
                // Only fetch regions for buckets that don't have region info
                var bucketsNeedingRegion = buckets.Where(b => string.IsNullOrEmpty(b.BucketRegion)).ToList();

                if (bucketsNeedingRegion.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] All {buckets.Count} buckets already have region info");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[S3Manager] Pre-caching regions for {bucketsNeedingRegion.Count} buckets without region info");

                var regionTasks = bucketsNeedingRegion.Select(async bucket =>
                {
                    try
                    {
                        var region = await DetectBucketRegionWithAuthAsync(bucket.BucketName);
                        if (region != null)
                        {
                            // Update the bucket object's region
                            bucket.BucketRegion = region.SystemName;

                            // Also update in cache
                            lock (_lock)
                            {
                                if (_buckets.TryGetValue(bucket.BucketName, out var cachedBucket))
                                {
                                    cachedBucket.BucketRegion = region.SystemName;
                                }
                            }

                            System.Diagnostics.Debug.WriteLine($"[S3Manager] Pre-cached region '{region.SystemName}' for bucket '{bucket.BucketName}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[S3Manager] Failed to pre-cache region for bucket '{bucket.BucketName}': {ex.Message}");
                    }
                });

                await Task.WhenAll(regionTasks);
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Completed pre-caching regions for {bucketsNeedingRegion.Count} buckets");
            });

            // Add known public buckets that aren't in the authenticated list
            lock (_lock)
            {
                var bucketNames = new HashSet<string>(buckets.Select(b => b.BucketName));
                foreach (var publicBucketName in _publicBuckets)
                {
                    if (!bucketNames.Contains(publicBucketName) && _buckets.TryGetValue(publicBucketName, out var publicBucket))
                    {
                        buckets.Add(publicBucket);
                    }
                }
            }

            return buckets;
        }

        /// <summary>
        /// Lists objects in a bucket with optional prefix filtering.
        /// </summary>
        /// <param name="bucketName">Name of the S3 bucket.</param>
        /// <param name="prefix">Optional prefix to filter objects.</param>
        /// <returns>Result containing folders and files.</returns>
        public async Task<ListObjectsResult> ListObjectsAsync(string bucketName, string prefix = "")
        {
            var client = await GetS3ClientForBucketAsync(bucketName);
            if (client == null)
            {
                string mode = _isAnonymousMode ? "anonymous" : "authenticated";
                throw new InvalidOperationException($"Unable to access bucket '{bucketName}' in {mode} mode. " +
                    $"The bucket may not exist, may not be accessible with your credentials, or may be in a region that couldn't be detected.");
            }

            var request = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                Delimiter = "/"
            };

            var response = await client.ListObjectsV2Async(request);
            var result = new ListObjectsResult();

            // Extract folders (common prefixes)
            if (response.CommonPrefixes != null)
            {
                foreach (var commonPrefix in response.CommonPrefixes)
                {
                    if (!string.IsNullOrEmpty(commonPrefix))
                    {
                        result.Folders.Add(new S3FolderInfo
                        {
                            FullKey = commonPrefix,
                            Name = ExtractNameFromPrefix(commonPrefix, prefix)
                        });
                    }
                }
            }

            // Extract files (objects)
            if (response.S3Objects != null)
            {
                foreach (var s3Object in response.S3Objects)
                {
                    // Skip folder markers
                    if (s3Object.Key.EndsWith("/"))
                        continue;

                    result.Files.Add(new S3FileInfo
                    {
                        FullKey = s3Object.Key,
                        Name = ExtractNameFromKey(s3Object.Key, prefix),
                        Size = s3Object.Size ?? 0,
                        LastModified = s3Object.LastModified
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Gets metadata for a specific S3 object.
        /// </summary>
        public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key)
        {
            var client = await GetS3ClientForBucketAsync(bucketName);
            if (client == null)
            {
                throw new InvalidOperationException($"Unable to access bucket '{bucketName}'.");
            }

            var request = new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key
            };

            return await client.GetObjectMetadataAsync(request);
        }

        /// <summary>
        /// Gets an S3 object.
        /// </summary>
        public async Task<GetObjectResponse> GetObjectAsync(string bucketName, string key)
        {
            var client = await GetS3ClientForBucketAsync(bucketName);
            if (client == null)
            {
                throw new InvalidOperationException($"Unable to access bucket '{bucketName}'.");
            }

            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            return await client.GetObjectAsync(request);
        }

        /// <summary>
        /// Gets an S3 client for a specific bucket, with proper region and credentials.
        /// Handles both authenticated and anonymous access automatically.
        /// </summary>
        public async Task<IAmazonS3?> GetS3ClientForBucketAsync(string bucketName)
        {
            System.Diagnostics.Debug.WriteLine($"[S3Manager] GetS3ClientForBucketAsync: bucket='{bucketName}', isAnonymous={_isAnonymousMode}");

            lock (_lock)
            {
                // Check cache first
                if (_clientCache.TryGetValue(bucketName, out var cachedClient))
                {
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Using cached client for bucket '{bucketName}'");
                    return cachedClient;
                }
            }

            // Optimization: If bucket is not in authenticated list and we're not in anonymous mode,
            // skip authenticated access and go straight to anonymous
            bool tryAuthenticatedFirst = _isAnonymousMode == false &&
                                         _defaultS3Client != null &&
                                         _authenticatedBuckets.Contains(bucketName);

            // Try authenticated access first (only if bucket is in our authenticated bucket list)
            if (tryAuthenticatedFirst)
            {
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Bucket '{bucketName}' is in authenticated list, trying authenticated access");

                try
                {
                    // Get region from cached bucket info
                    RegionEndpoint? region = null;
                    lock (_lock)
                    {
                        if (_buckets.TryGetValue(bucketName, out var bucket) && !string.IsNullOrEmpty(bucket.BucketRegion))
                        {
                            region = RegionEndpoint.GetBySystemName(bucket.BucketRegion);
                            System.Diagnostics.Debug.WriteLine($"[S3Manager] Using cached region '{region.SystemName}' from bucket info");
                        }
                    }

                    // If no cached region, detect it
                    if (region == null)
                    {
                        region = await DetectBucketRegionWithAuthAsync(bucketName);
                        if (region != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[S3Manager] Detected region '{region.SystemName}' for bucket '{bucketName}'");
                            // Update cached bucket with region
                            lock (_lock)
                            {
                                if (_buckets.TryGetValue(bucketName, out var bucket))
                                {
                                    bucket.BucketRegion = region.SystemName;
                                }
                            }
                        }
                    }

                    IAmazonS3 clientToTest;
                    if (region != null)
                    {
                        // Check if we need a region-specific client
                        if (_defaultS3Client == null)
                        {
                            throw new InvalidOperationException("Default S3 client is not initialized.");
                        }

                        var defaultRegion = _defaultS3Client.Config.RegionEndpoint;
                        if (defaultRegion != null && defaultRegion.SystemName.Equals(region.SystemName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Same region - use default client
                            System.Diagnostics.Debug.WriteLine($"[S3Manager] Using default client (same region)");
                            clientToTest = _defaultS3Client;
                        }
                        else
                        {
                            // Bucket is in a different region, create region-specific client
                            System.Diagnostics.Debug.WriteLine($"[S3Manager] Creating region-specific client for '{region.SystemName}'");
                            clientToTest = CreateAuthenticatedClientForRegion(region);
                        }
                    }
                    else
                    {
                        // Region detection failed, try with default client
                        if (_defaultS3Client == null)
                        {
                            throw new InvalidOperationException("Default S3 client is not initialized.");
                        }

                        System.Diagnostics.Debug.WriteLine($"[S3Manager] Region detection failed, using default client");
                        clientToTest = _defaultS3Client;
                    }

                    // Test that we can actually list the bucket with authenticated credentials
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Testing authenticated access to bucket '{bucketName}'");
                    var testRequest = new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        MaxKeys = 1
                    };
                    await clientToTest.ListObjectsV2Async(testRequest);

                    // Success - cache and return the client
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Authenticated access successful for bucket '{bucketName}'");
                    CacheClient(bucketName, clientToTest);
                    return clientToTest;
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                                     ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Access denied - shouldn't happen for authenticated buckets, but fall through
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Unexpected: Authenticated access denied for bucket '{bucketName}' that was in ListBuckets");
                    lock (_lock)
                    {
                        _authenticatedBuckets.Remove(bucketName);
                    }
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Bucket '{bucketName}' not found (404)");
                    return null;
                }
                catch (AmazonS3Exception ex) when (ex.ErrorCode == "PermanentRedirect" || ex.Message.Contains("endpoint"))
                {
                    // Region mismatch - this means our region detection was wrong, clear and retry
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Region mismatch for bucket '{bucketName}': {ex.Message}");
                    lock (_lock)
                    {
                        if (_buckets.TryGetValue(bucketName, out var bucket))
                        {
                            bucket.BucketRegion = null;
                        }
                    }
                    // Fall through to anonymous access
                }
                catch (Exception ex)
                {
                    // Other errors - don't fall back to anonymous, rethrow
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] Unexpected error accessing bucket '{bucketName}': {ex.Message}");
                    throw;
                }
            }
            else if (!_isAnonymousMode && _defaultS3Client != null)
            {
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Bucket '{bucketName}' not in authenticated list, assuming public bucket");
            }

            // Try anonymous access
            System.Diagnostics.Debug.WriteLine($"[S3Manager] Trying anonymous access for bucket '{bucketName}'");

            // Detect region for this bucket
            RegionEndpoint regionToUse;
            try
            {
                regionToUse = await DetectBucketRegionAsync(bucketName);
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Detected region '{regionToUse.SystemName}' for anonymous access");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Failed to detect region for bucket '{bucketName}': {ex.Message}");
                regionToUse = RegionEndpoint.USEast1;
            }

            try
            {
                var anonymousClient = new AmazonS3Client(new AnonymousAWSCredentials(), regionToUse);

                // Test access
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Testing anonymous access to bucket '{bucketName}'");
                var testRequest = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 1
                };
                await anonymousClient.ListObjectsV2Async(testRequest);

                // Success - mark as public and cache
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Anonymous access successful for bucket '{bucketName}'");
                _publicBuckets.Add(bucketName);
                CacheClient(bucketName, anonymousClient);
                return anonymousClient;
            }
            catch (Exception ex)
            {
                // Log the actual error for debugging
                System.Diagnostics.Debug.WriteLine($"[S3Manager] Failed to access bucket '{bucketName}' anonymously in region '{regionToUse.SystemName}': {ex.Message}");
                if (ex is AmazonS3Exception s3Ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[S3Manager] S3 Error Code: {s3Ex.ErrorCode}, Status: {s3Ex.StatusCode}");
                }
                return null;
            }
        }

        /// <summary>
        /// Detects the AWS region for a bucket.
        /// </summary>
        public async Task<RegionEndpoint> DetectBucketRegionAsync(string bucketName)
        {
            // Check cached bucket info first
            lock (_lock)
            {
                if (_buckets.TryGetValue(bucketName, out var bucket) && !string.IsNullOrEmpty(bucket.BucketRegion))
                {
                    return RegionEndpoint.GetBySystemName(bucket.BucketRegion);
                }
            }

            // Try GetBucketLocation API
            try
            {
                var usEast1Client = new AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1);
                var locationRequest = new GetBucketLocationRequest
                {
                    BucketName = bucketName
                };
                var locationResponse = await usEast1Client.GetBucketLocationAsync(locationRequest);

                var regionString = string.IsNullOrEmpty(locationResponse.Location.Value) || locationResponse.Location.Value == "us-east-1"
                    ? "us-east-1"
                    : locationResponse.Location.Value;

                var region = RegionEndpoint.GetBySystemName(regionString);

                // Cache in bucket info
                CacheBucketRegion(bucketName, regionString);

                return region;
            }
            catch
            {
                // Fallback to brute force
            }

            // Try common regions
            var regionsToTry = new[]
            {
                RegionEndpoint.USEast1,
                RegionEndpoint.USWest2,
                RegionEndpoint.USWest1,
                RegionEndpoint.USEast2,
                RegionEndpoint.EUWest1,
                RegionEndpoint.EUCentral1,
                RegionEndpoint.APSoutheast1,
                RegionEndpoint.APNortheast1
            };

            foreach (var region in regionsToTry)
            {
                try
                {
                    var client = new AmazonS3Client(new AnonymousAWSCredentials(), region);
                    var request = new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        MaxKeys = 1
                    };
                    await client.ListObjectsV2Async(request);

                    // Cache in bucket info
                    CacheBucketRegion(bucketName, region.SystemName);

                    return region;
                }
                catch
                {
                    continue;
                }
            }

            // Default to us-east-1
            var defaultRegion = RegionEndpoint.USEast1;
            CacheBucketRegion(bucketName, "us-east-1");
            return defaultRegion;
        }

        /// <summary>
        /// Checks if a bucket is known to be public.
        /// </summary>
        public bool IsPublicBucket(string bucketName)
        {
            return _publicBuckets.Contains(bucketName);
        }

        /// <summary>
        /// Gets the current AWS profile name (if any).
        /// </summary>
        public string? GetAwsProfile() => _awsProfile;

        /// <summary>
        /// Gets whether the manager is in anonymous mode.
        /// </summary>
        public bool IsAnonymousMode() => _isAnonymousMode;

        private async Task<RegionEndpoint?> DetectBucketRegionWithAuthAsync(string bucketName)
        {
            if (_defaultS3Client == null)
                return null;

            try
            {
                var locationRequest = new GetBucketLocationRequest
                {
                    BucketName = bucketName
                };
                var locationResponse = await _defaultS3Client.GetBucketLocationAsync(locationRequest);

                if (string.IsNullOrEmpty(locationResponse.Location.Value) ||
                    locationResponse.Location.Value == "us-east-1")
                {
                    return RegionEndpoint.USEast1;
                }

                return RegionEndpoint.GetBySystemName(locationResponse.Location.Value);
            }
            catch
            {
                return null;
            }
        }

        private IAmazonS3 CreateAuthenticatedClientForRegion(RegionEndpoint region)
        {
            if (string.IsNullOrEmpty(_awsProfile))
            {
                throw new InvalidOperationException("AWS profile is not configured.");
            }

            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(_awsProfile, out var credentials))
            {
                return new AmazonS3Client(credentials, region);
            }

            if (_defaultS3Client == null)
            {
                throw new InvalidOperationException("No S3 client available.");
            }

            return _defaultS3Client;
        }

        private void CacheBucketRegion(string bucketName, string regionString)
        {
            lock (_lock)
            {
                if (!_buckets.ContainsKey(bucketName))
                {
                    _buckets[bucketName] = new S3Bucket { BucketName = bucketName, BucketRegion = regionString };
                }
                else
                {
                    _buckets[bucketName].BucketRegion = regionString;
                }
            }
        }

        private void CacheClient(string bucketName, IAmazonS3 client)
        {
            lock (_lock)
            {
                _clientCache[bucketName] = client;
            }
        }

        private string ExtractNameFromPrefix(string fullKey, string prefix)
        {
            var name = fullKey.TrimEnd('/');
            if (!string.IsNullOrEmpty(prefix))
            {
                name = name.Substring(prefix.Length);
            }
            return name;
        }

        private string ExtractNameFromKey(string fullKey, string prefix)
        {
            var name = fullKey;
            if (!string.IsNullOrEmpty(prefix))
            {
                name = name.Substring(prefix.Length);
            }
            return name;
        }
    }

    /// <summary>
    /// Information about an S3 folder (common prefix).
    /// </summary>
    public class S3FolderInfo
    {
        public string FullKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Information about an S3 file (object).
    /// </summary>
    public class S3FileInfo
    {
        public string FullKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime? LastModified { get; set; }
    }

    /// <summary>
    /// Result of listing objects in a bucket.
    /// </summary>
    public class ListObjectsResult
    {
        public List<S3FolderInfo> Folders { get; set; } = new();
        public List<S3FileInfo> Files { get; set; } = new();
    }
}
