using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CargoStack.EditorTools
{
    /// <summary>
    /// 두 시점의 구도를 이미지로 뽑는다.
    /// 카메라 각도와 화각은 숫자만 봐서는 판단할 수 없어서, 눈으로 확인할 수단을 코드로 남겨 둔다.
    /// 씬 빌더의 값을 바꾼 뒤 이 메뉴를 다시 실행해 구도를 비교하면 된다.
    /// </summary>
    public static class PrototypePreview
    {
        private const string ScenePath = "Assets/Scenes/Prototype.unity";
        private const string OutputFolder = "/tmp/cargo-stack-preview";
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("CargoStack/시점 프리뷰 캡처")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(OutputFolder);

            // 1인칭은 게임 시작 그대로(짐이 바닥에 널린 상태)를 찍어야 첫인상을 볼 수 있다.
            CaptureCamera("First Person Camera", $"{OutputFolder}/first-person.png");

            LoadCargoOntoBedForPreview();
            CaptureCamera("First Person Camera", $"{OutputFolder}/first-person-loaded.png");

            // 자유 시점이 실제로 원하는 각도까지 도는지 대표 네 각도로 확인한다.
            // 트럭이 +X 로 달리므로 yaw 0 이 옆면, yaw 90 이 뒷면이다.
            CaptureDiorama("quarter", 35f, 38f, 16f);
            CaptureDiorama("top", 35f, 85f, 16f);
            CaptureDiorama("side", 0f, 8f, 14f);
            CaptureDiorama("rear", 90f, 8f, 14f);
            CaptureTruckCandidates();

            // 경로 전체. 굴곡 모양과 도로 이음매에 구멍이 없는지는 이 그림으로만 확인할 수 있다.
            // 오르막·내리막이 얼마나 굽이치는지는 위에서 봐서는 판단이 안 되므로 옆에서도 한 장 찍는다.
            CaptureRouteFraming("route-overview", 52f, 20f);
            CaptureRouteFraming("route-profile", 4f, 0f);

            Debug.Log($"[CargoStack] 프리뷰 저장 완료: {OutputFolder}");
        }

        private static void CaptureTruckCandidates()
        {
            TruckVisualSelector selector = Object.FindFirstObjectByType<TruckVisualSelector>();
            if (selector == null)
            {
                Debug.LogError("[CargoStack] 씬에 TruckVisualSelector 가 없다");
                return;
            }

            string[] labels = { "cartoon", "low-poly", "free-pickup" };
            for (int index = 0; index < selector.CandidateCount; index++)
            {
                selector.Select(index);
                LoadRepresentativeCargoOntoActiveBed(selector);
                CaptureDiorama($"truck-{labels[index]}", 35f, 28f, 13f);
            }

            selector.Select(0);
        }

        private static void LoadRepresentativeCargoOntoActiveBed(TruckVisualSelector selector)
        {
            TruckBedProfile profile = selector.ActiveProfile;
            Cargo[] cargo =
            {
                FindCargoWithVisual("CardboardBox"),
                FindCargoWithVisual("BlueBarrel"),
                FindCargoWithVisual("MarbleBust"),
            };
            Cargo[] allCargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);
            Vector2[] offsets =
            {
                new Vector2(-0.48f, -0.48f),
                new Vector2(-0.48f, 0.48f),
                new Vector2(0.48f, 0f),
            };

            int hiddenIndex = 0;
            foreach (Cargo item in allCargo)
            {
                bool isRepresentative = false;
                foreach (Cargo representative in cargo)
                {
                    isRepresentative |= representative == item;
                }

                if (!isRepresentative)
                {
                    item.transform.position = new Vector3(0f, -20f - hiddenIndex, 0f);
                    hiddenIndex++;
                }
            }

            for (int index = 0; index < cargo.Length; index++)
            {
                BoxCollider proxy = cargo[index].GetComponent<BoxCollider>();
                Vector2 offset = offsets[index];
                Vector3 localPosition = new Vector3(
                    profile.CenterX + offset.x,
                    profile.FloorTop + proxy.size.y * 0.5f + 0.02f,
                    profile.CenterZ + offset.y);
                cargo[index].transform.SetPositionAndRotation(
                    selector.transform.TransformPoint(localPosition),
                    selector.transform.rotation);
            }
        }

        private static Cargo FindCargoWithVisual(string visualName)
        {
            foreach (Cargo cargo in Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID))
            {
                foreach (Transform child in cargo.transform)
                {
                    if (child.name.Contains(visualName))
                    {
                        return cargo;
                    }
                }
            }

            throw new System.InvalidOperationException($"캡처용 화물을 찾지 못했다: {visualName}");
        }

        /// <summary>
        /// 빈 짐칸은 구도 판단에 도움이 안 되므로, 캡처용으로만 짐을 한 층 올려 둔다.
        /// 에디터라 물리가 돌지 않으니 계산한 자리에 그대로 놓는다.
        /// </summary>
        private static void LoadCargoOntoBedForPreview()
        {
            Transform bedAnchor = GameObject.Find("BedAnchor").transform;
            Cargo[] cargo = Object.FindObjectsByType<Cargo>(FindObjectsSortMode.InstanceID);

            for (int i = 0; i < cargo.Length; i++)
            {
                float height = cargo[i].transform.localScale.y;
                var offset = new Vector3(-1.05f + i % 3 * 1.05f, height * 0.5f + 0.02f, -0.5f + i / 3 * 1f);
                cargo[i].transform.SetPositionAndRotation(bedAnchor.TransformPoint(offset), bedAnchor.rotation);
            }
        }

        /// <summary>
        /// 경로 전체를 한 장에 담는다. 디오라마 카메라는 트럭을 축으로 돌기 때문에 이만큼 물러날 수
        /// 없으므로, 캡처 동안만 카메라 자세를 직접 잡는다. 에디터에서는 LateUpdate 가 돌지 않아
        /// DioramaCamera 가 자세를 되돌리지 않는다.
        /// </summary>
        private static void CaptureRouteFraming(string label, float pitch, float yaw)
        {
            RoutePath route = Object.FindFirstObjectByType<RoutePath>();
            if (route == null)
            {
                Debug.LogError("[CargoStack] 씬에 RoutePath 가 없다");
                return;
            }

            var bounds = new Bounds(route.SampleAt(0), Vector3.zero);
            for (int i = 1; i < route.SampleCount; i++)
            {
                bounds.Encapsulate(route.SampleAt(i));
            }

            GameObject holder = GameObject.Find("Diorama Camera");
            Camera camera = holder.GetComponent<Camera>();

            // 가로로 가장 긴 축이 화면에 들어가는 거리. 여유 15% 를 둔다.
            float horizontalHalfFov = Mathf.Atan(
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Width / Height);
            float distance = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.15f / Mathf.Tan(horizontalHalfFov);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            holder.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);

            CaptureCamera("Diorama Camera", $"{OutputFolder}/{label}.png");
        }

        private static void CaptureDiorama(string label, float yaw, float pitch, float distance)
        {
            DioramaCamera rig = Object.FindFirstObjectByType<DioramaCamera>();
            rig.SetFraming(yaw, pitch, distance);
            CaptureCamera("Diorama Camera", $"{OutputFolder}/diorama-{label}.png");
        }

        private static void CaptureCamera(string cameraName, string path)
        {
            GameObject holder = GameObject.Find(cameraName);
            if (holder == null)
            {
                Debug.LogError($"[CargoStack] 카메라를 찾지 못했다: {cameraName}");
                return;
            }

            Camera camera = holder.GetComponent<Camera>();
            var renderTexture = new RenderTexture(Width, Height, 24);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            bool previousEnabled = camera.enabled;

            try
            {
                camera.enabled = true;
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                image.Apply();

                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.enabled = previousEnabled;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(renderTexture);
            }
        }
    }
}
