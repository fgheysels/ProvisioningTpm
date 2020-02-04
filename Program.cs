using System.Threading.Tasks;
using Microsoft.Azure.Devices.Provisioning.Service;
using Microsoft.Extensions.Configuration;

namespace ProvisioningTpm
{
    // This example is based on:
    // https://github.com/Azure-Samples/azure-iot-samples-csharp/tree/master/provisioning/Samples/device

    using Microsoft.Azure.Devices.Provisioning.Client;
    using Microsoft.Azure.Devices.Provisioning.Client.Transport;
    using Microsoft.Azure.Devices.Shared;
    using System;
    using Microsoft.Azure.Devices.Provisioning.Security;

    public static class Program
    {
        // - pass Device Provisioning Service ID_Scope as a command-prompt argument
        private static string _idScope = string.Empty;

        // - pass an individual enrollment registration id for this device
        private static string _registrationId = string.Empty;

        // - The DeviceId that will be used to identify the device in IoT Hub
        private static string _deviceId = string.Empty;

        // - If you want to skip the device message send test, pass 'Y'
        private static string _skipTest = string.Empty;

        private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";

        public static async Task<int> Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                                    .SetBasePath(Environment.CurrentDirectory)
                                    .AddJsonFile("appSettings.json", optional: false)
                                    .AddJsonFile("appSettings.development.json", optional: true)
                                    .Build();

            var dpsConnection = configuration.GetConnectionString("Dps");

            if (String.IsNullOrWhiteSpace(dpsConnection))
            {
                Console.WriteLine("The connectionstring of the DPS service is not provided");
                Console.WriteLine("Make sure that the appsettings.json file contains an entry for the ConnectionStrings:Dps setting");

                return -1;
            }

            Console.WriteLine("Provision your TPM");
            Console.WriteLine("------------------");
            Console.WriteLine("Usage: ProvisionTpm <IDScope> <RegistrationID> <DeviceID> <SkipTest:Y|N>");
            Console.WriteLine("Run this 'As Adminsitrator' or 'SU'");

            if (string.IsNullOrWhiteSpace(_idScope) && (args.Length > 0))
            {
                _idScope = args[0];
            }

            if (string.IsNullOrWhiteSpace(_registrationId) && (args.Length > 1))
            {
                _registrationId = args[1];
            }

            if (string.IsNullOrWhiteSpace(_deviceId) && (args.Length > 2))
            {
                _deviceId = args[2].ToUpper();
            }

            if (string.IsNullOrWhiteSpace(_skipTest) && (args.Length > 3))
            {
                _skipTest = args[3].ToUpper();
            }

            if (string.IsNullOrWhiteSpace(_idScope)
                || string.IsNullOrWhiteSpace(_registrationId)
                || string.IsNullOrWhiteSpace(_deviceId)
                || string.IsNullOrWhiteSpace(_skipTest))
            {
                Console.WriteLine("Check if the parameters are corrent: ProvisionTpm <IDScope> <RegistrationID> <DeviceID> <SkipTest:Y|N>");
                return 1;
            }

            if (RegistrationId.IsValid(_registrationId) == false)
            {
                Console.WriteLine("Invalid registrationId: The registration ID is alphanumeric, lowercase, and may contain hyphens");
                return 1;
            }

            using (var security = new SecurityProviderTpmHsm(_registrationId))
            {
                using (var transport = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpOnly))
                {
                    // Note that the TPM simulator will create an NVChip file containing the simulated TPM state.
                    Console.WriteLine("Extracting endorsement key.");
                    string base64EK = Convert.ToBase64String(security.GetEndorsementKey());

                    Console.WriteLine(
                        "In your Azure Device Provisioning Service please go to 'Manage enrollments' and select " +
                        "'Individual Enrollments'. Select 'Add individual enrollment' then fill in the following:");

                    Console.WriteLine($"\tMechanism: TPM");
                    Console.WriteLine($"\tEndorsement key: {base64EK}");
                    Console.WriteLine($"\tRegistration ID: {_registrationId}");
                    Console.WriteLine($"\tSwitch over to the IoT Edge device enrollemnt is needed");
                    Console.WriteLine($"\tIoT Hub Device ID: {_registrationId} (or any other valid DeviceID)");

                    Console.WriteLine("Press enter to enroll this device in DPS");
                    Console.ReadLine();

                    await EnrollDeviceInDpsAsync(dpsConnection, _registrationId, base64EK, _deviceId);

                    Console.WriteLine("");
                    Console.WriteLine("The device is enrolled in DPS");
                    Console.WriteLine($"\tCheck if the correct IoT Hub is selected");
                    Console.WriteLine($"\tFinally, Save this individual enrollment");
                    Console.WriteLine();
                    Console.WriteLine("Press ENTER when ready. This will start finalizing the registration on your TPM");
                    Console.ReadLine();

                    ProvisioningDeviceClient provClient =
                        ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, _idScope, security, transport);

                    var client = new ProvisioningDeviceTpmClient(provClient, security, _skipTest);
                    await client.RunTestAsync();
                    Console.WriteLine("The registration is finalized on the TPM");

                    if (_skipTest != "Y")
                    {
                        Console.WriteLine("The connection is tested by sending a test message");
                    }
                }

                return 0;
            }
        }

        private static async Task EnrollDeviceInDpsAsync(string dpsConnectionString, string registrationId, string tpmEndorsementKey, string deviceId)
        {
            using (var provisioningServiceClient = ProvisioningServiceClient.CreateFromConnectionString(dpsConnectionString))
            {
                Console.WriteLine("\nCreating a new individualEnrollment...");

                var attestation = new TpmAttestation(tpmEndorsementKey);

                var individualEnrollment =
                    new IndividualEnrollment(registrationId.ToLower(), attestation);

                // The following parameters are optional. Remove them if you don't need them.
                individualEnrollment.DeviceId = deviceId;

                // Add this line if you deploy an IoT Edge device
                individualEnrollment.Capabilities =
                    new DeviceCapabilities { IotEdge = true };

                individualEnrollment.ProvisioningStatus = ProvisioningStatus.Enabled;

                Console.WriteLine("\nAdding new individualEnrollment...");

                var individualEnrollmentResult =
                    await provisioningServiceClient.
                          CreateOrUpdateIndividualEnrollmentAsync(individualEnrollment)
                          .ConfigureAwait(false);

                Console.WriteLine("\nIndividualEnrollment created with success.");
                Console.WriteLine(individualEnrollmentResult);
            }
        }
    }
}