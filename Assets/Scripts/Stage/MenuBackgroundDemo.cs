using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoStack
{
    [DisallowMultipleComponent]
    public sealed class MenuBackgroundDemo : MonoBehaviour
    {
        private const int CargoCount = 4;
        private static readonly Vector2[] CargoSpots =
        {
            new(-0.5f, -0.5f),
            new(0.5f, -0.5f),
            new(-0.5f, 0.5f),
            new(0.5f, 0.5f),
        };

        [SerializeField] private TruckMover truck;
        [SerializeField] private Transform bedAnchor;
        [SerializeField] private Cargo[] cargo = Array.Empty<Cargo>();
        [SerializeField] private DioramaCamera cameraRig;
        [SerializeField] private TruckTailgate tailgate;

        private Rigidbody[] displayedCargo = Array.Empty<Rigidbody>();
        private Coroutine pendingReload;

        public int DisplayedCargoCount => displayedCargo.Length;
        public bool IsDriving { get; private set; }

        public void Configure(
            TruckMover truckMover,
            Transform cargoBed,
            Cargo[] cargoItems,
            DioramaCamera rig,
            TruckTailgate truckTailgate)
        {
            truck = truckMover;
            bedAnchor = cargoBed;
            cargo = cargoItems;
            cameraRig = rig;
            tailgate = truckTailgate;
        }

        private void OnEnable()
        {
            truck.Arrived += HandleArrived;
        }

        private void OnDisable()
        {
            truck.Arrived -= HandleArrived;
        }

        private void Awake()
        {
            truck.SnapToStart();
            cameraRig.SetFraming(35f, 28f, 17f);
            tailgate.SetOpenInstantly(false);
            tailgate.CloseForDriving();

            Array.Sort(cargo, (left, right) =>
                string.CompareOrdinal(left.name, right.name));

            int count = Mathf.Min(CargoCount, cargo.Length);
            displayedCargo = new Rigidbody[count];
            for (int index = 0; index < cargo.Length; index++)
            {
                bool displayed = index < count;
                cargo[index].gameObject.SetActive(displayed);
                if (!displayed)
                {
                    continue;
                }

                Rigidbody body = cargo[index].GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.useGravity = false;
                cargo[index].transform.SetPositionAndRotation(
                    bedAnchor.TransformPoint(new Vector3(
                        CargoSpots[index].x,
                        3.2f + index * 0.25f,
                        CargoSpots[index].y)),
                    truck.transform.rotation * Quaternion.Euler(0f, index * 11f, 0f));
                displayedCargo[index] = body;
            }
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(0.5f);
            foreach (Rigidbody body in displayedCargo)
            {
                body.isKinematic = false;
                body.useGravity = true;
                yield return new WaitForSeconds(0.55f);
            }

            yield return new WaitForSeconds(1.5f);
            truck.EnableAutopilotForTesting();
            truck.BeginDrive();
            IsDriving = true;
        }

        private void HandleArrived()
        {
            pendingReload ??= StartCoroutine(Reload());
        }

        private static IEnumerator Reload()
        {
            yield return new WaitForSeconds(2f);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
