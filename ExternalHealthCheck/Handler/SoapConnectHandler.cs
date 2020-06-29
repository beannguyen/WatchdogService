using ExternalHealthCheck.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ExternalHealthCheck.Handler
{
    public class SoapConnectHandler
    {
        public static async Task<bool> Check(ExternalResource externalResource)
        {
            bool isSuccess = false;
            var uriBuilder = new UriBuilder($"{externalResource.Host}:{externalResource.Port}{externalResource.Path}");
            var uri = uriBuilder.Uri;

            using (var client = new HttpClient(new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip }))
            {
                var request = new HttpRequestMessage()
                {
                    RequestUri = uri,
                    Method = HttpMethod.Post
                };

                request.Content = new StringContent(externalResource.Payload, Encoding.UTF8, "text/xml");

                request.Headers.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                request.Headers.Add("SOAPAction", "");

                HttpResponseMessage response = client.SendAsync(request).Result;

                isSuccess = true;
            }

            return isSuccess;
        }
    }
}
