using System.Collections.Generic;
using UnityEngine;

namespace Supermarket.Components
{
    /// <summary>
    /// A shopping cart pushed by a customer who buys in bulk. It follows the customer as a child object and
    /// shows the items collected so far. The look comes from the prefab at Resources/Props/ShoppingCartModel;
    /// if that prefab is missing, a simple cart is built from primitive shapes instead.
    /// </summary>
    public class ShoppingCart : MonoBehaviour
    {
        private const string ModelResourcePath = "Props/ShoppingCartModel";

        // Basket of the cart model: usable inner area for items (metres, cart-local; the handle is at -Z).
        private const float ModelBasketWidth = 0.30f;
        private const float ModelBasketLength = 0.50f;
        private const float ModelBasketFloorY = 0.66f;
        private const float ModelBasketCenterZ = 0.13f;

        // Basket of the fallback cart built from primitives.
        private const float BasketWidth = 0.46f;
        private const float BasketLength = 0.70f;
        private const float BasketHeight = 0.32f;
        private const float BasketFloorY = 0.52f;
        private const int Columns = 3;
        private const int Rows = 4;
        private const int MaxVisibleItems = Columns * Rows * 2; // two layers
        private const float ItemScale = 0.8f;
        private const float MoveLerpSpeed = 6f;

        // In front of the customer while walking; beside them while standing at a shelf, in a queue or at a desk,
        // so the cart does not poke into whatever the customer is facing.
        private static readonly Vector3 PushingPosition = new Vector3(0f, 0f, 0.72f);

        // Where the hands hold the handle (cart-local; mirrored on X for the left hand), and how fast the
        // arms move between the walking animation and the pushing pose.
        private static readonly Vector3 GripPoint = new Vector3(0.2f, 0.97f, -0.40f);
        private const float PoseBlendSpeed = 5f;
        private static readonly Vector3 ParkedPosition = new Vector3(0.62f, 0f, -0.1f);

        private static Material _frameMaterial;
        private static Material _handleMaterial;

        private readonly List<GameObject> _items = new List<GameObject>();
        private Transform _itemsRoot;
        private bool _parked;

        // Pushing pose: the customer's arms are bent onto the handle after the animation has run.
        private Animator _animator;
        private Transform _leftUpperArm, _leftLowerArm, _leftHand, _rightUpperArm, _rightLowerArm, _rightHand;
        private float _pushWeight;
        // Area the items are laid out in; set by whichever cart look was built.
        private float _itemAreaWidth = BasketWidth;
        private float _itemAreaLength = BasketLength;

        public int ItemCount => _items.Count;

        public static ShoppingCart Create(Transform customer)
        {
            var cartObject = new GameObject("ShoppingCart");
            cartObject.transform.SetParent(customer, false);
            cartObject.transform.localPosition = PushingPosition;
            var cart = cartObject.AddComponent<ShoppingCart>();
            cart.Build();
            return cart;
        }

        public void SetParked(bool parked)
        {
            _parked = parked;
        }

        private void Update()
        {
            Vector3 target = _parked ? ParkedPosition : PushingPosition;
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * MoveLerpSpeed);
        }

        private void LateUpdate()
        {
            // Hands go to the handle while pushing and return to the normal animation while the cart is parked.
            _pushWeight = Mathf.MoveTowards(_pushWeight, _parked ? 0f : 1f, Time.deltaTime * PoseBlendSpeed);
            if (_pushWeight <= 0.001f) return;
            if (!EnsureArmBones()) return;

            Vector3 rightGrip = transform.TransformPoint(GripPoint);
            Vector3 leftGrip = transform.TransformPoint(new Vector3(-GripPoint.x, GripPoint.y, GripPoint.z));
            Transform body = transform.parent != null ? transform.parent : transform;

            ReachFor(_rightUpperArm, _rightLowerArm, _rightHand, rightGrip, body, 1f);
            ReachFor(_leftUpperArm, _leftLowerArm, _leftHand, leftGrip, body, -1f);
        }

        private bool EnsureArmBones()
        {
            if (_animator == null || _rightHand == null)
            {
                _animator = transform.parent != null ? transform.parent.GetComponentInChildren<Animator>() : null;
                if (_animator == null || !_animator.isHuman) return false;

                _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
            return _leftUpperArm != null && _leftLowerArm != null && _leftHand != null &&
                   _rightUpperArm != null && _rightLowerArm != null && _rightHand != null;
        }

        // Two-bone reach: bends shoulder and elbow so the hand lands on the grip, keeping the elbow down and slightly out.
        private void ReachFor(Transform upper, Transform lower, Transform hand, Vector3 grip, Transform body, float side)
        {
            Vector3 target = Vector3.Lerp(hand.position, grip, _pushWeight);

            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float upperLength = Vector3.Distance(a, b);
            float lowerLength = Vector3.Distance(b, c);
            if (upperLength < 0.001f || lowerLength < 0.001f) return;

            float targetDistance = Mathf.Clamp(Vector3.Distance(a, target), 0.01f, (upperLength + lowerLength) * 0.999f);

            float shoulderNow = Vector3.Angle(c - a, b - a);
            float elbowNow = Vector3.Angle(a - b, c - b);
            float shoulderWanted = Mathf.Acos(Mathf.Clamp((lowerLength * lowerLength - upperLength * upperLength - targetDistance * targetDistance) / (-2f * upperLength * targetDistance), -1f, 1f)) * Mathf.Rad2Deg;
            float elbowWanted = Mathf.Acos(Mathf.Clamp((targetDistance * targetDistance - upperLength * upperLength - lowerLength * lowerLength) / (-2f * upperLength * lowerLength), -1f, 1f)) * Mathf.Rad2Deg;

            // Bend in the plane the arm is already bent in (or downwards if it is perfectly straight).
            Vector3 bendAxis = Vector3.Cross(c - a, b - a);
            if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = Vector3.Cross(c - a, -body.up);
            bendAxis.Normalize();

            upper.rotation = Quaternion.AngleAxis(shoulderWanted - shoulderNow, bendAxis) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis(elbowWanted - elbowNow, bendAxis) * lower.rotation;

            // Swing the whole arm so the hand points at the target.
            upper.rotation = Quaternion.FromToRotation(hand.position - upper.position, target - upper.position) * upper.rotation;

            // Roll the arm around the shoulder-hand line so the elbow hangs down and a little outwards.
            Vector3 armAxis = (hand.position - upper.position).normalized;
            Vector3 elbowNowDir = Vector3.ProjectOnPlane(lower.position - upper.position, armAxis);
            Vector3 elbowWantedDir = Vector3.ProjectOnPlane(-body.up + body.right * (0.35f * side) - body.forward * 0.2f, armAxis);
            if (elbowNowDir.sqrMagnitude > 1e-6f && elbowWantedDir.sqrMagnitude > 1e-6f)
            {
                float roll = Vector3.SignedAngle(elbowNowDir, elbowWantedDir, armAxis) * _pushWeight;
                upper.rotation = Quaternion.AngleAxis(roll, armAxis) * upper.rotation;
            }
        }

        public void AddItem(ProductConfig product)
        {
            if (product == null || product.Prefab == null || _items.Count >= MaxVisibleItems) return;

            int index = _items.Count;
            int layer = index / (Columns * Rows);
            int slot = index % (Columns * Rows);
            int column = slot % Columns;
            int row = slot / Columns;

            float x = (column - (Columns - 1) * 0.5f) * (_itemAreaWidth / Columns);
            float z = (row - (Rows - 1) * 0.5f) * (_itemAreaLength / Rows);
            float y = 0.02f + layer * 0.12f;

            GameObject item = Instantiate(product.Prefab, _itemsRoot);
            item.transform.localPosition = new Vector3(x, y, z);
            item.transform.localRotation = Quaternion.Euler(0f, (index * 47) % 360, 0f);
            item.transform.localScale = product.Prefab.transform.localScale * ItemScale;

            // Items in the cart are decoration only: they must not block movement or be clickable.
            foreach (var itemCollider in item.GetComponentsInChildren<Collider>()) Destroy(itemCollider);
            foreach (var body in item.GetComponentsInChildren<Rigidbody>()) Destroy(body);

            _items.Add(item);
        }

        public void RemoveItem()
        {
            if (_items.Count == 0) return;
            GameObject last = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            if (last != null) Destroy(last);
        }

        private void Build()
        {
            GameObject modelPrefab = Resources.Load<GameObject>(ModelResourcePath);
            if (modelPrefab != null)
            {
                GameObject model = Instantiate(modelPrefab, transform);
                model.name = "Model";
                foreach (var modelCollider in model.GetComponentsInChildren<Collider>()) Destroy(modelCollider);

                _itemAreaWidth = ModelBasketWidth;
                _itemAreaLength = ModelBasketLength;
                var modelItemsRoot = new GameObject("Items");
                modelItemsRoot.transform.SetParent(transform, false);
                modelItemsRoot.transform.localPosition = new Vector3(0f, ModelBasketFloorY, ModelBasketCenterZ);
                _itemsRoot = modelItemsRoot.transform;
                return;
            }

            EnsureMaterials();

            const float wall = 0.02f;
            float halfW = BasketWidth * 0.5f + wall;
            float halfL = BasketLength * 0.5f + wall;
            float midY = BasketFloorY + BasketHeight * 0.5f;

            // Basket: floor and four low walls
            Part(PrimitiveType.Cube, new Vector3(0f, BasketFloorY - wall * 0.5f, 0f), new Vector3(BasketWidth + wall * 2f, wall, BasketLength + wall * 2f), _frameMaterial);
            Part(PrimitiveType.Cube, new Vector3(-halfW, midY, 0f), new Vector3(wall, BasketHeight, BasketLength + wall * 2f), _frameMaterial);
            Part(PrimitiveType.Cube, new Vector3(halfW, midY, 0f), new Vector3(wall, BasketHeight, BasketLength + wall * 2f), _frameMaterial);
            Part(PrimitiveType.Cube, new Vector3(0f, midY, halfL), new Vector3(BasketWidth + wall * 2f, BasketHeight, wall), _frameMaterial);
            Part(PrimitiveType.Cube, new Vector3(0f, midY, -halfL), new Vector3(BasketWidth + wall * 2f, BasketHeight, wall), _frameMaterial);

            // Legs, lower tray and wheels
            float legHeight = BasketFloorY - 0.1f;
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 corner = new Vector3(sx * (halfW - 0.03f), 0f, sz * (halfL - 0.05f));
                    Part(PrimitiveType.Cube, corner + Vector3.up * (0.1f + legHeight * 0.5f), new Vector3(0.025f, legHeight, 0.025f), _frameMaterial);
                    var wheel = Part(PrimitiveType.Cylinder, corner + Vector3.up * 0.05f, new Vector3(0.1f, 0.015f, 0.1f), _handleMaterial);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
            Part(PrimitiveType.Cube, new Vector3(0f, 0.16f, 0f), new Vector3(BasketWidth, wall, BasketLength), _frameMaterial);

            // Handle on the customer's side
            float handleY = BasketFloorY + BasketHeight + 0.12f;
            float handleZ = -halfL - 0.08f;
            Part(PrimitiveType.Cube, new Vector3(0f, handleY, handleZ), new Vector3(BasketWidth + 0.06f, 0.03f, 0.03f), _handleMaterial);
            foreach (float sx in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cube, new Vector3(sx * halfW, handleY - 0.07f, handleZ + 0.04f), new Vector3(0.02f, 0.16f, 0.02f), _frameMaterial);
            }

            var itemsRootObject = new GameObject("Items");
            itemsRootObject.transform.SetParent(transform, false);
            itemsRootObject.transform.localPosition = new Vector3(0f, BasketFloorY, 0f);
            _itemsRoot = itemsRootObject.transform;
        }

        private GameObject Part(PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var partRenderer = part.GetComponent<Renderer>();
            if (material != null) partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return part;
        }

        private static void EnsureMaterials()
        {
            if (_frameMaterial != null && _handleMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return; // parts keep the default primitive material

            _frameMaterial = new Material(shader) { name = "ShoppingCartFrame" };
            _frameMaterial.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.78f));
            _frameMaterial.SetFloat("_Metallic", 0.6f);
            _frameMaterial.SetFloat("_Smoothness", 0.55f);

            _handleMaterial = new Material(shader) { name = "ShoppingCartHandle" };
            _handleMaterial.SetColor("_BaseColor", new Color(0.80f, 0.16f, 0.14f));
            _handleMaterial.SetFloat("_Smoothness", 0.4f);
        }
    }
}
