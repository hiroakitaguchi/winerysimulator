using System.Collections.Generic;
using UnityEngine;
using Supermarket.Data;
using Supermarket.Interactables;

namespace Supermarket.Services.Game
{
    public interface IShelfManagerService
    {
        /// <summary>
        /// Регистрирует полку в системе для сохранения
        /// </summary>
        void RegisterShelf(ShelfController shelf);
        
        /// <summary>
        /// Убирает полку из системы сохранения
        /// </summary>
        void UnregisterShelf(ShelfController shelf);
        
        /// <summary>
        /// Регистрирует многоуровневую полку в системе для сохранения
        /// </summary>
        void RegisterMultiLevelShelf(MultiLevelShelfController multiShelf);
        
        /// <summary>
        /// Убирает многоуровневую полку из системы сохранения
        /// </summary>
        void UnregisterMultiLevelShelf(MultiLevelShelfController multiShelf);
        
        /// <summary>
        /// Собирает данные всех полок для сохранения
        /// </summary>
        List<ShelfSaveData> GetShelvesSaveData();
        
        /// <summary>
        /// Восстанавливает состояние полок из сохраненных данных
        /// </summary>
        void RestoreShelves(List<ShelfSaveData> shelvesData);
        
        /// <summary>
        /// Очищает все полки (например, при новой игре)
        /// </summary>
        void ClearAllShelves();

        /// <summary>Number of shelves currently placed in the store (single and multi-level).</summary>
        int GetShelfCount();

        /// <summary>
        /// Puts up to <paramref name="quantity"/> items of the product straight onto the shelves:
        /// first where the same product is already displayed, then on empty shelf space.
        /// Returns how many items were actually placed.
        /// </summary>
        int StockProduct(ProductConfig product, int quantity);

        /// <summary>How many items of the product are currently displayed on the shelves.</summary>
        int GetStockCount(ProductConfig product);

        /// <summary>The distinct products currently displayed on the shelves.</summary>
        List<ProductConfig> GetStockedProducts();
    }
} 