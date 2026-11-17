using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Samba.Infrastructure.Helpers;

namespace Samba.Modules.PosModule
{
    public class KitchenOrderNetworkPacket
    {
        public string Command { get; set; } // "NEW_ORDER", "STATUS_CHANGE", "PING"
        public int TicketId { get; set; }
        public string TicketNumber { get; set; }
        public string TableName { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public List<KitchenOrderItemPacket> Items { get; set; }

        public KitchenOrderNetworkPacket()
        {
            Items = new List<KitchenOrderItemPacket>();
        }
    }

    public class KitchenOrderItemPacket
    {
        public string MenuItemName { get; set; }
        public decimal Quantity { get; set; }
        public string PortionName { get; set; }
        public string Tags { get; set; }
        public bool IsNewAddition { get; set; }
    }

    public static class KitchenNetworkManager
    {
        public const int DefaultPort = 18888;
        private static TcpListener _listener;
        private static Thread _listenThread;
        private static bool _isRunning;

        public static event Action<KitchenOrderNetworkPacket> OrderReceived;

        public static void StartServer(int port = DefaultPort)
        {
            if (_isRunning) return;

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;

                _listenThread = new Thread(ListenLoop)
                {
                    IsBackground = true
                };
                _listenThread.Start();
            }
            catch (Exception)
            {
                // Port kullanımda veya yetki yok
            }
        }

        public static void StopServer()
        {
            _isRunning = false;
            try { _listener?.Stop(); } catch { }
            try { _listenThread?.Abort(); } catch { }
        }

        private static void ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void HandleClient(object obj)
        {
            var client = obj as TcpClient;
            if (client == null) return;

            try
            {
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        var packet = JsonHelper.Deserialize<KitchenOrderNetworkPacket>(line);
                        if (packet != null)
                        {
                            AppendOfflineOrder(packet);
                            if (OrderReceived != null)
                            {
                                OrderReceived(packet);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        public static bool SendOrderToKitchen(string kitchenIp, KitchenOrderNetworkPacket packet, int port = DefaultPort)
        {
            if (string.IsNullOrEmpty(kitchenIp)) return false;

            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(kitchenIp, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    if (!success) return false;
                    client.EndConnect(result);

                    using (var stream = client.GetStream())
                    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                    {
                        var json = JsonHelper.Serialize(packet);
                        writer.WriteLine(json);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetQueueFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = Path.Combine(appData, "SambaPOS3");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "kitchen_queue.json");
        }

        public static List<KitchenOrderNetworkPacket> LoadOfflineQueue()
        {
            try
            {
                var file = GetQueueFilePath();
                if (File.Exists(file))
                {
                    var content = File.ReadAllText(file, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(content))
                    {
                        var list = JsonHelper.Deserialize<List<KitchenOrderNetworkPacket>>(content);
                        if (list != null) return list;
                    }
                }
            }
            catch { }
            return new List<KitchenOrderNetworkPacket>();
        }

        public static void SaveOfflineQueue(List<KitchenOrderNetworkPacket> list)
        {
            try
            {
                var file = GetQueueFilePath();
                var json = JsonHelper.Serialize(list ?? new List<KitchenOrderNetworkPacket>());
                File.WriteAllText(file, json, Encoding.UTF8);
            }
            catch { }
        }

        public static void AppendOfflineOrder(KitchenOrderNetworkPacket packet)
        {
            lock (typeof(KitchenNetworkManager))
            {
                var list = LoadOfflineQueue();
                list.RemoveAll(x => x.TicketId == packet.TicketId);
                list.Insert(0, packet);
                if (list.Count > 100) list = list.GetRange(0, 100);
                SaveOfflineQueue(list);
            }
        }

        public static void RemoveOfflineOrder(int ticketId)
        {
            lock (typeof(KitchenNetworkManager))
            {
                var list = LoadOfflineQueue();
                list.RemoveAll(x => x.TicketId == ticketId);
                SaveOfflineQueue(list);
            }
        }

        private static string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = Path.Combine(appData, "SambaPOS3");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "kitchen_endpoints.txt");
        }

        public static string LoadEndpoints()
        {
            try
            {
                var file = GetConfigFilePath();
                if (File.Exists(file)) return File.ReadAllText(file, Encoding.UTF8);
            }
            catch { }
            return "127.0.0.1:18888";
        }

        public static void SaveEndpoints(string endpoints)
        {
            try
            {
                var file = GetConfigFilePath();
                File.WriteAllText(file, endpoints, Encoding.UTF8);
            }
            catch { }
        }

        public static void BroadcastOrderToAll(KitchenOrderNetworkPacket packet)
        {
            var config = LoadEndpoints();
            if (string.IsNullOrEmpty(config)) config = "127.0.0.1:18888";

            var lines = config.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var target = line.Trim();
                if (string.IsNullOrEmpty(target)) continue;

                var host = target;
                var port = DefaultPort;
                if (target.Contains(":"))
                {
                    var parts = target.Split(':');
                    host = parts[0].Trim();
                    int p;
                    if (int.TryParse(parts[1].Trim(), out p)) port = p;
                }

                try
                {
                    var sent = SendOrderToKitchen(host, packet, port);
                    if (!sent && host == "127.0.0.1")
                    {
                        // Mutfak ekranı henüz açılmamışsa bile yerel kuyruğa yaz
                        AppendOfflineOrder(packet);
                    }
                }
                catch { }
            }
        }
    }
}
