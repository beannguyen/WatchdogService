using Comvita.Common.Azure.Utilities;
using System.Fabric;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Helper
{
    public class KeyvaultHelper
    {
        private static string _endpoint;
        protected static string Endpoint
        {
            get
            {
                if (string.IsNullOrEmpty(_endpoint))
                {
                    _endpoint = FabricRuntime.GetActivationContext()?
                        .GetConfigurationPackageObject("Config")?
                        .Settings.Sections["Keyvault"]?
                        .Parameters["Endpoint"]?.Value;
                }
                return _endpoint;
            }
        }

        private static string _clientId;
        protected static string ClientId
        {
            get
            {
                if (string.IsNullOrEmpty(_clientId))
                {
                    _clientId = FabricRuntime.GetActivationContext()?
                       .GetConfigurationPackageObject("Config")?
                       .Settings.Sections["Keyvault"]?
                       .Parameters["ClientId"]?.Value;
                }
                return _clientId;
            }
        }

        private static string _clientSecrect;

        protected static string ClientSecrect
        {
            get
            {
                if (string.IsNullOrEmpty(_clientSecrect))
                {
                    _clientSecrect = FabricRuntime.GetActivationContext()?
                       .GetConfigurationPackageObject("Config")?
                       .Settings.Sections["Keyvault"]?
                       .Parameters["ClientSecrect"]?.Value;
                }

                return _clientSecrect;
            }
        }

        public static async Task<string> Get(string secretName)
        {
            ConfigKeyVault.SetUpKeyVault(Endpoint, ClientId, ClientSecrect);
            var kv = new ConfigKeyVault();
            return await kv.GetSecureSecret(secretName);
        }
    }
}
