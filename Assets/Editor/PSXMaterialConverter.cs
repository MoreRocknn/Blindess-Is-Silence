#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Convierte materiales URP (Lit / Simple Lit / Unlit) a PSX/Lit conservando
/// textura, color, tiling/offset, alpha clip, cull y transparencia.
/// Colocar en una carpeta "Editor" (p. ej. Assets/Editor/).
///
/// Uso:
///  - Selecciona materiales o carpetas en Project y usa Assets > PSX > Convert to PSX Lit
///  - Selecciona objetos (escena o prefabs) y usa GameObject > PSX > Convert Materials to PSX Lit
///  - O el menu Tools > PSX en ambos casos
/// Todo es deshacible con Ctrl+Z.
/// </summary>
public static class PSXMaterialConverter
{
    const string PSXShaderName = "PSX/Lit";
    const string URPLitName = "Universal Render Pipeline/Lit";

    static readonly string[] ConvertibleShaders =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Unlit",
    };

    // ---------------- Menus ----------------

    [MenuItem("Assets/PSX/Convert to PSX Lit")]
    [MenuItem("Tools/PSX/Convert Selected to PSX Lit")]
    static void ConvertSelectedAssets() => Run(toPSX: true);

    [MenuItem("GameObject/PSX/Convert Materials to PSX Lit", false, 20)]
    static void ConvertSelectedObjects() => Run(toPSX: true);

    [MenuItem("Assets/PSX/Revert to URP Lit")]
    [MenuItem("Tools/PSX/Revert Selected to URP Lit")]
    static void RevertSelectedAssets() => Run(toPSX: false);

    [MenuItem("GameObject/PSX/Revert Materials to URP Lit", false, 21)]
    static void RevertSelectedObjects() => Run(toPSX: false);

    // ---------------- Logica ----------------

    static void Run(bool toPSX)
    {
        string targetName = toPSX ? PSXShaderName : URPLitName;
        Shader target = Shader.Find(targetName);
        if (target == null)
        {
            Debug.LogError($"PSXMaterialConverter: no se encontro el shader '{targetName}'. " +
                           "Asegurate de que PSXLit.shader esta en Assets y compila sin errores.");
            return;
        }

        var materials = CollectMaterials();
        if (materials.Count == 0)
        {
            Debug.LogWarning("PSXMaterialConverter: no hay materiales en la seleccion.");
            return;
        }

        int converted = 0, skipped = 0;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var mat in materials)
        {
            if (mat == null || mat.shader == target) continue;

            // Materiales embebidos en modelos (.fbx) no se pueden editar de forma persistente
            string path = AssetDatabase.GetAssetPath(mat);
            if (!string.IsNullOrEmpty(path) && !path.EndsWith(".mat"))
            {
                Debug.LogWarning($"PSXMaterialConverter: '{mat.name}' esta embebido en '{path}'. " +
                                 "Extraelo desde el Inspector del modelo (Materials > Extract Materials) y repite.", mat);
                skipped++;
                continue;
            }

            if (toPSX && System.Array.IndexOf(ConvertibleShaders, mat.shader.name) < 0)
            {
                skipped++;
                continue;
            }

            Undo.RecordObject(mat, toPSX ? "Convert to PSX Lit" : "Revert to URP Lit");
            if (toPSX) ConvertToPSX(mat, target);
            else mat.shader = target;

            EditorUtility.SetDirty(mat);
            converted++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();
        Debug.Log($"PSXMaterialConverter: {converted} material(es) -> {targetName}. Omitidos: {skipped}.");
    }

    static void ConvertToPSX(Material mat, Shader psx)
    {
        // Leer valores ANTES de cambiar el shader
        Texture baseMap = GetTex(mat, "_BaseMap", "_MainTex");
        Vector2 scale = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
        Vector2 offset = mat.HasProperty("_BaseMap") ? mat.GetTextureOffset("_BaseMap") : Vector2.zero;
        Color color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;

        bool alphaClip = mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f;
        float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
        float cull = mat.HasProperty("_Cull") ? mat.GetFloat("_Cull") : 2f;
        bool transparent = mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f;

        mat.shader = psx;

        mat.SetTexture("_BaseMap", baseMap);
        mat.SetTextureScale("_BaseMap", scale);
        mat.SetTextureOffset("_BaseMap", offset);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Cull", cull);
        mat.SetFloat("_Cutoff", cutoff);
        mat.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        if (alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");

        if (transparent)
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.renderQueue = alphaClip ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest
                                        : (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        // Limpiar keywords heredados de URP/Lit que PSX/Lit no usa
        foreach (var kw in new[] { "_NORMALMAP", "_EMISSION", "_METALLICSPECGLOSSMAP", "_SURFACE_TYPE_TRANSPARENT",
                                   "_ALPHAPREMULTIPLY_ON", "_PARALLAXMAP", "_OCCLUSIONMAP", "_DETAIL_MULX2" })
            mat.DisableKeyword(kw);
    }

    static Texture GetTex(Material mat, params string[] names)
    {
        foreach (var n in names)
            if (mat.HasProperty(n) && mat.GetTexture(n) != null)
                return mat.GetTexture(n);
        return null;
    }

    // ---------------- Recoleccion ----------------

    static List<Material> CollectMaterials()
    {
        var set = new HashSet<Material>();

        // Materiales seleccionados directamente + carpetas en Project
        var folders = new List<string>();
        foreach (var obj in Selection.objects)
        {
            if (obj is Material m) { set.Add(m); continue; }

            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                folders.Add(path);
        }

        if (folders.Count > 0)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", folders.ToArray()))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null) set.Add(m);
            }
        }

        // Objetos de escena / prefabs: sus materiales (incluye hijos)
        foreach (var go in Selection.gameObjects)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) set.Add(m);
        }

        return new List<Material>(set);
    }
}
#endif
