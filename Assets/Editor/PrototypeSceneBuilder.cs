using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 검증용 프로토타입 씬을 코드로 만든다.
    /// 씬 파일은 git 자동 병합이 안 되므로(기획서 5장 협업 규칙), 손으로 고치는 대신
    /// 이 스크립트를 고쳐 다시 생성하는 것을 기본 절차로 삼는다.
    ///
    /// 트럭은 +X 방향으로 달린다. 짐칸 치수와 플레이어 규격은
    /// nan2026-cargo 스파이크에서 손맛이 검증된 값을 축 방향만 바꿔 가져왔다.
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string MaterialFolder = "Assets/Materials";
        private const string MeshFolder = "Assets/Meshes";
        private const string CargoArtFolder = "Assets/Art/Cargo";
        private const string VehicleArtFolder = "Assets/Art/Vehicles";
        private const string PrototypeAudioFolder = "Assets/Audio/Prototype";
        private const string BlueTruckFolder = VehicleArtFolder + "/BlueTruck";
        private const string BlueTruckMaterialPath = BlueTruckFolder + "/BlueTruckMaterial.mat";
        private const string BlueTruckCabGlassMeshPath =
            BlueTruckWheelMeshGenerator.GeneratedFolder + "/BlueTruckCabGlass.asset";
        private const string StageFolder = "Assets/Stages";
        private const string PrototypeStagePath = "Assets/Stages/Prototype/PrototypeStage.asset";
        private const string MainMenuScenePath = SceneFolder + "/MainMenu.unity";
        private const string IceRoadMaterialPath = MaterialFolder + "/IceRoad.mat";
        private const string SnowGroundMaterialPath = MaterialFolder + "/SnowGround.mat";
        private const string FontFolder = "Assets/Fonts";
        private const string TitleFontPath = FontFolder + "/Pretendard-Light.ttf";
        private const string BodyFontPath = FontFolder + "/Pretendard-Regular.ttf";
        private const string MenuBackgroundVideoPath = "Assets/Video/MenuBackground.mp4";

        // BlueTruck 메시의 수평면과 벽 삼각형을 계측한 Truck 로컬 좌표다.
        // v2 Z-up 원본을 Y-up으로 세우고 차량 앞(-Y)을 진행 방향 +X로 돌린 뒤
        // 원본 길이 0.999970m가 게임 규격 6.2m가 되도록 6.2002배로 맞춘다.
        private const float BedCenterX = -1.835f;
        private const float BedCenterZ = 0f;
        private const float BedFloorTop = 0.20f;
        private const float BedInsideLength = 2.29f;
        private const float BedInsideWidth = 2.26f;
        private const float BedWallHeight = 0.70f;
        private const float BedFrontBarrierX = -0.64f;
        private const float BedFrontBarrierHeight = 0.68f;
        private const float BedFrontBarrierWidth = 2.26f;
        private const float BedFloorThickness = 0.12f;
        private const float BedWallThickness = 0.12f;

        // 실제 BlueTruck 차체의 캐빈은 짐칸 앞 격벽 바로 뒤에서 시작한다. 기존 Cab 물리
        // 프록시만 표시하면 그 프록시 앞쪽의 실내 바닥(Chassis)에 놓는 경로가 남는다.
        // 이 볼륨은 배치 판정 전용 트리거라 주행 중 차체 충돌에는 관여하지 않는다.
        private const float CabInteriorForbiddenMinX = BedFrontBarrierX + BedWallThickness * 0.5f;
        private const float CabInteriorForbiddenMaxX = 3.10f;
        private const float CabInteriorForbiddenWidth = 2.75f;
        private const float CabInteriorForbiddenCenterY = 1.25f;
        private const float CabInteriorForbiddenHeight = 3.5f;

        // 짐칸 앞 격벽의 바로 뒤를 닫아, 낮은 격벽을 넘어온 화물이 캐빈 바닥으로
        // 떨어지지 않게 하는 물리 전용 벽이다. 렌더러는 만들지 않는다.
        private const float CabFrontCollisionWallThickness = BedWallThickness;
        private const float CabFrontCollisionWallWidth = BedFrontBarrierWidth;
        private const float CabFrontCollisionWallCenterY = CabInteriorForbiddenCenterY;
        private const float CabFrontCollisionWallHeight = CabInteriorForbiddenHeight;

        /// <summary>도로 표면에서 차체 원점까지의 높이. GroundSupport 콜라이더 바닥과 맞춘다.</summary>
        private const float RideHeight = 0.75f;

        private const float RoadWidth = 13f;
        private const float RoadThickness = 1.2f;
        private const float RoadTextureTileSize = 8f;

        /// <summary>도로 양옆에 까는 잔디 지면의 절반 폭. 나무·바위가 이 위에 선다.</summary>
        private const float GroundHalfWidth = 38f;
        private const float GroundThickness = 2f;
        private const float GroundTextureTileSize = 8f;

        /// <summary>
        /// 지면을 도로 윗면보다 이만큼만 살짝 낮춘다. 도로/지면 표면이 겹쳐 z-파이팅 나는 것은 막되,
        /// 플레이어 캡슐(반지름 0.4m)이 걸리지 않고 매끄럽게 넘어갈 만큼 낮은 턱이다.
        /// 도로와 땅의 구분은 높이가 아니라 재질 색(아스팔트/잔디)이 맡는다.
        /// </summary>
        private const float GroundDropBelowRoad = 0.05f;

        /// <summary>플레이어와 화물이 지면 리본 밖으로 빠지지 않게 두르는 보이지 않는 충돌벽.</summary>
        private const float GroundBoundaryHeight = 4f;
        private const float GroundBoundaryDepth = 1f;
        private const float GroundBoundaryThickness = 0.6f;
        private const float GroundBoundaryOverlap = 1.5f;

        /// <summary>도로 조각을 이웃과 겹치게 늘리는 배율. 커브 바깥쪽이 벌어지는 것을 막는다.</summary>
        private const float RoadBlockOverlap = 1.5f;

        /// <summary>
        /// 화물을 집을 수 있는 거리. 플레이어 발밑에서 화물 무게중심까지 잰다.
        /// 조준은 화면 중앙 레이캐스트가 하므로, 이 값을 늘려도 엉뚱한 상자가 집히지는 않는다.
        ///
        /// 3m 로 무엇이 닿고 무엇이 안 닿는지(트럭 옆 바퀴 바깥, 짐칸 중심에서 2.1m 지점 기준):
        /// - 짐칸 한복판까지 2.6m. 닿는다
        /// - 짐칸 건너편 구석까지 3.3m. 안 닿는다. 차체에 바짝 붙어야 겨우 닿는다
        /// 건너편 구석까지 서 있는 자리에서 닿게 하려면 3.4m 이상이 필요하다.
        /// </summary>
        private const float CargoReach = 3f;

        /// <summary>미리보기를 띄울 면을 찾는 거리. 집을 수 있는 거리보다 너무 멀면 되찾지 못할 곳에 놓게 된다.</summary>
        private const float CargoPlacementReach = 6f;

        [MenuItem("CargoStack/프로토타입 씬 다시 만들기")]
        public static void Build()
        {
            Build(LoadStageDefinition(PrototypeStagePath));
        }

        [MenuItem("CargoStack/스테이지/선택한 정의로 씬 만들기")]
        public static void BuildSelectedStage()
        {
            Build((StageDefinition)Selection.activeObject);
        }

        [MenuItem("CargoStack/스테이지/선택한 정의로 씬 만들기", true)]
        private static bool CanBuildSelectedStage()
        {
            return Selection.activeObject is StageDefinition;
        }

        [MenuItem("CargoStack/스테이지/모든 씬 다시 만들기")]
        public static void BuildAllStages()
        {
            string[] paths = FindStageDefinitionPaths();
            foreach (string path in paths)
            {
                Build(LoadStageDefinition(path));
            }

            BuildMainMenu();
        }

        [MenuItem("CargoStack/메인 메뉴 다시 만들기")]
        public static void BuildMainMenu()
        {
            string[] paths = FindStageDefinitionPaths();

            Directory.CreateDirectory(SceneFolder);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var visibleStages = new List<StageDefinition>();
            foreach (string path in paths)
            {
                StageDefinition definition = LoadStageDefinition(path);
                definition.ValidateOrThrow();
                if (definition.ShowInMenu)
                {
                    visibleStages.Add(definition);
                }
            }

            if (visibleStages.Count == 0)
            {
                throw new InvalidOperationException(
                    "메인 메뉴에 표시할 스테이지가 없다.");
            }

            var cameraObject = new GameObject("Main Camera")
            {
                tag = "MainCamera",
            };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            AttachMenuBackgroundVideo(camera);
            ConfigureBackgroundMusic();

            var menuObject = new GameObject("MainMenu");
            var controller = menuObject.AddComponent<MainMenuController>();
            controller.Configure(visibleStages.ToArray());
            controller.SetFonts(
                AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath),
                AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath));

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            EnsureSceneInBuildSettings(MainMenuScenePath, true);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[CargoStack] 메인 메뉴 생성 완료: {MainMenuScenePath} "
                + $"(표시 스테이지 {visibleStages.Count}개)");
        }

        /// <summary>
        /// 메인 메뉴 배경으로 게임플레이 녹화 영상을 카메라 근평면에 재생한다.
        /// 영상 위에는 MainMenuController 가 검은 필터와 글자를 얹는다(IMGUI 는 카메라 위에 그려진다).
        /// 아직 녹화 영상이 없으면 검은 배경으로 둔다. 영상은 CargoStack/메뉴 배경 영상 녹화 로 만든다.
        /// </summary>
        private static void AttachMenuBackgroundVideo(Camera camera)
        {
            var clip = AssetDatabase.LoadAssetAtPath<UnityEngine.Video.VideoClip>(
                MenuBackgroundVideoPath);
            if (clip == null)
            {
                Debug.LogWarning(
                    "[CargoStack] 메뉴 배경 영상을 찾지 못해 검은 배경으로 둔다: "
                    + MenuBackgroundVideoPath);
                return;
            }

            var player = camera.gameObject.AddComponent<UnityEngine.Video.VideoPlayer>();
            player.playOnAwake = true;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            player.renderMode = UnityEngine.Video.VideoRenderMode.CameraNearPlane;
            player.targetCamera = camera;
            player.clip = clip;
            player.aspectRatio = UnityEngine.Video.VideoAspectRatio.FitOutside;
            player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
        }

        /// <summary>
        /// 정의 하나를 플레이 가능한 씬 하나로 만든다.
        /// 아직 스테이지 선택 UI는 없으므로 메뉴는 Prototype만 호출하지만, 새 정의를 추가할 때
        /// 차량·카메라·게임 흐름 생성 코드를 복사하지 않도록 이 경계를 둔다.
        /// </summary>
        public static void Build(StageDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            definition.ValidateOrThrow();
            string definitionPath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(definitionPath))
            {
                throw new InvalidOperationException(
                    $"{definition.name}: 저장된 StageDefinition 에셋이 아니다.");
            }

            string scenePath = $"{SceneFolder}/{definition.SceneName}.unity";

            Directory.CreateDirectory(SceneFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            int groundLayer = EnsureLayer("Ground");

            bool isWinter = definition.Theme == StageTheme.Winter;

            // 겨울 스테이지는 얼음 도로와 눈 지면을 사용한다. Asset Store 텍스처가
            // 프로젝트에 임포트되어 있으면 이름으로 자동 연결하고, 아직 없으면 차가운
            // 단색 재질을 써서 씬 생성 자체는 막지 않는다.
            Material roadMaterial = isWinter
                ? EnsureWinterRoadMaterial()
                : EnsureColorMaterial("Road", new Color(0.30f, 0.30f, 0.33f));
            Material groundMaterial = isWinter
                ? EnsureSnowGroundMaterial()
                : EnsureColorMaterial("Grass", new Color(0.42f, 0.60f, 0.33f));
            Material truckMaterial = EnsureColorMaterial("Truck", new Color(0.26f, 0.48f, 0.62f));
            Material bedMaterial = EnsureColorMaterial("Bed", new Color(0.47f, 0.5f, 0.52f));
            Material playerMaterial = EnsureColorMaterial("Player", new Color(0.32f, 0.7f, 0.42f));

            PhysicsMaterial bedPhysics = EnsurePhysicsMaterial("BedSurface", 0.55f, 0.65f);
            PhysicsMaterial cargoPhysics = EnsurePhysicsMaterial("CargoSurface", 0.45f, 0.55f);
            PhysicsMaterial roadPhysics = isWinter
                ? EnsurePhysicsMaterial("IceRoadSurface", 0.02f, 0.02f)
                : null;

            ApplyPhysicsProjectSettings();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Single 모드로 새 씬을 열면 아직 씬에서 참조하지 않는 에셋이 언로드될 수 있다.
            // 저장 경로로 다시 불러와 StageContext가 영속적인 에셋 참조를 갖게 한다.
            definition = LoadStageDefinition(definitionPath);

            var stageObject = new GameObject("Stage");
            stageObject.AddComponent<StageContext>().Configure(definition);

            RoutePath route = BuildRoute(
                groundLayer,
                roadMaterial,
                roadPhysics,
                definition.CopyRouteControlPoints(),
                definition.SceneName);

            // 도로 아래에 경로 고저를 따라가는 넓은 지면을 깐다. 겨울에는 눈 지면이다.
            BuildGround(route, groundMaterial, groundLayer, definition.SceneName);

            // 도로 양옆 잔디 위에 나무·바위를 흩뿌린다. 모델이 아직 없으면 조용히 건너뛴다.
            EnvironmentScatter.Scatter(
                route,
                definition.SceneName,
                RoadWidth * 0.5f,
                definition.TruckStartDistance,
                GroundDropBelowRoad,
                isWinter);

            float goalDistance = route.TotalLength - definition.GoalOffsetFromEnd;
            if (goalDistance <= definition.TruckStartDistance)
            {
                throw new InvalidOperationException(
                    $"{definition.name}: 도착 거리({goalDistance:0.##}m)가 "
                    + $"출발 거리({definition.TruckStartDistance:0.##}m)보다 뒤에 있지 않다.");
            }

            CreateLighting(isWinter);

            GameObject truck = BuildTruck(
                truckMaterial,
                bedMaterial,
                bedPhysics,
                groundLayer,
                out Transform bedAnchor,
                out TruckTailgate tailgate);
            var mover = truck.GetComponent<TruckMover>();
            ConfigureTruckAudio(truck);

            using (var wiring = new Wiring(mover))
            {
                wiring.Ref("path", route)
                    .Num("startDistance", definition.TruckStartDistance)
                    .Num("goalDistance", goalDistance)
                    .Num("maxSpeed", definition.MaxSpeed)
                    .Curve("speedOverProgress", definition.CopySpeedOverProgress())
                    .Num("minSpeedFactor", 0.06f)
                    .Num("rideHeight", RideHeight);
            }

            // 트럭을 경로 위 출발선에 정확히 올린다. 배선이 끝난 뒤여야 경로를 읽을 수 있다.
            // 아래 짐·플레이어·카메라가 모두 이 자세를 기준으로 자리를 잡으므로 순서를 지켜야 한다.
            mover.SnapToStart();

            List<Cargo> cargo = BuildCargo(
                truck.transform,
                cargoPhysics,
                definition.Cargo,
                LoadPrototypeImpactClips());

            Camera firstPersonCamera = BuildFirstPersonCamera(out Transform carryAnchor);
            GameObject player = BuildPlayer(
                truck.transform,
                firstPersonCamera,
                carryAnchor,
                playerMaterial,
                definition.RopeCount);
            Camera dioramaCamera = BuildDioramaCamera(truck.transform);

            var systems = new GameObject("Systems");
            var flow = systems.AddComponent<GameFlow>();
            var tracker = systems.AddComponent<CargoTracker>();
            var director = systems.AddComponent<CameraDirector>();
            var hud = systems.AddComponent<PrototypeHud>();
            var resultScreen = systems.AddComponent<ResultScreen>();

            using (var wiring = new Wiring(director))
            {
                wiring.Ref("firstPersonCamera", firstPersonCamera)
                    .Ref("firstPersonLook", firstPersonCamera.GetComponent<FirstPersonCamera>())
                    .Ref("dioramaCamera", dioramaCamera);
            }

            using (var wiring = new Wiring(tracker))
            {
                wiring.Ref("bedAnchor", bedAnchor).Refs("tracked", cargo.ConvertAll(item => (Object)item));
            }

            using (var wiring = new Wiring(flow))
            {
                wiring.Ref("truck", mover)
                    .Ref("tracker", tracker)
                    .Ref("cameraDirector", director)
                    .Ref("player", player)
                    .Ref("tailgate", tailgate);
            }

            using (var wiring = new Wiring(hud))
            {
                wiring.Ref("flow", flow)
                    .Ref("tracker", tracker)
                    .Ref("bedMaterial", bedPhysics)
                    .Ref("cargoMaterial", cargoPhysics)
                    .Ref("ropeInteractor", player.GetComponent<PlayerRopeInteractor>());
            }

            resultScreen.Configure(flow, tracker);
            ConfigureBackgroundMusic();

            EditorSceneManager.SaveScene(scene, scenePath);
            EnsureSceneInBuildSettings(scenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CargoStack] {definition.StageId} 씬 생성 완료: {scenePath} " +
                $"(경로 {route.TotalLength:0.0}m, 도착 {goalDistance:0.0}m, 도로 조각 {route.SampleCount - 1}개, 화물 {cargo.Count}개)");
        }

        private static StageDefinition LoadStageDefinition(string path)
        {
            return AssetDatabase.LoadAssetAtPath<StageDefinition>(path)
                ?? throw new InvalidOperationException($"스테이지 정의를 찾지 못했다: {path}");
        }

        private static string[] FindStageDefinitionPaths()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:StageDefinition",
                new[] { StageFolder });
            if (guids.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{StageFolder}에서 스테이지 정의를 찾지 못했다.");
            }

            var paths = new string[guids.Length];
            for (int index = 0; index < guids.Length; index++)
            {
                paths[index] = AssetDatabase.GUIDToAssetPath(guids[index]);
            }

            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// 도로를 깐다. 보이는 면과 부딪히는 면을 분리한다.
        ///
        /// 보이는 면은 중심선을 따라 뜬 리본 메시 하나다. 처음에는 겹쳐 깐 상자를 그대로 보여
        /// 줬는데, 겹친 윗면이 같은 평면이라 커브마다 z-파이팅 얼룩이 생겼다.
        /// 부딪히는 면은 여전히 상자다. 커브 바깥쪽 호가 안쪽보다 길어서 상자 길이를 딱 맞추면
        /// 바깥 차선에 구멍이 뚫리므로, 이웃과 겹치게 늘려 깐다. 겹쳐도 이제는 안 보인다.
        /// </summary>
        private static RoutePath BuildRoute(
            int layer,
            Material material,
            PhysicsMaterial physics,
            Vector3[] controlPoints,
            string sceneName)
        {
            var holder = new GameObject("Route");
            RoutePath route = holder.AddComponent<RoutePath>();
            route.SetControlPoints(controlPoints);

            var surface = new GameObject("RoadSurface");
            surface.transform.SetParent(holder.transform, false);
            surface.AddComponent<MeshFilter>().sharedMesh = EnsureRoadMesh(route, sceneName);
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;

            var blocks = new GameObject("RoadColliders").transform;
            blocks.SetParent(holder.transform, false);

            for (int i = 0; i < route.SampleCount - 1; i++)
            {
                Vector3 from = route.SampleAt(i);
                Vector3 to = route.SampleAt(i + 1);
                Vector3 step = to - from;
                float length = step.magnitude;
                if (length < 1e-4f)
                {
                    continue;
                }

                // 조각의 로컬 +X 가 진행 방향이 되도록 LookRotation 을 90도 돌린다.
                Quaternion rotation = Quaternion.LookRotation(step / length, Vector3.up)
                    * Quaternion.Euler(0f, -90f, 0f);

                var block = new GameObject($"Road_{i:000}") { layer = layer };
                block.transform.SetParent(blocks, false);
                block.transform.SetPositionAndRotation(
                    (from + to) * 0.5f - rotation * Vector3.up * (RoadThickness * 0.5f),
                    rotation);

                BoxCollider box = block.AddComponent<BoxCollider>();
                box.size = new Vector3(length * RoadBlockOverlap, RoadThickness, RoadWidth);
                box.sharedMaterial = physics;
            }

            return route;
        }

        /// <summary>
        /// 도로 리본 메시. 중심선 샘플마다 좌우 모서리와 그 아래 치맛단을 만들어 이어 붙인다.
        /// 도로에 뱅킹을 주지 않으므로 좌우 폭은 항상 수평이다.
        /// 메시는 씬에 끼워 넣을 수 없어 별도 에셋으로 저장한다.
        /// </summary>
        private static Mesh EnsureRoadMesh(RoutePath route, string sceneName)
        {
            int count = route.SampleCount;
            var vertices = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            float distance = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 point = route.SampleAt(i);
                if (i > 0)
                {
                    distance += Vector3.Distance(route.SampleAt(i - 1), point);
                }

                Vector3 heading = route.SampleAt(Mathf.Min(i + 1, count - 1))
                    - route.SampleAt(Mathf.Max(i - 1, 0));
                heading.y = 0f;

                Vector3 side = Vector3.Cross(Vector3.up, heading.normalized) * (RoadWidth * 0.5f);
                Vector3 skirt = Vector3.down * RoadThickness;

                vertices[i * 4 + 0] = point - side;          // 왼쪽 모서리
                vertices[i * 4 + 1] = point + side;          // 오른쪽 모서리
                vertices[i * 4 + 2] = point - side + skirt;
                vertices[i * 4 + 3] = point + side + skirt;

                float u = distance / RoadTextureTileSize;
                float v = RoadWidth / RoadTextureTileSize;
                uvs[i * 4 + 0] = new Vector2(u, 0f);
                uvs[i * 4 + 1] = new Vector2(u, v);
                uvs[i * 4 + 2] = uvs[i * 4 + 0];
                uvs[i * 4 + 3] = uvs[i * 4 + 1];
            }

            var triangles = new List<int>((count - 1) * 18);
            for (int i = 0; i < count - 1; i++)
            {
                int a = i * 4;
                int b = (i + 1) * 4;

                // 윗면
                triangles.Add(a + 0); triangles.Add(b + 0); triangles.Add(a + 1);
                triangles.Add(b + 0); triangles.Add(b + 1); triangles.Add(a + 1);

                // 왼쪽 치맛단
                triangles.Add(a + 0); triangles.Add(a + 2); triangles.Add(b + 0);
                triangles.Add(a + 2); triangles.Add(b + 2); triangles.Add(b + 0);

                // 오른쪽 치맛단
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(a + 3);
                triangles.Add(b + 1); triangles.Add(b + 3); triangles.Add(a + 3);
            }

            var mesh = new Mesh { name = $"{sceneName}_RoadSurface" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();

            string path = $"{MeshFolder}/{sceneName}_RoadSurface.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // 이미 있는 에셋을 덮어써야 씬이 참조하던 메시가 그대로 갱신된다.
            existing.Clear();
            existing.SetVertices(vertices);
            existing.SetTriangles(triangles, 0);
            existing.SetUVs(0, uvs);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// 도로 아래에 경로를 따라가는 넓은 잔디 지면을 깐다. 도로와 같은 중심선을 쓰되 훨씬 넓고,
        /// 도로 윗면보다 <see cref="GroundDropBelowRoad"/> 만큼 낮아 길이 땅 위로 도드라진다.
        /// 나무·바위가 이 위에 서므로 더 이상 공중에 뜨지 않는다.
        /// </summary>
        private static void BuildGround(
            RoutePath route,
            Material material,
            int groundLayer,
            string sceneName)
        {
            var ground = new GameObject("Ground");
            var surface = new GameObject("GroundSurface") { layer = groundLayer };
            surface.transform.SetParent(ground.transform, false);
            Mesh mesh = EnsureGroundMesh(route, sceneName);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;
            // 콜라이더가 없으면 도로 밖으로 나갔을 때 발밑이 비어 아래로 떨어진다.
            // 지면을 도로와 같은 Ground 레이어에 두어 플레이어가 그 위를 걸을 수 있게 한다.
            surface.AddComponent<MeshCollider>().sharedMesh = mesh;

            BuildGroundBoundary(route, groundLayer, ground.transform);
        }

        /// <summary>
        /// 지면 리본의 좌우 가장자리와 양 끝을 보이지 않는 BoxCollider로 막는다.
        /// 각 벽은 월드 Y축으로 곧게 세우고 해당 구간의 최저·최고 지형 높이를 모두 덮어,
        /// 경사나 구덩이에서도 벽 아래 틈과 낮은 벽턱이 생기지 않게 한다.
        /// </summary>
        private static void BuildGroundBoundary(RoutePath route, int groundLayer, Transform parent)
        {
            var boundary = new GameObject("GroundBoundary")
            {
                layer = groundLayer,
                isStatic = true,
            };
            boundary.transform.SetParent(parent, false);

            for (int index = 0; index < route.SampleCount - 1; index++)
            {
                Vector3 from = route.SampleAt(index);
                Vector3 to = route.SampleAt(index + 1);
                Vector3 heading = Vector3.ProjectOnPlane(to - from, Vector3.up);
                float length = heading.magnitude;
                if (length < 1e-4f)
                {
                    continue;
                }

                heading /= length;
                Vector3 side = Vector3.Cross(Vector3.up, heading) * GroundHalfWidth;
                Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);
                float lowestSurface = Mathf.Min(from.y, to.y) - GroundDropBelowRoad;
                float highestSurface = Mathf.Max(from.y, to.y) - GroundDropBelowRoad;
                float bottom = lowestSurface - GroundBoundaryDepth;
                float top = highestSurface + GroundBoundaryHeight;
                Vector3 wallCenter = (from + to) * 0.5f;
                wallCenter.y = (bottom + top) * 0.5f;
                Vector3 size = new(
                    GroundBoundaryThickness,
                    top - bottom,
                    length * GroundBoundaryOverlap);

                AddGroundBoundaryWall(
                    boundary.transform,
                    $"Boundary_Left_{index:000}",
                    groundLayer,
                    wallCenter - side,
                    rotation,
                    size);
                AddGroundBoundaryWall(
                    boundary.transform,
                    $"Boundary_Right_{index:000}",
                    groundLayer,
                    wallCenter + side,
                    rotation,
                    size);
            }

            AddGroundBoundaryCap(
                boundary.transform,
                "Boundary_Start",
                groundLayer,
                route.SampleAt(0),
                route.SampleAt(1) - route.SampleAt(0));
            int last = route.SampleCount - 1;
            AddGroundBoundaryCap(
                boundary.transform,
                "Boundary_End",
                groundLayer,
                route.SampleAt(last),
                route.SampleAt(last) - route.SampleAt(last - 1));
        }

        private static void AddGroundBoundaryCap(
            Transform parent,
            string name,
            int layer,
            Vector3 routePoint,
            Vector3 heading)
        {
            heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);
            float surfaceHeight = routePoint.y - GroundDropBelowRoad;
            Vector3 position = routePoint;
            position.y = surfaceHeight
                + (GroundBoundaryHeight - GroundBoundaryDepth) * 0.5f;
            AddGroundBoundaryWall(
                parent,
                name,
                layer,
                position,
                rotation,
                new Vector3(
                    GroundHalfWidth * 2f + GroundBoundaryThickness * 2f,
                    GroundBoundaryHeight + GroundBoundaryDepth,
                    GroundBoundaryThickness));
        }

        private static void AddGroundBoundaryWall(
            Transform parent,
            string name,
            int layer,
            Vector3 position,
            Quaternion rotation,
            Vector3 size)
        {
            var wall = new GameObject(name)
            {
                layer = layer,
                isStatic = true,
            };
            wall.transform.SetParent(parent, false);
            wall.transform.SetPositionAndRotation(position, rotation);
            wall.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// 잔디 지면 리본 메시. 도로 리본과 같은 방식으로 중심선을 따라 좌우로 넓게 편다.
        /// 옆에서 봐도 두께가 보이도록 치맛단을 아래로 내려 붙인다.
        /// </summary>
        private static Mesh EnsureGroundMesh(RoutePath route, string sceneName)
        {
            int count = route.SampleCount;
            var vertices = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            float distance = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 point = route.SampleAt(i);
                if (i > 0)
                {
                    distance += Vector3.Distance(route.SampleAt(i - 1), point);
                }

                Vector3 heading = route.SampleAt(Mathf.Min(i + 1, count - 1))
                    - route.SampleAt(Mathf.Max(i - 1, 0));
                heading.y = 0f;

                Vector3 side = Vector3.Cross(Vector3.up, heading.normalized) * GroundHalfWidth;
                Vector3 top = point + Vector3.down * GroundDropBelowRoad;
                Vector3 skirt = Vector3.down * GroundThickness;

                vertices[i * 4 + 0] = top - side;
                vertices[i * 4 + 1] = top + side;
                vertices[i * 4 + 2] = top - side + skirt;
                vertices[i * 4 + 3] = top + side + skirt;

                float u = distance / GroundTextureTileSize;
                float v = (GroundHalfWidth * 2f) / GroundTextureTileSize;
                uvs[i * 4 + 0] = new Vector2(u, 0f);
                uvs[i * 4 + 1] = new Vector2(u, v);
                uvs[i * 4 + 2] = uvs[i * 4 + 0];
                uvs[i * 4 + 3] = uvs[i * 4 + 1];
            }

            var triangles = new List<int>((count - 1) * 18);
            for (int i = 0; i < count - 1; i++)
            {
                int a = i * 4;
                int b = (i + 1) * 4;

                // 윗면
                triangles.Add(a + 0); triangles.Add(b + 0); triangles.Add(a + 1);
                triangles.Add(b + 0); triangles.Add(b + 1); triangles.Add(a + 1);

                // 왼쪽 치맛단
                triangles.Add(a + 0); triangles.Add(a + 2); triangles.Add(b + 0);
                triangles.Add(a + 2); triangles.Add(b + 2); triangles.Add(b + 0);

                // 오른쪽 치맛단
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(a + 3);
                triangles.Add(b + 1); triangles.Add(b + 3); triangles.Add(a + 3);
            }

            var mesh = new Mesh { name = $"{sceneName}_Ground" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Directory.CreateDirectory(MeshFolder);
            AssetDatabase.Refresh();

            string path = $"{MeshFolder}/{sceneName}_Ground.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            existing.Clear();
            existing.SetVertices(vertices);
            existing.SetTriangles(triangles, 0);
            existing.SetUVs(0, uvs);
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static GameObject BuildTruck(
            Material truckMaterial,
            Material bedMaterial,
            PhysicsMaterial bedPhysics,
            int groundLayer,
            out Transform bedAnchor,
            out TruckTailgate tailgate)
        {
            // 자리는 배선이 끝난 뒤 TruckMover.SnapToStart 가 경로에서 잡아 준다.
            var truck = new GameObject("Truck");

            Rigidbody body = truck.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            truck.AddComponent<TruckMover>();

            // 바닥 지지대. 바닥면이 정확히 -RideHeight 라서 지면 추종 높이의 기준이 된다.
            AddPart(truck.transform, "GroundSupport", new Vector3(0f, -0.6f, 0f), new Vector3(4.6f, 0.3f, 1.9f), truckMaterial, bedPhysics, false);
            AddPart(truck.transform, "Chassis", new Vector3(0f, -0.05f, 0f), new Vector3(5.8f, 0.55f, 2.4f), truckMaterial, bedPhysics, false);
            // 캐빈 뒤쪽 문(-0.15)~앞쪽 차체(2.7), 바닥(-0.075)~지붕(2.05)을
            // 하나의 고체 박스로 막아 플레이어와 화물이 시각 메시를 관통하지 않게 한다.
            AddPart(truck.transform, "Cab", new Vector3(1.275f, 0.9875f, 0f), new Vector3(2.85f, 2.125f, 2.25f), truckMaterial, bedPhysics, false)
                .gameObject.AddComponent<CargoPlacementForbiddenVolume>();
            AddCabInteriorPlacementForbiddenVolume(truck.transform);
            AddCabFrontCollisionWall(truck.transform, bedMaterial, bedPhysics);

            float bedLengthWithWalls = BedInsideLength + BedWallThickness * 2f;
            float wallCenterY = BedFloorTop + BedWallHeight * 0.5f;
            float minZ = BedCenterZ - BedInsideWidth * 0.5f;
            float maxZ = BedCenterZ + BedInsideWidth * 0.5f;

            // 보이는 BlueTruck 메시와 겹쳐 그리지 않고, 계측한 안쪽 면에 공유 물리만 둔다.
            AddPart(
                truck.transform,
                "BedFloor",
                new Vector3(BedCenterX, BedFloorTop - BedFloorThickness * 0.5f, BedCenterZ),
                new Vector3(bedLengthWithWalls, BedFloorThickness, BedInsideWidth),
                bedMaterial,
                bedPhysics,
                false);
            AddPart(
                truck.transform,
                "BedWall_Left",
                new Vector3(BedCenterX, wallCenterY, minZ - BedWallThickness * 0.5f),
                new Vector3(bedLengthWithWalls, BedWallHeight, BedWallThickness),
                bedMaterial,
                bedPhysics,
                false);
            AddPart(
                truck.transform,
                "BedWall_Right",
                new Vector3(BedCenterX, wallCenterY, maxZ + BedWallThickness * 0.5f),
                new Vector3(bedLengthWithWalls, BedWallHeight, BedWallThickness),
                bedMaterial,
                bedPhysics,
                false);
            Transform tailgatePivot = CreatePoint(
                "TailgatePivot",
                truck.transform,
                BlueTruckWheelMeshGenerator.TailgatePivot);
            tailgate = tailgatePivot.gameObject.AddComponent<TruckTailgate>();
            AddPart(
                tailgatePivot,
                "BedWall_Rear",
                new Vector3(0f, BedWallHeight * 0.5f, BedCenterZ),
                new Vector3(BedWallThickness, BedWallHeight, BedInsideWidth),
                bedMaterial,
                bedPhysics,
                false);
            AddPart(
                truck.transform,
                "BedWall_Front",
                new Vector3(BedFrontBarrierX, BedFloorTop + BedFrontBarrierHeight * 0.5f, BedCenterZ),
                new Vector3(BedWallThickness, BedFrontBarrierHeight, BedFrontBarrierWidth),
                bedMaterial,
                bedPhysics,
                false);

            bedAnchor = CreatePoint(
                "BedAnchor",
                truck.transform,
                new Vector3(BedCenterX, BedFloorTop, BedCenterZ));
            AddBlueTruckVisual(truck.transform, tailgatePivot, groundLayer);
            return truck;
        }

        private static void ConfigureTruckAudio(GameObject truck)
        {
            AudioClip engineLoop = LoadAudioClip("engine_idle_loop.wav");
            truck.AddComponent<AudioSource>();
            truck.AddComponent<TruckEngineAudio>().Configure(engineLoop);
        }

        private static void ConfigureBackgroundMusic()
        {
            var music = new GameObject("BackgroundMusic");
            AudioSource source = music.AddComponent<AudioSource>();
            source.clip = LoadAudioClip("bgm_playful_loop.ogg");
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.14f;
            music.AddComponent<BackgroundMusic>();
        }

        private static AudioClip[] LoadPrototypeImpactClips()
        {
            return new[]
            {
                LoadAudioClip("cargo_thump_01.wav"),
                LoadAudioClip("cargo_thump_02.wav"),
                LoadAudioClip("cargo_thump_03.wav"),
            };
        }

        private static AudioClip LoadAudioClip(string fileName)
        {
            string path = $"{PrototypeAudioFolder}/{fileName}";
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                ?? throw new InvalidOperationException(
                    $"프로토타입 오디오를 찾지 못했다: {path}");
        }

        private static void AddBlueTruckVisual(
            Transform truck,
            Transform tailgatePivot,
            int groundLayer)
        {
            BlueTruckWheelMeshGenerator.Generate();
            Material material = EnsureBlueTruckMaterial();
            var visual = new GameObject("BlueTruckVisual");
            visual.transform.SetParent(truck, false);

            AddGeneratedMesh(
                visual.transform,
                "BlueTruckBody",
                BlueTruckWheelMeshGenerator.BodyMeshPath,
                material);
            AddCabGlass(visual.transform);
            AddGeneratedMesh(
                tailgatePivot,
                "BlueTruckTailgate",
                BlueTruckWheelMeshGenerator.TailgateMeshPath,
                material);

            var suspensionRoots = new Transform[4];
            var spinRoots = new Transform[4];
            for (int index = 0; index < suspensionRoots.Length; index++)
            {
                string wheelName = BlueTruckWheelMeshGenerator.WheelNames[index];
                suspensionRoots[index] = CreatePoint(
                    $"Wheel_{wheelName}_Suspension",
                    visual.transform,
                    BlueTruckWheelMeshGenerator.WheelPivots[index]);
                spinRoots[index] = CreatePoint(
                    $"Wheel_{wheelName}_Spin",
                    suspensionRoots[index],
                    Vector3.zero);
                AddGeneratedMesh(
                    spinRoots[index],
                    $"Wheel_{wheelName}_Mesh",
                    BlueTruckWheelMeshGenerator.WheelMeshPath(index),
                    material);
            }

            var wheelAnimator = truck.gameObject.AddComponent<TruckWheelAnimator>();
            wheelAnimator.Configure(
                suspensionRoots,
                spinRoots,
                BlueTruckWheelMeshGenerator.WheelRadius,
                1 << groundLayer);
        }

        private static void AddCabGlass(Transform parent)
        {
            // 차체 프레임 안쪽 네 모서리를 공유해 앞·뒤·양옆의 열린 창을 한 번에 막는다.
            var vertices = new[]
            {
                new Vector3(-0.15f, 0.78f, -0.96f),
                new Vector3(-0.15f, 0.78f, 0.96f),
                new Vector3(-0.15f, 1.45f, -0.84f),
                new Vector3(-0.15f, 1.45f, 0.84f),
                new Vector3(1.42f, 0.78f, -0.96f),
                new Vector3(1.42f, 0.78f, 0.96f),
                new Vector3(0.82f, 1.45f, -0.84f),
                new Vector3(0.82f, 1.45f, 0.84f),
            };
            int[] triangles =
            {
                0, 2, 1, 2, 3, 1,
                4, 5, 6, 6, 5, 7,
                0, 4, 2, 2, 4, 6,
                1, 3, 5, 3, 7, 5,
            };
            var mesh = new Mesh { name = "BlueTruckCabGlass" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(BlueTruckCabGlassMeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, BlueTruckCabGlassMeshPath);
            }
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }

            var glass = new GameObject("DriverCabGlass");
            glass.transform.SetParent(parent, false);
            glass.AddComponent<MeshFilter>().sharedMesh = mesh;
            glass.AddComponent<MeshRenderer>().sharedMaterial = EnsureCabGlassMaterial();
        }

        private static void AddGeneratedMesh(
            Transform parent,
            string objectName,
            string meshPath,
            Material material)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath)
                ?? throw new InvalidOperationException($"생성된 BlueTruck 메시를 찾지 못했다: {meshPath}");
            var holder = new GameObject(objectName);
            holder.transform.SetParent(parent, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material EnsureBlueTruckMaterial()
        {
            Shader standard = Shader.Find("Standard")
                ?? throw new InvalidOperationException("블루트럭 재질에 쓸 Standard 셰이더를 찾지 못했다");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BlueTruckMaterialPath);
            if (material == null)
            {
                material = new Material(standard) { name = "BlueTruckMaterial" };
                AssetDatabase.CreateAsset(material, BlueTruckMaterialPath);
            }

            string normalPath = BlueTruckFolder + "/BlueTruck_Normal.jpg";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter normalImporter
                && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }

            material.shader = standard;
            material.SetTexture(
                "_MainTex",
                AssetDatabase.LoadAssetAtPath<Texture2D>(BlueTruckFolder + "/BlueTruck_BaseColor.jpg"));
            material.SetTexture(
                "_BumpMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            material.SetTexture(
                "_MetallicGlossMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(BlueTruckFolder + "/BlueTruck_Metallic.jpg"));
            material.SetFloat("_Metallic", 0.45f);
            material.SetFloat("_Glossiness", 0.25f);
            material.SetFloat("_GlossMapScale", 0.25f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureCabGlassMaterial()
        {
            Material material = EnsureColorMaterial(
                "CabGlass",
                new Color(0.45f, 0.78f, 0.88f, 0.28f));
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform AddPart(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            PhysicsMaterial physics,
            bool visible)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            part.GetComponent<BoxCollider>().sharedMaterial = physics;

            Renderer renderer = part.GetComponent<Renderer>();
            if (visible)
            {
                renderer.sharedMaterial = material;
            }
            else
            {
                Object.DestroyImmediate(renderer);
            }

            return part.transform;
        }

        private static void AddCabInteriorPlacementForbiddenVolume(Transform truck)
        {
            var volume = new GameObject("CabInteriorPlacementForbiddenVolume");
            volume.transform.SetParent(truck, false);
            volume.transform.localPosition = new Vector3(
                (CabInteriorForbiddenMinX + CabInteriorForbiddenMaxX) * 0.5f,
                CabInteriorForbiddenCenterY,
                0f);

            var collider = volume.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                CabInteriorForbiddenMaxX - CabInteriorForbiddenMinX,
                CabInteriorForbiddenHeight,
                CabInteriorForbiddenWidth);
            // 조준 Raycast는 trigger를 무시한다. 따라서 이 표식은 Chassis를 지지면으로
            // 삼는 실제 배치 경로도 OverlapBox에서만 막고 물리·조준을 바꾸지 않는다.
            collider.isTrigger = true;
            volume.AddComponent<CargoPlacementForbiddenVolume>();
        }

        private static void AddCabFrontCollisionWall(
            Transform truck,
            Material bedMaterial,
            PhysicsMaterial bedPhysics)
        {
            AddPart(
                truck,
                "CabFrontCollisionWall",
                new Vector3(
                    CabInteriorForbiddenMinX + CabFrontCollisionWallThickness * 0.5f,
                    CabFrontCollisionWallCenterY,
                    BedCenterZ),
                new Vector3(
                    CabFrontCollisionWallThickness,
                    CabFrontCollisionWallHeight,
                    CabFrontCollisionWallWidth),
                bedMaterial,
                bedPhysics,
                false);
        }

        /// <summary>
        /// 짐은 트럭 옆 바닥에 널어 둔다. 플레이어가 걸어가 하나씩 실어야 한다.
        /// 자리는 트럭 기준 상대 좌표로 잡는다. 출발선이 경로를 따라 움직여도 같이 따라오게 하기 위함이다.
        /// </summary>
        private static List<Cargo> BuildCargo(
            Transform truck,
            PhysicsMaterial physics,
            IReadOnlyList<StageCargoDefinition> definitions,
            AudioClip[] impactClips)
        {
            var cargoRoot = new GameObject("Cargo").transform;
            // 각 모델은 보이는 FBX와 보이지 않는 충돌 프록시를 분리한다. 프록시는
            // PlayerCargoInteractor 의 배치 계산 및 트럭 위 물리를 안정적으로 유지한다.
            var cargo = new List<Cargo>(definitions.Count);

            for (int index = 0; index < definitions.Count; index++)
            {
                StageCargoDefinition definition = definitions[index];
                var item = new GameObject($"Cargo_{index + 1:00}");
                item.transform.SetParent(cargoRoot, false);
                Vector3 proxySize = AddImportedCargoVisual(item.transform, definition);

                // 트럭 옆 인도. 폭 13m 도로를 벗어나지 않게 z 를 -4.7m 안쪽으로 묶는다.
                var localSpot = new Vector3(
                    -2.6f + index % 3 * 1.5f,
                    proxySize.y * 0.5f + 0.02f - RideHeight,
                    -3f - index / 3 * 1.2f);

                item.transform.position = truck.TransformPoint(localSpot);
                AddCargoCollider(item, definition, proxySize, physics);

                Rigidbody body = item.AddComponent<Rigidbody>();
                body.mass = definition.Mass;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.15f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                cargo.Add(item.AddComponent<Cargo>());
                item.AddComponent<AudioSource>();
                item.AddComponent<CargoImpactAudio>().Configure(impactClips);

                if (definition.IsFragile)
                {
                    item.AddComponent<CargoBreakable>().Configure(definition.BreakImpactSpeed);
                }
            }

            return cargo;
        }

        private static void AddCargoCollider(
            GameObject item,
            StageCargoDefinition definition,
            Vector3 proxySize,
            PhysicsMaterial physics)
        {
            switch (definition.ColliderShape)
            {
                case StageCargoColliderShape.Box:
                    BoxCollider box = item.AddComponent<BoxCollider>();
                    box.size = proxySize;
                    box.sharedMaterial = physics;
                    break;

                case StageCargoColliderShape.Capsule:
                    CapsuleCollider capsule = item.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = Mathf.Min(proxySize.x, proxySize.z) * 0.5f;
                    capsule.height = Mathf.Max(proxySize.y, capsule.radius * 2f);
                    capsule.sharedMaterial = physics;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"지원하지 않는 화물 충돌 형태다: {definition.ColliderShape}");
            }
        }

        private static Vector3 AddImportedCargoVisual(
            Transform cargoRoot,
            StageCargoDefinition definition)
        {
            string modelPath = GetCargoModelPath(definition);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
                ?? throw new InvalidOperationException($"화물 모델을 찾지 못했다: {modelPath}");
            Material material = EnsureCargoMaterial(definition);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = $"ImportedVisual_{definition.AssetName}";
            visual.transform.SetParent(cargoRoot, false);
            visual.transform.localPosition = Vector3.zero;
            // The supplied Meshy FBX files use Z-up. Turn their +Z axis toward Unity's +Y world
            // before measuring the renderer and creating its matching physics proxy.
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Vector3 importedScale = visual.transform.localScale;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }

            Bounds originalBounds = GetRendererBounds(visual);
            if (definition.StretchToMaximumSize)
            {
                Vector3 worldStretch = new(
                    definition.MaximumSize.x / originalBounds.size.x,
                    definition.MaximumSize.y / originalBounds.size.y,
                    definition.MaximumSize.z / originalBounds.size.z);
                // FBX의 Z-up을 Y-up으로 세우려고 루트를 X축 -90도로 돌렸으므로,
                // 월드 Y/Z 배율은 모델 로컬 Z/Y축에 각각 적용해야 한다.
                Vector3 modelStretch = new(
                    worldStretch.x,
                    worldStretch.z,
                    worldStretch.y);
                visual.transform.localScale = Vector3.Scale(importedScale, modelStretch);
            }
            else
            {
                float scale = Mathf.Min(
                    definition.MaximumSize.x / originalBounds.size.x,
                    definition.MaximumSize.y / originalBounds.size.y,
                    definition.MaximumSize.z / originalBounds.size.z);
                // Meshy FBX roots carry their source-unit conversion in localScale (typically
                // 100). Preserve it while fitting the visible renderer into the proxy bounds.
                visual.transform.localScale = importedScale * scale;
            }

            Bounds scaledBounds = GetRendererBounds(visual);
            visual.transform.localPosition = -scaledBounds.center;
            return scaledBounds.size * 1.05f;
        }

        private static Bounds GetRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"화물 모델에 Renderer 가 없다: {target.name}");
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            if (bounds.size.x <= 1e-4f || bounds.size.y <= 1e-4f || bounds.size.z <= 1e-4f)
            {
                throw new InvalidOperationException($"화물 모델의 유효한 크기를 읽지 못했다: {target.name}");
            }

            return bounds;
        }

        private static Material EnsureCargoMaterial(StageCargoDefinition definition)
        {
            string materialPath = GetCargoMaterialPath(definition);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"))
                {
                    name = definition.AssetName + "Material",
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.SetTexture("_MainTex", LoadCargoTexture(definition, "Albedo"));
            material.SetTexture("_MetallicGlossMap", LoadCargoTexture(definition, "Metallic"));
            material.SetTexture("_BumpMap", LoadCargoTexture(definition, "Normal", true));
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Glossiness", 0.25f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadCargoTexture(
            StageCargoDefinition definition,
            string textureName,
            bool normalMap = false)
        {
            string path = $"{GetCargoFolder(definition)}/{textureName}.png";
            if (normalMap && AssetImporter.GetAtPath(path) is TextureImporter importer
                && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path)
                ?? throw new InvalidOperationException($"화물 텍스처를 찾지 못했다: {path}");
        }

        private static string GetCargoFolder(StageCargoDefinition definition)
        {
            return $"{CargoArtFolder}/{definition.AssetName}";
        }

        private static string GetCargoModelPath(StageCargoDefinition definition)
        {
            return $"{GetCargoFolder(definition)}/{definition.AssetName}.fbx";
        }

        private static string GetCargoMaterialPath(StageCargoDefinition definition)
        {
            return $"{GetCargoFolder(definition)}/{definition.AssetName}Material.mat";
        }

        private static Camera BuildFirstPersonCamera(out Transform carryAnchor)
        {
            var holder = new GameObject("First Person Camera") { tag = "MainCamera" };
            Camera camera = holder.AddComponent<Camera>();
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.backgroundColor = new Color(0.63f, 0.76f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            holder.AddComponent<AudioListener>();
            holder.AddComponent<FirstPersonCamera>();

            // 든 화물이 시야 한가운데를 가리지 않도록 살짝 오른쪽 아래에 붙인다.
            carryAnchor = CreatePoint("CarryAnchor", holder.transform, new Vector3(0.65f, -0.9f, 2f));
            return camera;
        }

        private static GameObject BuildPlayer(
            Transform truck,
            Camera firstPersonCamera,
            Transform carryAnchor,
            Material material,
            int ropeCount)
        {
            // 짐 뒤에 서서 +Z(트럭 쪽)를 바라보게 둔다. 첫 화면에 짐과 트럭이 함께 들어온다.
            var player = new GameObject("Player");
            player.transform.position = truck.TransformPoint(new Vector3(-2f, 0.05f - RideHeight, -5.4f));

            Rigidbody body = player.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.linearDamping = 0f;
            body.angularDamping = 8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 회전을 세 축 모두 잠근다. Y 를 풀어 두면 상자나 트럭에 부딪힐 때 생긴 토크가
            // 몸통을 돌려 버리고, 1인칭 카메라가 이 몸통에 매달려 있어 시점이 통째로 홱 돌아간다.
            // 시점 yaw 는 FirstPersonCamera 의 마우스 입력만으로 정해져야 한다.
            body.constraints = RigidbodyConstraints.FreezeRotation;

            // 키를 20% 키운다(사용자 요청). 굵기(radius)는 그대로 둔다 - 사거리 실측표(README)가
            // 트럭 옆 선 자리에서 수평거리로 잰 값이라 반지름을 건드리면 그 실측이 무효가 된다.
            const float HeightScale = 1.2f;

            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 1.8f * HeightScale;
            capsule.center = new Vector3(0f, 0.9f * HeightScale, 0f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f * HeightScale, 0f);
            visual.transform.localScale = new Vector3(0.75f, 0.9f * HeightScale, 0.75f);
            visual.GetComponent<Renderer>().sharedMaterial = material;

            // 1인칭 시야를 자기 몸이 가리지 않게 그림자만 남긴다.
            visual.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            Transform eye = CreatePoint("PlayerView", player.transform, new Vector3(0f, 1.6f * HeightScale, 0f));

            PlayerController controller = player.AddComponent<PlayerController>();
            controller.Configure(firstPersonCamera);

            PlayerCargoInteractor interactor = player.AddComponent<PlayerCargoInteractor>();
            interactor.Configure(carryAnchor, firstPersonCamera);
            PlayerTailgateInteractor tailgateInteractor =
                player.AddComponent<PlayerTailgateInteractor>();
            tailgateInteractor.Configure(firstPersonCamera);

            // 사거리는 원본 스파이크의 기본값을 쓰지 않고 여기서 덮어쓴다.
            // 복사해 온 파일을 그대로 두어야 출처 표기가 사실로 남고, 튜닝 값은 레벨 데이터와 함께 모인다.
            using (var wiring = new Wiring(interactor))
            {
                wiring.Num("interactionRadius", CargoReach)
                    .Num("placementRange", CargoPlacementReach);
            }

            // 로프는 짐을 놓을 면을 찾는 거리와 같은 사거리를 쓴다. 짐을 놓을 수 있는 자리면
            // 그 자리에 매듭도 지을 수 있어야 조작이 한 덩어리로 느껴진다.
            PlayerRopeInteractor ropeInteractor = player.AddComponent<PlayerRopeInteractor>();
            ropeInteractor.Configure(firstPersonCamera, ropeCount);
            using (var wiring = new Wiring(ropeInteractor))
            {
                wiring.Num("ropeReach", CargoPlacementReach);
            }

            firstPersonCamera.GetComponent<FirstPersonCamera>().Configure(eye, true);
            return player;
        }

        private static Camera BuildDioramaCamera(Transform truck)
        {
            var holder = new GameObject("Diorama Camera");
            Camera camera = holder.AddComponent<Camera>();

            // 좁은 화각 + 먼 거리 조합이 폴리브릿지 같은 미니어처 느낌을 만든다.
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 250f;
            camera.backgroundColor = new Color(0.63f, 0.76f, 0.85f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.enabled = false;
            AudioListener listener = holder.AddComponent<AudioListener>();
            listener.enabled = false;

            DioramaCamera rig = holder.AddComponent<DioramaCamera>();
            using (var wiring = new Wiring(rig))
            {
                wiring.Ref("target", truck);
            }

            // 에디터에서도 제자리를 잡아 둬야 씬 뷰와 프리뷰 캡처에서 구도를 확인할 수 있다.
            rig.ResetFraming();
            return camera;
        }

        private static void CreateLighting(bool winter)
        {
            var holder = new GameObject("Sun");
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = winter ? 1.05f : 1.15f;
            light.shadows = LightShadows.Soft;
            holder.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = winter
                ? new Color(0.72f, 0.82f, 0.92f)
                : new Color(0.6f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = winter
                ? new Color(0.58f, 0.64f, 0.68f)
                : new Color(0.42f, 0.45f, 0.44f);
            RenderSettings.ambientGroundColor = winter
                ? new Color(0.72f, 0.78f, 0.82f)
                : new Color(0.24f, 0.26f, 0.22f);
        }

        private static void EnsureSceneInBuildSettings(
            string scenePath,
            bool placeFirst = false)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int existingIndex = scenes.FindIndex(item => item.path == scenePath);
            var enabledScene = new EditorBuildSettingsScene(scenePath, true);

            if (placeFirst)
            {
                if (existingIndex >= 0)
                {
                    scenes.RemoveAt(existingIndex);
                }

                scenes.Insert(0, enabledScene);
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }

            if (existingIndex >= 0)
            {
                scenes[existingIndex] = enabledScene;
            }
            else
            {
                scenes.Add(enabledScene);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Transform CreatePoint(string name, Transform parent, Vector3 localPosition)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, false);
            point.transform.localPosition = localPosition;
            point.transform.localRotation = Quaternion.identity;
            return point.transform;
        }

        /// <summary>스택 안정성을 위한 물리 설정(기획서 4.1).</summary>
        private static void ApplyPhysicsProjectSettings()
        {
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 2;
        }

        private static int EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    return i;
                }
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    continue;
                }

                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return i;
            }

            throw new InvalidOperationException($"빈 레이어 슬롯이 없어 '{layerName}' 레이어를 만들지 못했다.");
        }

        private static Material EnsureColorMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var material = new Material(Shader.Find("Standard")) { color = color };
            material.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material EnsureWinterRoadMaterial()
        {
            Texture2D albedo = FindWinterTexture("ice", false, "ice_toon_smooth_1");
            Texture2D normal = FindWinterTexture("ice", true);
            if (albedo == null)
            {
                Debug.LogWarning(
                    "[CargoStack] 얼음 도로 텍스처를 찾지 못했다. "
                    + "Asset Store의 4K Tiled Ground Textures (part 1)를 임포트하면 "
                    + "재생성 시 ice 텍스처를 자동 연결한다.");
            }

            return EnsureSurfaceMaterial(
                IceRoadMaterialPath,
                new Color(0.56f, 0.82f, 0.93f),
                albedo,
                normal,
                0.18f,
                0.92f);
        }

        private static Material EnsureSnowGroundMaterial()
        {
            Texture2D albedo = FindWinterTexture("snow", false, "snow_solid_1");
            Texture2D normal = FindWinterTexture("snow", true);
            if (albedo == null)
            {
                Debug.LogWarning(
                    "[CargoStack] 눈 지면 텍스처를 찾지 못했다. "
                    + "Asset Store의 4K Tiled Ground Textures (part 1)를 임포트하면 "
                    + "재생성 시 snow 텍스처를 자동 연결한다.");
            }

            return EnsureSurfaceMaterial(
                SnowGroundMaterialPath,
                new Color(0.91f, 0.96f, 1f),
                albedo,
                normal,
                0f,
                0.35f);
        }

        private static Material EnsureSurfaceMaterial(
            string path,
            Color color,
            Texture2D albedo,
            Texture2D normal,
            float metallic,
            float glossiness)
        {
            Shader standard = Shader.Find("Standard")
                ?? throw new InvalidOperationException("겨울 지면 재질에 쓸 Standard 셰이더를 찾지 못했다");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(standard)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = standard;
            material.color = color;
            material.SetTexture("_MainTex", albedo);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", glossiness);
            ConfigureNormalMap(normal);
            material.SetTexture("_BumpMap", normal);
            if (normal != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureNormalMap(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static Texture2D FindWinterTexture(
            string keyword,
            bool normalMap,
            string preferredFileName = null)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            Texture2D fallback = null;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = path.ToLowerInvariant();
                if (!lower.Contains(keyword))
                {
                    continue;
                }

                bool looksNormal = lower.Contains("normal")
                    || lower.Contains("bump")
                    || lower.Contains("height");
                if (normalMap != looksNormal)
                {
                    continue;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(preferredFileName)
                    && Path.GetFileNameWithoutExtension(lower).Contains(preferredFileName))
                {
                    return texture;
                }

                if (fallback == null)
                {
                    fallback = texture;
                }

                // The linked 4K ground pack is preferred when it has already been imported.
                if (lower.Contains("4k") || lower.Contains("tiled ground"))
                {
                    return texture;
                }
            }

            return fallback;
        }

        private static PhysicsMaterial EnsurePhysicsMaterial(string name, float dynamicFriction, float staticFriction)
        {
            string path = $"{MaterialFolder}/{name}.physicsMaterial";
            var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            PhysicsMaterial material = existing != null ? existing : new PhysicsMaterial(name);

            material.dynamicFriction = dynamicFriction;
            material.staticFriction = staticFriction;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Average;

            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>private [SerializeField] 필드를 코드에서 연결하기 위한 얇은 래퍼.</summary>
        private sealed class Wiring : IDisposable
        {
            private readonly SerializedObject serialized;

            public Wiring(Object target)
            {
                serialized = new SerializedObject(target);
            }

            public Wiring Ref(string field, Object value)
            {
                Find(field).objectReferenceValue = value;
                return this;
            }

            public Wiring Num(string field, float value)
            {
                Find(field).floatValue = value;
                return this;
            }

            public Wiring Mask(string field, int value)
            {
                Find(field).intValue = value;
                return this;
            }

            public Wiring Curve(string field, AnimationCurve value)
            {
                Find(field).animationCurveValue = value;
                return this;
            }

            public Wiring Refs(string field, IReadOnlyList<Object> values)
            {
                SerializedProperty property = Find(field);
                property.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++)
                {
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }

                return this;
            }

            public void Dispose()
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private SerializedProperty Find(string field)
            {
                SerializedProperty property = serialized.FindProperty(field)
                    ?? throw new InvalidOperationException($"필드를 찾지 못했다: {serialized.targetObject.GetType().Name}.{field}");
                return property;
            }
        }
    }
}
