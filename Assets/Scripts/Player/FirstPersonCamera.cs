using UnityEngine;

namespace CargoStack
{
    /// <summary>
    /// 적재 단계의 1인칭 시점. 마우스로 둘러보고 화면 중앙 조준점으로 화물을 겨눈다.
    /// 원본: nan2026-cargo(NAN 2026 사전과제)의 Assets/Spike/Runtime/FirstPersonCamera.cs.
    /// 네임스페이스만 바꿔 그대로 가져왔다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform viewAnchor;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float minimumPitch = -80f;
        [SerializeField] private float maximumPitch = 80f;
        [SerializeField] private bool drawReticle = true;

        private float yawOffset;
        private float pitch;

        public Transform ViewAnchor => viewAnchor;
        public Camera ViewCamera => GetComponent<Camera>();

        public void Configure(Transform anchor, bool resetLook)
        {
            viewAnchor = anchor;
            if (resetLook)
            {
                yawOffset = 0f;
                pitch = 0f;
            }

            SnapToAnchor();
        }

        public void SetLookAngles(float yaw, float pitchAngle)
        {
            yawOffset = yaw;
            pitch = Mathf.Clamp(pitchAngle, minimumPitch, maximumPitch);
            SnapToAnchor();
        }

        public void SnapToAnchor()
        {
            if (viewAnchor == null)
            {
                return;
            }

            transform.position = viewAnchor.position;
            transform.rotation = viewAnchor.rotation * Quaternion.Euler(pitch, yawOffset, 0f);
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                LockCursor();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor();
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            yawOffset += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, minimumPitch, maximumPitch);
        }

        private void LateUpdate()
        {
            SnapToAnchor();
        }

        private void OnGUI()
        {
            if (!drawReticle || !Application.isPlaying)
            {
                return;
            }

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(centerX - 3f, centerY - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(centerX - 1f, centerY - 1f, 2f, 2f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
