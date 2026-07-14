using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(BluePrinceMapOverlay.Core), "BluePrinceMapOverlay", "1.1.0", "ComplexSimple", null)]
[assembly: MelonGame("Dogubomb", "BLUE PRINCE")]

namespace BluePrinceMapOverlay
{
    public class Core : MelonMod
    {
        private const string PreferencesCategoryName = "BluePrinceMapOverlay";
        private MelonPreferences_Category _category;

        // I had to make these doubles since the config wouldn't allow for the right precision with floats.
        private MelonPreferences_Entry<double> _overlayX;
        private MelonPreferences_Entry<double> _overlayY;
        private MelonPreferences_Entry<float> _scale;
        private MelonPreferences_Entry<float> _transparency;
        private MelonPreferences_Entry<KeyCode> _toggleKey;
        private MelonPreferences_Entry<bool> _visibleAtStart;

        private const string HudGameObjectPath = "__SYSTEM/HUD";
        private const string CullingReferencePath = "__SYSTEM/HUD/Steps/Steps Icon";
        private const string GridReferencePath = "__SYSTEM/THE GRID";
        private const string SystemGameObjectPath = "__SYSTEM";
        private const string PlayerIconRelativePath = "FPS Home/FPSController - Prince/Player Core/Player Icon";
        private const string GridManagerPath = "ROOMS/Grid Manager";
        private const string MapDraftingCameraPath = "UI OVERLAY CAM/Map HOlder/Map Camera Drafting";
        private const string DraftReferencePath = "__SYSTEM/THE DRAFT/anchor/DRAFT UI";

        private const string BundlePathSuffix = "BluePrinceMapOverlay/assets/map_overlay.bundle";
        private const string PrefabName = "Map Overlay";


        private const float OverlayZPosition = 27.46f;
        private const float ResolutionScale = 2f; // Adjust this value to change the resolution of the overlay

        private readonly Vector3 BaseScale = new(34f, 20f, 1f);
        
        private GameObject _mapOverlay;
        private Renderer _mapOverlayRenderer;
        private GameObject _cullingReference;
        private Camera _mapOverlayCamera;
        private Dictionary<Renderer, bool> _mapRendererVisibilities;
        private GameObject _playerIcon;
        private GridManager _gridManager;
        private GameObject _mapDraftingCamera;
        private PlayMakerFSM _theGridFsm;
        private PlayMakerFSM _draftUIFSM;

        private bool _openingCutsceneFinished;
        private bool _isToggledOn = false;

        public override void OnInitializeMelon()
        {
            InitPreferences();
            LoggerInstance.Msg("Initialized Blue Prince Map Overlay Mod.");
        }

        // Initializes the preferences for the compass mod, including position, scale, and
        // rotation inversion.
        private void InitPreferences()      
        {
            _category = MelonPreferences.CreateCategory(PreferencesCategoryName);
            
            _overlayX = MelonPreferences.CreateEntry<double>(PreferencesCategoryName, "OverlayX", -11.3f, null, "The x position of the overlay relative to the HUD GameObject.");
            _overlayY = MelonPreferences.CreateEntry<double>(PreferencesCategoryName, "OverlayY", -1004.6f, null, "The y position of the overlay relative to the HUD GameObject.");
            _scale = MelonPreferences.CreateEntry<float>(PreferencesCategoryName, "Scale", 1.0f, null, "The size of the overlay relative to its default size.");
            _transparency = MelonPreferences.CreateEntry<float>(PreferencesCategoryName, "Transparency", 0.5f, null, "The transparency of the overlay, from 0 (opaque) to 1 (fully transparent).");
            _toggleKey = MelonPreferences.CreateEntry<KeyCode>(PreferencesCategoryName, "ToggleKey", KeyCode.LeftControl, null, 
                "The key to toggle the overlay visibility. Left Control by default. See Unity KeyCodes (https://docs.unity3d.com/6000.1/Documentation/ScriptReference/KeyCode.html)");
            _visibleAtStart = MelonPreferences.CreateEntry<bool>(PreferencesCategoryName, "VisibleAtStart", true, null, "The default visibility of the overlay when the day starts.");

            LoggerInstance.Msg($"Compass Preferences: {Path.Combine(MelonEnvironment.UserDataDirectory, $"{PreferencesCategoryName}.cfg")}");
            _category.SetFilePath(Path.Combine(MelonEnvironment.UserDataDirectory, $"{PreferencesCategoryName}.cfg"));
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            GameObject hud = GameObject.Find(HudGameObjectPath);
            if (hud == null)
            {
                return;
            }

            _openingCutsceneFinished = false;

            _cullingReference = GetCullingReference();
            _mapOverlay = LoadMapOverlay(hud, _cullingReference);
            _mapOverlayRenderer = _mapOverlay.GetComponent<Renderer>();
            _mapDraftingCamera = GameObject.Find(MapDraftingCameraPath);

            if (_mapDraftingCamera == null)
            {
                LoggerInstance.Error($"Map Drafting Camera GameObject \"{MapDraftingCameraPath}\" not found in the scene, skipping camera creation.");
                return;
            }
            _mapOverlayCamera = CreateMapOverlayCamera(_mapDraftingCamera);

            Material material = SetupMaterial(_mapOverlay, _mapOverlayCamera);
            if (material == null)
            {
                LoggerInstance.Error("Failed to set up Map Overlay material.");
                return;
            }

            GameObject grid = GameObject.Find(GridReferencePath);
            _theGridFsm = grid?.GetComponent<PlayMakerFSM>();
            _mapRendererVisibilities = GetRendererVisibilities(grid);
            _playerIcon = ClonePlayerIcon();
            _gridManager = GetGridManager();
            _draftUIFSM = GameObject.Find(DraftReferencePath)?.GetComponent<PlayMakerFSM>();

            _isToggledOn = false;
        }

        // Retrieves the culling reference GameObject from the HUD GameObject using the
        // specified relative path.
        private GameObject GetCullingReference()
        {
            GameObject cullingReference = GameObject.Find(CullingReferencePath);
            if (cullingReference == null)
            {
                LoggerInstance.Error($"Could not find culling reference Transform \"{CullingReferencePath}\"");
                return null;
            }
            return cullingReference;
        }

        // Instantiates the map overlay prefab from the asset bundle and sets it up with the
        private GridManager GetGridManager()
        {
            GameObject gridManager = GameObject.Find(GridManagerPath);
            if (gridManager == null)
            {
                LoggerInstance.Error($"Could not find Grid Manager GameObject \"{GridManagerPath}\"");
                return null;
            }
            return gridManager.GetComponent<GridManager>();
        }

        private GameObject FindRelativeToSystem(string relativePath)
        {
            GameObject system = GameObject.Find(SystemGameObjectPath);
            Transform relativeTransform = system.transform.Find(relativePath);
            if (relativeTransform == null)
            {
                LoggerInstance.Error($"Could not find relative Transform \"{relativePath}\" in system GameObject.");
                return null;
            }
            return relativeTransform.gameObject;
        }

        // Loads the map overlay prefab from the asset bundle and instantiates it at the specified
        // position and scale.
        private GameObject LoadMapOverlay(GameObject hud, GameObject cullingReference)
        {
            string bundlePath = Path.Combine(MelonEnvironment.ModsDirectory, BundlePathSuffix);
            LoggerInstance.Msg($"Bundle Path: {bundlePath}");
            Il2CppAssetBundle bundle = Il2CppAssetBundleManager.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                LoggerInstance.Error($"Failed to load asset bundle from path: {bundlePath}");
                return null;
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(PrefabName);
            bundle.Unload(false);
            if (prefab == null)
            {
                LoggerInstance.Error($"Failed to load prefab \"{PrefabName}\" from asset bundle.");
                return null;
            }

            Vector3 overlayPosition = new((float)_overlayX.Value, (float)_overlayY.Value, OverlayZPosition);
            GameObject mapOverlay = GameObject.Instantiate(prefab, overlayPosition, Quaternion.identity, hud.transform);

            mapOverlay.SetActive(false); // Start with the overlay inactive, activate it after the cutscene.
            Vector3 scale = _scale.Value * BaseScale;
            mapOverlay.transform.localScale = scale;

            InitCulling(mapOverlay, hud, cullingReference);

            return mapOverlay;
        }

        // Sets up the material for the map overlay, including the RenderTexture and transparency.
        private Material SetupMaterial(GameObject mapOverlay, Camera camera)
        {
            Renderer renderer = mapOverlay.GetComponent<Renderer>();
            if (renderer == null)
            {
                LoggerInstance.Error("Map Overlay GameObject does not have a Renderer component.");
                return null;
            }
            Material material = renderer.material;
            if (material == null)
            {
                LoggerInstance.Error("Map Overlay GameObject does not have a Material component.");
                return null;
            }
            int textureWidth = (int) (camera.pixelWidth * ResolutionScale);
            int textureHeight = (int) (camera.pixelHeight * ResolutionScale);
            RenderTexture renderTexture = new(textureWidth, textureHeight, 24);
            material.mainTexture = renderTexture; // Set the main texture of the material to the RenderTexture
            camera.targetTexture = renderTexture;
            material.SetFloat("_Alpha", 1 - _transparency.Value);
            
            return material;
        }

        // Sets up map overlay culling so it will only be rendered when steps, gems and keys are
        // rendered.
        private void InitCulling(GameObject obj, GameObject hud, GameObject cullingReference)
        {
            Culler hudCuller = hud.GetComponent<Culler>();
            if (hudCuller == null)
            {
                LoggerInstance.Error($"HUD GameObject with name \"{HudGameObjectPath}\" does not have a Culler component. The map overlay will not be added to the culler.");
                return;
            }

            // This (like this whole codebase) is incredibly overengineered, but I have an
            // irrational fear of breaking forward compatibility if I use hardcoded values.
            bool referenceInEnabledList = true;
            if (cullingReference == null)
            {
                LoggerInstance.Error("The culling reference is null, so the rendering layer couldn't be determined.");
            }
            else
            {
                obj.layer = cullingReference.layer;
                Renderer referenceRenderer = _cullingReference.GetComponent<Renderer>();
                referenceInEnabledList = (referenceRenderer != null &&
                    hudCuller._childRenderersEnabled.Contains(referenceRenderer));
            }

            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            // I wasn't sure how to convert from the array to an Il2CPP enumerable, so I just
            // add the MeshRenderers one by one.
            foreach (Renderer renderer in renderers)
            {
                // I don't like using these private fields, but I don't know how to add the
                // map overlay to the culler otherwise.
                hudCuller._childRenderers.Add(renderer);
                if (referenceInEnabledList)
                {
                    hudCuller._childRenderersEnabled.Add(renderer);
                }
                else
                {
                    hudCuller._childRenderersDisabled.Add(renderer);
                }
            }
        }

        // Creates a new camera for the map overlay, which will render the map overlay
        private Camera CreateMapOverlayCamera(GameObject mapDraftingCamera)
        {
            GameObject cameraObject = GameObject.Instantiate(
                mapDraftingCamera,
                mapDraftingCamera.transform.position,
                mapDraftingCamera.transform.rotation,
                mapDraftingCamera.transform.parent
            );
            cameraObject.SetActive(true);
            cameraObject.name = "Map Overlay Camera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Nothing;
            // Blue Prince constantly enables and disables the map tile renderers, and I'm not sure where that's happening.
            // I added these callbacks as a workaround to ensure the map overlay is always visible when the camera is active.
            Camera.onPreCull += (Camera.CameraCallback) PreCullCallback;
            Camera.onPostRender += (Camera.CameraCallback) PostRenderCallback;
            return camera;
        }

        private void PreCullCallback(Camera cam)
        {
            if (cam == _mapOverlayCamera)
            {
                ClearRenderTexture(cam);
                SetRenderers(_mapRendererVisibilities, true);
            }
        }

        private void PostRenderCallback(Camera cam)
        {
            if (cam == _mapOverlayCamera)
            {
                ResetRenderers(_mapRendererVisibilities);
            }
        }

        // The bottom of the RenderTexture isn't cleared by default, so we need to clear it manually.
        private void ClearRenderTexture(Camera cam)
        {
            if (cam != _mapOverlayCamera)
            {
                return;
            }
            RenderTexture renderTexture = cam.targetTexture;
            if (renderTexture != null)
            {
                RenderTexture.active = renderTexture;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = null;
            }
        }


        // Retrieves the visibilities of all renderers in the GameObject and stores them in a dictionary.
        private static Dictionary<Renderer, bool> GetRendererVisibilities(GameObject root)
        {
            Dictionary<Renderer, bool> rendererVisibilities = [];

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                rendererVisibilities[renderer] = renderer.enabled;
            }

            return rendererVisibilities;
        }

        // Sets the renderers to the given values and stores their previous values in the map.
        private static void SetRenderers(Dictionary<Renderer, bool> rendererVisibilities, bool enabled)
        {
            foreach (Renderer renderer in rendererVisibilities.Keys)
            {
                rendererVisibilities[renderer] = renderer.enabled;
                renderer.enabled = enabled;
            }
        }

        // Resets the visibility of all map renderers to their original state after rendering.
        private static void ResetRenderers(Dictionary<Renderer, bool> rendererVisibilities)
        {
            foreach (KeyValuePair<Renderer, bool> entries in rendererVisibilities)
            {
                Renderer renderer = entries.Key;
                bool enabled = entries.Value;
                renderer.enabled = enabled;
            }
        }

        // Clones the existing player icon GameObject
        private GameObject ClonePlayerIcon()
        {
            // The player icon starts disabled, so we can't use GameObject.Find directly to get it.
            GameObject oldPlayerIcon = FindRelativeToSystem(PlayerIconRelativePath);
            GameObject newPlayerIcon = GameObject.Instantiate(oldPlayerIcon,
                // The icon is too high at the top of the clocktower, so we move it down a bit.
                oldPlayerIcon.transform.position - new Vector3(0, 2, 0), 
                oldPlayerIcon.transform.rotation, 
                oldPlayerIcon.transform.parent
            );
            newPlayerIcon.name = "Player Icon Map Overlay";
            return newPlayerIcon;
        }

        public override void OnUpdate()
        {
            if (_mapOverlay == null)
            {
                return;
            }

            HandleCutsceneFinished();
            HandleToggleKeyPress();
            HandleDraftingUI();
            SetPlayerIconActive();
            SetOverlayActive();
        }

        // Enables the map overlay after the opening cutscene is finished.
        private void HandleCutsceneFinished()
        {
            if (_openingCutsceneFinished)
            {
                return;
            }

            if (_cullingReference.activeInHierarchy)
            {
                _openingCutsceneFinished = true;
                _isToggledOn = _visibleAtStart.Value;
            }
        }

        // Toggles the visibility of the map overlay when the toggle key is pressed.
        private void HandleToggleKeyPress()
        {
            if (Input.GetKeyDown(_toggleKey.Value))
            {
                _isToggledOn = !_isToggledOn;
            }
        }

        // Sets the player icon active or inactive based on the player's position in the grid.
        private void SetPlayerIconActive()
        {
            if (_playerIcon == null)
            {
                return;
            }

            bool active = _mapOverlayRenderer.isVisible &&
                (_gridManager == null || !_gridManager.IsPlayerActuallyOutside);

            _playerIcon.SetActive(active);
        }

        // Sets the map overlay active or inactive based on the toggle state and whether the drafting map is visible.
        private void SetOverlayActive()
        {
            bool isActive = _isToggledOn;
            if (_mapDraftingCamera.activeInHierarchy)
            {
                isActive = false; // Hide the overlay while the drafting map is visible.
            }
            _mapOverlay.SetActive(isActive);
        }

        private void HandleDraftingUI()
        {
            bool isDrafting = _draftUIFSM.FsmVariables.GetFsmBool("are drafting").Value;
            if (!isDrafting)    
            {
                return;
            }

            // In the original game, the tile is only enabled when the drafting map is enabled.
            // With this, it can show up on the minimap right away with the correct rotation.
            GameObject targetTileChild = _theGridFsm.FsmVariables.GetFsmGameObject("TargetTileChild")?.Value;

            targetTileChild.SetActive(true);
            float direction = _theGridFsm.FsmVariables.GetFsmFloat("Cardinal Direction").Value;

            targetTileChild.transform.rotation = Quaternion.Euler(0, direction, 0);
            
        }
    }
}