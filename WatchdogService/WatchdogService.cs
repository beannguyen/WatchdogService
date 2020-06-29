using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Data;
using System.Fabric.Description;
using System.Net.Http;
using WatchdogService.Models;
using System.Net;
using System.Text;
using System.Fabric.Health;
using Comvita.Common.Logger;
using System.Security.Cryptography.X509Certificates;

namespace WatchdogService
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public sealed class WatchdogService : StatefulService
    {
        /// <summary>
        /// Service Fabric client instance.
        /// </summary>
        private static FabricClient _client = null;

        /// <summary>
        /// Constant values. The metrics names must match the values in the ServiceManifest.
        /// </summary>
        private const string ObservedMetricCountMetricName = "ObservedMetricCount";

        private const string HealthCheckCountMetricName = "HealthCheckCount";
        private const string WatchdogConfigSectionName = "Watchdog";

        private static readonly string clientThumbCert = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["Certificate"]?
            .Parameters["ClientThumbCert"]?.Value;

        private static readonly string certCommonName = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["Certificate"]?
            .Parameters["CertCommonName"]?.Value;

        private ConfigurationSettings _settings = null;

        /// <summary>
        /// Health report interval. Can be changed based on configuration.
        /// </summary>
        private TimeSpan HealthReportInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// HealthCheckController operations class instance.
        /// </summary>
        private HealthCheckOperations _healthCheckOperations = null;
        private MetricsOperations _metricsOperations = null;
        private CleanupOperations _cleanupOperations;

        /// <summary>
        /// Configuration settings.
        /// </summary>
        public ConfigurationSettings Settings => this._settings;

        /// <summary>
        /// Service Fabric client instance with user level privileges.
        /// </summary>
        public FabricClient Client => _client;

        /// <summary>
        /// Gets the read status of the partition.
        /// </summary>
        public PartitionAccessStatus ReadStatus => this.Partition.ReadStatus;

        /// <summary>
        /// Gets the write status of the partition.
        /// </summary>
        public PartitionAccessStatus WriteStatus => this.Partition.WriteStatus;

        /// <summary>
        /// HealthCheckController operations class instance.
        /// </summary>
        public HealthCheckOperations HealthCheckOperations => this._healthCheckOperations;

        /// <summary>
        /// MetricsController operations class instance.
        /// </summary>
        public MetricsOperations MetricsOperations => this._metricsOperations;

        private readonly ITelemetryLogger _logger;
        /// <summary>
        /// Static WatchdogService constructor.
        /// </summary>
        static WatchdogService()
        {
            _client = CreateFabricClient();
        }

        public WatchdogService(StatefulServiceContext context, ITelemetryLogger logger)
            : base(context)
        {
            _logger = logger;
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<WatchdogService>(this)
                                            .AddSingleton<IReliableStateManager>(this.StateManager))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // Create the operations classes.
            // this._telemetry = new AiTelemetry(this.GetConfigValueAsString(WatchdogConfigSectionName, "AIKey"));
            this._healthCheckOperations = new HealthCheckOperations(
                this,
                 this._logger,
                this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "HealthCheckInterval", TimeSpan.FromMinutes(5)),
                cancellationToken);

            this._metricsOperations = new MetricsOperations(
                this,
                // this._telemetry,
                this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "MetricInterval", TimeSpan.FromMinutes(5)),
                cancellationToken);

            this._cleanupOperations = new CleanupOperations(TimeSpan.FromMinutes(2), cancellationToken)
            {
                Endpoint = this.GetConfigValueAsString(WatchdogConfigSectionName, "DiagnosticEndpoint"),
                SasToken = this.GetConfigValueAsString(WatchdogConfigSectionName, "DiagnosticSasToken"),
                TimeToKeep = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "DiagnosticTimeToKeep", TimeSpan.FromDays(10)),
                TimerInterval = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "DiagnosticInterval", TimeSpan.FromMinutes(2))
            };

            // Register the watchdog health check.
            //await this.RegisterHealthCheckAsync(cancellationToken).ConfigureAwait(false);

            // Loop waiting for cancellation.
            while (false == cancellationToken.IsCancellationRequested)
            {
                // Report the health and metrics of the watchdog to Service Fabric.
                this.ReportWatchdogHealth();
                //await this.ReportWatchdogMetricsAsync(cancellationToken);
                //await this.ReportClusterHealthAsync(cancellationToken);

                // Delay up to the time for the next health report.
                await Task.Delay(this.HealthReportInterval, cancellationToken);
            }

            await base.RunAsync(cancellationToken);
        }

        protected override Task OnOpenAsync(ReplicaOpenMode openMode, CancellationToken cancellationToken)
        {
            // Get the configuration settings and monitor for changes.
            this.Context.CodePackageActivationContext.ConfigurationPackageModifiedEvent += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;

            ConfigurationPackage configPackage = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config");
            if (null != configPackage)
            {
                Interlocked.Exchange(ref this._settings, configPackage.Settings);
            }
            return base.OnOpenAsync(openMode, cancellationToken);
        }

        /// <summary>
        /// Called when a configuration package is modified.
        /// </summary>
        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            if ("Config" == e.NewPackage.Description.Name)
            {
                Interlocked.Exchange<ConfigurationSettings>(ref this._settings, e.NewPackage.Settings);

                //// Update the configured values.
                //if (null != this._telemetry)
                //{
                //    this._telemetry.Key = this.Settings.Sections[WatchdogConfigSectionName].Parameters["AIKey"].Value;
                //}

                this.HealthReportInterval = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "WatchdogHealthReportInterval", TimeSpan.FromSeconds(60));

                this._healthCheckOperations.TimerInterval = this.GetConfigValueAsTimeSpan(
                    WatchdogConfigSectionName,
                    "HealthCheckInterval",
                    TimeSpan.FromMinutes(5));
                this._metricsOperations.TimerInterval = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "MetricInterval", TimeSpan.FromMinutes(5));
                this._cleanupOperations.Endpoint = this.GetConfigValueAsString(WatchdogConfigSectionName, "DiagnosticEndpoint");
                this._cleanupOperations.SasToken = this.GetConfigValueAsString(WatchdogConfigSectionName, "DiagnosticSasToken");
                this._cleanupOperations.TimerInterval = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "DiagnosticInterval", TimeSpan.FromMinutes(2));
                this._cleanupOperations.TimeToKeep = this.GetConfigValueAsTimeSpan(WatchdogConfigSectionName, "DiagnosticTimeToKeep", TimeSpan.FromDays(10));
                this._cleanupOperations.TargetCount = this.GetConfigValueAsInteger(WatchdogConfigSectionName, "DiagnosticTargetCount", 8000);
            }
        }

        /// <summary>
        /// Refreshes the FabricClient instance.
        /// </summary>
        public void RefreshFabricClient()
        {
            FabricClient old = Interlocked.CompareExchange<FabricClient>(ref _client, CreateFabricClient(), _client);
            old?.Dispose();
        }

        /// <summary>
        /// Checks that the service has been initialized correctly and is health by all internal metrics.
        /// </summary>
        /// <param name="description">StringBuilder instance containing the description to return.</param>
        /// <returns>Reported health state and any errors encountered added to the StringBuilder instance.</returns>
        public HealthState CheckWatchdogHealth(StringBuilder description)
        {
            HealthState current = HealthState.Ok;
            if (null == ServiceEventSource.Current)
            {
                current = this.CompareHealthState(current, HealthState.Error);
                description.AppendLine("ServiceEventSource is null.");
            }

            if (null == this._healthCheckOperations)
            {
                current = this.CompareHealthState(current, HealthState.Error);
                description.AppendLine("HealthCheckOperations is null.");
            }

            if (null == this._metricsOperations)
            {
                current = this.CompareHealthState(current, HealthState.Error);
                description.AppendLine("MetricOperations is null.");
            }

            // Check the number of endpoints listening.
            //if (0 == this.Context.CodePackageActivationContext.GetEndpoints().Count)
            //{
            //    current = this.CompareHealthState(current, HealthState.Error);
            //    description.AppendLine("Endpoints listening is zero.");
            //}

            return current;
        }

        /// <summary>
        /// Registers health checks with the watchdog service.
        /// </summary>
        /// <param name="token">Cancellation token to monitor for cancellation requests.</param>
        /// <returns>A Task that represents outstanding operation.</returns>
        //internal async Task RegisterHealthCheckAsync(CancellationToken token)
        //{
        //    HttpClient client = new HttpClient();

        //    // Called from RunAsync, don't let an exception out so the service will start, but log the exception because the service won't work.
        //    try
        //    {
        //        // Use the reverse proxy to locate the service endpoint.
        //        string postUrl = "http://localhost:19081/Watchdog/WatchdogService/healthcheck";
        //        HealthCheck hc = new HealthCheck("Watchdog Health Check", this.Context.ServiceName, this.Context.PartitionId, "watchdog/health");
        //        HttpResponseMessage msg = await client.PostAsJsonAsync(postUrl, hc);

        //        // Log a success or error message based on the returned status code.
        //        if (HttpStatusCode.OK == msg.StatusCode)
        //        {
        //            ServiceEventSource.Current.Trace(nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode));
        //        }
        //        else
        //        {
        //            ServiceEventSource.Current.Error(nameof(this.RegisterHealthCheckAsync), Enum.GetName(typeof(HttpStatusCode), msg.StatusCode));
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        ServiceEventSource.Current.Error($"Exception: {ex.Message} at {ex.StackTrace}.");
        //    }
        //}

        /// <summary>
        /// Reports the service health to Service Fabric.
        /// </summary>
        private void ReportWatchdogHealth()
        {
            // Called from RunAsync, don't let an exception out so the service will start, but log the exception because the service won't work.
            try
            {
                // Collect the health information from the local service state.
                TimeSpan interval = this.HealthReportInterval.Add(TimeSpan.FromSeconds(30));
                StringBuilder sb = new StringBuilder();
                HealthState hs = this.CheckWatchdogHealth(sb);

                // Issue a health report for the watchdog service.
                HealthInformation hi = new HealthInformation(this.Context.ServiceName.AbsoluteUri, "WatchdogServiceHealth", hs)
                {
                    TimeToLive = interval,
                    Description = sb.ToString(),
                    RemoveWhenExpired = false,
                    SequenceNumber = HealthInformation.AutoSequenceNumber,
                };
                this.Partition.ReportPartitionHealth(hi);

                hi = new HealthInformation(this.Context.ServiceName.AbsoluteUri, "HealthCheckOperations", this._healthCheckOperations.Health);
                hi.TimeToLive = interval;
                hi.RemoveWhenExpired = false;
                hi.SequenceNumber = HealthInformation.AutoSequenceNumber;
                this.Partition.ReportPartitionHealth(hi);

                hi = new HealthInformation(this.Context.ServiceName.AbsoluteUri, "MetricOperations", this._metricsOperations.Health);
                hi.TimeToLive = interval;
                hi.RemoveWhenExpired = false;
                hi.SequenceNumber = HealthInformation.AutoSequenceNumber;
                this.Partition.ReportPartitionHealth(hi);

                hi = new HealthInformation(this.Context.ServiceName.AbsoluteUri, "CleanupOperations", this._cleanupOperations.Health);
                hi.TimeToLive = interval;
                hi.RemoveWhenExpired = false;
                hi.SequenceNumber = HealthInformation.AutoSequenceNumber;
                this.Partition.ReportPartitionHealth(hi);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Error($"Exception: {ex.Message} at {ex.StackTrace}.");
            }
        }

        /// <summary>
        /// Reports the service load metrics to Service Fabric and the telemetry provider.
        /// </summary>
        /// <param name="cancellationToken"></param>
        private async Task ReportWatchdogMetricsAsync(CancellationToken token)
        {
            // Calculate the metric value.
            int omc = this._metricsOperations?.MetricCount ?? -1;
            int hcc = this._healthCheckOperations?.HealthCheckCount ?? -1;

            try
            {
                // Load the list of current metric values to report.
                List<LoadMetric> metrics = new List<LoadMetric>();
                metrics.Add(new LoadMetric(ObservedMetricCountMetricName, omc));
                metrics.Add(new LoadMetric(HealthCheckCountMetricName, hcc));

                // Report the metrics to Service Fabric.
                this.Partition.ReportLoad(metrics);

                //TODO:
                // Report them to the telemetry provider also.
                //await this._telemetry.ReportMetricAsync(ObservedMetricCountMetricName, omc, token);
                //await this._telemetry.ReportMetricAsync(HealthCheckCountMetricName, hcc, token);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Error($"Exception: {ex.Message} at {ex.StackTrace}.");
            }
        }

        /// <summary>
        /// Reports the overall cluster health to the telemetry provider.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReportClusterHealthAsync(CancellationToken cancellationToken)
        {
            // Called from RunAsync, don't let an exception out so the service will start, but log the exception because the service won't work.
            try
            {
                ClusterHealth health = await _client.HealthManager.GetClusterHealthAsync(TimeSpan.FromSeconds(4), cancellationToken);
                if (null != health)
                {
                    //TODO:
                    // Report the aggregated cluster health.
                    //await
                    //    this._telemetry.ReportHealthAsync(
                    //        this.Context.ServiceName.AbsoluteUri,
                    //        this.Context.PartitionId.ToString(),
                    //        this.Context.ReplicaOrInstanceId.ToString(),
                    //        "Cluster",
                    //        "Aggregated Cluster Health",
                    //        health.AggregatedHealthState,
                    //        cancellationToken);

                    // Get the state of each of the applications running within the cluster. Report anything that is unhealthy.
                    foreach (ApplicationHealthState appHealth in health.ApplicationHealthStates)
                    {
                        if (HealthState.Ok != appHealth.AggregatedHealthState)
                        {
                            //TODO:
                            //await
                            //    this._telemetry.ReportHealthAsync(
                            //        appHealth.ApplicationName.AbsoluteUri,
                            //        this.Context.ServiceName.AbsoluteUri,
                            //        this.Context.PartitionId.ToString(),
                            //        this.Context.ReplicaOrInstanceId.ToString(),
                            //        this.Context.NodeContext.NodeName,
                            //        appHealth.AggregatedHealthState,
                            //        cancellationToken);
                        }
                    }

                    // Get the state of each of the nodes running within the cluster.
                    foreach (NodeHealthState nodeHealth in health.NodeHealthStates)
                    {
                        if (HealthState.Ok != nodeHealth.AggregatedHealthState)
                        {
                            //TODO:
                            //await
                            //    this._telemetry.ReportHealthAsync(
                            //        this.Context.NodeContext.NodeName,
                            //        this.Context.ServiceName.AbsoluteUri,
                            //        this.Context.PartitionId.ToString(),
                            //        this.Context.NodeContext.NodeType,
                            //        this.Context.NodeContext.IPAddressOrFQDN,
                            //        nodeHealth.AggregatedHealthState,
                            //        cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Error($"Exception: {ex.Message} at {ex.StackTrace}.");
            }
        }

        /// <summary>
        /// Gets a configuration value or the specified default value.
        /// </summary>
        /// <param name="sectionName">Name of the section containing the parameter.</param>
        /// <param name="parameterName">Name of the parameter containing the value.</param>
        /// <param name="value">Default value.</param>
        /// <returns>Configuraiton value or default.</returns>
        private string GetConfigValueAsString(string sectionName, string parameterName, string value = null)
        {
            if (null != this.Settings)
            {
                ConfigurationSection section = this.Settings.Sections[sectionName];
                if (null != section)
                {
                    ConfigurationProperty parameter = section.Parameters[parameterName];
                    if (null != parameter)
                    {
                        value = parameter.Value;
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Gets a configuration value or the specified default value.
        /// </summary>
        /// <param name="sectionName">Name of the section containing the parameter.</param>
        /// <param name="parameterName">Name of the parameter containing the value.</param>
        /// <param name="value">Default value.</param>
        /// <returns>Configuraiton value or default.</returns>
        private TimeSpan GetConfigValueAsTimeSpan(string sectionName, string parameterName, TimeSpan value = default(TimeSpan))
        {
            if (null != this.Settings)
            {
                ConfigurationSection section = this.Settings.Sections[sectionName];
                if (null != section)
                {
                    ConfigurationProperty parameter = section.Parameters[parameterName];
                    if (null != parameter)
                    {
                        if (TimeSpan.TryParse(
                            parameter.Value,
                            out TimeSpan
                        val))
                        {
                            value = val;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Gets a configuration value or the specified default value.
        /// </summary>
        /// <param name="sectionName">Name of the section containing the parameter.</param>
        /// <param name="parameterName">Name of the parameter containing the value.</param>
        /// <param name="value">Default value.</param>
        /// <returns>Configuraiton value or default.</returns>
        private int GetConfigValueAsInteger(string sectionName, string parameterName, int value = 0)
        {
            if (null != this.Settings)
            {
                ConfigurationSection section = this.Settings.Sections[sectionName];
                if (null != section)
                {
                    ConfigurationProperty parameter = section.Parameters[parameterName];
                    if (null != parameter)
                    {
                        if (int.TryParse(
                            parameter.Value,
                            out int
                        val))
                        {
                            value = val;
                        }
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Compares the proposed health state with the current value and returns the least healthy.
        /// </summary>
        /// <param name="current">Current health state.</param>
        /// <param name="proposed">Proposed health state.</param>
        /// <returns>Selected health state value.</returns>
        private HealthState CompareHealthState(HealthState current, HealthState proposed)
        {
            if ((HealthState.Ok == current) && ((HealthState.Warning == proposed) || (HealthState.Error == proposed)))
            {
                return proposed;
            }
            if ((HealthState.Warning == current) && (HealthState.Error == proposed))
            {
                return proposed;
            }
            if ((HealthState.Invalid == current) || (HealthState.Unknown == current))
            {
                return proposed;
            }

            return current;
        }

        static FabricClient CreateFabricClient()
        {

            string thumb = clientThumbCert;
            string serverCertThumb = clientThumbCert;
            string CommonName = certCommonName;
            string connection = "localhost:19000";
            if (string.IsNullOrEmpty(thumb))
            {
                return new FabricClient(FabricClientRole.User);
            }
            else
            {
                X509Credentials xc = GetCredentials(thumb, serverCertThumb, CommonName);
                return new FabricClient(xc, connection);
            }
        }

        static X509Credentials GetCredentials(string clientCertThumb, string serverCertThumb, string name)
        {
            X509Credentials xc = new X509Credentials();
            xc.StoreLocation = StoreLocation.LocalMachine;
            xc.StoreName = "My";
            xc.FindType = X509FindType.FindByThumbprint;
            xc.FindValue = clientCertThumb;
            xc.RemoteCommonNames.Add(name);
            xc.RemoteCertThumbprints.Add(serverCertThumb);
            xc.ProtectionLevel = ProtectionLevel.EncryptAndSign;
            return xc;
        }
    }
}
