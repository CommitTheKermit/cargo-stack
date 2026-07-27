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

            Debug.Log($"[CargoStack] 프리뷰 저장 완료: {OutputFolder}");
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
