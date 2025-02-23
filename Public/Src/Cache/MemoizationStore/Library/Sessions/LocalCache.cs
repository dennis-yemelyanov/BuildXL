// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Service;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A single-level local cache. This is also a factory for <see cref="OneLevelCache"/>. There are many mixes of
    ///     <see cref="IMemoizationStore"/> and <see cref="IContentStore"/> supported, each depending on which function
    ///     is being called.
    /// </summary>
    /// <remarks>
    ///     "Local" here is used in the sense that it is located in this machine, and not over the network. For
    ///     example, the cache could live in a different process and communicate over gRPC.
    ///
    ///     "InProc" and "Remote" refer to whether the cache logic is executed in the process that is building the
    ///     object or in a external process.
    /// </remarks>
    public class LocalCache : OneLevelCache
    {
        private const string IdFileName = "Cache.id";

        private readonly IAbsFileSystem _fileSystem;

        /// <summary>
        ///     Content Stores:
        ///         - <see cref="ServiceClientContentStore"/> if <see cref="LocalCacheConfiguration.ServiceClientContentStoreConfiguration"/>
        ///         - <see cref="FileSystemContentStore"/> otherwise
        ///     Memoization Stores:
        ///         - <see cref="CreateInProcessLocalMemoizationStoreFactory(ILogger, IClock, MemoizationStoreConfiguration)"/>
        /// </summary>
        public static LocalCache CreateUnknownContentStoreInProcMemoizationStoreCache(
            ILogger logger,
            AbsolutePath rootPath,
            MemoizationStoreConfiguration memoizationStoreConfiguration,
            LocalCacheConfiguration localCacheConfiguration,
            ConfigurationModel configurationModel = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool assumeCallerCreatesDirectoryForPlace = false)
        {
            clock ??= SystemClock.Instance;

            var fileSystem = new PassThroughFileSystem(logger);
            var contentStoreSettings = new ContentStoreSettings
            {
                CheckFiles = checkLocalFiles,
                AssumeCallerCreatesDirectoryForPlace = assumeCallerCreatesDirectoryForPlace,
            };

            Func<IContentStore> contentStoreFactory = () =>
            {
                if (localCacheConfiguration.ServiceClientContentStoreConfiguration != null)
                {
                    return new ServiceClientContentStore(logger, fileSystem, localCacheConfiguration.ServiceClientContentStoreConfiguration);
                }
                else
                {
                    return new FileSystemContentStore(
                                    fileSystem,
                                    clock,
                                    rootPath,
                                    configurationModel: configurationModel,
                                    settings: contentStoreSettings);
                }
            };

            var memoizationStoreFactory = CreateInProcessLocalMemoizationStoreFactory(logger, clock, memoizationStoreConfiguration);
            return new LocalCache(fileSystem, contentStoreFactory, memoizationStoreFactory, LoadPersistentCacheGuid(rootPath, fileSystem));
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalCache" /> class backed by <see cref="TwoContentStore"/> implemented as <see cref="StreamPathContentStore"/>
        /// </summary>
        public static LocalCache CreateStreamPathContentStoreInProcMemoizationStoreCache(ILogger logger,
            AbsolutePath rootPathForStream,
            AbsolutePath rootPathForPath,
            MemoizationStoreConfiguration memoConfig,
            ConfigurationModel configurationModelForStream = null,
            ConfigurationModel configurationModelForPath = null,
            IClock clock = null,
            bool checkLocalFiles = true,
            bool assumeCallerCreatesDirectoryForPlace = false)
        {
            var fileSystem = new PassThroughFileSystem(logger);
            clock ??= SystemClock.Instance;

            var contentStoreSettings = new ContentStoreSettings
            {
                CheckFiles = checkLocalFiles,
                AssumeCallerCreatesDirectoryForPlace = assumeCallerCreatesDirectoryForPlace,
            };

            Func<IContentStore> contentStoreFactory = () => new StreamPathContentStore(
                                () => new FileSystemContentStore(fileSystem, clock, rootPathForStream, configurationModelForStream, settings: contentStoreSettings),
                                () => new FileSystemContentStore(fileSystem, clock, rootPathForPath, configurationModelForPath, settings: contentStoreSettings));
            var memoizationStoreFactory = CreateInProcessLocalMemoizationStoreFactory(logger, clock, memoConfig);
            return new LocalCache(fileSystem, contentStoreFactory, memoizationStoreFactory, LoadPersistentCacheGuid(rootPathForStream, fileSystem));
        }

        /// <summary>
        ///     Both content and metadata are entirely backed by an out-of-proc cache.
        /// </summary>
        public static ICache CreateRpcCache(
            ILogger logger,
            ServiceClientContentStoreConfiguration serviceClientCacheConfiguration)
        {
            var fileSystem = new PassThroughFileSystem(logger);
            return new ServiceClientCache(logger, fileSystem, serviceClientCacheConfiguration);
        }

        /// <summary>
        ///     Both content and metadata are entirely backed by an out-of-proc L2 cache, and asynchronously published to an L3 cache
        /// </summary>
        public static ICache CreatePublishingRpcCache(
            ILogger logger,
            ServiceClientContentStoreConfiguration serviceClientCacheConfiguration,
            PublishingCacheConfiguration publishingConfiguration,
            string personalAccessToken)
        {
            var fileSystem = new PassThroughFileSystem(logger);

            return new ServiceClientPublishingCache(
                logger,
                fileSystem,
                serviceClientCacheConfiguration,
                publishingConfiguration,
                personalAccessToken);
        }

        private LocalCache(IAbsFileSystem fileSystem, Func<IContentStore> contentStoreFunc, Func<IMemoizationStore> memoizationStoreFunc, Guid id)
            : base(contentStoreFunc, memoizationStoreFunc, id)
        {
            Contract.Requires(fileSystem != null);

            _fileSystem = fileSystem;
        }

        private static Guid LoadPersistentCacheGuid(AbsolutePath rootPath, IAbsFileSystem fileSystem)
        {
            return PersistentId.Load(fileSystem, rootPath / IdFileName);
        }

        private static Func<IMemoizationStore> CreateInProcessLocalMemoizationStoreFactory(ILogger logger, IClock clock, MemoizationStoreConfiguration config)
        {
            Contract.Requires(config != null);

            return () => config.CreateStore(logger, clock ?? SystemClock.Instance);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> PreStartupAsync(Context context)
        {
            var contentStore = ContentStore as IAcquireDirectoryLock;
            if (contentStore != null)
            {
                var acquireLockResult = await contentStore.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    return acquireLockResult;
                }
            }

            if (MemoizationStore is IAcquireDirectoryLock memoizationStore)
            {
                var acquireLockResult = await memoizationStore.AcquireDirectoryLockAsync(context);
                if (!acquireLockResult.Succeeded)
                {
                    if (contentStore != null)
                    {
                        ContentStore?.Dispose(); // Dispose to release the content store's directory lock.
                    }

                    return acquireLockResult;
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            _fileSystem?.Dispose();
            base.DisposeCore();
        }
    }
}
