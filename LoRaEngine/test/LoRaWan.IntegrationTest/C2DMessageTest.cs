using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.EventHubs;
using Xunit;

namespace LoRaWan.IntegrationTest
{
    // Tests Cloud to Device messages
    [Collection(Constants.TestCollectionName)] // run in serial
    [Trait("Category", "SkipWhenLiveUnitTesting")]
    public sealed class C2DMessageTest : IntegrationTestBase
    {
        const string FportPropertyName = "fport";
        const string ConfirmedPropertyName = "Confirmed";

        static Random random = new Random();

        public C2DMessageTest(IntegrationTestFixture testFixture) : base(testFixture)
        {
        }

        string ToHexString(string str)
        {
            var sb = new StringBuilder();

            var bytes = Encoding.UTF8.GetBytes(str);
            foreach (var t in bytes)
            {
                sb.Append(t.ToString("X2"));
            }

            return sb.ToString(); // returns: "48656C6C6F20776F726C64" for "Hello world"
        }

        // Ensures that C2D messages are received when working with confirmed messages
        // Uses Device9_OTAA
        [Fact]
        public async Task Test_OTAA_Confirmed_Receives_C2D_Message()
        {
            var device = this.TestFixture.Device9_OTAA;
            this.LogTestStart(device);    

            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWOTAA);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            var joinSucceeded = await this.ArduinoDevice.setOTAAJoinAsyncWithRetry(LoRaArduinoSerial._otaa_join_cmd_t.JOIN, 20000, 5);
            Assert.True(joinSucceeded, "Join failed");

            // wait 1 second after joined
            await Task.Delay(Constants.DELAY_FOR_SERIAL_AFTER_JOIN); 

            // Sends 2x confirmed messages
            for (var i=1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending confirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketWithConfirmedAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                // +CMSG: ACK Received
                await AssertUtils.ContainsWithRetriesAsync("+CMSG: ACK Received", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, c2dMessageBody, new Dictionary<string, string>{ { FportPropertyName, "1"} });
            this.Log($"Message {c2dMessageBody} sent to device, need to check if it receives");

            var foundC2DMessage = false;
            var foundReceivePacket = false;
            var expectedRxSerial = $"+CMSG: PORT: 1; RX: \"{this.ToHexString(c2dMessageBody)}\"";
            this.Log($"Expected C2D received log is: {expectedRxSerial}");

            // Sends 8x confirmed messages, stopping if C2D message is found
            for (var i=3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending confirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketWithConfirmedAsync(msg, 10);

                await Task.Delay(TimeSpan.FromSeconds(5));

                // After transferPacketWithConfirmed: Expectation from serial
                // +CMSG: ACK Received
                await AssertUtils.ContainsWithRetriesAsync("+CMSG: ACK Received", this.ArduinoDevice.SerialLogs);

                // check if c2d message was found
                // 0000000000000009: C2D message: 58
                var c2dLogMessage = $"{device.DeviceID}: C2D message: {c2dMessageBody}";
                var searchResults = await this.TestFixture.SearchNetworkServerModuleAsync(
                    (messageBody) => {
                        return messageBody.StartsWith(c2dLogMessage);
                    },
                    new SearchLogOptions {
                        Description = c2dLogMessage,
                        MaxAttempts = 1
                    });

                // We should only receive the message once
                if (searchResults.Found)
                {
                    this.Log($"{device.DeviceID}: Found C2D message in log (after sending {i}/10) ? {foundC2DMessage}");
                    Assert.False(foundC2DMessage, "Cloud to Device message should have been detected in Network Service module only once");
                    foundC2DMessage = true;                    
                }

                var localFoundCloudToDeviceInSerial = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
                if (localFoundCloudToDeviceInSerial)
                {                    
                    Assert.False(foundReceivePacket, "Cloud to device message should have been received only once");
                    foundReceivePacket = true;
                }

                this.TestFixture.ClearLogs();

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            }

            Assert.True(foundC2DMessage, $"Did not find '{device.DeviceID}: C2D message: {c2dMessageBody}' in logs");

            // checks if log arrived
            if (!foundReceivePacket)
            {
                foundReceivePacket = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
            }

            Assert.True(foundReceivePacket, $"Could not find lora receiving message '{expectedRxSerial}'");
                
        }

        // Ensures that C2D messages are received when working with unconfirmed messages
        // Uses Device10_OTAA
        [Fact]
        public async Task Test_OTAA_Unconfirmed_Receives_C2D_Message()
        {
            var device = this.TestFixture.Device10_OTAA;
            this.LogTestStart(device);   

            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWOTAA);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            var joinSucceeded = await this.ArduinoDevice.setOTAAJoinAsyncWithRetry(LoRaArduinoSerial._otaa_join_cmd_t.JOIN, 20000, 5);
            Assert.True(joinSucceeded, "Join failed");

            // wait 1 second after joined
            await Task.Delay(Constants.DELAY_FOR_SERIAL_AFTER_JOIN); 

            // Sends 2x confirmed messages
            for (var i=1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketAsync(msg, 10);                

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, c2dMessageBody, new Dictionary<string, string> { { FportPropertyName, "1"} });
            this.Log($"Message {c2dMessageBody} sent to device, need to check if it receives");

            var foundC2DMessage = false;
            var foundReceivePacket = false;
            var expectedRxSerial = $"+MSG: PORT: 1; RX: \"{this.ToHexString(c2dMessageBody)}\"";
            this.Log($"Expected C2D received log is: {expectedRxSerial}");

            // Sends 8x confirmed messages, stopping if C2D message is found
            for (var i=3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);
                
                // check if c2d message was found
                // 0000000000000009: C2D message: 58
                var c2dLogMessage = $"{device.DeviceID}: C2D message: {c2dMessageBody}";
                var searchResults = await this.TestFixture.SearchNetworkServerModuleAsync(
                    (messageBody) => {
                        return messageBody.StartsWith(c2dLogMessage);
                    },
                    new SearchLogOptions {
                        Description = c2dLogMessage,
                        MaxAttempts = 1
                    });

                // We should only receive the message once
                if (searchResults.Found)
                {
                    this.Log($"{device.DeviceID}: Found C2D message in log (after sending {i}/10) ? {foundC2DMessage}");
                    Assert.False(foundC2DMessage, "Cloud to Device message should have been detected in Network Service module only once");
                    foundC2DMessage = true;                    
                }

                var localFoundCloudToDeviceInSerial = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
                if (localFoundCloudToDeviceInSerial)
                {                    
                    Assert.False(foundReceivePacket, "Cloud to device message should have been received only once");
                    foundReceivePacket = true;
                }

                this.TestFixture.ClearLogs();

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            }

            Assert.True(foundC2DMessage, $"Did not find '{device.DeviceID}: C2D message: {c2dMessageBody}' in logs");

            // checks if log arrived
            if (!foundReceivePacket)
            {
                foundReceivePacket = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
            }

            Assert.True(foundReceivePacket, $"Could not find lora receiving message '{expectedRxSerial}'");
                
        }

        // Ensures that C2D messages are received when working with unconfirmed messages
        // Uses Device15_OTAA
        [Fact]
        public async Task Test_OTAA_Unconfirmed_Receives_Confirmed_FPort_2_Message()
        {
            var device = this.TestFixture.Device15_OTAA;
            this.LogTestStart(device);

            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWOTAA);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            var joinSucceeded = await this.ArduinoDevice.setOTAAJoinAsyncWithRetry(LoRaArduinoSerial._otaa_join_cmd_t.JOIN, 20000, 5);

            Assert.True(joinSucceeded, "Join failed");
        
            // wait 1 second after joined
            await Task.Delay(Constants.DELAY_FOR_SERIAL_AFTER_JOIN);

            // Sends 2x unconfirmed messages
            for (var i = 1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, c2dMessageBody, new Dictionary<string, string>() { { FportPropertyName, "2"} });
            this.Log($"Message {c2dMessageBody} sent to device, need to check if it receives");

            var foundC2DMessage = false;
            var foundReceivePacket = false;
            var expectedRxSerial = $"+MSG: PORT: 2; RX: \"{this.ToHexString(c2dMessageBody)}\"";
            this.Log($"Expected C2D start with: {expectedRxSerial}");

            // Sends 8x confirmed messages, stopping if C2D message is found
            for (var i = 3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                // check if c2d message was found
                // 0000000000000009: C2D message: 58
                var c2dLogMessage = $"{device.DeviceID}: C2D message: {c2dMessageBody}";
                var searchResults = await this.TestFixture.SearchNetworkServerModuleAsync(
                    (messageBody) => {
                        return messageBody.StartsWith(c2dLogMessage);
                    },
                    new SearchLogOptions
                    {
                        Description = c2dLogMessage,
                        MaxAttempts = 1
                    });

                // We should only receive the message once
                if (searchResults.Found)
                {
                    this.Log($"{device.DeviceID}: Found C2D message in log (after sending {i}/10) ? {foundC2DMessage}");
                    Assert.False(foundC2DMessage, "Cloud to Device message should have been detected in Network Service module only once");
                    foundC2DMessage = true;
                }

                var localFoundCloudToDeviceInSerial = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
                if (localFoundCloudToDeviceInSerial)
                {
                    Assert.False(foundReceivePacket, "Cloud to device message should have been received only once");
                    foundReceivePacket = true;
                }

                this.TestFixture.ClearLogs();

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            }

            Assert.True(foundC2DMessage, $"Did not find '{device.DeviceID}: C2D message: {c2dMessageBody}' in logs");

            // checks if log arrived
            if (!foundReceivePacket)
            {
                foundReceivePacket = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
            }

            Assert.True(foundReceivePacket, $"Could not find lora receiving message '{expectedRxSerial}'");

        }

        // Ensures that C2D messages are received when working with unconfirmed messages
        // Uses Device10_OTAA
        [Fact]
        public async Task Test_OTAA_Unconfirmed_Receives_Confirmed_C2D_Message()
        {
            var device = this.TestFixture.Device14_OTAA;
            this.LogTestStart(device);

            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWOTAA);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            var joinSucceeded = await this.ArduinoDevice.setOTAAJoinAsyncWithRetry(LoRaArduinoSerial._otaa_join_cmd_t.JOIN, 20000, 5);
            Assert.True(joinSucceeded, "Join failed");
            
            // wait 1 second after joined
            await Task.Delay(Constants.DELAY_FOR_SERIAL_AFTER_JOIN);

            // Sends 2x unconfirmed messages
            for (var i = 1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            var msgId = Guid.NewGuid().ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, msgId, c2dMessageBody, new Dictionary<string, string>() 
            { 
                { FportPropertyName, "1" },
                { ConfirmedPropertyName, "true" } 
            });
            this.Log($"Message {c2dMessageBody} sent to device, need to check if it receives");

            var foundC2DMessage = false;
            var foundReceivePacket = false;
            var expectedRxSerial = $"+MSG: PORT: 1; RX: \"{this.ToHexString(c2dMessageBody)}\"";
            var expectedUDPMessageV1 = $"{device.DevAddr}: ConfirmedDataDown";
            var expectedUDPMessageV2 = $"{device.DeviceID}: C2D message: {c2dMessageBody}, id: {msgId}, fport: 1, confirmed: True";
            this.Log($"Expected C2D received log is: {expectedRxSerial}");
            this.Log($"Expected UDP log starting with: {expectedUDPMessageV1} or {expectedUDPMessageV2}");

            // Sends 8x confirmed messages, stopping if C2D message is found
            for (var i = 3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                // check if c2d message was found
                var searchResults = await this.TestFixture.SearchNetworkServerModuleAsync(
                    (messageBody) => {
                        return messageBody.StartsWith(expectedUDPMessageV1) || messageBody.StartsWith(expectedUDPMessageV2);
                    },
                    new SearchLogOptions
                    {
                        Description = $"{expectedUDPMessageV1} or {expectedUDPMessageV2}",
                        MaxAttempts = 1
                    });

                // We should only receive the message once
                if (searchResults.Found)
                {
                    this.Log($"{device.DeviceID}: Found C2D message in log (after sending {i}/10) ? {foundC2DMessage}");
                    Assert.False(foundC2DMessage, "Cloud to Device message should have been detected in Network Service module only once");
                    foundC2DMessage = true;
                }

                var localFoundCloudToDeviceInSerial = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
                if (localFoundCloudToDeviceInSerial)
                {
                    Assert.False(foundReceivePacket, "Cloud to device message should have been received only once");
                    foundReceivePacket = true;
                }

                this.TestFixture.ClearLogs();

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            }

            Assert.True(foundC2DMessage, $"Did not find {expectedUDPMessageV1} or {expectedUDPMessageV2} in logs");

            // checks if log arrived
            if (!foundReceivePacket)
            {
                foundReceivePacket = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial);
            }

            Assert.True(foundReceivePacket, $"Could not find lora receiving message '{expectedRxSerial}'");

        }

        // Ensures that C2D messages using invalid fport
        // - Message is not forwarded to device
        // - error is logged with correlation-id
        // - Invalid fport values:
        // - 0: reserved for mac commands
        // - 224+: reserved for future applications
        // Uses Device16_OTAA and Device17_OTAA
        [Theory]
        [InlineData(0, nameof(IntegrationTestFixture.Device18_ABP))] // 0 => mac commands
        [InlineData(224, nameof(IntegrationTestFixture.Device19_ABP))] // >= 224 is reserved
        public async Task C2D_Using_Invalid_FPort_Should_Be_Ignored(byte fport, string devicePropertyName)
        {
            var device = this.TestFixture.GetDeviceByPropertyName(devicePropertyName);
            this.LogTestStart(device);
            
            // Setup LoRa device properties
            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWABP);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            // Setup protocol properties
            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            // Sends 2x unconfirmed messages
            for (var i = 1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            var c2dMessage = new Message(Encoding.UTF8.GetBytes(c2dMessageBody))
            {
                MessageId = Guid.NewGuid().ToString(),
            };            
            c2dMessage.Properties[FportPropertyName] = fport.ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, c2dMessage);
            this.Log($"Message {c2dMessageBody} sent to device");

            // Following log will be generated in case C2D message has invalid fport:
            // "<device-id>: invalid fport '<fport>' in C2D message '<message-id>'
            var foundC2DInvalidFPortMessage = false;

            // Serial port will print the following once the message is received
            var expectedRxSerial = $"RX: \"{this.ToHexString(c2dMessageBody)}\"";
            this.Log($"Expected C2D contains: {expectedRxSerial}");

            // Sends 8x unconfirmed messages, stopping if C2D message is found
            for (var i = 3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                // ensure c2d message is not found
                // 0000000000000016: C2D message: 58
                var c2dLogMessage = $"{device.DeviceID}: C2D message: {c2dMessageBody}";
                var searchC2DMessageLogResult = await this.TestFixture.SearchNetworkServerModuleAsync(
                    (messageBody) => {
                        return messageBody.StartsWith(c2dLogMessage);
                    },
                    new SearchLogOptions
                    {
                        Description = c2dLogMessage,
                        MaxAttempts = 1
                    }
                );
                Assert.False(searchC2DMessageLogResult.Found, $"Message with fport {fport} should not be forwarded to device");

                // ensure log with error is found
                if (!foundC2DInvalidFPortMessage)
                {
                    var invalidFportLog = $"{device.DeviceID}: invalid fport '{fport}' in C2D message '{c2dMessage.MessageId}'";
                    var searchInvalidFportLog = await this.TestFixture.SearchNetworkServerModuleAsync(
                        (messageBody) => {
                            return messageBody.StartsWith(invalidFportLog);
                        },
                        new SearchLogOptions
                        {
                            Description = invalidFportLog,
                            MaxAttempts = 1
                        }
                    );

                    foundC2DInvalidFPortMessage = searchInvalidFportLog.Found;
                }

                // Should not contain in the serial the c2d message
                Assert.DoesNotContain(this.ArduinoDevice.SerialLogs, log => log.Contains(expectedRxSerial, StringComparison.InvariantCultureIgnoreCase));

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
                
                this.TestFixture.ClearLogs();
            }

            Assert.True(foundC2DInvalidFPortMessage, "Invalid fport message was not found in network server log");
        }   

        /// <summary>
        /// Ensures that a device that has preferred window set to two receives C2D messages
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task C2D_When_Device_Has_Preferred_Windows_2_Should_Receive_In_2nd_Window()
        {
            var device = this.TestFixture.Device21_ABP;
            this.LogTestStart(device);

            // Setup LoRa device properties
            await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWABP);
            await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
            await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);

            // Setup protocol properties
            await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

            // Sends 2x unconfirmed messages
            for (var i = 1; i <= 2; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");

                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                this.TestFixture.ClearLogs();
            }

            // sends C2D - between 10 and 99
            var c2dMessageBody = (100 + random.Next(90)).ToString();
            var msgId = Guid.NewGuid().ToString();
            await this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, msgId, c2dMessageBody, new Dictionary<string, string>()
            {
                { FportPropertyName, "1" },
                { ConfirmedPropertyName, "true" },
            });

            this.Log($"Message {c2dMessageBody} sent to device, need to check if it receives");

            var foundC2DMessage = false;
            var foundReceivePacket = false;
            var foundReceivePacketInRX2 = false;
            var expectedRxSerial1 = $"+MSG: PORT: 1; RX: \"{this.ToHexString(c2dMessageBody)}\"";
            var expectedRxSerial2 = $"+MSG: RXWIN2";
            var expectedUDPMessageV1 = $"{device.DevAddr}: ConfirmedDataDown";
            var expectedUDPMessageV2 = $"{device.DeviceID}: C2D message: {c2dMessageBody}, id: {msgId}, fport: 1, confirmed: True";
            this.Log($"Expected C2D received log is: {expectedRxSerial1} and {expectedRxSerial2}");
            this.Log($"Expected UDP log starting with: {expectedUDPMessageV1} or {expectedUDPMessageV2}");

            // Sends 8x confirmed messages, stopping if C2D message is found
            for (var i = 3; i <= 10; ++i)
            {
                var msg = PayloadGenerator.Next().ToString();
                this.Log($"{device.DeviceID}: Sending unconfirmed '{msg}' {i}/10");
                await this.ArduinoDevice.transferPacketAsync(msg, 10);

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

                await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);

                // check if c2d message was found
                if (!foundC2DMessage)
                {
                    var searchResults = await this.TestFixture.SearchNetworkServerModuleAsync(
                        (messageBody) => {
                            return messageBody.StartsWith(expectedUDPMessageV1) || messageBody.StartsWith(expectedUDPMessageV2);
                        },
                        new SearchLogOptions
                        {
                            Description = $"{expectedUDPMessageV1} or {expectedUDPMessageV2}",
                            MaxAttempts = 1,
                        });

                    // We should only receive the message once
                    if (searchResults.Found)
                    {
                        this.Log($"{device.DeviceID}: Found C2D message in log (after sending {i}/10)");
                        foundC2DMessage = true;
                    }
                }

                if (!foundReceivePacket)
                {
                    if (this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial1))
                    {
                        this.Log($"{device.DeviceID}: Found serial log message {expectedRxSerial1} in log (after sending {i}/10)");
                        foundReceivePacket = true;
                    }
                }

                if (!foundReceivePacketInRX2)
                {
                    if (this.ArduinoDevice.SerialLogs.Any(x => x.StartsWith(expectedRxSerial2)))
                    {
                        this.Log($"{device.DeviceID}: Found serial log message {expectedRxSerial2} in log (after sending {i}/10)");
                        foundReceivePacketInRX2 = true;
                    }
                }

                if (foundReceivePacket && foundReceivePacketInRX2 && foundC2DMessage)
                {
                    this.Log($"{device.DeviceID}: Found all messages in log (after sending {i}/10)");
                    break;
                }

                this.TestFixture.ClearLogs();

                await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            }

            Assert.True(foundC2DMessage, $"Did not find {expectedUDPMessageV1} or {expectedUDPMessageV2} in logs");

            // checks if serial received the message
            if (!foundReceivePacket)
            {
                foundReceivePacket = this.ArduinoDevice.SerialLogs.Contains(expectedRxSerial1);
            }

            Assert.True(foundReceivePacket, $"Could not find lora receiving message '{expectedRxSerial1}'");


            // checks if serial received the message in RX2
            if (!foundReceivePacketInRX2)
            {
                foundReceivePacketInRX2 = this.ArduinoDevice.SerialLogs.Any(x => x.StartsWith(expectedRxSerial2));
            }

            Assert.True(foundReceivePacketInRX2, $"Could not find lora receiving message '{expectedRxSerial2}'");
        }

        // [Fact]
        // public async Task C2D_With_PreferredWindow2_Should_Process_C2d_In_First_Upstream()
        // {
        //    Test.Shared.TestLogger.LogDate = true;
        //    this.ArduinoDevice.LogWrites = true;

        // var device = this.TestFixture.Device21_ABP;
        //    this.LogTestStart(device);
            
        // // Setup LoRa device properties
        //    await this.ArduinoDevice.setIdAsync(device.DevAddr, device.DeviceID, device.AppEUI);
        //    await this.ArduinoDevice.setKeyAsync(device.NwkSKey, device.AppSKey, device.AppKey);
        //    await this.ArduinoDevice.setDeviceModeAsync(LoRaArduinoSerial._device_mode_t.LWABP);

        // // Setup protocol properties
        //    await this.ArduinoDevice.SetupLora(this.TestFixture.Configuration.LoraRegion);

        // // Send 1 message to have device in cache
        //    await this.ArduinoDevice.transferPacketAsync(PayloadGenerator.Next().ToString(), 10);
        //    await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
        //    await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs); 
            
        // await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);
            
        // this.TestFixture.ClearLogs();

        // var c2dMessageBody = (100 + random.Next(90)).ToString();
        //    var payloadToSearch = PayloadGenerator.Next().ToString();      
        //    this.TestFixture.IoTHubMessages.Subscribe((IEnumerable<EventData> events) => {
        //        foreach (var ev in events)
        //        {
        //            if ((string)ev.SystemProperties["iothub-connection-device-id"] == device.DeviceID)
        //            {
        //                var bodyText = Encoding.UTF8.GetString(ev.Body);
        //                if (bodyText.Contains(payloadToSearch))
        //                {                            
        //                    var c2dMessage = new Message(Encoding.UTF8.GetBytes(c2dMessageBody))
        //                    {
        //                        MessageId = Guid.NewGuid().ToString(),
        //                    };            
        //                    c2dMessage.Properties[FportPropertyName] = "1";
        //                    this.TestFixture.SendCloudToDeviceMessage(device.DeviceID, c2dMessage).GetAwaiter().GetResult();
        //                    break;
        //                }
        //            }
        //        }
        //    });

        // // Sends unconfirmed message & c2d message            
        //    await this.ArduinoDevice.transferPacketAsync(payloadToSearch, 10); 
        //    //await this.ArduinoDevice.transferPacketWithConfirmedAsync(payloadToSearch, 10);          

        // await Task.Delay(Constants.DELAY_BETWEEN_MESSAGES);

        // // +CMSG: ACK Received
        //    // await AssertUtils.ContainsWithRetriesAsync("+CMSG: ACK Received", this.ArduinoDevice.SerialLogs);

        // await AssertUtils.ContainsWithRetriesAsync("+MSG: Done", this.ArduinoDevice.SerialLogs);            
        //    await AssertUtils.ContainsWithRetriesAsync($"RX: \"{this.ToHexString(c2dMessageBody)}\"", this.ArduinoDevice.SerialLogs);
        // }   
    }
}