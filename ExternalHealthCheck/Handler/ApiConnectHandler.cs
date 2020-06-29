using ExternalHealthCheck.Helper;
using ExternalHealthCheck.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Handler
{
    public class ApiConnectHandler
    {
        public static async Task<bool> Check(ExternalResource externalResource)
        {
            var authType = externalResource.AuthenType?.ToLower();
            switch (authType)
            {
                case "oauth2":
                    return await oauth2Connect(externalResource);
                case "basic":
                    return await basicAuthConnect(externalResource);
                default:
                    return await connect(externalResource);
            }
        }

        private static async Task<bool> oauth2Connect(ExternalResource rs)
        {
            string clientSecrect = await KeyvaultHelper.Get(rs.Keyvault);
            string accessTokenKey = (rs.CustAccessTokenField != null) ? rs.CustAccessTokenField : "access_token";

            var pairs = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>( "grant_type", "client_credentials" ),
                            new KeyValuePair<string, string>( "client_id", rs.Username),
                            new KeyValuePair<string, string> ( "client_secret", clientSecrect),
                            new KeyValuePair<string, string> ( "scope", rs.Scope)
                        };
            var content = new FormUrlEncodedContent(pairs);
            string token = string.Empty;
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(rs.IdentityEndpoint, content);
                var resContent = await response.Content.ReadAsStringAsync();
                JObject jobj = JObject.Parse(resContent);
                token = (string)jobj[accessTokenKey];
            }

            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("Cannot get a token");
            }

            var uriBuilder = new UriBuilder($"{rs.Host}:{rs.Port}{rs.Path}");
            var uri = uriBuilder.Uri;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var result = await client.GetAsync(uri);
                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> basicAuthConnect(ExternalResource rs)
        {
            var isSuccess = false;
            string passwd = await KeyvaultHelper.Get(rs.Keyvault);
            var uriBuilder = new UriBuilder($"{rs.Host}:{rs.Port}{rs.Path}");
            var uri = uriBuilder.Uri;

            using (var client = new HttpClient())
            {
                var byteArray = Encoding.ASCII.GetBytes($"{rs.Username}:{passwd}");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

                var result = await client.GetAsync(uri);
                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
            }

            return isSuccess;
        }

        private static async Task<bool> connect(ExternalResource rs)
        {
            var isSuccess = false;

            var uriBuilder = new UriBuilder($"{rs.Host}:{rs.Port}{rs.Path}");
            var uri = uriBuilder.Uri;

            using (var client = new HttpClient())
            {
                var result = await client.GetAsync(uri);
                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
            }

            return isSuccess;
        }
    }
}
