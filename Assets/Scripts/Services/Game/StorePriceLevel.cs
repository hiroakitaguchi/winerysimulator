using System.Collections.Generic;
using UnityEngine;

namespace Supermarket.Services.Game
{
    /// <summary>
    /// How expensive the store looks to passers-by, and what that does to the number of visitors.
    /// The price level is the average of (retail price / standard price) over the products on the shelves:
    /// 1.0 = standard prices, 0.8 = 20% cheaper, 1.5 = 50% dearer.
    /// </summary>
    public static class StorePriceLevel
    {
        // Each 10% below the standard price brings about 15% more visitors, and the reverse above it.
        private const float TrafficSensitivity = 1.5f;
        private const float MinTrafficMultiplier = 0.3f;
        private const float MaxTrafficMultiplier = 1.8f;
        private const float CacheSeconds = 1f;

        private static float _cachedLevel = 1f;
        private static float _cacheTime = -100f;

        public static float GetPriceLevel(IShelfManagerService shelfManager, IRetailPriceService retailPrices)
        {
            if (shelfManager == null || retailPrices == null) return 1f;
            if (Time.time - _cacheTime < CacheSeconds && Time.time >= _cacheTime) return _cachedLevel;

            List<ProductConfig> products = shelfManager.GetStockedProducts();
            float sum = 0f;
            int count = 0;
            foreach (var product in products)
            {
                if (product == null || product.BaseSalePrice <= 0f) continue;
                sum += retailPrices.GetRetailPrice(product.ProductID) / product.BaseSalePrice;
                count++;
            }

            _cachedLevel = count > 0 ? sum / count : 1f;
            _cacheTime = Time.time;
            return _cachedLevel;
        }

        /// <summary>Multiplier applied to the chance that a passer-by enters the store.</summary>
        public static float GetTrafficMultiplier(float priceLevel)
        {
            return Mathf.Clamp(1f + (1f - priceLevel) * TrafficSensitivity, MinTrafficMultiplier, MaxTrafficMultiplier);
        }

        /// <summary>Forces the next query to recalculate (call after prices change).</summary>
        public static void Invalidate()
        {
            _cacheTime = -100f;
        }
    }
}
