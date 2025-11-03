using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeanStudio
{
    public class CameraController : MonoBehaviour
    {
        // Camera controller to rotate, zoom and pan camera.

        public static CameraController Instance;

        [SerializeField] Camera cam;
        private Vector3 previousLeftMousePosition;
        private Vector3 previousRightMousePosition;

        float zoom = -250f;

        void Awake()
        {
            Instance = this;
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
                previousLeftMousePosition = cam.ScreenToViewportPoint(Input.mousePosition);

            zoom += (Input.mouseScrollDelta.y * (Input.GetKey(KeyCode.LeftShift) ? 100f : 1f));
            zoom = Mathf.Clamp(zoom, -1000f, -10f);

            Vector3 leftMouseDirection = new Vector3();
            if (Input.GetMouseButton(0))
            {
                leftMouseDirection = previousLeftMousePosition - cam.ScreenToViewportPoint(Input.mousePosition);
                previousLeftMousePosition = cam.ScreenToViewportPoint(Input.mousePosition);
            }

            cam.transform.localPosition = new Vector3();
            cam.transform.Rotate(new Vector3(1, 0, 0), leftMouseDirection.y * 180);
            cam.transform.Rotate(new Vector3(0, 1, 0), -leftMouseDirection.x * 180, Space.World);
            cam.transform.Translate(new Vector3(0, 0, zoom));

            if (Input.GetMouseButtonDown(1))
                previousRightMousePosition = cam.ScreenToViewportPoint(Input.mousePosition);

            Vector3 rightMouseDirection = new Vector3();
            if (Input.GetMouseButton(1))
            {
                rightMouseDirection = previousRightMousePosition - cam.ScreenToViewportPoint(Input.mousePosition);
                previousRightMousePosition = cam.ScreenToViewportPoint(Input.mousePosition);

                transform.Translate(Quaternion.Euler(0f, cam.transform.localEulerAngles.y, 0f) * (new Vector3(rightMouseDirection.x, 0, rightMouseDirection.y)) * 100f);
            }
        }
    }
}
