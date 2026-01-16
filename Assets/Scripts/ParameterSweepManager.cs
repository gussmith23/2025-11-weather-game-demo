using UnityEngine;

/// <summary>
/// Spawns a grid of Weather2D instances so multiple parameterizations can run side by side.
/// Each instance gets its own buffers and display quad/RawImage.
/// </summary>
public class ParameterSweepManager : MonoBehaviour
{
    [Tooltip("Template Weather2D to clone. If null, the first Weather2D in the scene is used.")]
    public Weather2D template;
    [Tooltip("If false, only a single Weather2D instance is shown and no sweep clones are created.")]
    public bool enableSweep = false;
    [Tooltip("Number of columns in the comparison grid.")]
    public int columns = 2;
    [Tooltip("Number of rows in the comparison grid.")]
    public int rows = 2;
    [Tooltip("World-space spacing between instances.")]
    public Vector2 spacing = new Vector2(0f, 0f);
    [Tooltip("If true, keep the template as the first cell; otherwise, only clones are shown.")]
    public bool includeTemplate = true;

    private void Start()
    {
        if (template == null)
        {
            template = FindFirstObjectByType<Weather2D>();
        }

        if (template == null)
        {
            Debug.LogWarning("ParameterSweepManager could not find a Weather2D template.");
            return;
        }

        if (!enableSweep)
        {
            template.transform.SetParent(transform, worldPositionStays: true);
            template.transform.position = transform.position;
            return;
        }

        Vector3 origin = transform.position;
        int spawned = 0;
        for (int r = 0; r < Mathf.Max(1, rows); r++)
        {
            for (int c = 0; c < Mathf.Max(1, columns); c++)
            {
                bool isFirstCell = r == 0 && c == 0;
                Weather2D instance;
                if (isFirstCell && includeTemplate)
                {
                    instance = template;
                }
                else
                {
                    GameObject clone = Instantiate(template.gameObject, template.transform.parent);
                    instance = clone.GetComponent<Weather2D>();
                    instance.name = $"{template.name}_Variant_{++spawned}";
                }

                Vector3 offset = new Vector3(c * spacing.x, -r * spacing.y, 0f);
                instance.transform.SetParent(transform, worldPositionStays: true);
                instance.transform.position = origin + offset;
            }
        }
    }
}
