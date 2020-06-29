using Microsoft.WindowsAzure.Storage.Table;

namespace ExternalHealthCheck.Models
{
    public class ExternalResource : TableEntity
    {
        public int Frequency { get; set; }

        public string Host { get; set; }

        public bool IsInternal { get; set; }

        public string Path { get; set; }

        public int Port { get; set; }

        public string Type { get; set; }

        public string Keyvault { get; set; }

        public string Username { get; set; }

        public string Payload { get; set; }

        public string AuthenType { get; set; }

        public string CustAccessTokenField { get; set; }

        public string IdentityEndpoint { get; set; }

        public string Scope { get; set; }

        public bool IsUpdate { get; set; }
    }
}
