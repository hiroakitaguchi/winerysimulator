using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using BehaviourInject;
using Supermarket.Interactables;
using Supermarket.Data;
using Supermarket.Components;

namespace Supermarket.Services.Game
{
    public class CustomerSpawnerService : MonoBehaviour, ICustomerSpawnerService
    {
        [Header("Spawn Configuration")]
        [SerializeField] private GameObject _customerPrefab;
        [SerializeField] private Transform[] _spawnPoints; // Массив точек спавна
        [SerializeField] private float _spawnInterval = 2.5f;
        [SerializeField] private int _maxCustomers = 40;
        
        [Header("Character Appearance")]
        [SerializeField] private CharacterAppearanceConfig _appearanceConfig;
        
        [Header("Customer Configuration")]
        [SerializeField] private float _minCustomerMoney = 50f;
        [SerializeField] private float _maxCustomerMoney = 200f;
        [SerializeField] private int _minItemsInList = 1;
        [SerializeField] private int _maxItemsInList = 5;

        [Header("Price Sensitivity")]
        [Tooltip("Share of customers who must shop today: prices neither keep them out nor stop them buying.")]
        [SerializeField] private float _mustBuyCustomerChance = 0.25f;

        [Header("Cart Customers (bulk shoppers)")]
        [Tooltip("Chance that a new customer is a bulk shopper with a cart.")]
        [SerializeField] private float _cartCustomerChance = 0.2f;
        [SerializeField] private float _minCartCustomerMoney = 300f;
        [SerializeField] private float _maxCartCustomerMoney = 600f;
        [Tooltip("Number of different products a cart customer wants.")]
        [SerializeField] private int _minCartProducts = 4;
        [SerializeField] private int _maxCartProducts = 7;
        [Tooltip("How many of each product a cart customer wants.")]
        [SerializeField] private int _minCartQuantity = 2;
        [SerializeField] private int _maxCartQuantity = 4;
        [Tooltip("Upper limit of items for one cart customer. A cash desk belt holds 24 items.")]
        [SerializeField] private int _maxCartTotalItems = 22;
        
        [Header("Navigation Settings")]
        [SerializeField] private int _minAvoidancePriority = 30;
        [SerializeField] private int _maxAvoidancePriority = 70;
        [SerializeField] private bool _useUniqueAvoidancePriority = false; // Если true, каждый клиент получает уникальный приоритет
        
        private static int _nextUniqueAvoidancePriority = 30; // Счетчик для уникальных приоритетов
        
        private bool _isSpawning = false;
        private List<GameObject> _activeCustomers = new List<GameObject>();
        private Coroutine _spawnCoroutine;
        
        // События
        public event System.Action<GameObject> OnCustomerSpawned;
        public event System.Action<GameObject> OnCustomerLeft;
        
        // Зависимости
        private IProductCatalogService _productCatalogService;
        private IStorePointsService _storePointsService;
        private ICustomerManagerService _customerManagerService;
        
        [Inject]
        public void Construct(IProductCatalogService productCatalogService, IStorePointsService storePointsService, ICustomerManagerService customerManagerService)
        {
            _productCatalogService = productCatalogService;
            _storePointsService = storePointsService;
            _customerManagerService = customerManagerService;
        }
        
        public bool IsSpawning => _isSpawning;
        
        public void StartSpawning()
        {
            if (_isSpawning) return;
            
            if (_customerPrefab == null || _spawnPoints == null || _spawnPoints.Length == 0)
            {
                Debug.LogError("CustomerSpawnerService: Customer prefab or spawn points not set!");
                return;
            }
            
            _isSpawning = true;
            _spawnCoroutine = StartCoroutine(SpawnRoutine());
            Debug.Log($"CustomerSpawnerService: Started spawning customers with {_spawnPoints.Length} spawn points");
        }
        
        public void StopSpawning()
        {
            if (!_isSpawning) return;
            
            _isSpawning = false;
            if (_spawnCoroutine != null)
            {
                StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }
            Debug.Log("CustomerSpawnerService: Stopped spawning customers");
        }
        
        public void SetSpawnInterval(float intervalInSeconds)
        {
            _spawnInterval = Mathf.Max(1f, intervalInSeconds);
        }
        
        public void SetMaxCustomers(int maxCustomers)
        {
            _maxCustomers = Mathf.Max(1, maxCustomers);
        }
        
        public int GetActiveCustomerCount()
        {
            // Очистка null-ссылок (если покупатель был уничтожен извне)
            _activeCustomers.RemoveAll(c => c == null);
            return _activeCustomers.Count;
        }
        
        public CharacterAppearanceConfig GetCharacterAppearanceConfig()
        {
            return _appearanceConfig;
        }
        
        public void AddSpawnPoint(Transform spawnPoint)
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("CustomerSpawnerService: Cannot add null spawn point");
                return;
            }
            
            if (_spawnPoints == null)
            {
                _spawnPoints = new Transform[] { spawnPoint };
            }
            else
            {
                var newArray = new Transform[_spawnPoints.Length + 1];
                _spawnPoints.CopyTo(newArray, 0);
                newArray[_spawnPoints.Length] = spawnPoint;
                _spawnPoints = newArray;
            }
            
            Debug.Log($"CustomerSpawnerService: Added spawn point '{spawnPoint.name}'. Total spawn points: {_spawnPoints.Length}");
        }
        
        public void RemoveSpawnPoint(Transform spawnPoint)
        {
            if (spawnPoint == null || _spawnPoints == null)
                return;
                
            var spawnPointsList = new List<Transform>(_spawnPoints);
            if (spawnPointsList.Remove(spawnPoint))
            {
                _spawnPoints = spawnPointsList.ToArray();
                Debug.Log($"CustomerSpawnerService: Removed spawn point '{spawnPoint.name}'. Remaining spawn points: {_spawnPoints.Length}");
            }
        }
        
        public void SetSpawnPoints(Transform[] spawnPoints)
        {
            _spawnPoints = spawnPoints;
            int count = spawnPoints?.Length ?? 0;
            Debug.Log($"CustomerSpawnerService: Set {count} spawn points");
        }
        
        public int GetSpawnPointCount()
        {
            return _spawnPoints?.Length ?? 0;
        }
        
        public GameObject GetCustomerPrefab()
        {
            return _customerPrefab;
        }
        
        public void RegisterRestoredCustomer(GameObject customerObj)
        {
            if (customerObj == null)
            {
                Debug.LogWarning("CustomerSpawnerService: Attempted to register null restored customer");
                return;
            }
            
            if (!_activeCustomers.Contains(customerObj))
            {
                _activeCustomers.Add(customerObj);
                
                // Подписываемся на событие ухода для восстановленного покупателя
                CustomerController customerController = customerObj.GetComponent<CustomerController>();
                if (customerController != null)
                {
                    customerController.OnCustomerLeaving += HandleCustomerLeaving;
                }
                
                Debug.Log($"CustomerSpawnerService: Registered restored customer. Total active customers: {_activeCustomers.Count}");
            }
        }
        
        private IEnumerator SpawnRoutine()
        {
            while (_isSpawning)
            {
                if (GetActiveCustomerCount() < _maxCustomers)
                {
                    SpawnCustomer();
                }
                
                yield return new WaitForSeconds(_spawnInterval);
            }
        }
        
        private void SpawnCustomer()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                Debug.LogError("CustomerSpawnerService: Spawn points not set!");
                return;
            }
            
            // Рандомный выбор точки спавна
            Transform selectedSpawnPoint = _spawnPoints[Random.Range(0, _spawnPoints.Length)];
            if (selectedSpawnPoint == null)
            {
                Debug.LogError("CustomerSpawnerService: Selected spawn point is null!");
                return;
            }
            
            GameObject customerObj = Instantiate(_customerPrefab, selectedSpawnPoint.position, selectedSpawnPoint.rotation);
            
            Debug.Log($"CustomerSpawnerService: Spawned customer at spawn point '{selectedSpawnPoint.name}' ({selectedSpawnPoint.position})");
            
            // Применяем внешность персонажа
            CharacterAppearance appearance = customerObj.GetComponent<CharacterAppearance>();
            if (appearance == null)
            {
                appearance = customerObj.AddComponent<CharacterAppearance>();
            }
            
            CharacterAppearanceConfig.GenderModel selectedModel = null;
            
            if (_appearanceConfig != null)
            {
                selectedModel = _appearanceConfig.GetRandomGenderModel();
            }
            
            // Генерация данных покупателя с учетом пола
            CustomerData customerData = GenerateRandomCustomerData(selectedModel);
            
            // Применяем внешность
            if (appearance != null && selectedModel != null)
            {
                appearance.ApplyAppearance(selectedModel, customerData);
                
                // Обновляем ссылку на Animator в CustomerLocomotion
                CustomerLocomotion locomotion = customerObj.GetComponent<CustomerLocomotion>();
                if (locomotion != null)
                {
                    locomotion.SetWalkSpeed(customerData.MoveSpeed);
                    locomotion.UpdateAnimatorReference();
                }
            }
            
            // Инициализация компонента покупателя (если есть)
            CustomerController customerController = customerObj.GetComponent<CustomerController>();
            if (customerController != null)
            {
                customerController.Initialize(customerData);
                customerController.OnCustomerLeaving += HandleCustomerLeaving;
                
                // Регистрируем покупателя в CustomerManagerService
                if (_customerManagerService != null)
                {
                    _customerManagerService.RegisterCustomer(customerController);
                }
            }
            
            // Настройка навигации (приоритет avoidance)
            CustomerLocomotion customerLocomotion = customerObj.GetComponent<CustomerLocomotion>();
            if (customerLocomotion != null)
            {
                int avoidancePriority = GenerateAvoidancePriority();
                customerLocomotion.SetAvoidancePriority(avoidancePriority);
            }
            
            _activeCustomers.Add(customerObj);
            OnCustomerSpawned?.Invoke(customerObj);
            
            Debug.Log($"Spawned customer: {customerData.CustomerName} ({customerData.Gender}) with {customerData.ShoppingList.Count} items to buy");
        }
        
        private CustomerData GenerateRandomCustomerData(CharacterAppearanceConfig.GenderModel genderModel = null)
        {
            string customerName = "Customer";
            CharacterAppearanceConfig.Gender gender = CharacterAppearanceConfig.Gender.Male;
            Color topColor = Color.white;
            Color bottomColor = Color.white;
            Color shoesColor = Color.white;
            
            // Если есть модель, используем её данные
            if (genderModel != null)
            {
                customerName = genderModel.GetRandomName();
                gender = genderModel.gender;
                topColor = genderModel.topClothing.GetRandomColor();
                bottomColor = genderModel.bottomClothing.GetRandomColor();
                shoesColor = genderModel.shoes.GetRandomColor();
            }
            else
            {
                // Fallback на старые имена если конфиг не настроен
                string[] names = { "太郎", "花子", "健", "美咲", "大輔", "陽子", "翔", "恵" };
                customerName = names[Random.Range(0, names.Length)];
            }
            
            bool usesCart = Random.value < _cartCustomerChance;
            float randomMoney = usesCart
                ? Random.Range(_minCartCustomerMoney, _maxCartCustomerMoney)
                : Random.Range(_minCustomerMoney, _maxCustomerMoney);
            
            CustomerData data = new CustomerData(customerName, randomMoney);
            data.UsesCart = usesCart;
            data.MustBuy = Random.value < _mustBuyCustomerChance;
            data.Gender = gender;
            data.TopClothingColor = topColor;
            data.BottomClothingColor = bottomColor;
            data.ShoesColor = shoesColor;

            // Body size and walking speed vary per customer, within the ranges set in the appearance config.
            if (_appearanceConfig != null)
            {
                data.HeightScale = Random.Range(_appearanceConfig.heightScaleRange.x, _appearanceConfig.heightScaleRange.y);
                data.BuildScale = Random.Range(_appearanceConfig.buildScaleRange.x, _appearanceConfig.buildScaleRange.y);
                data.MoveSpeed = Random.Range(_appearanceConfig.walkSpeedRange.x, _appearanceConfig.walkSpeedRange.y);
            }
            
            // Генерация списка покупок
            if (_productCatalogService != null)
            {
                var allProducts = _productCatalogService.GetAllProductConfigs();
                var shelfProducts = new List<ProductConfig>();
                
                // Фильтруем только товары, которые можно размещать на полках
                foreach (var product in allProducts)
                {
                    if (product.CanBePlacedOnShelf)
                    {
                        shelfProducts.Add(product);
                    }
                }
                
                if (shelfProducts.Count > 0)
                {
                    int itemCount = usesCart
                        ? Random.Range(_minCartProducts, _maxCartProducts + 1)
                        : Random.Range(_minItemsInList, _maxItemsInList + 1);
                    itemCount = Mathf.Min(itemCount, shelfProducts.Count);
                    int remainingCartItems = _maxCartTotalItems;
                    
                    // Случайно выбираем товары
                    List<ProductConfig> selectedProducts = new List<ProductConfig>();
                    List<ProductConfig> availableProducts = new List<ProductConfig>(shelfProducts);
                    
                    for (int i = 0; i < itemCount; i++)
                    {
                        if (availableProducts.Count == 0) break;
                        
                        int randomIndex = Random.Range(0, availableProducts.Count);
                        ProductConfig selectedProduct = availableProducts[randomIndex];
                        availableProducts.RemoveAt(randomIndex);
                        
                        int quantity = Random.Range(1, 4); // 1-3 единицы товара
                        if (usesCart)
                        {
                            if (remainingCartItems <= 0) break;
                            quantity = Mathf.Min(Random.Range(_minCartQuantity, _maxCartQuantity + 1), remainingCartItems);
                            remainingCartItems -= quantity;
                        }
                        data.ShoppingList.Add(new ShoppingItem(selectedProduct, quantity));
                    }
                }
            }
            
            return data;
        }
        
        private void HandleCustomerLeaving(GameObject customerObj)
        {
            // Отменяем регистрацию в CustomerManagerService
            if (_customerManagerService != null)
            {
                CustomerController customerController = customerObj.GetComponent<CustomerController>();
                if (customerController != null)
                {
                    _customerManagerService.UnregisterCustomer(customerController);
                }
            }
            
            _activeCustomers.Remove(customerObj);
            OnCustomerLeft?.Invoke(customerObj);
        }
        
        /// <summary>
        /// Генерирует приоритет obstacle avoidance для нового клиента
        /// </summary>
        private int GenerateAvoidancePriority()
        {
            if (_useUniqueAvoidancePriority)
            {
                // Уникальный приоритет для каждого клиента
                int priority = _nextUniqueAvoidancePriority++;
                
                // Если достигли максимального значения, сбрасываем
                if (_nextUniqueAvoidancePriority > _maxAvoidancePriority)
                {
                    _nextUniqueAvoidancePriority = _minAvoidancePriority;
                }
                
                return Mathf.Clamp(priority, _minAvoidancePriority, _maxAvoidancePriority);
            }
            else
            {
                // Случайный приоритет в диапазоне
                return Random.Range(_minAvoidancePriority, _maxAvoidancePriority + 1);
            }
        }
        
        void OnDestroy()
        {
            StopSpawning();
        }
    }
} 