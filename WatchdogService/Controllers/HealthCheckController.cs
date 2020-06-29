using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using WatchdogService.Models;

namespace WatchdogService.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class HealthCheckController : Controller
    {
        /// <summary>
        /// TelemetryService instance.
        /// </summary>
        private readonly HealthCheckOperations _operations = null;

        private readonly WatchdogService _service;
        /// <summary>
        /// HealthCheckController constructor.
        /// </summary>
        /// <param name="service">WatchdogService class instance.</param>
        public HealthCheckController(WatchdogService service)
        {
            this._operations = service.HealthCheckOperations;
            this._service = service;
        }

        #region Watchdog Health for Self Monitoring

        [HttpGet]
        public async Task<IActionResult> GetWatchdogHealth()
        {
            // Check that an operations class exists.
            if (null == this._operations)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            // Check that there are items being monitored.
            IList<HealthCheck> items = await this._operations.GetHealthChecksAsync();
            if (0 == items.Count)
            {
                return StatusCode((int)HttpStatusCode.NoContent);
            }

            // Return the status.
            return Ok(items);
        }

        #endregion

        #region Health Check Operations

        [HttpGet(@"{application?}/{service?}/{partition:guid?}")]
        public async Task<IActionResult> GetHealthCheck(string application = null, string service = null, Guid? partition = null)
        {
            try
            {
                ServiceEventSource.Current.ServiceRequestStart(nameof(this.GetHealthCheck));

                // Get the list of health check items.
                IList<HealthCheck> items = await this._operations.GetHealthChecksAsync(application, service, partition);
                ServiceEventSource.Current.ServiceRequestStop(nameof(this.GetHealthCheck));

                return Ok(items);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Exception(ex.Message, ex.GetType().Name, nameof(this.GetHealthCheck));
                return StatusCode((int)HttpStatusCode.InternalServerError, ex);
            }
        }

        [HttpPost]
        //[Route(@"")]
        public async Task<IActionResult> PostHealthCheck([FromBody] HealthCheck hcm)
        {
            // Check required parameters.
            if (hcm.Equals(HealthCheck.Default))
            {
                return StatusCode((int)HttpStatusCode.BadRequest);
            }
            if (null == this._operations)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }

            try
            {
                ServiceEventSource.Current.ServiceRequestStart(nameof(this.PostHealthCheck));

                // Call the operations class to add the health check.
                await this._operations.AddHealthCheckAsync(hcm);

                ServiceEventSource.Current.ServiceRequestStop(nameof(this.PostHealthCheck));
                return Ok("OK");
            }
            catch (ArgumentException ex)
            {
                ServiceEventSource.Current.Exception(ex.Message, ex.GetType().Name, nameof(this.PostHealthCheck));
                return StatusCode((int)HttpStatusCode.BadRequest, ex);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Exception(ex.Message, ex.GetType().Name, nameof(this.PostHealthCheck));
                return StatusCode((int)HttpStatusCode.InternalServerError, ex);
            }
        }

        #endregion
    }
}
