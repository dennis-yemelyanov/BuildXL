// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;

#nullable enable

namespace BuildXL.Processes.Containers
{
    /// <summary>
    /// Wraps a job object in a Helium container, where some paths can be virtualized using
    /// Bind and WCI filters
    /// </summary>
    public sealed class Container : JobObject
    {
        private readonly ContainerConfiguration m_containerConfiguration;
        private readonly LoggingContext m_loggingContext;

        /// <nodoc/>
        public Container(
            string? name,
            ContainerConfiguration containerConfiguration,
            LoggingContext? loggingContext)
            : base(name)
        {
            Contract.Requires(containerConfiguration.IsIsolationEnabled);
            m_containerConfiguration = containerConfiguration;

            // Logging context is null for some tests
            m_loggingContext = loggingContext ?? new LoggingContext("Test");
        }

        /// <summary>
        /// Starts the container, attaching it to the associated job object
        /// </summary>
        /// <exception cref="BuildXLException">If the container is not setup properly</exception>
        /// <remarks>
        /// This operation is detached from the actual construction of the Container since the container has to be started
        /// after calling <see cref="JobObject.SetLimitInformation"/>
        /// </remarks>
        public override void StartContainerIfPresent()
        {
            Native.Processes.ProcessUtilities.AttachContainerToJobObject(
                            handle,
                            m_containerConfiguration.RedirectedDirectories,
                            m_containerConfiguration.EnableWciFilter,
                            m_containerConfiguration.BindFltExcludedPaths?.Select(p => p.ToString()) ?? Enumerable.Empty<string>(),
                            m_containerConfiguration.BindFltFlags,
                            m_containerConfiguration.CustomJobObjectCustomization,
                            out var warnings);

            // Log any warnings when setting up the container (at this point this is just WCI retries)
            foreach (var warning in warnings)
            {
                Native.Tracing.Logger.Log.WarningSettingUpContainer(m_loggingContext, handle.ToString(), warning);
            }
        }

        /// <summary>
        /// Cleans up the container before releasing the base class handler
        /// </summary>
        protected override bool ReleaseHandle()
        {
            if (!Native.Processes.ProcessUtilities.TryCleanUpContainer(handle, m_containerConfiguration.CustomJobObjectCleanup, out var warnings))
            {
                foreach (var warning in warnings)
                {
                    // This is logged as a warning
                    Native.Tracing.Logger.Log.FailedToCleanUpContainer(m_loggingContext, handle.ToString(), warning);
                }
            }

            return base.ReleaseHandle();
        }
    }
}
