﻿// Copyright 2014 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

#if NET45
using Microsoft.ServiceBus.Messaging;
#else
using Microsoft.Azure.EventHubs;
#endif

namespace Serilog.Sinks.AzureEventHub
{
#if NET45
    using System.Transactions;
#endif

    /// <summary>
    /// Writes log events to an Azure Event Hub.
    /// </summary>
    public class AzureEventHubSink : ILogEventSink
    {
        private readonly EventHubClient _eventHubClient;
        private readonly string _applicationName;
        private readonly ITextFormatter _formatter;
        private readonly Action<EventData, LogEvent> _eventDataAction;
#if NET45
        private readonly int? _compressionTreshold;
#endif

        /// <summary>
        /// Construct a sink that saves log events to the specified EventHubClient.
        /// </summary>
        /// <param name="eventHubClient">The EventHubClient to use in this sink.</param>
        /// <param name="applicationName">The name of the application associated with the logs.</param>
        /// <param name="formatter">Provides formatting for outputting log data</param>
        /// <param name="eventDataAction">An optional action for setting extra properties on each EventData.</param>
#if NET45
        /// <param name="compressionTreshold">An optional setting to configure when to start compressing messages with gzip. Specified in bytes</param>
#endif
        public AzureEventHubSink(
            EventHubClient eventHubClient,
            string applicationName,
            ITextFormatter formatter = null,
            Action<EventData, LogEvent> eventDataAction = null
#if NET45
            ,int? compressionTreshold = null
#endif
            )
        {
            _eventHubClient = eventHubClient;
            _applicationName = applicationName;
            _formatter = formatter ?? new ScalarValueTypeSuffixJsonFormatter(renderMessage: true);
            _eventDataAction = eventDataAction;
#if NET45
            _compressionTreshold = compressionTreshold;
#endif
        }

        /// <summary>
        /// Emit the provided log event to the sink.
        /// </summary>
        /// <param name="logEvent">The log event to write.</param>
        public void Emit(LogEvent logEvent)
        {
            byte[] body;
            using (var render = new StringWriter())
            {
                _formatter.Format(logEvent, render);
                body = Encoding.UTF8.GetBytes(render.ToString());
            }

            var eventHubData = new EventData(body)
            {
#if NET45
                PartitionKey = Guid.NewGuid().ToString()
#endif
            };
            _eventDataAction?.Invoke(eventHubData, logEvent);
            if (!string.IsNullOrWhiteSpace(_applicationName) && !eventHubData.Properties.ContainsKey("Type"))
            {
                eventHubData.Properties.Add("Type", _applicationName);
            }

#if NET45

            if (_compressionTreshold != null && eventHubData.SerializedSizeInBytes > _compressionTreshold)
                eventHubData = eventHubData.AsCompressed();

            using (var transaction = new TransactionScope(TransactionScopeOption.Suppress))
            {
                _eventHubClient.Send(eventHubData);
            }
#else
            _eventHubClient.SendAsync(eventHubData).Wait();
#endif
        }
    }
}
