using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

using System.Runtime.InteropServices; // DllImport requires

namespace MqTemp
{

    internal static class NativeMethods
    {

        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxText(IntPtr hWnd, String text, String caption, uint type);


        // Declares managed prototypes for unmanaged functions.
        [DllImport("User32.dll", EntryPoint = "MessageBoxA",
            CharSet = CharSet.Auto)]
        internal static extern int MessageBoxA(
            IntPtr hWnd, string lpText, string lpCaption, uint uType);

        // Causes incorrect output in the message window.
        [DllImport("User32.dll", EntryPoint = "MessageBoxW",
            CharSet = CharSet.Ansi)]
        internal static extern int MessageBoxW(
            IntPtr hWnd, string lpText, string lpCaption, uint uType);

        // Causes an exception to be thrown. EntryPoint, CharSet, and
        // ExactSpelling fields are mismatched.
        [DllImport("User32.dll", EntryPoint = "MessageBox",
            CharSet = CharSet.Ansi, ExactSpelling = true)]
        internal static extern int MessageBox(
            IntPtr hWnd, string lpText, string lpCaption, uint uType);
    }

    class JsonHardwareRecord
    {

        public UInt32 id { get; set; }
        public String Text { get; set; }
        public List<JsonHardwareRecord> Children { get; set; }
        public String Min { get; set; }
        public String Max { get; set; }
        public String Value { get; set; }

        public Int32 ValueInt
        {
            get {
                return Int32.Parse((new Regex(@"^\d+")).Match(this.Value).Value);
            }
        }

        public static JsonHardwareRecord BuildFrom(String json)
        {
            return JsonConvert.DeserializeObject<JsonHardwareRecord>(json);
        }

        public JsonHardwareRecord Child(String Name)
        {
            Int32 tree = Name.IndexOf('/');
            if (tree == -1) {
                foreach (var child in this.Children) {
                    if (child.Text == Name) {
                        return child;
                    }
                }
            } else {
                var split = Name.Split(new[] { '/' }, 2);
                if (split[0].Length > 0 && split[1].Length > 0) {
                    return Child(split[0]).Child(split[1]);
                } else {
                    return null;
                }
            }
            return null;
        }
    }

    class Program
    {

        const String ApplicationID = @"Latdev.Notification.Temperatures";
        const String HardwareMonitorURL = @"http://127.0.0.1:8085/data.json";
        static JsonHardwareRecord HardwareMonitorData = null;

        static bool getHardwareData()
        {

            Program.HardwareMonitorData = null;
            string result = String.Empty;

            try {

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Program.HardwareMonitorURL);
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (Stream stream = res.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream)) {
                    result = reader.ReadToEnd();
                }

            } catch (WebException) {
            }


            if (result.Length <= 1) {
                return false;
            }

            try {
                JsonHardwareRecord data = JsonHardwareRecord.BuildFrom(result);
                Program.HardwareMonitorData = data;
            } catch (Exception) {
                return false;
            }

            return true;
        }


        const UInt32 hwnd = 0;
        const UInt32 MB_ICONEXCLAMATION = 0x00000030;
        const UInt32 MB_RETRYCANCEL = 0x00000005;
        const UInt32 IDCANCEL = 0x00000002;

        const String title = @"OpenHardwareMonitor To MQTT";
        const String msg = "Trying to connect: {0}\n\nSorry unnable to find hardware data server!\n\nPlease enable OpenHardwareMonoitor and enable Web Server";

        static void Main(string[] args)
        {
            while (true) {
                while (!Program.getHardwareData()) {
                    int userRespond = NativeMethods.MessageBoxText((IntPtr) 0, msg.Replace("{0}", HardwareMonitorURL), title, MB_RETRYCANCEL | MB_ICONEXCLAMATION);
                    if (userRespond == IDCANCEL) {
                        Thread.Sleep(60000 * 60); // Sleep for hour
                    }
                }

                var gpurec = HardwareMonitorData.Child(@"DARKNOTE/NVIDIA GeForce 7300 GT/Temperatures/GPU Core");
                if (gpurec != null) {
                    Int32 gpu = gpurec.ValueInt;
                    List<Int32> cpu = new List<Int32> {
                        HardwareMonitorData.Child(@"DARKNOTE/Intel Core 2 Quad Q6700/Temperatures/CPU Core #1").ValueInt,
                        HardwareMonitorData.Child(@"DARKNOTE/Intel Core 2 Quad Q6700/Temperatures/CPU Core #2").ValueInt,
                        HardwareMonitorData.Child(@"DARKNOTE/Intel Core 2 Quad Q6700/Temperatures/CPU Core #3").ValueInt,
                        HardwareMonitorData.Child(@"DARKNOTE/Intel Core 2 Quad Q6700/Temperatures/CPU Core #4").ValueInt,
                    };

                    MqttClient client = new MqttClient("192.168.1.198");
                    client.Connect("MQ" + RandomString(20));
                    Thread.Sleep(500);
                    if (client.IsConnected) {
                        MQPublish(client, "sensors/dnote/gputemp", gpu.ToString());
                        MQPublish(client, "sensors/dnote/cputemp", "[" + String.Join(",", cpu) + "]");
                        client.Disconnect();
                    }
                }
                Thread.Sleep(5000);
            }
        }

        private static Random random = new Random();
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private static void MQPublish(MqttClient client, String topic, String message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            client.Publish(topic, bytes);
            Thread.Sleep(100);
        }

    }



}
