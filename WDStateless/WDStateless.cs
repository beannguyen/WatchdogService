using Comvita.Common.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WDStateless
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class WDStateless : StatelessService
    {
        public WDStateless(StatelessServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatelessServiceContext>(serviceContext))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // Register the health check and metrics with the watchdog.
            bool healthRegistered = await this.RegisterHealthCheckAsync(cancellationToken);
            bool healthActorRegistered = await RegisterHealCheckActorAsync(cancellationToken);

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                if (false == healthRegistered)
                {
                    healthRegistered = await this.RegisterHealthCheckAsync(cancellationToken);
                }
                if (false == healthActorRegistered)
                {
                    healthActorRegistered = await RegisterHealCheckActorAsync(cancellationToken);
                }
            }
            //await base.RunAsync(cancellationToken);
        }

        private async Task<bool> RegisterHealthCheckAsync(CancellationToken cancellationToken)
        {
            return await WatchdogHelper.RegisterHealthCheckAsync("WDStateless", this.Context.ServiceName, this.Context.PartitionId, 2,
                "api/values", cancellationToken);
        }

        private async Task<bool> RegisterHealCheckActorAsync(CancellationToken cancellationToken)
        {
            return await WatchdogHelper.RegisterHealthCheckAsync("WDActor", this.Context.ServiceName, this.Context.PartitionId, 2,
                "api/ActorHealth/abc", cancellationToken);

            //bool result = false;
            //HttpClient client = new HttpClient();
            //string jsonTemplate =
            //    "{{\"name\":\"WDActor\",\"serviceName\": \"{0}\",\"partition\": \"{1}\",\"frequency\": \"{2}\",\"suffixPath\": \"api/ActorHealth/abc\",\"method\": {{ \"Method\": \"GET\" }}, \"expectedDuration\": \"00:00:00.2000000\",\"maximumDuration\": \"00:00:05\" }}";
            //string json = string.Format(jsonTemplate, this.Context.ServiceName, this.Context.PartitionId, TimeSpan.FromMinutes(2));

            //// Called from RunAsync, don't let an exception out so the service will start, but log the exception because the service won't work.
            //try
            //{
            //    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:19081/Watchdog/WatchdogService/api/healthcheck");
            //    request.Content = new StringContent(json, Encoding.Default, "application/json");

            //    HttpResponseMessage msg = await client.SendAsync(request);

            //    // Log a success or error message based on the returned status code.
            //    if (HttpStatusCode.OK == msg.StatusCode)
            //    {
            //        ServiceEventSource.Current.Message(string.Format("{0} - {1}", nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode)));
            //        //ServiceEventSource.Current.Trace(nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode));
            //        result = true;
            //    }
            //    else
            //    {
            //        ServiceEventSource.Current.Message(string.Format("Error: {0} - {1}", nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode)));
            //        //ServiceEventSource.Current.Error(nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode));
            //        //ServiceEventSource.Current.Trace(nameof(this.RegisterHealthCheckAsync), json ?? "<null JSON>");
            //    }
            //}
            //catch (Exception ex)
            //{
            //    ServiceEventSource.Current.Message(string.Format("Exception: {0} ", ex.Message));
            //    //ServiceEventSource.Current.Error($"Exception: {ex.Message} at {ex.StackTrace}.");
            //}

            //return result;
        }
    }
}
