using Autofac;
using Comvita.Common.Logger;
using EventFlow;
using EventFlow.Autofac.Extensions;
using Microsoft.ApplicationInsights;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Diagnostics;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace WatchdogService
{
    internal static class Program
    {
        private static readonly string Environment = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["Environment"]?
            .Parameters["EnvironmentName"]?.Value;

        private static readonly string AppInsKey = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["ApplicationInsight"]?
            .Parameters["InstrumentKey"]?.Value;

        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                var builder = new ContainerBuilder();

                var telemetry = new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration(AppInsKey));
                builder.RegisterInstance(telemetry).As<TelemetryClient>();

                builder.RegisterType<TelemetryLogerAppIns>().As<ITelemetryLogger>();

                var container = EventFlowOptions.New.UseAutofacContainerBuilder(builder).CreateContainer(false);

                // The ServiceManifest.XML file defines one or more service type names.
                // Registering a service maps a service type name to a .NET type.
                // When Service Fabric creates an instance of this service type,
                // an instance of the class is created in this host process.
                using (container)
                {
                    ServiceRuntime.RegisterServiceAsync("WatchdogServiceType",
                        context => new WatchdogService(context, container.Resolve<ITelemetryLogger>())).GetAwaiter().GetResult();

                    ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(WatchdogService).Name);

                    // Prevents this host process from terminating so services keeps running. 
                    Thread.Sleep(Timeout.Infinite);
                }
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}
