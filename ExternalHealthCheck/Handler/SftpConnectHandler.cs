using ExternalHealthCheck.Helper;
using ExternalHealthCheck.Models;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExternalHealthCheck.Handler
{
    public class SFtpConnectHandler
    {
        public static async Task<bool> Check(ExternalResource externalResource)
        {
            var password = await KeyvaultHelper.Get(externalResource.Keyvault);

            ConnectionInfo ConnNfo = new ConnectionInfo(externalResource.Host, externalResource.Port, externalResource.Username,
                new AuthenticationMethod[]{

                    // Pasword based Authentication
                    new PasswordAuthenticationMethod(externalResource.Username, password)
                }
            );

            bool isSuccess = false;

            using (var sshclient = new SshClient(ConnNfo))
            {
                sshclient.Connect();
                if (sshclient.IsConnected)
                {
                    isSuccess = true;
                }
                sshclient.Disconnect();
            }

            return isSuccess;
        }
    }
}
