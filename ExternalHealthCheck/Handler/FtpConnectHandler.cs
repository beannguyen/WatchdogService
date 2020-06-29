using ExternalHealthCheck.Helper;
using ExternalHealthCheck.Models;
using FluentFTP;
using System;
using System.Net;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Handler
{
    public class FtpConnectHandler
    {
        public static async Task<bool> Check(ExternalResource externalResource)
        {
            var password = await KeyvaultHelper.Get(externalResource.Keyvault);

            FtpClient ftpClient = (externalResource.Type.Equals("ftps", StringComparison.OrdinalIgnoreCase) || externalResource.Port.Equals(990)) ?
                        new FtpClient(externalResource.Host)
                        {   //FTPS explicitly or Port = 990 => automatically create FTPS
                            Credentials = new NetworkCredential(externalResource.Username, password),
                            Port = externalResource.Port,
                            EnableThreadSafeDataConnections = true,
                            //DataConnectionType = config.Ftp.DataConnectionType.Equals(Constants.FtpActiveMode, StringComparison.InvariantCultureIgnoreCase) ? FtpDataConnectionType.AutoActive : FtpDataConnectionType.AutoPassive,
                            EncryptionMode = externalResource.Port.Equals(990) ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit
                        }
                        :
                        new FtpClient(externalResource.Host)
                        {
                            Credentials = new NetworkCredential(externalResource.Username, password),
                            Port = externalResource.Port,
                            EnableThreadSafeDataConnections = true
                        };

            ftpClient.ValidateCertificate += (control, e) =>
            {
                e.Accept = true;
            };

            await ftpClient.ConnectAsync();

            bool isSuccess = false;
            if (ftpClient.IsConnected)
            {
                isSuccess = true;
                await ftpClient.DisconnectAsync();
            }

            return isSuccess;
        }
    }
}
