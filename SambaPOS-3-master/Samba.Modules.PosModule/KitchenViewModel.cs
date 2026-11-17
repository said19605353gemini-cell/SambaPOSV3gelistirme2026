using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows;
using Microsoft.Practices.Prism.Commands;
using Samba.Domain.Models.Tickets;
using Samba.Infrastructure.Settings;
using Samba.Presentation.Common;
using Samba.Presentation.Common.Commands;
using Samba.Presentation.Services;
using Samba.Presentation.Services.Common;
using Samba.Services;

namespace Samba.Modules.PosModule
{
    public class KitchenOrderItemViewModel : ObservableObject
    {
        public string MenuItemName { get; set; }
        public decimal Quantity { get; set; }
        public string PortionName { get; set; }
        public string Tags { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsNewAddition { get; set; }

        public string ItemColor
        {
            get { return IsNewAddition ? "#FBBF24" : "#F1F5F9"; } // Amber/Yellow for new additions
        }

        public string DisplayText
        {
            get
            {
                var portion = !string.IsNullOrEmpty(PortionName) && PortionName != "Normal" ? string.Format(" ({0})", PortionName) : "";
                var tags = !string.IsNullOrEmpty(Tags) ? string.Format(" [{0}]", Tags) : "";
                var newBadge = IsNewAddition ? " [YENİ EKLENDİ]" : "";
                return string.Format("{0}x  {1}{2}{3}{4}", Quantity.ToString(LocalSettings.QuantityFormat), MenuItemName, portion, tags, newBadge);
            }
        }
    }

    public class KitchenTicketCardViewModel : ObservableObject
    {
        private readonly Action<KitchenTicketCardViewModel> _onComplete;
        private readonly Action<KitchenTicketCardViewModel> _onPrint;

        public int TicketId { get; set; }
        public string TicketNumber { get; set; }
        public string TableName { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedDateTime { get; set; }
        public ObservableCollection<KitchenOrderItemViewModel> Items { get; set; }

        public string OrderTimeFormatted
        {
            get { return CreatedDateTime.ToString("HH:mm"); }
        }

        public string TotalAmountFormatted
        {
            get { return TotalAmount.ToString(LocalSettings.ReportCurrencyFormat); }
        }

        public int ItemTypeCount
        {
            get { return Items != null ? Items.Count : 0; }
        }

        public decimal TotalQuantityCount
        {
            get { return Items != null ? Items.Sum(x => x.Quantity) : 0; }
        }

        private string _status;
        public string Status
        {
            get { return _status; }
            set { _status = value; RaisePropertyChanged(() => Status); RaisePropertyChanged(() => StatusColor); }
        }

        public string StatusColor
        {
            get
            {
                if (Status == "Hazırlanıyor") return "#F59E0B"; // Amber
                if (Status == "Hazır") return "#10B981"; // Emerald green
                return "#EF4444"; // Red for new/waiting
            }
        }

        public string ElapsedTime
        {
            get
            {
                var diff = DateTime.Now - CreatedDateTime;
                if (diff.TotalMinutes < 1) return "Yeni";
                return string.Format("{0} dk", (int)diff.TotalMinutes);
            }
        }

        public DelegateCommand<string> NextStatusCommand { get; set; }
        public DelegateCommand<string> PrintKitchenBillCommand { get; set; }

        public KitchenTicketCardViewModel(int ticketId, string ticketNumber, string tableName, decimal totalAmount, DateTime createdTime, IEnumerable<Order> orders, Action<KitchenTicketCardViewModel> onComplete, Action<KitchenTicketCardViewModel> onPrint)
        {
            TicketId = ticketId;
            TicketNumber = ticketNumber;
            TableName = string.IsNullOrEmpty(tableName) ? string.Format("Adisyon #{0}", ticketNumber) : tableName;
            TotalAmount = totalAmount;
            CreatedDateTime = createdTime;
            _status = "Bekliyor";
            _onComplete = onComplete;
            _onPrint = onPrint;

            Items = new ObservableCollection<KitchenOrderItemViewModel>(
                orders.Select(x => new KitchenOrderItemViewModel
                {
                    MenuItemName = x.MenuItemName,
                    Quantity = x.Quantity,
                    PortionName = x.PortionName,
                    Tags = x.GetOrderTagValues() != null ? string.Join(", ", x.GetOrderTagValues().Select(t => t.TagValue)) : "",
                    IsCompleted = false
                }));

            NextStatusCommand = new DelegateCommand<string>(OnNextStatus);
            PrintKitchenBillCommand = new DelegateCommand<string>(OnPrintBill);
        }

        private void OnNextStatus(string obj)
        {
            if (Status == "Bekliyor")
            {
                Status = "Hazırlanıyor";
            }
            else if (Status == "Hazırlanıyor")
            {
                Status = "Hazır";
                if (_onComplete != null) _onComplete(this);
            }
            RaisePropertyChanged(() => ElapsedTime);
        }

        private void OnPrintBill(string obj)
        {
            if (_onPrint != null) _onPrint(this);
        }

        public void UpdateElapsed()
        {
            RaisePropertyChanged(() => ElapsedTime);
        }
    }

    [Export]
    public class KitchenViewModel : ObservableObject
    {
        private readonly ITicketService _ticketService;
        private readonly ITicketServiceBase _ticketServiceBase;
        private readonly IPrinterService _printerService;
        private readonly ICacheService _cacheService;
        private readonly IApplicationState _applicationState;
        private Timer _refreshTimer;

        public ObservableCollection<KitchenTicketCardViewModel> PendingOrders { get; set; }

        public DelegateCommand<string> RefreshCommand { get; set; }
        public DelegateCommand<string> CloseCommand { get; set; }
        public DelegateCommand<string> OpenNetworkSettingsCommand { get; set; }

        private int _totalPendingCount;
        public int TotalPendingCount
        {
            get { return _totalPendingCount; }
            set { _totalPendingCount = value; RaisePropertyChanged(() => TotalPendingCount); }
        }

        [ImportingConstructor]
        public KitchenViewModel(ITicketService ticketService, ITicketServiceBase ticketServiceBase, IPrinterService printerService, ICacheService cacheService, IApplicationState applicationState)
        {
            _ticketService = ticketService;
            _ticketServiceBase = ticketServiceBase;
            _printerService = printerService;
            _cacheService = cacheService;
            _applicationState = applicationState;

            PendingOrders = new ObservableCollection<KitchenTicketCardViewModel>();
            RefreshCommand = new DelegateCommand<string>(x => LoadKitchenOrders(true));
            CloseCommand = new DelegateCommand<string>(OnClose);
            OpenNetworkSettingsCommand = new DelegateCommand<string>(OnOpenNetworkSettings);

            // Ağ dinleyicisini başlat (Ayrı DB / Mutfak Bilgisayarı modu)
            KitchenNetworkManager.StartServer();
            KitchenNetworkManager.OrderReceived += OnNetworkOrderReceived;

            _refreshTimer = new Timer(OnTimerTick, null, 10000, 10000);
        }

        private void OnOpenNetworkSettings(string obj)
        {
            var current = KitchenNetworkManager.LoadEndpoints();
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Siparişlerin iletileceği Mutfak / Bar Bilgisayarlarının IP ve Portlarını girin:\n(Birden fazla ise noktalı virgül ';' veya alt alta yazın)\nÖrnek:\n192.168.1.50:18888; 192.168.1.51:18888",
                "Mutfak / Bar Ekranları IP Ayarı",
                current);

            if (!string.IsNullOrEmpty(input))
            {
                KitchenNetworkManager.SaveEndpoints(input);
                MessageBox.Show("Mutfak/Bar ağ adresleri kaydedildi!", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnNetworkOrderReceived(KitchenOrderNetworkPacket packet)
        {
            if (packet == null || packet.Items == null || !packet.Items.Any()) return;

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                var existing = PendingOrders.FirstOrDefault(x => x.TicketId == packet.TicketId);
                if (existing == null)
                {
                    var orders = packet.Items.Select(i => new Order
                    {
                        MenuItemName = i.MenuItemName,
                        Quantity = i.Quantity,
                        PortionName = i.PortionName
                    }).ToList();

                    var card = new KitchenTicketCardViewModel(packet.TicketId, packet.TicketNumber, packet.TableName, packet.TotalAmount, packet.CreatedDateTime, orders, OnTicketCompleted, OnPrintTicket);
                    for (int i = 0; i < packet.Items.Count && i < card.Items.Count; i++)
                    {
                        card.Items[i].IsNewAddition = packet.Items[i].IsNewAddition;
                    }
                    PendingOrders.Insert(0, card);
                    TotalPendingCount = PendingOrders.Count;

                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
                else
                {
                    // Masaya yeni eklenen ürünler geldi: Mevcut karta güncelleme yap ve yeni eklenenleri işaretle
                    existing.TotalAmount = packet.TotalAmount;
                    foreach (var item in packet.Items)
                    {
                        var foundItem = existing.Items.FirstOrDefault(x => x.MenuItemName == item.MenuItemName && x.PortionName == item.PortionName);
                        if (foundItem == null)
                        {
                            existing.Items.Add(new KitchenOrderItemViewModel
                            {
                                MenuItemName = item.MenuItemName,
                                Quantity = item.Quantity,
                                PortionName = item.PortionName,
                                Tags = item.Tags,
                                IsNewAddition = true
                            });
                        }
                        else if (item.IsNewAddition)
                        {
                            existing.Items.Add(new KitchenOrderItemViewModel
                            {
                                MenuItemName = item.MenuItemName,
                                Quantity = item.Quantity,
                                PortionName = item.PortionName,
                                Tags = item.Tags,
                                IsNewAddition = true
                            });
                        }
                    }
                    existing.Status = "Bekliyor (Güncellendi)";
                    RaisePropertyChanged(() => PendingOrders);
                    try { SystemSounds.Exclamation.Play(); } catch { }
                }
            }));
        }

        private void OnClose(string obj)
        {
            EventServiceFactory.EventService.PublishEvent(EventTopicNames.ActivateNavigation);
        }

        private void OnTimerTick(object state)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                LoadKitchenOrders(false);
                foreach (var order in PendingOrders)
                {
                    order.UpdateElapsed();
                }
            }));
        }

        public void LoadKitchenOrders(bool playSoundIfNew)
        {
            var openTickets = _ticketServiceBase.GetOpenTickets(x => !x.IsClosed).ToList();
            var previousCount = PendingOrders.Count;

            var existingIds = PendingOrders.Select(x => x.TicketId).ToList();
            var newTicketIds = openTickets.Select(x => x.Id).ToList();

            // Silinen veya kapananları çıkar
            var toRemove = PendingOrders.Where(x => !newTicketIds.Contains(x.TicketId)).ToList();
            foreach (var r in toRemove) PendingOrders.Remove(r);

            // Yeni eklenenleri yükle
            foreach (var ot in openTickets.Where(x => !existingIds.Contains(x.Id)).OrderBy(x => x.LastOrderDate))
            {
                var orders = _ticketServiceBase.GetOrders(ot.Id).ToList();
                if (orders.Any())
                {
                    var tableName = "";
                    if (ot.TicketEntities != null && ot.TicketEntities.Any())
                    {
                        var firstEntity = ot.TicketEntities.FirstOrDefault();
                        if (firstEntity != null) tableName = firstEntity.EntityName;
                    }

                    var card = new KitchenTicketCardViewModel(ot.Id, ot.TicketNumber, tableName, ot.RemainingAmount, ot.LastOrderDate, orders, OnTicketCompleted, OnPrintTicket);
                    PendingOrders.Add(card);
                }
            }

            // Çevrimdışı / Kapalıyken gelen kuyruktaki siparişleri yükle
            var offlineQueue = KitchenNetworkManager.LoadOfflineQueue();
            foreach (var op in offlineQueue)
            {
                if (!PendingOrders.Any(x => x.TicketId == op.TicketId))
                {
                    var orders = op.Items.Select(i => new Order
                    {
                        MenuItemName = i.MenuItemName,
                        Quantity = i.Quantity,
                        PortionName = i.PortionName
                    }).ToList();

                    var card = new KitchenTicketCardViewModel(op.TicketId, op.TicketNumber, op.TableName, op.TotalAmount, op.CreatedDateTime, orders, OnTicketCompleted, OnPrintTicket);
                    PendingOrders.Insert(0, card);
                }
            }

            TotalPendingCount = PendingOrders.Count;

            if (playSoundIfNew && PendingOrders.Count > previousCount)
            {
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
        }

        private void OnTicketCompleted(KitchenTicketCardViewModel card)
        {
            // Tamamlandığında listeden ve kuyruktan kaldır
            PendingOrders.Remove(card);
            KitchenNetworkManager.RemoveOfflineOrder(card.TicketId);
            TotalPendingCount = PendingOrders.Count;
        }

        private void OnPrintTicket(KitchenTicketCardViewModel card)
        {
            try
            {
                var ticket = _ticketService.OpenTicket(card.TicketId);
                if (ticket != null)
                {
                    var kitchenJob = _cacheService.GetPrintJobByName("Print Orders to Kitchen Printer")
                                  ?? _cacheService.GetPrintJobByName("Mutfak Yazıcısına Yazdır")
                                  ?? _cacheService.GetPrintJobByName("Mutfak Yazıcısına Sipariş Yazdır");

                    if (kitchenJob != null)
                    {
                        _printerService.PrintTicket(ticket, kitchenJob, o => true, true);
                        MessageBox.Show(string.Format("Mutfak bilgi fişi yazdırıldı: {0}", card.TableName), "Mutfak", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Mutfak yazdırma görevi tanımlanmamış. Yönetim > Yazıcılar menüsünden kontrol edebilirsiniz.", "Mutfak", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Fiş yazdırılırken hata oluştu: {0}", ex.Message));
            }
        }
    }
}
