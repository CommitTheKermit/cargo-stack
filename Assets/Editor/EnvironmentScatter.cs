using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 도로 주변에 나무와 바위를 코드로 흩뿌린다.
    ///
    /// 씬은 손으로 고치지 않고 <see cref="PrototypeSceneBuilder"/> 가 다시 만든다는 원칙(AGENTS.md 3장)을
    /// 그대로 따른다. 그래서 환경도 씬에 손으로 심지 않고 여기서 재생성한다.
    ///
    /// 기본 스테이지는 Broken Vector 의 "Low Poly Tree Pack" / "Low Poly Rock Pack" 을 쓴다
    /// (Assets/Environment/LowPoly*Pack). 겨울 스테이지는 이 경로를 사용하지 않는다.
    /// 겨울에는 사용자가 지정한 MochiModels 의 "3D Low Poly Environment Assets" 패키지 안
    /// IcePrefabs 를 명시적으로 로드한다. 패키지에 없는 대체 나무·바위로 조용히 폴백하지 않아야
    /// 씬에서 Asset Store 미리보기와 다른 환경이 만들어지는 일을 막을 수 있다.
    /// 기본 팩은 .dae 모델과 공용 컬러시트 텍스처로 오므로, 프리팹 대신 임포트된 모델(GameObject)을
    /// 바로 인스턴스화하고 컬러시트 재질을 입힌다.
    /// 팩마다 모델 이름이 제각각이라(Tree Type1 01, Rock Type3 02 …) 이름으로는 못 잡는다. 대신
    /// 임포트되는 <b>팩 폴더 경로</b>에 "tree" / "rock" 이 들어간다는 점을 이용해 폴더 기준으로 모은다.
    /// 폴더명이 예상과 다르면 <see cref="TreeFolderKeyword"/> / <see cref="RockFolderKeyword"/> 만 고치면 된다.
    ///
    /// 배치는 시드를 고정한 결정론적 난수라, 씬을 다시 만들어도 같은 결과가 나온다.
    /// 씬 파일이 재생성물이므로(협업 규칙) 실행마다 결과가 흔들리면 안 되기 때문이다.
    /// </summary>
    public static class EnvironmentScatter
    {
        // 프로젝트 자체 에셋(짐·차량 등)은 후보에서 뺀다. 환경 팩은 Assets/Environment 아래에 둔다.
        private static readonly string[] ExcludedFolders =
        {
            "Assets/Art",
            "Assets/Audio",
            "Assets/Editor",
            "Assets/Materials",
            "Assets/Meshes",
            "Assets/Scenes",
            "Assets/Scripts",
            "Assets/Stages",
            "Assets/Tests",
        };

        private const string TreeFolderKeyword = "tree";
        private const string RockFolderKeyword = "rock";

        // Asset Store 패키지의 실제 폴더명에는 게시자가 입력한 오타(Enivronment)가 포함되어 있다.
        // 이름을 임의로 고치지 않고, 임포트된 패키지의 원래 경로를 그대로 참조한다.
        private const string WinterAssetFolder =
            "Assets/3D Enivronment Assets/Prefabs/IcePrefabs";
        private const string WinterAssetRoot = "Assets/3D Enivronment Assets";
        private const string WinterAssetPrefix = "MochiModels_";

        private const string MaterialFolder = "Assets/Environment";
        private const string TreeMaterialPath = MaterialFolder + "/Environment_TreeMaterial.mat";
        private const string RockMaterialPath = MaterialFolder + "/Environment_RockMaterial.mat";

        // 컬러시트가 여러 벌이라 기본 스테이지에서 계절/색을 하나 고른다. 없으면 아무거나 첫 번째를 쓴다.
        private static readonly string[] DefaultTreeColorPreference = { "normal" };
        private static readonly string[] RockColorPreference = { "grey", "gray" };

        // 도로 가장자리에서 이만큼 떨어진 곳부터 심는다. 도로 위나 갓길 적재 공간을 침범하지 않게 한다.
        private const float EdgeMargin = 2f;

        // 겨울 패키지 프리팹은 원본 피벗과 가로 크기가 제각각이다. 특히 IcePlatform은
        // 높이에 비해 폭이 매우 넓어, 고정된 중심점 오프셋만으로는 얼음 도로를 침범할 수 있다.
        // 실제 Renderer.bounds를 기준으로 이 여백까지 확보한 뒤 도로 바깥으로 밀어낸다.
        private const float WinterRoadVisualClearance = 5f;

        // 가장자리 여백부터 바깥으로 이만큼의 띠 안에 흩뿌린다.
        private const float BandDepth = 27f;

        // 경로를 따라 이 간격(m)마다 심을지 말지 판정한다. 작을수록 빽빽해진다.
        private const float StepAlongRoute = 3.2f;

        // 각 판정 지점에서 한쪽에 나무·바위가 실제로 설 확률.
        private const float PlacementChance = 0.92f;

        // 한 판정 지점에서 한쪽에 여러 그루를 심을 확률. 숲처럼 뭉치게 한다.
        private const float ExtraClusterChance = 0.45f;

        // 심는 것 중 바위의 비율. 나머지는 나무.
        private const float RockRatio = 0.28f;

        // 모델을 이 높이(m)에 맞춰 크기를 정규화한다. 원본 단위가 무엇이든 일정한 크기로 선다.
        private const float TreeTargetHeight = 4f;
        private const float RockTargetHeight = 1.1f;

        // 정규화 높이에 주는 무작위 배율 범위. 줄지어 있어도 티가 덜 나게 한다.
        private const float MinScale = 0.7f;
        private const float MaxScale = 1.4f;

        /// <summary>
        /// 도로 양옆에 환경을 흩뿌린 "Environment" 루트를 만들어 돌려준다.
        /// 나무·바위 모델을 하나도 찾지 못하면(아직 임포트 전이면) 경고만 남기고 null 을 돌려준다.
        /// </summary>
        /// <param name="route">도로 중심선.</param>
        /// <param name="sceneName">난수 시드로 쓴다. 스테이지마다 다른 배치가 나오되 재생성해도 같게 유지된다.</param>
        /// <param name="roadHalfWidth">도로 절반 폭. 이보다 안쪽에는 심지 않는다.</param>
        /// <param name="startClearance">경로 시작에서 이 거리(m)까지는 비워 둔다. 갓길 적재 시야를 틔운다.</param>
        /// <param name="groundDrop">지면이 도로 윗면보다 낮은 높이. 나무·바위 바닥을 이 잔디면에 맞춘다.</param>
        public static GameObject Scatter(
            RoutePath route,
            string sceneName,
            float roadHalfWidth,
            float startClearance,
            float groundDrop)
        {
            return Scatter(route, sceneName, roadHalfWidth, startClearance, groundDrop, false);
        }

        public static GameObject Scatter(
            RoutePath route,
            string sceneName,
            float roadHalfWidth,
            float startClearance,
            float groundDrop,
            bool winter)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (winter)
            {
                return ScatterWinter(
                    route,
                    sceneName,
                    roadHalfWidth,
                    startClearance,
                    groundDrop);
            }

            GameObject[] trees = DiscoverModels(TreeFolderKeyword);
            GameObject[] rocks = DiscoverModels(RockFolderKeyword);

            if (trees.Length == 0 && rocks.Length == 0)
            {
                Debug.LogWarning(
                    "[CargoStack] 나무·바위 모델을 찾지 못해 환경 배치를 건너뛴다. "
                    + "Broken Vector 'Low Poly Tree Pack' / 'Low Poly Rock Pack' 을 "
                    + "Assets/Environment 아래에 둔 뒤 씬을 다시 만든다.");
                return null;
            }

            Material treeMaterial = EnsureColorsheetMaterial(
                TreeMaterialPath,
                TreeFolderKeyword,
                DefaultTreeColorPreference);
            Material rockMaterial = EnsureColorsheetMaterial(
                RockMaterialPath, RockFolderKeyword, RockColorPreference);

            var environment = new GameObject("Environment").transform;
            Transform treeRoot = new GameObject("Trees").transform;
            Transform rockRoot = new GameObject("Rocks").transform;
            treeRoot.SetParent(environment, false);
            rockRoot.SetParent(environment, false);

            // 시드를 씬 이름에서 뽑아 스테이지마다 다르되 실행마다 같은 배치를 얻는다.
            var random = new System.Random(StableHash(sceneName));

            float length = route.TotalLength;
            float minRadius = roadHalfWidth + EdgeMargin;
            int treeCount = 0;
            int rockCount = 0;
            Vector3 firstTreeSize = Vector3.zero;

            for (float distance = startClearance; distance < length; distance += StepAlongRoute)
            {
                Vector3 center = route.PositionAt(distance);
                Vector3 side = SideDirection(route, distance, length);
                Vector3 along = TangentDirection(route, distance, length);

                // 왼쪽(-1)과 오른쪽(+1)을 따로 판정한다. 양옆이 대칭으로 차지 않게 한다.
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    if (random.NextDouble() > PlacementChance)
                    {
                        continue;
                    }

                    // 이 자리의 기준 반지름. 뭉쳐 심는 나무들은 이 근처에 모인다.
                    float baseRadius = minRadius + (float)random.NextDouble() * BandDepth;

                    // 한 그루 심고, 주사위가 계속 맞으면 바로 옆에 더 심어 숲처럼 뭉치게 한다.
                    do
                    {
                        bool placeRock = random.NextDouble() < RockRatio;
                        GameObject[] palette = placeRock ? rocks : trees;
                        if (palette.Length == 0)
                        {
                            placeRock = !placeRock; // 한쪽 팩만 있으면 있는 쪽을 쓴다.
                            palette = placeRock ? rocks : trees;
                        }

                        GameObject model = palette[random.Next(palette.Length)];
                        float radius = baseRadius + ((float)random.NextDouble() - 0.5f) * 6f;
                        // 경로 접선 방향으로도 흔들어 격자처럼 줄 서는 것을 깬다.
                        float alongJitter = ((float)random.NextDouble() - 0.5f) * StepAlongRoute * 1.6f;
                        Vector3 position = center + side * (sign * radius) + along * alongJitter;
                        // 도로 윗면보다 낮은 잔디 지면 위에 세운다. 더 이상 도로 높이에 뜨지 않는다.
                        position.y = center.y - groundDrop;

                        Transform parent = placeRock ? rockRoot : treeRoot;
                        string name = placeRock ? $"Rock_{rockCount:000}" : $"Tree_{treeCount:000}";
                        Material material = placeRock ? rockMaterial : treeMaterial;
                        float targetHeight = placeRock ? RockTargetHeight : TreeTargetHeight;

                        Vector3 size = PlaceInstance(
                            model, parent, name, position, material, targetHeight, random);

                        if (placeRock)
                        {
                            rockCount++;
                        }
                        else
                        {
                            if (treeCount == 0)
                            {
                                firstTreeSize = size;
                            }

                            treeCount++;
                        }
                    }
                    while (random.NextDouble() < ExtraClusterChance);
                }
            }

            // 첫 나무의 크기를 남겨 세워졌는지(높이 y 가 가로/세로보다 큰지) 확인할 수 있게 한다.
            Debug.Log(
                $"[CargoStack] 환경 배치 완료: 나무 {treeCount}그루, 바위 {rockCount}개 "
                + $"(나무 모델 {trees.Length}종, 바위 모델 {rocks.Length}종, "
                + "나무 소스 기본 환경 팩, 계절 기본). "
                + $"첫 나무 크기(WxHxD) = {firstTreeSize.x:0.0} x {firstTreeSize.y:0.0} x {firstTreeSize.z:0.0} m");
            return environment.gameObject;
        }

        /// <summary>
        /// MochiModels의 실제 겨울 프리팹으로 눈밭을 구성한다.
        ///
        /// 이 경로에서는 이름 검색이나 다른 환경 팩으로의 폴백을 하지 않는다. Asset Store
        /// 패키지의 IceTree, IceMountain, IceCave, IceRock, IcePlatform, Snowman을 직접
        /// 인스턴스화해야 패키지 미리보기와 같은 세계 요소가 씬에 남는다.
        /// </summary>
        private static GameObject ScatterWinter(
            RoutePath route,
            string sceneName,
            float roadHalfWidth,
            float startClearance,
            float groundDrop)
        {
            GameObject iceTree = LoadWinterPrefab("IceTree");
            GameObject[] iceRocks =
            {
                LoadWinterPrefab("IceRock_01"),
                LoadWinterPrefab("IceRock_02"),
                LoadWinterPrefab("IceRock_03"),
            };
            GameObject[] iceMountains =
            {
                LoadWinterPrefab("IceMountain_01"),
                LoadWinterPrefab("IceMountain_02"),
                LoadWinterPrefab("IceMountain_03"),
            };
            GameObject iceCave = LoadWinterPrefab("IceCave");
            GameObject[] icePlatforms =
            {
                LoadWinterPrefab("IcePlatform_01"),
                LoadWinterPrefab("IcePlatform_02"),
            };
            GameObject[] snowmen =
            {
                LoadWinterPrefab("Snowman_01"),
                LoadWinterPrefab("Snowman_02"),
            };

            var environment = new GameObject("Environment").transform;
            Transform landmarkRoot = CreateEnvironmentChild(environment, "IceLandmarks");
            Transform treeRoot = CreateEnvironmentChild(environment, "Trees");
            Transform rockRoot = CreateEnvironmentChild(environment, "Rocks");
            Transform snowmanRoot = CreateEnvironmentChild(environment, "Snowmen");
            Transform platformRoot = CreateEnvironmentChild(environment, "IcePlatforms");

            var random = new System.Random(StableHash(sceneName + ":MochiModels"));
            float length = route.TotalLength;
            int treeCount = 0;
            int rockCount = 0;

            // 시작 구간에도 실제 IceTree를 배치해 첫 플레이 화면에서 겨울 에셋이 보이게 한다.
            // 도로에 붙이지 않고 눈 지면 안쪽에 두어 주행 공간과 적재 시야를 비워 둔다.
            const float TreeSpacing = 11f;
            for (float distance = startClearance + 4f, index = 0f;
                distance < length - 6f;
                distance += TreeSpacing, index++)
            {
                Vector3 center = route.PositionAt(distance);
                Vector3 side = SideDirection(route, distance, length);
                Vector3 along = TangentDirection(route, distance, length);

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float offset = roadHalfWidth + 6.5f + (float)random.NextDouble() * 4f;
                    float alongJitter = ((float)random.NextDouble() - 0.5f) * 2.5f;
                    Vector3 position = center
                        + side * (sign * offset)
                        + along * alongJitter;
                    position.y = center.y - groundDrop;

                    PlaceWinterInstance(
                        route,
                        iceTree,
                        treeRoot,
                        $"{WinterAssetPrefix}IceTree_{treeCount:000}",
                        position,
                        null,
                        4.8f,
                        random,
                        center,
                        side,
                        sign,
                        roadHalfWidth);
                    treeCount++;

                    // 바위는 나무 사이에만 놓아 눈밭이 반복되는 띠처럼 보이지 않게 한다.
                    if (((int)index + sign) % 2 == 0)
                    {
                        Vector3 rockPosition = center
                            + side * (sign * (offset + 2.5f))
                            + along * (alongJitter + 2.2f);
                        rockPosition.y = center.y - groundDrop;
                        GameObject rock = iceRocks[random.Next(iceRocks.Length)];
                        PlaceWinterInstance(
                            route,
                            rock,
                            rockRoot,
                            $"{WinterAssetPrefix}IceRock_{rockCount:000}",
                            rockPosition,
                            null,
                            2.4f,
                            random,
                            center,
                            side,
                            sign,
                            roadHalfWidth);
                        rockCount++;
                    }
                }
            }

            // 패키지 미리보기의 핵심 실루엣인 눈 덮인 얼음 산·동굴을 경로 바깥에 세운다.
            float[] mountainProgress = { 0.12f, 0.34f, 0.58f, 0.80f, 0.94f };
            int mountainCount = 0;
            foreach (float progress in mountainProgress)
            {
                float distance = Mathf.Lerp(startClearance + 4f, length - 6f, progress);
                Vector3 center = route.PositionAt(distance);
                Vector3 side = SideDirection(route, distance, length);
                int sign = mountainCount % 2 == 0 ? -1 : 1;
                Vector3 position = center + side * (sign * (roadHalfWidth + 19f));
                position.y = center.y - groundDrop;
                PlaceWinterInstance(
                    route,
                    iceMountains[mountainCount % iceMountains.Length],
                    landmarkRoot,
                    $"{WinterAssetPrefix}IceMountain_{mountainCount:00}",
                    position,
                    null,
                    15f + (mountainCount % 3) * 2f,
                    random,
                    center,
                    side,
                    sign,
                    roadHalfWidth);
                mountainCount++;
            }

            float caveDistance = Mathf.Lerp(startClearance + 4f, length - 6f, 0.47f);
            Vector3 caveRouteCenter = route.PositionAt(caveDistance);
            Vector3 caveCenter = caveRouteCenter;
            Vector3 caveSide = SideDirection(route, caveDistance, length);
            caveCenter += caveSide * (roadHalfWidth + 17f);
            caveCenter.y = caveRouteCenter.y - groundDrop;
            PlaceWinterInstance(
                route,
                iceCave,
                landmarkRoot,
                $"{WinterAssetPrefix}IceCave",
                caveCenter,
                null,
                11f,
                random,
                caveRouteCenter,
                caveSide,
                1,
                roadHalfWidth);

            // 얼음 플랫폼은 넓은 원본 외곽 때문에 도로 가장자리에서 멀리 떼고,
            // 산과 산 사이의 낮은 지형에 작게 두어 공식 패키지의 빙하 지형을 보강한다.
            float[] platformProgress = { 0.27f, 0.72f };
            for (int index = 0; index < platformProgress.Length; index++)
            {
                float distance = Mathf.Lerp(startClearance + 5f, length - 7f, platformProgress[index]);
                Vector3 center = route.PositionAt(distance);
                Vector3 side = SideDirection(route, distance, length);
                int sign = index == 0 ? -1 : 1;
                Vector3 position = center + side * (sign * (roadHalfWidth + 12f));
                position.y = center.y - groundDrop;
                PlaceWinterInstance(
                    route,
                    icePlatforms[index],
                    platformRoot,
                    $"{WinterAssetPrefix}IcePlatform_{index:00}",
                    position,
                    null,
                    2.2f,
                    random,
                    center,
                    side,
                    sign,
                    roadHalfWidth);
            }

            // 눈사람은 시작·중간·도착의 눈 지면에 배치해 실제 패키지의 캐릭터 소품도 노출한다.
            float[] snowmanProgress = { 0.08f, 0.50f, 0.90f };
            for (int index = 0; index < snowmanProgress.Length; index++)
            {
                float distance = Mathf.Lerp(startClearance + 3f, length - 5f, snowmanProgress[index]);
                Vector3 center = route.PositionAt(distance);
                Vector3 side = SideDirection(route, distance, length);
                int sign = index % 2 == 0 ? 1 : -1;
                Vector3 position = center + side * (sign * (roadHalfWidth + 5.5f));
                position.y = center.y - groundDrop;
                PlaceWinterInstance(
                    route,
                    snowmen[index % snowmen.Length],
                    snowmanRoot,
                    $"{WinterAssetPrefix}Snowman_{index:00}",
                    position,
                    null,
                    2.1f,
                    random,
                    center,
                    side,
                    sign,
                    roadHalfWidth);
            }

            Debug.Log(
                $"[CargoStack] 겨울 환경 배치 완료: MochiModels 실제 프리팹 사용 "
                + $"(IceTree {treeCount}개, IceRock {rockCount}개, "
                + $"IceMountain {mountainCount}개, IceCave 1개, "
                + $"IcePlatform {platformProgress.Length}개, Snowman {snowmanProgress.Length}개)");
            return environment.gameObject;
        }

        private static Transform CreateEnvironmentChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static GameObject LoadWinterPrefab(string prefabName)
        {
            string path = $"{WinterAssetFolder}/{prefabName}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "MochiModels 겨울 에셋을 찾지 못했다: " + path
                    + ". Unity Asset Store의 '3D Low Poly Environment Assets'를 먼저 임포트해야 한다.");
            }

            return prefab;
        }

        [MenuItem("CargoStack/환경/발견된 나무·바위 모델 로그")]
        private static void LogDiscoveredModels()
        {
            GameObject[] trees = DiscoverModels(TreeFolderKeyword);
            GameObject[] rocks = DiscoverModels(RockFolderKeyword);
            Debug.Log(
                $"[CargoStack] 나무 모델 {trees.Length}종: "
                + string.Join(", ", Array.ConvertAll(trees, item => item.name)));
            Debug.Log(
                $"[CargoStack] 바위 모델 {rocks.Length}종: "
                + string.Join(", ", Array.ConvertAll(rocks, item => item.name)));
        }

        /// <summary>
        /// 폴더 경로에 키워드가 든 모델·프리팹을 모두 모은다. 프로젝트 자체 에셋 폴더는 뺀다.
        /// 경로 사전순으로 정렬해 순서가 실행마다 흔들리지 않게 한다.
        /// </summary>
        private static GameObject[] DiscoverModels(string folderKeyword)
        {
            // t:GameObject 는 프리팹과 임포트된 모델(.dae/.fbx)을 모두 잡는다.
            string[] guids = AssetDatabase.FindAssets("t:GameObject");
            var matched = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsExcluded(path))
                {
                    continue;
                }

                if (path.ToLowerInvariant().Contains(folderKeyword))
                {
                    matched.Add(path);
                }
            }

            matched.Sort(StringComparer.Ordinal);

            var models = new List<GameObject>(matched.Count);
            foreach (string path in matched)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                // 렌더러가 없는 것(빈 프리팹 등)은 배경으로 쓸 수 없으니 뺀다.
                if (model != null && model.GetComponentInChildren<Renderer>() != null)
                {
                    models.Add(model);
                }
            }

            return models.ToArray();
        }

        private static bool IsExcluded(string path)
        {
            if (path.StartsWith(WinterAssetRoot, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string folder in ExcludedFolders)
            {
                if (path.StartsWith(folder, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 모델을 심고 재질·크기·바닥 정렬을 맞춘다. 정규화한 뒤의 월드 크기를 돌려준다.
        /// </summary>
        private static Vector3 PlaceInstance(
            GameObject model,
            Transform parent,
            string name,
            Vector3 groundPosition,
            Material material,
            float targetHeight,
            System.Random random)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = name;
            instance.transform.SetParent(parent, false);

            // 임포트된 모델의 재질은 자주 비어 있어(분홍) 컬러시트 재질로 덮는다. 그림자는 남긴다.
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }

                renderer.shadowCastingMode = ShadowCastingMode.On;
            }

            // Y 축 회전만 무작위로 준다. 나무·바위는 세워 두는 물건이라 눕히면 어색하다.
            instance.transform.position = groundPosition;
            instance.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

            // 원본 단위가 무엇이든 목표 높이에 맞춘다. 크기 변주는 목표 높이에 곱한다.
            Bounds bounds = GetRendererBounds(instance);
            if (bounds.size.y > 1e-4f)
            {
                float variation = MinScale + (float)random.NextDouble() * (MaxScale - MinScale);
                float factor = targetHeight * variation / bounds.size.y;
                instance.transform.localScale *= factor;
            }

            // 크기를 맞춘 뒤 바닥면이 지면 높이에 닿도록 내려놓는다. 떠 있거나 파묻히지 않게 한다.
            bounds = GetRendererBounds(instance);
            float lift = groundPosition.y - bounds.min.y;
            instance.transform.position += new Vector3(0f, lift, 0f);

            if (parent.name == "Trees" || parent.name == "Rocks")
            {
                AddMeshColliders(instance);
            }

            return GetRendererBounds(instance).size;
        }

        private static void AddMeshColliders(GameObject instance)
        {
            foreach (MeshFilter meshFilter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null || meshFilter.GetComponent<Collider>() != null)
                {
                    continue;
                }

                meshFilter.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.sharedMesh;
            }
        }

        /// <summary>
        /// 겨울 프리팹의 실제 렌더러 외곽이 도로 회랑을 침범하지 않게 옆으로 민다.
        /// 프리팹마다 피벗·가로 크기가 달라 고정 오프셋만으로는 IcePlatform 같은 넓은
        /// 지형이 도로를 가리는 문제가 생긴다. 시각 경계만 조정하고, 패키지 프리팹의
        /// 모양·재질·배치는 그대로 유지한다.
        /// </summary>
        private static Vector3 PlaceWinterInstance(
            RoutePath route,
            GameObject model,
            Transform parent,
            string name,
            Vector3 groundPosition,
            Material material,
            float targetHeight,
            System.Random random,
            Vector3 routeCenter,
            Vector3 side,
            int sideSign,
            float roadHalfWidth)
        {
            Vector3 size = PlaceInstance(
                model,
                parent,
                name,
                groundPosition,
                material,
                targetHeight,
                random);

            GameObject instance = parent.Find(name)?.gameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"겨울 환경 인스턴스를 찾지 못했다: {parent.name}/{name}");
            }

            KeepWinterInstanceOffRoad(
                route,
                instance,
                routeCenter,
                side,
                sideSign,
                roadHalfWidth);
            return size;
        }

        private static void KeepWinterInstanceOffRoad(
            RoutePath route,
            GameObject instance,
            Vector3 routeCenter,
            Vector3 side,
            int sideSign,
            float roadHalfWidth)
        {
            Bounds bounds = GetRendererBounds(instance);
            float lateralExtent = Mathf.Abs(side.x) * bounds.extents.x
                + Mathf.Abs(side.z) * bounds.extents.z;
            float signedCenterDistance = Vector3.Dot(
                bounds.center - routeCenter,
                side) * sideSign;
            float requiredDistance = roadHalfWidth
                + WinterRoadVisualClearance
                + lateralExtent;

            if (signedCenterDistance < requiredDistance)
            {
                instance.transform.position += side
                    * (sideSign * (requiredDistance - signedCenterDistance));
            }

            // S자 경로에서는 배치 기준점에서 멀어져도 다른 곡선 구간이
            // 크게 휘어 인스턴스 뒤로 가까이 올 수 있다. 전체 경로를 기준으로
            // 렌더러 외곽과의 최소 거리를 다시 확인해 곡선 안쪽의 침범을 막는다.
            const int MaximumPushAttempts = 16;
            for (int attempt = 0; attempt < MaximumPushAttempts; attempt++)
            {
                bounds = GetRendererBounds(instance);
                float closestDistance = float.PositiveInfinity;
                for (int sample = 0; sample < route.SampleCount; sample++)
                {
                    Vector3 routePoint = route.SampleAt(sample);
                    float closestX = Mathf.Clamp(routePoint.x, bounds.min.x, bounds.max.x);
                    float closestZ = Mathf.Clamp(routePoint.z, bounds.min.z, bounds.max.z);
                    float distance = Vector2.Distance(
                        new Vector2(routePoint.x, routePoint.z),
                        new Vector2(closestX, closestZ));
                    closestDistance = Mathf.Min(closestDistance, distance);
                }

                if (closestDistance >= requiredDistance)
                {
                    break;
                }

                instance.transform.position += side
                    * (sideSign * (requiredDistance - closestDistance + 0.1f));
            }
        }

        private static Bounds GetRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// 컬러시트 텍스처를 입힌 Standard 재질을 만들거나 불러온다.
        /// 텍스처는 팩 폴더 안에서 키워드+선호색으로 고른다.
        /// </summary>
        private static Material EnsureColorsheetMaterial(
            string materialPath,
            string folderKeyword,
            string[] colorPreference)
        {
            System.IO.Directory.CreateDirectory(MaterialFolder);

            Texture2D texture = FindColorsheetTexture(folderKeyword, colorPreference);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = Shader.Find("Standard");
            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);
                material.color = Color.white;
            }

            material.SetFloat("_Glossiness", 0.1f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D FindColorsheetTexture(string folderKeyword, string[] colorPreference)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            Texture2D fallback = null;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsExcluded(path))
                {
                    continue;
                }

                string lower = path.ToLowerInvariant();
                if (!lower.Contains(folderKeyword) || !lower.Contains("colorsheet"))
                {
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    continue;
                }

                fallback ??= texture;
                foreach (string prefer in colorPreference)
                {
                    if (lower.Contains(prefer))
                    {
                        return texture;
                    }
                }
            }

            return fallback;
        }

        /// <summary>경로 접선(진행 방향) 단위 벡터. 수평면에 눕혀서 쓴다.</summary>
        private static Vector3 TangentDirection(RoutePath route, float distance, float length)
        {
            const float delta = 0.5f;
            Vector3 ahead = route.PositionAt(Mathf.Min(distance + delta, length));
            Vector3 behind = route.PositionAt(Mathf.Max(distance - delta, 0f));
            Vector3 tangent = ahead - behind;
            tangent.y = 0f;
            return tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector3.forward;
        }

        /// <summary>도로 옆방향(접선을 수평에서 90도 돌린) 단위 벡터.</summary>
        private static Vector3 SideDirection(RoutePath route, float distance, float length)
        {
            Vector3 tangent = TangentDirection(route, distance, length);
            return Vector3.Cross(Vector3.up, tangent).normalized;
        }

        /// <summary>문자열에서 실행 간 안정적인 해시를 만든다. string.GetHashCode 는 실행마다 달라질 수 있다.</summary>
        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in value)
                {
                    hash = hash * 31 + c;
                }

                return hash;
            }
        }
    }
}
