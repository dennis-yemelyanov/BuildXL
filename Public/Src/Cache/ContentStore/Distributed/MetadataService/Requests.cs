// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public enum RpcMethodId
    {
        None,
        GetContentLocations,
        RegisterContentLocations,
        PutBlob, // WARNING: deprecated
        GetBlob, // WARNING: deprecated
        CompareExchange,
        GetLevelSelectors,
        GetContentHashList,
    }

    public static class ServiceRequestExtensions
    {
        public class ClientMustRetryException : Exception
        {
            public ServiceResponseBase Response { get; }

            public ClientMustRetryException(ServiceResponseBase response)
                : base($"Target machine indicated that retry is needed. Reason=[{response.RetryReason}] ErrorMessage=[{response.ErrorMessage}]")
            {
                Contract.Requires(response is not null);
                Response = response;
            }
        }

        public static Result<TResult> ToResult<TResponse, TResult>(this TResponse response, Func<TResponse, TResult> select, bool isNullAllowed = false)
            where TResponse : ServiceResponseBase
        {
            return ToCustomResult(response, r => Result.Success(select(r), isNullAllowed));
        }

        public static TResult ToCustomResult<TResponse, TResult>(this TResponse response, Func<TResponse, TResult> select)
            where TResponse : ServiceResponseBase
            where TResult : ResultBase
        {
            if (response.ShouldRetry || response.RetryReason != RetryReason.Invalid)
            {
                return new ErrorResult(
                    exception: new ClientMustRetryException(response),
                    message: response.ErrorMessage).AsResult<TResult>();
            }
            else if (response.Succeeded)
            {
                return select(response);
            }
            else
            {
                return new ErrorResult(response.ErrorMessage, response.Diagnostics).AsResult<TResult>();
            }
        }

        public static BoolResult ToBoolResult(this ServiceResponseBase response)
        {
            return ToCustomResult(response, r => BoolResult.Success);
        }
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsRequest))]
    [ProtoInclude(11, typeof(RegisterContentLocationsRequest))]
    // WARNING: 12 amd 13 are deprecated, DO NOT USE
    [ProtoInclude(14, typeof(GetContentHashListRequest))]
    [ProtoInclude(15, typeof(CompareExchangeRequest))]
    [ProtoInclude(16, typeof(GetLevelSelectorsRequest))]
    // WARNING: 17 and 18 are reserved for past backwards-compatibility
    public abstract record ServiceRequestBase
    {
        public virtual RpcMethodId MethodId => RpcMethodId.None;

        [ProtoMember(1)]
        public string ContextId { get; set; }

        public bool Replaying { get; set; }

        public BlockReference? BlockId { get; set; }
    }

    public enum RetryReason
    {
        Invalid = 0,

        ShutdownStarted = 1,
        WorkerMode = 2,
        StaleHeartbeat = 3,
        MissingCheckpoint = 4,
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsResponse))]
    [ProtoInclude(11, typeof(RegisterContentLocationsResponse))]
    // WARNING: 12 amd 13 are deprecated, DO NOT USE
    [ProtoInclude(14, typeof(GetContentHashListResponse))]
    [ProtoInclude(15, typeof(CompareExchangeResponse))]
    [ProtoInclude(16, typeof(GetLevelSelectorsResponse))]
    // WARNING: 17 and 18 are reserved for past backwards-compatibility
    public record ServiceResponseBase
    {
        public virtual RpcMethodId MethodId => RpcMethodId.None;

        public bool Succeeded => ErrorMessage == null;

        [ProtoMember(1)]
        public string ErrorMessage { get; set; }

        [ProtoMember(2)]
        public string Diagnostics { get; set; }

        [ProtoMember(3)]
        public bool ShouldRetry { get; set; }

        [ProtoMember(4)]
        public RetryReason RetryReason { get; set; } = RetryReason.Invalid;

        public bool PersistRequest { get; init; }
    }

    [ProtoContract]
    public record GetContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHash> Hashes { get; init; } = new List<ShortHash>();
    }

    [ProtoContract]
    public record GetContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ContentLocationEntry> Entries { get; init; } = new List<ContentLocationEntry>();
    }

    [ProtoContract]
    public record RegisterContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHashWithSize> Hashes { get; init; } = new List<ShortHashWithSize>();

        [ProtoMember(2)]
        public MachineId MachineId { get; init; }
    }

    [ProtoContract]
    public record RegisterContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;
    }

    [ProtoContract]
    public record CompareExchangeRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.CompareExchange;

        [ProtoMember(1)]
        public StrongFingerprint StrongFingerprint { get; init; }

        [ProtoMember(2)]
        public SerializedMetadataEntry Replacement { get; init; }

        [ProtoMember(3)]
        public string ExpectedReplacementToken { get; init; }

        public override string ToString()
        {
            return $"{StrongFingerprint} Replacement=[{Replacement}] ExpectedReplacementToken=[{ExpectedReplacementToken}]";
        }
    }

    [ProtoContract]
    public record CompareExchangeResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.CompareExchange;

        [ProtoMember(1)]
        public bool Exchanged { get; init; }

        public override string ToString()
        {
            return $"Exchanged=[{Exchanged}]";
        }
    }

    [ProtoContract]
    public record GetLevelSelectorsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetLevelSelectors;

        [ProtoMember(1)]
        public Fingerprint WeakFingerprint { get; init; }

        [ProtoMember(2)]
        public int Level { get; init; }
    }

    [ProtoContract]
    public record GetLevelSelectorsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetLevelSelectors;

        [ProtoMember(1)]
        public IReadOnlyList<Selector> Selectors { get; init; } = new List<Selector>();

        [ProtoMember(2)]
        public bool HasMore { get; init; }
    }

    [ProtoContract]
    public record GetContentHashListRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentHashList;

        [ProtoMember(1)]
        public StrongFingerprint StrongFingerprint { get; init; }
    }

    [ProtoContract]
    public record GetContentHashListResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentHashList;

        [ProtoMember(1)]
        public SerializedMetadataEntry MetadataEntry { get; init; }
    }

    [ProtoContract]
    public class SerializedMetadataEntry
    {
        [ProtoMember(1)]
        public byte[] Data { get; set; }

        [ProtoMember(2)]
        public string ReplacementToken { get; set; }

        [ProtoMember(3)]
        public long? SequenceNumber { get; set; }

        /// <summary>
        /// This field is set when data is stored externally
        /// </summary>
        [ProtoMember(4)]
        public string ExternalDataStorageId { get; set; }

        public override string ToString()
        {
            return $"ReplacementToken=[{ReplacementToken}] SequenceNumber=[{SequenceNumber}]";
        }
    }

    [ProtoContract]
    public class ClusterMachineInfo
    {
        [ProtoMember(1)]
        public MachineId MachineId { get; set; }

        [ProtoMember(2)]
        public MachineLocation Location { get; init; }

        [ProtoMember(4)]
        public string Name { get; set; }

        public override string ToString()
        {
            return $"Id=[{MachineId}] Name=[{Name}] Location=[{Location}]";
        }
    }
}
