﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App;
using BuildXL.Cache.Monitor.App.Notifications;
using BuildXL.Cache.Monitor.App.Rules;
using BuildXL.Cache.Monitor.App.Scheduling;
using BuildXL.Cache.Monitor.Library.IcM;

namespace BuildXL.Cache.Monitor.Library.Rules
{
    internal abstract class SingleStampRuleBase : KustoRuleBase
    {
        private readonly SingleStampRuleConfiguration _configuration;

        /// <summary>
        /// Identifier corresponding to a specific rule and stamp
        /// </summary>
        public abstract override string Identifier { get; }

        public SingleStampRuleBase(SingleStampRuleConfiguration configuration)
            : base(configuration)
        {
            _configuration = configuration;
        }

        protected void Emit(RuleContext context, string bucket, Severity severity, string message, string? summary = null, DateTime? eventTimeUtc = null)
        {
            var now = _configuration.Clock.UtcNow;
            _configuration.Logger.Log(severity, $"[{Identifier}] {message}");
            _configuration.Notifier.Emit(new Notification(
                Identifier,
                context.RunGuid,
                context.RunTimeUtc,
                now,
                eventTimeUtc ?? now,
                bucket,
                severity,
                _configuration.Environment,
                _configuration.Stamp,
                message,
                summary ?? message));
        }

        protected Task EmitIcmAsync(
            int severity,
            string title,
            IEnumerable<string>? machines,
            IEnumerable<string>? correlationIds,
            string? description = null,
            DateTime? eventTimeUtc = null,
            TimeSpan? cacheTimeToLive = null)
        {
            // Do not create Sev3 or higher incidents for non-production environments
            if (!_configuration.Environment.IsProduction())
            {
                severity = Math.Max(severity, 4);
            }

            title = string.Concat($"[{_configuration.Environment}/{_configuration.Stamp}] ", title);

            var incident = new IcmIncident(_configuration.Stamp, _configuration.Environment.ToString(), machines, correlationIds, severity, description ?? title, title, eventTimeUtc, cacheTimeToLive);
            return _configuration.IcmClient.EmitIncidentAsync(incident);
        }
    }
}
