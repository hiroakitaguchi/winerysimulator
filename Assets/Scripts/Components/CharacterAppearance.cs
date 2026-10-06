using UnityEngine;
using Supermarket.Data;

namespace Supermarket.Components
{
    public class CharacterAppearance : MonoBehaviour
    {
        [Header("Model Configuration")]
        [SerializeField] private CharacterAppearanceConfig.GenderModel _currentModel;
        
        [Header("Current Colors")]
        [SerializeField] private Color _topClothingColor = Color.white;
        [SerializeField] private Color _bottomClothingColor = Color.white;
        [SerializeField] private Color _shoesColor = Color.white;
        
        private GameObject _currentModelInstance;
        
        // Применяет конфигурацию модели и цвета к персонажу
        public void ApplyAppearance(CharacterAppearanceConfig.GenderModel model, CustomerData customerData)
        {
            _currentModel = model;
            
            // Удаляем старую модель если есть
            if (_currentModelInstance != null)
            {
                DestroyImmediate(_currentModelInstance);
            }
            
            // Создаем новую модель
            if (model != null && model.modelPrefab != null)
            {
                _currentModelInstance = Instantiate(model.modelPrefab, transform);
                _currentModelInstance.transform.localPosition = Vector3.zero;
                _currentModelInstance.transform.localRotation = Quaternion.identity;

                // Per-customer body size: height on Y, build on X/Z, relative to the prefab's own scale.
                float height = customerData.HeightScale > 0f ? customerData.HeightScale : 1f;
                float build = customerData.BuildScale > 0f ? customerData.BuildScale : 1f;
                Vector3 baseScale = model.modelPrefab.transform.localScale;
                _currentModelInstance.transform.localScale = new Vector3(baseScale.x * build, baseScale.y * height, baseScale.z * build);
                
                // Применяем цвета
                ApplyClothingColors(customerData.TopClothingColor, customerData.BottomClothingColor, customerData.ShoesColor);

                SetupLevelOfDetail(model.modelPrefab.name);
            }
        }

        // --- Level of detail ---
        // The full model has about 50,000 triangles. Simplified meshes (baked onto the same skeleton and stored in
        // Resources/Characters/LOD as "<ModelName>_LOD1" / "_LOD2") take over as the character gets smaller on screen.
        private const string LodResourceFolder = "Characters/LOD/";
        private const float Lod0ScreenHeight = 0.30f;  // taller than 30% of the screen: full model
        private const float Lod1ScreenHeight = 0.10f;  // down to 10%: about 15,000 triangles
        private const float Lod2ScreenHeight = 0.01f;  // down to 1%: about 5,000 triangles; smaller is not drawn

        private void SetupLevelOfDetail(string modelName)
        {
            SkinnedMeshRenderer main = _currentModelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            if (main == null || main.sharedMesh == null) return;

            Mesh lod1Mesh = Resources.Load<Mesh>(LodResourceFolder + modelName + "_LOD1");
            Mesh lod2Mesh = Resources.Load<Mesh>(LodResourceFolder + modelName + "_LOD2");
            if (lod1Mesh == null && lod2Mesh == null) return; // no simplified meshes for this model: keep the full one always

            var levels = new System.Collections.Generic.List<LOD> { new LOD(Lod0ScreenHeight, new Renderer[] { main }) };
            if (lod1Mesh != null) levels.Add(new LOD(Lod1ScreenHeight, new Renderer[] { CreateLodRenderer(main, lod1Mesh) }));
            if (lod2Mesh != null) levels.Add(new LOD(Lod2ScreenHeight, new Renderer[] { CreateLodRenderer(main, lod2Mesh) }));
            // The last level must reach down to the cull threshold.
            LOD last = levels[levels.Count - 1];
            levels[levels.Count - 1] = new LOD(Lod2ScreenHeight, last.renderers);

            LODGroup group = _currentModelInstance.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(levels.ToArray());
            group.RecalculateBounds();

            _lodAnimator = _currentModelInstance.GetComponentInChildren<Animator>();
            _lodRenderers = _currentModelInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        // The Animator only knows about the full model: when a simplified model is the one on screen it believes the
        // character is hidden and stops moving the bones, which left distant customers gliding along in their rest
        // pose. So decide here: animate fully while any of the models is visible, and save the work only when none is.
        private Animator _lodAnimator;
        private SkinnedMeshRenderer[] _lodRenderers;

        private void LateUpdate()
        {
            if (_lodAnimator == null || _lodRenderers == null) return;

            bool visible = false;
            for (int i = 0; i < _lodRenderers.Length; i++)
            {
                if (_lodRenderers[i] != null && _lodRenderers[i].isVisible) { visible = true; break; }
            }

            AnimatorCullingMode wanted = visible ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            if (_lodAnimator.cullingMode != wanted) _lodAnimator.cullingMode = wanted;
        }

        // A second renderer on the same bones and the same (per-customer) materials, drawing a simpler mesh.
        private SkinnedMeshRenderer CreateLodRenderer(SkinnedMeshRenderer main, Mesh mesh)
        {
            var lodObject = new GameObject(mesh.name);
            lodObject.layer = main.gameObject.layer;
            lodObject.transform.SetParent(main.transform.parent, false);
            lodObject.transform.localPosition = main.transform.localPosition;
            lodObject.transform.localRotation = main.transform.localRotation;
            lodObject.transform.localScale = main.transform.localScale;

            var lodRenderer = lodObject.AddComponent<SkinnedMeshRenderer>();
            lodRenderer.sharedMesh = mesh;
            lodRenderer.bones = main.bones;
            lodRenderer.rootBone = main.rootBone;
            lodRenderer.localBounds = main.localBounds;
            lodRenderer.sharedMaterials = main.sharedMaterials; // already the customer's own coloured instances
            lodRenderer.shadowCastingMode = main.shadowCastingMode;
            lodRenderer.receiveShadows = main.receiveShadows;
            lodRenderer.updateWhenOffscreen = main.updateWhenOffscreen;
            lodRenderer.quality = SkinQuality.Bone2; // far away, two bones per vertex is enough
            return lodRenderer;
        }
        
        // Применяет цвета одежды к текущей модели
        public void ApplyClothingColors(Color topColor, Color bottomColor, Color shoesColor)
        {
            if (_currentModelInstance == null || _currentModel == null)
                return;
                
            _topClothingColor = topColor;
            _bottomClothingColor = bottomColor;
            _shoesColor = shoesColor;
            
            // Применяем цвет верхней одежды
            ApplyColorToMaterial(_currentModel.topClothingRendererPath, 
                               _currentModel.topClothingMaterialIndex, 
                               _currentModel.topClothing.materialPropertyName, 
                               topColor);
            
            // Применяем цвет нижней одежды
            ApplyColorToMaterial(_currentModel.bottomClothingRendererPath, 
                               _currentModel.bottomClothingMaterialIndex, 
                               _currentModel.bottomClothing.materialPropertyName, 
                               bottomColor);
            
            // Применяем цвет обуви
            ApplyColorToMaterial(_currentModel.shoesRendererPath, 
                               _currentModel.shoesMaterialIndex, 
                               _currentModel.shoes.materialPropertyName, 
                               shoesColor);
        }
        
        private void ApplyColorToMaterial(string rendererPath, int materialIndex, string propertyName, Color color)
        {
            if (string.IsNullOrEmpty(rendererPath))
                return;
                
            Transform rendererTransform = _currentModelInstance.transform.Find(rendererPath);
            if (rendererTransform == null)
            {
                // Пробуем найти рекурсивно
                Renderer[] allRenderers = _currentModelInstance.GetComponentsInChildren<Renderer>();
                foreach (var rend in allRenderers)
                {
                    if (rend.name == rendererPath || rend.transform.name == rendererPath)
                    {
                        rendererTransform = rend.transform;
                        break;
                    }
                }
            }
            
            if (rendererTransform != null)
            {
                Renderer renderer = rendererTransform.GetComponent<Renderer>();
                if (renderer != null && renderer.materials.Length > materialIndex)
                {
                    // Создаем копию материала чтобы не менять общий материал
                    Material mat = renderer.materials[materialIndex];
                    if (mat.HasProperty(propertyName))
                    {
                        mat.SetColor(propertyName, color);
                    }
                    else
                    {
                        Debug.LogWarning($"Material doesn't have property '{propertyName}'");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Couldn't find renderer at path '{rendererPath}'");
            }
        }
        
        // Возвращает экземпляр модели для доступа к Animator и другим компонентам
        public GameObject GetModelInstance()
        {
            return _currentModelInstance;
        }
        
        void OnDestroy()
        {
            if (_currentModelInstance != null)
            {
                DestroyImmediate(_currentModelInstance);
            }
        }
    }
} 