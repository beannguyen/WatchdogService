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
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using ExternalHealthCheck.Models;
using Microsoft.ServiceFabric.Data.Collections;
using ExternalHealthCheck.Helper;
using Comvita.Common.Helpers;

namespace ExternalHealthCheck
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class ExternalHealthCheck : StatefulService
    {
        private readonly string blobConnection = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["BlobConnect"]?
            .Parameters["ConnectionString"]?.Value;

        private readonly string blobTableName = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["BlobConnect"]?
            .Parameters["TableName"]?.Value;

        private readonly string blobPartitionkey = FabricRuntime.GetActivationContext()?
            .GetConfigurationPackageObject("Config")?
            .Settings.Sections["BlobConnect"]?
            .Parameters["PartitionKey"]?.Value;

        public ExternalHealthCheck(StatefulServiceContext context)
            : base(context)
        {

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
            bool isLoadedConfig = false;
            while (cancellationToken.IsCancellationRequested == false)
            {
                isLoadedConfig = await this.loadConfigFromBlob();
                if (isLoadedConfig)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                    await registerResourcesToWatchdog(cancellationToken);
                }
                await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);
            }
            // await base.RunAsync(cancellationToken);
        }

        private async Task<bool> loadConfigFromBlob()
        {
            try
            {
                string connectionString = blobConnection;
                string tableName = blobTableName;
                string partitionKey = blobPartitionkey;

                CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(connectionString);
                CloudTableClient tableClient = cloudStorageAccount.CreateCloudTableClient();
                CloudTable cloudTable = tableClient.GetTableReference(tableName);

                TableQuery<ExternalResource> query = new TableQuery<ExternalResource>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));
                var result = await cloudTable.ExecuteQuerySegmentedAsync(query, null);

                var listResources = result.ToList();

                IReliableDictionary<string, ExternalResource> rsDict = await StateManagerHelper.GetExternalResourceDictAsync(this.StateManager);

                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    for (int i = 0; i < listResources.Count; i++)
                    {
                        var item = listResources[i];
                        var exists = await rsDict.TryGetValueAsync(tx, item.RowKey);

                        if (exists.HasValue)
                        {
                            if (false == exists.Value.ETag.Equals(item.ETag))
                            {
                                item.IsUpdate = true;
                            }
                            else
                            {
                                item.IsUpdate = false;
                            }
                        }
                        else
                        {
                            item.IsUpdate = true;
                        }

                        await rsDict.AddOrUpdateAsync(tx, item.RowKey, item, (k, v) =>
                        {
                            return item;
                        }, TimeSpan.FromSeconds(5), CancellationToken.None);

                    }

                    await tx.CommitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Exception("Load resource from blob failed", ex.Message, ex.StackTrace);
                return false;
            }

            return true;
        }

        private async Task<bool> registerResourcesToWatchdog(CancellationToken cancellationToken)
        {
            try
            {
                var listResources = await StateManagerHelper.GetExternalResourcesAsync(this.StateManager);
                var updateResources = listResources.Where(p => p.IsUpdate).ToList();
                List<Task> listTaskRegister = new List<Task>();

                for (int i = 0; i < updateResources.Count; i++)
                {
                    var rs = listResources[i];
                    listTaskRegister.Add(new Task(async () =>
                    {
                        IReliableDictionary<string, ExternalResource> rsDict = await StateManagerHelper.GetExternalResourceDictAsync(this.StateManager);
                        try
                        {
                            await WatchdogHelper.RegisterHealthCheckAsync(rs.RowKey, this.Context.ServiceName, this.Context.PartitionId, rs.Frequency,
                                                                            $"api/health/{rs.RowKey}", cancellationToken);
                            rs.IsUpdate = false;
                            using (ITransaction tx = this.StateManager.CreateTransaction())
                            {
                                await rsDict.AddOrUpdateAsync(tx, rs.RowKey, rs, (k, v) =>
                                {
                                    return rs;
                                }, TimeSpan.FromSeconds(5), CancellationToken.None);

                                await tx.CommitAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            ServiceEventSource.Current.Exception($"Resource {rs.RowKey} register failed", ex.Message, ex.StackTrace);
                        }
                    }));

                }

                listTaskRegister.ForEach(task =>
                {
                    task.Start();
                });

                Task.WaitAll(listTaskRegister.ToArray());

                return true;
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Exception("Register resources failed", ex.Message, ex.StackTrace);
                return false;
            }
        }
    }
}
