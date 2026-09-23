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
/// ─────────────────────────────────────────────────────────────────
/// Naganeupseong 데모 씬(Assets/Naganeupseong/Scene/Demo.unity)에서 가져온 규칙
/// ─────────────────────────────────────────────────────────────────
/// 데모의 담장은 세 조각의 조합이다. 다른 Wall 프리팹은 거의 쓰이지 않는다.
///   Wall01c  208개   직선 구간. 2.13m 간격으로 늘어놓는다.
///   Wall03c   25개   방향이 꺾이는 자리(모서리)에 놓는 기둥
///   Door01k    9개   담장 한 칸을 비우고 그 자리에 끼우는 문 (폭 1.87m)
///
/// 그리고 데모의 담장은 전부 rotY 0 / 90 뿐이다. 즉 임의 각도로 휘두르지 않고
/// 직선 런을 이어붙여 구획을 만든다. 그래서 여기서도 원을 매끄러운 곡선으로 두르지 않고
/// 정팔각형으로 두른다 — 한 변이 직선 런이고 꼭짓점마다 Wall03c 기둥이 선다.
/// 멀리서 보면 원으로 읽히면서 조각들은 원래 쓰이던 방식대로 놓인다.
///
/// 조명도 데모 값을 그대로 가져왔다. rot(36,136,15) / 따뜻한 색 / Soft 그림자 /
/// 앰비언트는 Skybox, 포그 없음. 이 팩의 텍스처가 이 조명에서 제일 제대로 보인다.
///
/// 건물은 데모에서 전부 scale 1 의 자립 프리팹이다. 밑에 기단을 따로 깔지 않는다.
/// 프롭은 집 한 채 주변 12m 안에 100개 넘게 깔려 있다. 여기서도 넉넉히 뿌린다.
/// ─────────────────────────────────────────────────────────────────
///
/// 컬링
///   환경 오브젝트 전부에 Occluder / Occludee / Batching Static 을 켜고 오클루전을 굽는다.
///   다만 카메라가 투기장 한가운데 있으면 지붕이 담장보다 높아 실제로 걷어내는 건
///   대부분 프러스텀 컬링이다. Verify() 로 실제 숫자를 확인할 수 있다.
/// </summary>
public static class Arena3DBuilder
{
    public const string ScenePath = "Assets/Scenes/Arena3D.unity";

    // ── 담장 (데모 수치) ──
    private const float WallSegment     = 2.13f;  // 데모가 Wall01c 를 늘어놓는 간격
    private const float WallSideOffset  = 0.23f;  // Wall01c 몸통이 피벗에서 -X 로 밀려 있는 만큼
    private const int   Sides           = 8;      // 정팔각형
    private const int   SegmentsPerSide = 4;      // 한 변에 들어가는 Wall01c 수
    private const int   GateSegment     = 2;      // 출입구가 될 세그먼트 번호 (변 안에서)
    private static readonly int[] GateSides = { 0, 4 };   // 마주보는 두 변에 문을 낸다

    // 한 변 길이와 외접/내접 반지름은 위 값에서 나온다.
    private const float SideLength   = WallSegment * SegmentsPerSide;                        // 8.52
    private static readonly float Circumradius = SideLength / (2f * Mathf.Sin(Mathf.PI / Sides));   // 11.13
    private static readonly float Inradius     = Circumradius * Mathf.Cos(Mathf.PI / Sides);       // 10.28

    // ── 나머지 배치 ──
    private const float GroundTile     = 3f;      // Ground01a 한 장 크기
    private const int   BuildingCount  = 12;
    private const float FighterOffsetX = 3.5f;    // 두 캐릭터 사이 거리의 절반
    private const int   PropCount      = 34;

    /// <summary>건물 링. 담장 바깥으로 충분히 물려야 지붕이 담장을 파고들지 않는다.</summary>
    private static float BuildingRadius { get { return Inradius + 3.5f; } }

    // 링에 세울 건물. 정사각형에 가까운 것 위주로 골랐다.
    // 가로세로 비가 심한 건물은 어느 면이 정면인지에 따라 링에서 튀어 보인다.
    private static readonly string[] BuildingNames =
    {
        "Ox_Mill", "L_Shaped_House_02", "Wooden_Bench_House_02", "Bamboo_Rafter_House_03",
        "Local_Clerks_House_02", "House_By_The_West_Gate_03", "Tavern_House_02", "Watermill",
        "House_With_Wooden_Floored_Room_03", "Smithy", "Wooden_Bench_House_03",
        "Local_Personnel_Clerks_House_04",
    };

    // 데모에서 가장 많이 쓰인 프롭들. 마당에 굴러다니는 것 위주로 골랐다.
    // (데모 상위: Rice_Straw 42, Straw_Bag 33, Corn 32, Chili 24 …)
    private static readonly string[] PropNames =
    {
        "Rice_Straw", "Straw_Bag", "Jar01a", "Jar01b", "Bamboo_Basket", "Basket01a", "Basket01b",
        "Wicker_Basket", "Cart", "Cauldron01a", "Cauldron01b", "Brazer", "Anvil", "Broom01a",
        "Straw_Mat01b", "A_frame_Carrier01a", "Jars",
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
    private const string WallPath    = "Assets/Naganeupseong/Prefabs/Structure/Wall/Wall01c.prefab";
    private const string PostPath    = "Assets/Naganeupseong/Prefabs/Structure/Wall/Wall03c.prefab";
    private const string DoorPath    = "Assets/Naganeupseong/Prefabs/Structure/Door/Door01k.prefab";

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
        Debug.Log("[Arena3D] 씬 생성 완료 → " + ScenePath
                + "  (팔각 담장 내접반지름 " + Inradius.ToString("F1") + "m, 투기장 지름 " + (Inradius * 2f).ToString("F1") + "m)");

        BakeOcclusion();
    }

    /// <summary>
    /// 오클루전 굽기. 씬 규모에 따라 수십 초 걸릴 수 있고 그동안 에디터가 멈춘다.
    /// 배치를 바꿀 때마다 다시 구워야 하므로 따로 빼 두었다.
    /// </summary>
    [MenuItem("Tools/Arena3D/오클루전 굽기")]
    public static void BakeOcclusion()
    {
        // smallestOccluder 를 담장 높이(1.68m)보다 작게 잡아야 담장이 가림막으로 인정된다.
        StaticOcclusionCulling.smallestOccluder = 1.2f;
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

    /// <summary>조명과 환경은 데모 씬 값을 그대로 쓴다. 이 팩 텍스처가 이 세팅에서 제일 제대로 보인다.</summary>
    private static void SetupCameraAndLight()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            // 담장 안쪽에 둔다. 바깥에 두면 앞쪽 담장이 화면 아래를 가로막는다.
            cam.transform.position = new Vector3(0f, 3.4f, -(Inradius - 1.6f));
            cam.transform.LookAt(new Vector3(0f, 1.3f, 0f));
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.2f;
            // 링 바깥까지만 보이면 된다. 멀리 자를수록 프러스텀 컬링이 일을 더 한다.
            cam.farClipPlane = 80f;
        }

        var light = Object.FindAnyObjectByType<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            light.transform.rotation = Quaternion.Euler(36f, 136f, 15f);   // 데모와 동일
            light.intensity = 1f;
            light.color = new Color(1f, 0.957f, 0.839f);                   // 데모의 따뜻한 색
            light.shadows = LightShadows.Soft;
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.fog = false;
    }

    /// <summary>바닥 타일을 팔각형 안쪽에 깐다. 타일 피벗이 모서리라 중심을 반 칸 밀어 계산한다.</summary>
    private static void BuildGround(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundPath);
        if (prefab == null) { Warn(GroundPath); return; }

        int half = Mathf.CeilToInt(Circumradius / GroundTile) + 1;
        int placed = 0;

        for (int ix = -half; ix <= half; ix++)
        {
            for (int iz = -half; iz <= half; iz++)
            {
                // 타일 중심이 팔각형 안에 들어올 때만 깐다.
                // 원으로 자르면 변 중앙(내접반지름)에서 바닥이 담장 밖으로 삐져나온다.
                // 타일이 3m 라 경계에서 조금 넘치는 건 남지만, 담장이 그 위에 서서 가려준다.
                var center = new Vector3((ix + 0.5f) * GroundTile, 0f, (iz + 0.5f) * GroundTile);
                float ang = Mathf.Atan2(center.z, center.x);
                if (center.magnitude > WallDistanceAt(ang)) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                // 타일 윗면이 y = 0.5 이므로 0.5 만큼 내려야 바닥이 y = 0 이 된다.
                go.transform.position = new Vector3(ix * GroundTile, -0.5f, iz * GroundTile);
                placed++;
            }
        }

        Debug.Log("[Arena3D] 바닥 타일 " + placed + "장");
    }

    /// <summary>
    /// 팔각 담장. 한 변마다 Wall01c 를 2.13m 간격 직선 런으로 놓고, 꼭짓점에 Wall03c 기둥을 세운다.
    /// 데모가 담장을 rotY 0/90 직선 런으로만 놓기 때문에 원을 곡선으로 두르지 않고 이렇게 간다.
    /// 마주보는 두 변의 가운데 칸은 Wall01c 대신 Door01k 를 끼워 출입구로 쓴다.
    /// </summary>
    private static void BuildWallRing(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var wall = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
        var post = AssetDatabase.LoadAssetAtPath<GameObject>(PostPath);
        var door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        if (wall == null) { Warn(WallPath); return; }

        int walls = 0, posts = 0, doors = 0;

        for (int side = 0; side < Sides; side++)
        {
            Vector3 a = VertexAt(side);
            Vector3 b = VertexAt(side + 1);
            Vector3 dir = (b - a).normalized;
            Quaternion rot = Quaternion.LookRotation(dir);

            // Wall01c 몸통은 피벗에서 로컬 -X 로 0.23 밀려 있다. 로컬 +X 가 바깥이므로
            // 그만큼 밖으로 내보내야 담장 면이 의도한 팔각형 위에 정확히 앉는다.
            Vector3 shift = (rot * Vector3.right) * WallSideOffset;

            bool isGateSide = System.Array.IndexOf(GateSides, side) >= 0;

            for (int j = 0; j < SegmentsPerSide; j++)
            {
                Vector3 pos = a + dir * (j * WallSegment) + shift;

                if (isGateSide && j == GateSegment && door != null)
                {
                    // 문은 피벗이 가운데라 칸의 중앙에 놓는다.
                    var d = (GameObject)PrefabUtility.InstantiatePrefab(door, root);
                    d.transform.SetPositionAndRotation(pos + dir * (WallSegment * 0.5f), rot);
                    doors++;
                    continue;
                }

                var w = (GameObject)PrefabUtility.InstantiatePrefab(wall, root);
                w.transform.SetPositionAndRotation(pos, rot);
                walls++;
            }

            // 꼭짓점 기둥. 데모에서 Wall03c 가 놓이는 자리와 같은 역할이다.
            if (post != null)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(post, root);
                p.transform.SetPositionAndRotation(a + shift, rot);
                posts++;
            }
        }

        Debug.Log("[Arena3D] 담장 Wall01c " + walls + " / 기둥 Wall03c " + posts + " / 문 Door01k " + doors
                + "  (팔각 한 변 " + SideLength.ToString("F2") + "m)");
    }

    /// <summary>팔각형 꼭짓점. 한 변이 -Z 쪽 정면에 오도록 half-step 돌려 둔다.</summary>
    private static Vector3 VertexAt(int index)
    {
        float step = 2f * Mathf.PI / Sides;
        float ang = step * index + step * 0.5f + Mathf.PI * 0.5f;
        return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Circumradius;
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
            // +Z 가 원 중심을 향하게 세운다. (데모는 격자 마을이라 rotY 가 90도 단위지만
            //  링으로 두르려면 임의 각도가 필요하다. 여기가 데모와 다른 유일한 지점이다)
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

    /// <summary>
    /// 담장 안쪽을 따라 프롭을 흩어 놓는다. 데모가 집 주변에 프롭을 빽빽하게 까는 편이라
    /// 여기서도 넉넉히 뿌린다. 위치는 고정 시드라 다시 만들어도 같은 그림이 나온다.
    /// </summary>
    private static void BuildProps(Transform root, Transform parent)
    {
        root.SetParent(parent, false);

        var state = Random.state;
        Random.InitState(20260909);

        for (int i = 0; i < PropCount; i++)
        {
            string name = PropNames[i % PropNames.Length];
            var prefab = FindPrefab(name, PropFolder);
            if (prefab == null) { Warn(name); continue; }

            float deg = 360f * i / PropCount + Random.Range(-4f, 4f);
            float rad = deg * Mathf.Deg2Rad;

            // 담장 안쪽 면에 붙인다. 팔각형이라 방향에 따라 벽까지 거리가 달라지므로
            // 내접반지름이 아니라 그 방향의 실제 벽 거리를 구해서 뺀다.
            float wallDist = WallDistanceAt(rad);
            float r = wallDist - Random.Range(0.7f, 1.6f);

            var pos = new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);

            // 출입구 앞은 막지 않는다.
            if (IsNearGate(pos)) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        Random.state = state;
        Debug.Log("[Arena3D] 프롭 " + root.childCount + "개");
    }

    /// <summary>중심에서 그 각도로 나갔을 때 팔각 담장까지의 거리.</summary>
    private static float WallDistanceAt(float rad)
    {
        // 팔각형은 내접반지름을 반각 주기로 되풀이한다. 변 중앙에서 가장 가깝고 꼭짓점에서 가장 멀다.
        float step = 2f * Mathf.PI / Sides;
        float local = Mathf.Repeat(rad - Mathf.PI * 0.5f - step * 0.5f, step) - step * 0.5f;
        return Inradius / Mathf.Cos(local);
    }

    /// <summary>출입구 문짝에서 2.5m 안쪽인지.</summary>
    private static bool IsNearGate(Vector3 p)
    {
        foreach (int side in GateSides)
        {
            Vector3 a = VertexAt(side);
            Vector3 dir = (VertexAt(side + 1) - a).normalized;
            Vector3 gate = a + dir * ((GateSegment + 0.5f) * WallSegment);
            if (Vector3.Distance(p, gate) < 2.5f) return true;
        }
        return false;
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
