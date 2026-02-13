using UnityEngine;

// Creates a minimal play-mode setup for previewing the CloudPrototype shader.
public class CloudPrototypeSceneBootstrap : MonoBehaviour
{
  public CloudPrototypeController controller;
  public Camera targetCamera;

  private const string QuadName = "CloudPrototypeQuad";

  private void Awake()
  {
    EnsureSetup();
  }

  private void Reset()
  {
    EnsureSetup();
  }

  private void EnsureSetup()
  {
    if (targetCamera == null)
    {
      targetCamera = Camera.main;
      if (targetCamera == null)
      {
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        targetCamera = camGo.AddComponent<Camera>();
        targetCamera.orthographic = true;
        targetCamera.orthographicSize = 5f;
        targetCamera.transform.position = new Vector3(0f, 0f, -10f);
      }
    }

    if (controller == null)
    {
      controller = GetComponent<CloudPrototypeController>();
      if (controller == null)
      {
        controller = gameObject.AddComponent<CloudPrototypeController>();
      }
    }

    Transform quadTransform = transform.Find(QuadName);
    GameObject quadGo;
    if (quadTransform == null)
    {
      quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
      quadGo.name = QuadName;
      quadGo.transform.SetParent(transform, false);
      quadGo.transform.localPosition = new Vector3(0f, 0f, 5f);
      quadGo.transform.localScale = new Vector3(20f, 10f, 1f);

      Collider collider = quadGo.GetComponent<Collider>();
      if (collider != null)
      {
        if (Application.isPlaying)
          Destroy(collider);
        else
          DestroyImmediate(collider);
      }
    }
    else
    {
      quadGo = quadTransform.gameObject;
    }

    var renderer = quadGo.GetComponent<Renderer>();
    controller.targetRenderer = renderer;
    controller.BindTargetRenderer();
  }
}
