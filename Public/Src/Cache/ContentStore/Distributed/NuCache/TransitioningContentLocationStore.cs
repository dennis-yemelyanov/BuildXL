// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// <see cref="IContentLocationStore"/> implementation that supports old redis and new local location store.
    /// </summary>
    public class TransitioningContentLocationStore : StartupShutdownBase, IContentLocationStore, IDistributedLocationStore, IDistributedMachineInfo
    {
        /// <nodoc />
        public ILocalContentStore LocalContentStore { get; }

        /// <nodoc />
        public MachineLocation LocalMachineLocation { get; }

        /// <summary>
        /// The local machine id. Settable for testing purposes.
        /// </summary>
        public MachineId LocalMachineId { get; internal set; }

        private readonly LocalLocationStoreConfiguration _configuration;

        /// <nodoc />
        public TransitioningContentLocationStore(
            LocalLocationStoreConfiguration configuration,
            LocalLocationStore localLocationStore,
            MachineLocation localMachineLocation,
            ILocalContentStore localContentStore)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(localMachineLocation.IsValid);

            LocalContentStore = localContentStore;

            _configuration = configuration;
            LocalLocationStore = localLocationStore;
            LocalMachineLocation = localMachineLocation;
        }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(TransitioningContentLocationStore));

        /// <inheritdoc />
        public MachineReputationTracker MachineReputationTracker => LocalLocationStore.MachineReputationTracker;

        /// <summary>
        /// Exposes <see cref="LocalLocationStore"/>. Mostly for testing purposes.
        /// </summary>
        public LocalLocationStore LocalLocationStore { get; }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var success = await LocalLocationStore.StartupAsync(context.TracingContext);
            if (success)
            {
                if (LocalLocationStore.ClusterState.TryResolveMachineId(LocalMachineLocation, out var localMachineId))
                {
                    LocalMachineId = localMachineId;
                }
                else if (_configuration.DistributedContentConsumerOnly)
                {
                    LocalMachineId = LocalLocationStore.ClusterState.PrimaryMachineId;
                }
                else
                {
                    return new BoolResult($"Unable to resolve machine id for location {LocalMachineLocation} in cluster state.");
                }

                LocalLocationStore.PostInitialization(LocalMachineId, LocalContentStore);
            }

            return success;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return LocalLocationStore.ShutdownAsync(context.TracingContext);
        }

        /// <nodoc />
        public Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, GetBulkOrigin origin)
        {
            return LocalLocationStore.GetBulkAsync(context, LocalMachineId, contentHashes, origin);
        }

        /// <nodoc />
        public Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentHashes, bool touch, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return LocalLocationStore.RegisterLocalLocationAsync(context, LocalMachineId, contentHashes, touch, urgencyHint: urgencyHint);
        }

        /// <inheritdoc />
        public Task<GetBulkLocationsResult> GetBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, GetBulkOrigin origin)
        {
            var operationContext = new OperationContext(context, cts);
            return GetBulkAsync(operationContext, contentHashes, origin);
        }

        /// <inheritdoc />
        public Task<BoolResult> RegisterLocalLocationAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, bool touch)
        {
            var operationContext = new OperationContext(context, cts);
            return RegisterLocalLocationAsync(operationContext, contentHashes, touch, urgencyHint);
        }

        /// <inheritdoc />
        public Task<BoolResult> TrimBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            var operationContext = new OperationContext(context, cts);
            return LocalLocationStore.TrimBulkAsync(operationContext, LocalMachineId, contentHashes);
        }

        /// <inheritdoc />
        public Task<BoolResult> TouchBulkAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            var operationContext = new OperationContext(context, cts);
            return LocalLocationStore.TouchBulkAsync(operationContext, LocalMachineId, contentHashes.SelectList(c => c.Hash));
        }

        /// <inheritdoc />
        public CounterSet GetCounters(Context context)
        {
            return LocalLocationStore.GetCounters(context);
        }

        /// <inheritdoc />
        public Task<BoolResult> InvalidateLocalMachineAsync(Context context, CancellationToken cts)
        {
            var operationContext = new OperationContext(context, cts);
            return LocalLocationStore.InvalidateLocalMachineAsync(operationContext, LocalMachineId);
        }

        /// <inheritdoc />
        public void ReportReputation(MachineLocation location, MachineReputation reputation) =>
            MachineReputationTracker.ReportReputation(location, reputation);

        /// <inheritdoc />
        public Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except)
        {
            return LocalLocationStore.ClusterState.GetRandomMachineLocation(except);
        }

        /// <inheritdoc />
        public Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash)
        {
            return LocalLocationStore.ClusterState.GetDesignatedLocations(hash, includeExpired: false);
        }

        /// <inheritdoc />
        public bool IsMachineActive(MachineLocation machine)
        {
            return LocalLocationStore.ClusterState.TryResolveMachineId(machine, out var machineId)
                ? !LocalLocationStore.ClusterState.InactiveMachines.Contains(machineId)
                : false;
        }

        /// <inheritdoc />
        public bool CanComputeLru => true;

        /// <inheritdoc />
        public Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token, TimeSpan? minEffectiveAge = null)
        {
            return TrimBulkAsync(context, contentHashes, token, UrgencyHint.Nominal);
        }

        /// <inheritdoc />
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            return GetHashesInEvictionOrder(context, contentHashesWithInfo, reverse: false);
        }

        /// <summary>
        /// Computes content hashes with effective last access time sorted in LRU manner.
        /// </summary>
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo, bool reverse)
        {
            return LocalLocationStore.GetHashesInEvictionOrder(
                context,
                this,
                contentHashesWithInfo,
                reverse: reverse);
        }
    }
}
