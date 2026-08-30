using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Outcrop
{
    /// <summary>
    /// Registers custom ROC prefabs into AssetBase.fetch.prefabs so ROCManager.Start
    /// can resolve them through AssetBase.GetPrefab(prefabName).
    ///
    ///   ROC_PREFAB
    ///   {
    ///       prefabName        = myObsidianSpire        // -> ROC_DEFINITION prefabName
    ///       modelName         = obsidianSpire          // -> ROC_DEFINITION modelName
    ///       model             = Outcrop/Models/spire    // GameDatabase url, no extension
    ///       colliderMode      = Mesh                   // Auto | Mesh | None
    ///       colliderTransform = obsidianSpire_LOD0     // Mesh mode; blank = search
    ///       colliderConvex    = false
    ///       layer             = Local Scenery
    ///   }
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class ROCPrefabLoader : MonoBehaviour
    {
        public const string NodeName = "ROC_PREFAB";
        private const string LogTag = "[ROCPrefabLoader] ";

        private static void Log(string m) { Debug.Log(LogTag + m); }
        private static void Warn(string m) { Debug.LogWarning(LogTag + m); }
        private static void Error(string m) { Debug.LogError(LogTag + m); }

        private enum ColliderMode { Auto, Mesh, Sphere, None }

        private class PrefabDef
        {
            public string PrefabName;
            public string ModelName;
            public string ModelUrl;
            public ColliderMode Mode = ColliderMode.Auto;
            public string ColliderTransform;
            public bool ColliderConvex;
            public string LayerName = "Local Scenery";
            public string TagName = "ROC";
            public int AutoScanPoints;          // points per ring; 0 = off
            public int AutoScanRings = 3;
            public float AutoScanLow = 0.25f;   // lowest ring, fraction of mesh height
            public float AutoScanHigh = 0.75f;  // highest ring
            public float AutoScanInset = 1.0f;  // radius multiplier
            public Vector3 ModelOffset = Vector3.zero;
            public float ModelScale = 1f;
            public string ShaderName;
            public string MainTex;
            public string BumpMap;
            public Color SpecColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            public float Shininess = 0.1f;

            public static PrefabDef Load(ConfigNode node)
            {
                PrefabDef d = new PrefabDef
                {
                    PrefabName = node.GetValue("prefabName"),
                    ModelName = node.GetValue("modelName"),
                    ModelUrl = NormalizeModelUrl(node.GetValue("model")),
                    ColliderTransform = node.GetValue("colliderTransform")
                };

                if (string.IsNullOrEmpty(d.PrefabName) ||
                    string.IsNullOrEmpty(d.ModelName) ||
                    string.IsNullOrEmpty(d.ModelUrl))
                {
                    Error("node missing prefabName, modelName or model; skipped.");
                    return null;
                }

                string mode = node.GetValue("colliderMode");
                if (!string.IsNullOrEmpty(mode))
                {
                    switch (mode.Trim().ToLowerInvariant())
                    {
                        case "auto": d.Mode = ColliderMode.Auto; break;
                        case "mesh": d.Mode = ColliderMode.Mesh; break;
                        case "none": d.Mode = ColliderMode.None; break;
                        default:
                            Warn("'" + d.PrefabName + "': unknown colliderMode '"
                                             + mode + "', falling back to Auto.");
                            break;
                    }
                }

                bool b;
                if (bool.TryParse(node.GetValue("colliderConvex"), out b)) d.ColliderConvex = b;

                string layer = node.GetValue("layer");
                if (!string.IsNullOrEmpty(layer)) d.LayerName = layer;

                string tag = node.GetValue("tag");
                if (!string.IsNullOrEmpty(tag)) d.TagName = tag;

                int ip;
                if (int.TryParse(node.GetValue("autoScanPoints"), out ip)) d.AutoScanPoints = ip;
                if (int.TryParse(node.GetValue("autoScanRings"), out ip) && ip > 0) d.AutoScanRings = ip;

                float fp;
                if (float.TryParse(node.GetValue("autoScanLow"), out fp)) d.AutoScanLow = fp;
                if (float.TryParse(node.GetValue("autoScanHigh"), out fp)) d.AutoScanHigh = fp;
                if (float.TryParse(node.GetValue("autoScanInset"), out fp) && fp > 0f) d.AutoScanInset = fp;

                Vector3 off;
                if (TryParseVector3(node.GetValue("modelOffset"), out off)) d.ModelOffset = off;

                float ms;
                if (float.TryParse(node.GetValue("modelScale"), out ms) && ms > 0f) d.ModelScale = ms;

                ConfigNode[] mats = node.GetNodes("MATERIAL");
                if (mats != null && mats.Length > 0)
                {
                    ConfigNode m = mats[0];
                    d.ShaderName = m.GetValue("shader");
                    d.MainTex = m.GetValue("mainTex");
                    d.BumpMap = m.GetValue("bumpMap");

                    float sh;
                    if (float.TryParse(m.GetValue("shininess"), out sh)) d.Shininess = sh;

                    Color c;
                    if (TryParseColor(m.GetValue("specColor"), out c)) d.SpecColor = c;
                }

                return d;
            }
        }

        private static string NormalizeModelUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            string s = url.Trim().Replace('\\', '/');
            while (s.StartsWith("/")) s = s.Substring(1);
            if (s.StartsWith("GameData/")) s = s.Substring("GameData/".Length);
            if (s.EndsWith(".mu")) s = s.Substring(0, s.Length - 3);
            return s;
        }

        private static bool _done;
        private static readonly Dictionary<string, PrefabDef> _defsByPrefab =
            new Dictionary<string, PrefabDef>();
        private static readonly Dictionary<string, GameObject> _built =
            new Dictionary<string, GameObject>();

        private void Start()
        {
            GameEvents.OnGameDatabaseLoaded.Add(Register);
            DontDestroyOnLoad(gameObject);
            if (GameDatabase.Instance != null && GameDatabase.Instance.IsReady()) Register();
        }

        private void OnDestroy()
        {
            GameEvents.OnGameDatabaseLoaded.Remove(Register);
        }

        private void Register()
        {
            if (_done) return;
            _done = true;

            AssetBase assets = ResolveAssetBase();
            if (assets == null)
            {
                Error("could not resolve the AssetBase instance; cannot register prefabs.");
                return;
            }

            if (GameDatabase.Instance == null || !GameDatabase.Instance.IsReady())
            {
                Error("GameDatabase is not ready; cannot load models.");
                return;
            }

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(NodeName);
            if (nodes == null || nodes.Length == 0) return;

            List<GameObject> prefabs = assets.prefabs != null
                ? new List<GameObject>(assets.prefabs)
                : new List<GameObject>();
            HashSet<string> taken = new HashSet<string>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null) taken.Add(prefabs[i].name);
            }

            int added = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                PrefabDef def = PrefabDef.Load(nodes[i]);
                if (def == null) continue;

                if (taken.Contains(def.PrefabName))
                {
                    Warn("prefab name '" + def.PrefabName + "' already registered; skipped.");
                    continue;
                }

                GameObject prefab;
                try
                {
                    prefab = Build(def);
                }
                catch (System.Exception e)
                {
                    Error("exception building '" + def.PrefabName + "': " + e);
                    continue;
                }
                if (prefab == null) continue;

                prefabs.Add(prefab);
                taken.Add(def.PrefabName);
                added++;

                _built[def.PrefabName] = prefab;
                _defsByPrefab[def.PrefabName] = def;
            }

            if (added == 0) return;

            assets.prefabs = prefabs.ToArray();
            Log("registered " + added + " prefab(s); AssetBase.prefabs now "
                + assets.prefabs.Length);

            GenerateScanPoints();
            BackfillROCManager();
            ValidateDefinitions();
        }

        /// <summary>
        /// Placing localSpaceScanPoints by hand is tedious. Generate rings of points
        /// from the model's own mesh bounds and write them into the ROC_DEFINITION
        /// ConfigNode before ROCManager.Start reads it, so stock SetStats picks them up.
        /// </summary>
        private static void GenerateScanPoints()
        {
            ConfigNode[] defs = GameDatabase.Instance.GetConfigNodes("ROC_DEFINITION");
            if (defs == null) return;

            for (int i = 0; i < defs.Length; i++)
            {
                ConfigNode node = defs[i];

                string prefabName = node.GetValue("prefabName");
                if (string.IsNullOrEmpty(prefabName)) continue;

                PrefabDef def;
                if (!_defsByPrefab.TryGetValue(prefabName, out def)) continue;
                if (def.AutoScanPoints <= 0) continue;

                // Never override hand-placed points.
                string[] existing = node.GetValues("localSpaceScanPoints");
                if (existing != null && existing.Length > 0)
                {
                    Log("'" + prefabName + "': localSpaceScanPoints already present; "
                        + "skipping autoScanPoints.");
                    continue;
                }

                GameObject prefab;
                if (!_built.TryGetValue(prefabName, out prefab)) continue;

                Bounds b;
                if (!TryGetLocalBounds(prefab, out b))
                {
                    Warn("'" + prefabName + "': could not measure mesh bounds; "
                         + "autoScanPoints skipped.");
                    continue;
                }

                int written = 0;
                float radius = Mathf.Max(b.extents.x, b.extents.z) * def.AutoScanInset;
                int rings = def.AutoScanRings;

                for (int r = 0; r < rings; r++)
                {
                    float t = rings == 1 ? 0.5f : (float)r / (rings - 1);
                    float frac = Mathf.Lerp(def.AutoScanLow, def.AutoScanHigh, t);
                    float y = b.min.y + b.size.y * frac;

                    for (int k = 0; k < def.AutoScanPoints; k++)
                    {
                        // Offset alternate rings so points are not stacked in columns.
                        float a = ((float)k / def.AutoScanPoints + r * 0.5f / def.AutoScanPoints)
                                  * Mathf.PI * 2f;
                        float x = b.center.x + Mathf.Cos(a) * radius;
                        float z = b.center.z + Mathf.Sin(a) * radius;

                        node.AddValue("localSpaceScanPoints",
                            x.ToString("F3") + ", " + y.ToString("F3") + ", " + z.ToString("F3"));
                        written++;
                    }
                }

                Log("'" + prefabName + "': generated " + written + " scan point(s) across "
                    + rings + " ring(s), radius " + radius.ToString("F2")
                    + ", y " + (b.min.y + b.size.y * def.AutoScanLow).ToString("F2")
                    + " to " + (b.min.y + b.size.y * def.AutoScanHigh).ToString("F2") + ".");
            }
        }

        private static bool TryGetLocalBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;

            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null || filters[i].sharedMesh == null) continue;

                Bounds mb = filters[i].sharedMesh.bounds;
                if (!any) { bounds = mb; any = true; }
                else bounds.Encapsulate(mb);
            }
            return any;
        }

        private static void ValidateDefinitions()
        {
            ConfigNode[] defs = GameDatabase.Instance.GetConfigNodes("ROC_DEFINITION");
            if (defs == null) return;

            for (int i = 0; i < defs.Length; i++)
            {
                string prefabName = defs[i].GetValue("prefabName");
                string type = defs[i].GetValue("Type");
                if (string.IsNullOrEmpty(prefabName)) continue;

                if (AssetBase.GetPrefab(prefabName) != null) continue;

                Error("ROC_DEFINITION Type='" + type + "' references prefabName='"
                      + prefabName + "' which is NOT registered. Check for a "
                      + "matching ROC_PREFAB node and that its model url resolved.");
            }
        }

        private static void BackfillROCManager()
        {
            ROCManager mgr = ROCManager.Instance;
            if (mgr == null) return;                 // hasn't run yet; Start will pick us up

            // KSP's own collection type, not System.Collections.Generic.Dictionary.
            DictionaryValueList<string, GameObject> table = mgr.RocTypeObjects;
            if (table == null) return;

            ConfigNode[] defs = GameDatabase.Instance.GetConfigNodes("ROC_DEFINITION");
            if (defs == null) return;

            int n = 0;
            for (int i = 0; i < defs.Length; i++)
            {
                string type = defs[i].GetValue("Type");
                string prefabName = defs[i].GetValue("prefabName");
                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(prefabName)) continue;
                if (table.ContainsKey(type)) continue;

                GameObject prefab;
                if (!_built.TryGetValue(prefabName, out prefab)) continue;

                GameObject live = Instantiate(prefab);
                live.name = prefabName;
                live.SetActive(false);
                if (live.GetComponent<ROC>() == null) live.AddComponent<ROC>();
                live.transform.SetParent(mgr.transform);
                live.SetActive(false);

                table.Add(type, live);
                n++;
            }

            if (n > 0)
            {
                Warn("backfilled " + n + " entry(s) into ROCManager.RocTypeObjects because "
                     + "ROCManager.Start had already run. These entries have NOT had "
                     + "ROC.SetStats called on them, so depth/scale/scan points are unset. "
                     + "This is a fallback, not a working configuration.");
            }
        }

        private static AssetBase ResolveAssetBase()
        {
            AssetBase found = FindObjectOfType<AssetBase>();
            if (found != null) return found;

            FieldInfo fi = typeof(AssetBase).GetField(
                "fetch", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi == null)
            {
                Warn("AssetBase.fetch not found by reflection; KSP version mismatch?");
                return null;
            }

            return fi.GetValue(null) as AssetBase;
        }

        private static GameObject Build(PrefabDef def)
        {
            GameObject source = GameDatabase.Instance.GetModel(def.ModelUrl);
            if (source == null)
            {
                Error("GetModel failed for '" + def.ModelUrl + "'. The url is relative to "
                      + "GameData and carries no file extension, e.g. "
                      + "GameData/Outcrop/RockTower_LOD0.mu -> 'Outcrop/RockTower_LOD0'.");
                return null;
            }

            GameObject model = Instantiate(source);
            model.name = def.ModelName;
            model.SetActive(true);

            GameObject root = new GameObject(def.PrefabName);

            root.SetActive(false);
            model.transform.SetParent(root.transform, false);

            if (def.ModelOffset.x != 0f || def.ModelOffset.y != 0f || def.ModelOffset.z != 0f)
                model.transform.localPosition = def.ModelOffset;

            if (def.ModelScale != 1f)
                model.transform.localScale = new Vector3(def.ModelScale, def.ModelScale, def.ModelScale);

            root.AddComponent<ROC>();

            if (model.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                Error("'" + def.PrefabName + "': model '" + def.ModelUrl
                      + "' has no Renderer; refusing to register.");
                return null;
            }

            ApplyMaterial(model, def);
            ApplyCollider(root, model, def);
            SetTagRecursive(root, def.TagName);

            int layer = LayerMask.NameToLayer(def.LayerName);
            if (layer < 0)
            {
                Warn("'" + def.PrefabName + "': unknown layer '" + def.LayerName + "'; leaving default.");
            }
            else
            {
                SetLayerRecursive(root, layer);
            }

            DontDestroyOnLoad(root);

            if (root.GetComponent<ROC>() == null)
            {
                Error("'" + def.PrefabName + "': ROC component vanished after construction. "
                      + "ROCManager will silently drop any definition using this prefab.");
                return null;
            }

            return root;
        }

        private static void ApplyCollider(GameObject root, GameObject model, PrefabDef def)
        {
            if (def.Mode == ColliderMode.None) return;

            if (def.Mode == ColliderMode.Auto && model.GetComponentsInChildren<Collider>(true).Length > 0)
            {
                return;   // .mu already shipped colliders
            }

            if (def.Mode == ColliderMode.Mesh || def.Mode == ColliderMode.Auto)
            {
                MeshFilter mf = FindColliderMesh(model, def);
                if (mf != null && mf.sharedMesh != null)
                {
                    MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = def.ColliderConvex;
                    return;
                }

                if (def.Mode == ColliderMode.Mesh)
                {
                    Warn("'" + def.PrefabName
                                     + "': colliderMode=Mesh but no usable MeshFilter found"
                                     + " (colliderTransform='" + def.ColliderTransform
                                     + "')");
                }
            }
        }

        /// <summary>
        /// Resolves the mesh to build a MeshCollider from. Named transform wins;
        /// otherwise take the highest-detail MeshFilter.
        /// </summary>
        private static MeshFilter FindColliderMesh(GameObject model, PrefabDef def)
        {
            if (!string.IsNullOrEmpty(def.ColliderTransform))
            {
                Transform t = model.transform.Find(def.ColliderTransform);
                if (t != null) return t.GetComponent<MeshFilter>();
            }

            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter best = null;
            int bestVerts = -1;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;

                int verts = mf.sharedMesh.vertexCount;
                if (mf.name.EndsWith("LOD0")) verts = int.MaxValue;
                if (verts > bestVerts)
                {
                    bestVerts = verts;
                    best = mf;
                }
            }
            return best;
        }

        private static bool TryParseVector3(string v, out Vector3 r)
        {
            r = Vector3.zero;
            if (string.IsNullOrEmpty(v)) return false;

            string[] p = v.Split(',');
            if (p.Length < 3) return false;

            float x, y, z;
            if (!float.TryParse(p[0], out x)) return false;
            if (!float.TryParse(p[1], out y)) return false;
            if (!float.TryParse(p[2], out z)) return false;

            r = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseColor(string v, out Color c)
        {
            c = new Color(0.2f, 0.2f, 0.2f, 1f);
            if (string.IsNullOrEmpty(v)) return false;

            string[] parts = v.Split(',');
            if (parts.Length < 3) return false;

            float r, g, b, a = 1f;
            if (!float.TryParse(parts[0], out r)) return false;
            if (!float.TryParse(parts[1], out g)) return false;
            if (!float.TryParse(parts[2], out b)) return false;
            if (parts.Length > 3) float.TryParse(parts[3], out a);

            c = new Color(r, g, b, a);
            return true;
        }

        private static void ApplyMaterial(GameObject model, PrefabDef def)
        {
            if (string.IsNullOrEmpty(def.MainTex) && string.IsNullOrEmpty(def.ShaderName)) return;

            Shader shader = null;
            if (!string.IsNullOrEmpty(def.ShaderName))
            {
                shader = Shader.Find(def.ShaderName);
                if (shader == null)
                {
                    Warn("'" + def.PrefabName + "': Shader.Find('" + def.ShaderName + "') failed.");
                }
            }

            if (shader == null) shader = BorrowStockShader();
            if (shader == null)
            {
                Error("'" + def.PrefabName + "': no usable shader; leaving material alone.");
                return;
            }

            Texture2D main = null;
            if (!string.IsNullOrEmpty(def.MainTex))
            {
                main = GameDatabase.Instance.GetTexture(def.MainTex, false);
                if (main == null) Warn("'" + def.PrefabName + "': mainTex '" + def.MainTex + "' not found.");
            }

            Texture2D bump = null;
            if (!string.IsNullOrEmpty(def.BumpMap))
            {
                bump = GameDatabase.Instance.GetTexture(def.BumpMap, true);
                if (bump == null) Warn("'" + def.PrefabName + "': bumpMap '" + def.BumpMap + "' not found.");
            }

            Material mat = new Material(shader);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (main != null) mat.SetTexture("_MainTex", main);
                if (bump != null) mat.SetTexture("_BumpMap", bump);
                mat.SetColor("_SpecColor", def.SpecColor);
                mat.SetFloat("_Shininess", def.Shininess);
                renderers[i].sharedMaterial = mat;
            }

            Log("'" + def.PrefabName + "': applied material to " + renderers.Length
                + " renderer(s) using shader '" + shader.name + "'.");
        }

        private static Shader BorrowStockShader()
        {
            string[] donors = { "munStone", "dunaStone", "kerbinQuartz", "laytheStone" };
            for (int i = 0; i < donors.Length; i++)
            {
                GameObject go = AssetBase.GetPrefab(donors[i]);
                if (go == null) continue;

                Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < rs.Length; j++)
                {
                    if (rs[j].sharedMaterial != null && rs[j].sharedMaterial.shader != null)
                        return rs[j].sharedMaterial.shader;
                }
            }
            return null;
        }

        private static void SetTagRecursive(GameObject go, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            try
            {
                go.tag = tag;
            }
            catch (System.Exception)
            {
                Error("tag '" + tag + "' is not defined in this Unity project's tag list.");
                return;
            }

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child != null) SetTagRecursive(child.gameObject, tag);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child != null) SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}