using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 3D 맵 전환에 대비한 원형 투기장 씬을 만든다.
///
/// 씬을 손으로 짜지 않고 코드로 만드는 이유:
/// 배치 수치(반지름, 건물 수)를 바꿔가며 몇 번이고 다시 뽑아봐야 하는 단계이기 때문이다.
/// 마음에 드는 구도가 나오면 그때부터는 씬을 직접 손보면 된다.
///
/// 구성
///   바닥      Ground01a 타일을 원판 모양으로 깔았다. 한 장이 52 트라이앵글뿐이라 장수가 늘어도 부담이 적다.
///   담장      Wall05b 를 원형으로 둘러 세운다. 이게 "원형 공간" 의 윤곽이자 오클루전의 가림막 역할을 한다.
///   건물      담장 바깥에 안쪽을 보도록 배치. 담장과 서로에 가려지므로 오클루전 컬링이 실제로 일을 한다.
///   프롭      담장 안쪽에 몇 개. 분위기용.
///   캐릭터    Joseon_Royal_Wear 의 의류 메시를 겹쳐 세워 자리만 잡아둔다. (뼈대 없는 정적 메시라 애니메이션은 안 된다)
///
/// 컬링
///   환경 오브젝트 전부에 Occluder / Occludee / Batching Static 을 켜고 오클루전을 굽는다.
///   담장(높이 1.9m)이 건물 하단을 가리고, 건물끼리도 서로 가리기 때문에 굽는 값이 헛돌지 않는다.
///   자세한 효과는 Verify() 로 실제 렌더러 수를 세어 확인할 수 있다.
/// </summary>
public static class Arena3DBuilder
{
    public const string ScenePath = "Assets/Scenes/Arena3D.unity";

    // ── 배치 수치 ──
    private const float WallRadius     = 9f;      // 담장 반지름 = 투기장 크기
    // 바닥은 담장보다 아주 조금만 넓게 깐다. 3m 타일로는 원을 매끄럽게 못 따라가서
    // 여유를 크게 주면 담장 밖으로 모래밭이 들쭉날쭉 삐져나온다.
    private const float GroundRadius   = 10.4f;
    private const float BuildingRadius = 13.5f;   // 담장 바깥 건물 링
    private const float GroundTile     = 3f;      // Ground01a 한 장 크기
    private const float WallSegment    = 2.15f;   // Wall05b 가 +Z 로 뻗는 길이
    private const int   BuildingCount  = 12;
    private const float FighterOffsetX = 3.5f;    // 두 캐릭터 사이 거리의 절반

    // 담장에 낼 출입구. 이 인덱스의 세그먼트를 빼면 문처럼 뚫린다.
    private static readonly float[] GateAngles = { 0f, 180f };
    private const float GateHalfWidth = 12f;      // 각 출입구가 차지하는 각도의 절반(도)

    // 링에 세울 건물. 정사각형에 가까운 것 위주로 골랐다.
    // 가로세로 비가 심한 건물은 어느 면이 정면인지에 따라 링에서 튀어 보인다.
    private static readonly string[] BuildingNames =
    {
        "Ox_Mill", "L_Shaped_House_02", "Wooden_Bench_House_02", "Bamboo_Rafter_House_03",
        "Local_Clerks_House_02", "House_By_The_West_Gate_03", "Tavern_House_02", "Watermill",
        "House_With_Wooden_Floored_Room_03", "Smithy", "Wooden_Bench_House_03",
        "Local_Personnel_Clerks_House_04",
    };

    private static readonly string[] PropNames =
    {
        "Jar01a", "Cart", "Bamboo_Basket", "Basket01a", "Cauldron01a", "Anvil", "Brazer", "Basket01b",
    };

    // 의류 메시를 겹쳐 사람 형태를 만든다. 전부 발밑이 원점이라 같은 자리에 두면 그대로 맞물린다.
    private static readonly string[] PlayerWear =
        { "SM_Pants", "SM_Jeogori01a", "SM_Jeonbok01a", "SM_Gat", "SM_Black_Shoes" };
    private static readonly string[] EnemyWear =
        { "SM_Pants", "SM_Dongdari", "SM_Battle_Sash", "SM_Military_Hat", "SM_Boots" };

    private const string BuildFolder = "Assets/Naganeupseong/Prefabs/Build";
    private const string PropFolder  = "Assets/Naganeupseong/Prefabs/Prop";
    private const string WearFolder  = "Assets/Joseon_Royal_Wear/Meshes/Male";
    private const string GroundPath  = "Assets/Naganeupseong/Prefabs/Structure/Ground/Ground01a.prefab";
    private const string WallPath    = "Assets/Naganeupseong/Prefabs/Structure/Wall/Wall05b.prefab";

    [MenuItem("Tools/Arena3D/원형 투기장 씬 생성")]
    public static void Build()
    {
        // 열려 있는 씬에 저장 안 된 변경이 있으면 새 씬을 여는 순간 날아간다.
        var open = EditorSceneManager.GetActiveScene();
        if (open.isDirty)
        {
            Debug.LogError("[Arena3D] 현재 씬(" + open.name + ")에 저장하지 않은 변경이 있습니다. 저장한 뒤 다시 실행하세요.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        SetupCameraAndLight();

        var environment = new GameObject("Environment").transform;
        BuildGround(new GameObject("Ground").transform, environment);
        BuildWallRing(new GameObject("WallRing").transform, environment);
        BuildBuildings(new GameObject("Buildings").transform, environment);
        BuildProps(new GameObject("Props").transform, environment);

        MarkStatic(environment.gameObject);

        var fighters = new GameObject("Fighters").transform;
        BuildFighter("Player (placeholder)", PlayerWear, new Vector3(-FighterOffsetX, 0f, 0f), 90f, fighters);
        BuildFighter("Enemy (placeholder)",  EnemyWear,  new Vector3( FighterOffsetX, 0f, 0f), -90f, fighters);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[Arena3D] 씬 생성 완료 → " + ScenePath + "  (오클루전은 Tools/Arena3D 메뉴에서 따로 굽는다)");

        BakeOcclusion();
    }

    /// <summary>
    /// 오클루전 굽기. 씬 규모에 따라 수십 초 걸릴 수 있고 그동안 에디터가 멈춘다.
    /// 배치를 바꿀 때마다 다시 구워야 하므로 따로 빼 두었다.
    /// </summary>
    [MenuItem("Tools/Arena3D/오클루전 굽기")]
    public static void BakeOcclusion()
    {
        // smallestOccluder 를 담장 높이(1.9m)보다 작게 잡아야 담장이 가림막으로 인정된다.
        StaticOcclusionCulling.smallestOccluder = 1.5f;
        StaticOcclusionCulling.smallestHole = 0.25f;
        StaticOcclusionCulling.backfaceThreshold = 100f;

        StaticOcclusionCulling.Compute();

        var scene = EditorSceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

        // umbraDataSize 는 굽기 직후에 0 을 돌려준다. 실제로 구워졌는지는 디스크의 데이터 에셋으로 확인한다.
        string asset = System.IO.Path.ChangeExtension(scene.path, null) + "/OcclusionCullingData.asset";
        string size = System.IO.File.Exists(asset)
            ? new System.IO.FileInfo(asset).Length.ToString("N0") + " bytes"
            : "생성되지 않음";

        Debug.Log("[Arena3D] 오클루전 굽기 완료. " + asset + " → " + size);
    }

    // ─────────────────────── 구성 요소 ───────────────────────

    private static void SetupCameraAndLight()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            // 담장 안쪽에 둔다. 바깥에 두면 앞쪽 담장이 화면 아래를 가로막는다.
            cam.transform.position = new Vector3(0f, 3.4f, -(WallRadius - 0.7f));
            cam.transform.LookAt(new Vector3(0f, 1.3f, 0f));
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.2f;
            // 링 바깥까지만 보이면 된다. 멀리 자를수록 프러스텀 컬링이 일을 더 한다.
            cam.farClipPlane = 80f;
        }

        var light = Object.FindAnyObjectByType<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            light.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
        }
    }

    /// <summary>바닥 타일을 원판 모양으로 깐다. 타일 피벗이 모서리라 중심을 반 칸 밀어 계산한다.</summary>
    private static void BuildGround(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundPath);
        if (prefab == null) { Warn(GroundPath); return; }

        int half = Mathf.CeilToInt(GroundRadius / GroundTile);
        int placed = 0;

        for (int ix = -half; ix <= half; ix++)
        {
            for (int iz = -half; iz <= half; iz++)
            {
                // 타일 중심이 원 안에 들어올 때만 깐다. 이래야 사각형이 아니라 원판이 된다.
                var center = new Vector3((ix + 0.5f) * GroundTile, 0f, (iz + 0.5f) * GroundTile);
                if (center.magnitude > GroundRadius) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                // 타일 윗면이 y = 0.5 이므로 0.5 만큼 내려야 바닥이 y = 0 이 된다.
                go.transform.position = new Vector3(ix * GroundTile, -0.5f, iz * GroundTile);
                placed++;
            }
        }

        Debug.Log("[Arena3D] 바닥 타일 " + placed + "장");
    }

    /// <summary>담장을 원형으로 세운다. 세그먼트가 +Z 로 뻗으므로 접선 방향을 바라보게 돌린다.</summary>
    private static void BuildWallRing(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
        if (prefab == null) { Warn(WallPath); return; }

        // 올림으로 잡아 한 칸의 호 길이를 메시보다 짧게 만든다. 살짝 겹치는 편이 틈이 벌어지는 것보다 낫다.
        int count = Mathf.CeilToInt(2f * Mathf.PI * WallRadius / WallSegment);
        int placed = 0, skipped = 0;

        for (int i = 0; i < count; i++)
        {
            float deg = 360f * i / count;
            if (IsGate(deg)) { skipped++; continue; }

            float rad = deg * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(rad) * WallRadius, 0f, Mathf.Sin(rad) * WallRadius);
            var tangent = new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent));
            placed++;
        }

        Debug.Log("[Arena3D] 담장 " + placed + "칸 (출입구로 " + skipped + "칸 비움)");
    }

    private static bool IsGate(float deg)
    {
        foreach (var g in GateAngles)
            if (Mathf.Abs(Mathf.DeltaAngle(deg, g)) < GateHalfWidth) return true;
        return false;
    }

    /// <summary>건물을 담장 바깥에 안쪽을 보도록 세운다. 겹치지 않게 자기 깊이의 절반만큼 더 밀어낸다.</summary>
    private static void BuildBuildings(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        for (int i = 0; i < BuildingCount; i++)
        {
            string name = BuildingNames[i % BuildingNames.Length];
            var prefab = FindPrefab(name, BuildFolder);
            if (prefab == null) { Warn(name); continue; }

            float rad = 2f * Mathf.PI * i / BuildingCount;
            var dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            // +Z 가 원 중심을 향하게 세운다.
            go.transform.rotation = Quaternion.LookRotation(-dir);
            go.transform.position = dir * (BuildingRadius + DepthOf(go) * 0.5f);
        }

        Debug.Log("[Arena3D] 건물 " + root.childCount + "채");
    }

    /// <summary>회전을 적용한 뒤의 앞뒤 두께. 링 바깥으로 얼마나 밀어야 하는지 계산에 쓴다.</summary>
    private static float DepthOf(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return 0f;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return Mathf.Abs(Vector3.Dot(b.size, go.transform.forward));
    }

    /// <summary>담장 안쪽을 따라 프롭을 흩어 놓는다. 위치는 고정 시드라 다시 만들어도 같은 그림이 나온다.</summary>
    private static void BuildProps(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var state = Random.state;
        Random.InitState(20260909);

        for (int i = 0; i < 16; i++)
        {
            string name = PropNames[i % PropNames.Length];
            var prefab = FindPrefab(name, PropFolder);
            if (prefab == null) { Warn(name); continue; }

            float deg = 360f * i / 16f + Random.Range(-6f, 6f);
            if (IsGate(deg)) continue;   // 출입구는 막지 않는다

            float rad = deg * Mathf.Deg2Rad;
            float r = WallRadius - Random.Range(0.6f, 1.4f);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.position = new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        Random.state = state;
        Debug.Log("[Arena3D] 프롭 " + root.childCount + "개");
    }

    /// <summary>
    /// 의류 메시를 한 자리에 겹쳐 세워 캐릭터 자리를 표시한다.
    /// Joseon_Royal_Wear 에는 몸체 메시가 없어서 옷만 세운 형태가 된다. 위치 잡이용이므로 그대로 둔다.
    /// </summary>
    private static void BuildFighter(string name, string[] wear, Vector3 pos, float yaw, Transform parent)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

        foreach (var piece in wear)
        {
            var prefab = FindPrefab(piece, WearFolder);
            if (prefab == null) { Warn(piece); continue; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }

        Debug.Log("[Arena3D] " + name + " 의류 " + root.childCount + "점");
    }

    // ─────────────────────── 컬링 ───────────────────────

    /// <summary>
    /// 정적 플래그는 렌더러가 실제로 붙어 있는 오브젝트에 켜야 의미가 있다.
    /// 루트에만 켜면 자식으로 내려가지 않으므로 전부 훑는다.
    ///
    ///   Occluder  다른 것을 가리는 쪽    (담장, 건물)
    ///   Occludee  가려질 수 있는 쪽      (전부)
    ///   Batching  드로우콜 병합          (건물 한 채가 렌더러 수십 개짜리라 효과가 크다)
    ///
    /// ContributeGI 는 켜지 않는다. 라이트맵을 구우려면 UV2 가 필요한데 이 에셋들에는 없다.
    /// </summary>
    private static void MarkStatic(GameObject root)
    {
        const StaticEditorFlags flags =
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic;

        int count = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            count++;
        }

        Debug.Log("[Arena3D] 정적 플래그 " + count + "개 오브젝트에 적용");
    }

    /// <summary>
    /// 지금 씬에서 컬링이 실제로 몇 개를 걷어내는지 센다.
    /// 카메라 위치를 바꿔가며 눌러보면 배치가 컬링에 유리한지 바로 알 수 있다.
    /// </summary>
    [MenuItem("Tools/Arena3D/컬링 효과 확인")]
    public static void Verify()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("[Arena3D] Main Camera 가 없습니다."); return; }

        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);

        int total = all.Length, inFrustum = 0, visible = 0, tris = 0, frustumTris = 0;

        foreach (var r in all)
        {
            var mf = r.GetComponent<MeshFilter>();
            int t = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.triangles.Length / 3 : 0;
            tris += t;

            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            inFrustum++;
            frustumTris += t;
            if (r.isVisible) visible++;
        }

        Debug.Log(string.Format(
            "[Arena3D] 렌더러 {0}개 / 삼각형 {1:N0}\n" +
            "  프러스텀 안 : {2}개 ({3:P0}) / 삼각형 {4:N0} ({5:P0})  ← 여기서 걸러지는 양이 가장 크다\n" +
            "  isVisible   : {6}개  (마지막 렌더 결과. 에디터에서는 씬 뷰가 함께 그려져 부풀 수 있으니 참고용)",
            total, tris, inFrustum, (float)inFrustum / total, frustumTris, (float)frustumTris / tris,
            visible));
    }

    // ─────────────────────── 도우미 ───────────────────────

    /// <summary>
    /// 이름으로 에셋을 찾는다. 경로를 직접 쓰지 않는 이유는 확장자 대소문자가 제각각이기 때문이다.
    /// (SM_Gat.FBX / SM_Pants.fbx 가 한 폴더에 같이 있다)
    /// </summary>
    private static GameObject FindPrefab(string name, string folder)
    {
        foreach (var guid in AssetDatabase.FindAssets(name, new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileNameWithoutExtension(path).Equals(name, System.StringComparison.OrdinalIgnoreCase))
                continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) return go;
        }
        return null;
    }

    private static void Warn(string what)
    {
        Debug.LogWarning("[Arena3D] 에셋을 찾지 못해 건너뜁니다: " + what);
    }
}
