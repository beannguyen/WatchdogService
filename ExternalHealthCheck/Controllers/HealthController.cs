using Comvita.Common.Azure.Utilities;
using ExternalHealthCheck.Handler;
using ExternalHealthCheck.Helper;
using ExternalHealthCheck.Models;
using FluentFTP;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Controllers
{
    [Route("api/[controller]")]
    public class HealthController : Controller
    {
        private readonly IReliableStateManager _stateManager;
        public HealthController(IReliableStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        [HttpGet("{name}")]
        public async Task<IActionResult> Get(string name)
        {

            List<ExternalResource> listResources = await StateManagerHelper.GetExternalResourcesAsync(_stateManager);

            var extRes = listResources.Where(p => p.RowKey == name).FirstOrDefault();

            if (extRes != null)
            {
                bool isSuccess = false;
                try
                {
                    isSuccess = await checkResourceHealth(extRes);
                    if (isSuccess)
                    {
                        return Ok();
                    }
                    else
                    {
                        return StatusCode(500);
                    }
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.Message("Error: {0} - {1}", ex.Message, ex.StackTrace);
                    return StatusCode(500, ex);
                }
            }
            else
            {
                return StatusCode(404);
            }

        }

        private async Task<bool> checkResourceHealth(ExternalResource externalResource)
        {
            if (string.IsNullOrEmpty(externalResource.Type))
            {
                throw new ArgumentNullException("Resource type is required");
            }

            var type = externalResource.Type.ToLower();
            switch (type)
            {
                case "ftp":
                case "ftps":
                    return await FtpConnectHandler.Check(externalResource);
                case "sftp":
                    return await SFtpConnectHandler.Check(externalResource);
                case "api":
                    return await ApiConnectHandler.Check(externalResource);
                case "soap":
                    return await SoapConnectHandler.Check(externalResource);
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
