using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Linq;
using BehaviourInject;
using Supermarket.Interactables; // Для BoxController
using Supermarket.Services.UI; // Для INotificationService
using Supermarket.Services.Game; // Для IBoxManagerService
using Supermarket.Data;

namespace Supermarket.Services.Game
{
    public class DeliveryService : MonoBehaviour, IDeliveryService
    {
        [Header("Delivery Configuration")]
        [SerializeField] private Transform _deliveryPoint; // Точка, куда будут доставляться коробки
        [SerializeField] private float _spawnOffsetRadius = 1.0f; // Небольшой разброс при спавне коробок
        [SerializeField] private float _defaultDeliveryTimeMinutes = 3f; // Время доставки по умолчанию
        [SerializeField] private float _imminentWarningTimeMinutes = 1f; // За сколько минут предупреждать

        [Inject] public INotificationService _notificationService;
        [Inject] public IBoxManagerService _boxManagerService;
        [Inject] public IPlayerDataService _playerDataService;
        [Inject] public IGameConfigService _gameConfigService;
        [Inject] public IShelfManagerService _shelfManagerService;

        // Очередь активных заказов
        private List<OrderSaveData> _activeOrders = new List<OrderSaveData>();
        private HashSet<string> _imminentNotificationsSent = new HashSet<string>();
        
        // Счетчик для простых номеров заказов
        private static int _orderCounter = 0;
        
        // События
        public event Action<OrderSaveData> OnOrderPlaced;
        public event Action<OrderSaveData> OnOrderDelivered;
        public event Action<OrderSaveData> OnOrderCancelled;
        public event Action<OrderSaveData> OnDeliveryImminent;

        [Inject]
        public void Construct()
        {
            if (_deliveryPoint == null)
            {
                Debug.LogError("DeliveryService: Delivery Point is not assigned in the Inspector!", this);
            }
        }

        void Update()
        {
            ProcessActiveOrders();
        }

        /// <summary>
        /// Старый метод немедленной доставки (для обратной совместимости)
        /// </summary>
        public void DeliverBoxes(Dictionary<ProductConfig, int> orderedProducts)
        {
            if (_deliveryPoint == null || _boxManagerService == null)
            {
                Debug.LogError("DeliveryService: Cannot deliver boxes. Delivery Point or BoxManagerService is not available.");
                return;
            }

            if (orderedProducts == null || orderedProducts.Count == 0)
            {
                Debug.LogWarning("DeliveryService: No products to deliver.");
                return;
            }

            Debug.Log($"DeliveryService: Starting immediate delivery of {orderedProducts.Count} types of products to {_deliveryPoint.name}.");
            
            // Используем новый метод для правильного создания коробок
            CreateBoxesFromOrder(orderedProducts);
            
            // Подсчитываем общее количество коробок для уведомления
            int totalBoxes = 0;
            foreach (var orderEntry in orderedProducts)
            {
                ProductConfig product = orderEntry.Key;
                int totalQuantityToDeliver = orderEntry.Value;

                if (totalQuantityToDeliver <= 0) continue;

                int itemsPerBox = product.ItemsPerBox;
                int fullBoxes = totalQuantityToDeliver / itemsPerBox;
                int remainingItems = totalQuantityToDeliver % itemsPerBox;
                
                totalBoxes += fullBoxes;
                if (remainingItems > 0) totalBoxes++;
            }

            // Показываем уведомление о завершении доставки
            if (_notificationService != null && totalBoxes > 0)
            {
                string message = totalBoxes == 1 ? 
                    "商品が1箱届きました" : 
                    $"商品が{totalBoxes}箱届きました";
                    
                _notificationService.ShowNotification(message, NotificationType.Success, 3.0f);
            }
            
            Debug.Log($"DeliveryService: Immediate delivery completed. Total boxes delivered: {totalBoxes}");
        }

        /// <summary>
        /// Новый метод заказа с отложенной доставкой
        /// </summary>
        public string PlaceOrder(Dictionary<ProductConfig, int> orderedProducts)
        {
            if (orderedProducts == null || orderedProducts.Count == 0)
            {
                Debug.LogWarning("DeliveryService: Cannot place order - no products specified.");
                return null;
            }

            // Создаем новый заказ
            // Goods go straight onto free shelf space; only what does not fit is delivered in boxes.
            orderedProducts = StockShelvesFromOrder(orderedProducts);
            if (orderedProducts.Count == 0)
            {
                return ShelvedOrderId;
            }

            string orderId = GenerateOrderId();
            var order = new OrderSaveData
            {
                OrderId = orderId,
                OrderTime = DateTime.Now,
                DeliveryTime = _defaultDeliveryTimeMinutes * 60f, // Конвертируем в секунды
                Status = OrderStatus.InTransit,
                Items = new List<OrderItemData>(),
                TotalCost = 0f
            };

            // Заполняем товары и рассчитываем стоимость
            foreach (var entry in orderedProducts)
            {
                var orderItem = new OrderItemData
                {
                    ProductType = entry.Key.ProductID,
                    Quantity = entry.Value,
                    PricePerUnit = entry.Key.PurchasePrice
                };
                
                order.Items.Add(orderItem);
                order.TotalCost += entry.Value * entry.Key.PurchasePrice;
            }

            // Добавляем в очередь активных заказов
            _activeOrders.Add(order);

            // Уведомляем о размещении заказа
            _notificationService?.ShowNotification(
                $"注文 #{orderId.Replace("ORDER_", "")} を受け付けました。{_defaultDeliveryTimeMinutes:F1}分後に届きます",
                NotificationType.Success,
                4f
            );

            // Вызываем событие
            OnOrderPlaced?.Invoke(order);

            Debug.Log($"DeliveryService: Order {orderId} placed. Delivery in {_defaultDeliveryTimeMinutes} minutes. Total cost: {order.TotalCost:F2}");
            
            return orderId;
        }

        /// <summary>
        /// Отменить заказ (возможно с частичным возвратом средств)
        /// </summary>
        // Returned by PlaceOrder when the whole order went onto the shelves and nothing is in transit.
        public const string ShelvedOrderId = "SHELVED";

        /// <summary>
        /// Stocks ordered goods directly onto the shelves and returns what is left to deliver in boxes.
        /// </summary>
        private Dictionary<ProductConfig, int> StockShelvesFromOrder(Dictionary<ProductConfig, int> orderedProducts)
        {
            var remaining = new Dictionary<ProductConfig, int>();
            int shelved = 0;

            foreach (var entry in orderedProducts)
            {
                if (entry.Key == null || entry.Value <= 0) continue;

                int placed = _shelfManagerService != null ? _shelfManagerService.StockProduct(entry.Key, entry.Value) : 0;
                shelved += placed;
                if (entry.Value - placed > 0)
                {
                    remaining[entry.Key] = entry.Value - placed;
                }
            }

            if (shelved > 0)
            {
                string message = remaining.Count == 0
                    ? $"{shelved}個を棚に陳列しました"
                    : $"{shelved}個を棚に陳列しました。棚に入らない分はダンボールで届きます";
                _notificationService?.ShowNotification(message, NotificationType.Success, 4f);
            }

            return remaining;
        }

        public float CancelOrder(string orderId, float refundPercentage = 0.5f)
        {
            var order = _activeOrders.FirstOrDefault(o => o.OrderId == orderId);
            if (order == null)
            {
                Debug.LogWarning($"DeliveryService: Cannot cancel order {orderId} - not found.");
                return 0f;
            }

            if (order.Status != OrderStatus.InTransit)
            {
                Debug.LogWarning($"DeliveryService: Cannot cancel order {orderId} - status is {order.Status}.");
                return 0f;
            }

            // Рассчитываем возврат
            float refundAmount = order.TotalCost * Mathf.Clamp01(refundPercentage);
            
            // Возвращаем деньги игроку
            if (_playerDataService != null && refundAmount > 0)
            {
                _playerDataService.AdjustMoney(refundAmount);
                _playerDataService.SaveData();
            }

            // Обновляем статус заказа
            order.Status = OrderStatus.Cancelled;
            
            // Убираем из активных заказов
            _activeOrders.Remove(order);
            _imminentNotificationsSent.Remove(orderId);

            // Уведомляем
            _notificationService?.ShowNotification(
                $"注文 #{orderId.Replace("ORDER_", "")} を取り消しました。返金: ${refundAmount:F0}",
                NotificationType.Warning,
                4f
            );

            // Вызываем событие
            OnOrderCancelled?.Invoke(order);

            Debug.Log($"DeliveryService: Order {orderId} cancelled. Refund: {refundAmount:F2}");
            
            return refundAmount;
        }

        /// <summary>
        /// Получить все активные заказы
        /// </summary>
        public List<OrderSaveData> GetActiveOrders()
        {
            return new List<OrderSaveData>(_activeOrders);
        }

        /// <summary>
        /// Получить заказ по ID
        /// </summary>
        public OrderSaveData GetOrder(string orderId)
        {
            return _activeOrders.FirstOrDefault(o => o.OrderId == orderId);
        }

        /// <summary>
        /// Обрабатывает активные заказы (уменьшает время, доставляет по готовности)
        /// </summary>
        private void ProcessActiveOrders()
        {
            if (_activeOrders.Count == 0) return;

            var ordersToProcess = new List<OrderSaveData>(_activeOrders);
            
            foreach (var order in ordersToProcess)
            {
                if (order.Status != OrderStatus.InTransit) continue;

                // Уменьшаем время доставки
                order.DeliveryTime -= Time.deltaTime;

                // Проверяем на предупреждение за минуту
                float remainingMinutes = order.DeliveryTime / 60f;
                if (remainingMinutes <= _imminentWarningTimeMinutes && 
                    !_imminentNotificationsSent.Contains(order.OrderId))
                {
                    _notificationService?.ShowNotification(
                        $"🚚 注文 #{order.OrderId.Replace("ORDER_", "")} がまもなく届きます！",
                        NotificationType.Info,
                        3f
                    );
                    
                    _imminentNotificationsSent.Add(order.OrderId);
                    OnDeliveryImminent?.Invoke(order);
                }

                // Проверяем готовность к доставке
                if (order.DeliveryTime <= 0)
                {
                    ProcessOrderDelivery(order);
                }
            }
        }

        /// <summary>
        /// Обрабатывает доставку готового заказа
        /// </summary>
        private void ProcessOrderDelivery(OrderSaveData order)
        {
            Debug.Log($"DeliveryService: Processing delivery for order {order.OrderId}");

            // Конвертируем заказ обратно в Dictionary для старого метода
            var orderedProducts = new Dictionary<ProductConfig, int>();
            
            foreach (var item in order.Items)
            {
                // Получаем ProductConfig по ID
                var productConfig = GetProductConfigById(item.ProductType);
                if (productConfig != null)
                {
                    orderedProducts[productConfig] = item.Quantity;
                }
            }

            // Используем старый метод для создания коробок
            if (orderedProducts.Count > 0)
            {
                CreateBoxesFromOrder(orderedProducts);
                
                // Уведомляем о доставке
                _notificationService?.ShowNotification(
                    $"🚚 注文 #{order.OrderId.Replace("ORDER_", "")} が届きました！",
                    NotificationType.Success,
                    4f
                );
            }

            // Обновляем статус и убираем из активных
            order.Status = OrderStatus.Delivered;
            _activeOrders.Remove(order);
            _imminentNotificationsSent.Remove(order.OrderId);

            // Вызываем событие
            OnOrderDelivered?.Invoke(order);
        }

        /// <summary>
        /// Создает коробки из заказа с учетом ItemsPerBox
        /// </summary>
        private void CreateBoxesFromOrder(Dictionary<ProductConfig, int> orderedProducts)
        {
            int totalBoxes = 0;
            
            foreach (var orderEntry in orderedProducts)
            {
                ProductConfig product = orderEntry.Key;
                int totalQuantityToDeliver = orderEntry.Value;

                if (totalQuantityToDeliver <= 0) continue;

                // Рассчитываем количество коробок и распределяем товары
                int itemsPerBox = product.ItemsPerBox;
                int fullBoxes = totalQuantityToDeliver / itemsPerBox;
                int remainingItems = totalQuantityToDeliver % itemsPerBox;

                // Создаем полные коробки
                for (int i = 0; i < fullBoxes; i++)
                {
                    Vector3 spawnPosition = _deliveryPoint.position + UnityEngine.Random.insideUnitSphere * _spawnOffsetRadius;
                    spawnPosition.y = _deliveryPoint.position.y;

                    BoxData boxData = new BoxData(product, itemsPerBox);
                    _boxManagerService.CreateBox(boxData, spawnPosition, false);
                    totalBoxes++;

                    Debug.Log($"DeliveryService: Created full box with {itemsPerBox}x {product.ProductName} at delivery point.");
                }

                // Создаем частично заполненную коробку, если есть остаток
                if (remainingItems > 0)
                {
                    Vector3 spawnPosition = _deliveryPoint.position + UnityEngine.Random.insideUnitSphere * _spawnOffsetRadius;
                    spawnPosition.y = _deliveryPoint.position.y;

                    BoxData boxData = new BoxData(product, remainingItems);
                    _boxManagerService.CreateBox(boxData, spawnPosition, false);
                    totalBoxes++;

                    Debug.Log($"DeliveryService: Created partial box with {remainingItems}x {product.ProductName} at delivery point.");
                }
            }
            
            Debug.Log($"DeliveryService: Created {totalBoxes} boxes from scheduled delivery.");
        }

        /// <summary>
        /// Получает ProductConfig по ID
        /// </summary>
        private ProductConfig GetProductConfigById(string productId)
        {
            if (_gameConfigService != null)
            {
                return _gameConfigService.GetProductByID(productId);
            }
            
            Debug.LogWarning($"DeliveryService: GameConfigService is null, could not find ProductConfig for ID: {productId}");
            return null;
        }

        /// <summary>
        /// Генерирует уникальный ID заказа
        /// </summary>
        private string GenerateOrderId()
        {
            return $"ORDER_{_orderCounter++}";
        }

        /// <summary>
        /// Метод для загрузки активных заказов из сохранения
        /// </summary>
        public void LoadActiveOrders(List<OrderSaveData> orders)
        {
            _activeOrders.Clear();
            _imminentNotificationsSent.Clear();
            
            if (orders != null)
            {
                _activeOrders.AddRange(orders.Where(o => o.Status == OrderStatus.InTransit));
                
                // Обновляем счетчик заказов на основе загруженных ID, чтобы избежать конфликтов
                UpdateOrderCounterFromLoadedOrders(orders);
                
                Debug.Log($"DeliveryService: Loaded {_activeOrders.Count} active orders from save data.");
            }
        }

        /// <summary>
        /// Обновляет статический счетчик заказов на основе загруженных заказов
        /// </summary>
        private void UpdateOrderCounterFromLoadedOrders(List<OrderSaveData> orders)
        {
            int maxOrderNumber = 0;
            
            foreach (var order in orders)
            {
                // Извлекаем номер из ID вида "ORDER_X"
                if (order.OrderId.StartsWith("ORDER_"))
                {
                    string numberPart = order.OrderId.Replace("ORDER_", "");
                    if (int.TryParse(numberPart, out int orderNumber))
                    {
                        maxOrderNumber = Mathf.Max(maxOrderNumber, orderNumber);
                    }
                }
            }
            
            // Устанавливаем счетчик на следующий номер после максимального загруженного
            _orderCounter = maxOrderNumber + 1;
            
            Debug.Log($"DeliveryService: Updated order counter to {_orderCounter} based on loaded orders (max found: {maxOrderNumber})");
        }

        /// <summary>
        /// Метод для получения данных заказов для сохранения
        /// </summary>
        public List<OrderSaveData> GetOrdersForSaving()
        {
            return new List<OrderSaveData>(_activeOrders);
        }
    }
} 